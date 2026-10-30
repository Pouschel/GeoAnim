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
        Assert.Contains("<figcaption><span class=\"figure-label\">Abb. 1</span> Punkte mit <em>Namen</em></figcaption>", page.Html);
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

    [Fact]
    public void StatementsAreNumberedAndLabelled()
    {
        MarkdownPage page = MarkdownPage.Parse("""
            ::: axiom {#ax-gerade} Geradenaxiom
            Durch zwei verschiedene Punkte verläuft **genau eine Gerade**.
            :::

            ::: def Strecke $bar(AB)$ {#def-strecke}
            Die gerade Verbindung.

            Zweiter Absatz.
            :::

            ::: proof
            Klar.
            :::

            ::: theorem
            $$
            a^2 + b^2 = c^2
            $$
            :::

            ::: unsinn
            x
            :::
            """);

        Assert.Equal(["ax-gerade", "def-strecke"], page.Marks.Select(m => m.Name));
        Assert.Equal(("axiom", "Axiom 1", "Geradenaxiom"), (page.Marks[0].Kind, page.Marks[0].Label, page.Marks[0].Text));
        Assert.Equal(("definition", "Definition 1", 5), (page.Marks[1].Kind, page.Marks[1].Label, page.Marks[1].Line));
        Assert.Contains("<div id=\"ax-gerade\" class=\"statement statement-axiom\" data-line=\"1\">", page.Html);
        Assert.Contains("<span class=\"statement-label\">Axiom 1 (Geradenaxiom).</span> Durch zwei", page.Html);
        Assert.Contains("<span class=\"statement-label\">Definition 1 (Strecke <svg", page.Html);
        Assert.Contains("<span class=\"statement-label\">Beweis.</span> Klar. <span class=\"qed\">∎</span>", page.Html);
        // axioms count for themselves, the other kinds together
        Assert.Contains("<p><span class=\"statement-label\">Satz 2.</span></p>", page.Html);
        Assert.Contains("\"unsinn\" is no kind of statement", Assert.Single(page.Errors).Message);
    }

    [Fact]
    public void ReferencesShowWhatTheMarkIs()
    {
        File.WriteAllText(Path.Combine(folder, "punkte.geo"), "point A = 0 0\nstep Eins\n");
        string file = Path.Combine(folder, "punkte.geo.md");
        var other = new Dictionary<string, BookMark>
        {
            ["def-strecke"] = new BookMark(Path.Combine(folder, "sub", "strecken.geo.md"), new PageMark("def-strecke", "definition", "Definition 4", "", 3, 1)),
        };
        MarkdownPage page = MarkdownPage.Parse("""
            ## Grundlagen

            ### Die Gerade {#abschnitt-gerade}

            ![Die Gerade](#1){#abb-gerade}

            ![](#1)

            In §abb-gerade, nach §§ax-gerade und §def-strecke, siehe §abschnitt-gerade. Kein Verweis: \§abb und § 5. Fehlt: §nichts.

            ::: axiom {#ax-gerade}
            Text.
            :::
            """, file, ScriptCompiler.CompileFile, other);

        Assert.Equal(["abschnitt-gerade", "abb-gerade", "ax-gerade"], page.Marks.Select(m => m.Name));
        // a heading below the page's: its text (they have no numbers)
        Assert.Equal("„Die Gerade“", page.Marks[0].Label);
        Assert.Contains("<a class=\"ref\" href=\"#abb-gerade\">Abb. 1</a>", page.Html);
        Assert.Contains("<a class=\"ref\" href=\"#ax-gerade\">Axiom 1</a>", page.Html);
        Assert.Contains("<a class=\"ref\" href=\"sub/strecken.geo.md#def-strecke\">Definition 4</a>", page.Html);
        Assert.Contains("<a class=\"ref\" href=\"#abschnitt-gerade\">„Die Gerade“</a>", page.Html);
        Assert.Contains("Kein Verweis: §abb und § 5.", page.Html);
        Assert.Contains("<span class=\"ref-missing\" title=\"nichts\">??</span>", page.Html);
        Assert.Contains("<figure class=\"geo-figure\" id=\"abb-gerade\"", page.Html);
        Assert.Contains("<span class=\"figure-label\">Abb. 1</span> Die Gerade</figcaption>", page.Html);
        // without a caption: the still's step description
        Assert.Contains("<span class=\"figure-label\">Abb. 2</span> Eins</figcaption>", page.Html);
        MarkdownProblem error = Assert.Single(page.Errors);
        Assert.Contains("no mark \"nichts\"", error.Message);
        Assert.Equal(9, error.Line);
    }

    [Fact]
    public void MarksAreUniqueInTheBook()
    {
        var other = new Dictionary<string, BookMark>
        {
            ["ax"] = new BookMark(Path.Combine(folder, "andere.geo.md"), new PageMark("ax", "axiom", "Axiom 1", "", 3, 1)),
        };
        MarkdownPage page = MarkdownPage.Parse("## A {#ab}\n\n## B {#ab}\n\n::: axiom {#ax}\nx\n:::\n", Path.Combine(folder, "seite.geo.md"), null, other);

        Assert.Collection(page.Errors,
            e => Assert.Equal((3, "The mark \"ab\" is in line 1 already."), (e.Line, e.Message)),
            e => Assert.Equal((5, "The mark \"ax\" is on andere.geo.md already (line 3)."), (e.Line, e.Message)));
        Assert.Contains("at least two characters", Assert.Single(MarkdownPage.Parse("## A {#a}\n").Errors).Message);
        MarksOfPage read = MarkdownPage.ReadMarks("## A {#ab}\n\n## B {#ab}\n\n::: axiom {#ax}\nx\n:::\n");
        Assert.Equal(["ab", "ab", "ax"], read.Marks.Select(m => m.Name));
        Assert.Equal(1, read.Counts["axiom"]);
    }

    [Fact]
    public void DaemonFindsTheMarksOfTheBookAndTheStepsOfAScript()
    {
        File.WriteAllText(Path.Combine(folder, "index.yaml"), "title: Buch\n");
        Directory.CreateDirectory(Path.Combine(folder, "kapitel"));
        File.WriteAllText(Path.Combine(folder, "kapitel", "a.geo.md"), "::: axiom {#ax-a}\nx\n:::\n");
        File.WriteAllText(Path.Combine(folder, "kapitel", "b.geo"), "point A = 0 0\nstep Gleiche Punkte #gleich\nstep Zwei\n");
        string file = Path.Combine(folder, "kapitel", "b.geo.md");
        JsonElement answer = JsonDocument.Parse(Daemon.Handle(JsonSerializer.Serialize(new
        {
            id = 1, type = "markdown", file, text = "Nach §ax-a.\n\n![](#gleich){#abb-b}\n",
        }))).RootElement;

        Assert.Empty(answer.GetProperty("errors").EnumerateArray());
        Assert.Contains("href=\"a.geo.md#ax-a\">Axiom 1</a>", answer.GetProperty("html").GetString());
        Assert.Equal(["abb-b", "ax-a"], answer.GetProperty("marks").EnumerateArray().Select(m => m.GetProperty("name").GetString()));
        // the figure as in print too: its picture in pt and the font size of the text
        JsonElement figure = answer.GetProperty("figures").EnumerateObject().Single().Value;
        Assert.Equal(11, figure.GetProperty("textSize").GetDouble());
        JsonElement picture = Assert.Single(figure.GetProperty("pictures").EnumerateArray());
        Assert.StartsWith("<svg", picture.GetProperty("svg").GetString());
        Assert.True(picture.GetProperty("width").GetDouble() > 0);

        JsonElement steps = JsonDocument.Parse(Daemon.Handle(JsonSerializer.Serialize(new
        {
            id = 2, type = "steps", file = Path.Combine(folder, "kapitel", "b.geo"),
        }))).RootElement;
        Assert.Equal(["gleich", null], steps.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("mark").GetString()));
        Assert.Equal("zwei", steps.GetProperty("steps")[1].GetProperty("name").GetString());
    }

    [Fact]
    public void ChaptersAndPagesAreNumberedInTheirOrder()
    {
        Dictionary<string, string> numbers = Daemon.BookNumbers(
            ["Vorwort.geo.md", "10_Eins/10_A.geo.md", "10_Eins/20_Unter/10_B.geo.md", "10_Eins/20_Unter/20_C.geo.md", "10_Eins/30_D.geo.md", "20_Zwei/10_E.geo.md"]);

        Assert.False(numbers.ContainsKey("Vorwort.geo.md"));
        Assert.Equal("1", numbers["10_Eins"]);
        Assert.Equal("1.1", numbers["10_Eins/10_A.geo.md"]);
        Assert.Equal("1.2", numbers["10_Eins/20_Unter"]);
        Assert.Equal("1.2.2", numbers["10_Eins/20_Unter/20_C.geo.md"]);
        Assert.Equal("1.3", numbers["10_Eins/30_D.geo.md"]);
        Assert.Equal("2.1", numbers["20_Zwei/10_E.geo.md"]);
    }

    // a page of the book as the preview shows it
    private MarkdownPage PageOfBook(string relative)
    {
        string file = Path.Combine(folder, relative);
        string text = File.ReadAllText(file);
        Daemon.BookOfPage book = Daemon.ReadBook(file, text);
        return MarkdownPage.Parse(text, file, null, book.Marks.ToDictionary(m => m.Mark.Name), book.Numbering, book.KeywordLinks);
    }

    [Fact]
    public void FiguresAndStatementsAreNumberedInTheirChapter()
    {
        File.WriteAllText(Path.Combine(folder, "index.yaml"), "title: Buch\n");
        Directory.CreateDirectory(Path.Combine(folder, "10_Eins"));
        Directory.CreateDirectory(Path.Combine(folder, "20_Zwei"));
        File.WriteAllText(Path.Combine(folder, "10_Eins", "10_A.geo.md"),
            "# Erste {#erste}\n\n::: axiom {#ax-a}\nx\n:::\n\n::: def {#def-a}\nx\n:::\n\n::: example\nx\n:::\n\n![](x.geo)\n");
        File.WriteAllText(Path.Combine(folder, "10_Eins", "20_B.geo.md"),
            "# Zweite\n\n::: axiom {#ax-b}\nx\n:::\n\n::: theorem {#satz-b}\nx\n:::\n\n::: example {#bsp-b}\nx\n:::\n\n![](x.geo){#abb-b}\n\nNach §def-a und §erste.\n");
        File.WriteAllText(Path.Combine(folder, "20_Zwei", "10_C.geo.md"),
            "# Dritte\n\n::: axiom {#ax-c}\nx\n:::\n\n::: lemma {#lemma-c}\nx\n:::\n\nSiehe §satz-b, §ax-b und §abb-b.\n");

        MarkdownPage second = PageOfBook("10_Eins/20_B.geo.md");
        // the page's heading with its section; axioms through the book, the other statements one counter in the chapter,
        // examples and figures their own
        Assert.Contains("<h1 data-line=\"1\"><span class=\"section-number\">1.2</span> Zweite</h1>", second.Html);
        Assert.Equal(["Axiom 2", "Satz 1.2", "Beispiel 1.2", "Abb. 1.2"], second.Marks.Select(m => m.Label));
        Assert.Contains(">Definition 1.1</a>", second.Html);
        Assert.Contains(">Abschnitt 1.1</a>", second.Html);
        Assert.Equal("1.2", second.SectionNumber);

        // a new chapter starts again, the axioms go on
        MarkdownPage third = PageOfBook("20_Zwei/10_C.geo.md");
        Assert.Equal(["Axiom 3", "Hilfssatz 2.1"], third.Marks.Select(m => m.Label));
        Assert.Contains(">Satz 1.2</a>, <a class=\"ref\" href=\"../10_Eins/20_B.geo.md#ax-b\">Axiom 2</a> und", third.Html);
        Assert.Contains(">Abb. 1.2</a>", third.Html);

        // other rules: through the book, examples with the others, no numbers on headings
        File.WriteAllText(Path.Combine(folder, "index.yaml"), "title: Buch\nnumbering:\n  within: book\n  example: shared\n  headings: no\n");
        MarkdownPage again = PageOfBook("20_Zwei/10_C.geo.md");
        Assert.Equal(["Axiom 3", "Hilfssatz 5"], again.Marks.Select(m => m.Label));
        Assert.DoesNotContain("section-number", again.Html);
        Assert.Contains(">Satz 3</a>", again.Html);
        Assert.Null(again.SectionNumber);
    }

    [Fact]
    public void UnknownNumberingIsAWarning()
    {
        File.WriteAllText(Path.Combine(folder, "index.yaml"), "numbering:\n  axiom: eigen\n  within: kapitel\n  bild: own\n  figure: own page\n");
        var warnings = new List<string>();
        BookSettings settings = BookFile.Read(folder, warnings.Add, out _);
        Assert.Equal(3, warnings.Count);
        Assert.Equal(NumberScope.Page, settings.Numbering.ScopeOf("figure"));
        Assert.Equal(NumberScope.Book, settings.Numbering.ScopeOf("axiom"));
    }

    [Fact]
    public void ABoxHasNoName()
    {
        MarkdownPage page = MarkdownPage.Parse("::: box\nWelche Eigenschaften?\n:::\n\n::: box Merke\nNie negativ.\n:::\n\n::: axiom\nx\n:::\n\n::: box {#bx}\ny\n:::\n");

        Assert.Contains("<div class=\"statement statement-box\" data-line=\"1\">\n<p data-line=\"2\">Welche Eigenschaften?</p>", page.Html);
        Assert.Contains("<span class=\"statement-label\">Merke.</span> Nie negativ.", page.Html);
        // boxes are not counted
        Assert.Contains("Axiom 1.", page.Html);
        Assert.Empty(page.Marks);
        Assert.Contains("A box has no number", Assert.Single(page.Errors).Message);
    }

    [Fact]
    public void ADisplayFormulaInAParagraphStandsOnItsOwnLine()
    {
        MarkdownPage page = MarkdownPage.Parse("Eine Abbildung\n\n$$d: cc E xx cc E -> RR_(>=0)$$.\n\nund $$x$$, weiter.\n");

        Assert.Empty(page.Errors);
        Assert.Matches("<span class=\"formula-display\"><svg[^>]*aria-label=\"d: cc E xx cc E -&gt; RR_\\(&gt;=0\\)\"[^>]*>.*?</svg>\\.</span></p>", page.Html);
        Assert.Matches("</svg>,</span> weiter\\.</p>", page.Html);
    }

    [Fact]
    public void AxiomsAreCountedThroughTheBook()
    {
        File.WriteAllText(Path.Combine(folder, "index.yaml"), "title: Buch\n");
        Directory.CreateDirectory(Path.Combine(folder, "10_Erstes"));
        Directory.CreateDirectory(Path.Combine(folder, "20_Zweites"));
        File.WriteAllText(Path.Combine(folder, "10_Erstes", "10_a.geo.md"), "::: axiom {#ax-eins}\nx\n:::\n\n::: def\ny\n:::\n\n::: axiom {#ax-zwei}\nz\n:::\n");
        File.WriteAllText(Path.Combine(folder, "20_Zweites", "10_c.geo.md"), "::: axiom {#ax-vier}\nx\n:::\n");
        File.WriteAllText(Path.Combine(folder, "_entwurf.geo.md"), "::: axiom\nnicht gezählt\n:::\n");
        // between them (book order: 10_Erstes/10_a, 10_Erstes/20_b, 20_Zweites/10_c), not saved yet
        string file = Path.Combine(folder, "10_Erstes", "20_b.geo.md");
        JsonElement answer = JsonDocument.Parse(Daemon.Handle(JsonSerializer.Serialize(new
        {
            id = 1, type = "markdown", file, text = "::: axiom {#ax-drei}\nx\n:::\n\nNach §ax-zwei und §ax-vier.\n",
        }))).RootElement;

        string html = answer.GetProperty("html").GetString()!;
        Assert.Contains("Axiom 3.", html);
        Assert.Contains(">Axiom 2</a>", html);
        Assert.Contains(">Axiom 4</a>", html);
        Assert.Equal(["Axiom 3", "Axiom 1", "Axiom 2", "Axiom 4"], answer.GetProperty("marks").EnumerateArray().Select(m => m.GetProperty("label").GetString()));
    }

    [Fact]
    public void KeywordsStandAtTheirTerms()
    {
        MarkdownPage page = MarkdownPage.Parse("""
            ---
            keywords: [Lage]
            ---
            Ein **Haus**{kw=/es/e,Häuser/n} und ein **Strahl**{kw}, *Halbgerade*{kw=/n} und **Geraden**{kw=Gerade/n}.

            ## Der Kreis {kw=Kreis/es/e}

            ::: def Strecke {#def-strecke kw=Strecke/n}
            x
            :::

            **Fehler**{kw=/1}
            """);

        Assert.Equal(["Lage", "Haus", "Strahl", "Halbgerade", "Gerade", "Kreis", "Strecke"], page.KeywordDefinitions.Select(k => k.Term));
        Assert.Equal(["Haus", "Hauses", "Hause", "Häuser", "Häusern"], page.KeywordDefinitions[1].Forms);
        Assert.Equal(["Halbgerade", "Halbgeraden"], page.KeywordDefinitions[3].Forms);
        Assert.Equal([null, "kw-haus", "kw-strahl", "kw-halbgerade", "kw-gerade", "kw-kreis", "def-strecke"], page.KeywordDefinitions.Select(k => k.Anchor));
        Assert.Contains("<strong id=\"kw-haus\">Haus</strong>", page.Html);
        Assert.Contains("<h2 id=\"kw-kreis\" data-line=\"6\">Der Kreis</h2>", page.Html);
        Assert.DoesNotContain("kw=", page.Html);
        MarkdownProblem error = Assert.Single(page.Errors);
        Assert.Equal(12, error.Line);
    }

    [Fact]
    public void KeywordsOfOtherPagesAreLinkedOncePerSection()
    {
        var links = new Dictionary<string, KeywordLink>
        {
            ["Punkt"] = new KeywordLink("punkte.geo.md#kw-punkt", "Punkte"),
            ["Punkte"] = new KeywordLink("punkte.geo.md#kw-punkt", "Punkte"),
            ["rechter Winkel"] = new KeywordLink("winkel.geo#3", "Winkel"),
            ["Winkel"] = new KeywordLink("winkel.geo", "Winkel"),
        };
        MarkdownPage page = MarkdownPage.Parse("""
            Ein Punkt, noch ein Punkt und Punkte; ein rechter Winkel ist ein Winkel. \Winkel bleibt Text, \Wort auch.

            ## Punkt im Titel

            Wieder ein Punkt, [ein Punkt](x.md), **Punkt**{kw} und $Punkt$.
            """, null, null, null, null, links);

        // once per section and target: Punkt, rechter Winkel, Winkel; after the heading Punkt again
        Assert.Equal(4, page.Html.Split("class=\"keyword\"").Length - 1);
        Assert.Contains("Ein <a href=\"punkte.geo.md#kw-punkt\" class=\"keyword\" title=\"Punkt – Punkte\">Punkt</a>, noch ein Punkt und Punkte;", page.Html);
        Assert.Contains("ein <a href=\"winkel.geo#3\" class=\"keyword\" title=\"rechter Winkel – Winkel\">rechter Winkel</a> ist ein <a href=\"winkel.geo\"", page.Html);
        Assert.Contains("Winkel bleibt Text, Wort auch.", page.Html);
        Assert.Contains("<h2 data-line=\"3\">Punkt im Titel</h2>", page.Html);
        Assert.Contains("Wieder ein <a href=\"punkte.geo.md#kw-punkt\"", page.Html);
    }

    [Fact]
    public void AStatementCanStandInABoxWithMoreColons()
    {
        // as the editor puts a block around one: the outer one with a colon more
        MarkdownPage page = MarkdownPage.Parse(":::: box\n::: theorem\nSatz.\n:::\nBeweistext.\n::::\n");
        Assert.Empty(page.Errors);
        Assert.Matches("(?s)<div class=\"statement statement-box\"[^>]*>.*<div class=\"statement statement-theorem\"[^>]*>.*Satz[.].*</div>.*Beweistext[.].*</div>", page.Html);
    }

    [Fact]
    public void KeywordsAreLinkedAfterAReference()
    {
        var links = new Dictionary<string, KeywordLink> { ["Punkt"] = new KeywordLink("punkte.geo.md#kw-punkt", "Punkte") };
        MarkdownPage page = MarkdownPage.Parse("::: axiom {#ax}\nText.\n:::\n\nNach §ax gilt es für jeden Punkt.\n", null, null, null, null, links);
        Assert.Contains("für jeden <a href=\"punkte.geo.md#kw-punkt\" class=\"keyword\"", page.Html);
    }

    [Fact]
    public void DaemonLinksTheKeywordsOfTheBook()
    {
        File.WriteAllText(Path.Combine(folder, "index.yaml"), "title: Buch\n");
        File.WriteAllText(Path.Combine(folder, "10_Punkte.geo.md"), "Ein **Punkt**{kw=/e/en} ist da.\n");
        File.WriteAllText(Path.Combine(folder, "20_Winkel.geo"), "keyword Winkel/s\npoint A = 0 0\nstep Rechte\nkeyword rechter Winkel\n");
        // a script with a page of its own: only the page's keywords count
        File.WriteAllText(Path.Combine(folder, "30_Kreis.geo"), "keyword Kreis\npoint A = 0 0\n");
        string file = Path.Combine(folder, "30_Kreis.geo.md");
        JsonElement answer = JsonDocument.Parse(Daemon.Handle(JsonSerializer.Serialize(new
        {
            id = 1, type = "markdown", file, text = "Ein Punkt, ein rechter Winkel und ein Winkel. Auch ein **Punkt**{kw}. Ein Kreis.\n",
        }))).RootElement;

        string html = answer.GetProperty("html").GetString()!;
        Assert.Contains("<a href=\"10_Punkte.geo.md#kw-punkt\" class=\"keyword\" title=\"Punkt – 10_Punkte.geo.md\">Punkt</a>", html);
        Assert.Contains("<a href=\"20_Winkel.geo#1\" class=\"keyword\" title=\"rechter Winkel – Winkel\">rechter Winkel</a>", html);
        Assert.Contains("<a href=\"20_Winkel.geo\" class=\"keyword\" title=\"Winkel – Winkel\"", html);
        Assert.DoesNotContain("30_Kreis.geo", html);
        JsonElement warning = Assert.Single(answer.GetProperty("warnings").EnumerateArray());
        Assert.Contains("already the keyword of 10_Punkte.geo.md", warning.GetProperty("message").GetString());
    }

    [Fact]
    public void KeywordValuesMayHaveSpaces()
    {
        MarkdownPage page = MarkdownPage.Parse("""
            ## Metrische Räume{kw=Metrik/en, metrische/r Raum}

            Ein **Haus**{kw=/es, Häuser/n} steht. **Ort**{#ort-mark kw} auch.
            """);

        Assert.Equal(["Metrik", "Haus", "Ort"], page.KeywordDefinitions.Select(k => k.Term));
        Assert.Equal(["Metrik", "Metriken", "metrische Raum", "metrischer Raum"], page.KeywordDefinitions[0].Forms);
        Assert.Equal(["Haus", "Hauses", "Häuser", "Häusern"], page.KeywordDefinitions[1].Forms);
        Assert.Equal("ort-mark", page.KeywordDefinitions[2].Anchor);
        Assert.Contains("<h2 id=\"kw-metrik\" data-line=\"1\">Metrische Räume</h2>", page.Html);
        Assert.Contains("<strong id=\"kw-haus\">Haus</strong> steht.", page.Html);
        Assert.Empty(page.Errors);
    }
}
