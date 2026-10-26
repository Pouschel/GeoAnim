namespace GeoAnim.Script;

/// <summary>A statement of a script, at the position of its first word.</summary>
internal abstract record Statement(int Line, int Column);

internal sealed record TitleStatement(int Line, int Column, string Title) : Statement(Line, Column);

/// <summary>"description …": the page description of an exported file (meta description).</summary>
internal sealed record DescriptionStatement(int Line, int Column, string Description) : Statement(Line, Column);

/// <summary>"keyword Punkt/e/en Strecke/n": words that link to this page from the others (geoanim exportdir).</summary>
internal sealed record KeywordStatement(int Line, int Column, string Text) : Statement(Line, Column);

internal sealed record ViewStatement(int Line, int Column, ViewWindow View) : Statement(Line, Column);

internal sealed record BackgroundStatement(int Line, int Column, Color Color) : Statement(Line, Column);

internal sealed record GridStatement(int Line, int Column, double Step) : Statement(Line, Column);

internal sealed record AxesStatement(int Line, int Column) : Statement(Line, Column);

internal sealed record SpeedStatement(int Line, int Column, double Speed) : Statement(Line, Column);

/// <summary>"default point size 4": options for all later objects of the kinds (no kind: all kinds).</summary>
internal sealed record DefaultStatement(int Line, int Column, IReadOnlyList<string> Kinds, OptionList Options) : Statement(Line, Column);

internal sealed record StepStatement(int Line, int Column, string Caption) : Statement(Line, Column);

/// <summary>"info …": an explanation for the current step, below its description.</summary>
internal sealed record InfoStatement(int Line, int Column, string Text) : Statement(Line, Column);

/// <summary>A definition: point, segment, line, ray, circle, polygon, text.</summary>
internal sealed record DefineStatement(int Line, int Column, string Kind, IReadOnlyList<NameRef> Names, Definition Definition, OptionList Options)
    : Statement(Line, Column);

/// <summary>"input point A = -3 0": an input of the file when it is used; when it runs alone, an ordinary definition.</summary>
internal sealed record InputStatement(int Line, int Column, DefineStatement Define) : Statement(Line, Column);

/// <summary>
/// "output C" or "output point C = …" (with the definition of the result): the results of the file when it is used.
/// </summary>
internal sealed record OutputStatement(int Line, int Column, IReadOnlyList<NameRef> Names, DefineStatement? Define = null) : Statement(Line, Column);

/// <summary>"show A k" / "hide A k", or with <see cref="Label"/> "show label A B": only the labels.</summary>
internal sealed record ShowStatement(int Line, int Column, bool Show, IReadOnlyList<NameRef> Targets, OptionList Options, bool Label = false)
    : Statement(Line, Column)
{
    /// <summary>"hide all", "hide label all": everything visible (no targets).</summary>
    public bool All { get; init; }
}

/// <summary>
/// "move P to x y" (Target), "move P to 90" (Parameter: angle or position on a path) or "move P to Q" (TargetPoint: where
/// Q is at that moment).
/// </summary>
/// <summary>"number l = length s": a number measured from objects (length, degrees, area, radius, perimeter).</summary>
/// <summary>
/// "number l = length A B" (a measure of objects) or "number h = a * sqrt(3) / 2" (<see cref="Expression"/>: computed
/// from other numbers; <paramref name="Measure"/> is then empty).
/// </summary>
internal sealed record NumberStatement(int Line, int Column, NameRef Name, string Measure, IReadOnlyList<NameRef> Arguments)
    : Statement(Line, Column)
{
    public Expression? Expression { get; init; }
}

/// <summary>
/// "move label A B to 0.2 -0.3" (<paramref name="Shift"/>) or "move label A to SE gap 12" (<see cref="Direction"/>, <see cref="Gap"/>).
/// </summary>
internal sealed record LabelShiftStatement(int Line, int Column, IReadOnlyList<NameRef> Targets, Vec? Shift, OptionList Options)
    : Statement(Line, Column)
{
    public double? Direction { get; init; }

    public double? Gap { get; init; }
}

/// <summary>"move A B C along P Q": the objects moved by the vector PQ.</summary>
internal sealed record TranslateStatement(int Line, int Column, IReadOnlyList<NameRef> Targets, NameRef From, NameRef To, OptionList Options)
    : Statement(Line, Column);

/// <summary>"rotate A B C around M by 60": the objects turned around M (degrees, counterclockwise).</summary>
internal sealed record RotateStatement(int Line, int Column, IReadOnlyList<NameRef> Targets, NameRef Center, double Degrees, OptionList Options)
    : Statement(Line, Column);

