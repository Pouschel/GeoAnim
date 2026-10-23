using System.Text;
using EmbedIO;
using EmbedIO.Actions;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

/// <summary>
/// Serves an exported book (the output folder of "geoanim exportdir … -serve") on http://localhost:port/, always
/// fresh from disk (no caching), so that a reload in the browser shows the latest export.
/// </summary>
internal sealed class BookServer : IDisposable
{
    private readonly WebServer server;
    private readonly string root;

    public BookServer(string root, int port)
    {
        this.root = Path.GetFullPath(root);
        Port = port;
        Swan.Logging.Logger.NoLogging();
        server = new WebServer(o => o
            .WithUrlPrefix(Url)
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

    private async Task HandleAsync(IHttpContext context)
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        string relative = Uri.UnescapeDataString(context.RequestedPath.TrimStart('/'));
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        // a folder: its index.html; nothing outside the book
        if (Directory.Exists(path))
        {
            path = Path.Combine(path, "index.html");
        }
        bool inside = path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
        if (!inside || !File.Exists(path))
        {
            context.Response.StatusCode = 404;
            await context.SendStringAsync("Not found: " + relative, "text/plain", new UTF8Encoding(false));
            return;
        }
        byte[] content = await File.ReadAllBytesAsync(path);
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
