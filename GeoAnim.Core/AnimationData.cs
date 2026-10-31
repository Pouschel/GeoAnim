using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GeoAnim.Formulas;

namespace GeoAnim;

/// <summary>
/// A finished animation: what the player needs. <see cref="ToJson"/> gives the format the player reads
/// (described in README.md of the repository).
/// </summary>
public sealed class AnimationData
{
    private readonly IReadOnlyList<Primitive> primitives;

    // factor for the times of the primitives' keys (1 / speed); duration and steps are already scaled
    private readonly double timeScale;

    internal AnimationData(
        string? title, ViewWindow view, Color background, double? gridStep, bool axes, double duration,
        IReadOnlyList<AnimationStep> steps, IReadOnlyList<Primitive> primitives, double timeScale,
        IReadOnlyDictionary<string, Formula> formulas)
    {
        Formulas = formulas;
        this.timeScale = timeScale;
        Title = title;
        View = view;
        Background = background;
        GridStep = gridStep;
        Axes = axes;
        Duration = duration;
        Steps = steps;
        this.primitives = primitives;
    }

    public string? Title { get; }

    /// <summary>The page description given in the script (meta description), or null.</summary>
    public string? Description { get; internal init; }

    /// <summary>The word forms that link to this page from the other pages of "geoanim exportdir" ("keyword").</summary>
    public IReadOnlyList<PageKeyword> Keywords { get; internal init; } = [];

    /// <summary>
    /// For an editor (<see cref="Construction.Inspect"/>): every object over time with its names, written as "inspect";
    /// empty otherwise.
    /// </summary>
    internal IReadOnlyList<InspectedObject> Inspection { get; init; } = [];

    /// <summary>For an editor: primitive id → the names of its object (written as "owners").</summary>
    internal IReadOnlyDictionary<int, IReadOnlyList<string>> Owners { get; init; } = new Dictionary<int, IReadOnlyList<string>>();

    public ViewWindow View { get; }

    public Color Background { get; }

    public double? GridStep { get; }

    public bool Axes { get; }

    /// <summary>The size of the axes' numbers in points (null: the player's 15).</summary>
    public double? AxesSize { get; init; }

    /// <summary>The colour of the axes and their numbers (null: the player's grey).</summary>
    public Color? AxesColor { get; init; }

    /// <summary>The length of a unit in the book's figures in points ("print scale"; null: the book's).</summary>
    public double? PrintScale { get; init; }

    /// <summary>The width of the view in the book's figures in points ("print width").</summary>
    public double? PrintWidth { get; init; }

    /// <summary>Total length in milliseconds.</summary>
    public double Duration { get; }

    public IReadOnlyList<AnimationStep> Steps { get; }

    /// <summary>The formulas of all texts (AsciiMath → SVG).</summary>
    public IReadOnlyDictionary<string, Formula> Formulas { get; }

    internal IReadOnlyList<Primitive> Primitives => primitives;

