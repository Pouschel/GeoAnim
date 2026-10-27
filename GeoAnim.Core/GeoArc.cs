namespace GeoAnim;

/// <summary>
/// A part of a circle from A to B (counterclockwise; after a reflection clockwise): an arc ("arc b = k from A to B") or a
/// sector, bounded by the two radii and the arc ("sector s = k from A to B", filled like a polygon). A and B give only the
/// directions from the centre (or are directions in degrees: "from 30 to 120"), so the arc stays on the circle when it
/// changes. It is drawn from A to B like with a compass; with "clockwise" it runs clockwise from A to B (internally the
/// counterclockwise part from B to A, drawn from its end).
/// </summary>
public sealed class GeoArc : GeoObject
{
    private Primitive? disc;
    private Primitive? part;
    private double? lastStart;

    internal GeoArc(string name, GeoCircle circle, ArcEnd from, ArcEnd to, bool isSector, Options options)
        : base(name, options, new GeoObject?[] { circle, from.Point, to.Point }.OfType<GeoObject>().ToArray())
    {
        Circle = circle;
        // clockwise from A to B: the counterclockwise part from B to A
        (From, To) = options.Clockwise ? (to, from) : (from, to);
        IsSector = isSector;
    }

    public override string Kind => IsSector ? "sector" : "arc";

    public GeoCircle Circle { get; }

    /// <summary>Where the counterclockwise part begins (with "clockwise": the end given in the script).</summary>
    public ArcEnd From { get; }

    /// <summary>Where the counterclockwise part ends.</summary>
    public ArcEnd To { get; }

    /// <summary>Whether it is drawn clockwise (from <see cref="To"/> to <see cref="From"/>).</summary>
    public bool Clockwise => Options.Clockwise;

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
        IsDefined = Circle.IsDefined && From.Direction(Circle) is not null && To.Direction(Circle) is not null;
        if (!IsDefined)
        {
            return;
        }
        double start = From.Direction(Circle)!.Value;
        double end = To.Direction(Circle)!.Value;
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
                disc = context.Scene.Create(this, "disc", Layers.Of(Options, Layers.Polygon));
            }
            part = context.Scene.Create(this, "part", Layers.Of(Options, Layers.Curve));
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
                // clockwise: it grows from its end
                primitive.Animate("start", span, Clockwise ? Start + Degrees * (1 - Reveal) : Start);
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
            // "arrow" points the way it is drawn: clockwise at the counterclockwise start
            Arrow arrow = !Clockwise ? Options.Arrow : Options.Arrow switch { Arrow.End => Arrow.Start, Arrow.Start => Arrow.End, _ => Options.Arrow };
            part.Animate("arrow", span, arrow.ToString().ToLowerInvariant());
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

/// <summary>An end of an arc or sector: a point (its direction from the centre counts) or a fixed direction in degrees.</summary>
public readonly record struct ArcEnd(GeoPoint? Point, double Degrees = 0)
{
    public static implicit operator ArcEnd(GeoPoint point) => new(point);

    public static implicit operator ArcEnd(double degrees) => new(null, degrees);

    /// <summary>The direction from the circle's centre in degrees, or null where there is none (the point is the centre or does not exist).</summary>
    internal double? Direction(GeoCircle circle)
    {
        if (Point == null)
        {
            return Degrees;
        }
        Vec d = Point.Position - circle.Center.Position;
        return Point.IsDefined && d.Length > 1e-9 ? d.AngleDegrees : null;
    }

    /// <summary>For messages: the point's name or the direction.</summary>
    public override string ToString() => Point?.Name ?? Degrees.ToString(System.Globalization.CultureInfo.InvariantCulture) + "°";
}
