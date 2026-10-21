namespace GeoAnim;

/// <summary>A point or vector in mathematical coordinates (y axis pointing up).</summary>
public readonly record struct Vec(double X, double Y)
{
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec operator *(double k, Vec a) => new(k * a.X, k * a.Y);

    public static Vec operator *(Vec a, double k) => new(k * a.X, k * a.Y);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public static double Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y;

    /// <summary>The z component of the cross product: positive if <paramref name="b"/> lies to the left of <paramref name="a"/>.</summary>
    public static double Cross(Vec a, Vec b) => a.X * b.Y - a.Y * b.X;

    public static Vec Lerp(Vec a, Vec b, double t) => a + t * (b - a);

    /// <summary>The direction of the vector in degrees, counterclockwise from the positive x axis (0 to 360).</summary>
    public double AngleDegrees
    {
        get
        {
            double angle = Math.Atan2(Y, X) * 180 / Math.PI;
            return angle < 0 ? angle + 360 : angle;
        }
    }

    public override string ToString() => FormattableString.Invariant($"({X}, {Y})");
}
