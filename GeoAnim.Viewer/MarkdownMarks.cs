using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Markdig.Extensions.CustomContainers;
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
public sealed record PageMark(string Name, string Kind, string Label, string Text, int Line, int Column);

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
        ["lemma"] = ("Hilfssatz", true),
        ["corollary"] = ("Folgerung", true),
        ["proof"] = ("Beweis", false),
        ["example"] = ("Beispiel", true),
        ["remark"] = ("Bemerkung", false),
        ["box"] = ("", false),
    };

    private static readonly Dictionary<string, string> KindAliases = new() { ["def"] = "definition" };

    private const string FigureName = "Abb.";
    private const string SectionName = "Abschnitt";

    // a statement as read: its kind, title (Markdown) and what references show
    private sealed record Statement(string Kind, string? Title, string Label);

    // ------------------------------------------------------------------ numbers and marks

    // numbers figures ("Abb. 1" …), statements (one counter for the numbered kinds) and sections (## → 1, ### → 1.1 …) in
    // the order of the page, and collects the marks; with render, puts the labels where the page shows them
    private static List<PageMark> Number(string text, MarkdownDocument document, Func<int, (int, int)> locate,
        List<MarkdownProblem> errors, bool render)
    {
        var marks = new List<PageMark>();
        int figures = 0, statements = 0;
        var sections = new int[7];
        string[] lines = text.Split('\n');
        foreach (Block block in document.Descendants<Block>().ToList())
        {
            switch (block)
            {
                case HeadingBlock { Level: >= 2 } heading:
                    {
                        sections[heading.Level]++;
                        for (int level = heading.Level + 1; level < sections.Length; level++)
                        {
                            sections[level] = 0;
                        }
                        string number = string.Join(".", sections.Skip(2).Take(heading.Level - 1).Select(n => n.ToString(CultureInfo.InvariantCulture)));
                        if (heading.TryGetAttributes()?.Id is { } id)
                        {
                            marks.Add(Mark(id, "section", SectionName + " " + number, HeadingText(text, heading), heading));
                        }
                        break;
                    }
                case GeoFigureBlock figure:
                    {
                        figures++;
                        figure.Label = FigureName + " " + figures.ToString(CultureInfo.InvariantCulture);
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
                        if (id != null)
                        {
                            figure.GetAttributes().Id = id;
                            string caption = string.Join(" · ", figure.Items.Select(i => i.Reference?.Caption).Where(c => !string.IsNullOrEmpty(c)));
                            marks.Add(Mark(id, "figure", figure.Label, caption, figure));
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
                        string label = numbered ? name + " " + (++statements).ToString(CultureInfo.InvariantCulture) : name;
                        statement = statement with { Label = label };
                        container.SetData(typeof(Statement), statement);
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
                                marks.Add(Mark(id, statement.Kind, label, statement.Title ?? StartOf(text, container), container));
                            }
                        }
                        break;
                    }
            }
        }
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

    // "§name" (what the mark is: "Abb. 3"), "§§name" (in print also its page); "\§" is the sign itself, "§ 5" too
    [GeneratedRegex(@"(\\?)(§§?)([\p{L}\p{N}_](?:[\p{L}\p{N}_-]*[\p{L}\p{N}_])?)")]
    private static partial Regex ReferencePattern();

    private static void ResolveReferences(MarkdownDocument document, Func<int, (int, int)> locate, List<PageMark> marks,
        string? page, IReadOnlyDictionary<string, BookMark>? bookMarks, List<MarkdownProblem> errors)
    {
        var own = marks.GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.First());
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
                var reference = new MarkReferenceInline(name, match.Groups[2].Length == 2);
                if (own.TryGetValue(name, out PageMark? mark))
                {
                    reference.Label = mark.Label;
                    reference.Href = "#" + name;
                }
                else if (bookMarks != null && bookMarks.TryGetValue(name, out BookMark? other))
                {
                    reference.Label = other.Mark.Label;
                    reference.Href = (page == null ? other.File : Path.GetRelativePath(Path.GetDirectoryName(page)!, other.File)).Replace('\\', '/') + "#" + name;
                }
                else
                {
                    (int line, int column) = locate(literal.Span.Start + match.Index);
                    errors.Add(new MarkdownProblem(line, column, $"There is no mark \"{name}\" (write it at a figure, a statement or a heading: {{#{name}}})."));
                }
                pieces.Add(reference);
            }
            if (position < content.Length)
            {
                pieces.Add(new LiteralInline(content.Substring(position)));
            }
            foreach (Inline piece in pieces)
            {
                literal.InsertBefore(piece);
            }
            literal.Remove();
        }
    }

    // a reference to a mark: what it shows ("Axiom 2"), where it leads; Label null: an unknown mark
    private sealed class MarkReferenceInline(string name, bool withPage) : LeafInline
    {
        public string Name { get; } = name;

        public bool WithPage { get; } = withPage;

        public string? Label { get; set; }

        public string? Href { get; set; }
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
            renderer.Write("<a class=\"ref\" href=\"").WriteEscapeUrl(reference.Href).Write("\">").WriteEscape(reference.Label).Write("</a>");
        }
    }

    // ------------------------------------------------------------------ statements

    // "<div class="statement statement-axiom">" with the label at the start of its first paragraph ("Axiom 2
    // (Geradenaxiom)."); a proof ends with ∎
    private sealed class StatementRenderer : HtmlObjectRenderer<CustomContainer>
    {
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
            renderer.WriteLine("</div>");
        }
    }
}
