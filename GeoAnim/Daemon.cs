using System.Text;
using System.Text.Json;
using GeoAnim.Script;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

/// <summary>
/// "geoanim daemon": compiles for an editor (the VS Code extension), one JSON request per line on standard input, one
/// JSON answer per line on standard output. The process stays, so MathJax starts only once.
/// <list type="bullet">
/// <item>{"id": 1, "type": "compile", "file": "C:\\…\\a.geo", "text": "…"} → {"id": 1, "animation": {…} or null,
/// "errors": [{"file": "C:\\…", "line": 3, "column": 7, "message": "…"}], "files": ["C:\\…", …], "definitions":
/// [{"name": "k", "kind": "var" | "set" | "object", "text": "…", "file": "C:\\…", "line": 4, "column": 5}, …]} – the
/// text as if it were the file (unsaved changes; includes and uses relative to its folder).</item>
/// <item>{"id": 2, "type": "player"} → {"id": 2, "html": "…"} – the player page for the preview.</item>
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
        try
        {
            source = Path.IsPathRooted(file) ? ScriptSource.FromFile(file, text) : ScriptSource.FromText(text);
            animation = ScriptCompiler.Compile(source).ToJson();
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
            json.WriteStartArray("files");
            foreach (string read in source?.Files ?? [])
            {
                json.WriteStringValue(read);
            }
            json.WriteEndArray();
            // for tooltips and "go to definition"
            json.WriteStartArray("definitions");
            foreach (ScriptDefinition definition in source == null ? [] : ScriptCompiler.Definitions(source))
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
        });
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
