using System.Text.Json;
using GeoAnim.Script;

namespace GeoAnim.Tests;

public sealed class UseTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("geoanim-use").FullName;

    // an equilateral triangle on AB: the third point C (to the left of A → B)
    private const string Equilateral = """
        title Gleichseitiges Dreieck
        input point A = -3 0
        input point B = 3 0
        step Kreise um $A$ und $B$
          circle k1 = center A through B color gray
          circle k2 = center B through A color gray
        step Der Schnittpunkt $C$
          point C T = intersect k1 k2 color red
          pause 700
          segment AC = A C
        output C
        step Beweis (nur, wenn die Datei allein läuft)
          circle proof = center C through A
        """;

    public UseTests() => File.WriteAllText(Path.Combine(folder, "equilateral.geo"), Equilateral);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private Construction Compile(string script)
    {
        string path = Path.Combine(folder, "main.geo");
        File.WriteAllText(path, script);
        return ScriptCompiler.CompileConstruction(ScriptSource.FromFile(path));
    }

    private ScriptError Error(string script) => Assert.Throws<ScriptError>(() => Compile(script));

    [Fact]
    public void TheFileRunsAloneWithTheValuesOfItsInputs()
    {
        var c = ScriptCompiler.CompileConstruction(ScriptSource.FromFile(Path.Combine(folder, "equilateral.geo")));
        Assert.Equal(0, ((GeoPoint)c.Objects["C"]).Position.X, 9);
        Assert.Equal(Math.Sqrt(27), ((GeoPoint)c.Objects["C"]).Position.Y, 9);
        Assert.Equal("Gleichseitiges Dreieck", c.Title);
    }

    [Fact]
    public void UseGivesTheOutputUnderTheCallersName()
    {
        var c = Compile("""
            point P = 0 0
            point Q = 2 0
            step Ein Dreieck
              point R = use equilateral.geo P Q color blue
              move Q to 4 0
            """);
        var r = (GeoPoint)c.Objects["R"];
        // the result depends on the inputs: it followed Q
        Assert.Equal(2, r.Position.X, 9);
        Assert.Equal(Math.Sqrt(12), r.Position.Y, 9);
        Assert.Equal(Color.Parse("blue"), r.Options.Color);
        Assert.Null(r.Options.Label);
        // the helpers have names of their own and stay hidden; the file's title and steps do not count
        Assert.DoesNotContain("k1", c.Objects.Keys);
        Assert.Contains(c.Objects.Keys, name => name.EndsWith(".k1", StringComparison.Ordinal));
        Assert.All(c.Objects.Values.Where(o => o.Name.Contains('#')), o => Assert.True(o.Options.Hidden));
        Assert.Null(c.Title);
        AnimationData animation = c.Build();
        Assert.Single(animation.Steps);
        // drawn like an ordinary point (400 ms), then the move (1000 ms)
        Assert.Equal(1400, animation.Duration);
    }

    [Fact]
    public void DrawShowsTheConstructionInTheCurrentStepAndHidesTheHelpers()
    {
        var c = Compile("""
            point P = 0 0
            point Q = 2 0
            step Ein Dreieck
              point R = use equilateral.geo P Q draw
            """);
        AnimationData animation = c.Build();
        Assert.Single(animation.Steps);
        // circles (1200 ms each, one after the other), the points (400), the segment (800), no pause, then fading out (400)
        Assert.Equal(1200 + 1200 + 400 + 800 + 400, animation.Duration);
        Assert.False(c.Objects["R"].Options.Hidden);
        JsonElement texts = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("primitives");
        // the helper point is labelled with its name in the file
        Assert.Contains(texts.EnumerateArray(), p => p.GetProperty("type").GetString() == "text"
            && p.GetProperty("tracks").GetProperty("text")[0][1].GetString() == "$T$");
    }

    [Fact]
    public void StepsTakesTheStepsOfTheFile()
    {
        AnimationData animation = Compile("""
            point P = 0 0
            point Q = 2 0
            step Wir konstruieren
              point R = use equilateral.geo P Q steps
            """).Build();
        Assert.Equal(["Wir konstruieren", "Kreise um $A$ und $B$", "Der Schnittpunkt $C$"], animation.Steps.Select(s => s.Caption));
    }

    [Fact]
    public void UsedFilesCanUseOthers()
    {
        File.WriteAllText(Path.Combine(folder, "lib.geo"), """
            input point U = 0 0
            input point V = 1 0
            point M = midpoint U V
            point W = use equilateral.geo M V draw
            output W
            """);
        var c = Compile("point P = 0 0\npoint Q = 4 0\nstep s\n  point X = use lib.geo P Q draw");
        Assert.Equal(3, ((GeoPoint)c.Objects["X"]).Position.X, 9);
        // within the result-only use, the inner one shows only its result, too
        var hidden = Compile("point P = 0 0\npoint Q = 4 0\npoint X = use lib.geo P Q");
        Assert.All(hidden.Objects.Values.Where(o => o.Name.Contains('#')), o => Assert.True(o.Options.Hidden));
    }

    [Fact]
    public void TheKindChoosesAmongSeveralOutputs()
    {
        File.WriteAllText(Path.Combine(folder, "two.geo"), """
            input point A = 0 0
            input point B = 2 0
            point C = use equilateral.geo A B
            segment side = A C
            output C side
            """);
        // with a kind: the outputs of that kind
        var c = Compile("point P = 0 0\npoint Q = 2 0\npoint R = use two.geo P Q color red\nsegment s = use two.geo Q P");
        Assert.Equal(Color.Parse("red"), c.Objects["R"].Options.Color);
        Assert.IsType<GeoStraight>(c.Objects["s"]);
        // the result not taken is a hidden helper
        Assert.Contains(c.Objects.Values, o => o.Name.EndsWith(".side", StringComparison.Ordinal) && o.Options.Hidden);
        // without a kind: all outputs in their order; "_" skips one; options only where they fit
        var both = Compile("point P = 0 0\npoint Q = 2 0\nR s = use two.geo P Q width 3 size 6\n_ t = use two.geo Q P");
        Assert.IsType<GeoPoint>(both.Objects["R"]);
        Assert.Equal((6.0, (double?)null), (both.Objects["R"].Options.Size!.Value, both.Objects["R"].Options.Width));
        Assert.Equal(3.0, both.Objects["s"].Options.Width);
        Assert.IsType<GeoStraight>(both.Objects["t"]);
        Assert.DoesNotContain("_", both.Objects.Keys);
        Assert.Contains("gives only 2 results (point C, segment side)", Error("point P = 0 0\npoint Q = 2 0\nR s t = use two.geo P Q").Message);
        Assert.Equal("Line 1, column 1: Unknown command \"R\".", Error("R s = 1 2").ToString());
    }

    [Fact]
    public void InputsAndOutputsMayStandInTogetherBlocks()
    {
        File.WriteAllText(Path.Combine(folder, "four.geo"), """
            input point A = 0 0
            step zwei Punkte zugleich
              together
                input point B = 2 0
                input point C = 0 2
              end
              together
                point M = midpoint B C
                output M
              end
            """);
        var c = Compile("point P = 0 0\npoint Q = 4 0\npoint R = 0 4\npoint X = use four.geo P Q R");
        Assert.Equal(new Vec(2, 2), ((GeoPoint)c.Objects["X"]).Position);
        // drawn, the inputs in the block are bound too (and the block draws only the file's own objects)
        var drawn = Compile("point P = 0 0\npoint Q = 4 0\npoint R = 0 4\nstep s\n  point X = use four.geo P Q R draw");
        Assert.Equal(new Vec(2, 2), ((GeoPoint)drawn.Objects["X"]).Position);
    }

    [Fact]
    public void AUsedFileEndsWithItsLastOutputLine()
    {
        // alone: the proof after the output runs
        var alone = ScriptCompiler.CompileConstruction(ScriptSource.FromFile(Path.Combine(folder, "equilateral.geo")));
        Assert.Contains("proof", alone.Objects.Keys);
        Assert.Equal(3, alone.Build().Steps.Count);
        // used, with its steps: the proof is not part of it
        var used = Compile("point P = 0 0\npoint Q = 2 0\nstep Wir konstruieren\n  point R = use equilateral.geo P Q steps");
        Assert.DoesNotContain(used.Objects.Keys, name => name.EndsWith(".proof", StringComparison.Ordinal));
        Assert.Equal(["Wir konstruieren", "Kreise um $A$ und $B$", "Der Schnittpunkt $C$"], used.Build().Steps.Select(s => s.Caption));
        // an output before its object is a mistake, also when the file runs alone
        File.WriteAllText(Path.Combine(folder, "early.geo"), "input point A = 0 0\noutput B\npoint B = 1 1");
        Assert.Equal("Line 2, column 8: B is not defined yet: \"output\" comes after the construction of its objects (a used file ends with its last output line).",
            Assert.Throws<ScriptError>(() => ScriptCompiler.CompileFile(Path.Combine(folder, "early.geo"))).ToString());
        Assert.Contains("In early.geo, line 2, column 8: B is not defined yet", Error("point P = 0 0\npoint R = use early.geo P").Message);
    }

    [Fact]
    public void OutputCanStandBeforeTheDefinitionLikeInput()
    {
        File.WriteAllText(Path.Combine(folder, "prefix.geo"), """
            input point A = 0 0
            input point B = 2 0
            output point M = midpoint A B
            circle k = center A through B hidden
            line g = A B
            output point S T = intersect k g
            """);
        var c = Compile("point P = 0 0\npoint Q = 4 0\nM S T = use prefix.geo P Q");
        Assert.Equal(new Vec(2, 0), ((GeoPoint)c.Objects["M"]).Position);
        Assert.Equal(new Vec(4, 0), ((GeoPoint)c.Objects["T"]).Position);
        // the kinds are known before the file runs
        Assert.Contains("gives only 3 points", Error("point P = 0 0\npoint Q = 4 0\npoint A B C D = use prefix.geo P Q").Message);
        // without a kind, with use, and the short form for an input given back
        File.WriteAllText(Path.Combine(folder, "chain.geo"), "input point A = 0 0\ninput point B = 2 0\noutput X _ Y = use prefix.geo A B\noutput A");
        var chain = Compile("point P = 0 0\npoint Q = 4 0\nR S U = use chain.geo P Q");
        Assert.Equal((new Vec(2, 0), new Vec(0, 0)), (((GeoPoint)chain.Objects["R"]).Position, ((GeoPoint)chain.Objects["U"]).Position));
        Assert.Same(chain.Objects["P"], chain.Objects["U"]);
        Assert.Contains("needs a definition or the names", Error("point P = 0 0\noutput").Message);
    }

    [Fact]
    public void UseWithoutResultsDrawsTheFile()
    {
        File.WriteAllText(Path.Combine(folder, "flash.geo"), """
            input point A = 0 0
            input point B = 1 0
            step zeigen
            segment s = A B color red
            pause 100
            hide s
            """);
        var c = Compile("point P = 0 0\npoint Q = 2 0\nstep s\n  use flash.geo P Q");
        AnimationData animation = c.Build();
        // drawn in the step (800), its pause (100) and fading out (400); the whole file, as it has no output line
        Assert.Single(animation.Steps);
        Assert.Equal(1300, animation.Duration);
        // "draw" leaves out the pauses
        Assert.Equal(1200, Compile("point P = 0 0\npoint Q = 2 0\nstep s\n  use flash.geo P Q draw").Build().Duration);
        Assert.Contains(c.Objects.Keys, name => name.EndsWith(".s", StringComparison.Ordinal));
        // with its steps
        Assert.Equal(["s", "zeigen"], Compile("point P = 0 0\npoint Q = 2 0\nstep s\n  use flash.geo P Q steps").Build().Steps.Select(s => s.Caption));
        Assert.Contains("takes no options", Error("point P = 0 0\npoint Q = 2 0\nstep s\n  use flash.geo P Q color red").Message);
        Assert.Contains("needs 2 inputs", Error("point P = 0 0\nstep s\n  use flash.geo P").Message);
        // a file without output still gives no results to names
        Assert.Contains("has no \"output\" line", Error("point P = 0 0\npoint Q = 2 0\npoint R = use flash.geo P Q").Message);
    }

    [Fact]
    public void PathsWithSpacesStandInQuotes()
    {
        Directory.CreateDirectory(Path.Combine(folder, "Buch 1"));
        File.WriteAllText(Path.Combine(folder, "Buch 1", "Satz 1.geo"), Equilateral);
        File.WriteAllText(Path.Combine(folder, "meine Stile.geo"), "default point size 7");
        var c = Compile("""
            include "meine Stile.geo"
            include meine Stile.geo
            point P = 0 0
            point Q = 2 0
            point R = use "Buch 1/Satz 1.geo" P Q
            step s
              use "Buch 1/Satz 1.geo" Q P draw
            """.Replace("include meine Stile.geo\n", ""));
        Assert.Equal(7.0, c.Objects["P"].Options.Size);
        Assert.Equal(Math.Sqrt(3), ((GeoPoint)c.Objects["R"]).Position.Y, 9);
        // also without quotes, the rest of the line is an include's path
        Assert.Equal(7.0, Compile("include meine Stile.geo\npoint P = 0 0").Objects["P"].Options.Size);
        Assert.StartsWith("Line 1, column 15: File not found: Buch 2/Satz 1.geo", Error("point R = use \"Buch 2/Satz 1.geo\" P").ToString());
        Assert.StartsWith("Line 1, column 10: File not found: fehlt.geo", Error("include \"fehlt.geo\"").ToString());
    }

    [Fact]
    public void MistakesAreReportedAtTheUse()
    {
        Assert.Equal("Line 3, column 15: equilateral.geo needs 2 inputs (point A point B), not 1.",
            Error("point P = 0 0\npoint Q = 2 0\npoint R = use equilateral.geo P").ToString());
        Assert.Equal("Line 3, column 31: The input A of equilateral.geo is a point; k is a circle.",
            Error("point P = 0 0\ncircle k = center P radius 1\npoint R = use equilateral.geo k P").ToString());
        Assert.Contains("gives only 1 point (point C)",
            Error("point P = 0 0\npoint Q = 2 0\npoint R S = use equilateral.geo P Q").Message);
        Assert.Contains("equilateral.geo has no circle among its results (point C)",
            Error("point P = 0 0\npoint Q = 2 0\ncircle k = use equilateral.geo P Q").Message);
        Assert.StartsWith("Line 1, column 15: File not found: missing.geo", Error("point R = use missing.geo").ToString());
        // an error inside the file: with its line, without the internal names
        Assert.StartsWith("Line 3, column 15: In equilateral.geo, line 5, column 3: circle k1 ",
            Error("point P = 0 0\npoint Q = 0 0\npoint R = use equilateral.geo P Q").ToString());
        File.WriteAllText(Path.Combine(folder, "self.geo"), "input point A = 0 0\npoint B = use self.geo A\noutput B");
        Assert.Contains("self.geo is already being used (circle: main.geo → self.geo → self.geo)",
            Error("point P = 0 0\npoint R = use self.geo P").Message);
        File.WriteAllText(Path.Combine(folder, "mover.geo"), "input point A = 0 0\npoint B = 1 1\nstep s\n  move A to 2 2\noutput B");
        Assert.Contains("A used file cannot move its input A", Error("point P = 0 0\nstep s\n  point R = use mover.geo P draw").Message);
        File.WriteAllText(Path.Combine(folder, "none.geo"), "point A = 0 0");
        Assert.Contains("has no \"output\" line", Error("point R = use none.geo").Message);
    }
}
