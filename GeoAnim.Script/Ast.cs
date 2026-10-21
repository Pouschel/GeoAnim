namespace GeoAnim.Script;

/// <summary>A statement of a script, at the position of its first word.</summary>
internal abstract record Statement(int Line, int Column);

internal sealed record TitleStatement(int Line, int Column, string Title) : Statement(Line, Column);

internal sealed record ViewStatement(int Line, int Column, ViewWindow View) : Statement(Line, Column);

internal sealed record BackgroundStatement(int Line, int Column, Color Color) : Statement(Line, Column);

internal sealed record GridStatement(int Line, int Column, double Step) : Statement(Line, Column);

internal sealed record AxesStatement(int Line, int Column) : Statement(Line, Column);

internal sealed record SpeedStatement(int Line, int Column, double Speed) : Statement(Line, Column);

/// <summary>"default point size 4": options for all later objects of the kinds (no kind: all kinds).</summary>
internal sealed record DefaultStatement(int Line, int Column, IReadOnlyList<string> Kinds, OptionList Options) : Statement(Line, Column);

internal sealed record StepStatement(int Line, int Column, string Caption) : Statement(Line, Column);

/// <summary>A definition: point, segment, line, ray, circle, polygon, text.</summary>
internal sealed record DefineStatement(int Line, int Column, string Kind, IReadOnlyList<NameRef> Names, Definition Definition, OptionList Options)
    : Statement(Line, Column);

/// <summary>"input point A = -3 0": an input of the file when it is used; when it runs alone, an ordinary definition.</summary>
internal sealed record InputStatement(int Line, int Column, DefineStatement Define) : Statement(Line, Column);

/// <summary>"output C": the results of the file when it is used.</summary>
internal sealed record OutputStatement(int Line, int Column, IReadOnlyList<NameRef> Names) : Statement(Line, Column);

internal sealed record ShowStatement(int Line, int Column, bool Show, IReadOnlyList<NameRef> Targets, OptionList Options) : Statement(Line, Column);

/// <summary>"move P to (x, y)" (Target) or "move P to 90" (Parameter: angle or position on a path).</summary>
internal sealed record MoveStatement(int Line, int Column, NameRef Point, Vec? Target, double? Parameter, OptionList Options) : Statement(Line, Column);

internal sealed record PauseStatement(int Line, int Column, double Duration) : Statement(Line, Column);

internal sealed record TogetherStatement(int Line, int Column, IReadOnlyList<Statement> Body) : Statement(Line, Column);

/// <summary>A name in the script with its position (for messages about it).</summary>
internal sealed record NameRef(string Name, int Line, int Column);

/// <summary>How an object is defined (the part after "=").</summary>
internal abstract record Definition;

internal sealed record CoordinatesDefinition(Vec Position) : Definition;

internal sealed record MidpointDefinition(NameRef A, NameRef B) : Definition;

internal sealed record OnDefinition(NameRef Path, double Parameter) : Definition;

internal sealed record IntersectDefinition(NameRef A, NameRef B) : Definition;

internal sealed record TwoPointsDefinition(NameRef A, NameRef B) : Definition;

internal sealed record CircleThroughDefinition(NameRef Center, NameRef Through) : Definition;

internal sealed record CircleRadiusDefinition(NameRef Center, double Radius) : Definition;

internal sealed record PolygonDefinition(IReadOnlyList<NameRef> Vertices) : Definition;

/// <summary>How a used file appears: only its results, its construction drawn in the current step, or its own steps.</summary>
internal enum UseMode
{
    Result,
    Draw,
    Steps,
}

/// <summary>"use Prop1.geo A B": the outputs of another file, constructed from these inputs.</summary>
internal sealed record UseDefinition(string Path, int PathLine, int PathColumn, IReadOnlyList<NameRef> Arguments, UseMode Mode) : Definition;

internal sealed record TextDefinition(string Content, Vec? Position, NameRef? Anchor) : Definition;

/// <summary>The options of a statement ("color red duration 800"), in their order.</summary>
internal sealed record OptionList(IReadOnlyList<OptionValue> Values)
{
    public static OptionList Empty { get; } = new([]);
}

/// <summary>An option; <paramref name="Var"/> names the var it came from (such options are skipped where they do not fit).</summary>
internal sealed record OptionValue(string Name, object? Value, int Line, int Column, string? Var = null);
