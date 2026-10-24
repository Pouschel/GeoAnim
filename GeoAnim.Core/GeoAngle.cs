namespace GeoAnim;

/// <summary>
/// An angle A B C: at the vertex B, counterclockwise from the ray BA to the ray BC (so it can be more than 180°). It is
/// drawn as a filled sector with an arc; its label stands on the bisector. Without a radius the player sizes the sector
/// so that the label fits between the arms (or a standard size without label); a right angle gets the arc with a dot.
/// </summary>
public sealed class GeoAngle : GeoObject
{
    private Primitive? sector;
    private double? lastStart;

    internal GeoAngle(string name, GeoPoint a, GeoPoint vertex, GeoPoint c, Options options)
        : base(name, options, a, vertex, c)
    {
        A = a;
        Vertex = vertex;
        C = c;
    }

    public override string Kind => "angle";

    public GeoPoint A { get; }

    public GeoPoint Vertex { get; }

    public GeoPoint C { get; }

    /// <summary>The direction of the first arm in degrees (counterclockwise from the x direction).</summary>
    public double Start { get; private set; }

    /// <summary>The size of the angle in degrees, 0 to 360.</summary>
    public double Degrees { get; private set; }

    public override bool HasLabel => Options.Label != null;

    internal override double DefaultDuration => 600;

    internal override Easing DefaultEasing => Easing.EaseOut;

    internal override void Compute()
    {
        Vec first = A.Position - Vertex.Position;
        Vec second = C.Position - Vertex.Position;
        IsDefined = A.IsDefined && Vertex.IsDefined && C.IsDefined && first.Length > 1e-9 && second.Length > 1e-9;
        if (!IsDefined)
        {
            return;
        }
        double start = Math.Atan2(first.Y, first.X) * 180 / Math.PI;
        double end = Math.Atan2(second.Y, second.X) * 180 / Math.PI;
        Degrees = ((end - start) % 360 + 360) % 360;
        // A point moving on a circle around the vertex keeps its unwrapped path parameter. This distinguishes the
        // initial position (0 degrees) from the same position after one or more complete counterclockwise turns
        // (360 degrees). From the point coordinates alone, a null angle and a full angle are indistinguishable.
        if (Degrees < 1e-9 && C.Path is GeoCircle circle && ReferenceEquals(circle.Center, Vertex))
        {
            double normalizedStart = ((start % 360) + 360) % 360;
            if (C.Parameter - normalizedStart > 1e-9)
            {
                Degrees = 360;
            }
        }
        // the start stays continuous while points move (no jump from 179° to -179°), so the player interpolates well
        if (lastStart is double previous)
        {
            start += 360 * Math.Round((previous - start) / 360);
        }
        Start = start;
        lastStart = start;
    }

    internal override void Render(RenderContext context, Span span)
    {
        if (sector == null)
        {
            IsDrawn = true;
            sector = context.Scene.Create("angle", Layers.Polygon);
            if (HasLabel)
            {
                // also a hidden label: it sizes the sector, which then stays the same when the label is shown
                sector.Animate("text", span, Options.Label!);
            }
        }
        if (HasLabel)
        {
            sector.Animate("labelOpacity", span, LabelOpacity);
        }
        sector.Animate("opacity", span, Visibility);
        sector.Animate("reveal", span, Reveal);
        if (IsDefined)
        {
            sector.Animate("x", span, Vertex.Position.X);
            sector.Animate("y", span, Vertex.Position.Y);
            sector.Animate("start", span, Start);
            sector.Animate("sweep", span, Degrees);
        }
        Color color = ColorOr(Defaults.LineColor);
        // radius in units; 0: the player sizes it (to the label, at least Defaults.AngleSize points)
        sector.Animate("radius", span, Options.Radius ?? 0);
        sector.Animate("base", span, Defaults.AngleSize);
        sector.Animate("color", span, color.ToHex());
        sector.Animate("width", span, WidthOr(Defaults.AngleWidth));
        sector.Animate("fill", span, (Options.Fill ?? color).ToHex());
        sector.Animate("fillOpacity", span, Options.FillOpacity ?? Defaults.FillOpacity);
        sector.Animate("size", span, Options.LabelSize ?? Defaults.LabelSize);
        if (Options.Arrow != Arrow.None)
        {
            // on the arc: at its end (counterclockwise), at its start (clockwise) or both
            sector.Animate("arrow", span, Options.Arrow.ToString().ToLowerInvariant());
        }
    }
}
