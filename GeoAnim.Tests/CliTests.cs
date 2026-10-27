using System.Net;
using System.Text.Json;
using GeoAnim.Cli;

namespace GeoAnim.Tests;

public sealed class CliTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("geoanim-test").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string Write(string name, string content)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static JsonElement State(LiveScript script) => JsonDocument.Parse(script.ToJson()).RootElement;

    [Fact]
    public void LiveScriptKeepsTheLastGoodAnimationAfterAMistake()
    {
        string path = Write("a.geo", "title one\npoint A = 1 1");
        using var script = new LiveScript(path);
        Assert.Equal(1, script.Version);
        Assert.Equal("one", State(script).GetProperty("animation").GetProperty("title").GetString());

        File.WriteAllText(path, "title one\npoint A = 1 1 colour red");
        script.Compile();
        JsonElement state = State(script);
        Assert.Equal(2, script.Version);
        Assert.Equal("Line 2, column 15: Unknown option or var \"colour\"", state.GetProperty("error").GetString()!.Split(" (")[0]);
        Assert.Equal("one", state.GetProperty("animation").GetProperty("title").GetString());

        // no change, no new version
        script.Compile();
        Assert.Equal(2, script.Version);

        File.WriteAllText(path, "title two");
        script.Compile();
        Assert.Equal(JsonValueKind.Null, State(script).GetProperty("error").ValueKind);
        Assert.Equal("two", State(script).GetProperty("animation").GetProperty("title").GetString());
    }

    [Fact]
    public void ExportDirectoryWritesAllScriptsAndReportsFaultyOnes()
    {
        Directory.CreateDirectory(Path.Combine(folder, "sub", "deeper"));
        Write("one.geo", "title one\npoint A = 1 1");
        Write(Path.Combine("sub", "deeper", "two.geo"), "title two");
        Assert.Equal(0, Program.ExportDirectory(folder));
        Assert.True(File.Exists(Path.Combine(folder, "one.html")));
        Assert.Contains("window.GEO_ANIMATION", File.ReadAllText(Path.Combine(folder, "sub", "deeper", "two.html")));

        Write(Path.Combine("sub", "bad.geo"), "pont A = 1 1");
        Write("three.geo", "title three");
        Assert.Equal(1, Program.ExportDirectory(folder));
        Assert.True(File.Exists(Path.Combine(folder, "three.html")));
        Assert.False(File.Exists(Path.Combine(folder, "sub", "bad.html")));
        Assert.Equal(1, Program.ExportDirectory(Path.Combine(folder, "missing")));

        // with an output folder: the same structure there, the sources stay clean
        File.Delete(Path.Combine(folder, "sub", "bad.geo"));
        string output = Path.Combine(folder, "..", Path.GetFileName(folder) + "-html");
        try
        {
            Assert.Equal(0, Program.ExportDirectory(folder, output));
            Assert.True(File.Exists(Path.Combine(output, "one.html")));
            Assert.True(File.Exists(Path.Combine(output, "sub", "deeper", "two.html")));
            Assert.False(File.Exists(Path.Combine(output, "one.geo")));
            Assert.True(File.Exists(Path.Combine(output, "index.html")));

            // with an index: links to the pages with something drawn, in natural order, by folder
            Write("Prop10.geo", "title Satz **10**\npoint A = 1 1\nstep Zehn");
            Write("Prop2.geo", "title Satz 2\npoint A = 1 1\nstep Zwei mit $bar(AB)$");
            Write(Path.Combine("sub", "deeper", "three.geo"), "point A = 1 1");
            Write("styles.geo", "default point size 3");
            Assert.Equal(0, Program.ExportDirectory(folder, output));
            string index = File.ReadAllText(Path.Combine(output, "index.html"));
            Assert.Contains("<a href=\"one.html\">one</a>", index);
            Assert.Contains("<a href=\"Prop2.html\">Satz 2</a><p>Zwei mit AB.</p>", index);
            Assert.True(index.IndexOf("Prop2.html") < index.IndexOf("Prop10.html") && index.IndexOf("Prop10.html") < index.IndexOf("<h2>"), index);
            Assert.Contains("<summary><h2>sub</h2>", index);
            Assert.Contains("<summary><h3>deeper</h3>", index);
            Assert.Contains("<a href=\"sub/deeper/three.html\">three</a>", index);
            Assert.DoesNotContain("styles.html", index);
            Assert.Contains("Satz 10</a>", index);
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    [Fact]
    public void ExportDirectoryMakesABook()
    {
        Directory.CreateDirectory(Path.Combine(folder, "10_Grundlagen"));
        Directory.CreateDirectory(Path.Combine(folder, "20_Mehr"));
        Directory.CreateDirectory(Path.Combine(folder, "_Stile"));
        Write("_def.geo", "default point size 3");
        Write(Path.Combine("_Stile", "farben.geo"), "point A = 1 1");
        Write(Path.Combine("10_Grundlagen", "10_Punkte.geo"), "include ../_def.geo\ntitle Punkte\npoint A = 1 1\nstep Ein Punkt");
        Write(Path.Combine("10_Grundlagen", "20_Strecken-und-Längen.geo"), "title Strecken\npoint A = 1 1\nstep Siehe [Punkte](10_Punkte.html#1) und <https://mathepedia.de>");
        Write(Path.Combine("20_Mehr", "10_Kreis.geo"), "title Kreis\npoint A = 1 1\nstep Wie bei [Strecken](../10_Grundlagen/20_Strecken-und-Längen.html)");
        Assert.Equal(0, Program.ExportDirectory(folder));
        // no pages for helpers, no numbers in the names
        Assert.False(File.Exists(Path.Combine(folder, "_def.html")));
        Assert.False(Directory.Exists(Path.Combine(folder, "Stile")) || File.Exists(Path.Combine(folder, "_Stile", "farben.html")));
        Assert.True(File.Exists(Path.Combine(folder, "Grundlagen", "Punkte.html")));
        string strecken = File.ReadAllText(Path.Combine(folder, "Grundlagen", "Strecken-und-Laengen.html"));
        string kreis = File.ReadAllText(Path.Combine(folder, "Mehr", "Kreis.html"));
        // by default every page has the player inside
        Assert.Contains("function valueAt", kreis);
        Assert.False(Directory.Exists(Path.Combine(folder, "assets")));

        // links with the scripts' names lead to the pages
        Assert.Contains("[Punkte](Punkte.html#1)", strecken);
        Assert.Contains("\\u003Chttps://mathepedia.de\\u003E", strecken);
        Assert.Contains("[Strecken](../Grundlagen/Strecken-und-Laengen.html)", kreis);
        // the book: overview, previous and next page across folders
        string index = File.ReadAllText(Path.Combine(folder, "index.html"));
        Assert.True(index.IndexOf("Grundlagen/Punkte.html") < index.IndexOf("Strecken-und-") && index.IndexOf("Strecken-und-") < index.IndexOf("Mehr/Kreis.html"), index);
        // chapters open with a click
        Assert.Contains("<details data-chapter=\"Grundlagen\">\n      <summary><h2>Grundlagen</h2><span class=\"count\">2 Seiten</span></summary>", index);
        Assert.Contains("<summary><h2>Mehr</h2><span class=\"count\">1 Seite</span></summary>", index);
        Assert.Contains("window.GEO_BOOK = {\"index\":\"../index.html\",\"previous\":{\"title\":\"Strecken\",\"href\":\"../Grundlagen/Strecken-und-Laengen.html\"},\"next\":null", kreis);
        Assert.Contains("\"previous\":null,\"next\":{\"title\":\"Strecken\",\"href\":\"Strecken-und-Laengen.html\"}",
            File.ReadAllText(Path.Combine(folder, "Grundlagen", "Punkte.html")));

        // names as people write them, addresses as web servers like them
        Assert.Equal("Grundlagen/Winkelsaetze/Masse-und-Groessen.html", Program.PageName("10_Grundlagen/40_Winkelsätze/20_Maße und Größen.geo"));
        Assert.Equal("Cafe-Ueber.html", Program.PageName("Café-Über.geo"));
        Assert.Equal("Winkelsätze", Program.DisplayName("40_Winkelsätze"));
        Assert.Equal("Strahl und Gerade", Program.DisplayName("30_Strahl-und-Gerade"));
        Assert.Equal("", Program.DroppedCharacters("10_Grundlagen/20_Maße und Größen.geo"));
        Assert.Equal("? €", Program.DroppedCharacters("Was?/Preis€.geo"));

        // with assets: the player's files once in assets/, the pages link to them (with a fingerprint against old copies in caches)
        Assert.Equal(0, Program.ExportDirectory(folder, assets: true));
        kreis = File.ReadAllText(Path.Combine(folder, "Mehr", "Kreis.html"));
        Assert.True(File.Exists(Path.Combine(folder, "assets", "player.js")));
        Assert.True(File.Exists(Path.Combine(folder, "assets", "fonts", "stix-two-text-latin-400-normal.woff2")));
        Assert.Matches("<script src=\"\\.\\./assets/player\\.js\\?v=[0-9a-f]{10}\"></script>", kreis);
        Assert.Contains("<link rel=\"stylesheet\" href=\"../assets/fonts/fonts.css?v=", kreis);
        Assert.DoesNotContain("function valueAt", kreis);
        Assert.Contains("window.GEO_BOOK", kreis);

        // index.yaml: title, description, language, icon, address on the web (canonical, preview, sitemap), home, footer
        Directory.CreateDirectory(Path.Combine(folder, "icons"));
        File.WriteAllText(Path.Combine(folder, "icons", "winkel.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        Write("index.yaml", """
            title: Ebene Geometrie
            description: Von Punkten bis zu Winkeln.
            icon: icons/winkel.svg   # relativ
            language: de
            url: https://mathepedia.de/geometrie
            home:
              title: mathepedia.de
              url: https://mathepedia.de/
            footer: © T. S. · [Impressum](https://mathepedia.de/impressum)
            colour: blau
            """);
        Assert.Equal(0, Program.ExportDirectory(folder));
        index = File.ReadAllText(Path.Combine(folder, "index.html"));
        Assert.Contains("<html lang=\"de\">", index);
        Assert.Contains("<title>Ebene Geometrie</title>", index);
        Assert.Contains("<p class=\"intro\">Von Punkten bis zu Winkeln.</p>", index);
        Assert.Contains("<link rel=\"canonical\" href=\"https://mathepedia.de/geometrie/\">", index);
        Assert.Contains("<a class=\"home\" href=\"https://mathepedia.de/\">← mathepedia.de</a>", index);
        Assert.Contains("<footer>© T. S. · <a href=\"https://mathepedia.de/impressum\">Impressum</a></footer>", index);
        Assert.Contains("<link rel=\"icon\" href=\"data:image/svg+xml;base64,", index);
        kreis = File.ReadAllText(Path.Combine(folder, "Mehr", "Kreis.html"));
        Assert.Contains("<html lang=\"de\">", kreis);
        Assert.Contains("<link rel=\"canonical\" href=\"https://mathepedia.de/geometrie/Mehr/Kreis.html\">", kreis);
        Assert.Contains("<meta property=\"og:title\" content=\"Kreis\">", kreis);
        Assert.Contains("<meta property=\"og:site_name\" content=\"Ebene Geometrie\">", kreis);
        Assert.Contains("\"home\":{\"title\":\"mathepedia.de\",\"href\":\"https://mathepedia.de/\"},\"footer\":\"\\u00A9 T. S. \\u00B7 [Impressum](https://mathepedia.de/impressum)\"", kreis);
        string sitemap = File.ReadAllText(Path.Combine(folder, "sitemap.xml"));
        Assert.Contains("<loc>https://mathepedia.de/geometrie/</loc>", sitemap);
        Assert.Contains("<loc>https://mathepedia.de/geometrie/Grundlagen/Strecken-und-Laengen.html</loc>", sitemap);
        // with assets the icon goes there
        Assert.Equal(0, Program.ExportDirectory(folder, assets: true));
        Assert.True(File.Exists(Path.Combine(folder, "assets", "winkel.svg")));
        Assert.Matches("<link rel=\"icon\" href=\"\\.\\./assets/winkel\\.svg\\?v=[0-9a-f]{10}\">", File.ReadAllText(Path.Combine(folder, "Mehr", "Kreis.html")));
        File.Delete(Path.Combine(folder, "index.yaml"));

        // two scripts with the same page
        Write(Path.Combine("20_Mehr", "30_Kreis.geo"), "point A = 1 1");
        Assert.Equal(1, Program.ExportDirectory(folder));
    }

    [Fact]
    public void ExportDirectoryNestsChaptersInTheirOrder()
    {
        Directory.CreateDirectory(Path.Combine(folder, "20_Winkel", "40_Winkelsätze"));
        Write(Path.Combine("20_Winkel", "30_Rechte-Winkel.geo"), "title Rechte Winkel\npoint A = 1 1");
        Write(Path.Combine("20_Winkel", "40_Winkelsätze", "10_Scheitelwinkel.geo"), "title Scheitelwinkel\npoint A = 1 1");
        Write(Path.Combine("20_Winkel", "50_Winkelarten.geo"), "title Winkelarten\npoint A = 1 1");
        Write(Path.Combine("20_Winkel", "40_Winkelsätze", "20_Nebenwinkel.geo"), "title Nebenwinkel\npoint A = 1 1");
        Assert.Equal(0, Program.ExportDirectory(folder));
        // the subfolder where its number puts it, with its heading as written, a chapter in its chapter
        string index = File.ReadAllText(Path.Combine(folder, "index.html"));
        int Where(string text) => index.IndexOf(text, StringComparison.Ordinal);
        Assert.True(Where("Rechte Winkel") < Where("<summary><h3>Winkelsätze</h3><span class=\"count\">2 Seiten</span></summary>")
            && Where("Winkelsätze</h3>") < Where("Scheitelwinkel") && Where("Nebenwinkel") < Where("Winkelarten"), index);
        Assert.Contains("<summary><h2>Winkel</h2><span class=\"count\">4 Seiten</span></summary>", index);
        Assert.Contains("href=\"Winkel/Winkelsaetze/Scheitelwinkel.html\"", index);
        // turning the pages goes the same way
        string nebenwinkel = File.ReadAllText(Path.Combine(folder, "Winkel", "Winkelsaetze", "Nebenwinkel.html"));
        Assert.Contains("\"previous\":{\"title\":\"Scheitelwinkel\",\"href\":\"Scheitelwinkel.html\"},\"next\":{\"title\":\"Winkelarten\",\"href\":\"../Winkelarten.html\"}", nebenwinkel);
    }

    [Fact]
    public void ExportDirectoryLinksKeywordsToTheirPages()
    {
        Directory.CreateDirectory(Path.Combine(folder, "Basics"));
        Directory.CreateDirectory(Path.Combine(folder, "Mehr"));
        Write(Path.Combine("Basics", "10_Punkte.geo"), "keyword Punkt/e\npoint A = 1 1\nstep Ein Punkt");
        Write(Path.Combine("Mehr", "Strecken.geo"), "keyword Strecke/n\npoint A = 1 1\nstep Zwei Punkte\ninfo Zwei Punkte und eine Strecke\nstep b\nkeyword Endpunkt/e");
        Write("Dreieck.geo", "description Ein Dreieck aus Strecken\npoint A = 1 1\nstep Drei Strecken\ninfo Drei Strecken, ihre Endpunkte");
        Assert.Equal(0, Program.ExportDirectory(folder));
        string strecken = File.ReadAllText(Path.Combine(folder, "Mehr", "Strecken.html"));
        Assert.Contains("[Punkte](../Basics/Punkte.html)", strecken);
        Assert.DoesNotContain("[Strecke]", strecken);
        string dreieck = File.ReadAllText(Path.Combine(folder, "Dreieck.html"));
        Assert.Contains("[Strecken](Mehr/Strecken.html), ihre [Endpunkte](Mehr/Strecken.html#2)", dreieck);
        Assert.Contains("\"caption\":\"Drei Strecken\"", dreieck);
        // the description below the starting picture too; as plain text for search engines and the overview
        Assert.Contains("\"description\":\"Ein Dreieck aus [Strecken](Mehr/Strecken.html)\"", dreieck);
        Assert.Contains("<meta name=\"description\" content=\"Ein Dreieck aus Strecken\">", dreieck);
        Assert.Contains("<p>Ein Dreieck aus Strecken</p>", File.ReadAllText(Path.Combine(folder, "index.html")));
        Assert.DoesNotContain("](", File.ReadAllText(Path.Combine(folder, "Basics", "Punkte.html")).Split("GEO_ANIMATION")[1].Split("</script>")[0]);

        // the keyword index: each term (not each form) with its page and the pages that mention it, linked from the overview
        string register = File.ReadAllText(Path.Combine(folder, "stichwoerter.html"));
        Assert.Contains("<a class=\"term\" href=\"Mehr/Strecken.html#2\">Endpunkt</a>", register);
        Assert.Contains("<p class=\"used\">erwähnt in: <a href=\"Dreieck.html\">Dreieck</a></p>", register);
        Assert.Contains("<a class=\"term\" href=\"Basics/Punkte.html\">Punkt</a>", register);
        Assert.DoesNotContain(">Endpunkte<", register);
        Assert.True(register.IndexOf("id=\"E\"", StringComparison.Ordinal) < register.IndexOf("id=\"P\"", StringComparison.Ordinal));
        Assert.Contains("href=\"stichwoerter.html\">Stichwortverzeichnis</a>", File.ReadAllText(Path.Combine(folder, "index.html")));

        // the contact sheet: only with -sheet, a still of every step (start included), linked to the page at that step
        Assert.False(File.Exists(Path.Combine(folder, "kontaktbogen.html")));
        Assert.Equal(0, Program.ExportDirectory(folder, sheet: true));
        string sheet = File.ReadAllText(Path.Combine(folder, "kontaktbogen.html"));
        Assert.Contains("href=\"Mehr/Strecken.html#2\"><iframe loading=\"lazy\" tabindex=\"-1\" title=\"b\" src=\"Mehr/Strecken.html?still#2\">", sheet);
        Assert.Contains("src=\"Basics/Punkte.html?still#0\"", sheet);
        Assert.DoesNotContain("kontaktbogen", File.ReadAllText(Path.Combine(folder, "index.html")));

        // a keyword of two pages
        Write("Doppelt.geo", "keyword Punkt\npoint A = 1 1");
        Assert.Equal(1, Program.ExportDirectory(folder));
    }

    [Fact]
    public void DaemonLinksKeywordsOfTheBook()
    {
        Directory.CreateDirectory(Path.Combine(folder, "Basics"));
        Write("index.yaml", "title: Buch");
        Write(Path.Combine("Basics", "10_Punkte.geo"), "keyword Punkt/e\npoint A = 1 1\nstep Ein Punkt");
        Write(Path.Combine("Basics", "20_Strecken.geo"), "point A = 1 1\nstep a\nstep b\nkeyword Endpunkt/e");
        Write("_Hilfe.geo", "keyword Hilfe");
        string main = Path.Combine(folder, "30_Dreieck.geo");
        JsonElement Ask(string text) => JsonDocument.Parse(Daemon.Handle(JsonSerializer.Serialize(new { id = 1, type = "compile", file = main, text })))
            .RootElement;

        // the unsaved text counts; links go to the scripts (a click opens them), to a step with #; no link to "_" files
        JsonElement answer = Ask("keyword Dreieck\npoint A = 1 1\nstep Punkte\ninfo Punkte, Endpunkte, Hilfe und ein Dreieck");
        Assert.Equal("[Punkte](Basics/10_Punkte.geo), [Endpunkte](Basics/20_Strecken.geo#2), Hilfe und ein Dreieck",
            answer.GetProperty("animation").GetProperty("steps")[0].GetProperty("info").GetString());
        Assert.Equal(0, answer.GetProperty("warnings").GetArrayLength());

        // a keyword of another script: a warning at the keyword line, with the other place
        JsonElement warning = Ask("point A = 1 1\nkeyword Dreieck, Punkt\nstep a").GetProperty("warnings")[0];
        Assert.Equal((main, 2), (warning.GetProperty("file").GetString(), warning.GetProperty("line").GetInt32()));
        Assert.Contains("Basics/10_Punkte.geo:1", warning.GetProperty("message").GetString());

        // outside a book: no links
        File.Delete(Path.Combine(folder, "index.yaml"));
        Assert.Equal("Punkte", Ask("point A = 1 1\nstep a\ninfo Punkte").GetProperty("animation").GetProperty("steps")[0].GetProperty("info").GetString());
    }

    [Fact]
    public async Task LiveScriptNoticesChangedIncludes()
    {
        string styles = Write("styles.geo", "title one");
        string path = Write("main.geo", "include styles.geo\npoint A = 1 1");
        using var script = new LiveScript(path);
        Assert.Equal([path, styles], script.WatchedFiles.Order(StringComparer.Ordinal));
        var compiled = new TaskCompletionSource();
        script.Compiled += _ => compiled.TrySetResult();
        await File.WriteAllTextAsync(styles, "title two");
        await compiled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("two", State(script).GetProperty("animation").GetProperty("title").GetString());
    }

    [Fact]
    public void DaemonCompilesUnsavedTextAsIfItWereTheFile()
    {
        string styles = Write("styles.geo", "default point size 6");
        string main = Path.Combine(folder, "main.geo");
        JsonElement Ask(object request) => JsonDocument.Parse(Daemon.Handle(JsonSerializer.Serialize(request))).RootElement;

        // the text need not be saved: main.geo does not even exist; its include is found next to it
        JsonElement answer = Ask(new { id = 7, type = "compile", file = main, text = "include styles.geo\ntitle eins\npoint A = 1 1" });
        Assert.Equal(7, answer.GetProperty("id").GetInt32());
        Assert.Equal("eins", answer.GetProperty("animation").GetProperty("title").GetString());
        Assert.Equal(0, answer.GetProperty("errors").GetArrayLength());
        Assert.Equal([main, styles], answer.GetProperty("files").EnumerateArray().Select(f => f.GetString()));

        // errors with the full path: of the script itself and of an included file
        JsonElement error = Ask(new { id = 8, type = "compile", file = main, text = "point A = 1 1\nsegment s = A C" }).GetProperty("errors")[0];
        Assert.Equal((main, 2, 15), (error.GetProperty("file").GetString(), error.GetProperty("line").GetInt32(), error.GetProperty("column").GetInt32()));
        File.WriteAllText(styles, "default point size 6\npont X = 1 1");
        answer = Ask(new { id = 9, type = "compile", file = main, text = "include styles.geo" });
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("animation").ValueKind);
        Assert.Equal((styles, 2), (answer.GetProperty("errors")[0].GetProperty("file").GetString(), answer.GetProperty("errors")[0].GetProperty("line").GetInt32()));

        // the names defined, for tooltips and "go to definition": also in included files, and up to a mistake
        File.WriteAllText(styles, "default point size 6\nvar dick width 5 color red");
        answer = Ask(new { id = 12, type = "compile", file = main, text = "include styles.geo\nset t = Ein\n| Text\npoint A = 1 1\n  segment s = A A dick\npont" });
        var definitions = answer.GetProperty("definitions").EnumerateArray()
            .Select(d => (d.GetProperty("name").GetString(), d.GetProperty("kind").GetString(), d.GetProperty("text").GetString(),
                d.GetProperty("file").GetString(), d.GetProperty("line").GetInt32(), d.GetProperty("column").GetInt32()))
            .ToList();
        Assert.Equal(
            [("dick", "var", "var dick width 5 color red", styles, 2, 5), ("t", "set", "set t = Ein Text", main, 2, 5),
             ("A", "object", "point A = 1 1", main, 4, 7), ("s", "object", "segment s = A A dick", main, 5, 11)],
            definitions);

        // every place a name stands, for renaming: definitions, objects, a var among the options, a text, a placeholder
        answer = Ask(new { id = 13, type = "compile", file = main, text = "var dick width 5\nset t = Ein Text\npoint A = 1 1\npoint B = 3 1\nsegment s = A B dick\nnumber l = length s\ntext u = t at A\nstep Länge {l} von A" });
        var references = answer.GetProperty("references").EnumerateArray()
            .Select(r => (r.GetProperty("name").GetString(), r.GetProperty("line").GetInt32(), r.GetProperty("column").GetInt32()))
            .ToList();
        Assert.Equal(
            [("dick", 1, 5), ("t", 2, 5), ("A", 3, 7), ("B", 4, 7), ("s", 5, 9), ("A", 5, 13), ("B", 5, 15), ("dick", 5, 17), ("l", 6, 8), ("s", 6, 19),
             ("u", 7, 6), ("t", 7, 10), ("A", 7, 15), ("l", 8, 13)],
            references);
        Assert.Contains("degrees", Ask(new { id = 14, type = "keywords" }).GetProperty("keywords").EnumerateArray().Select(k => k.GetString()));

        // the player page for the preview: everything inside, no animation, waiting for messages
        // for tooltips and ghosts: every object over time, also a hidden one, with its values
        JsonElement inspect = Ask(new { id = 9, type = "compile", file = main, text = "point A = 1 1\npoint B = 4 5 color red\ncircle k = center A through B hidden" })
            .GetProperty("animation").GetProperty("inspect");
        JsonElement circle = inspect.EnumerateArray().Single(o => o.GetProperty("names")[0].GetString() == "k");
        Assert.Equal("arc", circle.GetProperty("type").GetString());
        string info = circle.GetProperty("tracks").GetProperty("info")[0][1].GetString()!;
        Assert.Contains("Mittelpunkt A (1 | 1), Radius 5", info);
        Assert.Contains("nicht gezeichnet (hidden)", info);
        Assert.Contains("Farbe red", inspect[1].GetProperty("tracks").GetProperty("info")[0][1].GetString());
        Assert.DoesNotContain("\"inspect\"", GeoAnim.Script.ScriptCompiler.Compile("point A = 1 1").ToJson());

        // a hidden point: its label where it will stand (with its shift), and the formula for it
        JsonElement hidden = Ask(new { id = 12, type = "compile", file = main, text = "point A = 1 1 hidden shift 0.5 0" }).GetProperty("animation");
        JsonElement label = hidden.GetProperty("inspect")[0].GetProperty("label").GetProperty("tracks");
        Assert.Equal(("$A$", 0.5), (label.GetProperty("text")[0][1].GetString(), label.GetProperty("sx")[0][1].GetDouble()));
        Assert.True(hidden.GetProperty("math").TryGetProperty("A", out _));

        // which object each primitive belongs to (a click in the preview finds its definition); the colours for swatches
        JsonElement drawn = Ask(new { id = 13, type = "compile", file = main, text = "point A = 1 1\npoint B = 2 1\nsegment s = A B" }).GetProperty("animation");
        Assert.Equal(["A", "A", "B", "B", "s"], drawn.GetProperty("owners").EnumerateObject().Select(o => o.Value[0].GetString()));
        Assert.Equal("#d32f2f", Ask(new { id = 14, type = "keywords" }).GetProperty("colors").GetProperty("red").GetString());

        string html = Ask(new { id = 10, type = "player" }).GetProperty("html").GetString()!;
        Assert.Contains("window.GEO_EMBEDDED = true;", html);
        Assert.DoesNotContain("GEO_ANIMATION =", html);
        Assert.Contains("fatal", Daemon.Handle("{\"id\": 11, \"type\": \"dance\"}"));
        Assert.Contains("fatal", Daemon.Handle("not json"));

        // over standard input and output, one line each
        var output = new StringWriter();
        Daemon.Run(new StringReader("{\"id\":1,\"type\":\"compile\",\"file\":\"x.geo\",\"text\":\"title a\"}\n\n{\"id\":2,\"type\":\"player\"}\n"), output);
        Assert.Equal(2, output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task LiveScriptNoticesSavedFiles()
    {
        string path = Write("b.geo", "title one");
        using var script = new LiveScript(path);
        var compiled = new TaskCompletionSource();
        script.Compiled += _ => compiled.TrySetResult();
        await File.WriteAllTextAsync(path, "title two");
        await compiled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("two", State(script).GetProperty("animation").GetProperty("title").GetString());
    }

    [Fact]
    public async Task ServerDeliversPlayerAndAnimation()
    {
        using var script = new LiveScript(Write("c.geo", "title served\npoint A = 1 1"));
        using var server = new LiveServer(script, LiveServer.FindFreePort(18080));
        using var client = new HttpClient { BaseAddress = new Uri(server.Url) };

        HttpResponseMessage page = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<script src=\"player.js\"></script>", await page.Content.ReadAsStringAsync());

        JsonElement state = JsonDocument.Parse(await client.GetStringAsync("/animation.json")).RootElement;
        Assert.Equal(1, state.GetProperty("version").GetInt32());
        Assert.Equal("served", state.GetProperty("animation").GetProperty("title").GetString());

        HttpResponseMessage font = await client.GetAsync("/fonts/stix-two-text-latin-400-normal.woff2");
        Assert.Equal("font/woff2", font.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/missing.js")).StatusCode);
    }
}
