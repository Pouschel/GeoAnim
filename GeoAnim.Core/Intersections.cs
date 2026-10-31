namespace GeoAnim;

/// <summary>
/// Intersections of segments, lines, rays, circles and ellipses. The order of two intersections stays the same while points
/// move: for a straight and a circle or ellipse along the direction A → B, for two circles first the one to the left of
/// the direction from the first centre to the second; a circle and an ellipse (up to four) in the order of the ellipse's
/// parameter, from the vertex beyond its second focus counterclockwise.
/// </summary>
internal static class Intersections
{
    private const double Epsilon = 1e-9;

    /// <summary>The number of intersections two objects of these kinds can have (1 or 2), or 0 if they cannot intersect.</summary>
    public static int MaxCount(GeoObject a, GeoObject b) => (a, b) switch
    {
        (GeoStraight, GeoStraight) => 1,
        (GeoStraight, GeoCircle) or (GeoCircle, GeoStraight) or (GeoCircle, GeoCircle) => 2,
        (GeoStraight, GeoEllipse) or (GeoEllipse, GeoStraight) => 2,
        (GeoCircle, GeoEllipse) or (GeoEllipse, GeoCircle) => 4,
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
            (GeoStraight s, GeoEllipse e) => StraightEllipse(s, e, index),
            (GeoEllipse e, GeoStraight s) => StraightEllipse(s, e, index),
            (GeoCircle c, GeoEllipse e) => CircleEllipse(c, e, index),
            (GeoEllipse e, GeoCircle c) => CircleEllipse(c, e, index),
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

    // in the ellipse's coordinates stretched across to a circle of radius a: as with a circle
    private static Vec? StraightEllipse(GeoStraight s, GeoEllipse e, int index)
    {
        if (e.B < Epsilon)
        {
            return null;
        }
        double stretch = e.A / e.B;
        Vec p = e.ToLocal(s.A.Position);
        Vec q = e.ToLocal(s.A.Position + s.Direction);
        p = new Vec(p.X, p.Y * stretch);
        Vec d = new Vec(q.X, q.Y * stretch) - p;
        double a = Vec.Dot(d, d);
        double b = 2 * Vec.Dot(p, d);
        double cc = Vec.Dot(p, p) - e.A * e.A;
        double discriminant = b * b - 4 * a * cc;
        if (discriminant < -Epsilon * a)
        {
            return null;
        }
        double root = Math.Sqrt(Math.Max(0, discriminant));
        double t = index == 0 ? (-b - root) / (2 * a) : (-b + root) / (2 * a);
        return InRange(s, t) ? s.A.Position + t * s.Direction : null;
    }

    // the parameters where the squared distance from the centre minus r² changes its sign (an equation of degree four:
    // found by sampling and halving, far more exactly than drawing needs); a circle touching the ellipse may be missed
    private static Vec? CircleEllipse(GeoCircle c, GeoEllipse e, int index)
    {
        Vec m = c.Center.Position;
        double r2 = c.Radius * c.Radius;
        double F(double degrees)
        {
            Vec d = e.PointAt(degrees) - m;
            return Vec.Dot(d, d) - r2;
        }
        const int samples = 720;
        int found = -1;
        double previous = F(0);
        for (int i = 1; i <= samples; i++)
        {
            double t0 = 360.0 * (i - 1) / samples, t1 = 360.0 * i / samples;
            double value = F(t1);
            if (previous == 0 || (previous < 0) != (value < 0) && value != 0)
            {
                if (++found == index)
                {
                    double low = t0, high = t1, atLow = previous;
                    for (int k = 0; k < 60 && atLow != 0; k++)
                    {
                        double middle = (low + high) / 2;
                        double atMiddle = F(middle);
                        if ((atMiddle < 0) == (atLow < 0))
                        {
                            (low, atLow) = (middle, atMiddle);
                        }
                        else
                        {
                            high = middle;
                        }
                    }
                    return e.PointAt(atLow == 0 ? low : (low + high) / 2);
                }
            }
            previous = value;
        }
        return null;
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
