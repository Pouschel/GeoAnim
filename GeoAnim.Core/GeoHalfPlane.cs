namespace GeoAnim;

/// <summary>
/// A half-plane: the side of a line (or of the line of a segment or ray) on which a point lies ("side"), or the other one
/// ("opposite"). A filled area up to the edges of the picture, below the fills of polygons and angles; its border is not
/// drawn (the line is an object of its own). It grows from the line. A point on the line: no half-plane.
/// </summary>
public sealed class GeoHalfPlane : GeoObject
{
    private Primitive? area;

    internal GeoHalfPlane(string name, GeoStraight line, GeoPoint point, bool opposite, Options options)
        : base(name, options, line, point)
    {
        Line = line;
        Point = point;
        Opposite = opposite;
    }

    public override string Kind => "halfplane";

    public GeoStraight Line { get; }

    /// <summary>The point that gives the side.</summary>
    public GeoPoint Point { get; }

    /// <summary>The side away from <see cref="Point"/>.</summary>
    public bool Opposite { get; }

    /// <summary>A point of the border line.</summary>
    public Vec Origin { get; private set; }

    /// <summary>The unit normal of the line into the half-plane.</summary>
    public Vec Normal { get; private set; }

    internal override double DefaultDuration => 800;

    internal override Easing DefaultEasing => Easing.EaseOut;

    /// <summary>Whether a point lies in the (closed) half-plane.</summary>
    public bool Contains(Vec p) => IsDefined && Vec.Dot(p - Origin, Normal) >= -1e-9;

    internal override void Compute()
    {
        IsDefined = Line.IsDefined && Point.IsDefined;
        if (!IsDefined)
        {
            return;
        }
        Vec d = Line.Direction;
        Vec n = new Vec(-d.Y, d.X) * (1 / d.Length);
        double side = Vec.Dot(Point.Position - Line.A.Position, n);
        // on the line: no side
        IsDefined = Math.Abs(side) > 1e-9;
        if (!IsDefined)
        {
            return;
        }
        Origin = Line.A.Position;
        Normal = (side > 0) != Opposite ? n : n * -1;
    }

    internal override void Render(RenderContext context, Span span)
    {
        if (area == null)
        {
            IsDrawn = true;
            area = context.Scene.Create(this, "halfplane", Layers.Of(Options, Layers.HalfPlane));
            // up to the edges of the picture: no part of the content a printed figure is cut to
            area.Unbounded = true;
        }
        area.Animate("opacity", span, Visibility);
        if (IsDefined)
        {
            area.Animate("x", span, Origin.X);
            area.Animate("y", span, Origin.Y);
            area.Animate("nx", span, Normal.X);
            area.Animate("ny", span, Normal.Y);
        }
        // how far it reaches from the line, as a part of the way to the farthest corner of the picture
        area.Animate("reach", span, Reveal);
        area.Animate("fill", span, FillOr(Defaults.LineColor).ToHex());
        area.Animate("fillOpacity", span, FillOpacityOr(Defaults.FillOpacity));
    }
}
