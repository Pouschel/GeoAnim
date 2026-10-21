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
