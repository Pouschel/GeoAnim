namespace GeoAnim;

/// <summary>
/// The visible part of the plane in mathematical coordinates. The player keeps its aspect ratio, so the same scale
/// applies in both directions and circles stay round.
/// </summary>
public sealed record ViewWindow(double XMin, double XMax, double YMin, double YMax)
{
    /// <summary>The default window: x from -1 to 11, y from -1 to 7 (3:2).</summary>
    public static ViewWindow Default { get; } = new(-1, 11, -1, 7);

    public double Width => XMax - XMin;

    public double Height => YMax - YMin;

    /// <summary>Checks the window; throws <see cref="GeoException"/> if it is empty or extremely flat.</summary>
    public void Validate()
    {
        if (!(XMax > XMin) || !(YMax > YMin))
        {
            throw new GeoException("The view needs x min < x max and y min < y max.");
        }
        double ratio = Width / Height;
        if (ratio > 10 || ratio < 0.1)
        {
            throw new GeoException("The view is too flat (width : height must be between 1 : 10 and 10 : 1).");
        }
    }

    /// <summary>
    /// The part of the line through <paramref name="point"/> in direction <paramref name="direction"/> inside the window,
    /// as parameters of point + t·direction (Liang–Barsky); null if the line misses the window.
    /// </summary>
    internal (double TMin, double TMax)? Clip(Vec point, Vec direction, double tMin = double.NegativeInfinity, double tMax = double.PositiveInfinity)
    {
        // a little larger than the window, so that the ends of a line are never visible
        double marginX = Width * 0.05;
        double marginY = Height * 0.05;
        double[] p = [-direction.X, direction.X, -direction.Y, direction.Y];
        double[] q =
        [
            point.X - (XMin - marginX), XMax + marginX - point.X,
            point.Y - (YMin - marginY), YMax + marginY - point.Y,
        ];
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(p[i]) < 1e-12)
            {
                if (q[i] < 0)
                {
                    return null;
                }
                continue;
            }
            double r = q[i] / p[i];
            if (p[i] < 0)
            {
                tMin = Math.Max(tMin, r);
            }
            else
            {
                tMax = Math.Min(tMax, r);
            }
        }
        return tMin <= tMax ? (tMin, tMax) : null;
    }
}
