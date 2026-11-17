using GeoAnim.Cli;
using GeoAnim.Viewer;

namespace GeoAnim.Tests;

public sealed class SheetTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "geoanim-sheet-" + Guid.NewGuid().ToString("N"));

    public SheetTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private const string Test = """
        ---
        type: test
        title: LEK Binärzahlen
        subject: Informatik 10
        time: 20 min
        groups: [A, B]
        ---

        ## Bits und Bytes {3}

        5 Byte sind [[40]] Bit, und $2^2=$ [[$4$|3em]].

        ## Wahr oder falsch {1,5}

        Satelliten? [[sol: falsch]]

        \space 2cm

        \page

        ## Zusatzaufgabe {+1}

        Ein Marsmond: [[Phobos]]

        \points
        """;

    [Fact]
    public void ATestHasItsHeadTasksBlanksAndPoints()
    {
        MarkdownPage students = MarkdownPage.Parse(Test, variant: new SheetVariant("B", Solution: false));
        Assert.Empty(students.Errors);
        Assert.Equal("test", students.Document!.Type);
        Assert.Equal(["A", "B"], students.Document.Groups);
        Assert.Contains("<span class=\"sheet-name\">Informatik 10: LEK Binärzahlen</span><span class=\"sheet-info\">Gruppe B · 20 min</span>",
            System.Net.WebUtility.HtmlDecode(students.Html));
        // numbered tasks with their points; an extra task without a number
        Assert.Contains(">1. Bits und Bytes (3 P)</h2>", students.Html);
        Assert.Contains(">2. Wahr oder falsch (1,5 P)</h2>", students.Html);
        Assert.Contains(">Zusatzaufgabe (1 P)</h2>", students.Html);
        // the students' sheet: lines, no answer in it at all
        Assert.Contains("<span class=\"blank\" style=\"width: 5em\"></span>", students.Html);
        Assert.Contains("<span class=\"blank\" style=\"width: 3em\"></span>", students.Html);
        Assert.DoesNotContain(">40<", students.Html);
        Assert.DoesNotContain("falsch<", students.Html);
        Assert.DoesNotContain("Phobos", students.Html);
        Assert.Contains("<div class=\"space\" style=\"height: 2cm\"></div>", students.Html);
        Assert.Contains("<div class=\"page-break\"></div>", students.Html);
        // the points and the grade: the sum without the extra task; from how many points a grade (rounded up)
        Assert.Contains("</span> / 4,5 Punkte<", students.Html);
        Assert.Contains("<tr><th>Note</th><td>1</td><td>2</td><td>3</td><td>4</td><td>5</td></tr><tr><th>ab Punkte</th><td>5</td><td>4</td><td>3</td><td>2</td><td>1</td></tr>", students.Html);
        Assert.Contains("<td>1+</td><td>1</td><td>2+</td>", MarkdownPage.Parse(Test.Replace("\\points", "\\points +")).Html);

        MarkdownPage solutions = MarkdownPage.Parse(Test, variant: new SheetVariant("A", Solution: true));
        Assert.Contains("<span class=\"blank blank-solution\" style=\"min-width: 5em\">40</span>", solutions.Html);
        Assert.Contains("<span class=\"solution\">falsch</span>", solutions.Html);
        Assert.Contains("Lösung · Gruppe A", System.Net.WebUtility.HtmlDecode(solutions.Html));
        // the preview: with the solutions, the first group
        Assert.Contains("Lösung · Gruppe A", System.Net.WebUtility.HtmlDecode(MarkdownPage.Parse(Test).Html));
        // a page of a book has none of it
        Assert.Null(MarkdownPage.Parse("# Seite\n\nText.\n").Document);
    }

    [Fact]
    public void TheSheetsOfATest()
    {
        string file = Path.Combine(folder, "lek.geo.md");
        File.WriteAllText(file, Test);
        Assert.Equal(0, Program.Sheet([file]));
        Assert.True(File.Exists(Path.Combine(folder, "lek-A.html")));
        Assert.True(File.Exists(Path.Combine(folder, "lek-B.html")));
        Assert.DoesNotContain("Phobos", File.ReadAllText(Path.Combine(folder, "lek-B.html")));
        string all = System.Net.WebUtility.HtmlDecode(File.ReadAllText(Path.Combine(folder, "lek-Lsg.html")));
        Assert.True(all.IndexOf("Lösung · Gruppe A", StringComparison.Ordinal) < all.IndexOf("Lösung · Gruppe B", StringComparison.Ordinal));

        // the Typst project: a main file per group and one for the solutions
        // (the PDF itself only where Typst is installed)
        Program.Sheet([file, "-pdf"]);
        string project = Path.Combine(folder, "lek-typst");
        Assert.Contains("#show: sheet", File.ReadAllText(Path.Combine(project, "lek-A.typ")));
        string solutionsMain = File.ReadAllText(Path.Combine(project, "lek-Lsg.typ"));
        Assert.Contains("#include \"pages/lek-A-Lsg.typ\"", solutionsMain);
        Assert.Contains("#include \"pages/lek-B-Lsg.typ\"", solutionsMain);
        string page = File.ReadAllText(Path.Combine(project, "pages", "lek-A.typ"));
        Assert.Contains("#sheet-head(\"Informatik 10: LEK Binärzahlen\", info: \"Gruppe A · 20 min\", solution: false", page);
        Assert.Contains("#heading(level: 2, outlined: false)[1\\. Bits und Bytes (3 P)]", page);
        Assert.Contains("#blank(5em)", page);
        Assert.Contains("#v(2cm)", page);
        Assert.Contains("#pagebreak(weak: true)", page);
        Assert.Contains("#blank-solution(5em)[40]", File.ReadAllText(Path.Combine(project, "pages", "lek-A-Lsg.typ")));

        File.WriteAllText(file, "# Seite\n\nText.\n");
        Assert.Equal(1, Program.Sheet([file]));
    }
}
