using System.Diagnostics;
using System.Text;
using GeoAnim.Script;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

/// <summary>
/// geoanim serve &lt;file.geo&gt; [--port N] [--no-browser]: live preview in the browser, updated on every save.<br/>
/// geoanim export &lt;file.geo&gt; [-o file.html]: one self-contained HTML file.<br/>
/// geoanim exportdir &lt;dir&gt; [-o dir]: the folder as a book of pages with an overview and navigation.<br/>
/// geoanim daemon: compiles for an editor over standard input and output (see <see cref="Daemon"/>).
/// </summary>
internal static partial class Program
{
    private const string Usage = """
        Usage:
          geoanim serve <file.geo> [--port 8080] [--no-browser]
              Shows the animation in the browser and updates it whenever the file is saved.
          geoanim export <file.geo> [-o <file.html>]
              Writes the animation as one HTML file that plays without a server (default: next to the script).
          geoanim exportdir <dir> [-o <output directory>]
              Exports directory dir as a book: a page for every .geo file in it and all subdirectories (not for those
              starting with "_"), next to it or with -o in the same folder structure below the output directory, with
              an index.html linking to all pages. "20_Strecken.geo" becomes "Strecken.html" (the number gives the order).
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
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] is "-o" or "--output" && i + 1 < args.Length)
                {
                    outputFolder = args[++i];
                }
                else if (args[i] is "-index" or "--index")
                {
                    // the overview is always written now
                }
                else
                {
                    Console.Error.WriteLine("Unknown option " + args[i]);
                    Console.WriteLine(Usage);
                    return 1;
                }
            }
            return ExportDirectory(args[1], outputFolder);
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
    /// Exports a folder as a book: every .geo file below <paramref name="folder"/> becomes a page, next to it or, with
    /// <paramref name="outputFolder"/>, in the same folder structure below that folder; an index.html there links to all
    /// pages. Files and folders starting with "_" are helpers (no page); a number in front ("20_Strecken.geo",
    /// "10_Grundlagen") gives the order and is left out of the page's name ("Grundlagen/Strecken.html"). Links between the
    /// pages may use the scripts' names; the keywords of every page link to it from the others. Each page gets the way to
    /// the overview and the pages before and after it. A faulty script does not stop the others.
    /// </summary>
    internal static int ExportDirectory(string folder, string? outputFolder = null)
    {
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine("Directory not found: " + folder);
            return 1;
        }
        string root = Path.GetFullPath(folder);
        string target = outputFolder == null ? root : Path.GetFullPath(outputFolder);
        // the scripts in book order: the files of a folder before its subfolders, numbers by value
        var order = IndexPage.NaturalOrder.Instance;
        List<string> files = Directory.EnumerateFiles(root, "*.geo", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(relative => !relative.Split('/').Any(part => part.StartsWith('_')))
            .OrderBy(relative => relative.Contains('/') ? relative[..relative.LastIndexOf('/')] : "", order)
            .ThenBy(relative => relative[(relative.LastIndexOf('/') + 1)..], order)
            .ToList();
        int failed = 0;
        int conflicts = 0;
        // first all scripts: the keywords and names of every page are needed for the links in the others
        var compiled = new List<(string Source, string Page, AnimationData Animation)>();
        var pageOfSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string relative in files)
        {
            string page = PageName(relative);
            string unsafeCharacters = UnsafeCharacters(page);
            if (unsafeCharacters.Length > 0)
            {
                // works, but is fragile on a web server (upload, Unicode forms, copied addresses)
                Console.Error.WriteLine($"Warning: {relative}: the page name {page} has characters that are better avoided in web addresses: {unsafeCharacters} (e.g. ä → ae, ß → ss, space → -).");
            }
            if (compiled.Any(other => string.Equals(other.Page, page, StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine($"{relative}: its page {page} is also the page of {compiled.First(o => string.Equals(o.Page, page, StringComparison.OrdinalIgnoreCase)).Source} (without the numbers in front).");
                failed++;
                continue;
            }
            AnimationData? animation = CompileFile(Path.Combine(root, relative), relative);
            if (animation == null)
            {
                failed++;
                continue;
            }
            compiled.Add((relative, page, animation));
            pageOfSource[Path.ChangeExtension(relative, ".html")] = page;
        }
        // keyword → its page; a keyword of two pages is an error (the first one keeps it)
        var keywordPages = new Dictionary<string, (string Page, string Source, int Step)>(StringComparer.Ordinal);
        foreach (var page in compiled)
        {
            foreach (PageKeyword keyword in page.Animation.Keywords)
            {
                if (keywordPages.TryGetValue(keyword.Form, out var other))
                {
                    Console.Error.WriteLine($"{page.Source}: the keyword {keyword.Form} is already the keyword of {other.Source}.");
                    conflicts++;
                }
                else
                {
                    keywordPages[keyword.Form] = (page.Page, page.Source, keyword.Step);
                }
            }
        }
        // the pages in the overview and the navigation: those with something drawn (not e.g. a file of defaults)
        List<string> book = compiled.Where(p => !p.Animation.IsEmpty).Select(p => p.Page).ToList();
        var titles = compiled.ToDictionary(p => p.Page, p => IndexPage.TitleOf(p.Page, p.Animation));
        var pages = new List<(string Path, AnimationData Animation)>();
        foreach (var page in compiled)
        {
            // links written with the scripts' names ("20_Strecken.html#2") → the pages' names
            AnimationData animation = Keywords.RewriteLinks(page.Animation, href => PageAddress(page.Source, page.Page, href, pageOfSource));
            // inside a step: to its end ("page.html#3")
            var links = keywordPages.Where(k => k.Value.Page != page.Page)
                .ToDictionary(k => k.Key, k => Keywords.RelativeAddress(page.Page, k.Value.Page) + (k.Value.Step > 0 ? "#" + k.Value.Step : ""),
                    StringComparer.Ordinal);
            animation = Keywords.Link(animation, links);
            int position = book.IndexOf(page.Page);
            PageLink? Neighbour(int i) => position >= 0 && i >= 0 && i < book.Count
                ? new PageLink(titles[book[i]], Keywords.RelativeAddress(page.Page, book[i]))
                : null;
            var navigation = new PageNavigation(Keywords.RelativeAddress(page.Page, "index.html"), Neighbour(position - 1), Neighbour(position + 1));
            string output = Path.Combine(target, page.Page.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            Write(animation, output, navigation);
            pages.Add((page.Page, animation));
        }
        Directory.CreateDirectory(target);
        string indexFile = Path.Combine(target, "index.html");
        File.WriteAllText(indexFile, IndexPage.Create(Path.GetFileName(root), pages), new UTF8Encoding(false));
        Console.WriteLine($"Written: {indexFile} ({book.Count} links)");
        Console.WriteLine($"{files.Count - failed} of {files.Count} files exported" + (failed > 0 ? $", {failed} with errors." : ".")
            + (conflicts > 0 ? $" {conflicts} keyword(s) given twice." : ""));
        return failed > 0 || conflicts > 0 ? 1 : 0;
    }

    // the characters of a page's path outside ASCII letters, digits, "-", "_", "." and "/" (each once, as they appear)
    internal static string UnsafeCharacters(string page) =>
        string.Join(" ", page.Where(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/')).Distinct()
            .Select(c => c == ' ' ? "(space)" : c.ToString()));

    // "10_Grundlagen/20_Strecken.geo" → "Grundlagen/Strecken.html": the numbers in front only give the order
    internal static string PageName(string relative) =>
        string.Join("/", Path.ChangeExtension(relative, ".html").Split('/').Select(part => SortNumber().Replace(part, "")));

    [System.Text.RegularExpressions.GeneratedRegex(@"^\d+_(?=.)")]
    private static partial System.Text.RegularExpressions.Regex SortNumber();

    // a link of a page to another one by its script's name ("../10_Grundlagen/20_Strecken.html#2") → by its page's name;
    // null: not a link to a page of the book (a site, a file, a page written with its new name)
    private static string? PageAddress(string source, string page, string href, Dictionary<string, string> pageOfSource)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(href, "^[a-z][a-z0-9+.-]*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            || href.StartsWith('/') || href.StartsWith('#'))
        {
            return null;
        }
        int cut = href.IndexOfAny(['#', '?']);
        string path = Uri.UnescapeDataString(cut < 0 ? href : href[..cut]);
        string rest = cut < 0 ? "" : href[cut..];
        string folder = source.Contains('/') ? source[..source.LastIndexOf('/')] : "";
        // resolved against the script's folder, relative to the book's root
        string combined = NormalizePath(folder.Length == 0 ? path : folder + "/" + path);
        return pageOfSource.TryGetValue(combined, out string? targetPage) ? Keywords.RelativeAddress(page, targetPage) + rest : null;
    }

    // "a/b/../c.html" → "a/c.html"
    private static string NormalizePath(string path)
    {
        var parts = new List<string>();
        foreach (string part in path.Split('/'))
        {
            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
                else
                {
                    parts.Add(part);
                }
            }
            else if (part.Length > 0 && part != ".")
            {
                parts.Add(part);
            }
        }
        return string.Join("/", parts);
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

    private static void Write(AnimationData animation, string output, PageNavigation? navigation = null)
    {
        File.WriteAllText(output, HtmlExporter.Export(animation, navigation), new UTF8Encoding(false));
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
