using System.Text;
using System.Text.Json;
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
/// the book: completion and "go to definition" for "§name"), "errors": […], "warnings": […] (as for "compile")} – a page
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
        (List<BookMark> others, int axiomsBefore) = path == null ? ([], 0) : OtherPagesMarks(path, text);
        var bookMarks = new Dictionary<string, BookMark>(StringComparer.Ordinal);
        foreach (BookMark other in others)
        {
            bookMarks.TryAdd(other.Mark.Name, other);
        }
        MarkdownPage page = MarkdownPage.Parse(text, path, LoadScript, bookMarks, axiomsBefore);
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
            Problems("errors", page.Errors);
            Problems("warnings", page.Warnings);
        });
    }

    // the marks of the other pages of the book (the pages "*.geo.md" below the folder with index.yaml, else beside the
    // page; not in folders or files starting with "_"), as saved, read again when a page has changed; and the number of
    // axioms on the pages before this one in book order (axioms are counted through the book)
    private static readonly Dictionary<string, (DateTime Written, MarksOfPage Marks)> PageMarks =
        new(StringComparer.OrdinalIgnoreCase);

    private static (List<BookMark> Marks, int AxiomsBefore) OtherPagesMarks(string page, string text)
    {
        string root = BookKeywords.RootOf(page) ?? Path.GetDirectoryName(page)!;
        var found = new List<BookMark>();
        List<string> pages;
        try
        {
            pages = Directory.EnumerateFiles(root, "*.geo.md", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Where(relative => !relative.Split('/').Any(part => part.StartsWith('_')))
                .ToList();
        }
        catch (IOException)
        {
            return (found, 0);
        }
        string own = Path.GetRelativePath(root, page).Replace('\\', '/');
        if (!pages.Contains(own, StringComparer.OrdinalIgnoreCase))
        {
            pages.Add(own);
        }
        pages.Sort(Program.BookOrder.Instance);
        int axioms = 0, axiomsBefore = 0;
        foreach (string relative in pages)
        {
            string other = Path.GetFullPath(Path.Combine(root, relative));
            if (string.Equals(other, page, StringComparison.OrdinalIgnoreCase))
            {
                // this page as it is in the editor
                axiomsBefore = axioms;
                axioms += MarkdownPage.ReadMarks(text).Axioms;
                continue;
            }
            DateTime written = File.GetLastWriteTimeUtc(other);
            if (!PageMarks.TryGetValue(other, out var known) || known.Written != written)
            {
                try
                {
                    known = (written, MarkdownPage.ReadMarks(File.ReadAllText(other)));
                }
                catch (IOException)
                {
                    continue;
                }
                PageMarks[other] = known;
            }
            int before = axioms;
            found.AddRange(known.Marks.Marks.Select(m => new BookMark(other, MarkdownPage.AfterAxioms(m, before))));
            axioms += known.Marks.Axioms;
        }
        return (found, axiomsBefore);
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
    private static AnimationData LoadScript(string path)
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