    /// <summary>
    /// A text with its placeholders ("{l}", "{l:2}") filled in with the numbers' values at the end of the animation – for
    /// texts outside the player (the page's description, the overview).
    /// </summary>
    public string WithFinalNumbers(string text)
    {
        if (!text.Contains('{'))
        {
            return text;
        }
        var values = new Dictionary<string, (double Value, int Digits)>(StringComparer.Ordinal);
        foreach (Primitive number in primitives.Where(p => p.Type == "number" && p.Tracks.ContainsKey("value")))
        {
            values[(string)number.Tracks["name"][0].Value] = ((double)number.Tracks["value"][^1].Value, (int)(double)number.Tracks["digits"][0].Value);
        }
        return NumberText.Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var number)
            ? NumberText.Format(number.Value, m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : number.Digits)
            : m.Value);
    }

    /// <summary>The same animation with other step texts (e.g. with links).</summary>
    public AnimationData WithSteps(IReadOnlyList<AnimationStep> steps) => WithTexts(Title, steps);

    /// <summary>The same animation with another title and other step texts (their formulas must stay the same).</summary>
    public AnimationData WithTexts(string? title, IReadOnlyList<AnimationStep> steps) => WithTexts(title, Description, steps);

    /// <summary>The same animation with another title, description and other step texts (their formulas must stay the same).</summary>
    public AnimationData WithTexts(string? title, string? description, IReadOnlyList<AnimationStep> steps) =>
        new(title, View, Background, GridStep, Axes, Duration, steps, primitives, timeScale, Formulas)
        {
            AxesSize = AxesSize,
            AxesColor = AxesColor,
            PrintScale = PrintScale,
            PrintWidth = PrintWidth,
            Description = description,
            Keywords = Keywords,
            Inspection = Inspection,
            Owners = Owners,
        };

    /// <summary>Whether nothing is drawn (e.g. a file with only defaults and vars).</summary>
    public bool IsEmpty => primitives.Count == 0;

    /// <summary>The animation as JSON for the player.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        // the JSON is also embedded in a script element of the exported page: escape <, >, & (as < …)
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.Default }))
        {
            json.WriteStartObject();
            json.WriteNumber("format", 1);
            if (Title != null)
            {
                json.WriteString("title", Title);
            }
            if (Description != null)
            {
                // shown on the starting picture
                json.WriteString("description", Description);
            }
            json.WriteStartObject("view");
            json.WriteNumber("xmin", View.XMin);
            json.WriteNumber("xmax", View.XMax);
            json.WriteNumber("ymin", View.YMin);
            json.WriteNumber("ymax", View.YMax);
            json.WriteEndObject();
            json.WriteString("background", Background.ToHex());
            if (GridStep != null)
            {
                json.WriteNumber("grid", GridStep.Value);
            }
            json.WriteBoolean("axes", Axes);
            if (AxesSize != null)
            {
                json.WriteNumber("axesSize", AxesSize.Value);
            }
            if (AxesColor != null)
            {
                json.WriteString("axesColor", AxesColor.Value.ToHex());
            }
            if (PrintScale != null)
            {
                json.WriteNumber("printScale", Math.Round(PrintScale.Value, 4));
            }
            if (PrintWidth != null)
            {
                json.WriteNumber("printWidth", Math.Round(PrintWidth.Value, 4));
            }
            json.WriteNumber("duration", RoundTime(Duration));
            json.WriteStartArray("steps");
            foreach (AnimationStep step in Steps)
            {
                json.WriteStartObject();
                json.WriteString("caption", step.Caption);
                if (step.Info != null)
                {
                    json.WriteString("info", step.Info);
                }
                json.WriteNumber("start", RoundTime(step.Start));
                json.WriteNumber("end", RoundTime(step.End));
                if (step.TextOnly)
                {
                    json.WriteBoolean("text", true);
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();
            if (Formulas.Count > 0)
            {
                // "math": { "AsciiMath": { "box": [x, y, width, height], "svg": "…" } }, box in thousandths of an em
                json.WriteStartObject("math");
                foreach ((string source, Formula formula) in Formulas.OrderBy(f => f.Key, StringComparer.Ordinal))
                {
                    json.WriteStartObject(source);
                    json.WriteStartArray("box");
                    json.WriteNumberValue(formula.X);
                    json.WriteNumberValue(formula.Y);
                    json.WriteNumberValue(formula.Width);
                    json.WriteNumberValue(formula.Height);
                    json.WriteEndArray();
                    json.WriteString("svg", formula.Svg);
                    json.WriteEndObject();
                }
                json.WriteEndObject();
            }
            json.WriteStartArray("primitives");
            foreach (Primitive primitive in primitives)
            {
                json.WriteStartObject();
                WritePrimitive(json, primitive);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            if (Owners.Count > 0)
            {
                // "owners": { "12": ["A"], … }
                json.WriteStartObject("owners");
                foreach ((int id, IReadOnlyList<string> names) in Owners)
                {
                    json.WriteStartArray(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    foreach (string name in names)
                    {
                        json.WriteStringValue(name);
                    }
                    json.WriteEndArray();
                }
                json.WriteEndObject();
            }
            if (Inspection.Count > 0)
            {
                // "inspect": [{ "names": ["P"], "id": 1, "type": "dot", "layer": 0, "tracks": { …, "info": […] } }, …]
                json.WriteStartArray("inspect");
                foreach (InspectedObject inspected in Inspection)
                {
                    json.WriteStartObject();
                    json.WriteStartArray("names");
                    foreach (string name in inspected.Names)
                    {
                        json.WriteStringValue(name);
                    }
                    json.WriteEndArray();
                    WritePrimitive(json, inspected.Ghost);
                    if (inspected.Label != null)
                    {
                        json.WriteStartObject("label");
                        WritePrimitive(json, inspected.Label);
                        json.WriteEndObject();
                    }
                    json.WriteEndObject();
                }
                json.WriteEndArray();
            }
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // id, type, layer and tracks of a primitive (inside its object)
    private void WritePrimitive(Utf8JsonWriter json, Primitive primitive)
    {
        json.WriteNumber("id", primitive.Id);
        json.WriteString("type", primitive.Type);
        json.WriteNumber("layer", primitive.Layer);
        if (primitive.Unbounded)
        {
            // a ray or a whole line: not part of what a figure is cut to
            json.WriteBoolean("unbounded", true);
        }
        json.WriteStartObject("tracks");
        foreach (KeyValuePair<string, List<Primitive.Key>> track in primitive.Tracks)
        {
            json.WriteStartArray(track.Key);
            foreach (Primitive.Key key in Simplify(track.Value, ToleranceFor(primitive.Type, track.Key)))
            {
                // [time, value] or [time, value, easing]
                json.WriteStartArray();
                json.WriteNumberValue(RoundTime(key.Time * timeScale));
                WriteValue(json, key.Value);
                if (key.Easing != Easing.Linear)
                {
                    json.WriteStringValue(EasingFunctions.Code(key.Easing));
                }
                json.WriteEndArray();
            }
            json.WriteEndArray();
        }
        json.WriteEndObject();
    }

    private static double RoundTime(double time) => Math.Round(time, 1);

    /// <summary>
    /// How far a left-out key may be from the straight line between its neighbours: a quarter point for coordinates
    /// (a point is 1/1280 of the picture's width), else a thousandth (points, degrees, opacity).
    /// </summary>
    private double ToleranceFor(string type, string property)
    {
        bool coordinate = property is "x" or "y" or "x1" or "y1" or "x2" or "y2" or "cx" or "cy" or "points"
            || (type == "arc" && property == "r") || property is "rx" or "ry";
        return coordinate ? 0.25 * (View.XMax - View.XMin) / 1280 : 1e-3;
    }

    /// <summary>
    /// Leaves out the keys the player gets anyway: in a run of linear changes (moving points are computed every 40 ms),
    /// a key that lies on the straight line between the keys kept around it, within <paramref name="tolerance"/>.
    /// </summary>
    internal static List<Primitive.Key> Simplify(List<Primitive.Key> keys, double tolerance = 1e-3)
    {
        if (keys.Count <= 2)
        {
            return keys;
        }
        var kept = new List<Primitive.Key> { keys[0] };
        int from = 0;
        for (int to = 2; to < keys.Count; to++)
        {
            if (!Bridges(keys, from, to, tolerance))
            {
                // the key before can't be left out: it starts the next run
                kept.Add(keys[to - 1]);
                from = to - 1;
            }
        }
        kept.Add(keys[^1]);
        return kept;
    }

    // whether the straight line from keys[from] to keys[to] passes all keys between them
    private static bool Bridges(List<Primitive.Key> keys, int from, int to, double tolerance)
    {
        Primitive.Key a = keys[from];
        Primitive.Key b = keys[to];
        double span = b.Time - a.Time;
        if (span <= 0)
        {
            return false;
        }
        for (int i = from + 1; i <= to; i++)
        {
            if (keys[i].Easing != Easing.Linear)
            {
                return false;
            }
        }
        for (int i = from + 1; i < to; i++)
        {
            double u = (keys[i].Time - a.Time) / span;
            bool close = (a.Value, b.Value, keys[i].Value) switch
            {
                (double x, double y, double v) => Math.Abs(x + (y - x) * u - v) <= tolerance,
                (double[] x, double[] y, double[] v) when x.Length == y.Length && y.Length == v.Length =>
                    Enumerable.Range(0, x.Length).All(k => Math.Abs(x[k] + (y[k] - x[k]) * u - v[k]) <= tolerance),
                _ => false,
            };
            if (!close)
            {
                return false;
            }
        }
        return true;
    }

    private static double RoundValue(double value) => Math.Round(value, 4);

    private static void WriteValue(Utf8JsonWriter json, object value)
    {
        switch (value)
        {
            case double d:
                json.WriteNumberValue(RoundValue(d));
                break;
            case double[] values:
                json.WriteStartArray();
                foreach (double v in values)
                {
                    json.WriteNumberValue(RoundValue(v));
                }
                json.WriteEndArray();
                break;
            case string s:
                json.WriteStringValue(s);
                break;
            case bool b:
                json.WriteBooleanValue(b);
                break;
            default:
                throw new InvalidOperationException("Unexpected value " + value.GetType().Name);
        }
    }
}
