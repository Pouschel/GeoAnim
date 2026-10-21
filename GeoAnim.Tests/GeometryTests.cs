namespace GeoAnim.Tests;

public class GeometryTests
{
    private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 6);

    private static void Near(Vec expected, Vec actual)
    {
        Near(expected.X, actual.X);
        Near(expected.Y, actual.Y);
    }

    [Fact]
    public void TwoCirclesFirstIntersectionIsLeftOfTheCentreLine()
    {
        var c = new Construction();
        GeoPoint a = c.Point("A", 0, 0);
        GeoPoint b = c.Point("B", 4, 0);
        GeoCircle k1 = c.Circle("k1", a, b);
        GeoCircle k2 = c.Circle("k2", b, a);
        var (s, t) = c.Intersect("S", "T", k1, k2);

        // left of the direction A → B is above
        Near(new Vec(2, Math.Sqrt(12)), s.Position);
        Near(new Vec(2, -Math.Sqrt(12)), t.Position);
    }

    [Fact]
    public void CirclesThatDoNotMeetHaveNoIntersection()
    {
        var c = new Construction();
        GeoPoint a = c.Point("A", 0, 0);
        GeoPoint b = c.Point("B", 9, 0);
        GeoCircle k1 = c.Circle("k1", a, 2);
        GeoCircle k2 = c.Circle("k2", b, 3);
        var error = Assert.Throws<GeoException>(() => c.Intersect(["S"], k1, k2));
        Assert.Equal("k1 and k2 do not intersect.", error.Message);
    }

    [Fact]
    public void TouchingCirclesHaveOneIntersection()
    {
        var c = new Construction();
        GeoCircle k1 = c.Circle("k1", c.Point("A", 0, 0), 2);
        GeoCircle k2 = c.Circle("k2", c.Point("B", 5, 0), 3);
        Near(new Vec(2, 0), c.Intersect(["S"], k1, k2)[0].Position);
    }

    [Fact]
    public void LineAndCircleInTheDirectionOfTheLine()
    {
        var c = new Construction();
        GeoCircle k = c.Circle("k", c.Point("M", 0, 0), 5);
        GeoStraight g = c.Line("g", c.Point("P", -1, 3), c.Point("Q", 1, 3));
        IReadOnlyList<GeoPoint> points = c.Intersect(["S", "T"], g, k);
        Near(new Vec(-4, 3), points[0].Position);
        Near(new Vec(4, 3), points[1].Position);
    }

    [Fact]
    public void SegmentIntersectsOnlyBetweenItsEnds()
    {
        var c = new Construction();
        GeoPoint a = c.Point("A", 0, 0);
        GeoStraight s = c.Segment("s", a, c.Point("B", 1, 0));
        GeoStraight g = c.Line("g", c.Point("C", 3, -1), c.Point("D", 3, 1));
        Assert.Throws<GeoException>(() => c.Intersect(["X"], s, g));
        GeoStraight l = c.Line("l", a, c.Point("E", 2, 0));
        Near(new Vec(3, 0), c.Intersect(["Y"], l, g)[0].Position);
    }

    [Fact]
    public void ParallelLinesDoNotIntersect()
    {
        var c = new Construction();
        GeoStraight g = c.Line("g", c.Point("A", 0, 0), c.Point("B", 1, 1));
        GeoStraight h = c.Line("h", c.Point("C", 0, 1), c.Point("D", 1, 2));
        Assert.Throws<GeoException>(() => c.Intersect(["S"], g, h));
    }

    [Fact]
    public void LinesAreCutSlightlyOutsideTheView()
    {
        var view = new ViewWindow(0, 10, 0, 5);
        var clip = view.Clip(new Vec(5, 2), new Vec(1, 0))!.Value;
        // 5 % margin: x from -0.5 to 10.5
        Near(-5.5, clip.TMin);
        Near(5.5, clip.TMax);
        Assert.Null(view.Clip(new Vec(0, 20), new Vec(1, 0)));
    }

    [Fact]
    public void PointsOnCirclesAndStraights()
    {
        var c = new Construction();
        GeoPoint m = c.Point("M", 1, 1);
        GeoCircle k = c.Circle("k", m, 2);
        Near(new Vec(1, 3), c.PointOn("P", k, 90).Position);
        GeoStraight s = c.Segment("s", c.Point("A", 0, 0), c.Point("B", 4, 2));
        Near(new Vec(1, 0.5), c.PointOn("Q", s, 0.25).Position);
        Assert.Throws<GeoException>(() => c.PointOn("R", s, 1.5));
    }

    [Fact]
    public void ColorsByNameAndHex()
    {
        Assert.Equal("#d32f2f", Color.Parse("red").ToHex());
        Assert.Equal("#112233", Color.Parse("#123").ToHex());
        Assert.Equal("#a0b1c2", Color.Parse("#A0B1C2").ToHex());
        Assert.Throws<GeoException>(() => Color.Parse("reddish"));
    }
}
