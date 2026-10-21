using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GeoAnim;

/// <summary>
/// A finished animation: what the player needs. <see cref="ToJson"/> gives the format the player reads
/// (described in README.md of the repository).
/// </summary>
public sealed class AnimationData
{
    private readonly IReadOnlyList<Primitive> primitives;

    internal AnimationData(
        string? title, ViewWindow view, Color background, double? gridStep, bool axes, double duration,
        IReadOnlyList<AnimationStep> steps, IReadOnlyList<Primitive> primitives)
    {
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

    public ViewWindow View { get; }

    public Color Background { get; }

    public double? GridStep { get; }

    public bool Axes { get; }

    /// <summary>Total length in milliseconds.</summary>
    public double Duration { get; }

    public IReadOnlyList<AnimationStep> Steps { get; }

    internal IReadOnlyList<Primitive> Primitives => primitives;

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
                    foreach (Primitive.Key key in track.Value)
                    {
                        // [time, value] or [time, value, easing]
                        json.WriteStartArray();
                        json.WriteNumberValue(RoundTime(key.Time));
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
