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

    /// <summary>A title above the diagram (bold, on its left), or none.</summary>
    public string? Title { get; init; }
}

/// <summary>
/// Structure diagrams (Nassi–Shneiderman) from lines like pseudocode: a line is an instruction; one that starts with
/// repeat, while or for a loop with the lines indented below it as its body; if a decision with the lines below it,
/// "else" at the same indent the other branch ("else if": a decision in it); break and continue a jump out (the box of
/// DIN 66261 with the arrow on its left). With "#" the word only says the kind of box, the text after it is what the box
/// shows: "#repeat wiederhole 5-mal", "#if x kleiner als 3", "#break Schleife verlassen". Code ("print(x);", a line
/// with repeat/if/break, `…`) in a font of fixed width, other text in the book's font; "[[4]]" a blank (with the
/// solutions the answer, red). The boxes coloured by their kind (statements, loops, decisions, jumps, empty branches).
/// </summary>
public static partial class NassiDiagram
{
    private abstract record Node(int Line);

    private sealed record Statement(int Line, string Text) : Node(Line);

    private sealed record Loop(int Line, string Header, bool Code, List<Node> Body) : Node(Line);

    private sealed record Decision(int Line, string Condition, bool Code, List<Node> Then, List<Node> Else) : Node(Line);

    // break, continue: out of the loop / to its next round
    private sealed record Jump(int Line, string Text, bool Code) : Node(Line);

    private static readonly string[] LoopWords = ["repeat", "while", "for"];
    private static readonly string[] JumpWords = ["break", "continue"];

    // a line's kind ("#repeat …": only the kind, the box shows the rest) and what the box shows
    private static (string Word, string Shown, bool Code) Kind(string text)
    {
        bool hidden = text.StartsWith('#');
        string body = hidden ? text[1..].TrimStart() : text;
        string word = FirstWord(body);
        if (!hidden)
        {
            return (word, text, true);
        }
        string rest = body[word.Length..].Trim();
        return (word, rest, IsCode(rest));
    }

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

    // "break;", "continue", "break [[…]];"
    [GeneratedRegex(@"^(break|continue)\b[^\w]*")]
    private static partial Regex JumpLine();

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
            (string word, string shown, bool code) = Kind(text);
            if (LoopWords.Contains(word))
            {
                nodes.Add(new Loop(line, shown, code, Body(rows, ref at, indent, errors)));
            }
            else if (word == "if")
            {
                nodes.Add(Decide(rows, ref at, indent, text, line, errors));
            }
            else if (word == "else")
            {
                errors.Add((line, "\"else\" belongs right below an if (at its indent)."));
                Body(rows, ref at, indent, errors);
            }
            else if (JumpWords.Contains(word) && (text.StartsWith('#') || JumpLine().IsMatch(text)))
            {
                nodes.Add(new Jump(line, shown, code));
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

    // "if (x > y)" with its lines, an "else" at its indent with its own ("else if …": a decision in it); in the triangle
    // what follows "if", as written (with or without parentheses: sometimes they are the task); "#if Text": the text
    private static Decision Decide(List<(int Indent, string Text, int Line)> rows, ref int at, int indent, string text, int line, List<(int, string)> errors)
    {
        bool hidden = text.StartsWith('#');
        string condition = (hidden ? text[1..].TrimStart() : text)[2..].Trim();
        bool code = !hidden || IsCode(condition);
        List<Node> then = Body(rows, ref at, indent, errors);
        List<Node> otherwise = [];
        if (at < rows.Count && rows[at].Indent == indent && Kind(rows[at].Text).Word == "else")
        {
            (_, string elseText, int elseLine) = rows[at];
            at++;
            // "else if …", "else #if …", "#else if …"
            bool hiddenElse = elseText.StartsWith('#');
            string rest = (hiddenElse ? elseText[1..].TrimStart() : elseText)[4..].Trim();
            if (hiddenElse && FirstWord(rest) == "if")
            {
                rest = "#" + rest;
            }
            otherwise = Kind(rest).Word == "if" ? [Decide(rows, ref at, indent, rest, elseLine, errors)] : Body(rows, ref at, indent, errors);
        }
        return new Decision(line, condition, code, then, otherwise);
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
                    numbered.Add(new Loop(loop.Line, own, false, Number(loop.Body, ref number)));
                    break;
                case Jump jump:
                    numbered.Add(new Jump(jump.Line, own, false));
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
    // (a line with repeat/while/for/if/break is code anyway)
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

        // the arrow of a jump (on its left)
        private double Tip => size * 0.9;

        // the title above the diagram
        private double TitleSize => size * 1.05;

        private double TitleRow => size * 1.9;

        private string? Title => string.IsNullOrWhiteSpace(options.Title) ? null : options.Title.Trim();

        // the colours of the boxes (light: the text stays readable, also printed)
        private const string StatementFill = "#fdf1dd";
        private const string LoopFill = "#e6f3ec";
        private const string DecisionFill = "#fbe4df";
        private const string JumpFill = "#e9e2f5";
        private const string EmptyFill = "#fffdf2";

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
            Jump jump => Math.Max(Tip + TextWidth(jump.Text, jump.Code) + 2 * Pad, options.Numbered ? size * 6 : 0),
            Loop loop => Math.Max(TextWidth(loop.Header, loop.Code) + 2 * Pad, Bar + MinWidth(loop.Body)),
            Decision decision => Math.Max(
                Math.Max(MinWidth(decision.Then), LabelWidth(options.True)) + Math.Max(MinWidth(decision.Else), LabelWidth(options.False)),
                (TextWidth(decision.Condition, decision.Code) + 2 * Pad) / 0.6),
            _ => 0,
        };

        // the words at a decision's branches: true/false as code (the language's constants), others as text
        private static bool CodeLabel(string label) => label is "true" or "false";

        private double LabelSize(string label) => CodeLabel(label) ? Small * 0.92 : Small;

        // (a side at least so wide that the slope above passes the word in its corner)
        private double LabelWidth(string label) => (FigureFonts.Width(label, LabelSize(label), false, false, CodeLabel(label)) + 2 * Pad) * 1.25;

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
            // a title: on the left above the diagram
            double top = 0;
            if (Title != null)
            {
                width = Math.Max(width, FigureFonts.Width(Title, TitleSize, true, false));
                Path(FigureFonts.Outline(Title, margin, margin + TitleSize * 0.95, TitleSize, true, false), "#000");
                top = TitleRow;
            }
            Draw(nodes, margin, margin + top, width, height);
            height += top;
            string content = svg.ToString();
            double totalWidth = width + 2 * margin, totalHeight = height + 2 * margin;
            string picture = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{N(totalWidth)}pt\" height=\"{N(totalHeight)}pt\" viewBox=\"0 0 {N(totalWidth)} {N(totalHeight)}\">"
                + $"<rect x=\"0\" y=\"0\" width=\"{N(totalWidth)}\" height=\"{N(totalHeight)}\" fill=\"#fff\"/>"
                + $"<g stroke=\"#000\" stroke-width=\"0.8\" stroke-linecap=\"square\" fill=\"none\">{content}</g></svg>";
            return new NassiImage(picture, totalWidth, totalHeight);
        }

