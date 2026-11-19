using System.Globalization;
using System.Reflection;
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
    private readonly string? groupName;
    private readonly IReadOnlyList<string> generators;
    private readonly SheetOptions options;
    private List<Assembly>? assemblies;
    private readonly List<MarkdownProblem> errors;

    private SheetScript(int seed, int group, string? groupName, IReadOnlyList<string> generators, List<MarkdownProblem> errors, SheetOptions options)
    {
        this.options = options;
        // every group its own chance; the same group always the same
        random = new Random(unchecked(seed * 7919 + group * 104729 + 17));
        this.group = group;
        this.groupName = groupName;
        this.generators = generators;
        this.errors = errors;
    }

    // the generators' assemblies (built and loaded when first used)
    private List<Assembly> Assemblies => assemblies ??= generators.Select(SheetGenerators.Load).ToList();

    /// <summary>
    /// The page for the group with the number <paramref name="group"/> (from 0): its "::: let" and "::: pick" blocks and
    /// "{{…}}" worked out. <paramref name="seed"/>: the head's (the same seed, the same sheets); <paramref name="generators"/>:
    /// the C# projects whose classes it may use (their full paths).
    /// </summary>
    public static string Expand(string text, int seed, int group, List<MarkdownProblem> errors, string? groupName = null, IReadOnlyList<string>? generators = null,
        SheetOptions? options = null)
    {
        options ??= new SheetOptions();
        var script = new SheetScript(seed, group, groupName, generators ?? [], errors, options);
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
        bool skipping = false;
        for (int i = start; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }
            // a task only for some groups ("## Struktogramm {8} @A N"): for the others left out, to the next heading
            if (!fenced && Regex.IsMatch(line, @"^#{1,2}\s"))
            {
                skipping = false;
                if (TaskGroups().Match(line) is { Success: true } only)
                {
                    skipping = !script.IsFor(only.Groups[2].Value, i + 1);
                    line = only.Groups[1].Value;
                }
            }
            if (skipping)
            {
                result.Add("");
                continue;
            }
            Match block = fenced ? Match.Empty : BlockStart().Match(line);
            if (!block.Success)
            {
                // in code too ("{{…}}" in a program), but its fences as they are
                result.Add(fenced && !line.TrimStart().StartsWith("```", StringComparison.Ordinal) ? script.Interpolate(line, i + 1)
                    : fenced ? lines[i] : script.Choices(script.Interpolate(line, i + 1)));
                continue;
            }
            // to its ":::" (or the end)
            int close = i + 1;
            while (close < lines.Length && lines[close].Trim() != ":::")
            {
                close++;
            }
            string[] body = lines[(i + 1)..Math.Min(close, lines.Length)].Select(l => l.TrimEnd('\r')).ToArray();
            List<string> made = block.Groups[1].Value switch
            {
                "let" => script.Let(body, i + 2),
                "pick" => script.Pick(block.Groups[2].Value, body, i + 1).Select(script.Choices).ToList(),
                "match" => script.MatchBlock(block.Groups[2].Value, body, i + 1),
                _ => script.Choice(block.Groups[2].Value, body, i + 1),
            };
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

    [GeneratedRegex(@"^\s*:::\s*(let|pick|match|choice)\b\s*(.*?)\s*$")]
    private static partial Regex BlockStart();

    // "## Titel {3} @A N": the heading and the groups it is for ("@-": none, put aside)
    [GeneratedRegex(@"^(#{1,2}\s.*?)\s+@((?:[\p{L}\p{N}_-]+[\s,]*)+)$")]
    private static partial Regex TaskGroups();

    // whether a task "@A N" is for this sheet's group
    private bool IsFor(string list, int line)
    {
        string[] names = list.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (names.Contains("-"))
        {
            return false;
        }
        foreach (string name in names.Where(n => !options.Groups.Contains(n)))
        {
            Error(line, options.Groups.Count == 0
                ? $"@{name}: the head has no groups (groups: [U, A, N])."
                : $"@{name}: there is no group \"{name}\" (groups: {string.Join(", ", options.Groups)}).");
        }
        return groupName == null || names.Contains(groupName);
    }

    // ------------------------------------------------------------------ choices in blanks

    // a blank with choices, the right ones with "*" ("[[*Number | String | kein gültiger Datentyp]]"): its choices
    // mixed by the group's chance, unless "{ordered}" follows
    [GeneratedRegex(@"\[\[((?:[^\]\n]|\](?!\]))*?)\]\](\{[^}\n]*\})?")]
    private static partial Regex BlankPattern();

    private string Choices(string line) => BlankPattern().Replace(line, m =>
    {
        List<string> parts = SplitChoices(m.Groups[1].Value);
        if (!parts.Any(p => p.TrimStart().StartsWith('*')) || m.Groups[2].Value.Contains("ordered", StringComparison.Ordinal))
        {
            return m.Value;
        }
        return "[[" + string.Join(" | ", Shuffled(parts.Select(p => Value.Of(p.Trim())).ToList()).Select(v => v.Text)) + "]]" + m.Groups[2].Value;
    });

    /// <summary>The parts of a blank between "|" (not "\|").</summary>
    internal static List<string> SplitChoices(string content)
    {
        var parts = new List<string>();
        int start = 0;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\\' && i + 1 < content.Length)
            {
                i++;
            }
            else if (content[i] == '|')
            {
                parts.Add(content[start..i]);
                start = i + 1;
            }
        }
        parts.Add(content[start..]);
        return parts;
    }

    // ------------------------------------------------------------------ ::: match, ::: choice

    // "::: match" (or "::: match all": both sides mixed in ILIAS; else only the right one): lines "Definition -> Term";
    // "-> Term" one that fits nothing. As a table in a block (data-pairs: how many pairs, for the points); for the
    // students the right column empty and the terms below, mixed
    private List<string> MatchBlock(string arguments, string[] body, int line)
    {
        if (arguments is not ("" or "all"))
        {
            Error(line, "Expected \"::: match\" (in ILIAS only the terms mixed) or \"::: match all\" (both sides).");
        }
        var rows = new List<(string Left, string Right)>();
        for (int k = 0; k < body.Length; k++)
        {
            string text = body[k].Trim();
            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }
            int arrow = text.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0 || text[(arrow + 2)..].Trim().Length == 0)
            {
                Error(line + 1 + k, $"Expected \"Definition -> Term\" (or \"-> Term\": one that fits nothing), not \"{text}\".");
                continue;
            }
            rows.Add((Interpolate(text[..arrow].Trim(), line + 1 + k), Interpolate(text[(arrow + 2)..].Trim(), line + 1 + k)));
        }
        List<(string Left, string Right)> pairs = rows.Where(r => r.Left.Length > 0).ToList();
        if (pairs.Count == 0)
        {
            Error(line, "::: match needs pairs \"Definition -> Term\".");
        }
        // n:n: a definition with two terms, or a term for two definitions (in ILIAS with minus points)
        foreach (var twice in pairs.GroupBy(p => p.Left).Where(g => g.Count() > 1))
        {
            options.Warnings?.Add(new MarkdownProblem(line, 1, $"\"{twice.Key}\" has {twice.Count()} terms: an n:n matching (wrong pairs give minus points in ILIAS)."));
        }
        foreach (var twice in pairs.GroupBy(p => p.Right).Where(g => g.Count() > 1))
        {
            options.Warnings?.Add(new MarkdownProblem(line, 1, $"\"{twice.Key}\" is the term of {twice.Count()} definitions: an n:n matching (wrong pairs give minus points in ILIAS)."));
        }
        var made = new List<string>
        {
            $"<div class=\"match\" data-shuffle=\"{(arguments == "all" ? "all" : "terms")}\" data-pairs=\"{pairs.Count}\">",
            "",
            "| Definition | Term |",
            "|:---|:---|",
        };
        made.AddRange(rows.Select(r => "| " + r.Left + " | " + (options.Solution ? r.Right : "") + " |"));
        made.Add("");
        made.Add("</div>");
        if (!options.Solution)
        {
            made.Add("");
            made.Add("Zur Auswahl: " + string.Join(" · ", Shuffled(rows.Select(r => Value.Of(r.Right)).DistinctBy(v => v.Text).ToList()).Select(v => v.Text)));
        }
        return made;
    }

    // "::: choice": "+ …" the right answer, "- …" wrong ones (single choice); a list, mixed, the right one marked with the
    // solutions
    private List<string> Choice(string arguments, string[] body, int line)
    {
        if (arguments.Length > 0)
        {
            Error(line, "Expected \"::: choice\" (and \"+ …\"/\"- …\" lines).");
        }
        var items = new List<(bool Right, string Text)>();
        for (int k = 0; k < body.Length; k++)
        {
            string text = body[k].Trim();
            if (text.Length == 0)
            {
                continue;
            }
            if (!(text.StartsWith("+ ", StringComparison.Ordinal) || text.StartsWith("- ", StringComparison.Ordinal)))
            {
                Error(line + 1 + k, $"Expected \"+ right answer\" or \"- wrong answer\" in ::: choice, not \"{text}\".");
                continue;
            }
            items.Add((text[0] == '+', Interpolate(text[2..].Trim(), line + 1 + k)));
        }
        int right = items.Count(i => i.Right);
        if (right != 1 || items.Count < 2)
        {
            Error(line, right == 1 ? "::: choice needs wrong answers too (\"- …\")." : $"::: choice needs exactly one right answer (\"+ …\"), not {right}.");
        }
        var order = Shuffled(Enumerable.Range(0, items.Count).Select(k => Value.Of(k)).ToList()).Select(v => (int)v.Number!.Value).ToList();
        var made = new List<string> { "<div class=\"single-choice\">", "" };
        made.AddRange(order.Select(k => "- " + (!options.Solution ? "<span class=\"choice-box\"></span>"
            : items[k].Right ? "<span class=\"choice-right\"></span>" : "<span class=\"choice-wrong\"></span>") + items[k].Text));
        made.Add("");
        made.Add("</div>");
        return made;
    }

    private void Error(int line, string message) => errors.Add(new MarkdownProblem(line, 1, message));

    // an object of a generator's class for the group, with its own chance (from the group's)
    private object Create(Type type) => SheetGenerators.Create(type, new Random(random.Next()), group, groupName);

    // ------------------------------------------------------------------ values

    /// <summary>A number, a text or a list of them – or an object of a generator.</summary>
    internal sealed record Value(double? Number, string? Text, IReadOnlyList<Value>? List, object? Object = null)
    {
        public static Value Of(double number) => new(number, null, null);

        public static Value Of(string text) => new(null, text, null);

        public static Value Of(IReadOnlyList<Value> list) => new(null, null, list);

        public double AsNumber() => Number ?? (Text != null && double.TryParse(Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
            ? n : throw new FormatException($"\"{this}\" is no number."));

        public IReadOnlyList<Value> AsList() => List ?? [this];

        // numbers as in German: 2,5; whole ones without a comma; lists with ", "
        public override string ToString() => Number is double n ? Format(n) : Text ?? (List != null ? string.Join(", ", List.Select(v => v.ToString())) : Object?.ToString() ?? "");

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
            Match definition = Regex.Match(line, @"^([\p{L}_][\p{L}\p{N}_]*(?:\.[\p{L}_][\p{L}\p{N}_]*)*)\s*=\s*(.+)$");
            if (!definition.Success)
            {
                Error(firstLine + i, $"Expected \"name = …\" in ::: let, not \"{line}\".");
                continue;
            }
            try
            {
                string name = definition.Groups[1].Value;
                int dot = name.LastIndexOf('.');
                if (dot < 0)
                {
                    values[name] = Define(definition.Groups[2].Value.Trim());
                }
                else
                {
                    // "bc.Count = 5": a property or field of a generator's object
                    Value target = Evaluate(name[..dot]);
                    SheetGenerators.Set(target.Object?.GetType() ?? throw new FormatException($"{name[..dot]} is no object of a generator."),
                        target.Object, name[(dot + 1)..], Define(definition.Groups[2].Value.Trim()));
                }
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
        catch (FormatException) when (!Regex.IsMatch(text, @"[\^*/()\[\]]") && !Regex.IsMatch(text, @"^[\p{L}_][\p{L}\p{N}_]*(\.[\p{L}_][\p{L}\p{N}_]*)+$"))
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
        string choice = choices[group % choices.Count];
        // a value, else the text as it is (code: "turn("left");")
        try
        {
            return Evaluate(choice);
        }
        catch (FormatException)
        {
            return Value.Of(choice);
        }
    }

    // ------------------------------------------------------------------ ::: pick

    // "::: pick 3" (or "::: pick 3 wf", "::: pick 3 ja|nein"; without a number: all): 3 of the lines, by chance, mixed;
    // "+ …" true, "- …" false statements: each with the answer as a blank ("wahr"/"falsch", with wf "w"/"f"; for ILIAS a
    // choice of the two), at least one of each where there can be
    private List<string> Pick(string arguments, string[] body, int line)
    {
        Match parts = Regex.Match(arguments, @"^(\d+)?\s*(wf|[^|\s]+\s*\|\s*[^|\s]+)?$");
        var items = body.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (!parts.Success)
        {
            Error(line, "Expected \"::: pick 3\" (how many of the lines, without: all; \"::: pick 3 wf\": answers w/f; \"::: pick 3 ja|nein\": these).");
            return [];
        }
        int count = parts.Groups[1].Success ? int.Parse(parts.Groups[1].Value, CultureInfo.InvariantCulture) : items.Count;
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
        string[] words = !parts.Groups[2].Success ? ["wahr", "falsch"] : parts.Groups[2].Value == "wf" ? ["w", "f"]
            : parts.Groups[2].Value.Split('|').Select(w => w.Trim()).ToArray();
        string yes = words[0], no = words[1];
        // for ILIAS a choice of the two (in their order), on paper a line for the answer
        string Answer(bool right) => options.Ilias ? (right ? $"[[*{yes} | {no}]]{{ordered}}" : $"[[{yes} | *{no}]]{{ordered}}") : "[[" + (right ? yes : no) + "]]";
        return chosen.Select(item => judged
            ? "- " + Interpolate(item[2..], line) + " " + Answer(item[0] == '+')
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
                if (Peek() == '(')
                {
                    value = Call(name, Arguments()!);
                }
                else if (script.values.TryGetValue(name, out Value? known))
                {
                    value = known;
                }
                else
                {
                    value = Generator(name, start);
                }
            }
            else
            {
                throw new FormatException(c == '\0' ? "The expression ends too early." : $"Unexpected \"{c}\".");
            }
            while (true)
            {
                if (Take('['))
                {
                    // list[1]: the first
                    int index = (int)Expression().AsNumber();
                    Expect(']');
                    IReadOnlyList<Value> list = value.AsList();
                    value = index >= 1 && index <= list.Count ? list[index - 1] : throw new FormatException($"[{index}]: the list has {list.Count} items.");
                }
                else if (Name() is { } member)
                {
                    // bc.Query, bc.GetBin("a1", 4): a member of a generator's object
                    value = value.Object is { } target
                        ? SheetGenerators.ValueOf(SheetGenerators.Member(target.GetType(), target, member, Arguments()))
                        : throw new FormatException($"\"{value}\" is no object of a generator: it has no .{member}.");
                }
                else
                {
                    return value;
                }
            }
        }

        // ".name" (after a value), else null and nothing read
        private string? Name()
        {
            if (Peek() != '.' || position + 1 >= text.Length || !(char.IsLetter(text[position + 1]) || text[position + 1] == '_'))
            {
                return null;
            }
            int start = ++position;
            while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
            {
                position++;
            }
            return text[start..position];
        }

        // "(a, b)", else null (no parentheses)
        private List<Value>? Arguments()
        {
            if (!Take('('))
            {
                return null;
            }
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
            return arguments;
        }

        // "Info10.BinDec" (a new object of the class, for the group), "Info10.BinDec.Query" (a static member, else that
        // of a new object), "BinDec" (when only one class has the name)
        private Value Generator(string first, int start)
        {
            var names = new List<string> { first };
            var ends = new List<int> { position };
            while (Name() is { } next)
            {
                names.Add(next);
                ends.Add(position);
            }
            if (script.generators.Count == 0)
            {
                throw new FormatException(names.Count > 1
                    ? $"There is no value \"{first}\" – and no generators for \"{string.Join(".", names)}\" (generators: in the head)."
                    : $"There is no value \"{first}\" (set it in a ::: let block).");
            }
            // the longest start that is a class
            for (int k = names.Count; k >= 1; k--)
            {
                if (SheetGenerators.FindType(script.Assemblies, string.Join(".", names.Take(k))) is not { } type)
                {
                    continue;
                }
                // the rest again, as members
                position = ends[k - 1];
                if (Name() is not { } member)
                {
                    return new Value(null, null, null, script.Create(type));
                }
                List<Value>? arguments = Arguments();
                bool isStatic = type.GetMember(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.FlattenHierarchy).Length > 0;
                return SheetGenerators.ValueOf(SheetGenerators.Member(type, isStatic ? null : script.Create(type), member, arguments));
            }
            position = ends[^1];
            throw new FormatException(names.Count == 1
                ? $"There is no value \"{first}\" (set it in a ::: let block) and no class of the generators of that name."
                : $"There is no class \"{string.Join(".", names.Take(names.Count - 1))}\" (or \"{string.Join(".", names)}\") in the generators.");
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

/// <summary>
/// How a sheet is made beyond its group: the head's groups (for "@A N" at tasks), with the solutions or for the
/// students, for ILIAS (true/false as a choice), where warnings go.
/// </summary>
internal sealed record SheetOptions
{
    public IReadOnlyList<string> Groups { get; init; } = [];

    public bool Solution { get; init; } = true;

    public bool Ilias { get; init; }

    public List<MarkdownProblem>? Warnings { get; init; }
}
