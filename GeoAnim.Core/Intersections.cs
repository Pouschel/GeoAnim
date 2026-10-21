namespace GeoAnim;

/// <summary>
/// Intersections of segments, lines, rays and circles. The order of two intersections stays the same while points move:
/// for a straight and a circle along the direction A → B, for two circles first the one to the left of the direction
/// from the first centre to the second.
/// </summary>
internal static class Intersections
{
    private const double Epsilon = 1e-9;

    /// <summary>The number of intersections two objects of these kinds can have (1 or 2), or 0 if they cannot intersect.</summary>
    public static int MaxCount(GeoObject a, GeoObject b) => (a, b) switch
    {
        (GeoStraight, GeoStraight) => 1,
        (GeoStraight, GeoCircle) or (GeoCircle, GeoStraight) or (GeoCircle, GeoCircle) => 2,
        _ => 0,
    };

    /// <summary>The intersection number <paramref name="index"/> (0 or 1) for the current positions, or null if there is none.</summary>
    public static Vec? Find(GeoObject a, GeoObject b, int index)
    {
        if (!a.IsDefined || !b.IsDefined)
        {
            return null;
        }
        return (a, b) switch
        {
            (GeoStraight s, GeoStraight t) => index == 0 ? StraightStraight(s, t) : null,
            (GeoStraight s, GeoCircle c) => StraightCircle(s, c, index),
            (GeoCircle c, GeoStraight s) => StraightCircle(s, c, index),
            (GeoCircle c, GeoCircle d) => CircleCircle(c, d, index),
            _ => null,
        };
    }

    private static bool InRange(GeoStraight s, double t)
    {
        (double min, double max) = s.ParameterRange;
        return t >= min - 1e-9 && t <= max + 1e-9;
    }

    private static Vec? StraightStraight(GeoStraight s, GeoStraight t)
    {
        Vec p = s.A.Position;
        Vec r = s.Direction;
        Vec q = t.A.Position;
        Vec d = t.Direction;
        double denominator = Vec.Cross(r, d);
        if (Math.Abs(denominator) < Epsilon * r.Length * d.Length)
        {
            return null; // parallel
        }
        double u = Vec.Cross(q - p, d) / denominator;
        double v = Vec.Cross(q - p, r) / denominator;
        return InRange(s, u) && InRange(t, v) ? p + u * r : null;
    }

    private static Vec? StraightCircle(GeoStraight s, GeoCircle c, int index)
    {
        Vec p = s.A.Position;
        Vec d = s.Direction;
        Vec f = p - c.Center.Position;
        double a = Vec.Dot(d, d);
        double b = 2 * Vec.Dot(f, d);
        double cc = Vec.Dot(f, f) - c.Radius * c.Radius;
        double discriminant = b * b - 4 * a * cc;
        if (discriminant < -Epsilon * a)
        {
            return null;
        }
        double root = Math.Sqrt(Math.Max(0, discriminant));
        double t = index == 0 ? (-b - root) / (2 * a) : (-b + root) / (2 * a);
        return InRange(s, t) ? p + t * d : null;
    }

    private static Vec? CircleCircle(GeoCircle c1, GeoCircle c2, int index)
    {
        Vec p1 = c1.Center.Position;
        Vec p2 = c2.Center.Position;
        Vec between = p2 - p1;
        double distance = between.Length;
        double r1 = c1.Radius;
        double r2 = c2.Radius;
        if (distance < Epsilon || distance > r1 + r2 + Epsilon || distance < Math.Abs(r1 - r2) - Epsilon)
        {
            return null;
        }
        double along = (distance * distance + r1 * r1 - r2 * r2) / (2 * distance);
        double height = Math.Sqrt(Math.Max(0, r1 * r1 - along * along));
        Vec unit = (1 / distance) * between;
        Vec foot = p1 + along * unit;
        Vec left = new(-unit.Y, unit.X);
        return index == 0 ? foot + height * left : foot - height * left;
    }
}
