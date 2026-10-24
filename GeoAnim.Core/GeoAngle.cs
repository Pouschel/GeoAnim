namespace GeoAnim;

/// <summary>
/// An angle A B C: at the vertex B, counterclockwise from the ray BA to the ray BC (so it can be more than 180°; after a
/// reflection clockwise). It is
/// drawn as a filled sector with an arc; its label stands on the bisector. Without a radius the player sizes the sector
/// so that the label fits between the arms (or a standard size without label); a right angle gets the arc with a dot.
/// </summary>
public sealed class GeoAngle : GeoObject
{
    private Primitive? sector;
    private double? lastStart;
    private double? lastDegrees;

    // how close (in degrees) the arms must be to count as lying on top of each other
    private const double ArmsTogether = 1e-6;

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
        if (Mirrored)
        {
            // reflected: clockwise from BA to BC, that is counterclockwise from BC to BA
            (start, end) = (end, start);
        }
        Degrees = ((end - start) % 360 + 360) % 360;
        // With both arms on top of each other the positions cannot tell a null angle (0°) from a full angle (360°); where
        // the angle comes from can: from more than 180° the second arm has turned counterclockwise onto the first one (or
        // the first one clockwise onto the second), so it is a full angle, and it stays one while the arms stay together.
        // Movements are computed in small steps, so the angle before is always close. (This also settles a value like
        // 359.9999999° that rounding gives for arms that lie on top of each other.)
        if (Degrees < ArmsTogether || Degrees > 360 - ArmsTogether)
        {
            Degrees = (lastDegrees ?? DegreesWhenDefined(start)) > 180 ? 360 : 0;
        }
        lastDegrees = Degrees;
        // the start stays continuous while points move (no jump from 179° to -179°), so the player interpolates well
        if (lastStart is double previous)
        {
            start += 360 * Math.Round((previous - start) / 360);
        }
        Start = start;
        lastStart = start;
    }

    // A new angle with its arms on top of each other has no movement before it. Its second point may lie on a circle
    // around the vertex, given by the angle on it: one turn or more beyond the first arm ("on k at 360" with the first
    // arm at 0°) means a full angle; at the first arm itself ("at 0", "at -90" with the first arm at 270°) a null angle.
    private double DegreesWhenDefined(double start)
    {
        if (C.Path is GeoCircle circle && ReferenceEquals(circle.Center, Vertex))
        {
            double turns = (C.Parameter - (start % 360 + 360) % 360) / 360;
            return Math.Round(turns) >= 1 ? 360 : 0;
        }
        return 0;
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
        // the label moved from its place on the bisector (in units): "shift", or "offset" as before
        RenderShift(sector, span, "ox", "oy");
        if (Options.Arrow != Arrow.None)
        {
            // on the arc: at its end (counterclockwise), at its start (clockwise) or both
            sector.Animate("arrow", span, Options.Arrow.ToString().ToLowerInvariant());
        }
    }
}
