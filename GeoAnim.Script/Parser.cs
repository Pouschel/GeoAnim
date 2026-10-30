using System.Text.RegularExpressions;

namespace GeoAnim.Script;

/// <summary>Reads the statements of a script (one per line; "together" … "end" spans several lines).</summary>
internal sealed partial class Parser
{
    /// <summary>Words that cannot be names of objects.</summary>
    internal static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "title", "description", "info", "keyword", "view", "background", "grid", "axes", "speed", "print", "var", "default", "include", "input", "output", "use",
        "draw", "steps", "copy", "set", "step", "textstep", "point", "segment", "line", "ray", "circle", "arc", "sector", "polygon", "angle",
        "text", "number", "length", "degrees", "area", "perimeter", "show", "hide", "all", "highlight", "unhighlight", "keep", "move", "rotate", "reflect", "along", "around", "across", "in", "by", "pause", "together", "end", "midpoint", "intersect", "parallel", "perpendicular", "center", "through",
        "radius", "at", "from", "to", "on", "x", "y", "below", "gap", "align", "left", "right",
        "duration", "color", "width", "fill", "opacity", "size", "labelsize", "labelpos", "labeldir", "labelgap", "label", "nolabel", "hidden", "offset", "shift",
        "arrow", "start", "both", "straight", "back", "front", "halo", "clockwise", "linear", "ease", "easein", "easeout", "easeinout",
    };

    private static readonly HashSet<string> OptionNames = new(StringComparer.Ordinal)
    {
        "duration", "color", "width", "fill", "opacity", "size", "radius", "labelsize", "labelpos", "labeldir", "labelgap", "label", "nolabel", "hidden", "offset", "shift",
        "arrow", "align", "straight", "back", "front", "keep", "halo", "clockwise", "linear", "ease", "easein", "easeout", "easeinout",
    };

    /// <summary>The kinds a "default" can name.</summary>
    private static readonly HashSet<string> DefaultKinds = new(StringComparer.Ordinal)
    {
        "point", "segment", "line", "ray", "circle", "arc", "sector", "polygon", "angle", "text", "show", "hide", "highlight", "unhighlight", "move", "rotate", "reflect",
    };

    private readonly List<Token> tokens;
    private int position;

    // "var name options…": named groups of options, valid from their line on
    private readonly Dictionary<string, List<OptionValue>> vars = new(StringComparer.Ordinal);

    // "set name = text": named texts for "text" and "label", valid from their line on
    private readonly Dictionary<string, string> texts = new(StringComparer.Ordinal);

    // names given to objects so far (a var cannot take one of them, and the other way round)
    private readonly HashSet<string> objectNames = new(StringComparer.Ordinal);

    // numbers ("number l = …") and the placeholders in texts ("{l}"): every placeholder must name a number of the script
    private readonly HashSet<string> numberNames = new(StringComparer.Ordinal);

    // the numbers with a fixed value ("number tx = 5.2", "number ty = tx / 2"): usable wherever a number is expected
    private readonly Dictionary<string, double> constants = new(StringComparer.Ordinal);
    private readonly List<(string Name, int Line, int Column)> placeholders = new();

    // for an editor: the names defined so far (lines of the source), every place a name stands (also where it is
    // defined; for renaming), and the source's lines for their text
    private readonly List<ScriptDefinition>? definitions;
    private readonly List<ScriptReference>? references;
    private readonly string[] lines;

    private Parser(List<Token> tokens, string source, List<ScriptDefinition>? definitions, List<ScriptReference>? references)
    {
        this.tokens = tokens;
        this.definitions = definitions;
        this.references = references;
        lines = definitions == null ? [] : source.Split('\n');
    }

    public static List<Statement> Parse(string source) => Parse(source, null);

    /// <summary>Parses; with <paramref name="definitions"/> also collects the names defined (as far as it gets).</summary>
    public static List<Statement> Parse(string source, List<ScriptDefinition>? definitions, List<ScriptReference>? references = null)
    {
        var parser = new Parser(Lexer.Tokenize(source), source, definitions, references);
        List<Statement> statements = parser.ParseBlock(null, out _);
        // a placeholder may come before its number (a step that names a number measured in it)
        foreach ((string name, int line, int column) in parser.placeholders)
        {
            if (!parser.numberNames.Contains(name))
            {
                throw new ScriptError(line, column, $"{{{name}}} names no number (define it with \"number {name} = length …\"; \\{{ is a brace).");
            }
        }
        return statements;
    }

    // a name defined: its line as the text, or the text given
    private void Defined(string kind, NameRef name, string? text = null)
    {
        if (definitions != null)
        {
            text ??= name.Line >= 1 && name.Line <= lines.Length ? lines[name.Line - 1].Trim() : name.Name;
            definitions.Add(new ScriptDefinition(name.Name, kind, text, null, name.Line, name.Column));
        }
    }

    private Token Current => tokens[position];

    private Token Next() => tokens[position++];

    private List<Statement> ParseBlock(Token? together, out Token end)
    {
        bool inTogether = together != null;
        var statements = new List<Statement>();
        while (true)
        {
            while (Current.Kind == TokenKind.NewLine)
            {
                position++;
            }
            if (Current.Kind == TokenKind.End)
            {
                if (inTogether)
                {
                    throw Error(together!.Value, "The \"together\" block has no \"end\".");
                }
                end = Current;
                return statements;
            }
            if (Current.Is("end") && Current.Kind == TokenKind.Name)
            {
                if (!inTogether)
                {
                    throw Error(Current, "\"end\" without \"together\".");
                }
                end = Next();
                ExpectLineEnd();
                return statements;
            }
            Statement? statement = ParseStatement(inTogether);
            if (statement != null)
            {
                statements.Add(statement);
            }
        }
    }

    /// <summary>The next statement, or null for a line that only defines a var.</summary>
    private Statement? ParseStatement(bool inTogether)
    {
        Token word = Next();
        if (word.Kind != TokenKind.Name)
        {
            throw Error(word, $"A line must start with a command (like point, segment, step), not {word.Describe()}.");
        }
        int l = word.Line;
        int c = word.Column;
        Statement statement;
        switch (word.Text)
        {
            case "title":
                statement = new TitleStatement(l, c, TextOf(Next()));
                break;
            case "description":
                statement = new DescriptionStatement(l, c, TextOf(Next()));
                break;
            case "info":
                statement = new InfoStatement(l, c, TextOf(Next()));
                break;
            case "keyword":
                statement = new KeywordStatement(l, c, Next().Text);
                break;
            case "view":
                statement = ParseView(word);
                break;
            case "background":
                statement = new BackgroundStatement(l, c, ExpectColor());
                break;
            case "grid":
                statement = new GridStatement(l, c, IsNumberAt(position) ? ExpectPositive("the grid distance") : 1);
                break;
            case "axes":
                {
                    // "axes size 18 color gray": size of the numbers, colour of axes and numbers
                    double? size = null;
                    Color? color = null;
                    while (Current.Kind == TokenKind.Name && Current.Text is "size" or "color")
                    {
                        string option = Next().Text;
                        if (option == "size")
                        {
                            size = ExpectPositive("the size of the axes' numbers");
                        }
                        else
                        {
                            color = ExpectColor();
                        }
                    }
                    statement = new AxesStatement(l, c, size, color);
                }
                break;
            case "speed":
                statement = new SpeedStatement(l, c, ExpectPositive("the speed"));
                break;
            case "print":
                {
                    // "print scale 6mm", "print width 12cm": the size of the figures in the book (mm, cm, pt, in; mm without one)
                    Token what = Current;
                    if (what.Kind != TokenKind.Name || what.Text is not ("scale" or "width"))
                    {
                        throw Error(what, "\"print\" needs \"scale\" (a unit's length, print scale 6mm) or \"width\" (the view's width, print width 12cm).");
                    }
                    Next();
                    double length = ExpectPositive(what.Text == "scale" ? "the length of a unit" : "the width");
                    string unit = Current.Kind == TokenKind.Name && Current.Text is "mm" or "cm" or "pt" or "in" ? Next().Text : "mm";
                    double points = unit switch
                    {
                        "cm" => length * 720 / 25.4,
                        "pt" => length,
                        "in" => length * 72,
                        _ => length * 72 / 25.4,
                    };
                    statement = new PrintStatement(l, c, what.Text == "width", points);
                }
                break;
            case "var":
                ParseVar();
                ExpectLineEnd();
                return null;
            case "set":
                {
                    Token nameToken = Current;
                    NameRef name = ExpectName();
                    if (objectNames.Contains(name.Name) || vars.ContainsKey(name.Name))
                    {
                        throw Error(nameToken, $"{name.Name} is already the name of {(vars.ContainsKey(name.Name) ? "a var" : "an object")}.");
                    }
                    ExpectSymbol("=");
                    texts[name.Name] = TextOf(Next());
                    Defined("set", name, "set " + name.Name + " = " + texts[name.Name]);
                    ExpectLineEnd();
                    return null;
                }
            case "default":
                statement = ParseDefault(word);
                break;
            case "use":
                {
                    // "use file A B": no results, only the animation of the file (with its pauses; "draw" without them)
                    position--;
                    UseDefinition use = TryParseUse()!;
                    if (use.Mode == UseMode.Result)
                    {
                        use = use with { Mode = UseMode.Play };
                    }
                    if (Current.Kind == TokenKind.Name && IsOption(Current.Text))
                    {
                        throw Error(Current, "A \"use\" without results takes no options (they are for the results: \"point D = use … color red\").");
                    }
                    statement = new DefineStatement(l, c, "", [], use, OptionList.Empty);
                    break;
                }
            case "input":
                statement = ParseInput(word);
                break;
            case "output":
                {
                    // with a definition: "output point C = …", "output D s = use …"
                    DefineStatement? define = Current.Kind == TokenKind.Name ? Current.Text switch
                    {
                        "point" => ParsePoint(Next()),
                        "segment" or "line" or "ray" => ParseStraight(Next()),
                        "circle" => ParseCircle(Next()),
                        "arc" or "sector" => ParseArc(Next()),
                        "polygon" => ParsePolygon(Next()),
                        "angle" => ParseAngle(Next()),
                        "text" => ParseText(Next()),
                        _ => null,
                    } : null;
                    if (define == null && Current.Kind == TokenKind.Name)
                    {
                        define = ParseUseWithoutKind(Next());
                        if (define == null)
                        {
                            position--;
                        }
                    }
                    if (define != null)
                    {
                        statement = new OutputStatement(l, c, define.Names.Where(n => n.Name != "_").ToList(), define);
                        break;
                    }
                    // the names of results defined before
                    var names = new List<NameRef>();
                    while (Current.Kind == TokenKind.Name)
                    {
                        names.Add(ExpectName());
                    }
                    if (names.Count == 0)
                    {
                        throw Error(Current, "\"output\" needs a definition or the names of the results, like \"output point C = …\" or \"output C\".");
                    }
                    statement = new OutputStatement(l, c, names);
                    break;
                }
            case "step":
            case "textstep":
                if (inTogether)
                {
                    throw Error(word, "A step cannot begin inside a \"together\" block.");
                }
                {
                    // "step Gleiche Punkte #gleich": the mark at the end of the text (also after "|" lines), not part of it
                    string text = TextOf(Next());
                    int lineEnd = text.IndexOf('\n') < 0 ? text.Length : text.IndexOf('\n');
                    Match mark = StepMark().Match(text.Substring(0, lineEnd));
                    if (mark.Success)
                    {
                        text = text.Substring(0, mark.Index).TrimEnd() + text.Substring(lineEnd);
                    }
                    statement = new StepStatement(l, c, text) { TextOnly = word.Text == "textstep", Mark = mark.Success ? mark.Groups[1].Value : null };
                }
                break;
            case "point":
                statement = ParsePoint(word);
                break;
            case "segment":
            case "line":
            case "ray":
                statement = ParseStraight(word);
                break;
            case "circle":
                statement = ParseCircle(word);
                break;
            case "polygon":
                statement = ParsePolygon(word);
                break;
            case "arc":
            case "sector":
                statement = ParseArc(word);
                break;
            case "angle":
                statement = ParseAngle(word);
                break;
            case "text":
                statement = ParseText(word);
                break;
            case "show":
            case "hide":
                statement = ParseShow(word);
                break;
            case "highlight":
            case "unhighlight":
                statement = ParseHighlight(word);
                break;
            case "move":
                statement = ParseMove(word);
                break;
            case "rotate":
                statement = ParseRotate(word);
                break;
            case "reflect":
                statement = ParseReflect(word);
                break;
            case "number":
                statement = ParseNumber(word);
                break;
            case "pause":
                statement = new PauseStatement(l, c, IsNumberAt(position) ? ExpectNonNegative("the pause") : 500);
                break;
            case "together":
                if (inTogether)
                {
                    throw Error(word, "\"together\" blocks cannot be nested.");
                }
                OptionList togetherOptions = ParseOptions();
                ExpectLineEnd();
                List<Statement> body = ParseBlock(word, out _);
                return new TogetherStatement(l, c, body, togetherOptions);
            default:
                statement = ParseUseWithoutKind(word) ?? throw Error(word, $"Unknown command \"{word.Text}\".");
                break;
        }
        ExpectLineEnd();
        return statement;
    }

    private InputStatement ParseInput(Token word)
    {
        Token kind = Next();
        DefineStatement define = kind.Kind == TokenKind.Name ? kind.Text switch
        {
            "point" => ParsePoint(kind),
            "segment" or "line" or "ray" => ParseStraight(kind),
            "circle" => ParseCircle(kind),
            _ => throw Error(kind, $"An input is a point, segment, line, ray or circle, not \"{kind.Text}\"."),
        } : throw Error(kind, $"Expected the kind of the input (like \"input point A = 0 0\"), not {kind.Describe()}.");
        if (define.Names.Count > 1 || define.Definition is UseDefinition)
        {
            throw Error(kind, "An input is one object with a value for running the file alone, like \"input point A = 0 0\".");
        }
        return new InputStatement(word.Line, word.Column, define);
    }

    // "D s = use file A B": the outputs of the file in their order, whatever their kinds ("_" skips one)
    private DefineStatement? ParseUseWithoutKind(Token word)
    {
        int start = position - 1;
        int look = start;
        while (tokens[look].Kind == TokenKind.Name)
        {
            look++;
        }
        if (!(tokens[look].Kind == TokenKind.Symbol && tokens[look].Text == "=" && tokens[look + 1].Is("use")))
        {
            return null;
        }
        position = start;
        var names = new List<NameRef>();
        while (Current.Kind == TokenKind.Name)
        {
            names.Add(Current.Text == "_" ? new NameRef("_", Current.Line, Current.Column) : null!);
            if (Current.Text == "_")
            {
                position++;
            }
            else
            {
                names[^1] = ExpectNewName();
            }
        }
        ExpectSymbol("=");
        UseDefinition use = TryParseUse()!;
        return new DefineStatement(word.Line, word.Column, "", names, use, ParseOptions());
    }

    // after "=": "use file A B [draw|steps]"
    private UseDefinition? TryParseUse()
    {
        if (!(Current.Is("use") && Current.Kind == TokenKind.Name))
        {
            return null;
        }
        Token use = Next();
        Token path = Current;
        if (path.Kind != TokenKind.String)
        {
            throw Error(use, "\"use\" needs a file, like \"point C = use Prop1.geo A B\".");
        }
        position++;
        var arguments = new List<NameRef>();
        while (Current.Kind == TokenKind.Name && !IsOption(Current.Text) && Current.Text is not ("draw" or "steps"))
        {
            arguments.Add(ExpectName());
        }
        UseMode mode = UseMode.Result;
        if (Current.Is("draw") || Current.Is("steps"))
        {
            mode = Next().Text == "draw" ? UseMode.Draw : UseMode.Steps;
        }
        return new UseDefinition(path.Text, path.Line, path.Column, arguments, mode);
    }

    private DefaultStatement ParseDefault(Token word)
    {
        var kinds = new List<string>();
        while (Current.Kind == TokenKind.Name && DefaultKinds.Contains(Current.Text))
        {
            kinds.Add(Next().Text);
        }
        if (Current.Kind == TokenKind.Name && !IsOption(Current.Text))
        {
            throw Error(Current, $"\"{Current.Text}\" is neither a kind of object ({string.Join(", ", DefaultKinds)}) nor an option.");
        }
        OptionList options = ParseOptions();
        if (options.Values.Count == 0)
        {
            throw Error(Current, "\"default\" needs options, like \"default point size 4\" or \"default width 2\".");
        }
        return new DefaultStatement(word.Line, word.Column, kinds, options);
    }

    private void ParseVar()
    {
        Token token = Current;
        NameRef name = ExpectName();
        if (objectNames.Contains(name.Name) || texts.ContainsKey(name.Name))
        {
            throw Error(token, $"{name.Name} is already the name of {(texts.ContainsKey(name.Name) ? "a text (\"set\")" : "an object")}.");
        }
        if (Current.Kind == TokenKind.Name && !OptionNames.Contains(Current.Text) && !vars.ContainsKey(Current.Text))
        {
            throw Error(Current, $"Unknown option \"{Current.Text}\" (a var holds options like \"color red width 3\").");
        }
        List<OptionValue> values = ParseOptions().Values.Select(v => v with { Var = v.Var ?? name.Name }).ToList();
        if (values.Count == 0)
        {
            throw Error(Current, $"The var {name.Name} needs options, like \"var {name.Name} color red width 3\".");
        }
        vars[name.Name] = values;
        Defined("var", name);
    }

    private ViewStatement ParseView(Token word)
    {
        ExpectWord("x");
        (double xMin, double xMax) = ParseRange();
        ExpectWord("y");
        (double yMin, double yMax) = ParseRange();
        var view = new ViewWindow(xMin, xMax, yMin, yMax);
        try
        {
            view.Validate();
        }
        catch (GeoException e)
        {
            throw Error(word, e.Message);
        }
        return new ViewStatement(word.Line, word.Column, view);
    }

    private (double, double) ParseRange()
    {
        double from = ExpectNumber("the start of the range");
        ExpectSymbol("..");
        double to = ExpectNumber("the end of the range");
        return (from, to);
    }

    private DefineStatement ParsePoint(Token word)
    {
        // several names only for intersections: "point S T = intersect k1 k2"
        var names = new List<NameRef> { ExpectNewName() };
        while (Current.Kind == TokenKind.Name)
        {
            names.Add(ExpectNewName());
        }
        ExpectSymbol("=");
        Definition definition;
        if (TryParseUse() is { } use)
        {
            definition = use;
        }
        else if (IsNumberAt(position))
        {
            definition = new CoordinatesDefinition(ParseCoordinates());
        }
        else if (Current.Is("copy"))
        {
            position++;
            definition = new CopyDefinition(ExpectName());
        }
        else if (Current.Is("midpoint"))
        {
            position++;
            definition = new MidpointDefinition(ExpectName(), ExpectName());
        }
        else if (Current.Is("parallel") || Current.Is("perpendicular"))
        {
            definition = ParseParallel();
        }
        else if (Current.Is("intersect"))
        {
            position++;
            definition = new IntersectDefinition(ExpectName(), ExpectName());
        }
        else if (Current.Is("on"))
        {
            position++;
            NameRef path = ExpectName();
            ExpectWord("at");
            definition = new OnDefinition(path, ExpectNumber("the angle (on a circle) or the position (on a straight)"));
        }
        else
        {
            if (Current.Kind == TokenKind.Name && numberNames.Contains(Current.Text))
            {
                // a measured number as a coordinate: the message why not
                ExpectNumber("a coordinate");
            }
            throw Error(Current, $"Expected coordinates like 2 3, \"midpoint A B\", \"intersect k1 k2\", \"on k at 30\", \"copy P\" or \"use file …\" after \"=\", not {Current.Describe()}.");
        }
        if (names.Count > 1 && definition is not (IntersectDefinition or UseDefinition))
        {
            throw Error(names[1], "Several names are only possible for intersections and \"use\".");
        }
        if (names.Count > 2 && definition is IntersectDefinition)
        {
            throw Error(names[2], "Two objects have at most 2 intersections.");
        }
        return new DefineStatement(word.Line, word.Column, "point", names, definition, ParseOptions());
    }

    // "parallel g through C", "perpendicular g through C"
    private ParallelDefinition ParseParallel()
    {
        bool perpendicular = Next().Text == "perpendicular";
        NameRef straight = ExpectName();
        ExpectWord("through");
        return new ParallelDefinition(straight, ExpectName(), perpendicular);
    }

    private DefineStatement ParseStraight(Token word)
    {
        NameRef first = ExpectNewName();
        NameRef name;
        NameRef a;
        NameRef b;
        if (Current.Is("="))
        {
            position++;
            name = first;
            if (TryParseUse() is { } use)
            {
                return new DefineStatement(word.Line, word.Column, word.Text, [name], use, ParseOptions());
            }
            if (Current.Is("parallel") || Current.Is("perpendicular"))
            {
                return new DefineStatement(word.Line, word.Column, word.Text, [name], ParseParallel(), ParseOptions());
            }
            a = ExpectName();
            b = ExpectName();
        }
        else
        {
            // short form: "segment A B" is called AB
            a = first;
            b = ExpectName();
            name = new NameRef(a.Name + b.Name, a.Line, a.Column);
            CheckNewName(name);
        }
        return new DefineStatement(word.Line, word.Column, word.Text, [name], new TwoPointsDefinition(a, b), ParseOptions());
    }

    private DefineStatement ParseCircle(Token word)
    {
        NameRef name = ExpectNewName();
        ExpectSymbol("=");
        if (TryParseUse() is { } use)
        {
            return new DefineStatement(word.Line, word.Column, "circle", [name], use, ParseOptions());
        }
        ExpectWord("center");
        NameRef center = ExpectName();
        Definition definition;
        if (Current.Is("through"))
        {
            position++;
            definition = new CircleThroughDefinition(center, ExpectName());
        }
        else if (Current.Is("radius"))
        {
            position++;
            if (IsNumberAt(position))
            {
                definition = new CircleRadiusDefinition(center, ExpectPositive("the radius"));
            }
            else
            {
                // the compass takes a length: "radius A B" (the distance of two points) or "radius l" (a number)
                NameRef first = ExpectName();
                NameRef? second = Current.Kind == TokenKind.Name && !IsOption(Current.Text) ? ExpectName() : null;
                definition = new CircleRadiusOfDefinition(center, first, second);
            }
        }
        else
        {
            throw Error(Current, $"Expected \"through <point>\" or \"radius <number>\", \"radius A B\", \"radius l\", not {Current.Describe()}.");
        }
        return new DefineStatement(word.Line, word.Column, "circle", [name], definition, ParseOptions());
    }

    private DefineStatement ParsePolygon(Token word)
    {
        NameRef name = ExpectNewName();
        ExpectSymbol("=");
        if (TryParseUse() is { } use)
        {
            return new DefineStatement(word.Line, word.Column, "polygon", [name], use, ParseOptions());
        }
        var vertices = new List<NameRef>();
        while (Current.Kind == TokenKind.Name && !IsOption(Current.Text))
        {
            vertices.Add(ExpectName());
        }
        if (vertices.Count < 3)
        {
            throw Error(Current, "A polygon needs at least 3 points.");
        }
        return new DefineStatement(word.Line, word.Column, "polygon", [name], new PolygonDefinition(vertices), ParseOptions());
    }

    // "arc b = k from A to B", "sector s = k from A to B"
    private DefineStatement ParseArc(Token word)
    {
        NameRef name = ExpectNewName();
        ExpectSymbol("=");
        if (TryParseUse() is { } use)
        {
            return new DefineStatement(word.Line, word.Column, word.Text, [name], use, ParseOptions());
        }
        NameRef circle = ExpectName();
        if (!Current.Is("from"))
        {
            throw Error(Current, $"Expected \"from\" after the circle (\"{word.Text} {name.Name} = k from A to B\"), not {Current.Describe()}.");
        }
        position++;
        ArcEndRef from = ExpectArcEnd();
        ExpectWord("to");
        ArcEndRef to = ExpectArcEnd();
        return new DefineStatement(word.Line, word.Column, word.Text, [name], new ArcDefinition(circle, from, to), ParseOptions());
    }

    // a point or a direction in degrees (counterclockwise from east)
    private ArcEndRef ExpectArcEnd() =>
        IsNumberAt(position) ? new ArcEndRef(null, ExpectNumber("the direction in degrees")) : new ArcEndRef(ExpectName(), 0);

    private DefineStatement ParseAngle(Token word)
    {
        NameRef name = ExpectNewName();
        ExpectSymbol("=");
        if (TryParseUse() is { } use)
        {
            return new DefineStatement(word.Line, word.Column, "angle", [name], use, ParseOptions());
        }
        var definition = new AngleDefinition(ExpectName(), ExpectName(), ExpectName());
        return new DefineStatement(word.Line, word.Column, "angle", [name], definition, ParseOptions());
    }

    private DefineStatement ParseText(Token word)
    {
        NameRef name = ExpectNewName();
        ExpectSymbol("=");
        string content = ExpectText("the text");
        // "below t", "below t gap 0.2": one line below another text
        if (Current.Kind == TokenKind.Name && Current.Text == "below")
        {
            position++;
            NameRef above = ExpectName();
            double gap = 0;
            if (Current.Kind == TokenKind.Name && Current.Text == "gap")
            {
                position++;
                gap = ExpectNumber("the gap (in units)");
            }
            return new DefineStatement(word.Line, word.Column, "text", [name], new TextDefinition(content, null, null) { Above = above, Gap = gap }, ParseOptions());
        }
        if (!(Current.Kind == TokenKind.Name && Current.Text == "at"))
        {
            throw Error(Current, $"Expected \"at\" (coordinates or a point) or \"below\" (another text) after the text, not {Current.Describe()}.");
        }
        position++;
        TextDefinition definition = IsNumberAt(position)
            ? new TextDefinition(content, ParseCoordinates(), null)
            : new TextDefinition(content, null, ExpectName());
        return new DefineStatement(word.Line, word.Column, "text", [name], definition, ParseOptions());
    }

    // "highlight A B [keep] [options]", "unhighlight A B"
    private HighlightStatement ParseHighlight(Token word)
    {
        var targets = new List<NameRef>();
        while (Current.Kind == TokenKind.Name && !IsOption(Current.Text))
        {
            targets.Add(ExpectName());
        }
        if (targets.Count == 0)
        {
            throw Error(Current, $"\"{word.Text}\" needs the names of objects.");
        }
        return new HighlightStatement(word.Line, word.Column, word.Text == "unhighlight", targets, ParseOptions());
    }

    private ShowStatement ParseShow(Token word)
    {
        // "show label A B": only the labels
        bool label = Current.Kind == TokenKind.Name && Current.Text == "label";
        if (label)
        {
            Next();
        }
        // "hide all", "hide label all": everything visible
        if (Current.Kind == TokenKind.Name && Current.Text == "all")
        {
            if (word.Text == "show")
            {
                throw Error(Current, "\"all\" is only possible with \"hide\" (\"hide all\", \"hide label all\"); \"show\" needs the names of objects.");
            }
            Next();
            return new ShowStatement(word.Line, word.Column, false, [], ParseOptions(), label) { All = true };
        }
        var targets = new List<NameRef>();
        while (Current.Kind == TokenKind.Name && !IsOption(Current.Text))
        {
            targets.Add(ExpectName());
        }
        if (targets.Count == 0)
        {
            throw Error(Current, label
                ? $"\"{word.Text} label\" needs the names of objects."
                : $"\"{word.Text}\" needs the names of objects.");
        }
        return new ShowStatement(word.Line, word.Column, word.Text == "show", targets, ParseOptions(), label);
    }

    // "move A B C along P Q", or one point: "move A to …"; "move label A B to 0.2 -0.3": the labels' shift
    private Statement ParseMove(Token word)
    {
        if (Current.Kind == TokenKind.Name && Current.Text == "label")
        {
            Next();
            List<NameRef> labelled = ParseTargets(word, "to");
            ExpectWord("to");
            // a direction ("to SE", "to 300", with "gap 12") or a shift ("to 0.2 -0.3")
            if (IsCompass(Current) || (IsNumberAt(position) && !IsNumberAt(position + 1)))
            {
                double direction = ExpectDirection();
                double? gap = null;
                if (Current.Is("gap") && Current.Kind == TokenKind.Name)
                {
                    position++;
                    gap = ExpectNonNegative("the gap between point and label");
                }
                return new LabelShiftStatement(word.Line, word.Column, labelled, null, ParseOptions()) { Direction = direction, Gap = gap };
            }
            return new LabelShiftStatement(word.Line, word.Column, labelled, ParseCoordinates(), ParseOptions());
        }
        List<NameRef> targets = ParseTargets(word, "along");
        if (Current.Is("along") && Current.Kind == TokenKind.Name)
        {
            position++;
            NameRef from = ExpectName();
            NameRef to = ExpectName();
            return new TranslateStatement(word.Line, word.Column, targets, from, to, ParseOptions());
        }
        if (targets.Count > 1)
        {
            throw Error(Current, $"Expected \"along\" after the objects (\"move A B C along P Q\"; \"to\" moves one point), not {Current.Describe()}.");
        }
        NameRef point = targets[0];
        ExpectWord("to");
        // one number: angle or position on the path; two numbers: coordinates
        if (IsNumberAt(position) && !IsNumberAt(position + 1))
        {
            return new MoveStatement(word.Line, word.Column, point, null, ExpectNumber("the target"), ParseOptions());
        }
        // a point: where it is at that moment
        if (Current.Kind == TokenKind.Name && !IsOption(Current.Text))
        {
            NameRef targetPoint = ExpectName();
            return new MoveStatement(word.Line, word.Column, point, null, null, ParseOptions(), targetPoint);
        }
        if (!IsNumberAt(position))
        {
            throw Error(Current, $"Expected coordinates like 2 3, an angle or position like 90, or a point after \"to\", not {Current.Describe()}.");
        }
        Vec target = ParseCoordinates();
        return new MoveStatement(word.Line, word.Column, point, target, null, ParseOptions());
    }

    // "number l = length s", "number d = length A B", "number w = degrees alpha", "area D", "radius k", "perimeter D"
    private NumberStatement ParseNumber(Token word)
    {
        NameRef name = ExpectNewName();
        if (name.Name == "pi" || Expression.Functions.Contains(name.Name))
        {
            throw Error(name, $"{name.Name} is part of calculations (pi, {string.Join(", ", Expression.Functions)}) and cannot be the name of a number.");
        }
        numberNames.Add(name.Name);
        ExpectSymbol("=");
        // "number h = a * sqrt(3) / 2": computed from numbers; with constants only, a constant itself
        if (Current.Kind == TokenKind.Expression)
        {
            Token token = Next();
            // "number l = size A": a word and a name, as with a measure – but not one of them
            if (System.Text.RegularExpressions.Regex.Match(token.Text, @"^([A-Za-z_]\w*)\s+[A-Za-z_]") is { Success: true } measureLike
                && !numberNames.Contains(measureLike.Groups[1].Value))
            {
                throw Error(token, $"Expected a measure (length, degrees, area, radius, perimeter) or a calculation (2.5, a * 2, …), not \"{measureLike.Groups[1].Value}\".");
            }
            Expression expression;
            try
            {
                expression = Expression.Parse(token.Text);
            }
            catch (ExpressionException e)
            {
                throw new ScriptError(token.Line, token.Column + e.Index, e.Message);
            }
            bool constant = true;
            foreach ((string used, int index) in expression.Names)
            {
                if (used == name.Name || !numberNames.Contains(used))
                {
                    throw new ScriptError(token.Line, token.Column + index,
                        $"{used} is not a number defined before (an expression uses numbers, constants and measured ones: number h = a * sqrt(3) / 2).");
                }
                Referenced(used, token.Line, token.Column + index);
                constant &= constants.ContainsKey(used);
            }
            if (constant)
            {
                constants[name.Name] = expression.Evaluate(used => constants[used])
                    ?? throw Error(token, $"{token.Text} has no value (a division by 0 or the root of a negative number?).");
            }
            return new NumberStatement(word.Line, word.Column, name, "", []) { Expression = expression };
        }
        Token measure = Current;
        if (measure.Kind != TokenKind.Name || measure.Text is not ("length" or "degrees" or "area" or "radius" or "perimeter"))
        {
            throw Error(measure, $"Expected a measure (length, degrees, area, radius, perimeter) or a calculation (2.5, a * 2, …), not {measure.Describe()}.");
        }
        position++;
        var arguments = new List<NameRef> { ExpectName() };
        if (measure.Text == "length" && Current.Kind == TokenKind.Name)
        {
            arguments.Add(ExpectName());
        }
        return new NumberStatement(word.Line, word.Column, name, measure.Text, arguments);
    }

    // "rotate A B C around M by 60"
    private RotateStatement ParseRotate(Token word)
    {
        List<NameRef> targets = ParseTargets(word, "around");
        ExpectWord("around");
        NameRef center = ExpectName();
        ExpectWord("by");
        double degrees = ExpectNumber("the angle in degrees");
        return new RotateStatement(word.Line, word.Column, targets, center, degrees, ParseOptions());
    }

    // "reflect A B C across g" (a segment, ray or line), "reflect A B C across P Q" (the line PQ), "reflect A B C in Z"
    private ReflectStatement ParseReflect(Token word)
    {
        List<NameRef> targets = ParseTargets(word, "across\" or \"in");
        if (Current.Is("in") && Current.Kind == TokenKind.Name)
        {
            position++;
            return new ReflectStatement(word.Line, word.Column, targets, ExpectName(), null, true, ParseOptions());
        }
        if (!(Current.Is("across") && Current.Kind == TokenKind.Name))
        {
            throw Error(Current, $"Expected \"across\" (a line) or \"in\" (a point) after the objects (\"reflect A B C across g\"), not {Current.Describe()}.");
        }
        position++;
        NameRef axis = ExpectName();
        NameRef? second = Current.Kind == TokenKind.Name && !IsOption(Current.Text) ? ExpectName() : null;
        return new ReflectStatement(word.Line, word.Column, targets, axis, second, false, ParseOptions());
    }

    // the names of the objects a movement moves, up to "to", "along", "around", "across" or "in"
    private List<NameRef> ParseTargets(Token word, string next)
    {
        var targets = new List<NameRef>();
        while (Current.Kind == TokenKind.Name && !(Current.Text is "to" or "along" or "around" or "across" or "in"))
        {
            targets.Add(ExpectName());
        }
        if (targets.Count == 0)
        {
            throw Error(Current, $"\"{word.Text}\" needs the names of objects before \"{next}\".");
        }
        return targets;
    }

    private Vec ParseCoordinates()
    {
        double x = ExpectNumber("the x coordinate");
        double y = ExpectNumber("the y coordinate");
        return new Vec(x, y);
    }

    private OptionList ParseOptions()
    {
        var values = new List<OptionValue>();
        while (Current.Kind == TokenKind.Name)
        {
            Token option = Next();
            if (vars.TryGetValue(option.Text, out List<OptionValue>? group))
            {
                Referenced(option.Text, option.Line, option.Column);
                values.AddRange(group);
                continue;
            }
            object? value = option.Text switch
            {
                "duration" => ExpectNonNegative("the duration in milliseconds"),
                "width" => ExpectNonNegative("the line width"),
                "size" => ExpectPositive("the size"),
                "radius" => ExpectPositive("the radius"),
                "labelsize" => ExpectPositive("the label size"),
                "labelpos" => ExpectNumber("the position of the label (0 at A, 1 at B)"),
                "labeldir" => ExpectDirection(),
                "labelgap" => ExpectNonNegative("the gap between point and label"),
                "opacity" => ExpectOpacity(),
                "color" => ExpectColor(),
                // "fill" alone: in the object's own colour
                "fill" => Current.Kind == TokenKind.Color || (Current.Kind == TokenKind.Name && Color.Names.ContainsKey(Current.Text)) ? ExpectColor() : null,
                "label" => ExpectText("the label"),
                "offset" or "shift" => ParseCoordinates(),
                "align" => Current.Kind == TokenKind.Name && Current.Text is "left" or "center" or "right"
                    ? Next().Text
                    : throw Error(Current, $"Expected left, center or right after \"align\", not {Current.Describe()}."),
                // "arrow" alone: at the end; "arrow start", "arrow end", "arrow both"
                "arrow" => Current.Kind == TokenKind.Name && Current.Text is "start" or "end" or "both" ? Next().Text : "end",
                "nolabel" or "hidden" or "straight" or "back" or "front" or "keep" or "halo" or "clockwise" or "linear" or "ease" or "easein" or "easeout" or "easeinout" => null,
                _ => throw Error(option, $"Unknown option or var \"{option.Text}\" (possible options: {string.Join(", ", OptionNames.Order())})."),
            };
            values.Add(new OptionValue(option.Text, value, option.Line, option.Column));
        }
        return new OptionList(values);
    }

    // ------------------------------------------------------------------ expectations

    private ScriptError Error(Token token, string message) => new(token.Line, token.Column, message);

    private ScriptError Error(NameRef name, string message) => new(name.Line, name.Column, message);

    private void ExpectLineEnd()
    {
        if (Current.Kind != TokenKind.NewLine && Current.Kind != TokenKind.End)
        {
            throw Error(Current, $"Unexpected {Current.Describe()}.");
        }
    }

    private void ExpectWord(string word)
    {
        if (!Current.Is(word) || Current.Kind != TokenKind.Name)
        {
            throw Error(Current, $"Expected \"{word}\", not {Current.Describe()}.");
        }
        position++;
    }

    private void ExpectSymbol(string symbol)
    {
        if (!(Current.Kind == TokenKind.Symbol && Current.Text == symbol))
        {
            throw Error(Current, $"Expected \"{symbol}\", not {Current.Describe()}.");
        }
        position++;
    }

    private NameRef ExpectName()
    {
        Token token = Current;
        if (token.Kind != TokenKind.Name)
        {
            throw Error(token, $"Expected the name of an object, not {token.Describe()}.");
        }
        if (Keywords.Contains(token.Text))
        {
            throw Error(token, $"Expected the name of an object, not the keyword \"{token.Text}\".");
        }
        position++;
        Referenced(token.Text, token.Line, token.Column);
        return new NameRef(token.Text, token.Line, token.Column);
    }

    // a place where a name stands (an object, var, text or number)
    private void Referenced(string name, int line, int column)
    {
        if (references != null && name != "_")
        {
            references.Add(new ScriptReference(name, null, line, column));
        }
    }

    private bool IsOption(string word) => OptionNames.Contains(word) || vars.ContainsKey(word);

    private NameRef ExpectNewName()
    {
        NameRef name = ExpectName();
        CheckNewName(name);
        return name;
    }

    private void CheckNewName(NameRef name)
    {
        if (vars.ContainsKey(name.Name))
        {
            throw Error(name, $"{name.Name} is already the name of a var.");
        }
        if (texts.ContainsKey(name.Name))
        {
            throw Error(name, $"{name.Name} is already the name of a text (\"set\").");
        }
        objectNames.Add(name.Name);
        Defined("object", name);
    }


    // a text in quotes or the name of a text from "set"
    private string ExpectText(string what)
    {
        Token token = Current;
        if (token.Kind == TokenKind.Name && texts.TryGetValue(token.Text, out string? text))
        {
            position++;
            Referenced(token.Text, token.Line, token.Column);
            return text;
        }
        if (token.Kind != TokenKind.String)
        {
            throw Error(token, $"Expected {what} in double quotes or the name of a text from \"set\", not {token.Describe()}.");
        }
        position++;
        // the token starts at its opening quote
        return TextOf(token, 1);
    }

    // the text of a token; its placeholders ("{l}") are checked at the end
    private string TextOf(Token token, int offset = 0)
    {
        foreach ((string name, int index, _) in NumberText.Find(token.Text))
        {
            placeholders.Add((name, token.Line, token.Column + offset + index));
            // the name after the brace
            Referenced(name, token.Line, token.Column + offset + index + 1);
        }
        return token.Text;
    }

    // a number or the name of a constant
    private bool IsNumberAt(int index) =>
        index < tokens.Count && (tokens[index].Kind == TokenKind.Number || (tokens[index].Kind == TokenKind.Name && constants.ContainsKey(tokens[index].Text)));

    private double ExpectNumber(string what)
    {
        Token token = Current;
        if (token.Kind == TokenKind.Name && constants.TryGetValue(token.Text, out double constant))
        {
            position++;
            Referenced(token.Text, token.Line, token.Column);
            return constant;
        }
        if (token.Kind == TokenKind.Name && numberNames.Contains(token.Text))
        {
            throw Error(token, $"{token.Text} follows the drawing (it is measured or computed from measured numbers): it can be shown in texts ({{{token.Text}}}), but {what} needs a number or a constant (number c = 2.5).");
        }
        if (token.Kind != TokenKind.Number)
        {
            throw Error(token, $"Expected {what} (a number), not {token.Describe()}.");
        }
        position++;
        return token.Number;
    }

    private double ExpectPositive(string what)
    {
        Token token = Current;
        double value = ExpectNumber(what);
        return value > 0 ? value : throw Error(token, $"{Capitalize(what)} must be greater than 0.");
    }

    private double ExpectNonNegative(string what)
    {
        Token token = Current;
        double value = ExpectNumber(what);
        return value >= 0 ? value : throw Error(token, $"{Capitalize(what)} cannot be negative.");
    }

    private double ExpectOpacity()
    {
        Token token = Current;
        double value = ExpectNumber("the opacity");
        return value is >= 0 and <= 1 ? value : throw Error(token, "The opacity must be between 0 and 1.");
    }

    // the compass directions of a label, in degrees counterclockwise from east
    private static readonly Dictionary<string, double> Compass = new(StringComparer.Ordinal)
    {
        ["E"] = 0, ["NE"] = 45, ["N"] = 90, ["NW"] = 135, ["W"] = 180, ["SW"] = 225, ["S"] = 270, ["SE"] = 315,
    };

    private static bool IsCompass(Token token) => token.Kind == TokenKind.Name && Compass.ContainsKey(token.Text);

    // N, NE, E, SE, S, SW, W, NW or degrees (counterclockwise from east)
    private double ExpectDirection()
    {
        if (IsCompass(Current))
        {
            return Compass[Next().Text];
        }
        if (!IsNumberAt(position))
        {
            throw Error(Current, $"Expected a direction: N, NE, E, SE, S, SW, W, NW or degrees (counterclockwise from east), not {Current.Describe()}.");
        }
        return ExpectNumber("the direction in degrees");
    }

    private Color ExpectColor()
    {
        Token token = Current;
        if (token.Kind != TokenKind.Color && token.Kind != TokenKind.Name)
        {
            throw Error(token, $"Expected a colour (a name like red, or #rrggbb), not {token.Describe()}.");
        }
        position++;
        try
        {
            return Color.Parse(token.Text);
        }
        catch (GeoException e)
        {
            throw Error(token, e.Message);
        }
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

    // the mark of a step: "#name" at the end of its line, after a space (or alone)
    [GeneratedRegex(@"(?:^|\s)#([\p{L}\p{N}_-]+)\s*$")]
    private static partial Regex StepMark();
}
