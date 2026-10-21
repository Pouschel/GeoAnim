using System.Diagnostics;
using System.Text;
using GeoAnim.Script;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

/// <summary>
/// geoanim serve &lt;file.geo&gt; [--port N] [--no-browser]: live preview in the browser, updated on every save.<br/>
/// geoanim export &lt;file.geo&gt; [-o file.html]: one self-contained HTML file.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage:
          geoanim serve <file.geo> [--port 8080] [--no-browser]
              Shows the animation in the browser and updates it whenever the file is saved.
          geoanim export <file.geo> [-o <file.html>]
              Writes the animation as one HTML file that plays without a server (default: next to the script).
        """;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 2 || args[0] is not ("serve" or "export"))
        {
            Console.WriteLine(Usage);
            return args.Length == 0 || args[0] is "help" or "--help" or "-h" ? 0 : 1;
        }
        string file = args[1];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine("File not found: " + file);
            return 1;
        }
        try
        {
            return args[0] == "serve" ? Serve(file, args.Skip(2).ToList()) : Export(file, args.Skip(2).ToList());
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.WriteLine(Usage);
            return 1;
        }
    }

    private static int Export(string file, List<string> options)
    {
        string output = Path.ChangeExtension(Path.GetFullPath(file), ".html");
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is "-o" or "--output" && i + 1 < options.Count)
            {
                output = Path.GetFullPath(options[++i]);
            }
            else
            {
                throw new ArgumentException("Unknown option " + options[i]);
            }
        }
        try
        {
            AnimationData animation = ScriptCompiler.Compile(File.ReadAllText(file));
            File.WriteAllText(output, HtmlExporter.Export(animation), new UTF8Encoding(false));
            Console.WriteLine($"Written: {output} ({animation.Steps.Count} steps, {animation.Duration / 1000:0.0} s)");
            return 0;
        }
        catch (ScriptError e)
        {
            Console.Error.WriteLine(Path.GetFileName(file) + ": " + e);
            return 1;
        }
    }

    private static int Serve(string file, List<string> options)
    {
        int port = 8080;
        bool openBrowser = true;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] == "--port" && i + 1 < options.Count && int.TryParse(options[i + 1], out int p))
            {
                port = p;
                i++;
            }
            else if (options[i] == "--no-browser")
            {
                openBrowser = false;
            }
            else
            {
                throw new ArgumentException("Unknown option " + options[i]);
            }
        }

        using var script = new LiveScript(file);
        script.Compiled += error => Report(script, error);
        Report(script, ErrorOf(script));
        using var server = new LiveServer(script, LiveServer.FindFreePort(port));
        Console.WriteLine($"GeoAnim: {server.Url}  (watching {script.FilePath}; Ctrl+C ends)");
        if (openBrowser)
        {
            OpenBrowser(server.Url);
        }
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            server.Dispose();
        };
        server.Wait();
        return 0;
    }

    private static string? ErrorOf(LiveScript script) =>
        System.Text.Json.JsonDocument.Parse(script.ToJson()).RootElement.GetProperty("error").GetString();

    private static void Report(LiveScript script, string? error)
    {
        string now = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        if (error == null)
        {
            Console.WriteLine($"{now}  {Path.GetFileName(script.FilePath)}: OK");
        }
        else
        {
            Console.WriteLine($"{now}  {Path.GetFileName(script.FilePath)}: {error}");
        }
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Console.WriteLine("Open " + url + " in the browser.");
        }
    }
}
