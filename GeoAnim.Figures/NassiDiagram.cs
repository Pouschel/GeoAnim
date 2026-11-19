using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GeoAnim.Figures;

/// <summary>A structure diagram as SVG (texts as paths: it needs no font), with its size in points.</summary>
public sealed record NassiImage(string Svg, double Width, double Height);

/// <summary>
/// How a structure diagram is drawn: the size of its text (pt), its boxes numbered instead of their texts (for tasks:
/// "which piece of the program goes where"), the words at the branches of a decision, with the answers in its blanks.
/// </summary>
public sealed record NassiOptions
{
    public double Size { get; init; } = 11;

    public bool Numbered { get; init; }

    public string True { get; init; } = "true";

    public string False { get; init; } = "false";

    public bool Solution { get; init; } = true;
}

/// <summary>
/// Structure diagrams (Nassi–Shneiderman) from lines like pseudocode: a line is an instruction; one that starts with
/// repeat, while, for (wiederhole, solange, für) a loop with the lines indented below it as its body; if (wenn) a
/// decision with the lines below it, "else" (sonst) at the same indent the other branch ("else if": a decision in it).
/// Code ("print(x);", "repeat (4)", `…`) in a font of fixed width, other text in the book's font; "[[4]]" a blank
/// (with the solutions the answer, red).
/// </summary>
public static partial class NassiDiagram
{
    private abstract record Node(int Line);

    private sealed record Statement(int Line, string Text) : Node(Line);

    private sealed record Loop(int Line, string Header, List<Node> Body) : Node(Line);

    private sealed record Decision(int Line, string Condition, bool Code, List<Node> Then, List<Node> Else) : Node(Line);

    private static readonly string[] LoopWords = ["repeat", "while", "for", "wiederhole", "solange", "für"];
    private static readonly string[] IfWords = ["if", "wenn"];
    private static readonly string[] ElseWords = ["else", "sonst"];

