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
        LabelOpacity = options.NoLabel ? 0 : 1;
        LabelUsed = !options.NoLabel;
        // an angle's offset was a shift from its place on the bisector from the start
        LabelShift = options.Shift ?? (this is GeoAngle && options.Offset is { } offset ? offset : new Vec(0, 0));
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

    /// <summary>Whether the object has a label that "show label" can show (a point always: its name).</summary>
    public virtual bool HasLabel => false;

    /// <summary>The opacity of the label on top of the object's own (0 with "nolabel" or after "hide label").</summary>
    internal double LabelOpacity { get; set; }

    /// <summary>Whether the label is ever shown: only then it gets a primitive.</summary>
    internal bool LabelUsed { get; set; }

    /// <summary>The label's shift from its default place (units); "shift" sets it, "move label … to" changes it.</summary>
    internal Vec LabelShift { get; set; }

    /// <summary>Whether the label's shift changes during the animation ("move label"): then it is always written.</summary>
    internal bool ShiftChanges { get; set; }

    /// <summary>Writes the label's shift into its primitive (tracks sx, sy) once there is one.</summary>
    internal void RenderShift(Primitive label, Span span, string x = "sx", string y = "sy")
    {
        if (LabelShift.X != 0 || LabelShift.Y != 0 || ShiftChanges || label.Current(x) != null)
        {
            // a track that starts later than its primitive ("move label" in a later step) would leave the primitive
            // without a value until then, and the player would not draw it at all: the shift so far holds from its start
            if (label.Current(x) == null && label.FirstTime is double first && first < span.Start)
            {
                label.Animate(x, Span.At(first), LabelShift.X);
                label.Animate(y, Span.At(first), LabelShift.Y);
            }
            label.Animate(x, span, LabelShift.X);
            label.Animate(y, span, LabelShift.Y);
        }
    }

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
