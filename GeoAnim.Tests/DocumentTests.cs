using GeoAnim.Viewer;

namespace GeoAnim.Tests;

public sealed class DocumentTests
{
    [Fact]
    public void ADocumentWithItsContentsBoxesAndFloatingPictures()
    {
        MarkdownPage page = MarkdownPage.Parse("""
            ---
            type: document
            title: Einführung
            ---

            # Einführung

            \toc 2-3

            ## Ausgabe mit `print`

            ### Beispiel {5}

            ::: merke
            Anweisungen werden mit `;` beendet.
            :::

            ## Kommentare

            \clear
            """);
        Assert.Empty(page.Errors);
        // no head with names, no tasks with points (a heading's "{5}" stays text)
        Assert.DoesNotContain("sheet-head", page.Html);
        Assert.Empty(page.Tasks);
        // the contents: nested, linked; the headings with their ids
        Assert.Contains("<nav class=\"toc\" data-levels=\"2-3\"><ul><li><a href=\"#ausgabe-mit-print\">Ausgabe mit <code>print</code></a><ul><li><a href=\"#beispiel-5\">", page.Html);
        Assert.Contains("</a></li></ul></li><li><a href=\"#kommentare\">Kommentare</a></li></ul></nav>", page.Html);
        Assert.Contains("<h2 id=\"kommentare\"", page.Html);
        Assert.Contains("class=\"statement statement-merke\"", page.Html);
        Assert.DoesNotContain("Merke.", page.Html);
        Assert.Contains("<div class=\"clear\"></div>", page.Html);
    }

    [Fact]
    public void WichtigIsMerke()
    {
        MarkdownPage page = MarkdownPage.Parse("::: wichtig\nHeft mitbringen.\n:::\n");
        Assert.Contains("<div class=\"statement statement-merke\"", page.Html);
        Assert.Matches("<p[^>]*>Heft mitbringen.</p>", page.Html);
    }

    [Fact]
    public void SpaceAndPageInDocuments()
    {
        MarkdownPage page = MarkdownPage.Parse("---\ntype: document\n---\n\nText\n\n\\space 4cm\n\n\\page\n\n\\points\n");
        Assert.Contains("<div class=\"space\" style=\"height: 4cm\"></div>", page.Html);
        Assert.Contains("<div class=\"page-break\"></div>", page.Html);
        Assert.Single(page.Errors);
    }
}
