using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace GeoAnim.Figures;

/// <summary>A figure: an SVG picture (everything as paths) and its size in points (pt).</summary>
public sealed record FigureImage(string Svg, double Width, double Height);

/// <summary>
/// Draws the picture of an animation at a moment as SVG, as the player draws it – but in a fixed size: a coordinate unit
/// is <see cref="FigureStyle.PointsPerUnit"/> points, the sizes of the scripts (texts, lines, points) are multiplied by
/// <see cref="FigureStyle.SizeFactor"/>. Texts are outlines of the STIX Two Text glyphs, formulas the paths MathJax made:
/// the picture needs no font. Cut to its content (as in the player's book view): what is drawn at the given times (labels
/// too, not rays and whole lines), with a margin of 5 % of the content, at least 16 sizes.
/// </summary>
public static partial class FigureRenderer
{
    /// <summary>
    /// The picture at <paramref name="time"/> (ms). With <paramref name="cropTimes"/> it shows only what is drawn at those
    /// times (all pictures of a row of stills alike), else the whole view. Nothing reaches out of the script's view: what
    /// lies beyond it is cut off, as in the player – texts too (in the book they are larger next to the geometry than in the
    /// player, so a text near the border may need a larger view).
    /// </summary>
    public static FigureImage Render(FigureAnimation animation, double time, FigureStyle style, IReadOnlyList<double>? cropTimes = null)
    {
        var full = new View(animation.XMin, animation.XMax, animation.YMin, animation.YMax);
        View view = full;
        double scale = style.PointsPerUnit;
        double least = 16 * style.SizeFactor / scale;
        if (cropTimes is { Count: > 0 })
        {
            SKRect? content = null;
            foreach (double t in cropTimes)
            {
                var measure = new Scene(animation, style, full, full);
                measure.Draw(t);
                if (measure.Content is { } box)
                {
                    content = content is { } known ? SKRect.Union(known, box) : box;
                }
            }
            if (content is { } c)
            {
                double xmin = full.XMin + c.Left / scale, xmax = full.XMin + c.Right / scale;
                double ymax = full.YMax - c.Top / scale, ymin = full.YMax - c.Bottom / scale;
                // a margin of 5 % of the content, at least 16 sizes – within the script's view
                double margin = Math.Max(0.05 * Math.Max(xmax - xmin, ymax - ymin), least);
                view = new View(Math.Max(full.XMin, xmin - margin), Math.Min(full.XMax, xmax + margin),
                    Math.Max(full.YMin, ymin - margin), Math.Min(full.YMax, ymax + margin));
            }
        }
        var scene = new Scene(animation, style, view, full);
        scene.Draw(time);
        return scene.Image();
    }

    private sealed record View(double XMin, double XMax, double YMin, double YMax);

    // distance of the lines of a text, in font sizes; the space between two lines of letters
    private const double LineHeight = 1.3;
    private const double LineGap = LineHeight - 1.05;

