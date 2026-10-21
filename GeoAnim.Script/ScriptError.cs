namespace GeoAnim.Script;

/// <summary>A mistake in a script, with its position (line and column start at 1).</summary>
public sealed class ScriptError(int line, int column, string message) : Exception(message)
{
    public int Line { get; } = line;

    public int Column { get; } = column;

    /// <summary>"Line 3, column 7: message".</summary>
    public override string ToString() => $"Line {Line}, column {Column}: {Message}";
}
