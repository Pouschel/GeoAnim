namespace GeoAnim;

/// <summary>
/// A part of a circle from A to B (counterclockwise; after a reflection clockwise): an arc ("arc b = k from A to B") or a
/// sector, bounded by the two radii and the arc ("sector s = k from A to B", filled like a polygon). A and B give only the
/// directions from the centre, so the arc stays on the circle when it changes. It is drawn from A to B like with a compass.
/// </summary>
public sealed class GeoArc : GeoObject
{
    private Primitive? disc;
    private Primitive? part;
    private double? lastStart;

    internal GeoArc(string name, GeoCircle circle, GeoPoint from, GeoPoint to, bool isSector, Options options)
        : base(name, options, circle, from, to)
    {
        Circle = circle;
        From = from;
        To = to;
        IsSector = isSector;
    }

    public override string Kind => IsSector ? "sector" : "arc";

    public GeoCircle Circle { get; }

    public GeoPoint From { get; }

    public GeoPoint To { get; }

    /// <summary>Whether it is a sector (an area) rather than an arc (a line).</summary>
    public bool IsSector { get; }

    /// <summary>The direction of A from the centre in degrees (where it starts).</summary>
    public double Start { get; private set; }

    /// <summary>The angle at the centre in degrees, 0 to 360.</summary>
    public double Degrees { get; private set; }

    /// <summary>The length of the arc.</summary>
    public double Length => Circle.Radius * Degrees * Math.PI / 180;

    /// <summary>The area of the sector.</summary>
    public double Area => Circle.Radius * Circle.Radius * Degrees * Math.PI / 360;

    public override bool HasLabel => Options.Label != null;

    internal override double DefaultDuration => 800;

    internal override void Compute()
    {
        Vec center = Circle.Center.Position;
        Vec first = From.Position - center;
        Vec second = To.Position - center;
        IsDefined = Circle.IsDefined && From.IsDefined && To.IsDefined && first.Length > 1e-9 && second.Length > 1e-9;
        if (!IsDefined)
        {
            return;
        }
        double start = first.AngleDegrees;
        double end = second.AngleDegrees;
        if (Mirrored)
        {
            (start, end) = (end, start);
        }
        Degrees = ((end - start) % 360 + 360) % 360;
        // the start stays continuous while points move, so the player interpolates well
        if (lastStart is double previous)
        {
            start += 360 * Math.Round((previous - start) / 360);
        }
        Start = start;
        lastStart = start;
    }

    internal override void Render(RenderContext context, Span span)
    {
        if (part == null)
        {
            IsDrawn = true;
            // a sector's area below, with the fills of polygons and angles; its border and an arc with the lines
            if (IsSector)
            {
                disc = context.Scene.Create("disc", Layers.Of(Options, Layers.Polygon));
            }
            part = context.Scene.Create("part", Layers.Of(Options, Layers.Curve));
            if (IsSector)
            {
                part.Animate("sector", span, 1.0);
            }
            if (HasLabel)
            {
                part.Animate("text", span, Options.Label!);
            }
        }
        foreach (Primitive primitive in new[] { disc, part }.OfType<Primitive>())
        {
            primitive.Animate("opacity", span, Visibility);
            if (IsDefined)
            {
                primitive.Animate("cx", span, Circle.Center.Position.X);
                primitive.Animate("cy", span, Circle.Center.Position.Y);
                primitive.Animate("r", span, Circle.Radius);
                primitive.Animate("start", span, Start);
            }
            primitive.Animate("sweep", span, Degrees * Reveal);
        }
        if (disc != null)
        {
            disc.Animate("fill", span, FillOr(Defaults.LineColor).ToHex());
            disc.Animate("fillOpacity", span, FillOpacityOr(Defaults.FillOpacity));
        }
        part.Animate("color", span, ColorOr(Defaults.LineColor).ToHex());
        part.Animate("width", span, WidthOr(Defaults.LineWidth));
        if (Options.Arrow != Arrow.None)
        {
            part.Animate("arrow", span, Options.Arrow.ToString().ToLowerInvariant());
        }
        if (HasLabel)
        {
            part.Animate("labelOpacity", span, Reveal * LabelOpacity);
            part.Animate("size", span, Options.LabelSize ?? Defaults.LabelSize);
            if (Options.Halo)
            {
                part.Animate("halo", span, 1.0);
            }
            RenderShift(part, span);
        }
    }
}
