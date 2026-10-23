using System.Text.RegularExpressions;

namespace GeoAnim;

/// <summary>
/// A number measured from other objects (the length of a segment, the size of an angle, …); it follows them when they
/// move. Texts show it with a placeholder "{name}" or "{name:digits}", which the player fills in, with a decimal comma.
/// </summary>
public sealed class GeoNumber : GeoObject
{
    private readonly Func<double?> measure;
    private Primitive? primitive;

    private GeoNumber(string name, Func<double?> measure, int digits, params GeoObject[] parents)
        : base(name, Options.None, parents)
    {
        this.measure = measure;
        Digits = digits;
    }

    public override string Kind => "number";

    /// <summary>The current value (the last one while it does not exist).</summary>
    public double Value { get; private set; }

    /// <summary>The decimal places a placeholder without its own shows.</summary>
    public int Digits { get; }

    // a number appears at once
    internal override double DefaultDuration => 0;

    /// <summary>The length of a segment (not of a ray or line: they have none).</summary>
    internal static GeoNumber Length(string name, GeoStraight segment)
    {
        if (segment.StraightKind != StraightKind.Segment)
        {
            throw new GeoException($"A {segment.Kind} has no length; length measures a segment or the distance of two points.");
        }
        return new(name, () => segment.IsDefined ? segment.Direction.Length : null, 1, segment);
    }

    /// <summary>The distance of two points.</summary>
    internal static GeoNumber Distance(string name, GeoPoint a, GeoPoint b) =>
        new(name, () => a.IsDefined && b.IsDefined ? (b.Position - a.Position).Length : null, 1, a, b);

    /// <summary>The size of an angle in degrees (0 to 360).</summary>
    internal static GeoNumber Degrees(string name, GeoAngle angle) =>
        new(name, () => angle.IsDefined ? angle.Degrees : null, 0, angle);

    /// <summary>The area of a polygon or circle.</summary>
    internal static GeoNumber Area(string name, GeoObject item) => item switch
    {
        GeoPolygon polygon => new(name, () => polygon.IsDefined ? Math.Abs(SignedArea(polygon)) : null, 1, polygon),
        GeoCircle circle => new(name, () => circle.IsDefined ? Math.PI * circle.Radius * circle.Radius : null, 1, circle),
        _ => throw new GeoException($"A {item.Kind} has no area; area measures a polygon or a circle."),
    };

    /// <summary>The perimeter of a polygon or the circumference of a circle.</summary>
    internal static GeoNumber Perimeter(string name, GeoObject item) => item switch
    {
        GeoPolygon polygon => new(name, () => polygon.IsDefined
            ? polygon.Vertices.Select((v, i) => (polygon.Vertices[(i + 1) % polygon.Vertices.Count].Position - v.Position).Length).Sum()
            : null, 1, polygon),
        GeoCircle circle => new(name, () => circle.IsDefined ? 2 * Math.PI * circle.Radius : null, 1, circle),
        _ => throw new GeoException($"A {item.Kind} has no perimeter; perimeter measures a polygon or a circle."),
    };

    /// <summary>The radius of a circle.</summary>
    internal static GeoNumber RadiusOf(string name, GeoCircle circle) =>
        new(name, () => circle.IsDefined ? circle.Radius : null, 1, circle);

    // the shoelace formula
    private static double SignedArea(GeoPolygon polygon)
    {
        double sum = 0;
        IReadOnlyList<GeoPoint> v = polygon.Vertices;
        for (int i = 0; i < v.Count; i++)
        {
            Vec p = v[i].Position;
            Vec q = v[(i + 1) % v.Count].Position;
            sum += p.X * q.Y - q.X * p.Y;
        }
        return sum / 2;
    }

    internal override void Compute()
    {
        double? value = measure();
        IsDefined = value.HasValue;
        if (value.HasValue)
        {
            Value = value.Value;
        }
    }

    // a primitive the player does not draw: it reads the values for the placeholders from it
    internal override void Render(RenderContext context, Span span)
    {
        if (primitive == null)
        {
            IsDrawn = true;
            primitive = context.Scene.Create("number", Layers.Label);
            primitive.Animate("name", span, Name);
            primitive.Animate("digits", span, (double)Digits);
        }
        primitive.Animate("valid", span, IsDefined ? 1.0 : 0.0);
        if (IsDefined)
        {
            primitive.Animate("value", span, Value);
        }
    }
}

/// <summary>Placeholders for numbers in texts: "{l}" or "{l:2}" (2 decimal places); "\{" is a brace.</summary>
public static partial class NumberText
{
    [GeneratedRegex(@"(?<!\\)\{([A-Za-z_][A-Za-z0-9_]*'*)(?::(\d))?\}")]
    public static partial Regex Placeholder();

    /// <summary>The placeholders of a text: name, position and length.</summary>
    public static IEnumerable<(string Name, int Index, int Length)> Find(string text) =>
        Placeholder().Matches(text).Select(m => (m.Groups[1].Value, m.Index, m.Length));

    /// <summary>A value as the player shows it: rounded, with a decimal comma and a real minus sign.</summary>
    public static string Format(double value, int digits)
    {
        string text = Math.Round(value, digits, MidpointRounding.AwayFromZero).ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);
        if (text.TrimStart('-').All(c => c is '0' or '.'))
        {
            text = text.TrimStart('-');
        }
        return text.Replace('.', ',').Replace('-', '−');
    }
}
