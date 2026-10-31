using System.Globalization;
using System.Text.Json;

namespace GeoAnim.Figures;

/// <summary>A formula as MathJax made it: viewBox in thousandths of an em (baseline at y = 0) and its SVG content.</summary>
internal sealed record FigureFormula(double X, double Y, double Width, double Height, string Svg);

/// <summary>A step of an animation: when it starts and ends (ms) and its description.</summary>
public sealed record FigureStep(double Start, double End, string Caption);

/// <summary>A primitive of an animation with its tracks ([time, value] or [time, value, easing]).</summary>
internal sealed record FigurePrimitive(int Id, string Type, int Layer, bool Unbounded, IReadOnlyDictionary<string, JsonElement> Tracks);

/// <summary>
/// An animation as the player gets it (the JSON of GeoAnim.Core's AnimationData), read for figures; values of tracks
/// at a time as in the player.
/// </summary>
public sealed class FigureAnimation
{
    private FigureAnimation()
    {
    }

    public double XMin { get; private init; }

    public double XMax { get; private init; }

    public double YMin { get; private init; }

    public double YMax { get; private init; }

    public string Background { get; private init; } = "#ffffff";

    public double? Grid { get; private init; }

    public bool Axes { get; private init; }

    public double? AxesSize { get; private init; }

    public string? AxesColor { get; private init; }

    /// <summary>The distance of the ticks and numbers on the x and y axes ("axes step"), or null.</summary>
    public double? AxesStepX { get; private init; }

    public double? AxesStepY { get; private init; }

    /// <summary>The length of a unit in print in points ("print scale" in the script), or null.</summary>
    public double? PrintScale { get; private init; }

    /// <summary>The width of the view in print in points ("print width" in the script), or null.</summary>
    public double? PrintWidth { get; private init; }

    public double Duration { get; private init; }

    public IReadOnlyList<FigureStep> Steps { get; private init; } = [];

    internal IReadOnlyList<FigurePrimitive> Primitives { get; private init; } = [];

    internal IReadOnlyDictionary<string, FigureFormula> Math { get; private init; } = new Dictionary<string, FigureFormula>();

    /// <summary>The time at the end of a step (index from 0; -1: the starting picture, 0 ms).</summary>
    public double EndOf(int step) => step < 0 || Steps.Count == 0 ? 0 : Steps[System.Math.Min(step, Steps.Count - 1)].End;

