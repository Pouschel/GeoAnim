using System.Text.Json;
using GeoAnim.Figures;
using GeoAnim.Script;

namespace GeoAnim.Tests;

public class EllipseTests
{
    private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 6);

    private static void Near(Vec expected, Vec actual)
    {
        Near(expected.X, actual.X);
        Near(expected.Y, actual.Y);
    }

    // the distances of a point to the foci add up to the sum
    private static void OnEllipse(GeoEllipse e, Vec p) =>
        Near(2 * e.A, (p - e.Focus1.Position).Length + (p - e.Focus2.Position).Length);

    [Fact]
    public void FociAndSumGiveTheAxesAndTheTurn()
    {
        var c = new Construction();
        GeoEllipse e = c.Ellipse("e", c.Point("F", -3, 1), c.Point("G", 3, 1), 10);
        Near(5, e.A);
        Near(4, e.B);
        Near(0, e.Rotation);
        Near(new Vec(5, 1), e.PointAt(0));
        Near(new Vec(0, 5), e.PointAt(90));

        // turned as the foci lie
        GeoEllipse turned = c.Ellipse("t", c.Point("P", 0, 0), c.Point("Q", 2, 2), c.Point("R", 3, -1));
        Near(45, turned.Rotation);
        OnEllipse(turned, new Vec(3, -1));
        for (int degrees = 0; degrees < 360; degrees += 30)
        {
            OnEllipse(turned, turned.PointAt(degrees));
        }
    }

    [Fact]
    public void ASumShorterThanTheFociDistanceIsNoEllipse()
    {
        var c = new Construction();
        GeoException error = Assert.Throws<GeoException>(() => c.Ellipse("e", c.Point("F", 0, 0), c.Point("G", 6, 0), 4));
        Assert.Contains("cannot be constructed", error.Message);
        // as long as the foci: the segment between them
        GeoEllipse flat = c.Ellipse("f", c.Point("A", 0, 0), c.Point("B", 6, 0), 6);
        Assert.True(flat.IsDefined);
        Near(0, flat.B);
    }

    [Fact]
    public void ALineMeetsTheEllipseTwiceInItsDirection()
    {
        var c = new Construction();
        GeoEllipse e = c.Ellipse("e", c.Point("F", -3, 0), c.Point("G", 3, 0), 10);
        GeoStraight g = c.Line("g", c.Point("P", 0, -1), c.Point("Q", 0, 1));
        IReadOnlyList<GeoPoint> points = c.Intersect(["S", "T"], g, e);
        Near(new Vec(0, -4), points[0].Position);
        Near(new Vec(0, 4), points[1].Position);
        // a segment inside meets it nowhere
        GeoStraight inside = c.Segment("s", c.Point("A", -1, 0), c.Point("B", 1, 0));
        Assert.Throws<GeoException>(() => c.Intersect(["U"], inside, e));
    }

    [Fact]
    public void ACircleMeetsTheEllipseUpToFourTimes()
    {
        var c = new Construction();
        GeoEllipse e = c.Ellipse("e", c.Point("F", -3, 0), c.Point("G", 3, 0), 10);
        GeoCircle k = c.Circle("k", c.Point("M", 0, 0), 4.5);
        IReadOnlyList<GeoPoint> points = c.Intersect(["S", "T", "U", "V"], k, e);
        foreach (GeoPoint point in points)
        {
            OnEllipse(e, point.Position);
            Near(4.5, point.Position.Length);
        }
        // in the order of the parameter: the first in the first quadrant
        Assert.True(points[0].Position.X > 0 && points[0].Position.Y > 0);
        Assert.True(points[1].Position.X < 0 && points[1].Position.Y > 0);
        // around a focus only two
        GeoCircle around = c.Circle("j", c.Point("H", 3, 0), 3);
        GeoException error = Assert.Throws<GeoException>(() => c.Intersect(["A", "B", "C"], around, e));
        Assert.Contains("only 2 intersections", error.Message);
    }

    [Fact]
    public void TheScriptDefinesEllipsesPointsOnThemAndTheirArea()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            view x -6 .. 6 y -4 .. 4
            point F = -3 0
            point G = 3 0
            ellipse e = foci F G sum 10 fill
            point P = 0 2
            ellipse f = foci F G through P color red
            point Q = on e at 90
            number a = area e
            text t = "{a:2}" at 0 0
            step Bewegen
            move Q to 180
            """);
        JsonElement primitives = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives");
        Assert.Equal(2, primitives.EnumerateArray().Count(p => p.GetProperty("type").GetString() == "ellipse"));
        Assert.Single(primitives.EnumerateArray(), p => p.GetProperty("type").GetString() == "ellipsedisc");
        Assert.Equal("62,83", animation.WithFinalNumbers("{a:2}"));

        // in print: the ellipse as an arc of the path
        string svg = FigureRenderer.Render(FigureAnimation.Parse(animation.ToJson()), animation.Duration, FigureStyle.Default).Svg;
        Assert.Matches(@"<path d=""M[^""]*A[0-9.]+ [0-9.]+ -?0 1 0 [^""]*A", svg);

        ScriptError error = Assert.Throws<ScriptError>(() => ScriptCompiler.Compile("point F = 0 0\npoint G = 1 0\nellipse e = foci F G radius 3"));
        Assert.Contains("\"sum <number>\"", error.Message);
    }
}
