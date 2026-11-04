using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.Mathematics;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace GeoAnim.Viewer;

/// <summary>
/// A mark on a page ("{#name}" at a figure, a statement or a heading): its kind ("figure", "section", "axiom",
/// "definition", "theorem", "lemma", "corollary", "proof", "example", "remark"), what a reference shows ("Abb. 3",
/// "Axiom 2", "Abschnitt 1.2"), a text to recognise it by (caption, title, heading or the start of the statement) and
/// where it stands.
/// </summary>
public sealed record PageMark(string Name, string Kind, string Label, string Text, int Line, int Column)
{
    /// <summary>The counter that numbers it (<see cref="BookNumbering.CounterOf"/>), "heading" for the page's heading, or null.</summary>
    public string? Counter { get; init; }

    /// <summary>Its place in its counter on the page (from 1).</summary>
    public int Index { get; init; }

    /// <summary>Its title (Markdown), which "§*name" shows after the label: a heading's text, a statement's title; else null.</summary>
    public string? Title { get; init; }
}

/// <summary>
/// A reference "§name" on a page: where it stands, the id of its link ("r-name-2": the second reference to "name" on the
/// page) and what it stands in, for the list of where a mark is used. <see cref="Context"/>: the statement or figure
/// (numbered; its label as on a page alone, <see cref="MarkdownPage.Renumbered"/> gives the book's), <see cref="Section"/>:
/// the page's heading; in a proof <see cref="Proof"/>, with the mark of the statement it proves in <see cref="ProofOf"/>
/// ("::: proof §satz-x") or, without one, the statement before it in <see cref="Context"/>.
/// </summary>
public sealed record PageReference(string Name, int Line, int Column, string Anchor)
{
    public PageMark? Context { get; init; }

    public PageMark? Section { get; init; }

    public bool Proof { get; init; }

    public string? ProofOf { get; init; }
}

/// <summary>A place where a mark is used: what it is ("Beweis von Satz 3.4", "Abschnitt 2.1"), on which page (full path), the id there and the line.</summary>
public sealed record MarkUse(string Label, string File, string Anchor, int Line, int Column);

/// <summary>The marks of a page and how many of each counter it has (for the numbers of the pages after it).</summary>
public sealed record MarksOfPage(IReadOnlyList<PageMark> Marks, IReadOnlyDictionary<string, int> Counts)
{
    /// <summary>The references of the page ("§name"), in their order.</summary>
    public IReadOnlyList<PageReference> References { get; init; } = [];

    /// <summary>The keywords of the page.</summary>
    public IReadOnlyList<PageKeywordDefinition> Keywords { get; init; } = [];

    /// <summary>The title of the page (front matter, else the first heading).</summary>
    public string? Title { get; init; }
}

/// <summary>A mark on another page of the book (its full path).</summary>
public sealed record BookMark(string File, PageMark Mark);

public sealed partial class MarkdownPage
{
    /// <summary>
    /// The kinds of statements ("::: axiom … :::") with their names on the page and whether they are numbered. "def" is
    /// short for "definition"; "box" is a plain box without a name (only its title, if it has one).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, (string Name, bool Numbered)> StatementKinds = new Dictionary<string, (string, bool)>
    {
        ["axiom"] = ("Axiom", true),
        ["definition"] = ("Definition", true),
        ["theorem"] = ("Satz", true),
        ["lemma"] = ("Lemma", true),
        ["corollary"] = ("Folgerung", true),
        ["proof"] = ("Beweis", false),
        ["example"] = ("Beispiel", true),
        ["remark"] = ("Bemerkung", false),
        ["box"] = ("", false),
    };

    private static readonly Dictionary<string, string> KindAliases = new() { ["def"] = "definition" };

    private const string FigureName = "Abb.";
    private const string SectionName = "Abschnitt";
    private const string ChapterName = "Kapitel";

    private const string HeadingCounter = "heading";

    /// <summary>A mark read with <see cref="ReadMarks"/>, with the numbers of its page in the book.</summary>
    public static PageMark Renumbered(PageMark mark, PageNumbering numbering) => mark.Counter switch
    {
        null => mark,
        HeadingCounter => mark with { Label = HeadingLabel(numbering, mark.Text) },
        string counter => mark with { Label = LabelOf(mark.Kind, numbering.NumberOf(counter, mark.Index)) },
    };

    // "Abb.", "Satz", …
    private static string NameOf(string kind) => kind == "figure" ? FigureName : StatementKinds[kind].Name;

