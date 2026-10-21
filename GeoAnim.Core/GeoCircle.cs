namespace GeoAnim;

/// <summary>A circle with a centre and either a point on it or a radius. It is drawn like with a compass.</summary>
public sealed class GeoCircle : GeoObject
{
    private readonly GeoPoint? through;
    private readonly double fixedRadius;
    private Primitive? arc;

    internal GeoCircle(string name, GeoPoint center, GeoPoint through, Options options)
        : base(name, options, center, through)
    {
        Center = center;
        this.through = through;
    }

    internal GeoCircle(string name, GeoPoint center, double radius, Options options)
        : base(name, options, center)
    {
        if (!(radius > 0))
        {
            throw new GeoException($"The radius of {name} must be positive.");
        }
        Center = center;
        fixedRadius = radius;
    }

    public override string Kind => "circle";

    public GeoPoint Center { get; }

    public double Radius { get; private set; }

    /// <summary>Where drawing starts: the direction of the point on the circle, otherwise 0°.</summary>
    internal double StartAngle { get; private set; }

    internal override double DefaultDuration => 1200;

    internal override void Compute()
    {
        if (through == null)
        {
            Radius = fixedRadius;
            IsDefined = Center.IsDefined;
            return;
        }
        Vec radiusVector = through.Position - Center.Position;
        Radius = radiusVector.Length;
        IsDefined = Center.IsDefined && through.IsDefined && Radius > 1e-9;
        if (IsDefined)
        {
            StartAngle = radiusVector.AngleDegrees;
        }
    }

    internal override void Render(RenderContext context, Span span)
    {
        if (arc == null)
        {
            IsDrawn = true;
            arc = context.Scene.Create("arc", Layers.Curve);
        }
        arc.Animate("opacity", span, Visibility);
        if (IsDefined)
        {
            arc.Animate("cx", span, Center.Position.X);
            arc.Animate("cy", span, Center.Position.Y);
            arc.Animate("r", span, Radius);
            arc.Animate("start", span, StartAngle);
        }
        arc.Animate("sweep", span, 360 * Reveal);
        arc.Animate("color", span, ColorOr(Defaults.LineColor).ToHex());
        arc.Animate("width", span, WidthOr(Defaults.LineWidth));
    }
}
