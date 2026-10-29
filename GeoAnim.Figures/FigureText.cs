namespace GeoAnim.Figures;

/// <summary>A piece of a text in a figure: plain text (with its style) or a formula (AsciiMath).</summary>
internal sealed record TextPiece(string Text, bool IsFormula, bool Bold = false, bool Italic = false);

/// <summary>
/// The little Markdown of the texts in the picture, as the player reads it: **bold**, *italic* or _italic_, `code`,
/// [text](address), &lt;address&gt;; "\*" keeps a mark, "\" before a letter is not shown; formulas between $…$ ("\$" is a
/// dollar sign); a new line ("\n") starts a new line of the text.
/// </summary>
internal static class FigureText
{
    /// <summary>The lines of a text, each a list of pieces.</summary>
    public static List<List<TextPiece>> Lines(string text)
    {
        var items = new List<object>();
        foreach ((string run, bool math) in SplitMath(text))
        {
            if (math)
            {
                items.Add(new Formula(run));
            }
            else
            {
                items.AddRange(run.Select(c => (object)c));
            }
        }
        var lines = new List<List<TextPiece>> { new() };
        Flatten(Parse(items, 0, null)!.Value.Nodes, false, false, lines);
        return lines;
    }

    private sealed record Formula(string Source);

    private abstract record Node;

    private sealed record Plain(string Text) : Node;

    private sealed record Math(string Source) : Node;

    private sealed record Styled(string Type, List<Node> Children) : Node;

    // "Kreis um $A$" → ("Kreis um ", false), ("A", true)
    private static List<(string Text, bool Math)> SplitMath(string text)
    {
        var runs = new List<(string, bool)>();
        var current = new System.Text.StringBuilder();
        bool inFormula = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '$')
            {
                current.Append('$');
                i++;
            }
            else if (c == '$')
            {
                if (current.Length > 0)
                {
                    runs.Add((inFormula ? current.ToString().Trim() : current.ToString(), inFormula));
                }
                current.Clear();
                inFormula = !inFormula;
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0)
        {
            runs.Add((current.ToString(), inFormula));
        }
        return runs;
    }

    private static bool IsSpace(object? c) => c is null or ' ';

    private static bool IsWordChar(object? c) => c is char ch && char.IsLetterOrDigit(ch);

    private static bool StartsWith(List<object> items, int i, string mark) =>
        mark.Select((c, k) => i + k < items.Count && items[i + k] is char ch && ch == c).All(x => x);

    private static object? At(List<object> items, int i) => i >= 0 && i < items.Count ? items[i] : null;

    // the pieces from position i up to the closing mark (null: it does not close)
    private static (List<Node> Nodes, int End)? Parse(List<object> items, int i, string? closer)
    {
        var nodes = new List<Node>();
        var text = new System.Text.StringBuilder();
        void Flush()
        {
            if (text.Length > 0)
            {
                nodes.Add(new Plain(text.ToString()));
                text.Clear();
            }
        }
        while (i < items.Count)
        {
            object c = items[i];
            bool doubleStar = c is '*' && At(items, i + 1) is '*';
            if (closer != null && StartsWith(items, i, closer) && !IsSpace(At(items, i - 1))
                && !(closer == "*" && doubleStar && At(items, i + 2) != null) && !(closer == "_" && IsWordChar(At(items, i + closer.Length))))
            {
                Flush();
                return (nodes, i + closer.Length);
            }
            if (c is Formula formula)
            {
                Flush();
                nodes.Add(new Math(formula.Source));
                i++;
                continue;
            }
            char ch = (char)c;
            if (ch == '\\' && At(items, i + 1) is char next && "*_`[]<>{}\\".Contains(next))
            {
                text.Append(next);
                i += 2;
                continue;
            }
            if (ch == '\\' && At(items, i + 1) is char letter && char.IsLetter(letter))
            {
                i++;
                continue;
            }
            (Node Node, int End)? found = null;
            if (doubleStar)
            {
                found = Wrapped(items, i, "**", "strong");
            }
            else if (ch == '*')
            {
                found = Wrapped(items, i, "*", "em");
            }
            else if (ch == '_' && !IsWordChar(At(items, i - 1)))
            {
                found = Wrapped(items, i, "_", "em");
            }
            else if (ch == '`')
            {
                found = Code(items, i);
            }
            else if (ch == '[')
            {
                found = Link(items, i);
            }
            else if (ch == '<')
            {
                found = Autolink(items, i);
            }
            if (found is { } f)
            {
                Flush();
                nodes.Add(f.Node);
                i = f.End;
            }
            else
            {
                text.Append(ch);
                i++;
            }
        }
        if (closer != null)
        {
            return null;
        }
        Flush();
        return (nodes, i);
    }

    private static (Node, int)? Wrapped(List<object> items, int i, string mark, string type)
    {
        if (IsSpace(At(items, i + mark.Length)))
        {
            return null;
        }
        var inner = Parse(items, i + mark.Length, mark);
        return inner is { } found && found.Nodes.Count > 0 ? (new Styled(type, found.Nodes), found.End) : null;
    }

    private static (Node, int)? Code(List<object> items, int i)
    {
        var text = new System.Text.StringBuilder();
        for (int k = i + 1; k < items.Count; k++)
        {
            if (items[k] is '`')
            {
                return text.Length > 0 ? (new Styled("code", [new Plain(text.ToString())]), k + 1) : null;
            }
            text.Append(items[k] is char c ? c.ToString() : "$" + ((Formula)items[k]).Source + "$");
        }
        return null;
    }

    private static (Node, int)? Link(List<object> items, int i)
    {
        var label = Parse(items, i + 1, "]");
        if (label is not { } found || At(items, found.End) is not '(')
        {
            return null;
        }
        var href = new System.Text.StringBuilder();
        for (int k = found.End + 1; k < items.Count; k++)
        {
            if (items[k] is ')')
            {
                return href.Length > 0 ? (new Styled("link", found.Nodes), k + 1) : null;
            }
            if (items[k] is not char c || c == ' ')
            {
                return null;
            }
            href.Append(c);
        }
        return null;
    }

    private static (Node, int)? Autolink(List<object> items, int i)
    {
        var href = new System.Text.StringBuilder();
        for (int k = i + 1; k < items.Count; k++)
        {
            if (items[k] is '>')
            {
                string address = href.ToString();
                return address.IndexOfAny(['.', ':', '/']) >= 0 ? (new Styled("link", [new Plain(address)]), k + 1) : null;
            }
            if (items[k] is not char c || c == ' ' || c == '<')
            {
                return null;
            }
            href.Append(c);
        }
        return null;
    }

    private static void Flatten(List<Node> nodes, bool bold, bool italic, List<List<TextPiece>> lines)
    {
        foreach (Node node in nodes)
        {
            switch (node)
            {
                case Plain plain:
                    string[] parts = plain.Text.Split('\n');
                    for (int index = 0; index < parts.Length; index++)
                    {
                        if (index > 0)
                        {
                            lines.Add([]);
                        }
                        if (parts[index].Length > 0)
                        {
                            lines[^1].Add(new TextPiece(parts[index], false, bold, italic));
                        }
                    }
                    break;
                case Math math:
                    lines[^1].Add(new TextPiece(math.Source, true, bold, italic));
                    break;
                case Styled styled:
                    Flatten(styled.Children, bold || styled.Type == "strong", italic || styled.Type == "em", lines);
                    break;
            }
        }
    }
}