    // what a number shows: "Satz 2.3", a formula's "(2.3)"
    private static string LabelOf(string kind, string number) => kind == "equation" ? "(" + number + ")" : NameOf(kind) + " " + number;

    // what a reference to a heading shows: the page's section ("Abschnitt 2.1"; the introduction of a chapter: "Kapitel 2"),
    // else its text in quotes
    private static string HeadingLabel(PageNumbering numbering, string text) =>
        numbering.Section != null && numbering.Book.Headings
            ? (numbering.Section.Contains('.') ? SectionName : ChapterName) + " " + numbering.Section
            : "„" + HtmlExporter.PlainText(text) + "“";

    // a statement as read: its kind, title (Markdown) and what references show; a proof "::: proof §satz-x": the mark of
    // the statement it proves, and when found its label and link
    private sealed record Statement(string Kind, string? Title, string Label)
    {
        public string? ProofOf { get; init; }

        public string? ProofLabel { get; init; }

        public string? ProofHref { get; init; }
    }

    // what a reference in a block stands in: the statement or figure (numbered, with or without a mark), in a proof the
    // statement before it (or the one named, ProofOf)
    private sealed record ReferenceContext(PageMark? Mark, bool Proof, string? ProofOf);

    // the kinds that are proved (a proof without "§name" belongs to the last of them before it)
    private static readonly HashSet<string> Provable = ["theorem", "lemma", "corollary"];

    /// <summary>
    /// What a place a mark is used at shows in the list under it: "Beweis von Satz 3.4" (in a proof), "Satz 3.4" (in a
    /// statement), "Abb. 2.1" (in a caption), "Abschnitt 2.1" (in the text: the page's heading), else the page's title.
    /// <paramref name="markOf"/>: a mark of the book by its name, with its numbers.
    /// </summary>
    public static string UseLabel(PageReference reference, PageNumbering numbering, Func<string, PageMark?> markOf, string pageTitle)
    {
        string? section = reference.Section is { } heading ? Renumbered(heading, numbering).Label : null;
        if (reference.Proof)
        {
            string? proved = reference.ProofOf != null ? markOf(reference.ProofOf)?.Label
                : reference.Context is { } statement ? Renumbered(statement, numbering).Label : null;
            return proved != null ? "Beweis von " + proved : "Beweis in " + (section ?? pageTitle);
        }
        return reference.Context is { } context ? Renumbered(context, numbering).Label : section ?? pageTitle;
    }

    /// <summary>
    /// Where the marks of a book are used: for every mark's name the places that refer to it, in book order. The pages (in
    /// book order) with their full paths, marks as read (<see cref="ReadMarks"/>), numbers and titles; <paramref name="marks"/>:
    /// the marks of the book by name, with their numbers (for "Beweis von Satz 3.2" of a proof "::: proof §satz-x").
    /// </summary>
    public static Dictionary<string, IReadOnlyList<MarkUse>> BookUses(IEnumerable<(string File, MarksOfPage Page, PageNumbering Numbering, string Title)> pages,
        IReadOnlyDictionary<string, PageMark> marks)
    {
        var uses = new Dictionary<string, List<MarkUse>>(StringComparer.Ordinal);
        foreach ((string file, MarksOfPage read, PageNumbering numbering, string title) in pages)
        {
            foreach (PageReference reference in read.References)
            {
                string label = UseLabel(reference, numbering, name => marks.GetValueOrDefault(name), title);
                if (!uses.TryGetValue(reference.Name, out List<MarkUse>? list))
                {
                    uses[reference.Name] = list = [];
                }
                list.Add(new MarkUse(label, file, reference.Anchor, reference.Line, reference.Column));
            }
        }
        return uses.ToDictionary(u => u.Key, u => (IReadOnlyList<MarkUse>)u.Value, StringComparer.Ordinal);
    }

    /// <summary>The id of the link of the <paramref name="count"/>-th reference to a mark on a page ("r-name-2").</summary>
    public static string ReferenceAnchor(string name, int count) => "r-" + name + "-" + count.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ numbers and marks

