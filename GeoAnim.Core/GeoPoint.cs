namespace GeoAnim;

/// <summary>A point: free (with coordinates, can move), the midpoint of two points, or an intersection.</summary>
public sealed class GeoPoint : GeoObject
{
    private readonly Func<GeoPoint, Vec?> compute;
    private Vec position;
    private Primitive? dot;
    private Primitive? label;

    private GeoPoint(string name, Options options, Func<GeoPoint, Vec?> compute, bool isFree, params GeoObject[] parents)
        : base(name, options, parents)
    {
        this.compute = compute;
        IsFree = isFree;
    }

    /// <summary>For a point on a circle its angle in degrees, for a point on a straight its parameter (0 = A, 1 = B).</summary>
    internal double Parameter { get; private set; }

    /// <summary>The circle or straight a point "on" an object belongs to, otherwise null.</summary>
    public GeoObject? Path { get; private init; }

    public override string Kind => "point";

    /// <summary>Whether the point has its own coordinates (only free points can move).</summary>
    public bool IsFree { get; }

    /// <summary>The current position (undefined points keep their last one).</summary>
    public Vec Position => position;

    internal override double DefaultDuration => 400;

    internal override Easing DefaultEasing => Easing.EaseOut;

    internal static GeoPoint Free(string name, Vec position, Options options)
    {
        var point = new GeoPoint(name, options, p => p.position, isFree: true);
        point.position = position;
        return point;
    }

    /// <summary>A point on a circle at <paramref name="angle"/> degrees; it can move along the circle.</summary>
    internal static GeoPoint OnCircle(string name, GeoCircle circle, double angle, Options options)
    {
        var point = new GeoPoint(name, options, p =>
        {
            if (!circle.IsDefined)
            {
                return null;
            }
            double radians = p.Parameter * Math.PI / 180;
            return circle.Center.Position + circle.Radius * new Vec(Math.Cos(radians), Math.Sin(radians));
        }, false, circle) { Path = circle };
        point.Parameter = angle;
        return point;
    }

    /// <summary>A point A + t·(B − A) on a segment, line or ray; it can move along it.</summary>
    internal static GeoPoint OnStraight(string name, GeoStraight straight, double t, Options options)
    {
        (double min, double max) = straight.ParameterRange;
        if (t < min || t > max)
        {
            throw new GeoException($"The position {t} is not on the {straight.Kind} {straight.Name} (allowed: {min} to {max}).");
        }
        var point = new GeoPoint(name, options, p => straight.IsDefined ? straight.A.Position + p.Parameter * straight.Direction : null, false, straight)
        {
            Path = straight,
        };
        point.Parameter = t;
        return point;
    }

    internal void MoveAlong(double parameter) => Parameter = parameter;

    internal static GeoPoint Midpoint(string name, GeoPoint a, GeoPoint b, Options options) =>
        new(name, options, _ => a.IsDefined && b.IsDefined ? Vec.Lerp(a.position, b.position, 0.5) : null, false, a, b);

    internal static GeoPoint Intersection(string name, GeoObject a, GeoObject b, int index, Options options) =>
        new(name, options, _ => Intersections.Find(a, b, index), false, a, b);

    internal void MoveTo(Vec target) => position = target;

    internal override void Compute()
    {
        Vec? value = compute(this);
        IsDefined = value.HasValue;
        if (value.HasValue)
        {
            position = value.Value;
        }
    }

    public override bool HasLabel => true;

    // the name as a formula: italic, P_1 with subscript, A' with prime
    internal string LabelText => Options.Label ?? "$" + Name + "$";

    internal override void Render(RenderContext context, Span span)
    {
        if (dot == null)
        {
            IsDrawn = true;
            dot = context.Scene.Create("dot", Layers.Of(Options, Layers.Point));
        }
        if (label == null && LabelUsed)
        {
            label = context.Scene.Create("text", Layers.Label);
        }
        Color color = ColorOr(Defaults.PointColor);
        double visibility = Visibility;
        dot.Animate("opacity", span, visibility);
        if (IsDefined)
        {
            dot.Animate("x", span, position.X);
            dot.Animate("y", span, position.Y);
        }
        dot.Animate("r", span, (Options.Size ?? Defaults.PointSize) * Reveal * (1 + 0.6 * Highlight));
        dot.Animate("color", span, color.ToHex());
        if (label != null)
        {
            WriteLabel(label, span);
            label.Animate("opacity", span, visibility * Reveal * LabelOpacity);
            if (Options.Halo)
            {
                label.Animate("halo", span, 1.0);
            }
            label.Animate("color", span, color.ToHex());
        }
    }

    /// <summary>The label's text and place (also for the ghost of a point in an editor, whose label may not be shown yet).</summary>
    internal void WriteLabel(Primitive label, Span span)
    {
        label.Animate("text", span, LabelText);
        label.Animate("anchor", span, "start");
        // an offset is in coordinates; the default distance is in points
        Vec offset = Options.Offset ?? new Vec(0, 0);
        Vec distance = Options.Offset == null ? Defaults.LabelOffset : new Vec(0, 0);
        if (IsDefined)
        {
            label.Animate("x", span, position.X + offset.X);
            label.Animate("y", span, position.Y + offset.Y);
        }
        label.Animate("dx", span, distance.X);
        label.Animate("dy", span, distance.Y);
        label.Animate("size", span, Options.LabelSize ?? Defaults.LabelSize);
        RenderShift(label, span);
    }
}
