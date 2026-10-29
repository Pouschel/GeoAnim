using System.Globalization;
using System.Text.RegularExpressions;
using GeoAnim.Cli;
using GeoAnim.Figures;
using GeoAnim.Script;

namespace GeoAnim.Tests;

public sealed class FigureTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("geoanim-figure").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static FigureAnimation Animation(string script) => FigureAnimation.Parse(ScriptCompiler.Compile(script).ToJson());

    private static double Number(string svg, string attribute) =>
        double.Parse(Regex.Match(svg, attribute + "=\"([0-9.]+)pt\"").Groups[1].Value, CultureInfo.InvariantCulture);

    [Fact]
    public void LengthsAreReadInPoints()
    {
        Assert.Equal(72, FigureStyle.ParseLength("1in"));
        Assert.Equal(9, FigureStyle.ParseLength("9pt"));
        Assert.Equal(9, FigureStyle.ParseLength("9"));
        Assert.Equal(FigureStyle.Millimetres(5), FigureStyle.ParseLength("0.5cm")!.Value, 6);
        Assert.Null(FigureStyle.ParseLength("5 Meter"));
        Assert.Null(FigureStyle.ParseLength("-5mm"));
    }

    [Fact]
    public void TheWholeViewHasItsSizeInPoints()
    {
        FigureAnimation animation = Animation("view x -5 .. 5 y -2 .. 2\npoint A = 0 0\nstep Eins");
        FigureImage image = FigureRenderer.Render(animation, animation.EndOf(0), new FigureStyle(10, 0.25));
        Assert.Equal(100, image.Width, 6);
        Assert.Equal(40, image.Height, 6);
        Assert.Contains("width=\"100pt\" height=\"40pt\" viewBox=\"0 0 100 40\"", image.Svg);
        // everything is a path: no text, no font
        Assert.DoesNotContain("<text", image.Svg);
        Assert.Contains("<path", image.Svg);
    }

    [Fact]
    public void AFigureIsCutToWhatItShowsWithAMargin()
    {
        FigureAnimation animation = Animation("view x -10 .. 10 y -10 .. 10\npoint A = 0 0 nolabel\npoint B = 4 0 nolabel\nsegment s = A B\nstep Eins");
        var style = new FigureStyle(10, 0.25);
        double end = animation.EndOf(0);
        FigureImage image = FigureRenderer.Render(animation, end, style, [end]);
        // 4 units wide plus the points and a margin of at least 16 sizes (4 pt) on each side
        Assert.InRange(image.Width, 40 + 8, 40 + 20);
        Assert.InRange(image.Height, 8, 20);
        Assert.True(image.Width < FigureRenderer.Render(animation, end, style).Width);
    }

    [Fact]
    public void RaysAndLinesDoNotWidenTheFigure()
    {
        FigureAnimation animation = Animation("view x -10 .. 10 y -10 .. 10\npoint A = 0 0 nolabel\npoint B = 2 0 nolabel\nline g = A B\nstep Eins");
        double end = animation.EndOf(0);
        FigureImage image = FigureRenderer.Render(animation, end, new FigureStyle(10, 0.25), [end]);
        Assert.True(image.Width < 100, $"{image.Width} pt: the line widened the figure");
    }

    [Fact]
    public void TheStillsOfARowAreCutAlike()
    {
        FigureAnimation animation = Animation("view x -10 .. 10 y -10 .. 10\nstep Eins\npoint A = 0 0 nolabel\nstep Zwei\npoint B = 6 3 nolabel");
        var style = new FigureStyle(10, 0.25);
        double[] times = [animation.EndOf(0), animation.EndOf(1)];
        FigureImage first = FigureRenderer.Render(animation, times[0], style, times);
        FigureImage second = FigureRenderer.Render(animation, times[1], style, times);
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.True(first.Width > 60);
        // alone, the first would be small
        Assert.True(FigureRenderer.Render(animation, times[0], style, [times[0]]).Width < 20);
    }

    [Fact]
    public void LabelsGrowWithTheSizeFactor()
    {
        FigureAnimation animation = Animation("view x -10 .. 10 y -10 .. 10\ntext t = \"Kreis um $M$\" at 0 0\nstep Eins");
        double end = animation.EndOf(0);
        double small = FigureRenderer.Render(animation, end, new FigureStyle(10, 0.2), [end]).Width;
        double large = FigureRenderer.Render(animation, end, new FigureStyle(10, 0.4), [end]).Width;
        Assert.InRange(large / small, 1.8, 2.2);
    }

    [Fact]
    public void TheFigureCommandTakesItsSizeFromTheBook()
    {
        File.WriteAllText(Path.Combine(folder, "index.yaml"), "title: Buch\nprint:\n  scale: 1cm\n  labels: 10pt\n  labelsize: 20\n");
        string chapter = Directory.CreateDirectory(Path.Combine(folder, "10_Kapitel")).FullName;
        FigureStyle style = Program.StyleOf(chapter);
        Assert.Equal(FigureStyle.Millimetres(10), style.PointsPerUnit, 6);
        Assert.Equal(0.5, style.SizeFactor, 6);
        Assert.Equal(FigureStyle.Default, Program.StyleOf(Directory.CreateTempSubdirectory("geoanim-nobook").FullName));
    }

    [Fact]
    public void TheFigureCommandWritesStillsAndRows()
    {
        string script = Path.Combine(folder, "a.geo");
        File.WriteAllText(script, "view x -5 .. 5 y -5 .. 5\npoint A = 0 0\nstep Eins #eins\npoint B = 2 2\nstep Zwei\npoint C = -2 2\nstep Drei");
        string output = Path.Combine(folder, "bild.svg");
        Assert.Equal(0, Program.Figure([script + "#eins", "-o", output]));
        Assert.True(File.Exists(output));
        Assert.Equal(0, Program.Figure([script + "#2..3", "-o", output]));
        string second = File.ReadAllText(Path.Combine(folder, "bild-2.svg"));
        string third = File.ReadAllText(Path.Combine(folder, "bild-3.svg"));
        Assert.Equal(Number(second, "width"), Number(third, "width"));
        Assert.Equal(0, Program.Figure([script]));
        Assert.True(File.Exists(Path.Combine(folder, "a.svg")));
        Assert.Equal(1, Program.Figure([script + "#vier"]));
    }
}
