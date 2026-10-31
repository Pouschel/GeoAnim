using System.Text;
using System.Text.Json;
using GeoAnim.Figures;
using GeoAnim.Script;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

/// <summary>
/// "geoanim daemon": compiles for an editor (the VS Code extension), one JSON request per line on standard input, one
/// JSON answer per line on standard output. The process stays, so MathJax starts only once.
/// <list type="bullet">
/// <item>{"id": 1, "type": "compile", "file": "C:\\…\\a.geo", "text": "…"} → {"id": 1, "animation": {…} (with "inspect": every
/// object over time, for tooltips and ghosts in the preview) or null,
/// "errors": [{"file": "C:\\…", "line": 3, "column": 7, "message": "…"}], "warnings": […] (the same; e.g. a keyword of two
/// scripts of a book), "files": ["C:\\…", …], "definitions":
/// [{"name": "k", "kind": "var" | "set" | "object", "text": "…", "file": "C:\\…", "line": 4, "column": 5}, …],
/// "references": [{"name": "k", "file": "C:\\…", "line": 4, "column": 5}, …] (every place a name stands)} – the
/// text as if it were the file (unsaved changes; includes and uses relative to its folder).</item>
/// <item>{"id": 2, "type": "player"} → {"id": 2, "html": "…"} – the player page for the preview.</item>
/// <item>{"id": 3, "type": "keywords"} → {"id": 3, "keywords": ["angle", …], "colors": {"red": "#d32f2f", …}} – the words that
/// cannot be names, and the colour names.</item>
/// <item>{"id": 4, "type": "markdown", "file": "C:\\…\\a.geo.md", "text": "…"} → {"id": 4, "html": "…", "title": "…" or null,
/// "figures": {"key": {"animation": {…}, "from": 2 or null, "to": 3 or null, "still": false, "crop": true}, …}, "marks":
/// [{"name": "ax-gerade", "kind": "axiom", "label": "Axiom 2", "text": "…", "file": "C:\\…", "line": 4}, …] (of all pages of
/// the book: completion and "go to definition" for "§name"), "keywords": [{"term": "Strahl", "anchor": "kw-strahl" or
/// null, "file": "C:\\…", "line": 3, "column": 17}, …] (of all pages of the book: where a keyword link leads), "errors":
/// […], "warnings": […] (as for "compile"; a keyword of two pages is a warning)} – a page
/// of a book in Markdown, its figures from the scripts beside the file (as saved; compiled scripts are kept while their
/// files stay the same), its references to the marks of the book's pages (the other pages as saved).</item>
/// <item>{"id": 6, "type": "steps", "file": "C:\\…\\a.geo"} → {"id": 6, "steps": [{"number": 1, "mark": "gleich" or null,
/// "name": "gleiche-punkte", "caption": "…"}, …], "error": "…" or null} – the steps of a script, for the completion of
/// figures.</item>
/// <item>{"id": 5, "type": "page"} → {"id": 5, "html": "…"} – the page for the preview of a Markdown page.</item>
/// </list>
/// A request that fails completely gets {"id": …, "fatal": "…"}.
/// </summary>
internal static class Daemon
{
    public static int Run(TextReader input, TextWriter output)
    {
        string? line;
        while ((line = input.ReadLine()) != null)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }
            output.WriteLine(Handle(line));
            output.Flush();
        }
        return 0;
    }

    internal static string Handle(string request)
    {
        JsonElement id = default;
        try
        {
            JsonElement root = JsonDocument.Parse(request).RootElement;
            id = root.TryGetProperty("id", out JsonElement value) ? value.Clone() : default;
            string type = root.GetProperty("type").GetString() ?? "";
            return type switch
            {
                "compile" => Compile(id, root.GetProperty("file").GetString() ?? "", root.GetProperty("text").GetString() ?? ""),
                "player" => Answer(id, json => json.WriteString("html", HtmlExporter.ExportPlayer())),
                "markdown" => RenderMarkdown(id, root.GetProperty("file").GetString() ?? "", root.GetProperty("text").GetString() ?? ""),
                "page" => Answer(id, json => json.WriteString("html", HtmlExporter.ExportPagePreview())),
                "steps" => Steps(id, root.GetProperty("file").GetString() ?? ""),
                "keywords" => Answer(id, json =>
                {
                    json.WriteStartArray("keywords");
                    foreach (string keyword in ScriptCompiler.Keywords.Order(StringComparer.Ordinal))
                    {
                        json.WriteStringValue(keyword);
                    }
                    json.WriteEndArray();
                    // the colour names with their values (colour swatches in the editor)
                    json.WriteStartObject("colors");
                    foreach ((string name, Color color) in Color.Names)
                    {
                        json.WriteString(name, color.ToHex());
                    }
                    json.WriteEndObject();
                }),
                _ => Answer(id, json => json.WriteString("fatal", "Unknown request type \"" + type + "\".")),
            };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            return Answer(id, json => json.WriteString("fatal", e.Message));
        }
    }

    private static string Compile(JsonElement id, string file, string text)
    {
        // an editor's file without a path (not saved yet): includes relative to the current folder
        ScriptSource? source = null;
        string? animation = null;
        var errors = new List<(string? File, int Line, int Column, string Message)>();
        var warnings = new List<(string? File, int Line, string Message)>();
        try
        {
            source = Path.IsPathRooted(file) ? ScriptSource.FromFile(file, text) : ScriptSource.FromText(text);
            AnimationData compiled = ScriptCompiler.Compile(source, inspect: true);
            // a script in a book: the keyword links of the export, to the other scripts
            if (Path.IsPathRooted(file) && BookKeywords.RootOf(file) is { } root)
            {
                compiled = BookKeywords.Link(root, file, source, compiled, warnings);
            }
            animation = compiled.ToJson();
        }
        catch (ScriptError e)
        {
            string? errorFile = source?.FullPathOf(e) ?? (e.File == null && Path.IsPathRooted(file) ? Path.GetFullPath(file) : e.File);
            errors.Add((errorFile, e.Line, e.Column, e.Message));
        }
        catch (IOException e)
        {
            errors.Add((Path.IsPathRooted(file) ? Path.GetFullPath(file) : null, 1, 1, e.Message));
        }
        return Answer(id, json =>
        {
            json.WritePropertyName("animation");
            if (animation == null)
            {
                json.WriteNullValue();
            }
            else
            {
                json.WriteRawValue(animation);
            }
            json.WriteStartArray("errors");
            foreach ((string? errorFile, int line, int column, string message) in errors)
            {
                json.WriteStartObject();
                json.WriteString("file", errorFile);
                json.WriteNumber("line", line);
                json.WriteNumber("column", column);
                json.WriteString("message", message);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("warnings");
            foreach ((string? warningFile, int line, string message) in warnings)
            {
                json.WriteStartObject();
                json.WriteString("file", warningFile ?? (Path.IsPathRooted(file) ? Path.GetFullPath(file) : null));
                json.WriteNumber("line", line);
                json.WriteNumber("column", 1);
                json.WriteString("message", message);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("files");
            foreach (string read in source?.Files ?? [])
            {
                json.WriteStringValue(read);
            }
            json.WriteEndArray();
            // for tooltips, "go to definition", renaming and references
            (List<ScriptDefinition> definitions, List<ScriptReference> references) = source == null ? ([], []) : ScriptCompiler.Symbols(source);
            json.WriteStartArray("definitions");
            foreach (ScriptDefinition definition in definitions)
            {
                json.WriteStartObject();
                json.WriteString("name", definition.Name);
                json.WriteString("kind", definition.Kind);
                json.WriteString("text", definition.Text);
                json.WriteString("file", definition.File);
                json.WriteNumber("line", definition.Line);
                json.WriteNumber("column", definition.Column);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("references");
            foreach (ScriptReference reference in references)
            {
                json.WriteStartObject();
                json.WriteString("name", reference.Name);
                json.WriteString("file", reference.File);
                json.WriteNumber("line", reference.Line);
                json.WriteNumber("column", reference.Column);
                json.WriteEndObject();
            }
            json.WriteEndArray();
        });
    }

    private static string RenderMarkdown(JsonElement id, string file, string text)
    {
        string? path = Path.IsPathRooted(file) ? Path.GetFullPath(file) : null;
        BookOfPage book = path == null ? new BookOfPage([], PageNumbering.None, new Dictionary<string, KeywordLink>(), []) : ReadBook(path, text);
        List<BookMark> others = book.Marks;
        var bookMarks = new Dictionary<string, BookMark>(StringComparer.Ordinal);
        foreach (BookMark other in others)
        {
            bookMarks.TryAdd(other.Mark.Name, other);
        }
        MarkdownPage page = MarkdownPage.Parse(text, path, LoadScript, bookMarks, book.Numbering, book.KeywordLinks);
        (FigureStyle Style, double TextSize) print = path == null ? (FigureStyle.Default, 11) : BookPictures.PrintOf(Path.GetDirectoryName(path)!);
        return Answer(id, json =>
        {
            json.WriteString("html", page.Html);
            json.WriteString("title", page.Title);
            json.WriteStartObject("figures");
            foreach (PageFigure figure in page.PageFigures.DistinctBy(f => f.Key))
            {
                json.WriteStartObject(figure.Key);
                json.WritePropertyName("animation");
                json.WriteRawValue(figure.AnimationJson);
                void Index(string name, int? value)
                {
                    if (value is int index)
                    {
                        json.WriteNumber(name, index);
                    }
                    else
                    {
                        json.WriteNull(name);
                    }
                }
                Index("from", figure.From);
                Index("to", figure.To);
                json.WriteBoolean("still", figure.Still);
                json.WriteBoolean("crop", figure.Crop);
                // as in print: the pictures in their size (pt) and the font size of the text they go with
                json.WriteNumber("textSize", print.TextSize);
                json.WriteStartArray("pictures");
                foreach ((FigureImage image, string? caption) in PrintPictures(figure, print.Style).Pictures)
                {
                    json.WriteStartObject();
                    json.WriteString("svg", image.Svg);
                    json.WriteNumber("width", Math.Round(image.Width, 2));
                    json.WriteNumber("height", Math.Round(image.Height, 2));
                    json.WriteString("caption", caption);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndObject();
            void Problems(string name, IEnumerable<MarkdownProblem> problems)
            {
                json.WriteStartArray(name);
                foreach (MarkdownProblem problem in problems)
                {
                    json.WriteStartObject();
                    json.WriteString("file", path);
                    json.WriteNumber("line", problem.Line);
                    json.WriteNumber("column", problem.Column);
                    json.WriteString("message", problem.Message);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
            }
            json.WriteStartArray("marks");
            foreach (BookMark mark in page.Marks.Select(m => new BookMark(path ?? "", m)).Concat(others))
            {
                json.WriteStartObject();
                json.WriteString("name", mark.Mark.Name);
                json.WriteString("kind", mark.Mark.Kind);
                json.WriteString("label", mark.Mark.Label);
                json.WriteString("text", mark.Mark.Text);
                json.WriteString("file", mark.File.Length > 0 ? mark.File : null);
                json.WriteNumber("line", mark.Mark.Line);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            // the keywords of the book's pages: where a link to one leads in the editor
            json.WriteStartArray("keywords");
            foreach ((string keywordFile, PageKeywordDefinition keyword) in book.Definitions)
            {
                json.WriteStartObject();
                json.WriteString("term", keyword.Term);
                json.WriteString("anchor", keyword.Anchor);
                json.WriteString("file", keywordFile);
                json.WriteNumber("line", keyword.Line);
                json.WriteNumber("column", keyword.Column);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            Problems("errors", page.Errors);
            // a figure whose texts are larger next to its drawing in print than in the player
            IEnumerable<MarkdownProblem> figureWarnings = page.PageFigures.DistinctBy(f => f.Key)
                .Select(f => (Figure: f, Warning: PrintPictures(f, print.Style).Warning))
                .Where(f => f.Warning != null)
                .Select(f => new MarkdownProblem(f.Figure.Reference.Line, f.Figure.Reference.Column, f.Warning!));
            Problems("warnings", page.Warnings.Concat(book.Warnings).Concat(figureWarnings));
        });
    }

    // the pictures of figures as in print, made once for each figure and size
    private static readonly Dictionary<string, (List<(FigureImage Image, string? Caption)> Pictures, string? Warning)> PrintCache = new(StringComparer.Ordinal);

    // the pictures of a figure as in print; in a row of stills each with its step's description (HTML; no number: in the
    // script the steps may be 3, 4, 5)
    private static (List<(FigureImage Image, string? Caption)> Pictures, string? Warning) PrintPictures(PageFigure figure, FigureStyle style)
    {
        string key = figure.Key + "|" + style.PointsPerUnit.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
            + "|" + style.SizeFactor.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (PrintCache.TryGetValue(key, out var known))
        {
            return known;
        }
        if (PrintCache.Count > 200)
        {
            PrintCache.Clear();
        }
        var animation = FigureAnimation.Parse(figure.AnimationJson);
        List<FigureImage> images = BookPictures.Of(figure, animation, style, out FigureStyle used);
        List<string?> captions = BookPictures.Captions(figure, animation, images.Count);
        var pictures = images.Select((image, i) => (image, captions[i] is { } caption ? MarkdownPage.InlineHtml(caption) : null)).ToList();
        var result = (pictures, BookPictures.TextWarning(figure, animation, used));
        PrintCache[key] = result;
        return result;
    }

    // what a page needs of its book: the marks of the other pages, its numbers (section, counts before it), the links of the
    // other pages' keywords (form → link) and warnings for its own keywords that other pages have too
    internal sealed record BookOfPage(List<BookMark> Marks, PageNumbering Numbering, IReadOnlyDictionary<string, KeywordLink> KeywordLinks,
        List<MarkdownProblem> Warnings)
    {
        // the keywords of the book's pages (not of its scripts), for the editor to find the place of one
        public List<(string File, PageKeywordDefinition Keyword)> Definitions { get; } = [];
    }

    // the marks and keywords of a page "*.geo.md", as saved; read again when it has changed
    private static readonly Dictionary<string, (DateTime Written, string Numbering, MarksOfPage Marks)> PageMarks =
        new(StringComparer.OrdinalIgnoreCase);

    private static MarksOfPage? ReadPage(string file, BookNumbering numbering)
    {
        DateTime written = File.GetLastWriteTimeUtc(file);
        if (PageMarks.TryGetValue(file, out var known) && known.Written == written && known.Numbering == numbering.Key)
        {
            return known.Marks;
        }
        try
        {
            known = (written, numbering.Key, MarkdownPage.ReadMarks(File.ReadAllText(file), numbering));
        }
        catch (IOException)
        {
            return null;
        }
        PageMarks[file] = known;
        return known.Marks;
    }

    // the book of a page: its pages "*.geo.md" and scripts "*.geo" below the folder with index.yaml (else beside the page;
    // not in folders or files starting with "_") in book order, the other pages as saved, the page itself as in the editor
    internal static BookOfPage ReadBook(string page, string text)
    {
        // as the paths of the book's files are compared with it ("C:/a/b" is "C:\a\b" on Windows)
        page = Path.GetFullPath(page);
        string? bookRoot = BookKeywords.RootOf(page);
        string root = bookRoot ?? Path.GetDirectoryName(page)!;
        BookNumbering numbering = bookRoot == null ? BookNumbering.Default : BookFile.Read(bookRoot, _ => { }, out _).Numbering;
        var marks = new List<BookMark>();
        var warnings = new List<MarkdownProblem>();
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.geo*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".geo.md", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".geo", StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Where(relative => !relative.Split('/').Any(part => part.StartsWith('_')))
                .ToList();
        }
        catch (IOException)
        {
            return new BookOfPage(marks, PageNumbering.None, new Dictionary<string, KeywordLink>(), warnings);
        }
        string own = Path.GetRelativePath(root, page).Replace('\\', '/');
        if (!files.Contains(own, StringComparer.OrdinalIgnoreCase))
        {
            files.Add(own);
        }
        files.Sort(Program.BookOrder.Instance);
        MarksOfPage ownMarks = MarkdownPage.ReadMarks(text, numbering);
        // the numbers of the chapters and pages; the counts of the pages before, through the book and in the chapter
        Dictionary<string, string> numbers = BookNumbers(files.Where(f => f.EndsWith(".geo.md", StringComparison.OrdinalIgnoreCase)));
        var bookCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var chapterCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        string? chapter = null;
        PageNumbering ownNumbering = PageNumbering.None with { Book = numbering };
        string folder = Path.GetDirectoryName(page)!;
        string Href(string file, string? anchor) => Path.GetRelativePath(folder, file).Replace('\\', '/') + (anchor != null ? "#" + anchor : "");
        // the keywords in book order: the first page with a form keeps it (as in the export)
        var keywords = new Dictionary<string, (string File, KeywordLink Link)>(StringComparer.Ordinal);
        var ownForms = new Dictionary<string, PageKeywordDefinition>(StringComparer.Ordinal);
        var definitions = new List<(string File, PageKeywordDefinition Keyword)>();
        foreach (string relative in files)
        {
            string file = Path.GetFullPath(Path.Combine(root, relative));
            bool isOwn = string.Equals(file, page, StringComparison.OrdinalIgnoreCase);
            if (relative.EndsWith(".geo", StringComparison.OrdinalIgnoreCase))
            {
                // a script with a page of its own (seite.geo beside seite.geo.md): the page's keywords count, not the script's
                if (files.Contains(relative + ".md", StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }
                // a script: its keywords lead to its page, or to the step they stand at
                string title = Program.DisplayName(Path.GetFileNameWithoutExtension(relative));
                foreach (PageKeyword keyword in BookKeywords.Read(file))
                {
                    AddKeyword(keyword.Form, file, new KeywordLink(Href(file, keyword.Step > 0 ? keyword.Step.ToString(System.Globalization.CultureInfo.InvariantCulture) : null), title), null);
                }
                continue;
            }
            MarksOfPage? read = isOwn ? ownMarks : ReadPage(file, numbering);
            if (read == null)
            {
                continue;
            }
            PageNumbering pageNumbering = NumberingOf(relative);
            if (isOwn)
            {
                ownNumbering = pageNumbering;
            }
            else
            {
                marks.AddRange(read.Marks.Select(m => new BookMark(file, MarkdownPage.Renumbered(m, pageNumbering))));
            }
            foreach ((string counter, int count) in read.Counts)
            {
                bookCounts[counter] = bookCounts.GetValueOrDefault(counter) + count;
                chapterCounts[counter] = chapterCounts.GetValueOrDefault(counter) + count;
            }
            string pageTitle = read.Title ?? Path.GetFileName(file);
            foreach (PageKeywordDefinition keyword in read.Keywords)
            {
                definitions.Add((file, keyword));
                foreach (string form in keyword.Forms)
                {
                    AddKeyword(form, file, new KeywordLink(Href(file, keyword.Anchor), pageTitle), isOwn ? keyword : null);
                }
            }
        }
        // the own page's keywords are not linked on it
        var links = keywords.Where(k => !string.Equals(k.Value.File, page, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(k => k.Key, k => k.Value.Link, StringComparer.Ordinal);
        var book = new BookOfPage(marks, ownNumbering, links, warnings);
        book.Definitions.AddRange(definitions);
        return book;

        // the numbers of a page: its section and chapter, the counts before it (a new chapter starts its counters again)
        PageNumbering NumberingOf(string relative)
        {
            string? section = numbers.GetValueOrDefault(relative);
            string? pageChapter = section?.Split('.')[0];
            if (pageChapter != chapter)
            {
                chapterCounts.Clear();
                chapter = pageChapter;
            }
            var before = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string counter in BookNumbering.Kinds.Select(numbering.CounterOf).Distinct())
            {
                before[counter] = numbering.ScopeOf(counter) switch
                {
                    NumberScope.Book => bookCounts.GetValueOrDefault(counter),
                    NumberScope.Chapter => chapterCounts.GetValueOrDefault(counter),
                    _ => 0,
                };
            }
            return new PageNumbering(numbering, section, pageChapter, before);
        }

        void AddKeyword(string form, string file, KeywordLink link, PageKeywordDefinition? ownKeyword)
        {
            if (!keywords.TryGetValue(form, out var first))
            {
                keywords[form] = (file, link);
                if (ownKeyword != null)
                {
                    ownForms[form] = ownKeyword;
                }
                return;
            }
            if (ownKeyword != null)
            {
                warnings.Add(new MarkdownProblem(ownKeyword.Line, ownKeyword.Column,
                    string.Equals(first.File, page, StringComparison.OrdinalIgnoreCase) && ownForms.TryGetValue(form, out PageKeywordDefinition? earlier)
                        ? $"The keyword {form} is on this page already (line {earlier.Line})."
                        : $"The keyword {form} is already the keyword of {Path.GetFileName(first.File)} (links go there)."));
            }
            else if (string.Equals(first.File, page, StringComparison.OrdinalIgnoreCase) && ownForms.TryGetValue(form, out PageKeywordDefinition? mine))
            {
                warnings.Add(new MarkdownProblem(mine.Line, mine.Column, $"The keyword {form} is also the keyword of {Path.GetFileName(file)}."));
            }
        }
    }

    /// <summary>
    /// The numbers of the chapters and pages of a book from its pages (relative paths with "/", in book order): a folder in
    /// the book's folder is a chapter ("2"), the pages and folders in it are numbered together in their order ("2.1",
    /// "2.2", a folder "2.3" with its pages "2.3.1" …). Pages in the book's folder itself (a preface) have none. Folders
    /// without pages do not count. Keys: the pages' paths and the folders' paths (without "/" at the end).
    /// </summary>
    internal static Dictionary<string, string> BookNumbers(IEnumerable<string> pages)
    {
        var numbers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var next = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string pagePath in pages)
        {
            string[] parts = pagePath.Split('/');
            if (parts.Length == 1)
            {
                continue;
            }
            string parent = "";
            string prefix = "";
            for (int i = 0; i < parts.Length; i++)
            {
                string path = i == 0 ? parts[0] : parent + "/" + parts[i];
                if (!numbers.TryGetValue(path, out string? number))
                {
                    int index = next[parent] = next.GetValueOrDefault(parent) + 1;
                    number = numbers[path] = prefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                prefix = number + ".";
                parent = path;
            }
        }
        return numbers;
    }

    // the steps of a script, for the completion of a figure's steps
    private static string Steps(JsonElement id, string file) => Answer(id, json =>
    {
        json.WriteStartArray("steps");
        string? error = null;
        try
        {
            AnimationData animation = LoadScript(Path.GetFullPath(file));
            int number = 0;
            foreach (AnimationStep step in animation.Steps)
            {
                json.WriteStartObject();
                json.WriteNumber("number", ++number);
                json.WriteString("mark", step.Mark);
                json.WriteString("name", MarkdownPage.StepName(step.Caption));
                json.WriteString("caption", step.Caption);
                json.WriteEndObject();
            }
        }
        catch (Exception e) when (e is FormatException or IOException or ArgumentException)
        {
            error = e.Message;
        }
        json.WriteEndArray();
        json.WriteString("error", error);
    });

    // the scripts of the figures, compiled, with the files they read and when those were written
    private static readonly Dictionary<string, (List<(string File, DateTime Written)> Files, AnimationData Animation)> Scripts =
        new(StringComparer.OrdinalIgnoreCase);

    // a script for a figure: compiled once, again when it or a file it reads has changed
    internal static AnimationData LoadScript(string path)
    {
        if (Scripts.TryGetValue(path, out var known) && known.Files.All(f => File.Exists(f.File) && File.GetLastWriteTimeUtc(f.File) == f.Written))
        {
            return known.Animation;
        }
        ScriptSource source = ScriptSource.FromFile(path);
        AnimationData animation;
        try
        {
            animation = ScriptCompiler.Compile(source);
        }
        catch (ScriptError e)
        {
            string where = source.FullPathOf(e) is { } other && !string.Equals(other, path, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(other) + ", line " : "line ";
            throw new FormatException($"{Path.GetFileName(path)}: {where}{e.Line}: {e.Message}");
        }
        var files = source.Files.Append(path).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(f => (f, File.GetLastWriteTimeUtc(f))).ToList();
        Scripts[path] = (files, animation);
        return animation;
    }

    private static string Answer(JsonElement id, Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            if (id.ValueKind != JsonValueKind.Undefined)
            {
                json.WritePropertyName("id");
                id.WriteTo(json);
            }
            write(json);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
