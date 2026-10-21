using System.Net;
using System.Net.Sockets;
using System.Text;
using EmbedIO;
using EmbedIO.Actions;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

/// <summary>
/// Serves the player and the latest compilation of a script on http://localhost:port/. The player fetches
/// /animation.json again and again and shows every change at once.
/// </summary>
internal sealed class LiveServer : IDisposable
{
    private readonly WebServer server;
    private readonly LiveScript script;

    public LiveServer(LiveScript script, int port)
    {
        this.script = script;
        Port = port;
        Swan.Logging.Logger.NoLogging();
        server = new WebServer(o => o
            .WithUrlPrefix("http://localhost:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/")
            .WithMode(HttpListenerMode.EmbedIO));
        server.WithModule(new ActionModule("/", HttpVerbs.Get, HandleAsync));
        server.Start();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (server.State != WebServerState.Listening && watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (server.State == WebServerState.Stopped)
            {
                throw new IOException("Cannot listen on port " + port);
            }
            Thread.Sleep(10);
        }
    }

    public int Port { get; }

    public string Url => "http://localhost:" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/";

    /// <summary>A free port, starting at <paramref name="preferred"/>.</summary>
    public static int FindFreePort(int preferred)
    {
        for (int port = preferred; port < preferred + 50; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException)
            {
            }
        }
        throw new IOException($"No free port between {preferred} and {preferred + 49}.");
    }

    private async Task HandleAsync(IHttpContext context)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        string path = context.RequestedPath.TrimStart('/');
        if (path == "animation.json")
        {
            await context.SendStringAsync(script.ToJson(), "application/json", new UTF8Encoding(false));
            return;
        }
        if (path.Length == 0)
        {
            path = "player.html";
        }
        byte[]? content = ViewerFiles.Read(path);
        if (content == null)
        {
            context.Response.StatusCode = 404;
            await context.SendStringAsync("Not found: " + path, "text/plain", new UTF8Encoding(false));
            return;
        }
        context.Response.ContentType = ViewerFiles.ContentType(path);
        context.Response.ContentLength64 = content.Length;
        await context.Response.OutputStream.WriteAsync(content);
    }

    /// <summary>Waits until the server stops.</summary>
    public void Wait()
    {
        using var stopped = new ManualResetEventSlim();
        server.StateChanged += (_, e) =>
        {
            if (e.NewState == WebServerState.Stopped)
            {
                stopped.Set();
            }
        };
        if (server.State != WebServerState.Stopped)
        {
            stopped.Wait();
        }
    }

    public void Dispose() => server.Dispose();
}
