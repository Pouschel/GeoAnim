using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GeoAnim.Viewer;

/// <summary>
/// What differs from group to group on a test or worksheet, worked out before the page is read: blocks "::: let" (values,
/// drawn by chance or worked out), blocks "::: pick n" (n of the lines, by chance, in a random order; "+ …"/"- …": true
/// or false statements with the answer as a blank) and "{{…}}" in the text (a value, a sum, "{{alt: A | B | C}}": the
/// group's alternative). Every group has its own chance (from the head's "seed" and the group), the same on its sheet for
/// the students and with the solutions. The text keeps its lines (what is left out becomes empty lines), so that errors
/// and the preview find their places.
/// </summary>
internal sealed partial class SheetScript
{
    private readonly Random random;
    private readonly int group;
    private readonly Dictionary<string, Value> values = new(StringComparer.Ordinal);
    private readonly List<MarkdownProblem> errors;

    private SheetScript(int seed, int group, List<MarkdownProblem> errors)
    {
        // every group its own chance; the same group always the same
        random = new Random(unchecked(seed * 7919 + group * 104729 + 17));
        this.group = group;
        this.errors = errors;
    }

    /// <summary>
    /// The page for the group with the number <paramref name="group"/> (from 0): its "::: let" and "::: pick" blocks and
    /// "{{…}}" worked out. <paramref name="seed"/>: the head's (the same seed, the same sheets).
    /// </summary>
    public static string Expand(string text, int seed, int group, List<MarkdownProblem> errors)
    {
        var script = new SheetScript(seed, group, errors);
        string[] lines = text.Split('\n');
        var result = new List<string>(lines.Length);
        int start = 0;
        // the head stays as it is
        if (lines.Length > 0 && lines[0].TrimEnd('\r') == "---")
        {
            int end = Array.FindIndex(lines, 1, l => l.TrimEnd('\r') is "---" or "...");
            if (end > 0)
            {
                result.AddRange(lines[..(end + 1)]);
                start = end + 1;
            }
        }
        bool fenced = false;
        for (int i = start; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }
            Match block = fenced ? Match.Empty : BlockStart().Match(line);
            if (!block.Success)
            {
                result.Add(fenced ? lines[i] : script.Interpolate(line, i + 1));
                continue;
            }
            // to its ":::" (or the end)
            int close = i + 1;
            while (close < lines.Length && lines[close].Trim() != ":::")
            {
                close++;
            }
            string[] body = lines[(i + 1)..Math.Min(close, lines.Length)].Select(l => l.TrimEnd('\r')).ToArray();
            List<string> made = block.Groups[1].Value == "let"
                ? script.Let(body, i + 2)
                : script.Pick(block.Groups[2].Value, body, i + 1);
            int taken = Math.Min(close, lines.Length - 1) - i + 1;
            // as many lines as before: what is left over empty
            for (int k = 0; k < taken; k++)
            {
                result.Add(k < made.Count ? made[k] : "");
            }
            if (made.Count > taken)
            {
                // more than fits (a short block that makes long lists): the rest on the last line, joined (rare)
                result[^1] = string.Join("\n", made.Skip(taken - 1));
            }
            i = Math.Min(close, lines.Length - 1);
        }
        return string.Join("\n", result);
    }

    [GeneratedRegex(@"^\s*:::\s*(let|pick)\b\s*(.*?)\s*$")]
    private static partial Regex BlockStart();

    private void Error(int line, string message) => errors.Add(new MarkdownProblem(line, 1, message));

    // ------------------------------------------------------------------ values

    /// <summary>A number, a text or a list of them.</summary>
    internal sealed record Value(double? Number, string? Text, IReadOnlyList<Value>? List)
    {
        public static Value Of(double number) => new(number, null, null);

        public static Value Of(string text) => new(null, text, null);

        public static Value Of(IReadOnlyList<Value> list) => new(null, null, list);

        public double AsNumber() => Number ?? (Text != null && double.TryParse(Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
            ? n : throw new FormatException($"\"{this}\" is no number."));

        public IReadOnlyList<Value> AsList() => List ?? [this];

        // numbers as in German: 2,5; whole ones without a comma; lists with ", "
        public override string ToString() => Number is double n ? Format(n) : Text ?? string.Join(", ", List!.Select(v => v.ToString()));

        public static string Format(double n) =>
            Math.Abs(n - Math.Round(n)) < 1e-9 ? Math.Round(n).ToString("0", CultureInfo.InvariantCulture) : n.ToString("0.##########", CultureInfo.GetCultureInfo("de-DE"));
    }

    // ------------------------------------------------------------------ ::: let

    // "name = random 3..6 [except a, 5]", "name = pick 3 from list", "name = shuffle list", "name = alt a | b | c",
    // "name = a, b, c" (a list), "name = expression"
    private List<string> Let(string[] body, int firstLine)
    {
        for (int i = 0; i < body.Length; i++)
        {
            string line = body[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }
            Match definition = Regex.Match(line, @"^([\p{L}_][\p{L}\p{N}_]*)\s*=\s*(.+)$");
            if (!definition.Success)
            {
                Error(firstLine + i, $"Expected \"name = …\" in ::: let, not \"{line}\".");
                continue;
            }
            try
            {
                values[definition.Groups[1].Value] = Define(definition.Groups[2].Value.Trim());
            }
            catch (FormatException e)
            {
                Error(firstLine + i, $"{definition.Groups[1].Value}: {e.Message}");
            }
        }
        return [];
    }

    private Value Define(string definition)
    {
        Match random = Regex.Match(definition, @"^random\s+(.+?)\s*\.\.\s*(.+?)(?:\s+step\s+(.+?))?(?:\s+except\s+(.+))?$");
        if (random.Success)
        {
            double from = Evaluate(random.Groups[1].Value).AsNumber(), to = Evaluate(random.Groups[2].Value).AsNumber();
            double step = random.Groups[3].Success ? Evaluate(random.Groups[3].Value).AsNumber() : 1;
            var except = random.Groups[4].Success ? ListOf(random.Groups[4].Value).SelectMany(v => v.AsList()).Select(v => v.AsNumber()).ToList() : [];
            if (step <= 0 || to < from)
            {
                throw new FormatException("random needs from ≤ to (and a step > 0): random 3..6.");
            }
            var choices = new List<double>();
            for (double x = from; x <= to + step * 1e-9; x += step)
            {
                double value = Math.Round(x, 10);
                if (!except.Any(e => Math.Abs(e - value) < 1e-9))
                {
                    choices.Add(value);
                }
            }
            if (choices.Count == 0)
            {
                throw new FormatException("random has nothing left to choose from.");
            }
            return Value.Of(choices[this.random.Next(choices.Count)]);
        }
        Match pick = Regex.Match(definition, @"^pick\s+(.+?)\s+from\s+(.+)$");
        if (pick.Success)
        {
            int count = (int)Evaluate(pick.Groups[1].Value).AsNumber();
            List<Value> list = ListOf(pick.Groups[2].Value).SelectMany(v => v.AsList()).ToList();
            if (count < 0 || count > list.Count)
            {
                throw new FormatException($"pick {count} from a list of {list.Count}.");
            }
            // n of them, in the order of the list (shuffle them to show them mixed)
            var chosen = Enumerable.Range(0, list.Count).OrderBy(_ => this.random.Next()).Take(count).Order().Select(k => list[k]).ToList();
            return Value.Of(chosen);
        }
        Match shuffle = Regex.Match(definition, @"^shuffle\s+(.+)$");
        if (shuffle.Success)
        {
            return Value.Of(Shuffled(ListOf(shuffle.Groups[1].Value).SelectMany(v => v.AsList()).ToList()));
        }
        Match alt = Regex.Match(definition, @"^alt\s+(.+)$");
        if (alt.Success)
        {
            return Alternative(alt.Groups[1].Value);
        }
        List<Value> items = ListOf(definition);
        return items.Count == 1 ? items[0] : Value.Of(items);
    }

    // a list: "a, b, c" (each an expression, a word or text in quotes)
    private List<Value> ListOf(string text) => SplitTop(text, ',').Select(Item).ToList();

    // the parts of a text between the separators that are not in brackets or quotes
    private static List<string> SplitTop(string text, char separator)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c is '(' or '[')
            {
                depth++;
            }
            else if (!quoted && c is ')' or ']')
            {
                depth--;
            }
            else if (!quoted && depth == 0 && c == separator)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return parts;
    }

    // an item of a list: an expression, else the words as they are ("Merkur", "1+"); what looks like a calculation
    // ("2^ib - x", "bin(…)") must be one
    private Value Item(string text)
    {
        text = text.Trim();
        try
        {
            return Evaluate(text);
        }
        catch (FormatException) when (!Regex.IsMatch(text, @"[\^*/()\[\]]"))
        {
            return Value.Of(text);
        }
    }

    private List<Value> Shuffled(List<Value> list)
    {
        var copy = list.ToList();
        // a different order where there can be one (as the old ShuffleMax: not the same as before)
        for (int attempt = 0; attempt < 10; attempt++)
        {
            for (int i = copy.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (copy[i], copy[j]) = (copy[j], copy[i]);
            }
            if (copy.Count < 2 || !copy.Select(v => v.ToString()).SequenceEqual(list.Select(v => v.ToString())))
            {
                break;
            }
        }
        return copy;
    }

    // "A | B | C": the group's (the first group the first, …; fewer than groups: again from the start)
    private Value Alternative(string text)
    {
        List<string> choices = SplitTop(text, '|').Select(c => c.Trim()).ToList();
        return Item(choices[group % choices.Count]);
    }

    // ------------------------------------------------------------------ ::: pick

    // "::: pick 3" (or "::: pick 3 wf"): 3 of the lines, by chance, mixed; "+ …" true, "- …" false statements: each with
    // the answer as a blank ("wahr"/"falsch", with wf "w"/"f"), at least one of each where there can be
    private List<string> Pick(string arguments, string[] body, int line)
    {
        Match parts = Regex.Match(arguments, @"^(\d+)(?:\s+(wf))?$");
        var items = body.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (!parts.Success)
        {
            Error(line, "Expected \"::: pick 3\" (how many of the lines; \"::: pick 3 wf\": answers w/f).");
            return [];
        }
        int count = int.Parse(parts.Groups[1].Value, CultureInfo.InvariantCulture);
        bool judged = items.Count > 0 && items.All(l => l.StartsWith("+ ", StringComparison.Ordinal) || l.StartsWith("- ", StringComparison.Ordinal));
        if (count > items.Count)
        {
            Error(line, $"::: pick {count}, but it has only {items.Count} lines.");
            count = items.Count;
        }
        List<string> chosen;
        if (judged && count >= 2)
        {
            List<string> right = items.Where(l => l[0] == '+').ToList(), wrong = items.Where(l => l[0] == '-').ToList();
            // how many false ones: at least one of each where there are
            int min = Math.Max(right.Count > 0 ? 1 : 0, count - right.Count), max = Math.Min(wrong.Count, count - (right.Count > 0 ? 1 : 0));
            int falseOnes = min <= max ? random.Next(min, max + 1) : Math.Min(wrong.Count, count);
            chosen = Shuffled(TakeRandom(right, count - falseOnes).Concat(TakeRandom(wrong, falseOnes)).Select(Value.Of).ToList()).Select(v => v.Text!).ToList();
        }
        else
        {
            chosen = TakeRandom(items, count);
        }
        string yes = parts.Groups[2].Success ? "w" : "wahr", no = parts.Groups[2].Success ? "f" : "falsch";
        return chosen.Select(item => judged
            ? "- " + Interpolate(item[2..], line) + " [[" + (item[0] == '+' ? yes : no) + "]]"
            : "- " + Interpolate(item.StartsWith("- ", StringComparison.Ordinal) || item.StartsWith("* ", StringComparison.Ordinal) ? item[2..] : item, line)).ToList();
    }

    private List<string> TakeRandom(List<string> list, int count) =>
        Enumerable.Range(0, list.Count).OrderBy(_ => random.Next()).Take(count).Select(k => list[k]).ToList();

    // ------------------------------------------------------------------ {{…}}

    [GeneratedRegex(@"\{\{(.*?)\}\}")]
    private static partial Regex Placeholder();

    // "{{…}}" in a line: the value of the expression; "{{alt: A | B}}": the group's alternative
    private string Interpolate(string line, int number) => Placeholder().Replace(line, m =>
    {
        string inside = m.Groups[1].Value.Trim();
        try
        {
            return inside.StartsWith("alt:", StringComparison.Ordinal) ? Alternative(inside[4..]).ToString() : Evaluate(inside).ToString();
        }
        catch (FormatException e)
        {
            // the old way of a solution
            Error(number, inside.StartsWith("sol ", StringComparison.Ordinal)
                ? $"{{{{{inside}}}}}: a solution is written [[answer]] (a blank) or [[sol: …]] (only the solution) now."
                : $"{{{{{inside}}}}}: {e.Message}");
            return "??";
        }
    });

    // ------------------------------------------------------------------ expressions

    /// <summary>
    /// An expression: numbers, "texts", names of values, + − · / % ^, (…), list[1] (from 1) and the functions random(a, b), bin, hex,
    /// round, floor, ceil, abs, sqrt, min, max, gcd, lcm, sum, count, sorted, reversed, shuffle, join, first, last.
    /// </summary>
    internal Value Evaluate(string expression)
    {
        var parser = new ExpressionParser(this, expression);
        Value value = parser.Expression();
        parser.End();
        return value;
    }

    private sealed class ExpressionParser(SheetScript script, string text)
    {
        private int position;

        private char Peek()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }
            return position < text.Length ? text[position] : '\0';
        }

        private bool Take(char c)
        {
            if (Peek() != c)
            {
                return false;
            }
            position++;
            return true;
        }

        public void End()
        {
            if (Peek() != '\0')
            {
                throw new FormatException($"Unexpected \"{text[position..]}\".");
            }
        }

        public Value Expression()
        {
            Value value = Term();
            while (true)
            {
                if (Take('+'))
                {
                    Value right = Term();
                    value = value.Number != null && right.Number != null ? Value.Of(value.Number.Value + right.Number.Value) : Value.Of(value.ToString() + right);
                }
                else if (Take('-'))
                {
                    value = Value.Of(value.AsNumber() - Term().AsNumber());
                }
                else
                {
                    return value;
                }
            }
        }

        private Value Term()
        {
            Value value = Unary();
            while (true)
            {
                if (Take('*') || Take('·'))
                {
                    value = Value.Of(value.AsNumber() * Unary().AsNumber());
                }
                else if (Take('/') || Take(':'))
                {
                    double divisor = Unary().AsNumber();
                    value = divisor == 0 ? throw new FormatException("Division by 0.") : Value.Of(value.AsNumber() / divisor);
                }
                else if (Take('%'))
                {
                    // the remainder, never negative: -1 % 8 = 7
                    double divisor = Unary().AsNumber();
                    value = divisor == 0 ? throw new FormatException("Division by 0.") : Value.Of(((value.AsNumber() % divisor) + divisor) % divisor);
                }
                else
                {
                    return value;
                }
            }
        }

        private Value Unary()
        {
            if (Take('-'))
            {
                return Value.Of(-Unary().AsNumber());
            }
            Value value = Primary();
            return Take('^') ? Value.Of(Math.Pow(value.AsNumber(), Unary().AsNumber())) : value;
        }

        private Value Primary()
        {
            char c = Peek();
            Value value;
            if (Take('('))
            {
                value = Expression();
                Expect(')');
            }
            else if (c == '"')
            {
                int end = text.IndexOf('"', position + 1);
                if (end < 0)
                {
                    throw new FormatException("A text in quotes needs its closing \".");
                }
                value = Value.Of(text[(position + 1)..end]);
                position = end + 1;
            }
            else if (char.IsDigit(c) || c == '.')
            {
                int start = position;
                while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.'))
                {
                    position++;
                }
                value = Value.Of(double.Parse(text[start..position], CultureInfo.InvariantCulture));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                int start = position;
                while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
                {
                    position++;
                }
                string name = text[start..position];
                if (Take('('))
                {
                    var arguments = new List<Value>();
                    if (!Take(')'))
                    {
                        do
                        {
                            arguments.Add(Expression());
                        }
                        while (Take(','));
                        Expect(')');
                    }
                    value = Call(name, arguments);
                }
                else
                {
                    value = script.values.TryGetValue(name, out Value? known) ? known
                        : throw new FormatException($"There is no value \"{name}\" (set it in a ::: let block).");
                }
            }
            else
            {
                throw new FormatException(c == '\0' ? "The expression ends too early." : $"Unexpected \"{c}\".");
            }
            // list[1]: the first
            while (Take('['))
            {
                int index = (int)Expression().AsNumber();
                Expect(']');
                IReadOnlyList<Value> list = value.AsList();
                value = index >= 1 && index <= list.Count ? list[index - 1] : throw new FormatException($"[{index}]: the list has {list.Count} items.");
            }
            return value;
        }

        private void Expect(char c)
        {
            if (!Take(c))
            {
                throw new FormatException($"Expected \"{c}\".");
            }
        }

        private Value Call(string name, List<Value> a)
        {
            double N(int i) => i < a.Count ? a[i].AsNumber() : throw new FormatException($"{name} needs more arguments.");
            IReadOnlyList<Value> L() => a.Count == 1 ? a[0].AsList() : a;
            return name switch
            {
                // a whole number from … to (both included)
                "random" => Value.Of(script.random.Next((int)Math.Ceiling(N(0)), (int)Math.Floor(N(1)) + 1)),
                "bin" => Value.Of(Convert.ToString((long)N(0), 2).PadLeft(a.Count > 1 ? (int)N(1) : 0, '0')),
                "hex" => Value.Of(((long)N(0)).ToString("X", CultureInfo.InvariantCulture).PadLeft(a.Count > 1 ? (int)N(1) : 0, '0')),
                "round" => Value.Of(Math.Round(N(0), a.Count > 1 ? (int)N(1) : 0, MidpointRounding.AwayFromZero)),
                "floor" => Value.Of(Math.Floor(N(0))),
                "ceil" => Value.Of(Math.Ceiling(N(0))),
                "abs" => Value.Of(Math.Abs(N(0))),
                "sqrt" => Value.Of(Math.Sqrt(N(0))),
                "min" => Value.Of(L().Min(v => v.AsNumber())),
                "max" => Value.Of(L().Max(v => v.AsNumber())),
                "sum" => Value.Of(L().Sum(v => v.AsNumber())),
                "count" => Value.Of(L().Count),
                "gcd" => Value.Of(L().Select(v => (long)v.AsNumber()).Aggregate(Gcd)),
                "lcm" => Value.Of(L().Select(v => (long)v.AsNumber()).Aggregate((x, y) => x / Gcd(x, y) * y)),
                "sorted" => Value.Of(L().All(v => v.Number != null) ? L().OrderBy(v => v.Number).ToList() : L().OrderBy(v => v.ToString(), StringComparer.CurrentCulture).ToList()),
                "reversed" => Value.Of(L().Reverse().ToList()),
                "shuffle" => Value.Of(script.Shuffled(L().ToList())),
                "first" => L()[0],
                "last" => L()[^1],
                "join" => Value.Of(string.Join(a.Count > 1 ? a[1].ToString() : ", ", a[0].AsList().Select(v => v.ToString()))),
                _ => throw new FormatException($"There is no function \"{name}\" (random, bin, hex, round, floor, ceil, abs, sqrt, min, max, sum, count, gcd, lcm, sorted, reversed, shuffle, first, last, join)."),
            };
        }

        private static long Gcd(long x, long y) => y == 0 ? Math.Abs(x) : Gcd(y, x % y);
    }
}
