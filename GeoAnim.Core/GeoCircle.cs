namespace GeoAnim;

/// <summary>A circle with a centre and either a point on it or a radius. It is drawn like with a compass.</summary>
public sealed class GeoCircle : GeoObject
{
    private readonly GeoPoint? through;
    private readonly double fixedRadius;
    private readonly Func<double?>? radiusSource;
    private Primitive? arc;
    private Primitive? disc;

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

    /// <summary>
    /// A circle whose radius follows the drawing (the compass takes a length): the distance of two points or a number
    /// (measured or computed); <paramref name="parents"/> are what it depends on.
    /// </summary>
    internal GeoCircle(string name, GeoPoint center, Func<double?> radius, Options options, params GeoObject[] parents)
        : base(name, options, [center, .. parents])
    {
        Center = center;
        radiusSource = radius;
    }

    public override string Kind => "circle";

    public GeoPoint Center { get; }

    public double Radius { get; private set; }

    /// <summary>Where drawing starts: the direction of the point on the circle, otherwise 0°.</summary>
    internal double StartAngle { get; private set; }

    internal override double DefaultDuration => 1200;

    internal override void Compute()
    {
        if (radiusSource != null)
        {
            double? radius = radiusSource();
            IsDefined = Center.IsDefined && radius > 1e-9;
            if (IsDefined)
            {
                Radius = radius!.Value;
            }
            return;
        }
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
            // "fill": a disc below, with the fills of polygons and angles; it grows with the compass stroke
            if (Options.Filled)
            {
                disc = context.Scene.Create(this, "disc", Layers.Of(Options, Layers.Polygon));
            }
            arc = context.Scene.Create(this, "arc", Layers.Of(Options, Layers.Curve));
        }
        if (disc != null)
        {
            disc.Animate("opacity", span, Visibility);
            if (IsDefined)
            {
                disc.Animate("cx", span, Center.Position.X);
                disc.Animate("cy", span, Center.Position.Y);
                disc.Animate("r", span, Radius);
                disc.Animate("start", span, StartAngle);
            }
            disc.Animate("sweep", span, 360 * Reveal);
            disc.Animate("fill", span, FillOr(Defaults.LineColor).ToHex());
            disc.Animate("fillOpacity", span, FillOpacityOr(Defaults.FillOpacity));
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
