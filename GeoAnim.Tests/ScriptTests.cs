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

    /// <summary>
    /// The folders whose scripts are all compiled by the tests: the samples of the repository, and folders outside it
    /// that are tested only where they exist (the geometry book in its own repository).
    /// </summary>
    private static readonly (string Name, string Folder)[] ScriptFolders =
    [
        ("Samples", Repository.PathOf("Samples")),
        ("GeoBook", @"C:\Code\GeoBook\src"),
    ];

    /// <summary>Every script in these folders, with subfolders, named "Samples/Euklid/Book1/Prop1.geo".</summary>
    private static List<string> SamplePaths() => ScriptFolders
        .Where(entry => Directory.Exists(entry.Folder))
        .SelectMany(entry => Directory.EnumerateFiles(entry.Folder, "*.geo", SearchOption.AllDirectories)
            .Select(path => entry.Name + "/" + Path.GetRelativePath(entry.Folder, path).Replace('\\', '/')))
        .Order(StringComparer.Ordinal)
        .ToList();

    // "GeoBook/10_Grundlagen/10_Punkte.geo" → the file
    private static string FullPathOf(string sample)
    {
        string[] parts = sample.Split('/');
        string folder = ScriptFolders.Single(entry => entry.Name == parts[0]).Folder;
        return Path.Combine([folder, .. parts[1..]]);
    }

    public static TheoryData<string> Samples() => new(SamplePaths());

    // a change of the language must be carried through all samples: each one compiles completely
    [Theory]
    [MemberData(nameof(Samples))]
    public void SamplesCompile(string sample)
    {
        AnimationData animation = ScriptCompiler.CompileFile(FullPathOf(sample));
        JsonDocument.Parse(animation.ToJson());
    }

    [Fact]
    public void SamplesAreFound()
    {
        Assert.Contains("Samples/circumcircle.geo", SamplePaths());
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
    public void ShowAndHideLeaveObjectsAloneThatAreSoAlready()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point A = 0 0
            point B = 4 0 hidden
            step schon da, noch nie gezeigt
              show A
              hide B
            step ausgeblendet
              hide A
            step noch einmal ausgeblendet
              hide A
              show label A
            """);
        // nothing to do takes no time: the steps without action last 500 ms, "hide A" 400 ms
        Assert.Equal([500.0, 400.0, 500.0], animation.Steps.Select(s => s.End - s.Start));
        JsonElement dots = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives");
        Assert.Single(dots.EnumerateArray(), p => p.GetProperty("type").GetString() == "dot");
    }

    [Fact]
    public void HideAllHidesEverythingVisible()
    {
        Construction c = ScriptCompiler.CompileConstruction("""
            point A = 0 0
            point B = 4 0
            segment s = A B label "$a$"
            circle k = center A through B hidden
            number l = length s
            step neue Zeichnung
              hide label all
              hide all duration 300
            step weiter
              point C = 1 1
            """);
        AnimationData animation = c.Build();
        JsonElement primitives = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives");
        double Last(JsonElement p, string track) => p.GetProperty("tracks").GetProperty(track)[p.GetProperty("tracks").GetProperty(track).GetArrayLength() - 1][1].GetDouble();
        // the dots of A and B and the segment are faded out, C is new; the hidden circle was never drawn
        double[] dots = primitives.EnumerateArray().Where(p => p.GetProperty("type").GetString() == "dot").Select(p => Last(p, "opacity")).ToArray();
        Assert.Equal([0.0, 0.0, 1.0], dots);
        Assert.Equal(0, Last(primitives.EnumerateArray().Single(p => p.GetProperty("type").GetString() == "line"), "opacity"));
        Assert.DoesNotContain(primitives.EnumerateArray(), p => p.GetProperty("type").GetString() == "arc");
        // hide label all, then hide all: 300 ms after the 400 ms of the labels
        Assert.Equal(700, animation.Steps[0].End - animation.Steps[0].Start, 6);
        ErrorAt("point A = 0 0\nstep s\nshow all", 3, 6, "only possible with \"hide\"");
        ErrorAt("point A = 0 0\nhide all", 2, 1, "only possible inside a step");
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
    public void NumbersMeasureAndFollowTheObjects()
    {
        Construction c = ScriptCompiler.CompileConstruction("""
            point A = 0 0
            point B = 4 0
            point C = 0 3
            segment s = A B
            polygon D = A B C
            circle k = center A through C
            angle w = B A C
            number l = length s
            number d = length B C
            number g = degrees w
            number f = area D
            number u = perimeter D
            number r = radius k
            number q = area k
            text t = "$|AB| =$ {l} cm, {g}°" at 1 1
            step Die Strecke ist jetzt {l:2} lang
              move B to 6 0
            """);
        double Value(string name) => ((GeoNumber)c.Objects[name]).Value;
        Assert.Equal(6, Value("l"), 9);
        Assert.Equal(Math.Sqrt(45), Value("d"), 9);
        Assert.Equal(90, Value("g"), 9);
        Assert.Equal(9, Value("f"), 9);
        Assert.Equal(6 + 3 + Math.Sqrt(45), Value("u"), 9);
        Assert.Equal(3, Value("r"), 9);
        Assert.Equal(9 * Math.PI, Value("q"), 9);

        // the values over time for the player; texts outside the player get the final ones
        AnimationData animation = c.Build();
        JsonElement number = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray()
            .First(p => p.GetProperty("type").GetString() == "number");
        Assert.Equal("l", number.GetProperty("tracks").GetProperty("name")[0][1].GetString());
        JsonElement values = number.GetProperty("tracks").GetProperty("value");
        Assert.Equal((4.0, 6.0), (values[0][1].GetDouble(), values[values.GetArrayLength() - 1][1].GetDouble()));
        Assert.Equal("Die Strecke ist jetzt 6,00 lang", animation.WithFinalNumbers(animation.Steps[0].Caption));
        Assert.Equal(("3,3", "0,0", "−3", "4"), (NumberText.Format(3.25, 1), NumberText.Format(-0.04, 1), NumberText.Format(-2.5, 0), NumberText.Format(3.6, 0)));

        ErrorAt("point A = 0 0\ntext t = \"Länge {l}\" at A", 2, 17, "{l} names no number");
        ErrorAt("point A = 0 0\npoint B = 1 0\nnumber l = length A B\ntext t = \"$l = {l}$\" at A", 4, 1, "inside a formula");
        ErrorAt("point A = 0 0\npoint B = 1 0\nray r = A B\nnumber l = length r", 4, 1, "has no length");
        ErrorAt("point A = 0 0\nnumber l = degrees A", 2, 20, "degrees measures an angle");
        ErrorAt("point A = 0 0\nnumber l = size A", 2, 12, "Expected a measure");
        // "\{" is a brace, not a placeholder
        ScriptCompiler.Compile("point A = 0 0\ntext t = \"\\{l}\" at A");
    }

    [Fact]
    public void SegmentsAndRaysCanHaveArrows()
    {
        JsonElement primitives = JsonDocument.Parse(ScriptCompiler.Compile("""
            point A = 0 0
            point B = 3 0
            var pfeil arrow color red
            segment a = A B arrow
            segment b = A B arrow start
            segment c = A B arrow both
            ray r = A B pfeil
            segment d = A B
            """).ToJson()).RootElement.GetProperty("primitives");
        string[] arrows = primitives.EnumerateArray().Where(p => p.GetProperty("type").GetString() == "line")
            .Select(p => p.GetProperty("tracks").TryGetProperty("arrow", out JsonElement arrow) ? arrow[0][1].GetString()! : "-").ToArray();
        Assert.Equal(["end", "start", "both", "end", "-"], arrows);
        // on an angle's arc: at its end (counterclockwise) or start (clockwise)
        JsonElement angle = JsonDocument.Parse(ScriptCompiler.Compile("point A = 1 0\npoint S = 0 0\npoint C = 0 1\nangle w = A S C arrow start").ToJson())
            .RootElement.GetProperty("primitives").EnumerateArray().Single(p => p.GetProperty("type").GetString() == "angle");
        Assert.Equal("start", angle.GetProperty("tracks").GetProperty("arrow")[0][1].GetString());
        // the label of an angle can be moved
        JsonElement moved = JsonDocument.Parse(ScriptCompiler.Compile("point A = 1 0\npoint S = 0 0\npoint C = 0 1\nangle w = A S C label \"x\" offset 0.2 -0.1").ToJson())
            .RootElement.GetProperty("primitives").EnumerateArray().Single(p => p.GetProperty("type").GetString() == "angle").GetProperty("tracks");
        Assert.Equal((0.2, -0.1), (moved.GetProperty("ox")[0][1].GetDouble(), moved.GetProperty("oy")[0][1].GetDouble()));
        ErrorAt("point A = 0 0\npoint B = 1 0\nline g = A B arrow", 3, 14, "not possible for line");
        ErrorAt("point A = 0 0\narrow start", 2, 1, "command");
    }

    [Fact]
    public void TranslateAndRotateMoveTheFreePoints()
    {
        Construction c = ScriptCompiler.CompileConstruction("""
            point A = 0 0
            point B = 4 0
            point C = 0 3
            polygon D = A B C
            point M = midpoint A B
            point P = 1 1
            point Q = 3 2
            point Z = 10 10
            step verschieben
              move D along P Q
            step drehen
              rotate A B C around Z by 90 duration 500
            step zurück
              together
                rotate A around Z by -90
              end
            """);
        Vec At(string name) => ((GeoPoint)c.Objects[name]).Position;
        void Near(Vec expected, Vec actual)
        {
            Assert.Equal(expected.X, actual.X, 9);
            Assert.Equal(expected.Y, actual.Y, 9);
        }
        // moved by (2, 1), then turned around Z = (10, 10) by 90°; A turned back; M follows A and B
        Near(new Vec(2, 1), At("A"));
        Near(new Vec(19, 6), At("B"));
        Near(new Vec(16, 2), At("C"));
        Near(new Vec(10.5, 3.5), At("M"));
        Near(new Vec(1, 1), At("P"));
        ErrorAt("point A = 0 0\nstep s\n  move A B to 1 1", 3, 12, "Expected \"along\"");
        ErrorAt("point A = 0 0\nstep s\n  rotate A around A 90", 3, 21, "\"by\"");
        ErrorAt("point A = 0 0\nstep s\n  rotate around A by 90", 3, 10, "names of objects");
        ErrorAt("point A = 0 0\nmove A along A A", 2, 1, "only possible inside a step");
    }

    [Fact]
    public void ReflectMovesTheFreePointsOntoTheirImages()
    {
        Construction c = ScriptCompiler.CompileConstruction("""
            point P = 0 0
            point Q = 0 1
            line g = P Q
            point A = -4 0
            point B = -1 0
            point C = -1 3
            segment s = A B label "$s$"
            angle w = B A C
            point Z = 2 2
            step falten
              reflect A B C across g
            step zurück, geradlinig
              reflect A B C across P Q straight duration 300
            step am Punkt
              reflect A B C in Z
            step am Punkt geradlinig
              reflect A in Z straight
            """);
        Vec At(string name) => ((GeoPoint)c.Objects[name]).Position;
        void Near(Vec expected, Vec actual)
        {
            Assert.Equal(expected.X, actual.X, 9);
            Assert.Equal(expected.Y, actual.Y, 9);
        }
        // across the y axis and back, then turned around Z = (2, 2) by 180°: A (−4, 0) → (8, 4), C (−1, 3) → (5, 1);
        // A back through Z to (−4, 0)
        Near(new Vec(-4, 0), At("A"));
        Near(new Vec(5, 4), At("B"));
        Near(new Vec(5, 1), At("C"));
        Near(new Vec(0, 0), At("P"));
        ErrorAt("point A = 0 0\nstep s\n  reflect A over A", 3, 19, "\"across\"");
        ErrorAt("point A = 0 0\npoint B = 1 0\nstep s\n  reflect B across A", 4, 20, "\"in A\"");
        ErrorAt("point A = 0 0\nreflect A in A", 2, 1, "only possible inside a step");
        ErrorAt("point A = 0 0\nstep s\n  move A to 1 1 straight", 3, 17, "not possible for move");
    }

    [Fact]
    public void ReflectedAnglesAndLabelsKeepTheirSide()
    {
        // the angle at A (63.4°) stays that size, measured clockwise now (also while folding: never the outer angle);
        // the label of AB stays outside the triangle
        Construction c = ScriptCompiler.CompileConstruction("""
            point P = 0 0
            point Q = 0 1
            point A = 1 0
            point B = 4 0
            point C = 2 2
            segment s = A B label "$s$"
            angle w = B A C
            step falten
              reflect A B C across P Q
            """);
        var angle = (GeoAngle)c.Objects["w"];
        Assert.Equal(Math.Atan2(2, 1) * 180 / Math.PI, angle.Degrees, 6);
        AnimationData animation = c.Build();
        JsonElement label = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray()
            .Single(p => p.GetProperty("type").GetString() == "text" && p.GetProperty("tracks").GetProperty("text")[0][1].GetString() == "$s$");
        JsonElement ny = label.GetProperty("tracks").GetProperty("ny");
        // before: below AB (to the right of A → B); after: A (−1, 0) → B (−4, 0) runs to the left, the label still below
        Assert.Equal(-1, ny[0][1].GetDouble(), 6);
        Assert.Equal(-1, ny[ny.GetArrayLength() - 1][1].GetDouble(), 3);
        JsonElement sweep = animation.ToJson() is string json
            ? JsonDocument.Parse(json).RootElement.GetProperty("primitives").EnumerateArray().Single(p => p.GetProperty("type").GetString() == "angle").GetProperty("tracks").GetProperty("sweep")
            : default;
        Assert.All(sweep.EnumerateArray(), key => Assert.InRange(key[1].GetDouble(), 0, 90 + 1e-6));
    }

    [Fact]
    public void BackAndFrontChangeTheStacking()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            default segment back
            point A = 0 0
            point B = 4 0 front
            point C = 0 3
            polygon d = A B C fill lightgreen
            segment v = A B arrow label "$v$"
            text t = "Hallo" at 1 1 front
            circle k = center A through C front
            """);
        int[] layers = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray()
            .Select(p => p.GetProperty("layer").GetInt32()).ToArray();
        // A, its label, B (front), its label, C, its label, the polygon, the segment (back), its label (where labels are),
        // the text (front: still on top), the circle (front)
        Assert.Equal([30, 40, 45, 40, 30, 40, 10, 0, 40, 50, 45], layers);
        ErrorAt("point A = 0 0\nstep s\n  show A back", 3, 10, "not possible for show");
    }

    [Fact]
    public void HighlightPulsesOrStays()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point A = 0 0
            point B = 4 0
            segment s = A B
            point C = 0 3 color green
            step pulsieren
              highlight s duration 800
            step bleiben
              highlight C keep color red
            step zurück
              unhighlight C
            """);
        JsonElement[] primitives = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray().ToArray();
        JsonElement Track(string type, int index, string track) => primitives.Where(p => p.GetProperty("type").GetString() == type).ElementAt(index).GetProperty("tracks").GetProperty(track);
        double At(JsonElement track, double time) => track.EnumerateArray().Last(k => k[0].GetDouble() <= time + 1e-6)[1].ValueKind == JsonValueKind.Number
            ? track.EnumerateArray().Last(k => k[0].GetDouble() <= time + 1e-6)[1].GetDouble() : double.NaN;
        string Color(JsonElement track, double time) => track.EnumerateArray().Last(k => k[0].GetDouble() <= time + 1e-6)[1].GetString()!;
        // the segment: twice thicker and orange (at 200 and 600 ms), in between and afterwards as before
        JsonElement width = Track("line", 0, "width");
        Assert.Equal(2.5 * 1.8, width.EnumerateArray().Max(k => k[1].GetDouble()), 6);
        Assert.Equal(2.5, width[width.GetArrayLength() - 1][1].GetDouble(), 6);
        Assert.Equal("#ef6c00", Color(Track("line", 0, "color"), 200));
        Assert.Equal("#ef6c00", Color(Track("line", 0, "color"), 600));
        Assert.Equal("#222222", Color(Track("line", 0, "color"), 800));
        // C: red and larger from the second step on, green again after the third
        JsonElement color = Track("dot", 2, "color");
        Assert.Equal("#d32f2f", Color(color, 1200));
        Assert.Equal(5 * 1.6, At(Track("dot", 2, "r"), 1200), 6);
        Assert.Equal("#2e7d32", color[color.GetArrayLength() - 1][1].GetString());
        Assert.Equal(1600, animation.Duration, 6);
        ErrorAt("point A = 0 0\nhighlight A", 2, 1, "only possible inside a step");
        ErrorAt("point A = 0 0\nstep s\n  highlight keep", 3, 13, "needs the names");
    }

    [Fact]
    public void HaloGoesToLabelsAndTexts()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point A = 0 0 halo
            point B = 4 0
            point C = 0 3
            segment s = A B label "$s$" halo
            angle w = B A C label "$w$" halo
            text t = "x" at 1 1 halo
            """);
        JsonElement[] primitives = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray().ToArray();
        string[] withHalo = primitives.Where(p => p.GetProperty("tracks").TryGetProperty("halo", out _))
            .Select(p => p.GetProperty("type").GetString() + ":" + (p.GetProperty("tracks").TryGetProperty("text", out JsonElement text) ? text[0][1].GetString() : "")).ToArray();
        Assert.Equal(["text:$A$", "text:$s$", "angle:$w$", "text:x"], withHalo);
        ErrorAt("point A = 0 0\npoint B = 1 0\npoint C = 0 1\npolygon d = A B C halo", 4, 19, "not possible for polygon");
    }

    [Fact]
    public void ExpressionsFollowTheRulesOfArithmetic()
    {
        double Value(string text, double a = 0) => Expression.Parse(text).Evaluate(n => n == "a" ? a : null)!.Value;
        Assert.Equal(7, Value("1 + 2 * 3"));
        Assert.Equal(9, Value("(1 + 2) * 3"));
        Assert.Equal(-4, Value("-2^2"));
        Assert.Equal(512, Value("2^3^2"));
        Assert.Equal(0.5, Value("2^-1"));
        Assert.Equal(1.5, Value("a / 2", 3));
        Assert.Equal(3 * Math.Sqrt(3), Value("3*sqrt(3)"), 12);
        Assert.Equal(Math.PI, Value("abs(-pi)"), 12);
        Assert.Null(Expression.Parse("1 / 0").Evaluate(_ => null));
        Assert.Null(Expression.Parse("b + 1").Evaluate(_ => null));
        Assert.Equal([("a", 0), ("b", 4)], Expression.Parse("a + b").Names);
        Assert.Equal(4, Assert.Throws<ExpressionException>(() => Expression.Parse("2 * (a")).Index - 2);
    }

    [Fact]
    public void ConstantsStandForNumbers()
    {
        Construction c = ScriptCompiler.CompileConstruction("""
            number tx = 5.2
            number ty = 4.5   # a comment
            number dy = tx - 2 * (ty - 4)
            point A = tx ty
            point B = 0 dy size dy
            text t = "{tx} und {dy}" at tx ty
            segment s = A B
            number l = length s
            number halb = l / 2
            step s
              move A to 0 ty duration tx
              rotate B around A by dy
            """);
        Assert.Equal(new Vec(0, 4.5), ((GeoPoint)c.Objects["A"]).Position);
        var dy = (GeoNumber)c.Objects["dy"];
        Assert.Equal((4.2, true, 1), (dy.Value, dy.IsConstant, dy.Digits));
        var halb = (GeoNumber)c.Objects["halb"];
        Assert.False(halb.IsConstant);
        Assert.Equal(((GeoNumber)c.Objects["l"]).Value / 2, halb.Value, 12);
        Assert.Equal(1, halb.Digits);
        // "duration tx": 5.2 ms for the move, then the rotation
        Assert.Equal(1005.2, c.Build().Steps[0].End - c.Build().Steps[0].Start, 6);
        ErrorAt("point A = 0 0\npoint B = 1 0\nsegment s = A B\nnumber l = length s\npoint C = l 0", 5, 11, "follows the drawing");
        ErrorAt("number a = b + 1", 1, 12, "not a number defined before");
        ErrorAt("number a = 2 * (3", 1, 18, "\")\" is missing");
        ErrorAt("number a = 1 / 0", 1, 12, "has no value");
        ErrorAt("number a = a + 1", 1, 12, "not a number defined before");
    }

    [Fact]
    public void FillMakesACircleADisc()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point M = 0 0
            point P = 2 0
            circle k = center M through P
            circle s = center M radius 1 color red fill opacity 0.4
            circle t = center M radius 3 fill lightblue
            polygon d = M P M fill
            """);
        JsonElement[] primitives = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray().ToArray();
        JsonElement[] discs = primitives.Where(p => p.GetProperty("type").GetString() == "disc").ToArray();
        // only the circles with "fill"; with the fills of polygons (layer 10); "fill" alone: the circle's own colour
        Assert.Equal(2, discs.Length);
        Assert.All(discs, d => Assert.Equal(10, d.GetProperty("layer").GetInt32()));
        Assert.Equal(("#d32f2f", 0.4), (discs[0].GetProperty("tracks").GetProperty("fill")[0][1].GetString(), discs[0].GetProperty("tracks").GetProperty("fillOpacity")[0][1].GetDouble()));
        Assert.Equal(0.2, discs[1].GetProperty("tracks").GetProperty("fillOpacity")[0][1].GetDouble());
        Assert.Equal(3, primitives.Count(p => p.GetProperty("type").GetString() == "arc"));
    }

    [Fact]
    public void ArcsAndSectorsLieOnTheirCircle()
    {
        Construction c = ScriptCompiler.CompileConstruction("""
            point M = 0 0
            point P = 2 0
            point A = 1 0
            point B = 0 5
            circle k = center M through P
            arc b = k from A to B label "$b$" arrow
            sector s = k from B to A fill violet
            number l = length b
            number f = area s
            number w = degrees s
            step größer
              move P to 4 0
            """);
        var b = (GeoArc)c.Objects["b"];
        var s = (GeoArc)c.Objects["s"];
        // A and B give the directions: a quarter of the circle (radius 4 now), the sector the other three quarters
        Assert.Equal((0.0, 90.0), (b.Start, b.Degrees));
        Assert.Equal(270, s.Degrees, 9);
        Assert.Equal(2 * Math.PI, ((GeoNumber)c.Objects["l"]).Value, 9);
        Assert.Equal(12 * Math.PI, ((GeoNumber)c.Objects["f"]).Value, 9);
        Assert.Equal(270, ((GeoNumber)c.Objects["w"]).Value, 9);
        JsonElement[] primitives = JsonDocument.Parse(c.Build().ToJson()).RootElement.GetProperty("primitives").EnumerateArray().ToArray();
        Assert.Equal(["part:20", "disc:10", "part:20"], primitives.Where(p => p.GetProperty("type").GetString() is "part" or "disc")
            .Select(p => p.GetProperty("type").GetString() + ":" + p.GetProperty("layer").GetInt32()));
        ErrorAt("point M = 0 0\npoint A = 1 0\nsegment g = M A\narc b = g from A to M", 4, 9, "lies on a circle");
        ErrorAt("point M = 0 0\ncircle k = center M radius 1\narc b = k A to M", 3, 11, "\"from\"");
        ErrorAt("point M = 0 0\ncircle k = center M radius 1\nsector s = k from M to M arrow", 3, 26, "not possible for sector");
    }

    [Fact]
    public void LabelDirectionsPlaceLabelsAroundThePoint()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point A = 0 0 labeldir NW labelgap 6
            point B = 4 0
            step warten
              pause 200
            step drehen
              move label B to S gap 10
            """);
        JsonElement[] texts = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray()
            .Where(p => p.GetProperty("type").GetString() == "text").ToArray();
        JsonElement a = texts[0].GetProperty("tracks");
        // NW: the normal (−0.71, 0.71), the gap measured from the point's centre (radius 5 + 6)
        Assert.Equal((-Math.Sqrt(0.5), Math.Sqrt(0.5), 11.0), (a.GetProperty("nx")[0][1].GetDouble(), a.GetProperty("ny")[0][1].GetDouble(), a.GetProperty("gap")[0][1].GetDouble()), new ToleranceComparer());
        Assert.Equal(1, a.GetProperty("dir")[0][1].GetDouble());
        // B: its default place until the step (dir 0), then turned from NE the shorter way to S (−90°), gap 4 → 10
        JsonElement b = texts[1].GetProperty("tracks");
        Assert.Equal((0.0, 0.0), (b.GetProperty("dir")[0][0].GetDouble(), b.GetProperty("dir")[0][1].GetDouble()));
        Assert.Equal(1, b.GetProperty("dir")[b.GetProperty("dir").GetArrayLength() - 1][1].GetDouble());
        JsonElement ny = b.GetProperty("ny");
        Assert.Equal(-1, ny[ny.GetArrayLength() - 1][1].GetDouble(), 3);
        Assert.All(b.GetProperty("nx").EnumerateArray().Skip(1), k => Assert.True(k[1].GetDouble() >= -1e-3));
        JsonElement gap = b.GetProperty("gap");
        Assert.Equal(15, gap[gap.GetArrayLength() - 1][1].GetDouble(), 6);
        ErrorAt("point A = 0 0 labeldir up", 1, 24, "Expected a direction");
        ErrorAt("point A = 0 0\npoint B = 1 0\nsegment s = A B label \"s\"\nstep x\nmove label s to N", 5, 12, "for the label of a point");
    }

    private sealed class ToleranceComparer : IEqualityComparer<(double, double, double)>
    {
        public bool Equals((double, double, double) x, (double, double, double) y) =>
            Math.Abs(x.Item1 - y.Item1) < 1e-3 && Math.Abs(x.Item2 - y.Item2) < 1e-3 && Math.Abs(x.Item3 - y.Item3) < 1e-3;

        public int GetHashCode((double, double, double) obj) => 0;
    }

    [Fact]
    public void ArcsTakeDirectionsAndRunClockwise()
    {
        Construction c = ScriptCompiler.CompileConstruction("""
            number w = 120
            point M = 0 0
            point A = 2 0
            circle k = center M radius 2
            arc b = k from 30 to w
            sector s = k from 90 to 0 clockwise
            step zeichnen
              arc d = k from A to 90 clockwise arrow
            """);
        var b = (GeoArc)c.Objects["b"];
        var d = (GeoArc)c.Objects["d"];
        var s = (GeoArc)c.Objects["s"];
        Assert.Equal((30.0, 90.0), (b.Start, b.Degrees));
        // clockwise from A (0°) to 90°: the counterclockwise part from 90° to 360°
        Assert.Equal((90.0, 270.0), (d.Start, d.Degrees));
        Assert.Equal((0.0, 90.0), (s.Start, s.Degrees));
        JsonElement part = JsonDocument.Parse(c.Build().ToJson()).RootElement.GetProperty("primitives").EnumerateArray()
            .Last(p => p.GetProperty("type").GetString() == "part");
        // drawn from its end: the start goes from 360° back to 90° while the sweep grows; the arrow at the counterclockwise start
        JsonElement start = part.GetProperty("tracks").GetProperty("start");
        Assert.Equal(360, start[0][1].GetDouble(), 6);
        Assert.Equal(90, start[start.GetArrayLength() - 1][1].GetDouble(), 6);
        Assert.Equal("start", part.GetProperty("tracks").GetProperty("arrow")[0][1].GetString());
    }

    [Fact]
    public void TextStepsHaveTextOnly()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point A = 0 0
            step zeichnen
              point B = 1 1
            textstep Zusammenfassung
            info eins zwei drei vier
            number n = 2
            textstep Mit Pause
              pause 5000
            step weiter
            """);
        Assert.Equal([false, true, true, false], animation.Steps.Select(s => s.TextOnly));
        // a reading time: 3 s and a quarter of a second for each of the 5 words; the pause where there is one
        Assert.Equal(3000 + 250 * 5, animation.Steps[1].End - animation.Steps[1].Start, 6);
        Assert.Equal(5000, animation.Steps[2].End - animation.Steps[2].Start, 6);
        Assert.Contains("\"text\":true", animation.ToJson());
        ErrorAt("point A = 0 0\ntextstep x\nhide A", 3, 1, "only text");
        ErrorAt("point A = 0 0\ntextstep x\npoint B = 1 1", 3, 1, "only text");
    }

    [Fact]
    public void ParallelsPerpendicularsAndRadiiFollowTheDrawing()
    {
        Construction c = ScriptCompiler.CompileConstruction("""
            point A = 0 0
            point B = 2 1
            line g = A B
            point C = 0 3
            line p = parallel g through C
            ray s = perpendicular g through C
            point D = parallel g through C
            segment AB = A B
            number l = length AB
            number r = l + 1
            circle k = center C radius A B
            circle m = center C radius r
            step bewegen
              move B to 3 0
            """);
        Vec At(string name) => ((GeoPoint)c.Objects[name]).Position;
        // after the move: AB = (3, 0); D = C + AB; the parallel runs that way, the perpendicular turned by 90°
        Assert.Equal(new Vec(3, 3), At("D"));
        Assert.Equal(new Vec(3, 0), ((GeoStraight)c.Objects["p"]).Direction);
        Assert.Equal(new Vec(0, 3), ((GeoStraight)c.Objects["s"]).Direction);
        Assert.Equal(3, ((GeoCircle)c.Objects["k"]).Radius, 9);
        Assert.Equal(4, ((GeoCircle)c.Objects["m"]).Radius, 9);
        ErrorAt("point A = 0 0\ncircle k = center A radius A", 2, 28, "the radius is a number");
        ErrorAt("point A = 0 0\npoint C = 1 1\nline p = parallel A through C", 3, 19, "not a segment, ray or line");
    }

    [Fact]
    public void ShiftMovesLabelsFromTheirDefaultPlace()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point A = 0 0 shift 0.2 -0.1
            point B = 4 0
            segment s = A B label "$a$"
            angle w = B A B label "x" shift 0.3 0
            step nachjustieren
              move label A s to 0 -0.5 duration 300
            """);
        JsonElement[] texts = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray()
            .Where(p => p.GetProperty("type").GetString() == "text").ToArray();
        JsonElement Track(string text, string track) => texts.Single(t => t.GetProperty("tracks").GetProperty("text")[0][1].GetString() == text).GetProperty("tracks").GetProperty(track);
        // A: from (0.2, −0.1) to (0, −0.5); the default distance stays (dx 7, dy 7)
        Assert.Equal((0.2, -0.1), (Track("$A$", "sx")[0][1].GetDouble(), Track("$A$", "sy")[0][1].GetDouble()));
        Assert.Equal((0.0, -0.5), (Track("$A$", "sx")[Track("$A$", "sx").GetArrayLength() - 1][1].GetDouble(), Track("$A$", "sy")[Track("$A$", "sy").GetArrayLength() - 1][1].GetDouble()));
        Assert.Equal(7, Track("$A$", "dx")[0][1].GetDouble());
        // s had no shift: it starts from 0 and gets one now; B is untouched
        Assert.Equal(0, Track("$a$", "sy")[0][1].GetDouble());
        Assert.Equal(-0.5, Track("$a$", "sy")[Track("$a$", "sy").GetArrayLength() - 1][1].GetDouble());
        Assert.False(texts.Single(t => t.GetProperty("tracks").GetProperty("text")[0][1].GetString() == "$B$").GetProperty("tracks").TryGetProperty("sx", out _));
        Assert.Equal(300, animation.Steps[0].End - animation.Steps[0].Start, 6);
        ErrorAt("point A = 0 0\npoint B = 1 0\nsegment s = A B\nstep x\nmove label s to 0 1", 5, 12, "has no label");
        ErrorAt("point A = 0 0\nmove label A to 0 1", 2, 1, "only possible inside a step");
    }

    [Fact]
    public void LabelMovedInALaterStepIsShownFromTheStart()
    {
        // the shift tracks used to begin with "move label": until then the player had no value and drew nothing
        AnimationData animation = ScriptCompiler.Compile("""
            point A = 4 0
            point S = 0 0
            point B = 0 4
            angle alpha = A S B label "$beta$"
            step eins
              pause 500
            step zwei
              move label alpha A to -0.4 0
            """);
        JsonElement[] primitives = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray().ToArray();
        JsonElement angle = primitives.Single(p => p.GetProperty("type").GetString() == "angle");
        JsonElement label = primitives.Single(p => p.GetProperty("type").GetString() == "text" && p.GetProperty("tracks").GetProperty("text")[0][1].GetString() == "$A$");
        foreach ((JsonElement primitive, string track) in new[] { (angle, "ox"), (angle, "oy"), (label, "sx"), (label, "sy") })
        {
            JsonElement keys = primitive.GetProperty("tracks").GetProperty(track);
            Assert.Equal(0, keys[0][0].GetDouble());
            Assert.Equal(0, keys[0][1].GetDouble());
            Assert.Equal(-0.4 * (track is "ox" or "sx" ? 1 : 0), keys[keys.GetArrayLength() - 1][1].GetDouble(), 6);
        }
    }

    [Fact]
    public void TextsAlignAndStandBelowEachOther()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point P = 0 0
            text t1 = "erste Zeile" at 1 2 align left
            text t2 = "zweite" below t1
            text t3 = "dritte" below t2 gap 0.3 shift 0.5 0
            text r = "rechts" at 9 2 align right
            text m = "Mitte" at 5 5
            step
              hide t2
            """);
        JsonElement[] texts = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray()
            .Where(p => p.GetProperty("type").GetString() == "text").ToArray();
        JsonElement Text(string content) => texts.Single(t => t.GetProperty("tracks").GetProperty("text")[0][1].GetString() == content);
        string Anchor(string content) => Text(content).GetProperty("tracks").GetProperty("anchor")[0][1].GetString()!;
        // the lines of a block take the alignment of the first one; they refer to the text above (the player places them)
        Assert.Equal(["start", "start", "start", "end", "middle"], new[] { "erste Zeile", "zweite", "dritte", "rechts", "Mitte" }.Select(Anchor));
        Assert.Equal(Text("erste Zeile").GetProperty("id").GetInt32(), (int)Text("zweite").GetProperty("tracks").GetProperty("below")[0][1].GetDouble());
        Assert.Equal(Text("zweite").GetProperty("id").GetInt32(), (int)Text("dritte").GetProperty("tracks").GetProperty("below")[0][1].GetDouble());
        Assert.Equal(0.3, Text("dritte").GetProperty("tracks").GetProperty("belowGap")[0][1].GetDouble());
        Assert.False(Text("zweite").GetProperty("tracks").TryGetProperty("x", out _));
        ErrorAt("point P = 0 0\ntext u = \"x\" at P\ntext v = \"y\" below u", 3, 1, "not below the text u next to a point");
        ErrorAt("point P = 0 0\ntext v = \"y\" below P", 2, 20, "\"below\" names a text");
        ErrorAt("text t = \"x\" at 1 1 align oben", 1, 27, "left, center or right");
        ErrorAt("text t = \"x\" near 1 1", 1, 14, "\"at\"");
    }

    [Fact]
    public void TextsAtCoordinatesMove()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point P = 6 1
            point Q = 8 2
            point M = 0 0
            text t = "Hallo" at 1 1
            text u = "neben P" at P
            step bewegen
              move t to 4 3
              move t to P
              move t along P Q
              rotate t around M by 90
            """);
        JsonElement text = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives").EnumerateArray()
            .First(p => p.GetProperty("type").GetString() == "text" && p.GetProperty("tracks").GetProperty("text")[0][1].GetString() == "Hallo");
        double Last(string track) => text.GetProperty("tracks").GetProperty(track)[text.GetProperty("tracks").GetProperty(track).GetArrayLength() - 1][1].GetDouble();
        // to (4, 3), to P (6, 1), by PQ = (2, 1) to (8, 2), around (0, 0) by 90° to (−2, 8)
        Assert.Equal(-2, Last("x"), 6);
        Assert.Equal(8, Last("y"), 6);
        ErrorAt("point P = 0 0\ntext u = \"x\" at P\nstep s\n  move u to 1 1", 4, 3, "moves with it");
        ErrorAt("point P = 0 0\ntext t = \"x\" at 1 1\nstep s\n  move t to 30", 4, 8, "not to a number");
    }

    [Fact]
    public void KeywordsLinkTheFirstOccurrenceInEachStep()
    {
        AnimationData page = ScriptCompiler.Compile("""
            keyword Punkt/e/en, rechte/r/n Winkel/s,
            | Gerade/n, Satz/es des Pythagoras
            point A = 0 0
            """);
        Assert.Equal(["Punkt", "Punkte", "Punkten", "rechte Winkel", "rechte Winkels", "rechter Winkel", "rechter Winkels", "rechten Winkel", "rechten Winkels", "Gerade", "Geraden",
            "Satz des Pythagoras", "Satzes des Pythagoras"], page.Keywords.Select(k => k.Form));
        Assert.All(page.Keywords, k => Assert.Equal(0, k.Step));
        // inside a step: that step (counted from 1)
        AnimationData steps = ScriptCompiler.Compile("keyword Punkt\npoint A = 0 0\nstep a\nstep b\nkeyword Endpunkt/e\n  pause");
        Assert.Equal([("Punkt", 0, "Punkt", 1), ("Endpunkt", 2, "Endpunkt", 5), ("Endpunkte", 2, "Endpunkt", 5)],
            steps.Keywords.Select(k => (k.Form, k.Step, k.Term, k.Line)));
        ErrorAt("keyword Punkt//e", 1, 1, "an ending after /");
        ErrorAt("keyword \"rechter Winkel\"", 1, 1, "separated by commas");
        ErrorAt("keyword Punkt,, Strecke", 1, 1, "empty keyword");
        ErrorAt("keyword Punkt; Strecke", 1, 1, "not a word");

        AnimationData animation = ScriptCompiler.Compile("""
            point A = 0 0
            step Punkte auf einer Geraden, ein Punkt mehr
            info Der Punkt $Punkt$ und `Punkt`, [Punkte](x.html), Punktsymmetrie, gerade,
            | ein rechter  Winkel und **Gerade**.
            step Noch ein Punkt
            info Noch ein Punkt
            step Ohne Link
            info Auf der einen \Seite kein \Punkt, dann ein Punkt
            step Überschrift
            info ## Der Punkt
            info Ein Punkt
            """);
        var links = new Dictionary<string, string>
        {
            ["Punkt"] = "p.html",
            ["Punkte"] = "p.html",
            ["Gerade"] = "g.html",
            ["Geraden"] = "g.html",
            ["rechter Winkel"] = "w.html",
            ["Winkel"] = "x.html",
            ["Seite"] = "s.html",
        };
        AnimationData linked = Keywords.Link(animation, links);
        // only in the explanations: the step descriptions are headings
        Assert.Equal("Punkte auf einer Geraden, ein Punkt mehr", linked.Steps[0].Caption);
        Assert.Equal("Der [Punkt](p.html) $Punkt$ und `Punkt`, [Punkte](x.html), Punktsymmetrie, gerade, ein [rechter  Winkel](w.html) und **[Gerade](g.html)**.",
            linked.Steps[0].Info);
        Assert.Equal("Noch ein Punkt", linked.Steps[1].Caption);
        Assert.Equal("Noch ein [Punkt](p.html)", linked.Steps[1].Info);
        // a backslash before a word: no link there (the player does not show the backslash)
        Assert.Equal("Auf der einen \\Seite kein \\Punkt, dann ein [Punkt](p.html)", linked.Steps[2].Info);
        // not in a heading ("## …")
        Assert.Equal("## Der Punkt\nEin [Punkt](p.html)", linked.Steps[3].Info);
        Assert.Equal("../Basics/10_Punkte.html", Keywords.RelativeAddress("Mehr/a.html", "Basics/10_Punkte.html"));
        Assert.Equal("b%20%282%29.html", Keywords.RelativeAddress("a.html", "b (2).html"));
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

    [Fact]
    public void AxesHaveASizeAndAColour()
    {
        string json = ScriptCompiler.Compile("axes size 34 color navy\npoint A = 1 1\n").ToJson();
        Assert.Contains("\"axes\":true,\"axesSize\":34,\"axesColor\":\"#", json);
        Assert.DoesNotContain("axesSize", ScriptCompiler.Compile("axes\npoint A = 1 1\n").ToJson());
        Assert.Throws<ScriptError>(() => ScriptCompiler.Compile("axes size -2\n"));
    }
}
