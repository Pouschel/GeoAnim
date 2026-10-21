using System.Diagnostics;
using System.Text;
using GeoAnim.Script;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

/// <summary>
/// geoanim serve &lt;file.geo&gt; [--port N] [--no-browser]: live preview in the browser, updated on every save.<br/>
/// geoanim export &lt;file.geo&gt; [-o file.html]: one self-contained HTML file.<br/>
/// geoanim exportdir &lt;dir&gt;: an HTML file next to every .geo file in the folder and its subfolders.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage:
          geoanim serve <file.geo> [--port 8080] [--no-browser]
              Shows the animation in the browser and updates it whenever the file is saved.
          geoanim export <file.geo> [-o <file.html>]
              Writes the animation as one HTML file that plays without a server (default: next to the script).
          geoanim exportdir <dir>
              Writes the animation HTML next to every .geo file in directory dir and all subdirectories.
        """;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length < 2 || args[0] is not ("serve" or "export" or "exportdir"))
        {
            Console.WriteLine(Usage);
            return args.Length == 0 || args[0] is "help" or "--help" or "-h" ? 0 : 1;
        }
        if (args[0] == "exportdir")
        {
            if (args.Length > 2)
            {
                Console.Error.WriteLine("Unknown option " + args[2]);
                Console.WriteLine(Usage);
                return 1;
            }
            return ExportDirectory(args[1]);
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
        return ExportFile(file, output, Path.GetFileName(file)) ? 0 : 1;
    }

    /// <summary>Exports every .geo file below <paramref name="folder"/>; a faulty script does not stop the others.</summary>
    internal static int ExportDirectory(string folder)
    {
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine("Directory not found: " + folder);
            return 1;
        }
        string root = Path.GetFullPath(folder);
        List<string> files = Directory.EnumerateFiles(root, "*.geo", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        int failed = files.Count(file => !ExportFile(file, Path.ChangeExtension(file, ".html"), Path.GetRelativePath(root, file)));
        Console.WriteLine($"{files.Count - failed} of {files.Count} files exported" + (failed > 0 ? $", {failed} with errors." : "."));
        return failed > 0 ? 1 : 0;
    }

    private static bool ExportFile(string file, string output, string displayName)
    {
        try
        {
            AnimationData animation = ScriptCompiler.Compile(File.ReadAllText(file));
            File.WriteAllText(output, HtmlExporter.Export(animation), new UTF8Encoding(false));
            Console.WriteLine($"Written: {output} ({animation.Steps.Count} steps, {animation.Duration / 1000:0.0} s)");
            return true;
        }
        catch (ScriptError e)
        {
            Console.Error.WriteLine(displayName + ": " + e);
            return false;
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
