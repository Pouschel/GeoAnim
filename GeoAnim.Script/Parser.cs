namespace GeoAnim.Script;

/// <summary>Reads the statements of a script (one per line; "together" … "end" spans several lines).</summary>
internal sealed class Parser
{
    /// <summary>Words that cannot be names of objects.</summary>
    internal static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "title", "description", "info", "keyword", "view", "background", "grid", "axes", "speed", "var", "default", "include", "input", "output", "use",
        "draw", "steps", "copy", "set", "step", "point", "segment", "line", "ray", "circle", "polygon", "angle",
        "text", "show", "hide", "move", "rotate", "along", "around", "by", "pause", "together", "end", "midpoint", "intersect", "center", "through",
        "radius", "at", "to", "on", "x", "y",
        "duration", "color", "width", "fill", "opacity", "size", "labelsize", "labelpos", "label", "nolabel", "hidden", "offset",
        "arrow", "start", "both", "linear", "ease", "easein", "easeout", "easeinout",
    };

    private static readonly HashSet<string> OptionNames = new(StringComparer.Ordinal)
    {
        "duration", "color", "width", "fill", "opacity", "size", "radius", "labelsize", "labelpos", "label", "nolabel", "hidden", "offset",
        "arrow", "linear", "ease", "easein", "easeout", "easeinout",
    };

    /// <summary>The kinds a "default" can name.</summary>
    private static readonly HashSet<string> DefaultKinds = new(StringComparer.Ordinal)
    {
        "point", "segment", "line", "ray", "circle", "polygon", "angle", "text", "show", "hide", "move", "rotate",
    };

    private readonly List<Token> tokens;
    private int position;

    // "var name options…": named groups of options, valid from their line on
    private readonly Dictionary<string, List<OptionValue>> vars = new(StringComparer.Ordinal);

    // "set name = text": named texts for "text" and "label", valid from their line on
    private readonly Dictionary<string, string> texts = new(StringComparer.Ordinal);

    // names given to objects so far (a var cannot take one of them, and the other way round)
    private readonly HashSet<string> objectNames = new(StringComparer.Ordinal);

    // for an editor: the names defined so far (lines of the source), and the source's lines for their text
    private readonly List<ScriptDefinition>? definitions;
    private readonly string[] lines;

    private Parser(List<Token> tokens, string source, List<ScriptDefinition>? definitions)
    {
        this.tokens = tokens;
        this.definitions = definitions;
        lines = definitions == null ? [] : source.Split('\n');
    }

    public static List<Statement> Parse(string source) => Parse(source, null);

    /// <summary>Parses; with <paramref name="definitions"/> also collects the names defined (as far as it gets).</summary>
    public static List<Statement> Parse(string source, List<ScriptDefinition>? definitions)
    {
        var parser = new Parser(Lexer.Tokenize(source), source, definitions);
        return parser.ParseBlock(null, out _);
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
                statement = new TitleStatement(l, c, Next().Text);
                break;
            case "description":
                statement = new DescriptionStatement(l, c, Next().Text);
                break;
            case "info":
                statement = new InfoStatement(l, c, Next().Text);
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
                statement = new GridStatement(l, c, Current.Kind == TokenKind.Number ? ExpectPositive("the grid distance") : 1);
                break;
            case "axes":
                statement = new AxesStatement(l, c);
                break;
            case "speed":
                statement = new SpeedStatement(l, c, ExpectPositive("the speed"));
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
                    texts[name.Name] = Next().Text;
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
                if (inTogether)
                {
                    throw Error(word, "A step cannot begin inside a \"together\" block.");
                }
                statement = new StepStatement(l, c, Next().Text);
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
            case "move":
                statement = ParseMove(word);
                break;
            case "rotate":
                statement = ParseRotate(word);
                break;
            case "pause":
                statement = new PauseStatement(l, c, Current.Kind == TokenKind.Number ? ExpectNonNegative("the pause") : 500);
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
        else if (Current.Kind == TokenKind.Number)
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
            definition = new CircleRadiusDefinition(center, ExpectPositive("the radius"));
        }
        else
        {
            throw Error(Current, $"Expected \"through <point>\" or \"radius <number>\", not {Current.Describe()}.");
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
        ExpectWord("at");
        TextDefinition definition = Current.Kind == TokenKind.Number
            ? new TextDefinition(content, ParseCoordinates(), null)
            : new TextDefinition(content, null, ExpectName());
        return new DefineStatement(word.Line, word.Column, "text", [name], definition, ParseOptions());
    }

    private ShowStatement ParseShow(Token word)
    {
        // "show label A B": only the labels
        bool label = Current.Kind == TokenKind.Name && Current.Text == "label";
        if (label)
        {
            Next();
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

    // "move A B C along P Q", or one point: "move A to …"
    private Statement ParseMove(Token word)
    {
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
        if (Current.Kind == TokenKind.Number && tokens[position + 1].Kind != TokenKind.Number)
        {
            return new MoveStatement(word.Line, word.Column, point, null, ExpectNumber("the target"), ParseOptions());
        }
        // a point: where it is at that moment
        if (Current.Kind == TokenKind.Name && !IsOption(Current.Text))
        {
            NameRef targetPoint = ExpectName();
            return new MoveStatement(word.Line, word.Column, point, null, null, ParseOptions(), targetPoint);
        }
        if (Current.Kind != TokenKind.Number)
        {
            throw Error(Current, $"Expected coordinates like 2 3, an angle or position like 90, or a point after \"to\", not {Current.Describe()}.");
        }
        Vec target = ParseCoordinates();
        return new MoveStatement(word.Line, word.Column, point, target, null, ParseOptions());
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

    // the names of the objects a movement moves, up to "to", "along" or "around"
    private List<NameRef> ParseTargets(Token word, string next)
    {
        var targets = new List<NameRef>();
        while (Current.Kind == TokenKind.Name && !(Current.Text is "to" or "along" or "around"))
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
                "opacity" => ExpectOpacity(),
                "color" or "fill" => ExpectColor(),
                "label" => ExpectText("the label"),
                "offset" => ParseCoordinates(),
                // "arrow" alone: at the end; "arrow start", "arrow end", "arrow both"
                "arrow" => Current.Kind == TokenKind.Name && Current.Text is "start" or "end" or "both" ? Next().Text : "end",
                "nolabel" or "hidden" or "linear" or "ease" or "easein" or "easeout" or "easeinout" => null,
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
        return new NameRef(token.Text, token.Line, token.Column);
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
            return text;
        }
        if (token.Kind != TokenKind.String)
        {
            throw Error(token, $"Expected {what} in double quotes or the name of a text from \"set\", not {token.Describe()}.");
        }
        position++;
        return token.Text;
    }

    private double ExpectNumber(string what)
    {
        Token token = Current;
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
}
