using System.Text.Json;
using GeoAnim.Cli;
using GeoAnim.Script;
using GeoAnim.Viewer;

namespace GeoAnim.Tests;

public sealed class MarkdownTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("geoanim-markdown-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void FrontMatterGivesTitleDescriptionAndKeywords()
    {
        MarkdownPage page = MarkdownPage.Parse("""
            ---
            title: Punkte und ihre Lage
            description: Was ein Punkt ist.
            keywords: [Punkt/e/en, Lage]
            ---

            # Der Punkt

            Text.
            """);

        Assert.Equal("Punkte und ihre Lage", page.Title);
        Assert.Equal("Der Punkt", page.Heading);
        Assert.Equal("Was ein Punkt ist.", page.Description);
        Assert.Equal(["Punkt/e/en", "Lage"], page.Keywords);
        Assert.Empty(page.Errors);
        Assert.Empty(page.Warnings);
        // the front matter is not part of the page
        Assert.DoesNotContain("title:", page.Html);
        Assert.Contains("<h1", page.Html);
    }

    [Fact]
    public void WithoutTitleTheFirstHeadingIsTheTitle()
    {
        MarkdownPage page = MarkdownPage.Parse("Vorweg.\n\n## Nicht diese\n\n# Strecken und $bar(AB)$ #\n");

        Assert.Equal("Strecken und $bar(AB)$", page.Title);
        Assert.Null(page.FrontTitle);
    }

    [Fact]
    public void FrontMatterMistakesHaveTheirLine()
    {
        MarkdownPage page = MarkdownPage.Parse("---\ntitle: Gut\nauthor: Thomas\nkeywords: [a, [b]]\n---\n\nText\n");

        // an unknown key: a warning in line 3
        MarkdownProblem warning = Assert.Single(page.Warnings);
        Assert.Equal(3, warning.Line);
        Assert.Contains("author", warning.Message);
        MarkdownProblem error = Assert.Single(page.Errors);
        Assert.Equal(4, error.Line);
        Assert.Equal("Gut", page.Title);
    }

    [Fact]
    public void FormulasBecomeSvg()
    {
        MarkdownPage page = MarkdownPage.Parse("Die Punkte $M != Q$ und \\$5.\n\n$$\na^2 + b^2 = c^2\n$$\n");

        Assert.Contains("<svg class=\"formula\"", page.Html);
        Assert.Contains("aria-label=\"M != Q\"", page.Html);
        Assert.Contains("<div class=\"formula-block\"", page.Html);
        Assert.Contains("$5", page.Html);
        Assert.Empty(page.Errors);
    }

    [Fact]
    public void AFaultyFormulaIsMarkedAndReported()
    {
        MarkdownPage page = MarkdownPage.Parse("Erste Zeile.\n\nHier $a + b falsch, \\$ nicht.\n");

        MarkdownProblem error = Assert.Single(page.Errors);
        Assert.Equal(3, error.Line);
        Assert.Equal(6, error.Column);
        Assert.Contains("closing $", error.Message);
    }

    [Fact]
    public void BlocksCarryTheirLine()
    {
        MarkdownPage page = MarkdownPage.Parse("---\ntitle: T\n---\n# Kopf\n\nAbsatz eins.\n\n- Punkt\n");

        Assert.Contains("<h1 data-line=\"4\"", page.Html);
        Assert.Contains("<p data-line=\"6\"", page.Html);
    }

    [Fact]
    public void ScriptImagesBecomeFigures()
    {
        File.WriteAllText(Path.Combine(folder, "punkte.geo"), "title P\n");
        string file = Path.Combine(folder, "punkte.geo.md");
        MarkdownPage page = MarkdownPage.Parse("""
            ![Punkte mit *Namen*](punkte.geo#2)

            ![](punkte.geo#3..4)
            ![](punkte.geo#verschieden..gleich)

            ![](punkte.geo)

            Ein Bild ![](punkte.geo#2) im Text bleibt eins.

            ![](fehlt.geo#1-2)

            ![](punkte.geo#3...4)
            """, file);

        Assert.Equal(
            ["punkte.geo#2", "punkte.geo#3..4", "punkte.geo#verschieden..gleich", "punkte.geo", "fehlt.geo#1..2"],
            page.Figures.Select(f => f.Source));
        Assert.Equal(("3", "4"), (page.Figures[1].From, page.Figures[1].To));
        Assert.Equal(("2", "2"), (page.Figures[0].From, page.Figures[0].To));
        Assert.Equal("Punkte mit *Namen*", page.Figures[0].Caption);
        Assert.Contains("<figcaption>Punkte mit <em>Namen</em></figcaption>", page.Html);
        // two references in one paragraph: one figure
        Assert.Equal(5, page.Html.Split("<figure").Length - 1);
        Assert.Contains("geo-faulty", page.Html);
        Assert.Collection(page.Errors,
            e => Assert.Equal((10, "The script \"fehlt.geo\" does not exist."), (e.Line, e.Message)),
            e => Assert.Equal(12, e.Line));
    }

    [Fact]
    public void DaemonRendersMarkdown()
    {
        string file = Path.Combine(folder, "seite.geo.md");
        JsonElement answer = JsonDocument.Parse(Daemon.Handle(JsonSerializer.Serialize(new
        {
            id = 7, type = "markdown", file, text = "# Seite\n\n![](weg.geo)\n",
        }))).RootElement;

        Assert.Equal(7, answer.GetProperty("id").GetInt32());
        Assert.Equal("Seite", answer.GetProperty("title").GetString());
        Assert.Contains("<h1", answer.GetProperty("html").GetString());
        JsonElement error = Assert.Single(answer.GetProperty("errors").EnumerateArray());
        Assert.Equal(3, error.GetProperty("line").GetInt32());
        Assert.Equal(file, error.GetProperty("file").GetString());

        string page = JsonDocument.Parse(Daemon.Handle("{\"id\": 8, \"type\": \"page\"}")).RootElement.GetProperty("html").GetString()!;
        Assert.Contains("acquireVsCodeApi", page);
        Assert.Contains("font/woff2;base64", page);
    }

    [Fact]
    public void StepsHaveMarks()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            point A = 0 0
            step Gleiche Punkte #gleich
            step Zweiter Schritt
            | weiter #kein
            step #nur
            step Preis #5 und mehr
            """);

        Assert.Equal(["Gleiche Punkte", "Zweiter Schritt weiter", "", "Preis #5 und mehr"], animation.Steps.Select(s => s.Caption));
        Assert.Equal(["gleich", "kein", "nur", null], animation.Steps.Select(s => s.Mark));
        Assert.Contains("#gleich", Assert.Throws<ScriptError>(() => ScriptCompiler.Compile("step a #gleich\nstep b #gleich")).Message);
    }

    [Fact]
    public void StepNamesComeFromTheirText()
    {
        Assert.Equal("gleiche-punkte", MarkdownPage.StepName("Gleiche Punkte"));
        Assert.Equal("laenge-und-groesse", MarkdownPage.StepName("Länge und **Größe**"));
        Assert.Equal("die-strecke-ab", MarkdownPage.StepName("Die Strecke $bar(AB)$"));
        Assert.Equal("cafe-1", MarkdownPage.StepName("Café 1!\nzweite Zeile"));
    }

    [Fact]
    public void FiguresGetTheirAnimationAndSteps()
    {
        File.WriteAllText(Path.Combine(folder, "punkte.geo"), """
            view x -10 .. 10 y -5 .. 5
            point A = 0 0
            step Erster
            step Zweiter #zwei
            step Dritter
            step Erster
            """);
        string file = Path.Combine(folder, "punkte.geo.md");
        int loads = 0;
        AnimationData Load(string path)
        {
            loads++;
            return ScriptCompiler.CompileFile(path);
        }
        MarkdownPage page = MarkdownPage.Parse("""
            ![](punkte.geo)

            ![](punkte.geo#2)
            ![](punkte.geo#0)

            ![](punkte.geo#zwei..dritter)

            ![](punkte.geo#erster)

            ![](punkte.geo#7)

            ![](punkte.geo#0..2)

            ![](punkte.geo#3..zwei)

            ![](punkte.geo#nichts)
            """, file, Load);

        Assert.Equal(6 + 3, loads);
        Assert.Collection(page.PageFigures,
            f => Assert.Equal((null, null, false), (f.From, f.To, f.Still)),
            f => Assert.Equal((1, 1, true), (f.From, f.To, f.Still)),
            f => Assert.Equal((-1, -1, true), (f.From, f.To, f.Still)),
            f => Assert.Equal((1, 2, false), (f.From, f.To, f.Still)));
        Assert.Equal(2.0, page.PageFigures[0].Ratio);
        Assert.Contains("data-figure=\"" + page.PageFigures[1].Key + "\"", page.Html);
        Assert.Contains("geo-still", page.Html);
        Assert.Contains("geo-player", page.Html);
        Assert.Collection(page.Errors,
            e => Assert.Contains("steps 1 and 4 are both called \"erster\"", e.Message),
            e => Assert.Contains("has 4 steps, not 7", e.Message),
            e => Assert.Contains("starts at step 1", e.Message),
            e => Assert.Contains("wrong order", e.Message),
            e => Assert.Contains("no step \"#nichts\" (there are #erster, #zwei, #dritter, #erster)", e.Message));
    }

    [Fact]
    public void DaemonSendsTheFiguresAndReportsFaultyScripts()
    {
        File.WriteAllText(Path.Combine(folder, "gut.geo"), "point A = 0 0\nstep Eins\nstep Zwei\n");
        File.WriteAllText(Path.Combine(folder, "kaputt.geo"), "point A = 0 0\npoint A = 1 1\n");
        string file = Path.Combine(folder, "seite.geo.md");
        JsonElement Ask() => JsonDocument.Parse(Daemon.Handle(JsonSerializer.Serialize(new
        {
            id = 1, type = "markdown", file, text = "![](gut.geo#1..2)\n\n![](gut.geo#1..2)\n\n![](kaputt.geo)\n",
        }))).RootElement;

        JsonElement answer = Ask();
        JsonProperty figure = Assert.Single(answer.GetProperty("figures").EnumerateObject());
        Assert.Equal(0, figure.Value.GetProperty("from").GetInt32());
        Assert.Equal(1, figure.Value.GetProperty("to").GetInt32());
        Assert.False(figure.Value.GetProperty("still").GetBoolean());
        Assert.Equal(2, figure.Value.GetProperty("animation").GetProperty("steps").GetArrayLength());
        JsonElement error = Assert.Single(answer.GetProperty("errors").EnumerateArray());
        Assert.Equal(5, error.GetProperty("line").GetInt32());
        Assert.StartsWith("kaputt.geo: line 2:", error.GetProperty("message").GetString());

        // a changed script is compiled again
        File.WriteAllText(Path.Combine(folder, "gut.geo"), "point A = 0 0\nstep Eins\nstep Zwei\nstep Drei\n");
        File.SetLastWriteTimeUtc(Path.Combine(folder, "gut.geo"), DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(3, Assert.Single(Ask().GetProperty("figures").EnumerateObject()).Value.GetProperty("animation").GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public void AHashAloneMeansThePagesOwnScript()
    {
        File.WriteAllText(Path.Combine(folder, "punkte.geo"), "point A = 0 0\nstep Eins #eins\nstep Zwei\n");
        string file = Path.Combine(folder, "punkte.geo.md");
        MarkdownPage page = MarkdownPage.Parse("![](#)\n\n![](#2)\n\n![](#eins..2)\n", file, ScriptCompiler.CompileFile);

        Assert.Empty(page.Errors);
        Assert.Equal(["punkte.geo", "punkte.geo#2", "punkte.geo#eins..2"], page.Figures.Select(f => f.Source));
        Assert.Equal(3, page.PageFigures.Count);

        // a page not named after a script, and one not saved
        Assert.Contains("not named after one", Assert.Single(MarkdownPage.Parse("![](#2)", Path.Combine(folder, "seite.md")).Errors).Message);
        Assert.Contains("saved first", Assert.Single(MarkdownPage.Parse("![](#2)").Errors).Message);
        // a link to a place on the page stays a link
        Assert.Contains("<a href=\"#oben\">", MarkdownPage.Parse("[nach oben](#oben)").Html);
    }

    [Fact]
    public void FiguresHaveAttributes()
    {
        File.WriteAllText(Path.Combine(folder, "punkte.geo"), "point A = 0 0\nstep Eins\n");
        string file = Path.Combine(folder, "punkte.geo.md");
        MarkdownPage page = MarkdownPage.Parse("""
            ![](#1){crop=no float=right width=40%}

            ![](#1){width=60mm}
            ![](#1){width=50mm}

            ![](#1){crop=vielleicht float=oben width=breit farbe=rot}
            """, file, ScriptCompiler.CompileFile);

        Assert.False(page.Figures[0].Crop);
        Assert.True(page.Figures[1].Crop);
        Assert.Contains("data-float=\"right\"", page.Html);
        Assert.Contains("data-width=\"40%\"", page.Html);
        Assert.NotEqual(page.PageFigures[0].Key, page.PageFigures[1].Key);
        Assert.Collection(page.Errors,
            e => Assert.Equal((4, "The images of this figure have different values for \"width\" (60mm, 50mm)."), (e.Line, e.Message)),
            e => Assert.Contains("crop=yes or crop=no", e.Message),
            e => Assert.Contains("float=left or float=right", e.Message),
            e => Assert.Contains("width=40%", e.Message));
        Assert.Contains("farbe", Assert.Single(page.Warnings).Message);
    }
}
