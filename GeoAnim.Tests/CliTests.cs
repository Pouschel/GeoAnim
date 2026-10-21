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
        Assert.Equal("Line 2, column 15: Unknown option \"colour\"", state.GetProperty("error").GetString()!.Split(" (")[0]);
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

        HttpResponseMessage font = await client.GetAsync("/fonts/lato-latin-400-normal.woff2");
        Assert.Equal("font/woff2", font.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/missing.js")).StatusCode);
    }
}