    /// <summary>Reads the JSON of an animation.</summary>
    public static FigureAnimation Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement view = root.GetProperty("view");
        var steps = new List<FigureStep>();
        foreach (JsonElement step in root.GetProperty("steps").EnumerateArray())
        {
            steps.Add(new FigureStep(step.GetProperty("start").GetDouble(), step.GetProperty("end").GetDouble(),
                step.TryGetProperty("caption", out JsonElement caption) ? caption.GetString() ?? "" : ""));
        }
        var primitives = new List<FigurePrimitive>();
        foreach (JsonElement primitive in root.GetProperty("primitives").EnumerateArray())
        {
            var tracks = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty track in primitive.GetProperty("tracks").EnumerateObject())
            {
                tracks[track.Name] = track.Value.Clone();
            }
            primitives.Add(new FigurePrimitive(primitive.GetProperty("id").GetInt32(), primitive.GetProperty("type").GetString() ?? "",
                primitive.GetProperty("layer").GetInt32(),
                primitive.TryGetProperty("unbounded", out JsonElement unbounded) && unbounded.GetBoolean(), tracks));
        }
        var math = new Dictionary<string, FigureFormula>(StringComparer.Ordinal);
        if (root.TryGetProperty("math", out JsonElement formulas))
        {
            foreach (JsonProperty formula in formulas.EnumerateObject())
            {
                double[] box = formula.Value.GetProperty("box").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                math[formula.Name] = new FigureFormula(box[0], box[1], box[2], box[3], formula.Value.GetProperty("svg").GetString() ?? "");
            }
        }
        return new FigureAnimation
        {
            XMin = view.GetProperty("xmin").GetDouble(),
            XMax = view.GetProperty("xmax").GetDouble(),
            YMin = view.GetProperty("ymin").GetDouble(),
            YMax = view.GetProperty("ymax").GetDouble(),
            Background = root.TryGetProperty("background", out JsonElement background) ? background.GetString() ?? "#ffffff" : "#ffffff",
            Grid = root.TryGetProperty("grid", out JsonElement grid) && grid.ValueKind == JsonValueKind.Number ? grid.GetDouble() : null,
            Axes = root.TryGetProperty("axes", out JsonElement axes) && axes.ValueKind == JsonValueKind.True,
            AxesSize = root.TryGetProperty("axesSize", out JsonElement axesSize) ? axesSize.GetDouble() : null,
            AxesColor = root.TryGetProperty("axesColor", out JsonElement axesColor) ? axesColor.GetString() : null,
            AxesStepX = root.TryGetProperty("axesStepX", out JsonElement axesStepX) ? axesStepX.GetDouble() : null,
            AxesStepY = root.TryGetProperty("axesStepY", out JsonElement axesStepY) ? axesStepY.GetDouble() : null,
            PrintScale = root.TryGetProperty("printScale", out JsonElement printScale) ? printScale.GetDouble() : null,
            PrintWidth = root.TryGetProperty("printWidth", out JsonElement printWidth) ? printWidth.GetDouble() : null,
            Duration = root.GetProperty("duration").GetDouble(),
            Steps = steps,
            Primitives = primitives,
            Math = math,
        };
    }

    // ------------------------------------------------------------------ values over time (as the player's)

    /// <summary>
    /// The value of a track at a time: a number (double), a list of numbers (double[]), a text (colours as "#rrggbb" or
    /// "rgb(r,g,b)" between two colours), true/false; null before its first key.
    /// </summary>
    internal static object? ValueAt(JsonElement track, double time)
    {
        int count = track.GetArrayLength();
        if (count == 0 || time < track[0][0].GetDouble())
        {
            return null;
        }
        int low = 0, high = count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) >> 1;
            if (track[middle][0].GetDouble() <= time)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }
        JsonElement key = track[low];
        object? value = Read(key[1]);
        if (low + 1 >= count)
        {
            return value;
        }
        JsonElement next = track[low + 1];
        double span = next[0].GetDouble() - key[0].GetDouble();
        string easing = next.GetArrayLength() > 2 ? next[2].GetString() ?? "l" : "l";
        double u = span <= 0 ? 1 : (time - key[0].GetDouble()) / span;
        return Lerp(value, Read(next[1]), Ease(easing, u));
    }

    private static object? Read(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().Select(v => v.GetDouble()).ToArray(),
        _ => null,
    };

    private static double Ease(string easing, double u) => easing switch
    {
        "i" => u * u * u,
        "o" => 1 - System.Math.Pow(1 - u, 3),
        "io" => u < 0.5 ? 4 * u * u * u : 1 - System.Math.Pow(-2 * u + 2, 3) / 2,
        "s" => u < 1 ? 0 : 1,
        _ => u,
    };

    private static object? Lerp(object? a, object? b, double u) => (a, b) switch
    {
        (double x, double y) => x + (y - x) * u,
        (double[] x, double[] y) => x.Length == y.Length ? x.Select((v, i) => v + (y[i] - v) * u).ToArray() : (u < 1 ? x : y),
        (string x, string y) when x.StartsWith('#') && y.StartsWith('#') =>
            "rgb(" + string.Join(",", Rgb(x).Zip(Rgb(y), (p, q) => ((int)System.Math.Round(p + (q - p) * u)).ToString(CultureInfo.InvariantCulture))) + ")",
        _ => u < 1 ? a : b,
    };

    /// <summary>A colour as red, green, blue ("#rrggbb", "#rgb" or "rgb(r,g,b)").</summary>
    internal static int[] Rgb(string color)
    {
        if (color.StartsWith("rgb", StringComparison.Ordinal))
        {
            return color[(color.IndexOf('(') + 1)..color.IndexOf(')')].Split(',').Select(v => int.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray();
        }
        string hex = color.TrimStart('#');
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }
        int n = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
    }
}
