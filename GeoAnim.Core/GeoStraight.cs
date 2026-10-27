namespace GeoAnim;

/// <summary>A segment, a line or a ray through two points.</summary>
public sealed class GeoStraight : GeoObject
{
    private Primitive? line;
    private Primitive? label;

    internal GeoStraight(StraightKind straightKind, string name, GeoPoint a, GeoPoint b, Options options)
        : base(name, options, a, b)
    {
        StraightKind = straightKind;
        A = a;
        B = b;
    }

    public StraightKind StraightKind { get; }

    public GeoPoint A { get; }

    public GeoPoint B { get; }

    public override string Kind => StraightKind switch
    {
        StraightKind.Segment => "segment",
        StraightKind.Ray => "ray",
        _ => "line",
    };

    public override bool HasLabel => Options.Label != null;

    internal override double DefaultDuration => 800;

    /// <summary>The allowed parameters t of A + t·(B − A): [0, 1] for a segment, [0, ∞) for a ray.</summary>
    internal (double Min, double Max) ParameterRange => StraightKind switch
    {
        StraightKind.Segment => (0, 1),
        StraightKind.Ray => (0, double.PositiveInfinity),
        _ => (double.NegativeInfinity, double.PositiveInfinity),
    };

    internal Vec Direction => B.Position - A.Position;

    internal override void Compute() =>
        IsDefined = A.IsDefined && B.IsDefined && Direction.Length > 1e-9;

    /// <summary>The visible end points: the segment itself, or the line or ray cut at the edges of the view.</summary>
    internal (Vec Start, Vec End)? VisiblePart(ViewWindow view)
    {
        Vec a = A.Position;
        Vec d = Direction;
        if (StraightKind == StraightKind.Segment)
        {
            return (a, B.Position);
        }
        (double min, double max) = ParameterRange;
        // a ray with its arrowhead at the edge ends a little inside the picture, so that the head is seen
        double margin = Options.Arrow is Arrow.End or Arrow.Both ? -0.012 : 0.05;
        var clip = view.Clip(a, d, min, max, margin);
        if (clip == null)
        {
            return null;
        }
        return (a + clip.Value.TMin * d, a + clip.Value.TMax * d);
    }

    internal override void Render(RenderContext context, Span span)
    {
        if (line == null)
        {
            IsDrawn = true;
            line = context.Scene.Create(this, "line", Layers.Of(Options, Layers.Curve));
        }
        var part = IsDefined ? VisiblePart(context.View) : null;
        line.Animate("opacity", span, part == null ? 0 : Opacity);
        if (part != null)
        {
            (Vec start, Vec end) = part.Value;
            // a segment and a ray grow from A, a line from the middle of its visible part
            Vec origin = StraightKind == StraightKind.Line ? Vec.Lerp(start, end, 0.5) : start;
            Vec p1 = Vec.Lerp(origin, start, Reveal);
            Vec p2 = Vec.Lerp(origin, end, Reveal);
            line.Animate("x1", span, p1.X);
            line.Animate("y1", span, p1.Y);
            line.Animate("x2", span, p2.X);
            line.Animate("y2", span, p2.Y);
        }
        line.Animate("color", span, ColorOr(Defaults.LineColor).ToHex());
        line.Animate("width", span, WidthOr(Defaults.LineWidth));
        if (Options.Arrow != Arrow.None)
        {
            // the player draws the heads at the ends of the visible line (of a ray: at the edge of the picture)
            line.Animate("arrow", span, Options.Arrow.ToString().ToLowerInvariant());
        }
        if (HasLabel && LabelUsed)
        {
            RenderLabel(context, span, part != null);
        }
    }

    /// <summary>Distance of a label from its line, in points.</summary>
    private const double LabelDistance = 8;

    // the label at A + t·(B − A): by default a little to the right of the direction A → B (outside a triangle ABC
    // named counterclockwise), with an offset at that offset
    private void RenderLabel(RenderContext context, Span span, bool visible)
    {
        label ??= context.Scene.Create(this, "text", Layers.Label);
        WriteLabel(label, span);
        label.Animate("opacity", span, visible ? Opacity * Reveal * LabelOpacity : 0);
        if (Options.Halo)
        {
            label.Animate("halo", span, 1.0);
        }
        label.Animate("color", span, ColorOr(Defaults.LineColor).ToHex());
    }

    /// <summary>The label's text and place (also for the ghost in an editor, whose label may not be shown yet).</summary>
    internal void WriteLabel(Primitive label, Span span)
    {
        label.Animate("text", span, Options.Label!);
        if (IsDefined)
        {
            Vec at = A.Position + (Options.LabelPosition ?? 0.5) * Direction;
            if (Options.Offset is { } offset)
            {
                at += offset;
            }
            else
            {
                // the normal to the right (after a reflection to the left): the player moves the text along it until it keeps the gap to the line
                Vec d = Direction;
                Vec normal = (Mirrored ? -1 : 1) / d.Length * new Vec(d.Y, -d.X);
                label.Animate("nx", span, normal.X);
                label.Animate("ny", span, normal.Y);
                label.Animate("gap", span, LabelDistance);
            }
            label.Animate("x", span, at.X);
            label.Animate("y", span, at.Y);
            label.Animate("anchor", span, "middle");
        }
        label.Animate("size", span, Options.LabelSize ?? Defaults.LabelSize);
        RenderShift(label, span);
    }
}

public enum StraightKind
{
    Segment,
    Line,
    Ray,
}