    /// <summary>
    /// The diagram of <paramref name="lines"/> (the block's lines; <paramref name="firstLine"/>: the number of the first in
    /// its file, for the errors).
    /// </summary>
    public static NassiImage Render(IReadOnlyList<string> lines, NassiOptions options, List<(int Line, string Message)> errors, int firstLine = 1)
    {
        var rows = new List<(int Indent, string Text, int Line)>();
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].TrimEnd();
            if (line.Trim().Length == 0)
            {
                continue;
            }
            int indent = 0;
            foreach (char c in line)
            {
                if (c == ' ')
                {
                    indent++;
                }
                else if (c == '\t')
                {
                    indent += 4;
                }
                else
                {
                    break;
                }
            }
            rows.Add((indent, line.Trim(), firstLine + i));
        }
        int at = 0;
        List<Node> nodes = rows.Count == 0 ? [] : Sequence(rows, ref at, rows[0].Indent, errors);
        while (at < rows.Count)
        {
            // less indented than the first line
            errors.Add((rows[at].Line, "This line is indented less than the first one of the diagram."));
            nodes.AddRange(Sequence(rows, ref at, rows[at].Indent, errors));
        }
        if (nodes.Count == 0)
        {
            errors.Add((firstLine, "A diagram needs lines: instructions, repeat/while/for, if/else."));
        }
        var drawing = new Drawing(options);
        if (options.Numbered)
        {
            int number = 0;
            nodes = Number(nodes, ref number);
        }
        return drawing.Draw(nodes);
    }

    private static string FirstWord(string text) => WordPattern().Match(text).Value.ToLowerInvariant();

    [GeneratedRegex(@"^[\p{L}_]+")]
    private static partial Regex WordPattern();

    // the lines at this indent, each with what is indented below it
    private static List<Node> Sequence(List<(int Indent, string Text, int Line)> rows, ref int at, int indent, List<(int, string)> errors)
    {
        var nodes = new List<Node>();
        while (at < rows.Count && rows[at].Indent >= indent)
        {
            (int own, string text, int line) = rows[at];
            if (own > indent)
            {
                errors.Add((line, "This line is indented more, but has no repeat/while/for/if/else above it."));
                Sequence(rows, ref at, own, errors);
                continue;
            }
            at++;
            string word = FirstWord(text);
            if (LoopWords.Contains(word))
            {
                nodes.Add(new Loop(line, text, Body(rows, ref at, indent, errors)));
            }
            else if (IfWords.Contains(word))
            {
                nodes.Add(Decide(rows, ref at, indent, text, line, errors));
            }
            else if (ElseWords.Contains(word))
            {
                errors.Add((line, $"\"{word}\" belongs right below an if (at its indent)."));
                Body(rows, ref at, indent, errors);
            }
            else
            {
                nodes.Add(new Statement(line, text));
            }
        }
        return nodes;
    }

    // what is indented below a line
    private static List<Node> Body(List<(int Indent, string Text, int Line)> rows, ref int at, int indent, List<(int, string)> errors) =>
        at < rows.Count && rows[at].Indent > indent ? Sequence(rows, ref at, rows[at].Indent, errors) : [];

    // "if (x > y)" with its lines, an "else" at its indent with its own ("else if …": a decision in it)
    private static Decision Decide(List<(int Indent, string Text, int Line)> rows, ref int at, int indent, string text, int line, List<(int, string)> errors)
    {
        string condition = text[FirstWord(text).Length..].Trim();
        bool code = IsCode(condition) || condition.StartsWith('(');
        if (condition.StartsWith('(') && condition.EndsWith(')') && Balanced(condition[1..^1]))
        {
            condition = condition[1..^1].Trim();
        }
        List<Node> then = Body(rows, ref at, indent, errors);
        List<Node> otherwise = [];
        if (at < rows.Count && rows[at].Indent == indent && ElseWords.Contains(FirstWord(rows[at].Text)))
        {
            (_, string elseText, int elseLine) = rows[at];
            at++;
            string rest = elseText[FirstWord(elseText).Length..].Trim();
            otherwise = IfWords.Contains(FirstWord(rest)) ? [Decide(rows, ref at, indent, rest, elseLine, errors)] : Body(rows, ref at, indent, errors);
        }
        return new Decision(line, condition, code, then, otherwise);
    }

    private static bool Balanced(string text)
    {
        int depth = 0;
        foreach (char c in text)
        {
            depth += c == '(' ? 1 : c == ')' ? -1 : 0;
            if (depth < 0)
            {
                return false;
            }
        }
        return depth == 0;
    }

    // the boxes numbered in reading order (a loop's head, then its body; a decision's condition, then yes, then no)
    private static List<Node> Number(List<Node> nodes, ref int number)
    {
        var numbered = new List<Node>();
        foreach (Node node in nodes)
        {
            string own = (++number).ToString(CultureInfo.InvariantCulture);
            switch (node)
            {
                case Loop loop:
                    numbered.Add(new Loop(loop.Line, own, Number(loop.Body, ref number)));
                    break;
                case Decision decision:
                    List<Node> then = Number(decision.Then, ref number);
                    numbered.Add(new Decision(decision.Line, own, false, then, Number(decision.Else, ref number)));
                    break;
                default:
                    numbered.Add(new Statement(node.Line, own));
                    break;
            }
        }
        return numbered;
    }

    // code: ends with ";", "{" or "}", a comment ("// …"), or a word with parentheses ("print(x)", "repeat (4)")
    private static bool IsCode(string text) => CodePattern().IsMatch(text);

    [GeneratedRegex(@"[;{}]\s*$|^\s*//|^[\p{L}_]\w*\s*\(.*\)\s*[;{]?\s*$")]
    private static partial Regex CodePattern();

    // ------------------------------------------------------------------ drawing

    private sealed partial class Drawing(NassiOptions options)
    {
        private readonly double size = options.Size;
        private readonly StringBuilder svg = new();

        private double Pad => size * 0.5;

        private double Row => size * 1.8;

        // the left bar of a loop
        private double Bar => size * 1.6;

        private double Mono => size * 0.92;

        private double Small => size * 0.8;

        // a piece of a line: text, code (fixed width) or a blank (its answer)
        private sealed record Piece(string Text, bool Code, bool Blank);

        private static List<Piece> Pieces(string text, bool code)
        {
            var pieces = new List<Piece>();
            foreach (Match part in PiecePattern().Matches(text))
            {
                if (part.Groups[1].Success)
                {
                    pieces.Add(new Piece(part.Groups[1].Value.Trim(), true, true));
                }
                else if (part.Groups[2].Success)
                {
                    pieces.Add(new Piece(part.Groups[2].Value, true, false));
                }
                else
                {
                    pieces.Add(new Piece(part.Value, code, false));
                }
            }
            return pieces;
        }

        [GeneratedRegex(@"\[\[(.*?)\]\]|`([^`]*)`|(?:(?!\[\[|`).)+")]
        private static partial Regex PiecePattern();

        private double Width(Piece piece) => piece.Blank
            ? Math.Max(FigureFonts.Width(piece.Text, Mono, false, false, mono: true), FigureFonts.Width("0000", Mono, false, false, mono: true)) + size * 0.6
            : FigureFonts.Width(piece.Text, piece.Code ? Mono : size, false, false, piece.Code);

        private double TextWidth(string text, bool code) => Pieces(text, code).Sum(Width);

        // a line's text with its baseline at (x, y)
        private void Text(string text, bool code, double x, double y)
        {
            foreach (Piece piece in Pieces(text, code))
            {
                double width = Width(piece);
                if (piece.Blank)
                {
                    svg.Append($"<line x1=\"{N(x + size * 0.15)}\" y1=\"{N(y + size * 0.25)}\" x2=\"{N(x + width - size * 0.15)}\" y2=\"{N(y + size * 0.25)}\" stroke=\"#000\" stroke-width=\"0.6\"/>");
                    if (options.Solution)
                    {
                        double own = FigureFonts.Width(piece.Text, Mono, false, false, mono: true);
                        Path(FigureFonts.Outline(piece.Text, x + (width - own) / 2, y, Mono, false, false, mono: true), "#d32f2f");
                    }
                }
                else
                {
                    Path(FigureFonts.Outline(piece.Text, x, y, piece.Code ? Mono : size, false, false, piece.Code), "#000");
                }
                x += width;
            }
        }

        private void Path(string data, string color)
        {
            if (data.Length > 0)
            {
                svg.Append("<path d=\"").Append(data).Append("\" fill=\"").Append(color).Append("\" stroke=\"none\"/>");
            }
        }

        private void Line(double x1, double y1, double x2, double y2) =>
            svg.Append($"<line x1=\"{N(x1)}\" y1=\"{N(y1)}\" x2=\"{N(x2)}\" y2=\"{N(y2)}\"/>");

        private static string N(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

        private static bool CodeOf(string text) => IsCode(text);

        // ---------------------------------------------------------------- sizes

        private double MinWidth(List<Node> nodes) => nodes.Count == 0 ? size * 2 : nodes.Max(MinWidth);

        private double Height(List<Node> nodes) => nodes.Count == 0 ? Row : nodes.Sum(Height);

        private double MinWidth(Node node) => node switch
        {
            // (numbered boxes stand for pieces of a program: room as for one)
            Statement statement => Math.Max(TextWidth(statement.Text, CodeOf(statement.Text)) + 2 * Pad, options.Numbered ? size * 6 : 0),
            Loop loop => Math.Max(TextWidth(loop.Header, CodeOf(loop.Header)) + 2 * Pad, Bar + MinWidth(loop.Body)),
            Decision decision => Math.Max(
                Math.Max(MinWidth(decision.Then), LabelWidth(options.True)) + Math.Max(MinWidth(decision.Else), LabelWidth(options.False)),
                (TextWidth(decision.Condition, decision.Code) + 2 * Pad) / 0.6),
            _ => 0,
        };

        // the words at a decision's branches: true/false as code (the language's constants), others as text
        private static bool CodeLabel(string label) => label is "true" or "false";

        private double LabelSize(string label) => CodeLabel(label) ? Small * 0.92 : Small;

        private double LabelWidth(string label) => FigureFonts.Width(label, LabelSize(label), false, false, CodeLabel(label)) + 2 * Pad;

        private void Label(string label, double x, double y, bool right)
        {
            double width = FigureFonts.Width(label, LabelSize(label), false, false, CodeLabel(label));
            Path(FigureFonts.Outline(label, right ? x - width : x, y, LabelSize(label), false, false, CodeLabel(label)), "#000");
        }

        private double Height(Node node) => node switch
        {
            Loop loop => Row + Height(loop.Body),
            Decision decision => 2 * Row + Math.Max(Height(decision.Then), Height(decision.Else)),
            _ => Row,
        };

        // ---------------------------------------------------------------- drawing

        public NassiImage Draw(List<Node> nodes)
        {
            double margin = 1;
            double width = MinWidth(nodes), height = Height(nodes);
            Draw(nodes, margin, margin, width, height);
            string content = svg.ToString();
            double totalWidth = width + 2 * margin, totalHeight = height + 2 * margin;
            string picture = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(totalWidth)}pt\" height=\"{N(totalHeight)}pt\" viewBox=\"0 0 {N(totalWidth)} {N(totalHeight)}\">"
                + $"<rect x=\"0\" y=\"0\" width=\"{N(totalWidth)}\" height=\"{N(totalHeight)}\" fill=\"#fff\"/>"
                + $"<g stroke=\"#000\" stroke-width=\"0.8\" stroke-linecap=\"square\" fill=\"none\">{content}</g></svg>";
            return new NassiImage(picture, totalWidth, totalHeight);
        }

        // a sequence in the box (x, y, w, h): its last one takes what is left
        private void Draw(List<Node> nodes, double x, double y, double w, double h)
        {
            if (nodes.Count == 0)
            {
                Rectangle(x, y, w, h);
                return;
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                double own = i == nodes.Count - 1 ? h : Height(nodes[i]);
                Draw(nodes[i], x, y, w, own);
                y += own;
                h -= own;
            }
        }

        private void Rectangle(double x, double y, double w, double h) =>
            svg.Append($"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\"/>");

        // the baseline of a line of text in a row starting at y
        private double Baseline(double y) => y + Row / 2 + size * 0.33;

        private void Draw(Node node, double x, double y, double w, double h)
        {
            switch (node)
            {
                case Statement statement:
                    Rectangle(x, y, w, h);
                    Text(statement.Text, CodeOf(statement.Text), x + Pad, Baseline(y));
                    break;
                case Loop loop:
                    Rectangle(x, y, w, h);
                    Text(loop.Header, CodeOf(loop.Header), x + Pad, Baseline(y));
                    Draw(loop.Body, x + Bar, y + Row, w - Bar, h - Row);
                    break;
                case Decision decision:
                    {
                        Rectangle(x, y, w, 2 * Row);
                        double left = Math.Max(MinWidth(decision.Then), LabelWidth(options.True));
                        double right = Math.Max(MinWidth(decision.Else), LabelWidth(options.False));
                        double split = left + (w - left - right) * left / (left + right);
                        Line(x, y, x + split, y + 2 * Row);
                        Line(x + w, y, x + split, y + 2 * Row);
                        // the condition between the slopes, the words in the corners below them
                        double t = 0.4;
                        double from = x + split * t, to = x + w - (w - split) * t;
                        double conditionWidth = TextWidth(decision.Condition, decision.Code);
                        Text(decision.Condition, decision.Code, (from + to - conditionWidth) / 2, Baseline(y));
                        Label(options.True, x + Pad * 0.6, y + 2 * Row - size * 0.35, right: false);
                        Label(options.False, x + w - Pad * 0.6, y + 2 * Row - size * 0.35, right: true);
                        Draw(decision.Then, x, y + 2 * Row, split, h - 2 * Row);
                        Draw(decision.Else, x + split, y + 2 * Row, w - split, h - 2 * Row);
                        break;
                    }
            }
        }
    }
}
