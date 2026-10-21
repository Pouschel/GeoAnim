namespace GeoAnim;

/// <summary>A segment, a line or a ray through two points.</summary>
public sealed class GeoStraight : GeoObject
{
    private Primitive? line;

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
    private (Vec Start, Vec End)? VisiblePart(ViewWindow view)
    {
        Vec a = A.Position;
        Vec d = Direction;
        if (StraightKind == StraightKind.Segment)
        {
            return (a, B.Position);
        }
        (double min, double max) = ParameterRange;
        var clip = view.Clip(a, d, min, max);
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
            line = context.Scene.Create("line", Layers.Curve);
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
    }
}

public enum StraightKind
{
    Segment,
    Line,
    Ray,
}
