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
        MarkdownPage page = MarkdownPage.Parse("# Seite\n\n::: nassi ja|nein\nif x\n    a\nelse\n    b\n:::\n\nDanach.\n\n::: nassi\na\n  else\n:::\n");
        Assert.Contains("<figure class=\"nassi\" data-width=\"", page.Html);
        Assert.Contains("<svg class=\"nassi-picture\" xmlns=", page.Html);
        Assert.Contains("<p data-line=\"10\">Danach.</p>", page.Html);
        Assert.Contains(page.Errors, e => e.Line == 14);
    }

    [Fact]
    public void KindsWithoutTheirWords()
    {
        var errors = new List<(int, string)>();
        // "#repeat …", "#if …", "#break …": the box of its kind, only the text after the word in it
        string hidden = Draw("#repeat wiederhole 5-mal\n    #if x kleiner als 3\n        #break Schleife verlassen\n    #else if x gleich 3\n        continue;", null, errors).Svg;
        string shown = Draw("repeat wiederhole 5-mal\n    if x kleiner als 3\n        break Schleife verlassen\n    else if x gleich 3\n        continue;", null, errors).Svg;
        Assert.Empty(errors);
        Assert.NotEqual(hidden, shown);
        // the jumps: their arrow (a path with the colour of jumps), twice
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(hidden, "fill=\"#e9e2f5\"").Count);
        // "wiederhole" is no longer a word for a loop: one box
        string plain = Draw("wiederhole das Wort", null, errors).Svg;
        Assert.DoesNotContain("#e6f3ec", plain);
        // an empty branch: ∅
        Assert.Contains("<circle ", Draw("if (x)\n    a", null, errors).Svg);
    }

    [Fact]
    public void TitleAndSettingsInBraces()
    {
        MarkdownPage page = MarkdownPage.Parse("::: nassi {numbered ja|nein} Summe der Zahlen\nif x\n    a\n:::\n\n::: nassi Summe der Zahlen\na\n:::\n\n::: nassi\na\n:::\n\n::: nassi numbered\na\n:::\n\n::: nassi {nummeriert}\na\n:::\n");
        Assert.Single(page.Errors);
        // the title drawn: higher than without
        var heights = System.Text.RegularExpressions.Regex.Matches(page.Html, "height=\"([0-9.]+)pt\"").Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.True(heights[1] > heights[2] + 10);
    }
}
