using System.Globalization;

namespace GeoAnim;

/// <summary>
/// An arithmetic expression over numbers and variables, as in "number h = a * sqrt(3) / 2": + − * / ^ (power), unary
/// minus, parentheses, the functions sqrt and abs and the constant pi. Spaces do not matter. It is evaluated with the
/// variables' values at the time (constants once, measured numbers again and again; later also the variable of a
/// function graph).
/// </summary>
public sealed class Expression
{
    private readonly Node root;

    private Expression(string text, Node root, List<(string Name, int Index)> names)
    {
        Text = text;
        this.root = root;
        Names = names;
    }

    /// <summary>The expression as written.</summary>
    public string Text { get; }

    /// <summary>The variables it uses, each where it stands in <see cref="Text"/> (index from 0), in order.</summary>
    public IReadOnlyList<(string Name, int Index)> Names { get; }

    /// <summary>The functions an expression knows (one argument each).</summary>
    public static IReadOnlyCollection<string> Functions { get; } = ["sqrt", "abs"];

    /// <summary>Reads an expression; throws <see cref="ExpressionException"/> with the place of a mistake.</summary>
    public static Expression Parse(string text)
    {
        var reader = new Reader(text);
        Node root = reader.Sum();
        reader.SkipSpaces();
        if (!reader.AtEnd)
        {
            throw new ExpressionException($"Unexpected \"{text[reader.Position]}\" in the expression.", reader.Position);
        }
        return new Expression(text, root, reader.Names);
    }

    /// <summary>
    /// The value, with <paramref name="variable"/> giving the value of each variable (null: it has none at the moment);
    /// null if a variable has none or the result is not a number (a division by 0, the root of a negative number).
    /// </summary>
    public double? Evaluate(Func<string, double?> variable)
    {
        double? value = root.Evaluate(variable);
        return value is double v && double.IsFinite(v) ? v : null;
    }

    private abstract record Node
    {
        public abstract double? Evaluate(Func<string, double?> variable);
    }

    private sealed record Constant(double Value) : Node
    {
        public override double? Evaluate(Func<string, double?> variable) => Value;
    }

    private sealed record Variable(string Name) : Node
    {
        public override double? Evaluate(Func<string, double?> variable) => variable(Name);
    }

    private sealed record Unary(char Operator, Node Operand) : Node
    {
        public override double? Evaluate(Func<string, double?> variable) => -Operand.Evaluate(variable);
    }

    private sealed record Binary(char Operator, Node Left, Node Right) : Node
    {
        public override double? Evaluate(Func<string, double?> variable)
        {
            if (Left.Evaluate(variable) is not double a || Right.Evaluate(variable) is not double b)
            {
                return null;
            }
            return Operator switch
            {
                '+' => a + b,
                '-' => a - b,
                '*' => a * b,
                '/' => a / b,
                _ => Math.Pow(a, b),
            };
        }
    }

    private sealed record Call(string Function, Node Argument) : Node
    {
        public override double? Evaluate(Func<string, double?> variable) => Argument.Evaluate(variable) is double x
            ? Function switch
            {
                "sqrt" => Math.Sqrt(x),
                _ => Math.Abs(x),
            }
            : null;
    }

    // recursive descent: sum of products of powers
    private sealed class Reader(string text)
    {
        public int Position;

        public List<(string Name, int Index)> Names { get; } = [];

        public bool AtEnd => Position >= text.Length;

        public void SkipSpaces()
        {
            while (Position < text.Length && char.IsWhiteSpace(text[Position]))
            {
                Position++;
            }
        }

        private bool Take(char c)
        {
            SkipSpaces();
            if (Position < text.Length && text[Position] == c)
            {
                Position++;
                return true;
            }
            return false;
        }

        public Node Sum()
        {
            Node left = Product();
            while (true)
            {
                if (Take('+'))
                {
                    left = new Binary('+', left, Product());
                }
                else if (Take('-'))
                {
                    left = new Binary('-', left, Product());
                }
                else
                {
                    return left;
                }
            }
        }

        private Node Product()
        {
            Node left = Signed();
            while (true)
            {
                if (Take('*'))
                {
                    left = new Binary('*', left, Signed());
                }
                else if (Take('/'))
                {
                    left = new Binary('/', left, Signed());
                }
                else
                {
                    return left;
                }
            }
        }

        // a unary minus binds weaker than a power: -2^2 is -4
        private Node Signed() => Take('-') ? new Unary('-', Signed()) : Take('+') ? Signed() : Power();

        private Node Power()
        {
            Node basis = Atom();
            // right-associative: 2^3^2 is 2^9
            return Take('^') ? new Binary('^', basis, Signed()) : basis;
        }

        private Node Atom()
        {
            SkipSpaces();
            if (AtEnd)
            {
                throw new ExpressionException("The expression ends too early: a number or a name is missing.", Position);
            }
            char c = text[Position];
            if (c == '(')
            {
                Position++;
                Node inner = Sum();
                if (!Take(')'))
                {
                    throw new ExpressionException("A \")\" is missing.", Position);
                }
                return inner;
            }
            if (char.IsDigit(c) || c == '.')
            {
                int start = Position;
                while (Position < text.Length && (char.IsDigit(text[Position]) || text[Position] == '.'))
                {
                    Position++;
                }
                string number = text[start..Position];
                return double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value)
                    ? new Constant(value)
                    : throw new ExpressionException($"\"{number}\" is not a number.", start);
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = Position;
                while (Position < text.Length && (char.IsLetterOrDigit(text[Position]) || text[Position] == '_'))
                {
                    Position++;
                }
                while (Position < text.Length && text[Position] == '\'')
                {
                    Position++;
                }
                string name = text[start..Position];
                if (Functions.Contains(name))
                {
                    if (!Take('('))
                    {
                        throw new ExpressionException($"{name} needs its argument in parentheses: {name}(…).", Position);
                    }
                    Node argument = Sum();
                    if (!Take(')'))
                    {
                        throw new ExpressionException("A \")\" is missing.", Position);
                    }
                    return new Call(name, argument);
                }
                if (name == "pi")
                {
                    return new Constant(Math.PI);
                }
                Names.Add((name, start));
                return new Variable(name);
            }
            throw new ExpressionException($"Unexpected \"{c}\" in the expression.", Position);
        }
    }
}

/// <summary>A mistake in an expression, at <see cref="Index"/> (from 0) in its text.</summary>
public sealed class ExpressionException(string message, int index) : Exception(message)
{
    public int Index { get; } = index;
}
