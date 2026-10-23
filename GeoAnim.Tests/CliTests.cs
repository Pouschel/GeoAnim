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
            Assert.Contains("<h2>sub / deeper</h2>", index);
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
        string strecken = File.ReadAllText(Path.Combine(folder, "Grundlagen", "Strecken-und-Längen.html"));
        string kreis = File.ReadAllText(Path.Combine(folder, "Mehr", "Kreis.html"));
        // by default every page has the player inside
        Assert.Contains("function valueAt", kreis);
        Assert.False(Directory.Exists(Path.Combine(folder, "assets")));

        // links with the scripts' names lead to the pages
        Assert.Contains("[Punkte](Punkte.html#1)", strecken);
        Assert.Contains("\\u003Chttps://mathepedia.de\\u003E", strecken);
        Assert.Contains("[Strecken](../Grundlagen/Strecken-und-L%C3%A4ngen.html)", kreis);
        // the book: overview, previous and next page across folders
        string index = File.ReadAllText(Path.Combine(folder, "index.html"));
        Assert.True(index.IndexOf("Grundlagen/Punkte.html") < index.IndexOf("Strecken-und-") && index.IndexOf("Strecken-und-") < index.IndexOf("Mehr/Kreis.html"), index);
        Assert.Contains("<h2>Grundlagen</h2>", index);
        Assert.Contains("window.GEO_BOOK = {\"index\":\"../index.html\",\"previous\":{\"title\":\"Strecken\",\"href\":\"../Grundlagen/Strecken-und-L%C3%A4ngen.html\"},\"next\":null}", kreis);
        Assert.Contains("\"previous\":null,\"next\":{\"title\":\"Strecken\",\"href\":\"Strecken-und-L%C3%A4ngen.html\"}",
            File.ReadAllText(Path.Combine(folder, "Grundlagen", "Punkte.html")));

        // names that are better avoided in web addresses give a warning
        Assert.Equal("ä", Program.UnsafeCharacters("Grundlagen/Strecken-und-Längen.html"));
        Assert.Equal("ß (space) ö", Program.UnsafeCharacters("Mehr/Maße und Größen.html"));
        Assert.Equal("", Program.UnsafeCharacters("Grundlagen/Strahl-und-Gerade.html"));

        // with assets: the player's files once in assets/, the pages link to them (with a fingerprint against old copies in caches)
        Assert.Equal(0, Program.ExportDirectory(folder, assets: true));
        kreis = File.ReadAllText(Path.Combine(folder, "Mehr", "Kreis.html"));
        Assert.True(File.Exists(Path.Combine(folder, "assets", "player.js")));
        Assert.True(File.Exists(Path.Combine(folder, "assets", "fonts", "stix-two-text-latin-400-normal.woff2")));
        Assert.Matches("<script src=\"\\.\\./assets/player\\.js\\?v=[0-9a-f]{10}\"></script>", kreis);
        Assert.Contains("<link rel=\"stylesheet\" href=\"../assets/fonts/fonts.css?v=", kreis);
        Assert.DoesNotContain("function valueAt", kreis);
        Assert.Contains("window.GEO_BOOK", kreis);

        // two scripts with the same page
        Write(Path.Combine("20_Mehr", "30_Kreis.geo"), "point A = 1 1");
        Assert.Equal(1, Program.ExportDirectory(folder));
    }

    [Fact]
    public void ExportDirectoryLinksKeywordsToTheirPages()
    {
        Directory.CreateDirectory(Path.Combine(folder, "Basics"));
        Directory.CreateDirectory(Path.Combine(folder, "Mehr"));
        Write(Path.Combine("Basics", "10_Punkte.geo"), "keyword Punkt/e\npoint A = 1 1\nstep Ein Punkt");
        Write(Path.Combine("Mehr", "Strecken.geo"), "keyword Strecke/n\npoint A = 1 1\nstep Zwei Punkte und eine Strecke\nstep b\nkeyword Endpunkt/e");
        Write("Dreieck.geo", "point A = 1 1\nstep Drei Strecken, ihre Endpunkte");
        Assert.Equal(0, Program.ExportDirectory(folder));
        string strecken = File.ReadAllText(Path.Combine(folder, "Mehr", "Strecken.html"));
        Assert.Contains("[Punkte](../Basics/Punkte.html)", strecken);
        Assert.DoesNotContain("[Strecke]", strecken);
        string dreieck = File.ReadAllText(Path.Combine(folder, "Dreieck.html"));
        Assert.Contains("[Strecken](Mehr/Strecken.html), ihre [Endpunkte](Mehr/Strecken.html#2)", dreieck);
        Assert.DoesNotContain("](", File.ReadAllText(Path.Combine(folder, "Basics", "Punkte.html")).Split("GEO_ANIMATION")[1].Split("</script>")[0]);

        // a keyword of two pages
        Write("Doppelt.geo", "keyword Punkt\npoint A = 1 1");
        Assert.Equal(1, Program.ExportDirectory(folder));
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
