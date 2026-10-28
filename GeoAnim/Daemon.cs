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
/// "figures": {"key": {"animation": {…}, "from": 2 or null, "to": 3 or null, "still": false, "crop": true}, …}, "errors": […],
/// "warnings": […] (as for "compile")} – a page of a book in Markdown, its figures from the scripts beside the file (as
/// saved; compiled scripts are kept while their files stay the same).</item>
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
        MarkdownPage page = MarkdownPage.Parse(text, path, LoadScript);
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
            Problems("errors", page.Errors);
            Problems("warnings", page.Warnings);
        });
    }

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