        // a sequence in the box (x, y, w, h): its last one takes what is left; nothing: an empty box (a branch of a
        // decision with nothing in it: ∅)
        private void Draw(List<Node> nodes, double x, double y, double w, double h, bool branch = false)
        {
            if (nodes.Count == 0)
            {
                Rectangle(x, y, w, h, branch ? EmptyFill : StatementFill);
                if (branch)
                {
                    // ∅ (drawn: no font needed)
                    double r = size * 0.38, cx = x + w / 2, cy = y + Row / 2;
                    svg.Append($"<circle cx=\"{N(cx)}\" cy=\"{N(cy)}\" r=\"{N(r)}\"/>");
                    Line(cx - r * 1.25, cy + r * 1.25, cx + r * 1.25, cy - r * 1.25);
                }
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

        private void Rectangle(double x, double y, double w, double h, string fill) =>
            svg.Append($"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\" fill=\"{fill}\"/>");

        // the baseline of a line of text in a row starting at y
        private double Baseline(double y) => y + Row / 2 + size * 0.33;

        private void Draw(Node node, double x, double y, double w, double h)
        {
            switch (node)
            {
                case Statement statement:
                    Rectangle(x, y, w, h, StatementFill);
                    Text(statement.Text, CodeOf(statement.Text), x + Pad, Baseline(y));
                    break;
                case Jump jump:
                    {
                        // the box with an arrow pointing left (out): its tip in the middle of the box's left side
                        double tip = Math.Min(Tip, h / 2);
                        Rectangle(x, y, w, h, StatementFill);
                        svg.Append($"<path d=\"M{N(x + tip)} {N(y)} H{N(x + w)} V{N(y + h)} H{N(x + tip)} L{N(x)} {N(y + h / 2)} Z\" fill=\"{JumpFill}\"/>");
                        Text(jump.Text, jump.Code, x + tip + Pad, Baseline(y));
                        break;
                    }
                case Loop loop:
                    Rectangle(x, y, w, h, LoopFill);
                    Text(loop.Header, loop.Code, x + Pad, Baseline(y));
                    Draw(loop.Body, x + Bar, y + Row, w - Bar, h - Row);
                    break;
                case Decision decision:
                    {
                        Rectangle(x, y, w, 2 * Row, DecisionFill);
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
                        Draw(decision.Then, x, y + 2 * Row, split, h - 2 * Row, branch: true);
                        Draw(decision.Else, x + split, y + 2 * Row, w - split, h - 2 * Row, branch: true);
                        break;
                    }
            }
        }
    }
}
