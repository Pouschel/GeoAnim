namespace GeoAnim;

/// <summary>
/// An object of a construction. Objects can depend on others (a segment on its end points, an intersection on two
/// curves); when a point moves, everything that depends on it follows.
/// </summary>
public abstract class GeoObject
{
    private protected GeoObject(string name, Options options, params GeoObject[] parents)
    {
        Name = name;
        Options = options;
        Parents = parents;
    }

    public string Name { get; }

    internal Options Options { get; }

    internal IReadOnlyList<GeoObject> Parents { get; }

    /// <summary>"point", "segment", … for messages.</summary>
    public abstract string Kind { get; }

    /// <summary>Whether the object exists for the current positions (e.g. an intersection of circles that meet).</summary>
    public bool IsDefined { get; private protected set; } = true;

    // how far the object is drawn (0 to 1) and its opacity
    internal double Reveal { get; set; }

    internal double Opacity { get; set; } = 1;

    /// <summary>Whether it has primitives in the animation (it was shown once).</summary>
    internal bool IsDrawn { get; private protected set; }

    internal abstract double DefaultDuration { get; }

    internal virtual Easing DefaultEasing => Easing.EaseInOut;

    internal double Visibility => IsDefined ? Opacity : 0;

    internal Color ColorOr(Color fallback) => Options.Color ?? fallback;

    internal double WidthOr(double fallback) => Options.Width ?? fallback;

    /// <summary>Computes the object from its parents (they are computed first).</summary>
    internal abstract void Compute();

    /// <summary>Writes the object's current state into its primitives, changing during <paramref name="span"/>.</summary>
    internal abstract void Render(RenderContext context, Span span);

    public override string ToString() => $"{Kind} {Name}";
}

/// <summary>What objects need for drawing.</summary>
internal sealed class RenderContext(Scene scene, ViewWindow view)
{
    public Scene Scene { get; } = scene;

    public ViewWindow View { get; } = view;
}

/// <summary>Default sizes and colours.</summary>
internal static class Defaults
{
    public static readonly Color PointColor = Color.Names["blue"];
    public static readonly Color LineColor = Color.Names["black"];
    public const double LineWidth = 2.5;
    public const double PointSize = 5;
    public const double LabelSize = 24;
    public const double TextSize = 26;
    public const double FillOpacity = 0.2;
    public const double AngleSize = 36;
    public const double AngleWidth = 1.5;
    public static readonly Vec LabelOffset = new(7, 7);
}
