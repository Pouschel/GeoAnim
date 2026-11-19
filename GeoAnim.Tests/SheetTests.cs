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

    private const string Random = """
        ---
        type: test
        groups: [A, B, C, D]
        seed: 482
        ---

        ::: let
        n = random 3..6
        z = random 9..63 except 32
        k = 2^n - random(1, 3)
        planeten = Merkur, Venus, Erde, Mars, Jupiter
        p = pick 3 from planeten
        mond = alt Phobos | Io
        :::

        {{n}} Byte sind [[{{8*n}}]] Bit; ({{bin(z)}})₂ = [[{{z}}]]; {{k}} < 2^{{n}}.

        Ordne: {{shuffle(p)}} [[{{p}}]]

        Ein Mond {{alt: des Mars | des Jupiter | des Saturn}}: [[{{mond}}]]

        ::: pick 3
        + richtig 1
        + richtig 2
        + richtig 3
        - falsch 1
        - falsch 2
        :::
        """;

    // the text of a sheet without its markup (blanks with solutions as [[…]])
    private static string Text(string html) => System.Text.RegularExpressions.Regex.Replace(
        System.Text.RegularExpressions.Regex.Replace(html, "<span class=\"blank blank-solution\"[^>]*>(.*?)</span>", "[[$1]]"), "<[^>]+>", "");

    [Fact]
    public void GroupsHaveTheirOwnValuesTheSameOnBothSheets()
    {
        string Sheet(string group, bool solution) => Text(MarkdownPage.Parse(Random, variant: new SheetVariant(group, solution)).Html);
        Assert.Empty(MarkdownPage.Parse(Random).Errors);
        var match = System.Text.RegularExpressions.Regex.Match(Sheet("A", true), @"(\d) Byte sind \[\[(\d+)\]\] Bit; \((\d+)\)₂ = \[\[(\d+)\]\]; (\d+) &lt; 2\^(\d)");
        Assert.True(match.Success, Sheet("A", true));
        int n = int.Parse(match.Groups[1].Value);
        Assert.InRange(n, 3, 6);
        Assert.Equal(8 * n, int.Parse(match.Groups[2].Value));
        Assert.Equal(System.Convert.ToInt32(match.Groups[3].Value, 2), int.Parse(match.Groups[4].Value));
        Assert.NotEqual(32, int.Parse(match.Groups[4].Value));
        Assert.InRange((1 << n) - int.Parse(match.Groups[5].Value), 1, 3);
        // the students' sheet has the same values (only the solutions missing); again the same
        Assert.Contains($"{n} Byte sind  Bit; ({match.Groups[3].Value})₂ =", Sheet("A", false));
        Assert.Equal(Sheet("A", true), Sheet("A", true));
        // the groups differ (one of them at least)
        Assert.True(new[] { "B", "C", "D" }.Any(g => Sheet(g, true) != Sheet("A", true)));
        // alternatives by group, from the start again when there are fewer
        Assert.Contains("Ein Mond des Mars: [[Phobos]]", Sheet("A", true));
        Assert.Contains("Ein Mond des Jupiter: [[Io]]", Sheet("B", true));
        Assert.Contains("Ein Mond des Saturn: [[Phobos]]", Sheet("C", true));
        Assert.Contains("Ein Mond des Mars: [[Io]]", Sheet("D", true));
        // picked in the order of the list as the solution, shown mixed
        var order = System.Text.RegularExpressions.Regex.Match(Sheet("A", true), @"Ordne: ([^\[]+) \[\[([^\]]+)\]\]");
        string[] shown = order.Groups[1].Value.Split(", "), solution = order.Groups[2].Value.Split(", ");
        Assert.Equal(3, solution.Length);
        Assert.Equal(solution.OrderBy(p => Array.IndexOf(new[] { "Merkur", "Venus", "Erde", "Mars", "Jupiter" }, p)), solution);
        Assert.Equal(solution.Order(), shown.Order());
        // true or false: 3 of them, at least one of each, the answer as a blank
        foreach (string group in new[] { "A", "B", "C", "D" })
        {
            string sheet = Sheet(group, true);
            int right = System.Text.RegularExpressions.Regex.Matches(sheet, @"richtig \d \[\[wahr\]\]").Count;
            int wrong = System.Text.RegularExpressions.Regex.Matches(sheet, @"falsch \d \[\[falsch\]\]").Count;
            Assert.Equal(3, right + wrong);
            Assert.True(right >= 1 && wrong >= 1);
        }
    }

    [Fact]
    public void MistakesInTheGroupsValuesAreErrorsWithTheirLines()
    {
        MarkdownPage page = MarkdownPage.Parse("---\ntype: test\n---\n\n::: let\nx = 2^q + 1\nquatsch\n:::\n\n{{x}} und {{sol 3}}\n\n::: pick 9\n+ a\n:::\n");
        Assert.Contains(page.Errors, e => e.Line == 6 && e.Message.Contains("no value \"q\""));
        Assert.Contains(page.Errors, e => e.Line == 7 && e.Message.Contains("name = …"));
        Assert.Contains(page.Errors, e => e.Line == 10 && e.Message.Contains("[[sol: …]]"));
        Assert.Contains(page.Errors, e => e.Line == 12 && e.Message.Contains("only 1 lines"));
    }
    [Fact]
    public void GeneratorsAreCSharpProjectsWithWhatTheyUse()
    {
        // a project with a class for the sheet, which uses another project
        Directory.CreateDirectory(Path.Combine(folder, "Lib"));
        Directory.CreateDirectory(Path.Combine(folder, "Gen"));
        File.WriteAllText(Path.Combine(folder, "Lib", "Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(folder, "Lib", "Words.cs"), """
            namespace Lib;
            public static class Words
            {
                public static string Planet(int i) => new[] { "Mars", "Saturn" }[i];
            }
            """);
        File.WriteAllText(Path.Combine(folder, "Gen", "Gen.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
              <ItemGroup><ProjectReference Include="..\Lib\Lib.csproj" /></ItemGroup>
            </Project>
            """);
        string source = Path.Combine(folder, "Gen", "BinDec.cs");
        File.WriteAllText(source, """
            namespace Info10;
            public class BinDec(Random random, int group, string name)
            {
                public int N = random.Next(3, 7);
                public string Group => name + group;
                public string Planet => Lib.Words.Planet(group);
                public string[] Items => ["a", "b"];
                public static int Twice(int x) => 2 * x;
                public string Task(string label, int bits)
                {
                    int value = random.Next(1 << (bits - 1), 1 << bits);
                    return $"{label}) ({Convert.ToString(value, 2)})₂ = [[{value}]]";
                }
                public string Boom() => throw new InvalidOperationException("kaputt");
            }
            """);
        string sheet = """
            ---
            type: test
            groups: [A, B]
            generators: Gen/Gen.csproj
            ---

            ::: let
            bc = Info10.BinDec
            bc.N = 9
            :::

            {{bc.Group}} {{bc.N}} {{bc.Planet}} {{BinDec.Twice(21)}} {{join(bc.Items, "+")}}

            {{bc.Task("a", 4)}}

            {{bc.Boom()}} {{bc.Nix}}
            """;
        string file = Path.Combine(folder, "lek.geo.md");
        File.WriteAllText(file, sheet);
        List<MarkdownProblem> Errors(string group) => MarkdownPage.Parse(sheet, file, variant: new SheetVariant(group, true)).Errors.ToList();
        string Expanded(string group)
        {
            var errors = new List<MarkdownProblem>();
            string text = SheetScript.Expand(sheet, 1, group == "A" ? 0 : 1, errors, group, [Path.Combine(folder, "Gen", "Gen.csproj")]);
            return text;
        }
        // its group's number (from 0) and name, the other project, statics, lists; the group's own chance, always the same
        Assert.Contains("B1 9 Saturn 42 a+b", Expanded("B"));
        Assert.Contains("A0 9 Mars 42 a+b", Expanded("A"));
        var task = System.Text.RegularExpressions.Regex.Match(Expanded("B"), @"a\) \(([01]{4})\)₂ = \[\[(\d+)\]\]");
        Assert.True(task.Success, Expanded("B"));
        Assert.Equal(System.Convert.ToInt32(task.Groups[1].Value, 2), int.Parse(task.Groups[2].Value));
        Assert.Equal(Expanded("B"), Expanded("B"));
        // the generator's mistakes: errors of the sheet, at their lines
        List<MarkdownProblem> errors = Errors("A");
        Assert.Contains(errors, e => e.Line == 16 && e.Message.Contains("BinDec.Boom: InvalidOperationException: kaputt"));
        Assert.Contains(errors, e => e.Line == 16 && e.Message.Contains("no public member \"Nix\" (Boom, Group, Items, N, Planet, Task, Twice)"));
        // changed: built and loaded again
        File.WriteAllText(source, File.ReadAllText(source).Replace("name + group", "\"Gruppe \" + name"));
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(2));
        Assert.Contains("Gruppe B 9 Saturn", Expanded("B"));
        // does not build: its errors
        File.WriteAllText(source, File.ReadAllText(source).Replace("public int N", "public int N N"));
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(4));
        Assert.Contains(Errors("A"), e => e.Line == 8 && e.Message.Contains("Gen.csproj does not build: BinDec.cs(4): CS"));
    }
}
