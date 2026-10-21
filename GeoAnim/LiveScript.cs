using System.Text;
using System.Text.Json;
using GeoAnim.Script;

namespace GeoAnim.Cli;

/// <summary>
/// A script file and its latest compilation, compiled again whenever the file or a file it includes changes. After a
/// mistake the last good animation stays, together with the error message.
/// </summary>
internal sealed class LiveScript : IDisposable
{
    private readonly object gate = new();
    // one watcher per file read (the script and its includes)
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer debounce;
    private bool disposed;
    private string? animationJson;
    private string? error;
    private int version;

    public LiveScript(string path)
    {
        FilePath = Path.GetFullPath(path);
        debounce = new Timer(_ => Compile(), null, Timeout.Infinite, Timeout.Infinite);
        Compile();
    }

    /// <summary>The files watched: the script and the files it included at the last compilation.</summary>
    public IReadOnlyCollection<string> WatchedFiles
    {
        get
        {
            lock (gate)
            {
                return watchers.Keys.ToList();
            }
        }
    }

    private void Watch(IEnumerable<string> files)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            var wanted = new HashSet<string>(files.Prepend(FilePath), StringComparer.OrdinalIgnoreCase);
            foreach (string file in watchers.Keys.Where(f => !wanted.Contains(f)).ToList())
            {
                watchers[file].Dispose();
                watchers.Remove(file);
            }
            foreach (string file in wanted.Where(f => !watchers.ContainsKey(f) && Directory.Exists(Path.GetDirectoryName(f))))
            {
                var watcher = new FileSystemWatcher(Path.GetDirectoryName(file)!, Path.GetFileName(file))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                };
                watcher.Changed += (_, _) => Schedule();
                watcher.Created += (_, _) => Schedule();
                watcher.Renamed += (_, _) => Schedule();
                watcher.EnableRaisingEvents = true;
                watchers[file] = watcher;
            }
        }
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
        IEnumerable<string>? files = null;
        try
        {
            ScriptSource source = ReadWithRetry();
            files = source.Files;
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
        // after a failed include, keep watching the files of before (so that fixing them is noticed)
        Watch(files ?? WatchedFiles);
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

    private ScriptSource ReadWithRetry()
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return ScriptSource.FromFile(FilePath);
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
        lock (gate)
        {
            disposed = true;
            foreach (FileSystemWatcher watcher in watchers.Values)
            {
                watcher.Dispose();
            }
            watchers.Clear();
        }
        debounce.Dispose();
    }
}
