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

    public ViewWindow View { get; }

    public Color Background { get; }

    public double? GridStep { get; }

    public bool Axes { get; }

    /// <summary>Total length in milliseconds.</summary>
    public double Duration { get; }

    public IReadOnlyList<AnimationStep> Steps { get; }

    /// <summary>The formulas of all texts (AsciiMath → SVG).</summary>
    public IReadOnlyDictionary<string, Formula> Formulas { get; }

    internal IReadOnlyList<Primitive> Primitives => primitives;

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
            json.WriteNumber("duration", RoundTime(Duration));
            json.WriteStartArray("steps");
            foreach (AnimationStep step in Steps)
            {
                json.WriteStartObject();
                json.WriteString("caption", step.Caption);
                json.WriteNumber("start", RoundTime(step.Start));
                json.WriteNumber("end", RoundTime(step.End));
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
                json.WriteNumber("id", primitive.Id);
                json.WriteString("type", primitive.Type);
                json.WriteNumber("layer", primitive.Layer);
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
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static double RoundTime(double time) => Math.Round(time, 1);

    /// <summary>
    /// How far a left-out key may be from the straight line between its neighbours: a quarter point for coordinates
    /// (a point is 1/1280 of the picture's width), else a thousandth (points, degrees, opacity).
    /// </summary>
    private double ToleranceFor(string type, string property)
    {
        bool coordinate = property is "x" or "y" or "x1" or "y1" or "x2" or "y2" or "cx" or "cy" or "points"
            || (type == "arc" && property == "r");
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