    private static string N(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    // a text laid out: its markup (the first baseline at y = 0), width, number of lines, how far it reaches above the first
    // baseline and below, and the box of its ink
    private sealed record TextLayout(string Markup, string HaloSource, double Width, int Lines, double Top, double Bottom);

    // a picture of the part "view" of the plane; drawn is only what lies in the script's view ("area")
    private sealed partial class Scene(FigureAnimation animation, FigureStyle style, View view, View scriptView)
    {
        // where the shapes, the grid and the axes are drawn: the script's view, as far as the picture shows it
        private readonly View area = new(Math.Max(view.XMin, scriptView.XMin), Math.Min(view.XMax, scriptView.XMax),
            Math.Max(view.YMin, scriptView.YMin), Math.Min(view.YMax, scriptView.YMax));


        private readonly double scale = style.PointsPerUnit;
        private readonly double unit = style.SizeFactor;
        private readonly StringBuilder items = new();
        private readonly StringBuilder decor = new();
        private readonly Dictionary<int, (double X, double Bottom)> boxes = new();
        private Dictionary<string, (double? Value, int Digits)> numbers = new(StringComparer.Ordinal);
        private Dictionary<int, FigurePrimitive> byId = new();
        private double time;

        /// <summary>The box of what counts for cutting the figure to its content (in points), or null if nothing is drawn.</summary>
        public SKRect? Content { get; private set; }

        private double Width => (view.XMax - view.XMin) * scale;

        private double Height => (view.YMax - view.YMin) * scale;

        private double ToX(double x) => (x - view.XMin) * scale;

        private double ToY(double y) => (view.YMax - y) * scale;

        private double TextSize(double points) => points * unit;

        public FigureImage Image()
        {
            var svg = new StringBuilder();
            svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"").Append(N(Width)).Append("pt\" height=\"").Append(N(Height))
                .Append("pt\" viewBox=\"0 0 ").Append(N(Width)).Append(' ').Append(N(Height)).Append("\">");
            svg.Append("<rect width=\"").Append(N(Width)).Append("\" height=\"").Append(N(Height)).Append("\" fill=\"").Append(animation.Background).Append("\"/>");
            svg.Append("<clipPath id=\"").Append(ClipId).Append("\"><rect x=\"").Append(N(ToX(area.XMin))).Append("\" y=\"").Append(N(ToY(area.YMax)))
                .Append("\" width=\"").Append(N(Math.Max(0, (area.XMax - area.XMin) * scale))).Append("\" height=\"").Append(N(Math.Max(0, (area.YMax - area.YMin) * scale)))
                .Append("\"/></clipPath>");
            svg.Append("<g clip-path=\"url(#").Append(ClipId).Append(")\">").Append(decor).Append(items).Append("</g></svg>");
            return new FigureImage(svg.ToString(), Width, Height);
        }

        // the name of the cut: one of its own in each picture (pictures may stand together in one page)
        private string ClipId
        {
            get
            {
                // the same for the same picture (FNV-1a over the numbers): the files of an export stay the same
                uint hash = 2166136261;
                foreach (char c in string.Join(",", N(view.XMin), N(view.XMax), N(view.YMin), N(view.YMax), N(area.XMin), N(area.XMax), N(area.YMin), N(area.YMax)))
                {
                    hash = (hash ^ c) * 16777619;
                }
                return "area-" + hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }

        // counts a box (in points) for the content, if the primitive is not unbounded: as far as it lies in the area
        private void Count(SKRect box, bool counts)
        {
            if (!counts || box.Width < 0 || box.Height < 0)
            {
                return;
            }
            var inside = new SKRect((float)ToX(area.XMin), (float)ToY(area.YMax), (float)ToX(area.XMax), (float)ToY(area.YMin));
            if (!box.IntersectsWithInclusive(inside))
            {
                return;
            }
            box.Intersect(inside);
            Content = Content is { } known ? SKRect.Union(known, box) : box;
        }

        private void CountPath(string d, double strokeWidth, bool counts)
        {
            if (!counts || d.Length == 0)
            {
                return;
            }
            using SKPath? path = SKPath.ParseSvgPathData(d);
            if (path == null || path.IsEmpty)
            {
                return;
            }
            SKRect box = path.TightBounds;
            box.Inflate((float)(strokeWidth / 2), (float)(strokeWidth / 2));
            Count(box, true);
        }

        public void Draw(double at)
        {
            time = at;
            items.Clear();
            decor.Clear();
            boxes.Clear();
            Content = null;
            byId = animation.Primitives.ToDictionary(p => p.Id);
            UpdateNumbers();
            DrawDecor();
            foreach (FigurePrimitive primitive in animation.Primitives.Where(p => p.Type != "number").OrderBy(p => p.Layer).ThenBy(p => p.Id))
            {
                Dictionary<string, object>? values = ValuesOf(primitive, time);
                if (values == null || !(Number(values, "opacity", 1) > 0.001))
                {
                    continue;
                }
                bool counts = !primitive.Unbounded;
                double opacity = Number(values, "opacity", 1);
                items.Append("<g opacity=\"").Append(N(opacity)).Append("\">");
                switch (primitive.Type)
                {
                    case "dot":
                        DrawDot(values, counts);
                        break;
                    case "line":
                        DrawLine(values, counts);
                        break;
                    case "arc":
                        DrawArc(values, counts);
                        break;
                    case "part":
                        DrawPart(values, counts);
                        break;
                    case "disc":
                        DrawDisc(values, counts);
                        break;
                    case "polygon":
                        DrawPolygon(values, counts);
                        break;
                    case "angle":
                        DrawAngle(values, counts);
                        break;
                    case "text":
                        DrawText(primitive, values, counts);
                        break;
                }
                items.Append("</g>");
            }
        }

        // the values of a primitive's tracks at a time; null if one has none yet (the primitive is not there)
        private static Dictionary<string, object>? ValuesOf(FigurePrimitive primitive, double at)
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach ((string name, JsonElement track) in primitive.Tracks)
            {
                object? value = FigureAnimation.ValueAt(track, at);
                if (value == null)
                {
                    return null;
                }
                values[name] = value;
            }
            return values;
        }

        private static double Number(Dictionary<string, object> values, string name, double otherwise = 0) =>
            values.TryGetValue(name, out object? value) && value is double d ? d : otherwise;

        private static string? Text(Dictionary<string, object> values, string name) =>
            values.TryGetValue(name, out object? value) ? value as string : null;

        private static bool Flag(Dictionary<string, object> values, string name) =>
            values.TryGetValue(name, out object? value) && value is true;

        // ------------------------------------------------------------------ numbers in texts

        private void UpdateNumbers()
        {
            numbers = new Dictionary<string, (double?, int)>(StringComparer.Ordinal);
            foreach (FigurePrimitive primitive in animation.Primitives.Where(p => p.Type == "number"))
            {
                string? name = primitive.Tracks.TryGetValue("name", out JsonElement nameTrack) ? FigureAnimation.ValueAt(nameTrack, double.MaxValue) as string : null;
                if (name == null)
                {
                    continue;
                }
                object? valid = primitive.Tracks.TryGetValue("valid", out JsonElement v) ? FigureAnimation.ValueAt(v, time) : null;
                object? value = primitive.Tracks.TryGetValue("value", out JsonElement w) ? FigureAnimation.ValueAt(w, time) : null;
                object? digits = primitive.Tracks.TryGetValue("digits", out JsonElement d) ? FigureAnimation.ValueAt(d, double.MaxValue) : null;
                numbers[name] = (valid is double ok && ok > 0.5 && value is double x ? x : null, digits is double n ? (int)n : 1);
            }
        }

        [GeneratedRegex(@"(?<!\\)\{([A-Za-z_][A-Za-z0-9_]*'*)(?::(\d))?\}")]
        private static partial Regex Placeholder();

        // "{l}" or "{l:2}": the value of the number at this time, with a decimal comma
        private string WithNumbers(string text) => text.Contains('{') ? Placeholder().Replace(text, m =>
        {
            if (!numbers.TryGetValue(m.Groups[1].Value, out var number) || number.Value is not double value)
            {
                return "–";
            }
            int places = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : number.Digits;
            string shown = value.ToString("F" + places, CultureInfo.InvariantCulture);
            if (Regex.IsMatch(shown, @"^-?[0.]+$"))
            {
                shown = shown.Replace("-", "");
            }
            return shown.Replace('.', ',').Replace("-", "−");
        }) : text;

        // ------------------------------------------------------------------ shapes

        private static string CirclePath(double cx, double cy, double r) =>
            $"M{N(cx + r)} {N(cy)}A{N(r)} {N(r)} 0 1 0 {N(cx - r)} {N(cy)}A{N(r)} {N(r)} 0 1 0 {N(cx + r)} {N(cy)}Z";

        private void DrawDot(Dictionary<string, object> v, bool counts)
        {
            double r = Math.Max(0, Number(v, "r")) * unit;
            if (r <= 0)
            {
                return;
            }
            string d = CirclePath(ToX(Number(v, "x")), ToY(Number(v, "y")), r);
            items.Append("<path d=\"").Append(d).Append("\" fill=\"").Append(Text(v, "color")).Append("\"/>");
            CountPath(d, 0, counts);
        }

        // an arrowhead with its tip at (x, y), pointing in the direction (dx, dy)
        private void ArrowHead(double x, double y, double dx, double dy, double length, string? color, bool counts)
        {
            double half = 0.42 * length;
            double bx = x - dx * length, by = y - dy * length;
            string d = $"M{N(x)} {N(y)}L{N(bx - dy * half)} {N(by + dx * half)}L{N(bx + dy * half)} {N(by - dx * half)}Z";
            items.Append("<path d=\"").Append(d).Append("\" fill=\"").Append(color).Append("\"/>");
            CountPath(d, 0, counts);
        }

        private void DrawLine(Dictionary<string, object> v, bool counts)
        {
            double x1 = ToX(Number(v, "x1")), y1 = ToY(Number(v, "y1")), x2 = ToX(Number(v, "x2")), y2 = ToY(Number(v, "y2"));
            double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            if (length < 1e-6)
            {
                return;
            }
            string? arrow = Text(v, "arrow");
            bool atEnd = arrow is "end" or "both", atStart = arrow is "start" or "both";
            double width = Number(v, "width");
            double head = Math.Min((4 * width + 6) * unit, length / (atEnd && atStart ? 2 : 1));
            double dx = (x2 - x1) / length, dy = (y2 - y1) / length;
            string? color = Text(v, "color");
            if (atEnd)
            {
                ArrowHead(x2, y2, dx, dy, head, color, counts);
                x2 -= dx * head * 0.8;
                y2 -= dy * head * 0.8;
            }
            if (atStart)
            {
                ArrowHead(x1, y1, -dx, -dy, head, color, counts);
                x1 += dx * head * 0.8;
                y1 += dy * head * 0.8;
            }
            string d = $"M{N(x1)} {N(y1)}L{N(x2)} {N(y2)}";
            items.Append("<path d=\"").Append(d).Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(N(width * unit))
                .Append("\" stroke-linecap=\"round\"/>");
            CountPath(d, width * unit, counts);
        }

        private string ArcPath(double cx, double cy, double r, double start, double sweep)
        {
            if (!(r > 0) || !(sweep > 0.01))
            {
                return "";
            }
            double x = ToX(cx), y = ToY(cy), radius = r * scale;
            if (sweep >= 359.99)
            {
                return $"M{N(x + radius)} {N(y)}A{N(radius)} {N(radius)} 0 1 0 {N(x - radius)} {N(y)}A{N(radius)} {N(radius)} 0 1 0 {N(x + radius)} {N(y)}";
            }
            double a0 = start * Math.PI / 180, a1 = (start + sweep) * Math.PI / 180;
            return $"M{N(x + radius * Math.Cos(a0))} {N(y - radius * Math.Sin(a0))}A{N(radius)} {N(radius)} 0 {(sweep > 180 ? 1 : 0)} 0 {N(x + radius * Math.Cos(a1))} {N(y - radius * Math.Sin(a1))}";
        }

        private void DrawArc(Dictionary<string, object> v, bool counts)
        {
            string d = ArcPath(Number(v, "cx"), Number(v, "cy"), Number(v, "r"), Number(v, "start"), Number(v, "sweep"));
            if (d.Length == 0)
            {
                return;
            }
            double width = Number(v, "width") * unit;
            items.Append("<path d=\"").Append(d).Append("\" fill=\"none\" stroke=\"").Append(Text(v, "color")).Append("\" stroke-width=\"").Append(N(width))
                .Append("\" stroke-linecap=\"round\"/>");
            CountPath(d, width, counts);
        }

        // an arc or the border of a sector, with arrowheads and a label (an arc's outside its middle, a sector's inside)
        private void DrawPart(Dictionary<string, object> v, bool counts)
        {
            double cx = ToX(Number(v, "cx")), cy = ToY(Number(v, "cy")), r = Number(v, "r") * scale;
            double sweep = Math.Max(0, Math.Min(Number(v, "sweep"), 360)), start = Number(v, "start");
            string? arrow = Text(v, "arrow");
            bool sector = Flag(v, "sector");
            bool atEnd = arrow is "end" or "both" && sweep > 0.01, atStart = arrow is "start" or "both" && sweep > 0.01;
            double width = Number(v, "width");
            double head = Math.Min((4 * width + 6) * unit, r * sweep * Math.PI / 180 / (atEnd && atStart ? 2 : 1));
            double cut = r > 0 ? head * 0.8 / r * 180 / Math.PI : 0;
            double from = start + (atStart ? cut : 0), to = start + sweep - (atEnd ? cut : 0);
            string? color = Text(v, "color");
            if (r > 0 && sweep > 0.01)
            {
                string arc = sweep >= 359.99 && !sector
                    ? ArcPath(Number(v, "cx"), Number(v, "cy"), Number(v, "r"), 0, 360)
                    : ArcPath(Number(v, "cx"), Number(v, "cy"), Number(v, "r"), from, Math.Max(0, to - from));
                string d = sector && arc.Length > 0 ? $"M{N(cx)} {N(cy)}L{arc[1..]}Z" : arc;
                if (d.Length > 0)
                {
                    items.Append("<path d=\"").Append(d).Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(N(width * unit))
                        .Append("\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                    CountPath(d, width * unit, counts);
                }
            }
            (double X, double Y) Point(double degrees) => (cx + r * Math.Cos(degrees * Math.PI / 180), cy - r * Math.Sin(degrees * Math.PI / 180));
            double endAngle = (start + sweep) * Math.PI / 180, startAngle = start * Math.PI / 180;
            if (atEnd)
            {
                (double ex, double ey) = Point(start + sweep);
                ArrowHead(ex, ey, -Math.Sin(endAngle), -Math.Cos(endAngle), head, color, counts);
            }
            if (atStart)
            {
                (double sx, double sy) = Point(start);
                ArrowHead(sx, sy, Math.Sin(startAngle), Math.Cos(startAngle), head, color, counts);
            }
            if (Text(v, "text") is { Length: > 0 } label && sweep > 0.01)
            {
                double size = TextSize(Number(v, "size", 24));
                TextLayout layout = Layout(WithNumbers(label), size, "middle", color ?? "#000");
                double height = 0.7 * size + (layout.Lines - 1) * LineHeight * size;
                double bisector = (start + sweep / 2) * Math.PI / 180;
                double bx = Math.Cos(bisector), by = Math.Sin(bisector);
                double along = Math.Abs(bx) * layout.Width / 2 + Math.Abs(by) * height / 2;
                double distance = sector ? Math.Max(0.3 * r, Math.Min(0.6 * r, r - along - 6 * unit)) : r + 8 * unit + along;
                double x = cx + bx * distance + Number(v, "sx") * scale - layout.Width / 2;
                double y = cy - by * distance - Number(v, "sy") * scale - height / 2 + 0.7 * size;
                double labelOpacity = v.ContainsKey("labelOpacity") ? Number(v, "labelOpacity") : 1;
                PlaceText(layout, x, y, labelOpacity, Flag(v, "halo") ? animation.Background : null, size, counts);
            }
        }

        private void DrawDisc(Dictionary<string, object> v, bool counts)
        {
            double cx = ToX(Number(v, "cx")), cy = ToY(Number(v, "cy")), r = Number(v, "r") * scale, sweep = Number(v, "sweep");
            string d = "";
            if (r > 0 && sweep >= 359.99)
            {
                d = CirclePath(cx, cy, r);
            }
            else if (r > 0 && sweep > 0.01)
            {
                string arc = ArcPath(Number(v, "cx"), Number(v, "cy"), Number(v, "r"), Number(v, "start"), sweep);
                d = $"M{N(cx)} {N(cy)}L{arc[1..]}Z";
            }
            if (d.Length == 0)
            {
                return;
            }
            items.Append("<path d=\"").Append(d).Append("\" fill=\"").Append(Text(v, "fill")).Append("\" fill-opacity=\"").Append(N(Number(v, "fillOpacity", 1))).Append("\"/>");
            CountPath(d, 0, counts);
        }

        private void DrawPolygon(Dictionary<string, object> v, bool counts)
        {
            if (!v.TryGetValue("points", out object? value) || value is not double[] points || points.Length < 4)
            {
                return;
            }
            var d = new StringBuilder();
            for (int i = 0; i + 1 < points.Length; i += 2)
            {
                d.Append(i == 0 ? 'M' : 'L').Append(N(ToX(points[i]))).Append(' ').Append(N(ToY(points[i + 1])));
            }
            d.Append('Z');
            double width = Number(v, "width") * unit;
            items.Append("<path d=\"").Append(d).Append("\" fill=\"").Append(Text(v, "fill") ?? "none").Append("\" fill-opacity=\"").Append(N(Number(v, "fillOpacity", 0)))
                .Append("\" stroke=\"").Append(Text(v, "color")).Append("\" stroke-width=\"").Append(N(width)).Append("\" stroke-linejoin=\"round\"/>");
            CountPath(d.ToString(), width, counts);
        }

        // ------------------------------------------------------------------ angles

        private static (double X, double Y) Polar(double cx, double cy, double a, double r) =>
            (cx + r * Math.Cos(a * Math.PI / 180), cy - r * Math.Sin(a * Math.PI / 180));

        // the colour of a fill with its opacity over the background
        private string FillOver(string fill, double opacity)
        {
            int[] b = FigureAnimation.Rgb(animation.Background);
            int[] f = FigureAnimation.Rgb(fill);
            int Mix(int x, int y) => (int)Math.Round(x + (y - x) * opacity);
            return $"rgb({Mix(b[0], f[0])},{Mix(b[1], f[1])},{Mix(b[2], f[2])})";
        }

        private void DrawAngle(Dictionary<string, object> v, bool counts)
        {
            double reveal = v.ContainsKey("reveal") ? Math.Max(0, Math.Min(1, Number(v, "reveal"))) : 1;
            double cx = ToX(Number(v, "x")), cy = ToY(Number(v, "y"));
            double size = TextSize(Number(v, "size", 24));
            double start = Number(v, "start"), sweepFull = Number(v, "sweep"), width = Number(v, "width"), baseSize = Number(v, "base", 20);
            string? color = Text(v, "color");
            string? label = Text(v, "text") is { Length: > 0 } t ? WithNumbers(t) : null;
            TextLayout? layout = label != null ? Layout(label, size, "middle", color ?? "#000") : null;
            double textWidth = layout?.Width ?? 0;
            double textHeight = layout != null ? 0.7 * size + (layout.Lines - 1) * LineHeight * size : 0;
            double bisector = (start + sweepFull / 2) * Math.PI / 180;
            double bx = Math.Cos(bisector), by = Math.Sin(bisector);
            double along = Math.Abs(bx) * textWidth / 2 + Math.Abs(by) * textHeight / 2;
            double across = Math.Abs(by) * textWidth / 2 + Math.Abs(bx) * textHeight / 2;
            double pad = 6 * unit;
            double half = Math.Min(sweepFull, 359.9) / 2 * Math.PI / 180;
            double distance = label != null ? (half < Math.PI / 2 ? Math.Max((across + pad) / Math.Sin(half), along + pad) : along + pad) : 0;
            if (label != null)
            {
                distance = Math.Max(distance, 1.25 * size + along);
            }
            distance = Math.Min(distance, 8 * baseSize * unit);
            double radius;
            if (Number(v, "radius") > 0)
            {
                radius = Number(v, "radius") * scale;
                distance = Math.Max(radius * 0.55, Math.Min(distance, radius - along - pad));
            }
            else
            {
                radius = Math.Max(baseSize * unit, label != null ? distance + along + pad : 0);
            }
            double sweep = sweepFull * reveal;
            string fillPath = "", arcPath = "";
            if (sweep >= 359.99)
            {
                fillPath = arcPath = CirclePath(cx, cy, radius);
            }
            else if (sweep >= 0.01)
            {
                (double x0, double y0) = Polar(cx, cy, start, radius);
                (double x1, double y1) = Polar(cx, cy, start + sweep, radius);
                string arc = $"A{N(radius)} {N(radius)} 0 {(sweep > 180 ? 1 : 0)} 0 {N(x1)} {N(y1)}";
                fillPath = $"M{N(cx)} {N(cy)}L{N(x0)} {N(y0)}{arc}Z";
                arcPath = $"M{N(x0)} {N(y0)}{arc}";
            }
            string? arrow = Text(v, "arrow");
            bool atEnd = arrow is "end" or "both" && sweep >= 0.01, atStart = arrow is "start" or "both" && sweep >= 0.01;
            double head = Math.Min((4 * width + 6) * unit, radius * Math.Min(sweep, 359.99) * Math.PI / 180 / (atEnd && atStart ? 2 : 1));
            double cut = radius > 0 ? head / radius * 180 / Math.PI : 0;
            if ((atEnd || atStart) && sweep < 359.99)
            {
                double a0 = start + (atStart ? 0.8 * cut : 0), a1 = start + sweep - (atEnd ? 0.8 * cut : 0);
                (double x0, double y0) = Polar(cx, cy, a0, radius);
                (double x1, double y1) = Polar(cx, cy, a1, radius);
                arcPath = a1 > a0 ? $"M{N(x0)} {N(y0)}A{N(radius)} {N(radius)} 0 {(a1 - a0 > 180 ? 1 : 0)} 0 {N(x1)} {N(y1)}" : "";
            }
            string fill = Text(v, "fill") ?? "none";
            double fillOpacity = Number(v, "fillOpacity", 0);
            if (fillPath.Length > 0)
            {
                items.Append("<path d=\"").Append(fillPath).Append("\" fill=\"").Append(fill).Append("\" fill-opacity=\"").Append(N(fillOpacity)).Append("\"/>");
                CountPath(fillPath, 0, counts);
            }
            if (arcPath.Length > 0)
            {
                items.Append("<path d=\"").Append(arcPath).Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(N(width * unit))
                    .Append("\" stroke-linecap=\"round\"/>");
            }
            void ArcHead(bool wanted, double tipAngle, double fromAngle)
            {
                if (!wanted)
                {
                    return;
                }
                (double tx, double ty) = Polar(cx, cy, tipAngle, radius);
                (double fx, double fy) = Polar(cx, cy, fromAngle, radius);
                double length = Math.Sqrt((tx - fx) * (tx - fx) + (ty - fy) * (ty - fy));
                length = length > 0 ? length : 1;
                ArrowHead(tx, ty, (tx - fx) / length, (ty - fy) / length, head, color, counts);
            }
            ArcHead(atEnd, start + sweep, start + sweep - cut);
            ArcHead(atStart, start, start + cut);
            // a right angle: a dot – unless it has a label, which stands there instead
            bool right = Math.Abs(sweepFull - 90) < 0.05;
            double labelShown = label != null ? (v.ContainsKey("labelOpacity") ? Number(v, "labelOpacity") : 1) : 0;
            if (right && labelShown < 1)
            {
                (double dx, double dy) = Polar(cx, cy, start + sweepFull / 2, radius * 0.45);
                double r = Math.Max(1.5, 1.4 * width) * unit;
                items.Append("<path d=\"").Append(CirclePath(dx, dy, r)).Append("\" fill=\"").Append(color).Append("\" opacity=\"").Append(N(reveal * (1 - labelShown))).Append("\"/>");
            }
            if (layout != null)
            {
                double x = cx + bx * distance - textWidth / 2 + Number(v, "ox") * scale;
                double y = cy - by * distance - textHeight / 2 + 0.7 * size - Number(v, "oy") * scale;
                PlaceText(layout, x, y, reveal * labelShown, Flag(v, "halo") ? FillOver(fill, fillOpacity) : null, size, counts);
            }
        }

        // ------------------------------------------------------------------ texts

        private void DrawText(FigurePrimitive primitive, Dictionary<string, object> v, bool counts)
        {
            double size = TextSize(Number(v, "size", 26));
            bool alongNormal = v.ContainsKey("nx") && Number(v, "dir", 1) != 0;
            string align = alongNormal ? "middle" : Text(v, "anchor") ?? "start";
            string? color = Text(v, "color");
            TextLayout layout = Layout(WithNumbers(Text(v, "text") ?? ""), size, align, color ?? "#000");
            double x, y;
            if (v.ContainsKey("below"))
            {
                // below another text: under its bottom with the space between the lines of a text (plus the gap)
                (double X, double Bottom) above = BoxOf((int)Number(v, "below"));
                x = above.X + Number(v, "sx") * scale;
                y = above.Bottom + LineGap * size + Number(v, "belowGap") * scale - layout.Top - Number(v, "sy") * scale;
            }
            else
            {
                x = ToX(Number(v, "x")) + Number(v, "dx") * unit + Number(v, "sx") * scale;
                y = ToY(Number(v, "y")) - Number(v, "dy") * unit - Number(v, "sy") * scale;
            }
            boxes[primitive.Id] = (x, y + layout.Bottom);
            if (alongNormal)
            {
                // the label of a line or a point: moved along the normal until it is "gap" sizes away
                double nx = Number(v, "nx"), ny = Number(v, "ny");
                double height = 0.7 * size + (layout.Lines - 1) * LineHeight * size;
                double reach = Math.Abs(nx) * layout.Width / 2 + Math.Abs(ny) * height / 2 + Number(v, "gap") * unit;
                x += nx * reach - layout.Width / 2;
                y += -ny * reach - height / 2 + 0.7 * size;
            }
            else if (align == "middle")
            {
                x -= layout.Width / 2;
            }
            else if (align == "end")
            {
                x -= layout.Width;
            }
            PlaceText(layout, x, y, 1, Flag(v, "halo") ? animation.Background : null, size, counts);
        }

        // where a text stands (across) and its bottom, for a text below it: as drawn, else estimated from its lines
        private (double X, double Bottom) BoxOf(int id)
        {
            if (boxes.TryGetValue(id, out var box))
            {
                return box;
            }
            if (!byId.TryGetValue(id, out FigurePrimitive? primitive))
            {
                return (0, 0);
            }
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach ((string name, JsonElement track) in primitive.Tracks)
            {
                double first = track.GetArrayLength() > 0 ? track[0][0].GetDouble() : 0;
                if (FigureAnimation.ValueAt(track, Math.Max(time, first)) is { } value)
                {
                    values[name] = value;
                }
            }
            double size = TextSize(Number(values, "size", 26));
            int lines = (Text(values, "text") ?? "").Split('\n').Length;
            double bottom = 0.3 * size + (lines - 1) * LineHeight * size;
            if (values.ContainsKey("below"))
            {
                (double X, double Bottom) above = BoxOf((int)Number(values, "below"));
                double top = above.Bottom + LineGap * size + Number(values, "belowGap") * scale + 0.75 * size - Number(values, "sy") * scale;
                return (above.X + Number(values, "sx") * scale, top + bottom);
            }
            double y = ToY(Number(values, "y")) - Number(values, "dy") * unit - Number(values, "sy") * scale;
            return (ToX(Number(values, "x")) + Number(values, "dx") * unit + Number(values, "sx") * scale, y + bottom);
        }

        // a laid-out text at (x, y) (its first baseline), with a halo (a rim in the colour behind it) if wanted
        private void PlaceText(TextLayout layout, double x, double y, double opacity, string? halo, double size, bool counts)
        {
            items.Append("<g transform=\"translate(").Append(N(x)).Append(' ').Append(N(y)).Append(")\"");
            if (opacity < 0.999)
            {
                items.Append(" opacity=\"").Append(N(opacity)).Append('"');
            }
            items.Append('>');
            if (halo != null)
            {
                string rim = layout.HaloSource.Replace("HALO_COLOR", halo, StringComparison.Ordinal)
                    .Replace("HALO_WIDTH", N(0.22 * size), StringComparison.Ordinal);
                items.Append("<g fill=\"").Append(halo).Append("\" stroke=\"").Append(halo).Append("\" stroke-width=\"").Append(N(0.22 * size))
                    .Append("\" stroke-linejoin=\"round\" stroke-linecap=\"round\">").Append(rim).Append("</g>");
            }
            items.Append(layout.Markup).Append("</g>");
            Count(new SKRect((float)x, (float)(y + layout.Top), (float)(x + layout.Width), (float)(y + layout.Bottom)), counts);
        }

        // lays out a text: its lines aligned to the left, centre or right, glyph outlines and formulas; the first baseline
        // at y = 0
        private TextLayout Layout(string text, double size, string align, string color)
        {
            List<List<TextPiece>> lines = FigureText.Lines(text);
            var rows = new List<(StringBuilder Markup, StringBuilder Halo, double Width)>();
            double top = -0.75 * size, bottom = 0.3 * size;
            for (int index = 0; index < lines.Count; index++)
            {
                double baseline = index * LineHeight * size;
                bottom = Math.Max(bottom, baseline + 0.3 * size);
                var markup = new StringBuilder();
                var halo = new StringBuilder();
                double x = 0;
                foreach (TextPiece piece in lines[index])
                {
                    if (piece.IsFormula && animation.Math.TryGetValue(piece.Text, out FigureFormula? formula))
                    {
                        double s = size / 1000;
                        if (index == 0)
                        {
                            top = Math.Min(top, formula.Y * s);
                        }
                        bottom = Math.Max(bottom, baseline + (formula.Y + formula.Height) * s);
                        string transform = $"translate({N(x - formula.X * s)} 0) scale({N(s)})";
                        markup.Append("<g transform=\"").Append(transform).Append("\">")
                            .Append(formula.Svg.Replace("currentColor", color, StringComparison.Ordinal)).Append("</g>");
                        // the rim: the formula's strokes in the halo's colour and width (in its own, scaled coordinates)
                        halo.Append("<g transform=\"").Append(transform).Append("\">")
                            .Append(formula.Svg.Replace("currentColor", "HALO_COLOR", StringComparison.Ordinal)
                                .Replace("stroke-width=\"0\"", "stroke-width=\"" + N(220) + "\"", StringComparison.Ordinal)).Append("</g>");
                        x += formula.Width * s;
                    }
                    else
                    {
                        string content = piece.Text;
                        string d = FigureFonts.Outline(content, x, 0, size, piece.Bold, piece.Italic);
                        if (d.Length > 0)
                        {
                            markup.Append("<path d=\"").Append(d).Append("\" fill=\"").Append(color).Append("\"/>");
                            halo.Append("<path d=\"").Append(d).Append("\"/>");
                        }
                        x += FigureFonts.Width(content, size, piece.Bold, piece.Italic);
                    }
                }
                rows.Add((markup, halo, x));
            }
            double widest = rows.Count == 0 ? 0 : rows.Max(r => r.Width);
            var all = new StringBuilder();
            var allHalo = new StringBuilder();
            for (int index = 0; index < rows.Count; index++)
            {
                double shift = align == "middle" ? (widest - rows[index].Width) / 2 : align == "end" ? widest - rows[index].Width : 0;
                string transform = $"translate({N(shift)} {N(index * LineHeight * size)})";
                all.Append("<g transform=\"").Append(transform).Append("\">").Append(rows[index].Markup).Append("</g>");
                allHalo.Append("<g transform=\"").Append(transform).Append("\">").Append(rows[index].Halo).Append("</g>");
            }
            return new TextLayout(all.ToString(), allHalo.ToString(), widest, lines.Count, top, bottom);
        }

        // ------------------------------------------------------------------ grid and axes

        private void DrawDecor()
        {
            if (animation.Grid is double step && step > 0)
            {
                decor.Append("<g stroke=\"#e3e6eb\" stroke-width=\"").Append(N(Math.Max(0.25, unit))).Append("\">");
                for (double x = Math.Ceiling(area.XMin / step) * step; x <= area.XMax + 1e-9; x += step)
                {
                    decor.Append("<path d=\"M").Append(N(ToX(x))).Append(' ').Append(N(ToY(area.YMax))).Append('V').Append(N(ToY(area.YMin))).Append("\"/>");
                }
                for (double y = Math.Ceiling(area.YMin / step) * step; y <= area.YMax + 1e-9; y += step)
                {
                    decor.Append("<path d=\"M").Append(N(ToX(area.XMin))).Append(' ').Append(N(ToY(y))).Append('H').Append(N(ToX(area.XMax))).Append("\"/>");
                }
                decor.Append("</g>");
            }
            if (!animation.Axes)
            {
                return;
            }
            string color = animation.AxesColor ?? "#7a808c";
            double size = animation.AxesSize ?? 15;
            double f = size / 15;
            double arrow = 9 * unit;
            decor.Append("<g stroke=\"").Append(color).Append("\" stroke-width=\"").Append(N(1.5 * unit)).Append("\" fill=\"").Append(color).Append("\">");
            var labels = new StringBuilder();
            // labels on every k-th unit, at least 36 sizes apart (more for larger numbers)
            double k = 1;
            while (k * scale < 36 * f * unit)
            {
                k = k == 1 ? 2 : k == 2 ? 5 : k * 2;
            }
            void Label(string text, double x, double y, string anchor)
            {
                double width = FigureFonts.Width(text, size * unit, false, false);
                double left = anchor == "middle" ? x - width / 2 : x - width;
                labels.Append("<path d=\"").Append(FigureFonts.Outline(text, left, y, size * unit, false, false)).Append("\"/>");
            }
            if (area.YMin <= 0 && area.YMax >= 0)
            {
                double y0 = ToY(0), xEnd = ToX(area.XMax);
                decor.Append("<path d=\"M").Append(N(ToX(area.XMin))).Append(' ').Append(N(y0)).Append('H').Append(N(xEnd)).Append("\"/>");
                decor.Append("<path stroke=\"none\" d=\"M").Append(N(xEnd)).Append(' ').Append(N(y0)).Append('L').Append(N(xEnd - arrow)).Append(' ')
                    .Append(N(y0 - arrow / 2)).Append('L').Append(N(xEnd - arrow)).Append(' ').Append(N(y0 + arrow / 2)).Append("Z\"/>");
                for (double x = Math.Ceiling(area.XMin / k) * k; x < area.XMax; x += k)
                {
                    if (Math.Abs(x) < 1e-9 || ToX(x) - ToX(area.XMin) < 12 * f * unit || ToX(x) > ToX(area.XMax) - 24 * f * unit)
                    {
                        continue;
                    }
                    decor.Append("<path d=\"M").Append(N(ToX(x))).Append(' ').Append(N(y0 - 4 * unit)).Append('V').Append(N(y0 + 4 * unit)).Append("\"/>");
                    Label(FormatNumber(x), ToX(x), y0 + 20 * f * unit, "middle");
                }
            }
            if (area.XMin <= 0 && area.XMax >= 0)
            {
                double x0 = ToX(0), yEnd = ToY(area.YMax);
                decor.Append("<path d=\"M").Append(N(x0)).Append(' ').Append(N(ToY(area.YMin))).Append('V').Append(N(yEnd)).Append("\"/>");
                decor.Append("<path stroke=\"none\" d=\"M").Append(N(x0)).Append(' ').Append(N(yEnd)).Append('L').Append(N(x0 - arrow / 2)).Append(' ').Append(N(yEnd + arrow)).Append('L')
                    .Append(N(x0 + arrow / 2)).Append(' ').Append(N(yEnd + arrow)).Append("Z\"/>");
                for (double y = Math.Ceiling(area.YMin / k) * k; y < area.YMax; y += k)
                {
                    if (Math.Abs(y) < 1e-9 || ToY(y) - ToY(area.YMax) < 24 * f * unit || ToY(y) > ToY(area.YMin) - 12 * f * unit)
                    {
                        continue;
                    }
                    decor.Append("<path d=\"M").Append(N(x0 - 4 * unit)).Append(' ').Append(N(ToY(y))).Append('H').Append(N(x0 + 4 * unit)).Append("\"/>");
                    Label(FormatNumber(y), x0 - 8 * f * unit, ToY(y) + 5 * f * unit, "end");
                }
            }
            decor.Append("</g><g fill=\"").Append(color).Append("\">").Append(labels).Append("</g>");
        }

        private static string FormatNumber(double n) =>
            (Math.Round(n * 1000) / 1000).ToString(CultureInfo.InvariantCulture).Replace("-", "−");
    }
}
