using System.Text.Json;
using GeoAnim.Figures;
using GeoAnim.Script;

namespace GeoAnim.Tests;

public class HalfPlaneTests
{
    [Fact]
    public void TheSideOfALineWithAPoint()
    {
        var c = new Construction();
        GeoPoint a = c.Point("A", 0, 0), b = c.Point("B", 2, 0), p = c.Point("P", 1, 3);
        GeoStraight g = c.Line("g", a, b);
        GeoHalfPlane side = c.HalfPlane("H", g, p);
        GeoHalfPlane other = c.HalfPlane("K", g, p, opposite: true);
        Assert.True(side.Contains(new Vec(5, 1)));
        Assert.False(side.Contains(new Vec(5, -1)));
        Assert.True(other.Contains(new Vec(-5, -1)));
        Assert.Equal(0, side.Normal.X, 9);
        Assert.Equal(1, side.Normal.Y, 9);

        // P on the line: no side, no half-plane
        Assert.Throws<GeoException>(() => c.HalfPlane("L", g, c.Point("Q", 7, 0)));
    }

    [Fact]
    public void AHalfPlaneIsAnAreaToTheEdgesThatDoesNotCountForTheCut()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            view x -10 .. 10 y -10 .. 10
            point A = 0 0
            point B = 2 0
            point P = 1 3
            segment s = A B
            halfplane H = s side P fill blue opacity 0.3
            halfplane K = s opposite P fill red
            """);
        JsonElement[] areas = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray()
            .Where(p => p.GetProperty("type").GetString() == "halfplane").ToArray();
        Assert.Equal(2, areas.Length);
        Assert.True(areas[0].GetProperty("unbounded").GetBoolean());
        Assert.Equal(-1, areas[1].GetProperty("tracks").GetProperty("ny").EnumerateArray().Last()[1].GetDouble(), 9);

        // in print: the upper half of the view – but the picture is cut to the segment, not to the half-planes
        FigureImage image = FigureRenderer.Render(FigureAnimation.Parse(animation.ToJson()), animation.Duration, FigureStyle.Default, [animation.Duration]);
        Assert.Contains("fill=\"#1565c0\" fill-opacity=\"0.3\"", image.Svg);
        Assert.True(image.Width < 10 * FigureStyle.Default.PointsPerUnit);

        ScriptError error = Assert.Throws<ScriptError>(() => ScriptCompiler.Compile("point A = 0 0\npoint B = 1 0\nline g = A B\nhalfplane H = g left"));
        Assert.Contains("\"side <point>\"", error.Message);
        error = Assert.Throws<ScriptError>(() => ScriptCompiler.Compile("point A = 0 0\npoint B = 1 0\ncircle k = center A through B\nhalfplane H = k side A"));
        Assert.Contains("not a line", error.Message);
        // "side" and "opposite" are words of the half-plane only
        ScriptCompiler.Compile("point side = 0 0\npoint opposite = 1 0\nline g = side opposite\npoint P = 0 1\nhalfplane H = g side P");
    }
}
