namespace GeoAnim;

/// <summary>
/// An ellipse with two foci F and G: the points whose distances to F and G add up to a sum (the length of the major axis),
/// given as a number, the distance of two points, a number's value, or by a point on it. It lies as F and G lie; it is
/// drawn like a circle, from its start round once. A sum smaller than the distance of the foci: no ellipse; the same: the
/// segment FG.
/// </summary>
public sealed class GeoEllipse : GeoObject
{
    private readonly GeoPoint? through;
    private readonly Func<double?>? sumSource;
    private Primitive? curve;
    private Primitive? disc;

    internal GeoEllipse(string name, GeoPoint focus1, GeoPoint focus2, GeoPoint through, Options options)
        : base(name, options, focus1, focus2, through)
    {
        Focus1 = focus1;
        Focus2 = focus2;
        this.through = through;
    }

    /// <summary>An ellipse whose sum of distances follows a number (fixed, the distance of two points or a number of the construction).</summary>
    internal GeoEllipse(string name, GeoPoint focus1, GeoPoint focus2, Func<double?> sum, Options options, params GeoObject[] parents)
        : base(name, options, [focus1, focus2, .. parents])
    {
        Focus1 = focus1;
        Focus2 = focus2;
        sumSource = sum;
    }

    public override string Kind => "ellipse";

    public GeoPoint Focus1 { get; }

    public GeoPoint Focus2 { get; }

    /// <summary>The centre: the midpoint of the foci.</summary>
    public Vec Center { get; private set; }

    /// <summary>The semi-major axis a (half the sum of distances).</summary>
    public double A { get; private set; }

    /// <summary>The semi-minor axis b.</summary>
    public double B { get; private set; }

    /// <summary>The direction of the major axis in degrees (from F to G; 0 if they are one point).</summary>
    public double Rotation { get; private set; }

    /// <summary>Where drawing starts: the parameter of the point on it, otherwise 0 (the vertex beyond G).</summary>
    internal double StartParameter { get; private set; }

    internal override double DefaultDuration => 1200;

    /// <summary>The point at the parameter <paramref name="degrees"/> (0: the vertex beyond G, 90: the end of the minor axis to the left of FG).</summary>
    public Vec PointAt(double degrees)
    {
        double t = degrees * Math.PI / 180, r = Rotation * Math.PI / 180;
        Vec u = new(Math.Cos(r), Math.Sin(r));
        Vec v = new(-u.Y, u.X);
        return Center + A * Math.Cos(t) * u + B * Math.Sin(t) * v;
    }

    /// <summary>A point in the ellipse's own coordinates (centre at 0, major axis along x).</summary>
    internal Vec ToLocal(Vec point)
    {
        double r = Rotation * Math.PI / 180;
        Vec d = point - Center;
        return new Vec(d.X * Math.Cos(r) + d.Y * Math.Sin(r), -d.X * Math.Sin(r) + d.Y * Math.Cos(r));
    }

    /// <summary>The parameter (degrees) of the point of the ellipse in the direction of <paramref name="point"/> as seen in its own coordinates.</summary>
    internal double ParameterOf(Vec point)
    {
        Vec local = ToLocal(point);
        return Math.Atan2(B > 1e-12 ? local.Y / B : 0, local.X / A) * 180 / Math.PI;
    }

    internal override void Compute()
    {
        IsDefined = Focus1.IsDefined && Focus2.IsDefined && (through == null || through.IsDefined);
        if (!IsDefined)
        {
            return;
        }
        Vec between = Focus2.Position - Focus1.Position;
        double c = between.Length / 2;
        double? sum = through != null
            ? (through.Position - Focus1.Position).Length + (through.Position - Focus2.Position).Length
            : sumSource!();
        // the segment FG at most a rounding error longer than the sum is still the segment
        IsDefined = sum is double s && s > 1e-9 && s / 2 >= c - 1e-9;
        if (!IsDefined)
        {
            return;
        }
        Center = Vec.Lerp(Focus1.Position, Focus2.Position, 0.5);
        A = sum!.Value / 2;
        B = Math.Sqrt(Math.Max(0, A * A - c * c));
        // turned on without a jump (179° → 181°, not → −179°): the player goes smoothly from one to the next
        if (c > 1e-12)
        {
            double turn = between.AngleDegrees - Rotation;
            Rotation += turn - 360 * Math.Round(turn / 360);
        }
        if (through != null)
        {
            StartParameter = ParameterOf(through.Position);
        }
    }

    internal override void Render(RenderContext context, Span span)
    {
        if (curve == null)
        {
            IsDrawn = true;
            // "fill": the inside below, with the fills of polygons and angles; it grows with the line
            if (Options.Filled)
            {
                disc = context.Scene.Create(this, "ellipsedisc", Layers.Of(Options, Layers.Polygon));
            }
            curve = context.Scene.Create(this, "ellipse", Layers.Of(Options, Layers.Curve));
        }
        foreach (Primitive primitive in disc != null ? [disc, curve] : new[] { curve })
        {
            primitive.Animate("opacity", span, Visibility);
            if (IsDefined)
            {
                primitive.Animate("cx", span, Center.X);
                primitive.Animate("cy", span, Center.Y);
                primitive.Animate("rx", span, A);
                primitive.Animate("ry", span, B);
                primitive.Animate("rot", span, Rotation);
                primitive.Animate("start", span, StartParameter);
            }
            primitive.Animate("sweep", span, 360 * Reveal);
        }
        if (disc != null)
        {
            disc.Animate("fill", span, FillOr(Defaults.LineColor).ToHex());
            disc.Animate("fillOpacity", span, FillOpacityOr(Defaults.FillOpacity));
        }
        curve.Animate("color", span, ColorOr(Defaults.LineColor).ToHex());
        curve.Animate("width", span, WidthOr(Defaults.LineWidth));
    }
}