    // numbers figures and statements in the order of the page, each with its counter (from the numbers before the page in
    // the book), puts the section's number in front of the page's heading and collects the marks; how many of each counter
    // the page has
    private static List<PageMark> Number(string text, MarkdownDocument document, Func<int, (int, int)> locate,
        List<MarkdownProblem> errors, PageNumbering numbering, out Dictionary<string, int> counts, out PageMark? section)
    {
        var marks = new List<PageMark>();
        section = null;
        // the last statement that a proof after it proves
        PageMark? provable = null;
        var counted = new Dictionary<string, int>(StringComparer.Ordinal);
        BookNumbering book = numbering.Book;
        string[] lines = text.Split('\n');
        bool pageHeading = false;
        // the next number of a kind: its place in its counter and the number shown
        (int Index, string Number) Next(string kind)
        {
            string counter = book.CounterOf(kind);
            int index = counted[counter] = counted.GetValueOrDefault(counter) + 1;
            return (index, numbering.NumberOf(counter, index));
        }
        foreach (Block block in document.Descendants<Block>().ToList())
        {
            switch (block)
            {
                case HeadingBlock heading:
                    {
                        // the page's heading (the first "#"): its section in front; the others ("##" …) have no numbers
                        bool first = heading.Level == 1 && !pageHeading;
                        pageHeading |= heading.Level == 1;
                        string headingText = Attributes().Replace(HeadingText(text, heading), "").Trim();
                        if (first && numbering.Section != null && book.Headings && heading.Inline != null)
                        {
                            var number = new HtmlInline("<span class=\"section-number\">" + numbering.Section + "</span> ");
                            if (heading.Inline.FirstChild is { } start)
                            {
                                start.InsertBefore(number);
                            }
                            else
                            {
                                heading.Inline.AppendChild(number);
                            }
                        }
                        if (first)
                        {
                            section = new PageMark(heading.TryGetAttributes()?.Id ?? "", "section", HeadingLabel(numbering, headingText), headingText,
                                heading.Line + 1, heading.Column + 1) { Counter = HeadingCounter, Title = headingText };
                        }
                        if (heading.TryGetAttributes()?.Id is { } id)
                        {
                            marks.Add((first
                                ? Mark(id, "section", HeadingLabel(numbering, headingText), headingText, heading) with { Counter = HeadingCounter }
                                : Mark(id, "section", HeadingLabel(PageNumbering.None, headingText), headingText, heading)) with { Title = first ? headingText : null });
                        }
                        break;
                    }
                case GeoFigureBlock figure:
                    {
                        (int index, string number) = Next("figure");
                        figure.Label = FigureName + " " + number;
                        // the mark at one of its images ("![](#2){#abb-gerade}")
                        string? id = null;
                        foreach (FigureItem item in figure.Items)
                        {
                            if (item.Image.TryGetAttributes()?.Id is not { } own)
                            {
                                continue;
                            }
                            if (id != null && id != own)
                            {
                                (int line, int column) = locate(item.Image.Span.Start);
                                errors.Add(new MarkdownProblem(line, column, $"This figure has two marks, \"{id}\" and \"{own}\"."));
                                continue;
                            }
                            id = own;
                        }
                        figure.SetData(typeof(ReferenceContext), new ReferenceContext(
                            Mark(id ?? "", "figure", figure.Label, "", figure) with { Counter = book.CounterOf("figure"), Index = index }, false, null));
                        if (id != null)
                        {
                            figure.GetAttributes().Id = id;
                            string caption = string.Join(" · ", figure.Items.Select(i => i.Reference?.Caption).Where(c => !string.IsNullOrEmpty(c)));
                            marks.Add(Mark(id, "figure", figure.Label, caption, figure) with { Counter = book.CounterOf("figure"), Index = index });
                        }
                        break;
                    }
                case CustomContainer container:
                    {
                        Statement? statement = StatementOf(container, lines, locate, errors);
                        if (statement == null)
                        {
                            break;
                        }
                        (string name, bool numbered) = StatementKinds[statement.Kind];
                        (int index, string number) = numbered ? Next(statement.Kind) : (0, "");
                        string label = numbered ? name + " " + number : name;
                        statement = statement with { Label = label };
                        container.SetData(typeof(Statement), statement);
                        // what a reference in it stands in: a numbered statement itself; a proof the statement it proves
                        if (numbered)
                        {
                            PageMark own = Mark(container.TryGetAttributes()?.Id ?? "", statement.Kind, label, statement.Title ?? "", container)
                                with { Counter = book.CounterOf(statement.Kind), Index = index, Title = statement.Title };
                            container.SetData(typeof(ReferenceContext), new ReferenceContext(own, false, null));
                            if (Provable.Contains(statement.Kind))
                            {
                                provable = own;
                            }
                        }
                        else if (statement.Kind == "proof")
                        {
                            container.SetData(typeof(ReferenceContext), new ReferenceContext(statement.ProofOf == null ? provable : null, true, statement.ProofOf));
                        }
                        if (container.TryGetAttributes()?.Id is { } id)
                        {
                            if (statement.Kind == "box")
                            {
                                // nothing a reference could show
                                errors.Add(new MarkdownProblem(container.Line + 1, container.Column + 1,
                                    $"A box has no number to refer to: put the mark \"{id}\" at the heading before it."));
                            }
                            else
                            {
                                PageMark mark = Mark(id, statement.Kind, label, statement.Title ?? StartOf(text, container), container) with { Title = statement.Title };
                                marks.Add(numbered ? mark with { Counter = book.CounterOf(statement.Kind), Index = index } : mark);
                            }
                        }
                        break;
                    }
            }
        }
        // formulas on lines of their own with a mark ("$$…$$ {#gl-name}"): numbered in the order of the page
        foreach (MarkdownObject element in document.Descendants())
        {
            (string? Id, string Source, Block? At, MarkdownObject? Formula) found = element switch
            {
                MathBlock block when block.TryGetAttributes()?.Id is { } blockId =>
                    (blockId, string.Join(" ", block.Lines.Lines.Take(block.Lines.Count).Select(l => l.Slice.ToString().Trim())).Trim(), block, block),
                MathInline { DelimiterCount: 2 } inline when inline.TryGetAttributes()?.Id is { } inlineId =>
                    (inlineId, inline.Content.ToString().Trim(), inline.Parent?.ParentBlock, inline),
                _ => ((string?)null, "", (Block?)null, (MarkdownObject?)null),
            };
            (string? id, string source, Block? at, MarkdownObject? formula) = found;
            if (id == null || formula == null)
            {
                continue;
            }
            (int index, string number) = Next("equation");
            string label = LabelOf("equation", number);
            formula.SetData(typeof(FormulaNumber), label);
            (int line, int column) = element is MathInline math ? locate(math.Span.Start) : (at!.Line + 1, at.Column + 1);
            marks.Add(new PageMark(id, "equation", label, source, line, column) { Counter = book.CounterOf("equation"), Index = index });
        }
        counts = counted;
        // a mark of one character: Markdig does not take it ("{#a}")
        foreach (Match single in SingleCharacterMark().Matches(text))
        {
            (int line, int column) = locate(single.Index);
            errors.Add(new MarkdownProblem(line, column, $"A mark needs at least two characters (\"{single.Value}\")."));
        }
        // a mark twice on the page
        foreach (IGrouping<string, PageMark> twice in marks.GroupBy(m => m.Name).Where(g => g.Count() > 1))
        {
            PageMark first = twice.First();
            foreach (PageMark other in twice.Skip(1))
            {
                errors.Add(new MarkdownProblem(other.Line, other.Column, $"The mark \"{other.Name}\" is in line {first.Line} already."));
            }
        }
        return marks;

        PageMark Mark(string name, string kind, string label, string what, Block at) =>
            new(name, kind, label, what, at.Line + 1, at.Column + 1);
    }

