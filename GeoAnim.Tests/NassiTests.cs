using GeoAnim.Figures;
using GeoAnim.Viewer;

namespace GeoAnim.Tests;

public sealed class NassiTests
{
    private static NassiImage Draw(string text, NassiOptions? options, List<(int Line, string Message)> errors) =>
        NassiDiagram.Render(text.Split('\n'), options ?? new NassiOptions(), errors);

    [Fact]
    public void BoxesLoopsAndDecisions()
    {
        var errors = new List<(int, string)>();
        NassiImage image = Draw("""
            print("Merle");
            repeat (4)
                print("Puschel");
            if (x > y)
                print(x);
            else
                print(y);
            """, null, errors);
        Assert.Empty(errors);
        // a box each: the instructions, the loop and its body, the decision's head and its two branches
        Assert.Equal(1 + 6, System.Text.RegularExpressions.Regex.Matches(image.Svg, "<rect ").Count);
        // the slopes of the decision
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(image.Svg, "<line ").Count);
        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"", image.Svg);
        Assert.True(image.Width > 100 && image.Height > 100);
    }

    [Fact]
    public void MistakesHaveTheirLines()
    {
        var errors = new List<(int Line, string Message)>();
        Draw("a\n    b\nelse\n    c", null, errors);
        Assert.Contains(errors, e => e.Line == 2 && e.Message.Contains("indented more"));
        Assert.Contains(errors, e => e.Line == 3 && e.Message.Contains("belongs right below an if"));
    }

    [Fact]
    public void NumberedBoxesAndBlanks()
    {
        var errors = new List<(int, string)>();
        string numbered = Draw("print(1);\nrepeat (2)\n    print(3);", new NassiOptions { Numbered = true }, errors).Svg;
        string plain = Draw("print(1);\nrepeat (2)\n    print(3);", null, errors).Svg;
        Assert.NotEqual(plain, numbered);
        // a blank: the answer red with the solutions, only the line for the students
        Assert.Contains("#d32f2f", Draw("repeat ([[4]])\n    move();", new NassiOptions { Solution = true }, errors).Svg);
        Assert.DoesNotContain("#d32f2f", Draw("repeat ([[4]])\n    move();", new NassiOptions { Solution = false }, errors).Svg);
        Assert.Empty(errors);
    }

    [Fact]
    public void InAPageAPictureWhereTheBlockWas()
    {
        MarkdownPage page = MarkdownPage.Parse("# Seite\n\n::: nassi ja|nein\nwenn x\n    a\nsonst\n    b\n:::\n\nDanach.\n\n::: nassi\na\n  else\n:::\n");
        Assert.Contains("<figure class=\"nassi\" data-width=\"", page.Html);
        Assert.Contains("<svg class=\"nassi-picture\" xmlns=", page.Html);
        Assert.Contains("<p data-line=\"10\">Danach.</p>", page.Html);
        Assert.Contains(page.Errors, e => e.Line == 14);
    }
}
