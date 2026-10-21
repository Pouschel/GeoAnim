using System.Text.Json;
using GeoAnim.Script;

namespace GeoAnim.Tests;

public class ScriptTests
{
    private static ScriptError Error(string source) => Assert.Throws<ScriptError>(() => ScriptCompiler.Compile(source));

    private static void ErrorAt(string source, int line, int column, string messagePart)
    {
        ScriptError error = Error(source);
        Assert.True(error.Line == line && error.Column == column, $"Expected {line}:{column}, got {error}");
        Assert.Contains(messagePart, error.Message);
    }

    /// <summary>Every script in Samples, with subfolders (paths relative to Samples, with "/").</summary>
    private static List<string> SamplePaths()
    {
        string folder = Repository.PathOf("Samples");
        return Directory.EnumerateFiles(folder, "*.geo", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public static TheoryData<string> Samples() => new(SamplePaths());

    // a change of the language must be carried through all samples: each one compiles completely
    [Theory]
    [MemberData(nameof(Samples))]
    public void SamplesCompile(string sample)
    {
        AnimationData animation = ScriptCompiler.Compile(File.ReadAllText(Repository.PathOf(["Samples", .. sample.Split('/')])));
        JsonDocument.Parse(animation.ToJson());
    }

    [Fact]
    public void SamplesAreFound()
    {
        Assert.Contains("circumcircle.geo", SamplePaths());
    }

    [Fact]
    public void Tokens()
    {
        List<Token> tokens = Lexer.Tokenize("view x -1..11.5 # comment\npoint A' = 2.5 -.5 color #f80 label \"a \\\"b\\\"\"");
        Assert.Equal(
            ["view", "x", "-1", "..", "11.5", "\n", "point", "A'", "=", "2.5", "-.5", "color", "#f80", "label", "a \"b\"", "\n", ""],
            tokens.Select(t => t.Text));
        Assert.Equal(TokenKind.Color, tokens[12].Kind);
        Assert.Equal(TokenKind.String, tokens[14].Kind);
        Assert.Equal((2, 7), (tokens[7].Line, tokens[7].Column));
    }

    [Fact]
    public void TitleAndStepTakeTheRestOfTheLine()
    {
        List<Token> tokens = Lexer.Tokenize("title  Satz #1: \"a\" , (b)  \n  step\tKreis um A.\nstep");
        Assert.Equal(["title", "Satz #1: \"a\" , (b)", "\n", "step", "Kreis um A.", "\n", "step", "", "\n", ""], tokens.Select(t => t.Text));
        Assert.Equal((1, 8), (tokens[1].Line, tokens[1].Column));
        AnimationData animation = ScriptCompiler.Compile("title Satz #1\nstep Kreis um A.\n  pause");
        Assert.Equal("Satz #1", animation.Title);
        Assert.Equal("Kreis um A.", animation.Steps[0].Caption);
    }

    [Fact]
    public void OffsetIsInCoordinates()
    {
        JsonElement primitives = JsonDocument.Parse(ScriptCompiler.Compile("point A = 1 2 offset 0.5 -0.25\ntext t = \"x\" at A offset 1 1").ToJson())
            .RootElement.GetProperty("primitives");
        JsonElement[] texts = primitives.EnumerateArray().Where(p => p.GetProperty("type").GetString() == "text").ToArray();
        double First(JsonElement p, string track) => p.GetProperty("tracks").GetProperty(track)[0][1].GetDouble();
        Assert.Equal((1.5, 1.75, 0.0, 0.0), (First(texts[0], "x"), First(texts[0], "y"), First(texts[0], "dx"), First(texts[0], "dy")));
        Assert.Equal((2.0, 3.0, 0.0), (First(texts[1], "x"), First(texts[1], "y"), First(texts[1], "dx")));
    }

    [Fact]
    public void ScriptAndApiGiveTheSameAnimation()
    {
        string script = """
            title T
            view x 0 .. 10 y 0 .. 5
            grid 0.5
            axes
            point A = 1 1
            point B = 5 1 color red nolabel
            step one
              segment s = A B duration 600 linear
              circle k = center A through B hidden
            step two
              together
                show k duration 900
                move B to 6 2
              end
            """;
        var c = new Construction { Title = "T", View = new ViewWindow(0, 10, 0, 5), GridStep = 0.5, Axes = true };
        GeoPoint a = c.Point("A", 1, 1);
        GeoPoint b = c.Point("B", 5, 1, new Options { Color = Color.Parse("red"), NoLabel = true });
        c.Step("one");
        c.Segment("s", a, b, new Options { Duration = 600, Easing = Easing.Linear });
        GeoCircle k = c.Circle("k", a, b, new Options { Hidden = true });
        c.Step("two");
        c.Together(() =>
        {
            c.Show([k], duration: 900);
            c.Move(b, new Vec(6, 2));
        });
        Assert.Equal(c.Build().ToJson(), ScriptCompiler.Compile(script).ToJson());
    }

    [Fact]
    public void ShortFormNamesTheStraightAfterItsPoints()
    {
        var c = ScriptCompiler.CompileConstruction("point A = 0 0\npoint B = 1 0\nsegment A B\nray B A");
        Assert.IsType<GeoStraight>(c.Objects["AB"]);
        Assert.Equal(StraightKind.Ray, ((GeoStraight)c.Objects["BA"]).StraightKind);
    }

    [Fact]
    public void IntersectionsAndPointsOnObjects()
    {
        var c = ScriptCompiler.CompileConstruction("""
            point A = 0 0
            point B = 4 0
            circle k = center A radius 4
            point P = on k at 90
            segment AB = A B
            point Q = on AB at 0.5
            line g = A B
            point S T = intersect k g
            step s
              move P to 180
            """);
        Assert.Equal(new Vec(-4, 0), ((GeoPoint)c.Objects["S"]).Position);
        Assert.Equal(new Vec(4, 0), ((GeoPoint)c.Objects["T"]).Position);
        Assert.Equal(-4, ((GeoPoint)c.Objects["P"]).Position.X, 6);
    }

    [Fact]
    public void ErrorsHaveLineAndColumn()
    {
        ErrorAt("pont A = 1 2", 1, 1, "Unknown command \"pont\"");
        ErrorAt("point A = 1 2\nsegment s = A C", 2, 15, "Unknown object \"C\"");
        ErrorAt("point A = 1 2\ncircle k = center A radius 1\nsegment s = A k", 3, 15, "k is a circle, not a point");
        ErrorAt("point A = 1 2 width 3", 1, 15, "\"width\" is not possible for point");
        ErrorAt("point A = 1 2 label \"open", 1, 21, "no closing quote");
        ErrorAt("point A = (1, 2)", 1, 11, "without parentheses");
        ErrorAt("point A = 1 2\npoint B = 3 4\npoint S, T = intersect A B", 3, 8, "No commas");
        ErrorAt("point A = 1", 1, 12, "the y coordinate");
        ErrorAt("step a\ntogether\n  pause\n", 2, 1, "has no \"end\"");
        ErrorAt("end", 1, 1, "\"end\" without \"together\"");
        ErrorAt("step a\ntogether\nstep b\nend", 3, 1, "cannot begin inside");
        ErrorAt("point A = 1 2\npoint A = 2 2", 2, 7, "already used");
        ErrorAt("pause 100", 1, 1, "only possible inside a step");
        ErrorAt("point A = 1 2\nview x 0..1 y 0..1", 2, 1, "before the first object");
        ErrorAt("view x 5..1 y 0..1", 1, 1, "x min < x max");
        ErrorAt("point A = 0 0\npoint B = 9 0\ncircle a = center A radius 1\ncircle b = center B radius 1\npoint S = intersect a b", 5, 1, "do not intersect");
        ErrorAt("point A = 0 0\npoint B = midpoint A A\nstep s\n  move B to 1 1", 4, 3, "constructed from other objects");
        ErrorAt("point A = 0 0 color reddish", 1, 21, "Unknown colour");
        ErrorAt("point to = 0 0", 1, 7, "keyword \"to\"");
    }
}
