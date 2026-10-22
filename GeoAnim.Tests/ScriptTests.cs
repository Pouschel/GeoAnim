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
        AnimationData animation = ScriptCompiler.CompileFile(Repository.PathOf(["Samples", .. sample.Split('/')]));
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
    public void SpeedScalesAllTimes()
    {
        const string script = "point A = 0 0\npoint B = 1 0\nstep s\n  segment AB = A B duration 800\n  move B to 2 0";
        AnimationData normal = ScriptCompiler.Compile(script);
        AnimationData slow = ScriptCompiler.Compile("speed 0.5\n" + script);
        Assert.Equal(1800, normal.Duration);
        Assert.Equal(3600, slow.Duration);
        Assert.Equal((0.0, 3600.0), (slow.Steps[0].Start, slow.Steps[0].End));
        double LastKey(AnimationData a) => JsonDocument.Parse(a.ToJson()).RootElement.GetProperty("primitives").EnumerateArray()
            .SelectMany(p => p.GetProperty("tracks").EnumerateObject()).SelectMany(t => t.Value.EnumerateArray()).Max(k => k[0].GetDouble());
        Assert.Equal(2 * LastKey(normal), LastKey(slow));
        Assert.Equal(900, ScriptCompiler.Compile("speed 2\n" + script).Duration);
        ErrorAt("speed 0", 1, 7, "greater than 0");
        ErrorAt("step s\nspeed 2", 2, 1, "before the first step");
    }

    [Fact]
    public void VarsGiveGroupsTheSameOptions()
    {
        var c = ScriptCompiler.CompileConstruction("""
            var thin color gray width 1.5 duration 300
            var mark thin color red size 7
            point A = 0 0 mark
            point B = 4 0 mark color blue
            point C = 2 3 nolabel
            segment s = A B thin
            polygon P = A B C fill yellow thin
            """);
        // a point takes colour and size; width does not fit and is left out
        Assert.Equal((Color.Parse("red"), 7.0, 300.0), (c.Objects["A"].Options.Color, c.Objects["A"].Options.Size!.Value, c.Objects["A"].Options.Duration!.Value));
        Assert.Null(c.Objects["A"].Options.Width);
        Assert.Equal(Color.Parse("blue"), c.Objects["B"].Options.Color);
        Assert.Equal((Color.Parse("gray"), 1.5), (c.Objects["s"].Options.Color, c.Objects["s"].Options.Width!.Value));
        Assert.Equal(Color.Parse("yellow"), c.Objects["P"].Options.Fill);

        // a var only from its line on, and only with options
        ErrorAt("point A = 0 0 red\nvar red color red", 1, 15, "Unknown option or var \"red\"");
        ErrorAt("var u", 1, 6, "needs options");
        ErrorAt("var u color red point", 1, 17, "Unknown option or var");
        ErrorAt("var u blue", 1, 7, "a var holds options");
        // written directly, an option that does not fit is still an error
        ErrorAt("var u color red\npoint A = 0 0 u width 2", 2, 17, "\"width\" is not possible for point");
        // vars and objects have different names
        ErrorAt("point A = 0 0\nvar A color red", 2, 5, "already the name of an object");
        ErrorAt("var k color red\npoint A = 0 0\ncircle k = center A radius 1", 3, 8, "already the name of a var");
        ErrorAt("var var color red", 1, 5, "keyword");
    }

    [Fact]
    public void OneNameTakesTheIntersectionThatLiesOnTheRay()
    {
        // the ray from D through B leaves the circle around B once only, behind B
        var c = ScriptCompiler.CompileConstruction("""
            point D = 0 2
            point B = 0 0
            point C = 3 0
            ray r = D B
            circle k = center B through C
            point G = intersect r k
            """);
        Assert.Equal(new Vec(0, -3), ((GeoPoint)c.Objects["G"]).Position);
        Assert.Equal("Line 6, column 1: r and k have only one intersection; give one name.",
            Error("point D = 0 2\npoint B = 0 0\npoint C = 3 0\nray r = D B\ncircle k = center B through C\npoint G G' = intersect r k").ToString());
        // a segment inside the circle
        ErrorAt("point D = 0 5\npoint B = 0 4\ncircle k = center B radius 2\nsegment s = D B\npoint H = intersect s k", 5, 1, "do not intersect");
    }

    [Fact]
    public void TogetherWithDurationScalesItsActions()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point A = 0 0
            point C = 4 0
            step zusammen
              together duration 2500
                move A to -2 1
                move C to 4 2 duration 500
              end
            step warten
              together duration 300
              end
            """);
        // the longest move (1000 ms) becomes 2500 ms, the shorter one keeps its share (1250 ms)
        Assert.Equal([(0.0, 2500.0), (2500.0, 2800.0)], animation.Steps.Select(s => (s.Start, s.End)));
        JsonElement primitives = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives");
        double LastKey(JsonElement track) => track.EnumerateArray().Last()[0].GetDouble();
        // the dots of A and C: their x/y keys end when their moves end
        JsonElement[] dots = primitives.EnumerateArray().Where(p => p.GetProperty("type").GetString() == "dot").ToArray();
        Assert.Equal(2500, LastKey(dots[0].GetProperty("tracks").GetProperty("x")), 0);
        // (moves are computed every 40 ms: the last key is at the next step after 1250 ms)
        Assert.InRange(LastKey(dots[1].GetProperty("tracks").GetProperty("y")), 1250, 1291);
        ErrorAt("point A = 0 0\nstep s\ntogether color red\nend", 3, 10, "\"color\" is not possible for together");
    }

    [Fact]
    public void PointsMoveToWhereOtherPointsAre()
    {
        var c = ScriptCompiler.CompileConstruction("""
            point A = 0 0
            point B = 4 0
            point M = midpoint A B
            circle k = center A through B
            point P = on k at 90
            point C1 = 5 5
            point C2 = 6 5
            step zur Zielstrecke
              together duration 1500
                move C1 to M
                move C2 to P
              end
              move B to 6 0
            """);
        // C1 went to where M was then and stays there when M moves on
        Assert.Equal(new Vec(2, 0), ((GeoPoint)c.Objects["C1"]).Position);
        Assert.Equal(0, ((GeoPoint)c.Objects["C2"]).Position.X, 9);
        Assert.Equal(4, ((GeoPoint)c.Objects["C2"]).Position.Y, 9);
        ErrorAt("point A = 0 0\npoint B = 1 1\nstep s\n  move A to", 4, 12, "Expected coordinates like 2 3, an angle or position like 90, or a point");
        ErrorAt("point A = 0 0\ncircle k = center A radius 1\nstep s\n  move A to k", 4, 13, "k is a circle, not a point");
        ErrorAt("point A = 0 0\npoint B = 1 1\npoint M = midpoint A B\nstep s\n  move M to A", 5, 3, "constructed from other objects");
    }

    [Fact]
    public void CopiesAreFreePointsWhereTheOriginalIs()
    {
        var c = ScriptCompiler.CompileConstruction("""
            point A = 0 0
            point B = 4 0
            point M = midpoint A B
            point C = 0 5
            step zurück
              point Mx = copy M hidden
              point Bx = copy B hidden
              segment sx = Mx Bx
              together
                move Mx to C
                move Bx to 3 5
              end
            """);
        // the copies moved, the construction stayed
        Assert.Equal((new Vec(0, 5), new Vec(3, 5)), (((GeoPoint)c.Objects["Mx"]).Position, ((GeoPoint)c.Objects["Bx"]).Position));
        Assert.Equal((new Vec(2, 0), new Vec(4, 0)), (((GeoPoint)c.Objects["M"]).Position, ((GeoPoint)c.Objects["B"]).Position));
        ErrorAt("point A = 0 0\ncircle k = center A radius 1\npoint X = copy k", 3, 16, "k is a circle, not a point");
    }

    [Fact]
    public void EmptyTogetherBlocksTakeNoTime()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point A = 0 0
            point B = 1 0
            step leer
              together
              end
            step nur versteckt
              together
                circle k = center A through B hidden
              end
              segment s = A B duration 800
            """);
        // the first step is empty (500 ms by default), the second takes as long as the segment
        Assert.Equal([(0.0, 500.0), (500.0, 1300.0)], animation.Steps.Select(s => (s.Start, s.End)));
    }

    [Fact]
    public void DefaultsComeBeforeTheOptionsOfEachObject()
    {
        var c = ScriptCompiler.CompileConstruction("""
            default point size 3 labelsize 18 color navy
            default segment line ray circle width 1.5
            default color gray
            point A = 0 0
            point B = 4 0 size 6
            segment s = A B
            circle k = center A through B width 3
            text t = "x" at 1 1
            default point color red
            point C = 1 2
            """);
        Options a = c.Objects["A"].Options;
        // the default for all kinds comes first, then the one for the kind, then the object's own options
        Assert.Equal((3.0, 18.0, Color.Parse("navy")), (a.Size!.Value, a.LabelSize!.Value, a.Color));
        Assert.Equal(6.0, c.Objects["B"].Options.Size);
        Assert.Equal((1.5, Color.Parse("gray")), (c.Objects["s"].Options.Width!.Value, c.Objects["s"].Options.Color));
        Assert.Equal(3.0, c.Objects["k"].Options.Width);
        Assert.Equal(Color.Parse("gray"), c.Objects["t"].Options.Color);
        Assert.Null(c.Objects["t"].Options.Width);
        Assert.Equal(Color.Parse("red"), c.Objects["C"].Options.Color);
        ErrorAt("default point", 1, 14, "needs options");
        ErrorAt("default points size 3", 1, 9, "neither a kind of object");
    }

    [Fact]
    public void IncludeInsertsAnotherFile()
    {
        string folder = Directory.CreateTempSubdirectory("geoanim-include").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "lib"));
            File.WriteAllText(Path.Combine(folder, "lib", "styles.geo"), "default point size 3\nvar help color gray\ninclude more.geo");
            File.WriteAllText(Path.Combine(folder, "lib", "more.geo"), "# nothing but\ndefault segment width 1");
            string main = Path.Combine(folder, "main.geo");
            File.WriteAllText(main, "include lib/styles.geo\npoint A = 0 0\npoint B = 1 0\nsegment s = A B help");
            ScriptSource source = ScriptSource.FromFile(main);
            Assert.Equal([main, Path.Combine(folder, "lib", "styles.geo"), Path.Combine(folder, "lib", "more.geo")], source.Files);
            var c = ScriptCompiler.CompileConstruction(source);
            Assert.Equal((1.0, Color.Parse("gray")), (c.Objects["s"].Options.Width!.Value, c.Objects["s"].Options.Color));
            Assert.Equal(3.0, c.Objects["A"].Options.Size);

            // errors name the file and its line; errors of the script itself keep their line
            File.WriteAllText(Path.Combine(folder, "lib", "more.geo"), "default segment width 1\npont X = 1 1");
            Assert.Equal("lib/more.geo, line 2, column 1: Unknown command \"pont\".",
                Assert.Throws<ScriptError>(() => ScriptCompiler.CompileFile(main)).ToString());
            File.WriteAllText(Path.Combine(folder, "lib", "more.geo"), "");
            File.WriteAllText(main, "include lib/styles.geo\npoint A = 0 0\nsegment s = A C");
            Assert.Equal("Line 3, column 15: Unknown object \"C\".", Assert.Throws<ScriptError>(() => ScriptCompiler.CompileFile(main)).ToString());

            File.WriteAllText(main, "point A = 0 0\n  include missing.geo");
            Assert.StartsWith("Line 2, column 11: File not found: missing.geo", Assert.Throws<ScriptError>(() => ScriptCompiler.CompileFile(main)).ToString());
            File.WriteAllText(Path.Combine(folder, "lib", "more.geo"), "include styles.geo");
            File.WriteAllText(main, "include lib/styles.geo");
            Assert.Equal("lib/more.geo, line 1, column 9: styles.geo is already being included (circle: main.geo → lib/styles.geo → lib/more.geo → lib/styles.geo).",
                Assert.Throws<ScriptError>(() => ScriptCompiler.CompileFile(main)).ToString());
            ErrorAt("include", 1, 1, "needs a file name");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void SegmentsRaysAndLinesCanHaveLabels()
    {
        JsonElement primitives = JsonDocument.Parse(ScriptCompiler.Compile("""
            point A = 0 0 nolabel
            point B = 4 0 nolabel
            point C = 0 3 nolabel
            segment a = A B label "$a$"
            segment b = A C label "$b$" labelsize 20
            ray r = A B label "r" labelpos 2
            line g = C B label "g" offset 0.5 0.5
            segment s = B A labelpos 0.25 label "s"
            """).ToJson()).RootElement.GetProperty("primitives");
        JsonElement Label(string text) => primitives.EnumerateArray().Single(p => p.GetProperty("type").GetString() == "text"
            && p.GetProperty("tracks").GetProperty("text")[0][1].GetString() == text);
        double First(JsonElement p, string track) => p.GetProperty("tracks").GetProperty(track)[0][1].GetDouble();
        // A → B to the right: in the middle, below (the normal to the right points down; the player keeps the gap)
        JsonElement a = Label("$a$");
        Assert.Equal((2.0, 0.0, 0.0, -1.0, 8.0), (First(a, "x"), First(a, "y"), First(a, "nx"), First(a, "ny"), First(a, "gap")));
        // A → C upwards: to the right
        JsonElement b = Label("$b$");
        Assert.Equal((0.0, 1.5, 1.0, 0.0, 20.0), (First(b, "x"), First(b, "y"), First(b, "nx"), First(b, "ny"), First(b, "size")));
        // beyond B on the ray
        Assert.Equal(8.0, First(Label("r"), "x"));
        // an offset in coordinates from the middle
        JsonElement g = Label("g");
        Assert.Equal((2.5, 2.0), (First(g, "x"), First(g, "y")));
        Assert.False(g.GetProperty("tracks").TryGetProperty("nx", out _));
        // B → A to the left: above, a quarter of the way from B
        JsonElement s = Label("s");
        Assert.Equal((3.0, 1.0), (First(s, "x"), First(s, "ny")));

        ErrorAt("point A = 0 0\ncircle k = center A radius 1 label \"k\"", 2, 30, "\"label\" is not possible for circle");
        ErrorAt("point A = 0 0 labelpos 0.5", 1, 15, "\"labelpos\" is not possible for point");
        ErrorAt("point A = 0 0\npoint B = 1 0\nsegment s = A B label \"$a\"", 3, 1, "no closing $");
    }

    [Fact]
    public void TitleAndStepContinueOnLinesBeginningWithABar()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            title Ein langer
              | Titel
            point A = 0 0
            step Wir zeichnen um $A$ einen Kreis mit dem Radius $|AB|$
            |   und erhalten so alle Punkte,
            |
            | die gleich weit entfernt sind.
              pause
            step zweiter
            pont X = 1 1
            """.Replace("pont X = 1 1", "  pause"));
        Assert.Equal("Ein langer Titel", animation.Title);
        // the "|" alone starts a new paragraph
        Assert.Equal("Wir zeichnen um $A$ einen Kreis mit dem Radius $|AB|$ und erhalten so alle Punkte,\ndie gleich weit entfernt sind.",
            animation.Steps[0].Caption);
        Assert.Equal("zweiter", animation.Steps[1].Caption);
        // line numbers after continuation lines stay right
        ErrorAt("step a\n| b\n| c\npont X = 1 1", 4, 1, "Unknown command");
        ErrorAt("point A = 0 0\n| b", 2, 1, "continues the text of the title or step line");
    }

    [Fact]
    public void BarAloneStartsAParagraphAndSetNamesTexts()
    {
        var c = ScriptCompiler.CompileConstruction("""
            title Zwei
            |
            | Absätze
            set hinweis = Die Strecke $bar(AP)$ ist **genauso lang**
            | wie $c$.
            |
            |
            | Damit ist der Satz bewiesen.
            set kurz = $A_1$
            point A = 0 0 label kurz
            text t = hinweis at 3 2
            text u = "erste\nzweite Zeile" at 1 1
            step s
            |
              pause
            """);
        Assert.Equal("Zwei\nAbsätze", c.Title);
        Assert.Equal("Die Strecke $bar(AP)$ ist **genauso lang** wie $c$.\nDamit ist der Satz bewiesen.", ((GeoText)c.Objects["t"]).Content);
        Assert.Equal("erste\nzweite Zeile", ((GeoText)c.Objects["u"]).Content);
        Assert.Equal("$A_1$", c.Objects["A"].Options.Label);
        ErrorAt("set w = a\npoint w = 0 0", 2, 7, "already the name of a text");
        ErrorAt("var v color red\nset v = a", 2, 5, "already the name of a var");
        ErrorAt("point A = 0 0\ntext t = nichts at 1 1", 2, 10, "or the name of a text from \"set\"");
        ErrorAt("set = a", 1, 5, "Expected the name of an object");
    }

    [Fact]
    public void AnglesRunCounterclockwiseFromTheFirstArm()
    {
        var c = ScriptCompiler.CompileConstruction("""
            point B = 0 0
            point A = 2 0
            point C = 0 3
            angle w = A B C label "$alpha$" fill yellow
            angle v = C B A radius 0.8
            point D = -1 -1
            angle r = A B D
            step s
              move C to -3 0
            """);
        var w = (GeoAngle)c.Objects["w"];
        var v = (GeoAngle)c.Objects["v"];
        // after the move: from BA (0°) to BC (180°)
        Assert.Equal((0.0, 180.0), (w.Start, Math.Round(w.Degrees, 9)));
        Assert.Equal(180.0, Math.Round(v.Degrees, 9));
        Assert.Equal(225.0, Math.Round(((GeoAngle)c.Objects["r"]).Degrees, 9));
        Assert.Equal((Color.Parse("yellow"), 0.8), (w.Options.Fill, v.Options.Radius!.Value));
        JsonElement angle = JsonDocument.Parse(c.Build().ToJson()).RootElement.GetProperty("primitives").EnumerateArray()
            .First(p => p.GetProperty("type").GetString() == "angle");
        Assert.Equal("$alpha$", angle.GetProperty("tracks").GetProperty("text")[0][1].GetString());
        Assert.Equal(90.0, angle.GetProperty("tracks").GetProperty("sweep")[0][1].GetDouble(), 6);
        Assert.True(c.Build().Formulas.ContainsKey("alpha"));
        ErrorAt("point A = 0 0\nangle w = A A A", 2, 1, "cannot be constructed");
        ErrorAt("point A = 0 0\npoint B = 1 0\npoint C = 1 1\nangle w = A B C width 2 size 3", 4, 25, "\"size\" is not possible for angle");
    }

    [Fact]
    public void InfoExplainsTheCurrentStep()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point P = 0 0
            step Punkte markieren Orte
            info Ein Punkt beschreibt eine genaue Lage.
            | Er hat keine Ausdehnung.
            info Ein zweiter Absatz mit $P$.
              pause
            step ohne
              pause
            """);
        Assert.Equal("Ein Punkt beschreibt eine genaue Lage. Er hat keine Ausdehnung.\nEin zweiter Absatz mit $P$.", animation.Steps[0].Info);
        Assert.Null(animation.Steps[1].Info);
        JsonElement steps = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("steps");
        Assert.Equal("Ein Punkt beschreibt eine genaue Lage. Er hat keine Ausdehnung.\nEin zweiter Absatz mit $P$.", steps[0].GetProperty("info").GetString());
        Assert.False(steps[1].TryGetProperty("info", out _));
        Assert.True(animation.Formulas.ContainsKey("P"));
        ErrorAt("point P = 0 0\ninfo zu früh", 2, 1, "belongs to a step");
        ErrorAt("point P = 0 0\nstep s\ninfo kaputt $x", 3, 1, "no closing $");
    }

    [Fact]
    public void ShowLabelFadesLabelsInAndOut()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point A = 0 0 nolabel
            point B = 4 0 nolabel
            point C = 9 9 nolabel
            segment s = A B label "$a$" nolabel
            angle w = B A C label "$alpha$"
            step Namen
              show label A s
              hide label w duration 200
            step weg
              hide label A
              move A to 1 1
            """);
        JsonElement primitives = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives");
        JsonElement[] texts = primitives.EnumerateArray().Where(p => p.GetProperty("type").GetString() == "text").ToArray();
        // labels of A and s, none for B and C (never shown)
        Assert.Equal(["$A$", "$a$"], texts.Select(t => t.GetProperty("tracks").GetProperty("text")[0][1].GetString()).Order(StringComparer.Ordinal));
        JsonElement opacityA = texts.Single(t => t.GetProperty("tracks").GetProperty("text")[0][1].GetString() == "$A$")
            .GetProperty("tracks").GetProperty("opacity");
        Assert.Equal(0, opacityA[0][1].GetDouble());
        Assert.Equal(1, opacityA.EnumerateArray().Max(k => k[1].GetDouble()));
        Assert.Equal(0, opacityA[opacityA.GetArrayLength() - 1][1].GetDouble());
        JsonElement angleLabel = primitives.EnumerateArray().Single(p => p.GetProperty("type").GetString() == "angle")
            .GetProperty("tracks").GetProperty("labelOpacity");
        Assert.Equal([1.0, 1.0, 0.0], angleLabel.EnumerateArray().Select(k => k[1].GetDouble()));
        // a label hidden before the object is drawn: it appears without label
        JsonElement later = JsonDocument.Parse(ScriptCompiler.Compile("point A = 0 0\nstep s\n  point P = 1 1 hidden\n  hide label P\n  show P")
            .ToJson()).RootElement.GetProperty("primitives");
        Assert.Single(later.EnumerateArray(), p => p.GetProperty("type").GetString() == "text");
        ErrorAt("point A = 0 0\npoint B = 1 0\nsegment s = A B\nstep x\nshow label s", 5, 12, "has no label");
        ErrorAt("point A = 0 0\ncircle k = center A radius 1\nstep x\nhide label k", 4, 12, "has no label");
        ErrorAt("point A = 0 0\nstep x\nshow label", 3, 11, "needs the names");
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