/// <summary>
/// "reflect A B C across g" / "across P Q" (in a line: <paramref name="Second"/> is the second point, if given) or
/// "reflect A B C in Z" (<paramref name="InPoint"/>).
/// </summary>
internal sealed record ReflectStatement(int Line, int Column, IReadOnlyList<NameRef> Targets, NameRef Axis, NameRef? Second, bool InPoint, OptionList Options)
    : Statement(Line, Column);

/// <summary>"highlight A B [keep]" or "unhighlight A B" (<paramref name="Off"/>).</summary>
internal sealed record HighlightStatement(int Line, int Column, bool Off, IReadOnlyList<NameRef> Targets, OptionList Options)
    : Statement(Line, Column);

internal sealed record MoveStatement(int Line, int Column, NameRef Point, Vec? Target, double? Parameter, OptionList Options, NameRef? TargetPoint = null)
    : Statement(Line, Column);

internal sealed record PauseStatement(int Line, int Column, double Duration) : Statement(Line, Column);

internal sealed record TogetherStatement(int Line, int Column, IReadOnlyList<Statement> Body, OptionList Options) : Statement(Line, Column);

/// <summary>A name in the script with its position (for messages about it).</summary>
internal sealed record NameRef(string Name, int Line, int Column);

/// <summary>How an object is defined (the part after "=").</summary>
internal abstract record Definition;

internal sealed record CoordinatesDefinition(Vec Position) : Definition;

internal sealed record MidpointDefinition(NameRef A, NameRef B) : Definition;

/// <summary>"copy P": a free point where P is at that moment.</summary>
internal sealed record CopyDefinition(NameRef Source) : Definition;

internal sealed record OnDefinition(NameRef Path, double Parameter) : Definition;

internal sealed record IntersectDefinition(NameRef A, NameRef B) : Definition;

internal sealed record TwoPointsDefinition(NameRef A, NameRef B) : Definition;

internal sealed record CircleThroughDefinition(NameRef Center, NameRef Through) : Definition;

internal sealed record CircleRadiusDefinition(NameRef Center, double Radius) : Definition;

/// <summary>"arc b = k from A to B" / "sector s = k from A to B".</summary>
internal sealed record ArcDefinition(NameRef Circle, NameRef From, NameRef To) : Definition;

internal sealed record PolygonDefinition(IReadOnlyList<NameRef> Vertices) : Definition;

/// <summary>"angle w = A B C": at B, counterclockwise from BA to BC.</summary>
internal sealed record AngleDefinition(NameRef A, NameRef Vertex, NameRef C) : Definition;

/// <summary>How a used file appears: only its results, its construction drawn in the current step, or its own steps.</summary>
internal enum UseMode
{
    Result,
    Draw,
    Steps,

    /// <summary>"use file A B" without results: like Draw, but with the file's pauses (the file is the animation).</summary>
    Play,
}

/// <summary>"use Prop1.geo A B": the outputs of another file, constructed from these inputs.</summary>
internal sealed record UseDefinition(string Path, int PathLine, int PathColumn, IReadOnlyList<NameRef> Arguments, UseMode Mode) : Definition;

internal sealed record TextDefinition(string Content, Vec? Position, NameRef? Anchor) : Definition
{
    /// <summary>"below t [gap n]": the text one line below the text t.</summary>
    public NameRef? Above { get; init; }

    public double Gap { get; init; }
}

/// <summary>The options of a statement ("color red duration 800"), in their order.</summary>
internal sealed record OptionList(IReadOnlyList<OptionValue> Values)
{
    public static OptionList Empty { get; } = new([]);
}

/// <summary>An option; <paramref name="Var"/> names the var it came from (such options are skipped where they do not fit).</summary>
internal sealed record OptionValue(string Name, object? Value, int Line, int Column, string? Var = null);

/// <summary>
/// A name a script defines, for an editor (tooltip, go to definition): a var, a text ("set") or an object; its text (the
/// line of the definition, for a text the text itself) and where it is (<see cref="File"/>: full path, null for a script
/// given as text; line and column of the name).
/// </summary>
public sealed record ScriptDefinition(string Name, string Kind, string Text, string? File, int Line, int Column);

/// <summary>
/// A place where a name stands in a script (where it is defined and where it is used: in statements, as a var among the
/// options, as a text from "set", in a placeholder "{l}"), for an editor (renaming, references).
/// </summary>
public sealed record ScriptReference(string Name, string? File, int Line, int Column);
