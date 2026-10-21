namespace GeoAnim.Script;

/// <summary>
/// A mistake in a script, with its position (line and column start at 1) and, for a mistake in an included file, that
/// file (relative to the script).
/// </summary>
public sealed class ScriptError(int line, int column, string message, string? file = null) : Exception(message)
{
    public int Line { get; } = line;

    public int Column { get; } = column;

    /// <summary>The included file with the mistake, or null for the script itself.</summary>
    public string? File { get; } = file;

    /// <summary>"Line 3, column 7: message" or "styles.geo, line 3, column 7: message".</summary>
    public override string ToString() => (File == null ? "Line" : File + ", line") + $" {Line}, column {Column}: {Message}";
}
