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
          geoanim exportdir <dir> [-o <output directory>] [-assets] [-serve [port]]
              Exports directory dir as a book: a page for every .geo file in it and all subdirectories (not for those
              starting with "_"), next to it or with -o in the same folder structure below the output directory, with
              an index.html linking to all pages. "20_Strecken.geo" becomes "Strecken.html" (the number gives the
              order). Each page has everything inside; with -assets the pages link to the player's files in assets/
              instead (for a web server). With -serve it then serves the book on http://localhost:8080/ (or the port
              given).
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
            int? servePort = null;
            bool assets = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] is "-o" or "--output" && i + 1 < args.Length)
                {
                    outputFolder = args[++i];
                }
                else if (args[i] is "-assets" or "--assets")
                {
                    assets = true;
                }
                else if (args[i] is "-serve" or "--serve")
                {
                    servePort = i + 1 < args.Length && int.TryParse(args[i + 1], out int port) ? port : 8080;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out _))
                    {
                        i++;
                    }
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
            int exported = ExportDirectory(args[1], outputFolder, assets);
            return servePort == null || !Directory.Exists(args[1]) ? exported : ServeBook(args[1], outputFolder, servePort.Value);
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
    /// the overview and the pages before and after it. A faulty script does not stop the others. Each page has the player
    /// inside; with <paramref name="assets"/> the pages link to the player's files in assets/ at the root of the book
    /// instead (smaller pages for a web server: the files load once).
    /// </summary>
    internal static int ExportDirectory(string folder, string? outputFolder = null, bool assets = false)
    {
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine("Directory not found: " + folder);
            return 1;
        }
        string root = Path.GetFullPath(folder);
        string target = outputFolder == null ? root : Path.GetFullPath(outputFolder);
        // the scripts in book order: files and subfolders of a folder together by their numbers (by value)
        List<string> files = BookKeywords.Scripts(root);
        int failed = 0;
        int conflicts = 0;
        var progress = new Progress(files.Count);
        // what the book says about itself (index.yaml): title, icon, address, …
        BookSettings settings = BookFile.Read(root, progress.Warning, out string? bookError);
        if (bookError != null)
        {
            progress.Error(bookError);
            conflicts++;
        }
        // the icon: inside every page, or once in assets/
        string? iconData = null;
        string? iconName = null;
        if (settings.IconFile != null)
        {
            byte[] icon = File.ReadAllBytes(settings.IconFile);
            iconName = Path.GetFileName(settings.IconFile);
            string mime = Path.GetExtension(iconName).ToLowerInvariant() switch
            {
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                _ => "image/png",
            };
            iconData = "data:" + mime + ";base64," + Convert.ToBase64String(icon);
            if (assets)
            {
                string iconTarget = Path.Combine(target, AssetsFolder, iconName);
                Directory.CreateDirectory(Path.GetDirectoryName(iconTarget)!);
                if (!File.Exists(iconTarget) || !File.ReadAllBytes(iconTarget).AsSpan().SequenceEqual(icon))
                {
                    File.WriteAllBytes(iconTarget, icon);
                }
                iconName += "?v=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(icon))[..10].ToLowerInvariant();
            }
        }
        // first all scripts: the keywords and names of every page are needed for the links in the others
        var compiled = new List<(string Source, string Page, AnimationData Animation)>();
        var pageOfSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string relative in files)
        {
            progress.Step(relative);
            string page = PageName(relative);
            string dropped = DroppedCharacters(relative);
            if (dropped.Length > 0)
            {
                progress.Warning($"{relative}: these characters are left out of its address {page}: {dropped}.");
            }
            if (compiled.Any(other => string.Equals(other.Page, page, StringComparison.OrdinalIgnoreCase)))
            {
                progress.Error($"{relative}: its page {page} is also the page of {compiled.First(o => string.Equals(o.Page, page, StringComparison.OrdinalIgnoreCase)).Source}.");
                failed++;
                continue;
            }
            AnimationData? animation = CompileFile(Path.Combine(root, relative), relative, progress.Error);
            if (animation == null)
            {
                failed++;
                continue;
            }
            compiled.Add((relative, page, animation));
            pageOfSource[Path.ChangeExtension(relative, ".html")] = page;
        }
        // keyword → its page; a keyword of two pages is an error (the first one keeps it). Places as "file:line" (relative
        // to the book's folder), so that both can be opened with a click in a terminal
        string Place(string source, PageKeyword keyword)
        {
            string file = keyword.File == null ? source : Path.GetRelativePath(root, keyword.File).Replace('\\', '/');
            return keyword.Line > 0 ? file + ":" + keyword.Line : file;
        }
        var keywordPages = new Dictionary<string, (string Page, string Source, int Step, PageKeyword Keyword)>(StringComparer.Ordinal);
        foreach (var page in compiled)
        {
            foreach (PageKeyword keyword in page.Animation.Keywords)
            {
                if (keywordPages.TryGetValue(keyword.Form, out var other))
                {
                    progress.Error($"{Place(page.Source, keyword)}: the keyword {keyword.Form} is already the keyword of {Place(other.Source, other.Keyword)}.");
                    conflicts++;
                }
                else
                {
                    keywordPages[keyword.Form] = (page.Page, page.Source, keyword.Step, keyword);
                }
            }
        }
        // the pages in the overview and the navigation: those with something drawn (not e.g. a file of defaults)
        List<string> book = compiled.Where(p => !p.Animation.IsEmpty).Select(p => p.Page).ToList();
        var titles = compiled.ToDictionary(p => p.Page, p => IndexPage.TitleOf(p.Page, p.Animation));
        var pages = new List<(string Path, AnimationData Animation)>();
        // for the keyword index: the pages that mention each term (page and term of the keyword line)
        var mentions = new Dictionary<(string Page, string Term), List<string>>();
        foreach (var page in compiled)
        {
            // links written with the scripts' names ("20_Strecken.html#2") → the pages' names
            AnimationData animation = Keywords.RewriteLinks(page.Animation, href => PageAddress(page.Source, page.Page, href, pageOfSource));
            // inside a step: to its end ("page.html#3")
            var links = keywordPages.Where(k => k.Value.Page != page.Page)
                .ToDictionary(k => k.Key, k => Keywords.RelativeAddress(page.Page, k.Value.Page) + (k.Value.Step > 0 ? "#" + k.Value.Step : ""),
                    StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            animation = Keywords.Link(animation, links, used);
            foreach (var term in used.Select(form => keywordPages[form]).Select(k => (k.Page, k.Keyword.Term)).Distinct())
            {
                if (!mentions.TryGetValue(term, out List<string>? on))
                {
                    mentions[term] = on = [];
                }
                on.Add(page.Page);
            }
            int position = book.IndexOf(page.Page);
            PageLink? Neighbour(int i) => position >= 0 && i >= 0 && i < book.Count
                ? new PageLink(titles[book[i]], Keywords.RelativeAddress(page.Page, book[i]))
                : null;
            var navigation = new PageNavigation(Keywords.RelativeAddress(page.Page, "index.html"), Neighbour(position - 1), Neighbour(position + 1))
            {
                Home = settings.Home,
                Footer = settings.Footer,
            };
            string output = Path.Combine(target, page.Page.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            // with assets: the player's files once for the whole book, in assets/ at its root
            string? assetsAddress = assets ? string.Concat(Enumerable.Repeat("../", page.Page.Count(c => c == '/'))) + AssetsFolder + "/" : null;
            var head = new PageHead(settings.Language, assets && iconName != null ? assetsAddress + iconName : iconData, settings.AddressOf(page.Page), settings.Title);
            File.WriteAllText(output, HtmlExporter.Export(animation, navigation, assetsAddress, head), new UTF8Encoding(false));
            pages.Add((page.Page, animation));
        }
        Directory.CreateDirectory(target);
        if (assets)
        {
            HtmlExporter.WriteAssets(Path.Combine(target, AssetsFolder));
        }
        string indexFile = Path.Combine(target, "index.html");
        var indexHead = new PageHead(settings.Language, assets && iconName != null ? AssetsFolder + "/" + iconName : iconData, settings.AddressOf("index.html"), settings.Title);
        // the chapters' headings: the folders' names as written ("40_Winkelsätze" → "Winkelsätze")
        var chapters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string source in compiled.Select(p => p.Source))
        {
            string[] parts = source.Split('/');
            for (int i = 1; i < parts.Length; i++)
            {
                chapters[PageName(string.Join("/", parts[..i]), folder: true)] = DisplayName(parts[i - 1]);
            }
        }
        // the keyword index: every term once per page that explains it, with the pages that mention it in book order
        var entries = keywordPages.Values
            .GroupBy(k => (k.Page, k.Keyword.Term))
            .Select(g => g.First())
            .Select(k => new KeywordEntry(k.Keyword.Term,
                new PageLink(titles[k.Page], k.Page + (k.Step > 0 ? "#" + k.Step : "")),
                (mentions.GetValueOrDefault((k.Page, k.Keyword.Term)) ?? []).OrderBy(book.IndexOf).Select(p => new PageLink(titles[p], p)).ToList()))
            .ToList();
        string? keywordIndex = entries.Count > 0 ? KeywordIndexPage : null;
        if (keywordIndex != null)
        {
            var keywordHead = new PageHead(settings.Language, indexHead.Icon, settings.AddressOf(KeywordIndexPage), settings.Title);
            File.WriteAllText(Path.Combine(target, KeywordIndexPage), IndexPage.CreateKeywordIndex(Path.GetFileName(root), entries, "index.html", settings, keywordHead),
                new UTF8Encoding(false));
        }
        File.WriteAllText(indexFile, IndexPage.Create(Path.GetFileName(root), pages, settings, indexHead, chapters, keywordIndex), new UTF8Encoding(false));
        // for search engines: all pages with their address on the web
        if (settings.Url != null)
        {
            string sitemap = Path.Combine(target, "sitemap.xml");
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
            void Entry(string page, DateTime changed) => xml.Append("  <url><loc>").Append(System.Security.SecurityElement.Escape(settings.AddressOf(page)))
                .Append("</loc><lastmod>").Append(changed.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append("</lastmod></url>\n");
            var changes = compiled.Where(p => book.Contains(p.Page)).Select(p => (p.Page, Changed: File.GetLastWriteTime(Path.Combine(root, p.Source)))).ToList();
            Entry("index.html", changes.Count > 0 ? changes.Max(c => c.Changed) : DateTime.Now);
            if (keywordIndex != null)
            {
                Entry(KeywordIndexPage, changes.Count > 0 ? changes.Max(c => c.Changed) : DateTime.Now);
            }
            foreach ((string page, DateTime changed) in changes)
            {
                Entry(page, changed);
            }
            File.WriteAllText(sitemap, xml.Append("</urlset>\n").ToString(), new UTF8Encoding(false));
        }
        progress.Finish($"{files.Count - failed} of {files.Count} pages exported to {target} (index.html"
            + (keywordIndex != null ? ", " + KeywordIndexPage : "")
            + (settings.Url != null ? ", sitemap.xml" : "") + (assets ? ", assets/" : "") + ")");
        return failed > 0 || conflicts > 0 ? 1 : 0;
    }

    /// <summary>The keyword index of an exported book, next to index.html.</summary>
    internal const string KeywordIndexPage = "stichwoerter.html";

    /// <summary>The folder of an exported book with the player's files (script, stylesheets, fonts).</summary>
    internal const string AssetsFolder = "assets";

    /// <summary>
    /// After the export: serves the book on http://localhost:port/ (no browser is opened), always fresh from disk, so that
    /// a reload in the browser shows the latest export (exporting again is up to the author). Ctrl+C ends.
    /// </summary>
    private static int ServeBook(string folder, string? outputFolder, int port)
    {
        string target = outputFolder == null ? Path.GetFullPath(folder) : Path.GetFullPath(outputFolder);
        using var server = new BookServer(target, LiveServer.FindFreePort(port));
        Console.WriteLine($"GeoAnim: {server.Url}  (serving {target}; Ctrl+C ends)");
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            server.Dispose();
        };
        server.Wait();
        return 0;
    }

    /// <summary>
    /// The address of a page (or with <paramref name="folder"/> of a folder) from the script's path: without the numbers
    /// in front, which only give the order, and as it suits web addresses – ä → ae, ö → oe, ü → ue, ß → ss, é → e,
    /// spaces → "-", other characters left out: "10_Grundlagen/40_Winkelsätze/20_Größe.geo" → "Grundlagen/Winkelsaetze/Groesse.html".
    /// </summary>
    internal static string PageName(string relative, bool folder = false) =>
        string.Join("/", (folder ? relative : Path.ChangeExtension(relative, ".html")).Split('/').Select(part => WebName(SortNumber().Replace(part, ""))));

    // "Winkelsätze und Größen" → "Winkelsaetze-und-Groessen"
    private static string WebName(string part)
    {
        var name = new StringBuilder();
        foreach (char c in part.Normalize(NormalizationForm.FormC))
        {
            string? replacement = c switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'Ä' => "Ae", 'Ö' => "Oe", 'Ü' => "Ue", 'ß' => "ss",
                ' ' => "-",
                _ when char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' => c.ToString(),
                // é → e: the letter without its accent
                _ => string.Concat(c.ToString().Normalize(NormalizationForm.FormD).Where(char.IsAsciiLetter)),
            };
            name.Append(replacement);
        }
        return System.Text.RegularExpressions.Regex.Replace(name.ToString(), "-{2,}", "-");
    }

    // the characters of a script's path that its address leaves out (each once): neither ASCII nor an umlaut, ß or a letter with an accent
    internal static string DroppedCharacters(string relative) =>
        string.Join(" ", relative.Normalize(NormalizationForm.FormC)
            .Where(c => c != '/' && c != ' ' && WebName(c.ToString()).Length == 0).Distinct().Select(c => c.ToString()));

    // the heading of a chapter from its folder's name: "40_Winkelsätze" → "Winkelsätze", "30_Strahl-und-Gerade" → "Strahl und Gerade"
    internal static string DisplayName(string folder) => SortNumber().Replace(folder, "").Replace('-', ' ').Replace('_', ' ');

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
    private static AnimationData? CompileFile(string file, string displayName, Action<string>? error = null)
    {
        try
        {
            return ScriptCompiler.CompileFile(file);
        }
        catch (ScriptError e)
        {
            (error ?? Console.Error.WriteLine)(displayName + ": " + e);
            return null;
        }
    }

    /// <summary>
    /// The scripts of a book in their order: segment by segment, files and folders of a folder together by their names,
    /// numbers by value ("30_Rechte-Winkel.geo" before "40_Winkelsätze/…" before "50_Winkelarten.geo").
    /// </summary>
    internal sealed class BookOrder : IComparer<string>
    {
        public static BookOrder Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            string[] a = (x ?? "").Split('/');
            string[] b = (y ?? "").Split('/');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                string left = i == a.Length - 1 ? Path.GetFileNameWithoutExtension(a[i]) : a[i];
                string right = i == b.Length - 1 ? Path.GetFileNameWithoutExtension(b[i]) : b[i];
                int result = IndexPage.NaturalOrder.Instance.Compare(left, right);
                if (result != 0)
                {
                    return result;
                }
            }
            return a.Length.CompareTo(b.Length);
        }
    }

    /// <summary>
    /// The progress of an export: in a console one line that shows the script being exported; errors (red) and warnings
    /// (yellow) stand on lines of their own above it, so that none gets lost among the pages; at the end a summary.
    /// </summary>
    private sealed class Progress(int total)
    {
        private readonly bool live = !Console.IsOutputRedirected && !Console.IsErrorRedirected;
        private int done;
        private int errors;
        private int warnings;
        private int shown;

        public void Step(string name)
        {
            done++;
            if (live)
            {
                string text = $"[{done}/{total}] {name}";
                int width = Math.Max(20, SafeWidth() - 1);
                if (text.Length > width)
                {
                    text = text[..(width - 1)] + "…";
                }
                Clear();
                Console.Write(text);
                shown = text.Length;
            }
        }

        public void Error(string message)
        {
            errors++;
            Report(message, ConsoleColor.Red);
        }

        public void Warning(string message)
        {
            warnings++;
            Report("Warning: " + message, ConsoleColor.Yellow);
        }

        public void Finish(string summary)
        {
            Clear();
            string problems = (errors > 0 ? $", {errors} error{(errors == 1 ? "" : "s")}" : "") + (warnings > 0 ? $", {warnings} warning{(warnings == 1 ? "" : "s")}" : "");
            if (live && errors > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
            Console.WriteLine(summary + problems + (errors > 0 ? " – see above." : "."));
            if (live)
            {
                Console.ResetColor();
            }
        }

        private void Report(string message, ConsoleColor color)
        {
            Clear();
            if (live)
            {
                Console.ForegroundColor = color;
            }
            Console.Error.WriteLine(message);
            if (live)
            {
                Console.ResetColor();
            }
        }

        private void Clear()
        {
            if (live && shown > 0)
            {
                Console.Write("\r" + new string(' ', shown) + "\r");
                shown = 0;
            }
        }

        private static int SafeWidth()
        {
            try
            {
                return Console.WindowWidth > 0 ? Console.WindowWidth : 80;
            }
            catch (IOException)
            {
                return 80;
            }
        }
    }

    private static void Write(AnimationData animation, string output, PageNavigation? navigation = null, string? assets = null, PageHead? head = null)
    {
        File.WriteAllText(output, HtmlExporter.Export(animation, navigation, assets, head), new UTF8Encoding(false));
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
