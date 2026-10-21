using System.Text;
using System.Text.Json;
using GeoAnim.Script;

namespace GeoAnim.Cli;

/// <summary>
/// A script file and its latest compilation, compiled again whenever the file changes. After a mistake the last good
/// animation stays, together with the error message.
/// </summary>
internal sealed class LiveScript : IDisposable
{
    private readonly object gate = new();
    private readonly FileSystemWatcher watcher;
    private readonly Timer debounce;
    private string? animationJson;
    private string? error;
    private int version;

    public LiveScript(string path)
    {
        FilePath = Path.GetFullPath(path);
        Compile();
        debounce = new Timer(_ => Compile(), null, Timeout.Infinite, Timeout.Infinite);
        watcher = new FileSystemWatcher(Path.GetDirectoryName(FilePath)!, Path.GetFileName(FilePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        watcher.Changed += (_, _) => Schedule();
        watcher.Created += (_, _) => Schedule();
        watcher.Renamed += (_, _) => Schedule();
        watcher.EnableRaisingEvents = true;
    }

    public string FilePath { get; }

    /// <summary>Counts the compilations that changed something.</summary>
    public int Version
    {
        get
        {
            lock (gate)
            {
                return version;
            }
        }
    }

    /// <summary>Raised after every compilation that changed the animation or the error.</summary>
    public event Action<string?>? Compiled;

    // editors save in several writes: wait until the file is quiet
    private void Schedule() => debounce.Change(150, Timeout.Infinite);

    /// <summary>Compiles the file now.</summary>
    public void Compile()
    {
        string? newJson = null;
        string? newError = null;
        try
        {
            string source = ReadWithRetry();
            newJson = ScriptCompiler.Compile(source).ToJson();
        }
        catch (ScriptError e)
        {
            newError = e.ToString();
        }
        catch (IOException e)
        {
            newError = "Cannot read " + Path.GetFileName(FilePath) + ": " + e.Message;
        }
        catch (UnauthorizedAccessException e)
        {
            newError = "Cannot read " + Path.GetFileName(FilePath) + ": " + e.Message;
        }
        bool changed;
        lock (gate)
        {
            string? json = newJson ?? animationJson;
            changed = json != animationJson || newError != error || version == 0;
            animationJson = json;
            error = newError;
            if (changed)
            {
                version++;
            }
        }
        if (changed)
        {
            Compiled?.Invoke(newError);
        }
    }

    private string ReadWithRetry()
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(FilePath);
            }
            catch (IOException) when (attempt < 10 && File.Exists(FilePath))
            {
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>What the player fetches: {"version": …, "error": "…" or null, "animation": {…} or null}.</summary>
    public string ToJson()
    {
        lock (gate)
        {
            var json = new StringBuilder();
            json.Append("{\"version\":").Append(version);
            json.Append(",\"error\":").Append(error == null ? "null" : JsonSerializer.Serialize(error));
            json.Append(",\"animation\":").Append(animationJson ?? "null");
            json.Append('}');
            return json.ToString();
        }
    }

    public void Dispose()
    {
        watcher.Dispose();
        debounce.Dispose();
    }
}
