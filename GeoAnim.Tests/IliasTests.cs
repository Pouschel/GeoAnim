using System.IO.Compression;
using System.Xml.Linq;
using GeoAnim.Cli;
using GeoAnim.Viewer;

namespace GeoAnim.Tests;

public sealed class IliasTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "geoanim-ilias-" + Guid.NewGuid().ToString("N"));

    public IliasTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private const string Questions = """
        ---
        type: ilias
        title: LEK Repeat
        groups: [U, A, N]
        ---

        Hilfsmittel: EleuStudio

        ## Passwort

        ::: choice
        - zurückgesetzt
        + __nicht__ zurückgesetzt
        :::

        ## Ausgabe @A N

        ::: let
        z = alt 1234 | 4321 | 5678
        :::

        ```eleu
        print({{z}}); // Ausgabe
        ```

        ::: pick ja|nein
        + `print({{z}});`
        + `print("{{z}}");`
        - `print("{{z}});`
        :::

        ## Datentypen {4 cs}

        `"3.14`: [[Number | String | *kein gültiger Datentyp]]

        Ein Byte hat [[zwei | vier | *acht]]{ordered} Bits, ein Kibibyte [[1024]]{2 chars=6} Byte.

        ## Ohne Übung @-U

        [[*a | b]]

        ## Operatoren {5}

        ::: match
        Zuweisung -> `=`
        Gleichheit -> `==`
        -> `===`
        :::

        ## Geparkt @-

        [[x]]
        """;

    private static string Text(string html) => System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", ""));

    // a table's cells with a space between them
    private static string Cells(string html) => System.Text.RegularExpressions.Regex.Replace(
        System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")), @"\s+", " ");

    [Fact]
    public void QuestionsForIliasHaveTheirKindsPointsAndGroups()
    {
        MarkdownPage u = MarkdownPage.Parse(Questions, variant: new SheetVariant("U", Solution: false));
        Assert.Empty(u.Errors);
        // always with the answers; a question only for some groups, one put aside
        Assert.Contains("<span class=\"choice-right\"></span>", u.Html);
        Assert.Equal(["Passwort", "Datentypen", "Operatoren"], u.Tasks.Select(t => t.Title));
        Assert.Contains("Ohne Übung", MarkdownPage.Parse(Questions, variant: new SheetVariant("N", Solution: true)).Tasks.Select(t => t.Title));
        Assert.Equal([1.0, 4, 5], u.Tasks.Select(t => t.Points));
        MarkdownPage a = MarkdownPage.Parse(Questions, variant: new SheetVariant("A", Solution: true));
        Assert.Equal(["Passwort", "Ausgabe", "Datentypen", "Ohne Übung", "Operatoren"], a.Tasks.Select(t => t.Title));
        Assert.Contains(">2. Ausgabe (3 P)</h2>", a.Html);
        // values in code too, in colour
        Assert.Contains("<span class=\"tk-fn\">print</span>(<span class=\"tk-num\">4321</span>); <span class=\"tk-com\">// Ausgabe</span>", a.Html);
        // true or false as a choice of the two, in their order
        Assert.Contains("data-gap=\"{&quot;choices&quot;:[&quot;ja&quot;,&quot;nein&quot;],&quot;right&quot;:[0],&quot;points&quot;:1,&quot;ordered&quot;:true}\"", a.Html);
        // the choices mixed by the group's chance, the same every time; "ordered" as written
        Assert.Equal(a.Html, MarkdownPage.Parse(Questions, variant: new SheetVariant("A", Solution: true)).Html);
        Assert.Contains("zwei | vier | acht (1 P) Bits", Text(a.Html));
        Assert.Contains("1024 (2 P) Byte", Text(a.Html));
        Assert.Contains("<div class=\"match\" data-shuffle=\"terms\" data-pairs=\"2\">", a.Html);
        // the points of all groups: each question, its kind, the sums (U without "Ausgabe")
        string overview = Cells(MarkdownPage.PointsOverview(Questions, null, null, a, "A"));
        Assert.Contains("Ausgabe Lückentext – 3 3", overview);
        Assert.Contains("Operatoren Zuordnung 5 5 5", overview);
        Assert.Contains("Summe 10 14 14", overview);
        Assert.Contains("davon Lückentext 4 8 8", overview);
        Assert.Contains("Die Summen sind verschieden: U 10, A 14, N 14.", overview);
    }

    [Fact]
    public void LinesWithBlanksMixedByPick()
    {
        const string text = """
            ---
            type: ilias
            groups: [U, A, N]
            ---

            ## Datentypen

            ::: pick
            `1`: [[*Number | String]]{ordered}
            `"2"`: [[Number | *String]]{ordered}
            `3.5`: [[*Number | String]]{ordered}
            `"x"`: [[Number | *String]]{ordered}
            :::
            """;
        List<string> Order(string group) => System.Text.RegularExpressions.Regex.Matches(
            MarkdownPage.Parse(text, variant: new SheetVariant(group, true)).Html, "<p data-line=\"\\d+\"><code>([^<]+)</code>").Select(m => m.Groups[1].Value).ToList();
        // each a paragraph, all of them, mixed: not as written, and differently in the groups
        Assert.Equal(4, Order("A").Count);
        Assert.NotEqual(["1", "&quot;2&quot;", "3.5", "&quot;x&quot;"], Order("A"));
        Assert.True(new[] { "U", "N" }.Any(g => !Order(g).SequenceEqual(Order("A"))));
        Assert.DoesNotContain("<li", MarkdownPage.Parse(text, variant: new SheetVariant("A", true)).Html);
    }

    [Fact]
    public void MistakesInQuestionsForIlias()
    {
        MarkdownPage page = MarkdownPage.Parse("""
            ---
            type: ilias
            groups: [A, B]
            ---

            ## Zu viel {3}

            [[*a | b]] und [[c]]

            ## Nichts

            Nur Text.

            ## Fremd @C

            [[x]]

            ## Zwei richtige

            ::: choice
            + a
            + b
            :::

            ## Doppelt

            ::: match
            a -> x
            b -> x
            :::
            """);
        Assert.Contains(page.Errors, e => e.Line == 6 && e.Message.Contains("The blanks give 2 points, the heading says 3"));
        Assert.Contains(page.Errors, e => e.Line == 10 && e.Message.Contains("needs blanks"));
        Assert.Contains(page.Errors, e => e.Line == 14 && e.Message.Contains("no group \"C\""));
        Assert.Contains(page.Errors, e => e.Line == 20 && e.Message.Contains("exactly one right answer"));
        Assert.Contains(page.Warnings, w => w.Line == 27 && w.Message.Contains("n:n"));
    }

    [Fact]
    public void ThePoolsForIlias()
    {
        string file = Path.Combine(folder, "lek.geo.md");
        File.WriteAllText(file, Questions);
        Assert.Equal(0, Program.Sheet([file, "-pdf"]));
        Assert.True(File.Exists(Path.Combine(folder, "lek-U.html")));
        using ZipArchive zip = ZipFile.OpenRead(Path.Combine(folder, "lek-A__qti_1.zip"));
        Assert.Equal("lek-A__qti_1/lek-A__qti_1.xml", Assert.Single(zip.Entries).FullName);
        XDocument pool = XDocument.Load(zip.Entries[0].Open());
        List<XElement> items = pool.Root!.Elements("item").ToList();
        Assert.Equal(["Passwort", "Ausgabe", "Datentypen", "Ohne Übung", "Operatoren"], items.Select(i => (string)i.Attribute("title")!));
        string Kind(XElement item) => item.Descendants("qtimetadatafield").First(f => (string)f.Element("fieldlabel")! == "QUESTIONTYPE").Element("fieldentry")!.Value;
        Assert.Equal(["SINGLE CHOICE QUESTION", "CLOZE QUESTION", "CLOZE QUESTION", "CLOZE QUESTION", "MATCHING QUESTION"], items.Select(Kind));
        // single choice: the right one with the point
        XElement single = items[0];
        Assert.Equal(1, single.Descendants("respcondition").Where(c => c.Element("conditionvar")!.Element("varequal") != null).Sum(c => (double)c.Element("setvar")!));
        // cloze: the blanks in the text, choices with their points, written answers (cs: as written)
        XElement cloze = items[2];
        Assert.Equal(["gap_0", "gap_1", "gap_2"], cloze.Descendants("response_str").Select(r => (string)r.Attribute("ident")!));
        Assert.Equal("No", (string)cloze.Descendants("response_str").ElementAt(1).Element("render_choice")!.Attribute("shuffle")!);
        Assert.Equal("6", (string)cloze.Descendants("render_fib").Single().Attribute("maxchars")!);
        Assert.Contains(cloze.Descendants("respcondition"), c => c.Descendants("varequal").Single().Value == "1024" && (string)c.Element("setvar")! == "2");
        Assert.Contains(cloze.Descendants("respcondition"), c => c.Descendants("varequal").Single().Value == "kein gültiger Datentyp" && (string)c.Element("setvar")! == "1");
        Assert.Equal("cs", cloze.Descendants("qtimetadatafield").First(f => f.Element("fieldlabel")!.Value == "textgaprating").Element("fieldentry")!.Value);
        // matching: 5 points for 2 pairs (3 + 2), the term that fits nothing among them, 1:1
        XElement matching = items[4];
        Assert.Equal([3, 2], matching.Descendants("respcondition").Select(c => (int)c.Element("setvar")!));
        Assert.Equal(5, matching.Descendants("response_label").Count());
        Assert.Equal("1:1", matching.Descendants("qtimetadatafield").First(f => f.Element("fieldlabel")!.Value == "matching_mode").Element("fieldentry")!.Value);
        // the question above (all before the first blank's paragraph), the cloze text below: a paragraph for every item
        // (ILIAS keeps no list there that the blanks cut into pieces)
        List<string> texts = items[1].Element("presentation")!.Element("flow")!.Elements("material").Select(m => m.Element("mattext")!.Value).ToList();
        Assert.DoesNotContain("<li>", texts[0]);
        Assert.StartsWith("<p><code>print(", texts[1]);
        Assert.Contains("(1 P)</em></p><p><code>print(", texts[2]);
        // code in colour (ILIAS has no style sheet of ours)
        Assert.Contains("<span style=\"color: #4078f2\">print</span>", items[1].Descendants("mattext").First().Value);
    }
}