    // "::: axiom {#ax-gerade} Geradenaxiom", "::: def Strecke {#def-strecke}": kind, title and mark from the opening line
    private static Statement? StatementOf(CustomContainer container, string[] lines, Func<int, (int, int)> locate, List<MarkdownProblem> errors)
    {
        string line = container.Line < lines.Length ? lines[container.Line].TrimEnd('\r') : "";
        Match match = OpeningLine().Match(line);
        string kind = (match.Success ? match.Groups[1].Value : container.Info ?? "").ToLowerInvariant();
        kind = KindAliases.GetValueOrDefault(kind, kind);
        if (!StatementKinds.ContainsKey(kind))
        {
            errors.Add(new MarkdownProblem(container.Line + 1, container.Column + 1,
                $"\"{(kind.Length > 0 ? kind : ":::")}\" is no kind of statement (known: {string.Join(", ", StatementKinds.Keys)}, def)."));
            return null;
        }
        string rest = match.Success ? match.Groups[2].Value : container.Arguments ?? "";
        // the attributes ("{#name}") are Markdig's; the rest is the title
        Match attributes = Attributes().Match(rest);
        if (attributes.Success && container.TryGetAttributes()?.Id == null && IdIn(attributes.Value) is { } id)
        {
            container.GetAttributes().Id = id;
        }
        string title = Attributes().Replace(rest, "").Trim();
        // "::: proof §satz-x": the proof of that statement ("Beweis von Satz 3.2.")
        if (kind == "proof" && Regex.Match(title, @"^§([\p{L}\p{N}_](?:[\p{L}\p{N}_-]*[\p{L}\p{N}_])?)$") is { Success: true } proved)
        {
            return new Statement(kind, null, "") { ProofOf = proved.Groups[1].Value };
        }
        return new Statement(kind, title.Length > 0 ? title : null, "");
    }

