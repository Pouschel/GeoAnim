using System.Text.Json;
using GeoAnim.Cli;
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
}
