namespace GeoAnim;

/// <summary>
/// A drawing element of the player (dot, line, arc, polygon, text) with one track per property: the values it takes
/// over time. Between two keys the player interpolates with the easing of the later key. Before its first key a
/// primitive is not drawn.
/// </summary>
internal sealed class Primitive(int id, string type, int layer)
{
    private readonly Dictionary<string, List<Key>> tracks = new(StringComparer.Ordinal);

    internal readonly record struct Key(double Time, object Value, Easing Easing);

    public int Id { get; } = id;

    /// <summary>"dot", "line", "arc", "polygon" or "text".</summary>
    public string Type { get; } = type;

    /// <summary>Drawing order: higher layers are drawn on top.</summary>
    public int Layer { get; } = layer;

    public IReadOnlyDictionary<string, List<Key>> Tracks => tracks;

    /// <summary>The object it belongs to (its label too), or null.</summary>
    public GeoObject? Owner { get; init; }

    /// <summary>
    /// Changes <paramref name="property"/> to <paramref name="value"/> during <paramref name="span"/>: it keeps its value
    /// until the start of the span, then changes with the span's easing until its end. A span of length 0 sets the value
    /// at once. A value that does not change adds nothing.
    /// </summary>
    public void Animate(string property, Span span, object value)
    {
        if (!tracks.TryGetValue(property, out List<Key>? track))
        {
            track = new List<Key>();
            tracks[property] = track;
        }
        if (track.Count == 0)
        {
            // the first value: the primitive appears with it at the start of the span
            track.Add(new Key(span.Start, value, Easing.Step));
            return;
        }
        Key last = track[^1];
        if (SameValue(last.Value, value))
        {
            return;
        }
        if (span.Start < last.Time - 1e-6)
        {
            throw new GeoException($"Two animations change the same property ({property}) at the same time.");
        }
        if (span.End - span.Start < 1e-6)
        {
            if (Math.Abs(last.Time - span.Start) < 1e-6 && last.Easing == Easing.Step)
            {
                // a second change at the same moment: only the last one counts
                track[^1] = last with { Value = value };
            }
            else
            {
                track.Add(new Key(span.Start, value, Easing.Step));
            }
            return;
        }
        if (last.Time < span.Start - 1e-6)
        {
            // hold the old value until the animation starts
            track.Add(new Key(span.Start, last.Value, Easing.Linear));
        }
        track.Add(new Key(span.End, value, span.Easing));
    }

    /// <summary>When the primitive got its first value (of any property), or null if it has none yet.</summary>
    public double? FirstTime => tracks.Values.Where(t => t.Count > 0).Select(t => (double?)t[0].Time).Min();

    /// <summary>The last value of the property, or null if it has none yet.</summary>
    public object? Current(string property) =>
        tracks.TryGetValue(property, out List<Key>? track) && track.Count > 0 ? track[^1].Value : null;

    private static bool SameValue(object a, object b) => (a, b) switch
    {
        (double x, double y) => Math.Abs(x - y) < 1e-7,
        (double[] x, double[] y) => x.Length == y.Length && x.Zip(y).All(p => Math.Abs(p.First - p.Second) < 1e-7),
        _ => Equals(a, b),
    };
}

/// <summary>A time span of the animation in milliseconds, with the easing of a change during it.</summary>
internal readonly record struct Span(double Start, double End, Easing Easing = Easing.Linear)
{
    public static Span At(double time) => new(time, time, Easing.Step);
}