    [GeneratedRegex(@"\{#[\p{L}\p{N}_-]\}")]
    private static partial Regex SingleCharacterMark();

    [GeneratedRegex(@"^\s*:{3,}\s*([\p{L}]+)\s*(.*)$")]
    private static partial Regex OpeningLine();

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Attributes();

    private static string? IdIn(string attributes)
    {
        Match id = Regex.Match(attributes, @"#([\p{L}\p{N}_-]+)");
        return id.Success ? id.Groups[1].Value : null;
    }

    // the first words of a statement (to recognise it by in a list of marks)
    private static string StartOf(string text, Block container)
    {
        if (container is not ContainerBlock { Count: > 0 } inner)
        {
            return "";
        }
        Block first = inner[0];
        string start = text.Substring(first.Span.Start, Math.Min(first.Span.Length, 120)).Replace('\n', ' ').Trim();
        return first.Span.Length > 120 ? start + " …" : start;
    }

    // marks that other pages of the book have already
    private static void CheckMarks(List<PageMark> marks, string? page, IReadOnlyDictionary<string, BookMark>? bookMarks, List<MarkdownProblem> errors)
    {
        if (bookMarks == null)
        {
            return;
        }
        foreach (PageMark mark in marks)
        {
            if (bookMarks.TryGetValue(mark.Name, out BookMark? other) && !string.Equals(other.File, page, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new MarkdownProblem(mark.Line, mark.Column,
                    $"The mark \"{mark.Name}\" is on {Path.GetFileName(other.File)} already (line {other.Mark.Line})."));
            }
        }
    }

    // ------------------------------------------------------------------ references

    // "§name" (what the mark is: "Abb. 3"), "§*name" (with its title: "Abschnitt 2.4 (Das Axiom von Pasch)"), "§§name" (in
    // print also its page); "\§" is the sign itself, "§ 5" too
    [GeneratedRegex(@"(\\?)(§§?\*?)([\p{L}\p{N}_](?:[\p{L}\p{N}_-]*[\p{L}\p{N}_])?)")]
    private static partial Regex ReferencePattern();

