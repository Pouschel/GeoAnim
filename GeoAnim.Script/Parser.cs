namespace GeoAnim.Script;

/// <summary>Reads the statements of a script (one per line; "together" … "end" spans several lines).</summary>
internal sealed class Parser
{
    /// <summary>Words that cannot be names of objects.</summary>
    internal static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "title", "view", "background", "grid", "axes", "step", "point", "segment", "line", "ray", "circle", "polygon",
        "text", "show", "hide", "move", "pause", "together", "end", "midpoint", "intersect", "center", "through",
        "radius", "at", "to", "on", "x", "y",
        "duration", "color", "width", "fill", "opacity", "size", "label", "nolabel", "hidden", "offset",
        "linear", "ease", "easein", "easeout", "easeinout",
    };

    private static readonly HashSet<string> OptionNames = new(StringComparer.Ordinal)
    {
        "duration", "color", "width", "fill", "opacity", "size", "label", "nolabel", "hidden", "offset",
        "linear", "ease", "easein", "easeout", "easeinout",
    };

    private readonly List<Token> tokens;
    private int position;

    private Parser(List<Token> tokens) => this.tokens = tokens;

    public static List<Statement> Parse(string source)
    {
        var parser = new Parser(Lexer.Tokenize(source));
        return parser.ParseBlock(null, out _);
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
            statements.Add(ParseStatement(inTogether));
        }
    }

    private Statement ParseStatement(bool inTogether)
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
            case "pause":
                statement = new PauseStatement(l, c, Current.Kind == TokenKind.Number ? ExpectNonNegative("the pause") : 500);
                break;
            case "together":
                if (inTogether)
                {
                    throw Error(word, "\"together\" blocks cannot be nested.");
                }
                ExpectLineEnd();
                List<Statement> body = ParseBlock(word, out _);
                return new TogetherStatement(l, c, body);
            default:
                throw Error(word, $"Unknown command \"{word.Text}\".");
        }
        ExpectLineEnd();
        return statement;
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
        if (Current.Kind == TokenKind.Number)
        {
            definition = new CoordinatesDefinition(ParseCoordinates());
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
            throw Error(Current, $"Expected coordinates like 2 3, \"midpoint A B\", \"intersect k1 k2\" or \"on k at 30\" after \"=\", not {Current.Describe()}.");
        }
        if (names.Count > 1 && definition is not IntersectDefinition)
        {
            throw Error(names[1], "Several names are only possible for intersections.");
        }
        if (names.Count > 2)
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
            a = ExpectName();
            b = ExpectName();
        }
        else
        {
            // short form: "segment A B" is called AB
            a = first;
            b = ExpectName();
            name = new NameRef(a.Name + b.Name, a.Line, a.Column);
        }
        return new DefineStatement(word.Line, word.Column, word.Text, [name], new TwoPointsDefinition(a, b), ParseOptions());
    }

    private DefineStatement ParseCircle(Token word)
    {
        NameRef name = ExpectNewName();
        ExpectSymbol("=");
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
        var vertices = new List<NameRef>();
        while (Current.Kind == TokenKind.Name && !OptionNames.Contains(Current.Text))
        {
            vertices.Add(ExpectName());
        }
        if (vertices.Count < 3)
        {
            throw Error(Current, "A polygon needs at least 3 points.");
        }
        return new DefineStatement(word.Line, word.Column, "polygon", [name], new PolygonDefinition(vertices), ParseOptions());
    }

    private DefineStatement ParseText(Token word)
    {
        NameRef name = ExpectNewName();
        ExpectSymbol("=");
        string content = ExpectString("the text");
        ExpectWord("at");
        TextDefinition definition = Current.Kind == TokenKind.Number
            ? new TextDefinition(content, ParseCoordinates(), null)
            : new TextDefinition(content, null, ExpectName());
        return new DefineStatement(word.Line, word.Column, "text", [name], definition, ParseOptions());
    }

    private ShowStatement ParseShow(Token word)
    {
        var targets = new List<NameRef>();
        while (Current.Kind == TokenKind.Name && !OptionNames.Contains(Current.Text))
        {
            targets.Add(ExpectName());
        }
        if (targets.Count == 0)
        {
            throw Error(Current, $"\"{word.Text}\" needs the names of objects.");
        }
        return new ShowStatement(word.Line, word.Column, word.Text == "show", targets, ParseOptions());
    }

    private MoveStatement ParseMove(Token word)
    {
        NameRef point = ExpectName();
        ExpectWord("to");
        // one number: angle or position on the path; two numbers: coordinates
        if (Current.Kind == TokenKind.Number && tokens[position + 1].Kind != TokenKind.Number)
        {
            return new MoveStatement(word.Line, word.Column, point, null, ExpectNumber("the target"), ParseOptions());
        }
        Vec target = ParseCoordinates();
        return new MoveStatement(word.Line, word.Column, point, target, null, ParseOptions());
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
            object? value = option.Text switch
            {
                "duration" => ExpectNonNegative("the duration in milliseconds"),
                "width" => ExpectNonNegative("the line width"),
                "size" => ExpectPositive("the size"),
                "opacity" => ExpectOpacity(),
                "color" or "fill" => ExpectColor(),
                "label" => ExpectString("the label"),
                "offset" => ParseCoordinates(),
                "nolabel" or "hidden" or "linear" or "ease" or "easein" or "easeout" or "easeinout" => null,
                _ => throw Error(option, $"Unknown option \"{option.Text}\" (possible: {string.Join(", ", OptionNames.Order())})."),
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

    private NameRef ExpectNewName() => ExpectName();

    private string ExpectString(string what)
    {
        Token token = Current;
        if (token.Kind != TokenKind.String)
        {
            throw Error(token, $"Expected {what} in double quotes, not {token.Describe()}.");
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
