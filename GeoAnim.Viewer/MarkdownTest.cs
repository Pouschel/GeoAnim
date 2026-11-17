using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace GeoAnim.Viewer;

/// <summary>
/// The head of a page that stands alone – a test ("type: test") or a worksheet ("type: worksheet") – instead of being a
/// page of a book: what the head line of the sheet shows, and the groups it is made for (each its own sheet).
/// </summary>
public sealed record DocumentHead(string Type)
{
    public string? Title { get; init; }

    /// <summary>"Informatik 10": before the title.</summary>
    public string? Subject { get; init; }

    /// <summary>"20 min".</summary>
    public string? Time { get; init; }

    /// <summary>The groups ("A", "B", "C"); none: one sheet.</summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

    /// <summary>The start of the groups' chance (for later: numbers drawn by chance).</summary>
    public int? Seed { get; init; }

    public bool IsTest => Type == "test";
}

/// <summary>Which sheet of a test or worksheet: for a group (null: without one), the students' or with the solutions.</summary>
public sealed record SheetVariant(string? Group, bool Solution);

/// <summary>A task of a test: its number (null: an extra task, not in the sum), title and points.</summary>
public sealed record TestTask(int? Number, string Title, double Points);

public sealed partial class MarkdownPage
{
    /// <summary>The head of a test or worksheet ("type: …" in the front matter), or null for a page of a book.</summary>
    public DocumentHead? Document { get; private set; }

    /// <summary>The tasks of a test (headings "## … {3}"), in their order.</summary>
    public IReadOnlyList<TestTask> Tasks { get; private set; } = [];

    internal static readonly string[] DocumentTypes = ["test", "worksheet"];

    // ------------------------------------------------------------------ blanks: [[answer]], [[answer|6em]], [[sol: …]]

    /// <summary>"[[…]]" in the text: a blank with its answer.</summary>
    internal sealed class BlankInline : LeafInline
    {
        public string Content { get; init; } = "";
    }