    // the references of the page become links (with an id each: "r-name-2"); returns them with what they stand in
    private static List<PageReference> ResolveReferences(MarkdownDocument document, Func<int, (int, int)> locate, List<PageMark> marks,
        PageMark? section, string? page, IReadOnlyDictionary<string, BookMark>? bookMarks, List<MarkdownProblem> errors)
    {
        var own = marks.GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.First());
        var found = new List<PageReference>();
        // a reference at a place (a caption's: the figure's, with its place in the caption): its id, and what it stands in
        Dictionary<(int, int, int), string>? anchors = null;
        string Found(string name, int line, int column, int ordinal, Block? block)
        {
            string anchor = anchors![(line, column, ordinal)];
            ReferenceContext? context = null;
            for (Block? at = block; at != null && context == null; at = at.Parent)
            {
                context = at.GetData(typeof(ReferenceContext)) as ReferenceContext;
            }
            found.Add(new PageReference(name, line, column, anchor)
            {
                Context = context?.Mark,
                Section = section,
                Proof = context?.Proof ?? false,
                ProofOf = context?.ProofOf,
            });
            return anchor;
        }
        // the link of a mark: on this page or another one
        string HrefOf(BookMark other, string name) =>
            (page == null ? other.File : Path.GetRelativePath(Path.GetDirectoryName(page)!, other.File)).Replace('\\', '/') + "#" + name;
        // "::: proof §satz-x": the statement it proves
        foreach (CustomContainer container in document.Descendants<CustomContainer>())
        {
            if (container.GetData(typeof(Statement)) is not Statement { ProofOf: { } proved } statement)
            {
                continue;
            }
            if (own.TryGetValue(proved, out PageMark? ownMark))
            {
                container.SetData(typeof(Statement), statement with { ProofLabel = ownMark.Label, ProofHref = "#" + proved });
            }
            else if (bookMarks != null && bookMarks.TryGetValue(proved, out BookMark? other))
            {
                container.SetData(typeof(Statement), statement with { ProofLabel = other.Mark.Label, ProofHref = HrefOf(other, proved) });
            }
            else
            {
                errors.Add(new MarkdownProblem(container.Line + 1, container.Column + 1, $"There is no mark \"{proved}\" (write it at the statement this proves: {{#{proved}}})."));
            }
        }
        // "§*name": Markdig leaves the "*" (no emphasis) as a text of its own; one text again
        foreach (LiteralInline literal in document.Descendants<LiteralInline>().ToList())
        {
            while (literal.Parent != null && literal.Content.ToString().EndsWith('§')
                && literal.NextSibling is LiteralInline next && next.Span.Start == literal.Span.End + 1 && next.Content.ToString().StartsWith('*'))
            {
                string joined = literal.Content.ToString() + next.Content.ToString();
                // the text after the "*" may be a text of its own too
                if (next.NextSibling is LiteralInline after && after.Span.Start == next.Span.End + 1)
                {
                    joined += after.Content.ToString();
                    after.Remove();
                }
                literal.Content = new Markdig.Helpers.StringSlice(joined);
                literal.Span = new SourceSpan(literal.Span.Start, literal.Span.Start + joined.Length - 1);
                next.Remove();
            }
        }
        // the ids: the references to a mark numbered in the order of the text (as the editor finds them again)
        var sites = new List<(int Line, int Column, int Ordinal, string Name)>();
        foreach (LiteralInline literal in document.Descendants<LiteralInline>())
        {
            foreach (Match match in ReferencePattern().Matches(literal.Content.ToString()).Where(m => m.Groups[1].Length == 0))
            {
                (int line, int column) = locate(literal.Span.Start + match.Index);
                sites.Add((line, column, 0, match.Groups[3].Value));
            }
        }
        foreach (FigureItem item in document.Descendants<GeoFigureBlock>().SelectMany(f => f.Items))
        {
            if (item.Reference is { } captioned)
            {
                int ordinal = 0;
                foreach (Match match in ReferencePattern().Matches(captioned.Caption).Where(m => m.Groups[1].Length == 0))
                {
                    sites.Add((captioned.Line, captioned.Column, ++ordinal, match.Groups[3].Value));
                }
            }
        }
        anchors = [];
        foreach (IGrouping<string, (int Line, int Column, int Ordinal, string Name)> mark in sites.OrderBy(t => t.Line).ThenBy(t => t.Column).ThenBy(t => t.Ordinal).GroupBy(t => t.Name))
        {
            int count = 0;
            foreach ((int line, int column, int ordinal, _) in mark)
            {
                anchors[(line, column, ordinal)] = ReferenceAnchor(mark.Key, ++count);
            }
        }
        foreach (LiteralInline literal in document.Descendants<LiteralInline>().ToList())
        {
            string content = literal.Content.ToString();
            if (!content.Contains('§'))
            {
                continue;
            }
            int position = 0;
            var pieces = new List<Inline>();
            foreach (Match match in ReferencePattern().Matches(content))
            {
                if (match.Index > position)
                {
                    pieces.Add(new LiteralInline(content.Substring(position, match.Index - position)));
                }
                position = match.Index + match.Length;
                if (match.Groups[1].Length > 0)
                {
                    // "\§name": the sign and the word
                    pieces.Add(new LiteralInline(match.Groups[2].Value + match.Groups[3].Value));
                    continue;
                }
                string name = match.Groups[3].Value;
                (int referenceLine, int referenceColumn) = locate(literal.Span.Start + match.Index);
                var reference = new MarkReferenceInline(name, match.Groups[2].Value.StartsWith("§§", StringComparison.Ordinal))
                {
                    Anchor = Found(name, referenceLine, referenceColumn, 0, literal.Parent?.ParentBlock),
                };
                bool withTitle = match.Groups[2].Value.EndsWith('*');
                if (own.TryGetValue(name, out PageMark? mark))
                {
                    reference.Label = mark.Label;
                    reference.Title = withTitle ? mark.Title : null;
                    reference.Href = "#" + name;
                }
                else if (bookMarks != null && bookMarks.TryGetValue(name, out BookMark? other))
                {
                    reference.Label = other.Mark.Label;
                    reference.Title = withTitle ? other.Mark.Title : null;
                    reference.Href = HrefOf(other, name);
                }
                else
                {
                    errors.Add(new MarkdownProblem(referenceLine, referenceColumn, $"There is no mark \"{name}\" (write it at a figure, a statement or a heading: {{#{name}}})."));
                }
                pieces.Add(reference);
            }
            if (position < content.Length)
            {
                pieces.Add(new LiteralInline(content.Substring(position)));
            }
            ReplaceBy(literal, pieces);
        }
        // the captions of figures (Markdown of their own, rendered with the figure): a reference becomes a link there
        foreach (GeoFigureBlock figure in document.Descendants<GeoFigureBlock>())
        {
            for (int i = 0; i < figure.Items.Count; i++)
            {
                FigureItem item = figure.Items[i];
                if (item.Reference is not { } reference || !reference.Caption.Contains('§'))
                {
                    continue;
                }
                int ordinal = 0;
                string caption = ReferencePattern().Replace(reference.Caption, match =>
                {
                    if (match.Groups[1].Length > 0)
                    {
                        return match.Groups[2].Value + match.Groups[3].Value;
                    }
                    string name = match.Groups[3].Value;
                    string anchor = Found(name, reference.Line, reference.Column, ++ordinal, figure);
                    bool withTitle = match.Groups[2].Value.EndsWith('*');
                    (string Label, string? Title, string Href)? target =
                        own.TryGetValue(name, out PageMark? mark) ? (mark.Label, mark.Title, "#" + name)
                        : bookMarks != null && bookMarks.TryGetValue(name, out BookMark? other)
                            ? (other.Mark.Label, other.Mark.Title, HrefOf(other, name))
                            : null;
                    if (target is not { } found)
                    {
                        errors.Add(new MarkdownProblem(reference.Line, reference.Column, $"There is no mark \"{name}\" (write it at a figure, a statement or a heading: {{#{name}}})."));
                        return "??";
                    }
                    string label = Regex.Replace(found.Label, @"[\[\]\\*_]", @"\$0") + (withTitle && found.Title != null ? " (" + found.Title + ")" : "");
                    return "[" + label + "](" + found.Href.Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29") + "){.ref #" + anchor + "}";
                });
                figure.Items[i] = item with { Reference = reference with { Caption = caption } };
            }
        }
        // in the order of the text (the captions' where their figures are)
        return found.OrderBy(r => r.Line).ThenBy(r => r.Column).ToList();
    }

    // a reference to a mark: what it shows ("Axiom 2"), where it leads; Label null: an unknown mark
    private sealed class MarkReferenceInline(string name, bool withPage) : LeafInline
    {
        public string Name { get; } = name;

        public bool WithPage { get; } = withPage;

        public string? Label { get; set; }

        // "§*name": the mark's title (Markdown), shown in brackets after the label
        public string? Title { get; set; }

        public string? Href { get; set; }

        // the id of its link, for the list of where a mark is used
        public string? Anchor { get; set; }
    }

    private sealed class MarkReferenceRenderer : HtmlObjectRenderer<MarkReferenceInline>
    {
        protected override void Write(HtmlRenderer renderer, MarkReferenceInline reference)
        {
            if (reference.Label == null)
            {
                renderer.Write("<span class=\"ref-missing\" title=\"").WriteEscape(reference.Name).Write("\">??</span>");
                return;
            }
            renderer.Write("<a class=\"ref\"");
            if (reference.Anchor != null)
            {
                renderer.Write(" id=\"").WriteEscape(reference.Anchor).Write('"');
            }
            renderer.Write(" href=\"").WriteEscapeUrl(reference.Href).Write("\">").WriteEscape(reference.Label);
            if (reference.Title != null)
            {
                renderer.Write(" (").Write(InlineHtml(reference.Title)).Write(")");
            }
            renderer.Write("</a>");
        }
    }

    // ------------------------------------------------------------------ statements

    // "<div class="statement statement-axiom">" with the label at the start of its first paragraph ("Axiom 2
    // (Geradenaxiom)."); a proof ends with ∎
    // usedIn: where the marks are used (in a book's preview and the book as one HTML file), shown under the statements
    // with a mark; page: the page's full path (for the links)
    private sealed class StatementRenderer(IReadOnlyDictionary<string, IReadOnlyList<MarkUse>>? usedIn, string? page) : HtmlObjectRenderer<CustomContainer>
    {
        // "Verwendet in: Beweis von Satz 3.4 · Abschnitt 5.2" (one link for each, to its first reference), or "Nicht verwendet."
        private string UsesOf(string name)
        {
            IReadOnlyList<MarkUse> uses = usedIn!.TryGetValue(name, out IReadOnlyList<MarkUse>? found) ? found : [];
            if (uses.Count == 0)
            {
                return "<p class=\"used-in\">Nicht verwendet.</p>";
            }
            var links = uses.GroupBy(u => (u.File, u.Label)).Select(group =>
            {
                MarkUse first = group.First();
                string href = page != null && string.Equals(first.File, page, StringComparison.OrdinalIgnoreCase)
                    ? "#" + first.Anchor
                    : (page == null ? first.File : Path.GetRelativePath(Path.GetDirectoryName(page)!, first.File)).Replace('\\', '/') + "#" + first.Anchor;
                return "<a href=\"" + WebUtility.HtmlEncode(href.Replace(" ", "%20")) + "\">" + WebUtility.HtmlEncode(first.Label) + "</a>"
                    + (group.Count() > 1 ? " (" + group.Count().ToString(CultureInfo.InvariantCulture) + "×)" : "");
            });
            return "<p class=\"used-in\">Verwendet in: " + string.Join(" · ", links) + "</p>";
        }

        // our classes instead of Markdig's (which is the kind as written)
        private static void SetClasses(CustomContainer container, params string[] classes)
        {
            HtmlAttributes attributes = container.GetAttributes();
            attributes.Classes?.Clear();
            foreach (string name in classes)
            {
                attributes.AddClass(name);
            }
        }

        protected override void Write(HtmlRenderer renderer, CustomContainer container)
        {
            if (container.GetData(typeof(Statement)) is not Statement statement)
            {
                // an unknown kind: its content as it is
                SetClasses(container, "statement", "statement-unknown");
                renderer.Write("<div").WriteAttributes(container).WriteLine('>');
                renderer.WriteChildren(container);
                renderer.WriteLine("</div>");
                return;
            }
            // "Axiom 2 (Geradenaxiom).", "Beweis."; a box: its title ("Merke.") or nothing
            string label = statement.Label.Length == 0
                ? (statement.Title != null ? "<span class=\"statement-label\">" + InlineHtml(statement.Title) + ".</span> " : "")
                : statement.ProofOf != null
                    // "Beweis von Satz 3.2."
                    ? "<span class=\"statement-label\">" + WebUtility.HtmlEncode(statement.Label) + " von "
                        + (statement.ProofLabel != null
                            ? "<a class=\"ref\" href=\"" + WebUtility.HtmlEncode(statement.ProofHref!.Replace(" ", "%20")) + "\">" + WebUtility.HtmlEncode(statement.ProofLabel) + "</a>"
                            : "<span class=\"ref-missing\" title=\"" + WebUtility.HtmlEncode(statement.ProofOf) + "\">??</span>") + ".</span> "
                    : "<span class=\"statement-label\">" + WebUtility.HtmlEncode(statement.Label)
                        + (statement.Title != null ? " (" + InlineHtml(statement.Title) + ")" : "") + ".</span> ";
            string end = statement.Kind == "proof" ? " <span class=\"qed\">∎</span>" : "";
            SetClasses(container, "statement", "statement-" + statement.Kind);
            renderer.Write("<div").WriteAttributes(container).WriteLine('>');
            bool labelled = false;
            for (int i = 0; i < container.Count; i++)
            {
                Block child = container[i];
                if (child is ParagraphBlock paragraph)
                {
                    renderer.Write("<p").WriteAttributes(paragraph).Write('>');
                    if (!labelled)
                    {
                        renderer.Write(label);
                        labelled = true;
                    }
                    renderer.WriteLeafInline(paragraph);
                    if (i == container.Count - 1)
                    {
                        renderer.Write(end);
                    }
                    renderer.WriteLine("</p>");
                    continue;
                }
                if (!labelled)
                {
                    if (label.Length > 0)
                    {
                        renderer.Write("<p>").Write(label.TrimEnd()).WriteLine("</p>");
                    }
                    labelled = true;
                }
                renderer.Write(child);
                if (i == container.Count - 1 && end.Length > 0)
                {
                    renderer.Write("<p class=\"qed-line\">").Write(end.Trim()).WriteLine("</p>");
                }
            }
            if (!labelled && label.Length + end.Length > 0)
            {
                renderer.Write("<p>").Write(label.TrimEnd()).Write(end).WriteLine("</p>");
            }
            // where it is used (not for a proof or a box: nothing refers to them)
            if (usedIn != null && container.TryGetAttributes()?.Id is { } id && statement.Kind is not ("proof" or "box"))
            {
                renderer.WriteLine(UsesOf(id));
            }
            renderer.WriteLine("</div>");
        }
    }
}
