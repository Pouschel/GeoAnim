using System.Text;
using System.Text.RegularExpressions;

namespace GeoAnim.Script;

/// <summary>
/// The text of a script with its "include" lines replaced by the files they name (recursively, paths relative to the
/// including file). Remembers where every line comes from, so that errors name the right file and line.
/// </summary>
public sealed partial class ScriptSource
{
    private const int MaxDepth = 20;

    // for every line of Text: the file (null: the main script) and the line there
    private readonly List<(string? File, int Line)> origins = new();
    private readonly List<string> files = new();
    private readonly StringBuilder text = new();
    private readonly string baseDirectory;

    private ScriptSource(string? mainFile, string baseDirectory)
    {
        MainFile = mainFile;
        this.baseDirectory = baseDirectory;
    }

    /// <summary>The whole script, includes resolved.</summary>
    public string Text => text.ToString();

    /// <summary>The script file (full path), or null for a script given as text.</summary>
    public string? MainFile { get; }

    /// <summary>All files read: the script itself (if it is a file) and the included files (full paths).</summary>
    public IReadOnlyList<string> Files => files;

    /// <summary>Reads a script file and the files it includes; throws <see cref="ScriptError"/> for a missing or circular include.</summary>
    public static ScriptSource FromFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        var source = new ScriptSource(fullPath, Path.GetDirectoryName(fullPath)!);
        source.files.Add(fullPath);
        source.Add(File.ReadAllText(fullPath), null, Path.GetDirectoryName(fullPath)!, [fullPath]);
        return source;
    }

    /// <summary>A script given as text; includes are relative to <paramref name="baseDirectory"/> (default: the current directory).</summary>
    public static ScriptSource FromText(string script, string? baseDirectory = null)
    {
        string directory = Path.GetFullPath(baseDirectory ?? Environment.CurrentDirectory);
        var source = new ScriptSource(null, directory);
        source.Add(script, null, directory, []);
        return source;
    }

    [GeneratedRegex(@"^(\s*)include(?:[ \t]+(.*?))?\s*$")]
    private static partial Regex IncludeLine();

    private void Add(string script, string? file, string directory, List<string> chain)
    {
        string[] lines = script.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            Match include = IncludeLine().Match(lines[i]);
            if (!include.Success)
            {
                text.Append(lines[i].TrimEnd('\r')).Append('\n');
                origins.Add((file, i + 1));
                continue;
            }
            int column = include.Groups[1].Length + 1;
            string name = include.Groups[2].Value;
            if (name.Length == 0)
            {
                throw Error(file, i + 1, column, "\"include\" needs a file name, like \"include styles.geo\".");
            }
            string path = Path.GetFullPath(Path.Combine(directory, name));
            int nameColumn = include.Groups[2].Index + 1;
            if (chain.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                throw Error(file, i + 1, nameColumn,
                    $"{name} is already being included (circle: {string.Join(" → ", chain.Append(path).Select(Display))}).");
            }
            if (chain.Count >= MaxDepth)
            {
                throw Error(file, i + 1, nameColumn, $"Includes nested more than {MaxDepth} deep.");
            }
            if (!File.Exists(path))
            {
                throw Error(file, i + 1, nameColumn, $"File not found: {name} ({path}).");
            }
            if (!files.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(path);
            }
            Add(File.ReadAllText(path), path, Path.GetDirectoryName(path)!, [.. chain, path]);
        }
    }

    // a file for messages: relative to the main script's folder
    private string Display(string path) => Path.GetRelativePath(baseDirectory, path).Replace('\\', '/');

    private ScriptError Error(string? file, int line, int column, string message) =>
        new(line, column, message, file == null ? null : Display(file));

    /// <summary>An error with a line of <see cref="Text"/>, moved to the file and line it comes from.</summary>
    internal ScriptError Locate(ScriptError error)
    {
        if (error.File != null || error.Line < 1 || error.Line > origins.Count)
        {
            return error;
        }
        (string? file, int line) = origins[error.Line - 1];
        return file == null && line == error.Line ? error : new ScriptError(line, error.Column, error.Message, file == null ? null : Display(file));
    }
}