    private sealed class BlankParser : InlineParser
    {
        public BlankParser()
        {
            OpeningCharacters = ['['];
        }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            if (slice.PeekChar() != '[')
            {
                return false;
            }
            string rest = slice.Text.Substring(slice.Start, slice.End - slice.Start + 1);
            int end = rest.IndexOf("]]", 2, StringComparison.Ordinal);
            int newline = rest.IndexOf('\n');
            if (end < 0 || (newline >= 0 && newline < end))
            {
                return false;
            }
            int start = processor.GetSourcePosition(slice.Start, out int line, out int column);
            processor.Inline = new BlankInline
            {
                Content = rest[2..end],
                Span = new SourceSpan(start, start + end + 1),
                Line = line,
                Column = column,
            };
            slice.Start += end + 2;
            return true;
        }
    }

    private sealed class BlankExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            if (!pipeline.InlineParsers.Contains<BlankParser>())
            {
                pipeline.InlineParsers.InsertBefore<LinkInlineParser>(new BlankParser());
            }
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
            if (renderer is HtmlRenderer html && !html.ObjectRenderers.Contains<BlankRenderer>())
            {
                html.ObjectRenderers.Insert(0, new BlankRenderer(null));
            }
        }
    }

    // a width as written after "|" ("6em", "3cm", "8"): in em without a unit
    [GeneratedRegex(@"^\s*(\d+(?:[.,]\d+)?)\s*(em|cm|mm)?\s*$")]
    private static partial Regex BlankWidth();

    /// <summary>
    /// A blank: for the students a line to write on, as wide as the answer needs (or as given: "[[40|6em]]"); with the
    /// solutions the answer on it, red. "[[sol: …]]": only a solution (no line), e.g. to a drawing. Outside a test or
    /// worksheet the answer as written.
    /// </summary>
    internal sealed class BlankRenderer(SheetVariant? variant) : HtmlObjectRenderer<BlankInline>
    {
        protected override void Write(HtmlRenderer renderer, BlankInline blank)
        {
            string content = blank.Content.Trim();
            bool solutionOnly = content.StartsWith("sol:", StringComparison.Ordinal);
            string? width = null;
            int bar = content.LastIndexOf('|');
            if (!solutionOnly && bar >= 0 && BlankWidth().Match(content[(bar + 1)..]) is { Success: true } given)
            {
                width = given.Groups[1].Value.Replace(',', '.') + (given.Groups[2].Success ? given.Groups[2].Value : "em");
                content = content[..bar].Trim();
            }
            string answer = solutionOnly ? content[4..].Trim() : content;
            if (variant is { Solution: false })
            {
                if (!solutionOnly)
                {
                    renderer.Write("<span class=\"blank\" style=\"width: ").Write(width ?? WidthOf(answer)).Write("\"></span>");
                }
                return;
            }
            if (solutionOnly)
            {
                renderer.Write("<span class=\"solution\">").Write(InlineHtml(answer)).Write("</span>");
                return;
            }
            renderer.Write("<span class=\"blank blank-solution\" style=\"min-width: ").Write(width ?? WidthOf(answer)).Write("\">")
                .Write(InlineHtml(answer)).Write("</span>");
        }

        // as wide as the answer in handwriting, and some more: at least 4em
        private static string WidthOf(string answer)
        {
            int letters = HtmlExporter.PlainText(answer).Replace("$", "").Length;
            return Math.Max(4, Math.Round(0.6 * letters + 2)).ToString(CultureInfo.InvariantCulture) + "em";
        }
    }

    // ------------------------------------------------------------------ tasks, points, page breaks, room to write

    // "## Zahlensysteme {3}", "## Zusatzaufgabe {+1}": the points at the end of a task's heading
    [GeneratedRegex(@"\s*\{(\+)?\s*(\d+(?:[.,]\d+)?)\s*(?:P)?\s*\}\s*$")]
    private static partial Regex TaskPoints();

    // a paragraph that is one command: "\page", "\space 3cm", "\points"
    [GeneratedRegex(@"^\\(page|points|space)(?:\s+(\S+))?\s*$")]
    private static partial Regex SheetCommand();

    private static string Points(double points) => points.ToString("0.##", CultureInfo.GetCultureInfo("de-DE"));

    /// <summary>
    /// The tasks of a test or worksheet: a heading "## Titel {3}" becomes "1. Titel (3 P)" (numbered, counted), "## Titel
    /// {+1}" an extra task "Titel (1 P)" (not numbered, not in the sum); the commands "\page" (a new page), "\space 3cm"
    /// (room to write) and "\points" (the table of the points, with the sum).
    /// </summary>
    private static List<TestTask> ReadTasks(string text, MarkdownDocument document, Func<int, (int, int)> locate, List<MarkdownProblem> errors)
    {
        var tasks = new List<TestTask>();
        int number = 0;
        foreach (HeadingBlock heading in document.Descendants<HeadingBlock>().Where(h => h.Level == 2))
        {
            if (heading.Inline?.LastChild is not LiteralInline last || TaskPoints().Match(last.Content.ToString()) is not { Success: true } points)
            {
                continue;
            }
            last.Content = new StringSlice(last.Content.ToString()[..points.Index]);
            double value = double.Parse(points.Groups[2].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            bool extra = points.Groups[1].Success;
            string title = Regex.Replace(HeadingText(text, heading), @"\s*\{[^{}]*\}\s*$", "");
            int? own = extra ? null : ++number;
            tasks.Add(new TestTask(own, title, value));
            if (own != null)
            {
                heading.Inline.FirstChild!.InsertBefore(new LiteralInline(own.Value.ToString(CultureInfo.InvariantCulture) + ". "));
            }
            heading.Inline.AppendChild(new LiteralInline($" ({Points(value)} P)"));
        }
        foreach (ParagraphBlock paragraph in document.Descendants<ParagraphBlock>().ToList())
        {
            string source = text.Substring(paragraph.Span.Start, paragraph.Span.Length).Trim();
            if (SheetCommand().Match(source) is not { Success: true } command)
            {
                continue;
            }
            string? html = command.Groups[1].Value switch
            {
                "page" => "<div class=\"page-break\"></div>",
                "points" => PointsTable(tasks),
                _ when command.Groups[2].Success && BlankWidth().Match(command.Groups[2].Value) is { Success: true } length =>
                    $"<div class=\"space\" style=\"height: {length.Groups[1].Value.Replace(',', '.')}{(length.Groups[2].Success ? length.Groups[2].Value : "em")}\"></div>",
                _ => null,
            };
            if (html == null)
            {
                (int line, int column) = locate(paragraph.Span.Start);
                errors.Add(new MarkdownProblem(line, column, "\\space needs a height: \\space 3cm (or em, mm)."));
                continue;
            }
            ContainerBlock parent = paragraph.Parent!;
            int index = parent.IndexOf(paragraph);
            parent.RemoveAt(index);
            var block = new HtmlBlock(null) { Type = HtmlBlockType.NonInterruptingBlock, Line = paragraph.Line, Column = paragraph.Column, Span = paragraph.Span };
            block.Lines = new StringLineGroup(html);
            parent.Insert(index, block);
        }
        return tasks;
    }

    // the points of the tasks: their numbers, most points, a row to write in; the extra tasks apart; the sum
    private static string PointsTable(List<TestTask> tasks)
    {
        var html = new StringBuilder("<table class=\"points-table\"><thead><tr><th>Aufgabe</th>");
        foreach (TestTask task in tasks)
        {
            html.Append("<th>").Append(task.Number?.ToString(CultureInfo.InvariantCulture) ?? "Z").Append("</th>");
        }
        html.Append("<th>Summe</th></tr></thead><tbody><tr><td>Punkte</td>");
        foreach (TestTask task in tasks)
        {
            html.Append("<td>").Append(task.Number == null ? "+" : "").Append(Points(task.Points)).Append("</td>");
        }
        html.Append("<td>").Append(Points(tasks.Where(t => t.Number != null).Sum(t => t.Points))).Append("</td></tr><tr><td>erreicht</td>");
        html.Append(string.Concat(Enumerable.Repeat("<td></td>", tasks.Count + 1))).Append("</tr></tbody></table>");
        return html.ToString();
    }

    /// <summary>
    /// The head of a sheet: the subject and title, the group and time (with the solutions: "Lösung"), and the lines for
    /// name, class and date (of a worksheet: name and date).
    /// </summary>
    private static string HeadHtml(DocumentHead head, SheetVariant variant, string? fallbackTitle)
    {
        string title = head.Title ?? fallbackTitle ?? (head.IsTest ? "Test" : "Arbeitsblatt");
        string full = head.Subject != null ? head.Subject + ": " + title : title;
        var info = new List<string>();
        if (variant.Solution)
        {
            info.Add("Lösung");
        }
        if (variant.Group != null)
        {
            info.Add("Gruppe " + variant.Group);
        }
        if (head.Time != null)
        {
            info.Add(head.Time);
        }
        string[] fields = head.IsTest ? ["Name", "Klasse", "Datum"] : ["Name", "Datum"];
        var html = new StringBuilder("<header class=\"sheet-head\" data-title=\"").Append(WebUtility.HtmlEncode(HtmlExporter.PlainText(full)))
            .Append("\" data-info=\"").Append(WebUtility.HtmlEncode(string.Join(" · ", info)))
            .Append("\" data-fields=\"").Append(string.Join("|", fields)).Append("\">");
        html.Append("<div class=\"sheet-title\"><span class=\"sheet-name\">").Append(InlineHtml(full)).Append("</span>");
        if (info.Count > 0)
        {
            html.Append("<span class=\"sheet-info").Append(variant.Solution ? " sheet-solution" : "").Append("\">")
                .Append(WebUtility.HtmlEncode(string.Join(" · ", info))).Append("</span>");
        }
        html.Append("</div><div class=\"sheet-fields\">");
        foreach (string field in fields)
        {
            html.Append("<span>").Append(field).Append(":</span>");
        }
        return html.Append("</div></header>\n").ToString();
    }
}
