using System.Diagnostics;
using System.Text;
using GeoAnim.Script;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

/// <summary>
/// geoanim serve &lt;file.geo&gt; [--port N] [--no-browser]: live preview in the browser, updated on every save.<br/>
/// geoanim export &lt;file.geo&gt; [-o file.html]: one self-contained HTML file.<br/>
/// geoanim exportdir &lt;dir&gt;: an HTML file next to every .geo file in the folder and its subfolders.<br/>
/// geoanim daemon: compiles for an editor over standard input and output (see <see cref="Daemon"/>).
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage:
          geoanim serve <file.geo> [--port 8080] [--no-browser]
              Shows the animation in the browser and updates it whenever the file is saved.
          geoanim export <file.geo> [-o <file.html>]
              Writes the animation as one HTML file that plays without a server (default: next to the script).
          geoanim exportdir <dir> [-o <output directory>] [-index]
              Writes the animation HTML for every .geo file in directory dir and all subdirectories: next to it, or
              with -o in the same folder structure below the output directory. With -index also an index.html with
              links to all pages.
          geoanim daemon
              Compiles for an editor (the VS Code extension): JSON requests on standard input, answers on standard output.
        """;

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 1 && args[0] == "daemon")
        {
            Console.InputEncoding = Encoding.UTF8;
            return Daemon.Run(Console.In, Console.Out);
        }
        if (args.Length < 2 || args[0] is not ("serve" or "export" or "exportdir"))
        {
            Console.WriteLine(Usage);
            return args.Length == 0 || args[0] is "help" or "--help" or "-h" ? 0 : 1;
        }
        if (args[0] == "exportdir")
        {
            string? outputFolder = null;
            bool index = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] is "-o" or "--output" && i + 1 < args.Length)
                {
                    outputFolder = args[++i];
                }
                else if (args[i] is "-index" or "--index")
                {
                    index = true;
                }
                else
                {
                    Console.Error.WriteLine("Unknown option " + args[i]);
                    Console.WriteLine(Usage);
                    return 1;
                }
            }
            return ExportDirectory(args[1], outputFolder, index);
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
        return ExportFile(file, output, Path.GetFileName(file)) != null ? 0 : 1;
    }

    /// <summary>
    /// Exports every .geo file below <paramref name="folder"/>: next to it, or with <paramref name="outputFolder"/> in
    /// the same folder structure below that folder (so relative links between the pages still work); with
    /// <paramref name="index"/> also an index.html there with links to all pages. A faulty script does not stop the others.
    /// </summary>
    internal static int ExportDirectory(string folder, string? outputFolder = null, bool index = false)
    {
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine("Directory not found: " + folder);
            return 1;
        }
        string root = Path.GetFullPath(folder);
        string target = outputFolder == null ? root : Path.GetFullPath(outputFolder);
        List<string> files = Directory.EnumerateFiles(root, "*.geo", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        var pages = new List<(string Path, AnimationData Animation)>();
        int failed = 0;
        // first all scripts: the keywords of every page are needed for the links in the others
        var compiled = new List<(string File, string Relative, string Page, AnimationData Animation)>();
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(root, file);
            AnimationData? animation = CompileFile(file, relative);
            if (animation == null)
            {
                failed++;
            }
            else
            {
                compiled.Add((file, relative, Path.ChangeExtension(relative, ".html").Replace('\\', '/'), animation));
            }
        }
        // keyword → its page; a keyword of two pages is an error (the first one keeps it)
        int conflicts = 0;
        var keywordPages = new Dictionary<string, (string Page, string Relative, int Step)>(StringComparer.Ordinal);
        foreach (var page in compiled)
        {
            foreach (PageKeyword keyword in page.Animation.Keywords)
            {
                if (keywordPages.TryGetValue(keyword.Form, out var other))
                {
                    Console.Error.WriteLine($"{page.Relative}: the keyword {keyword.Form} is already the keyword of {other.Relative}.");
                    conflicts++;
                }
                else
                {
                    keywordPages[keyword.Form] = (page.Page, page.Relative, keyword.Step);
                }
            }
        }
        foreach (var page in compiled)
        {
            // inside a step: to its end ("page.html#3")
            var links = keywordPages.Where(k => k.Value.Page != page.Page)
                .ToDictionary(k => k.Key, k => Keywords.RelativeAddress(page.Page, k.Value.Page) + (k.Value.Step > 0 ? "#" + k.Value.Step : ""),
                    StringComparer.Ordinal);
            AnimationData animation = Keywords.Link(page.Animation, links);
            string output = Path.ChangeExtension(Path.Combine(target, page.Relative), ".html");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            Write(animation, output);
            pages.Add((page.Page, animation));
        }
        if (index)
        {
            Directory.CreateDirectory(target);
            string indexFile = Path.Combine(target, "index.html");
            File.WriteAllText(indexFile, IndexPage.Create(Path.GetFileName(root), pages), new UTF8Encoding(false));
            Console.WriteLine($"Written: {indexFile} ({pages.Count(p => !p.Animation.IsEmpty)} links)");
        }
        Console.WriteLine($"{files.Count - failed} of {files.Count} files exported" + (failed > 0 ? $", {failed} with errors." : ".")
            + (conflicts > 0 ? $" {conflicts} keyword(s) given twice." : ""));
        return failed > 0 || conflicts > 0 ? 1 : 0;
    }

    // the animation written, or null after an error (reported)
    private static AnimationData? ExportFile(string file, string output, string displayName)
    {
        AnimationData? animation = CompileFile(file, displayName);
        if (animation != null)
        {
            Write(animation, output);
        }
        return animation;
    }

    // the animation, or null after an error (reported)
    private static AnimationData? CompileFile(string file, string displayName)
    {
        try
        {
            return ScriptCompiler.CompileFile(file);
        }
        catch (ScriptError e)
        {
            Console.Error.WriteLine(displayName + ": " + e);
            return null;
        }
    }

    private static void Write(AnimationData animation, string output)
    {
        File.WriteAllText(output, HtmlExporter.Export(animation), new UTF8Encoding(false));
        Console.WriteLine($"Written: {output} ({animation.Steps.Count} steps, {animation.Duration / 1000:0.0} s)");
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
