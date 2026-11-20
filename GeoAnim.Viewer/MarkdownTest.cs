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

    /// <summary>The C# projects whose classes the sheet uses (full paths; "generators:" in the head).</summary>
    public IReadOnlyList<string> Generators { get; init; } = [];

    public bool IsTest => Type == "test";

    /// <summary>A document ("type: document"): a page of its own (to hand out or show), without names, points and solutions.</summary>
    public bool IsDocument => Type == "document";

    /// <summary>Questions for ILIAS ("type: ilias"): a pool of questions for every group, checked in an HTML file.</summary>
    public bool IsIlias => Type == "ilias";
}

/// <summary>Which sheet of a test or worksheet: for a group (null: without one), the students' or with the solutions.</summary>
public sealed record SheetVariant(string? Group, bool Solution);

/// <summary>A task of a test: its number (null: an extra task, not in the sum), title and points.</summary>
public sealed record TestTask(int? Number, string Title, double Points)
{
    /// <summary>A question for ILIAS: "Lückentext", "Zuordnung" or "Single Choice".</summary>
    public string? Kind { get; init; }
}

public sealed partial class MarkdownPage
{
    /// <summary>The head of a test or worksheet ("type: …" in the front matter), or null for a page of a book.</summary>
    public DocumentHead? Document { get; private set; }

    /// <summary>The tasks of a test (headings "## … {3}"), in their order.</summary>
    public IReadOnlyList<TestTask> Tasks { get; private set; } = [];

    internal static readonly string[] DocumentTypes = ["test", "worksheet", "ilias", "document"];

    // ------------------------------------------------------------------ blanks: [[answer]], [[answer|6em]], [[sol: …]]

    /// <summary>"[[…]]" in the text (and "{2 ordered}" right after it): a blank with its answer.</summary>
    internal sealed class BlankInline : LeafInline
    {
        public string Content { get; init; } = "";

        /// <summary>What stands in "{…}" after it ("2", "ordered", "chars=16"), or "".</summary>
        public string Options { get; init; } = "";
    }

    /// <summary>
    /// A blank as it is meant: <see cref="Choices"/> (the right ones marked) for a choice, else <see cref="Answers"/> to
    /// write (more than one: each counts); <see cref="SolutionOnly"/> ("[[sol: …]]") no blank at all.
    /// </summary>
    internal sealed record Gap(IReadOnlyList<(string Text, bool Right)> Choices, IReadOnlyList<string> Answers, double Points, bool Ordered,
        int? Chars, string? Width, bool SolutionOnly)
    {
        public bool IsChoice => Choices.Count > 0;

        /// <summary>A blank: "[[*a | b]]" (a choice), "[[a | b]]" (to write, both right), "[[a|6em]]" (a width); "{2}" its points.</summary>
        public static Gap Of(BlankInline blank, List<string>? problems = null)
        {
            string content = blank.Content.Trim();
            double points = 1;
            bool ordered = false;
            int? chars = null;
            foreach (string option in blank.Options.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                if (double.TryParse(option.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double given) && given > 0)
                {
                    points = given;
                }
                else if (option == "ordered")
                {
                    ordered = true;
                }
                else if (option.StartsWith("chars=", StringComparison.Ordinal) && int.TryParse(option[6..], out int count) && count > 0)
                {
                    chars = count;
                }
                else
                {
                    problems?.Add($"\"{{{blank.Options}}}\" after a blank: its points ({{2}}), \"ordered\" (the choices as written) or \"chars=16\" (how long an answer may be).");
                }
            }
            if (content.StartsWith("sol:", StringComparison.Ordinal))
            {
                return new Gap([], [content[4..].Trim()], 0, false, null, null, SolutionOnly: true);
            }
            List<string> parts = SheetScript.SplitChoices(content).Select(p => p.Trim().Replace("\\|", "|")).ToList();
            string? width = null;
            if (parts.Count > 1 && BlankWidth().Match(parts[^1]) is { Success: true } given2 && given2.Groups[2].Success)
            {
                width = given2.Groups[1].Value.Replace(',', '.') + (given2.Groups[2].Success ? given2.Groups[2].Value : "em");
                parts.RemoveAt(parts.Count - 1);
            }
            if (parts.Any(p => p.StartsWith('*')))
            {
                var choices = parts.Select(p => p.StartsWith('*') ? (p[1..].Trim(), true) : (p, false)).ToList();
                if (choices.Count < 2)
                {
                    problems?.Add("A choice needs more than one answer: [[*right | wrong]].");
                }
                return new Gap(choices, [], points, ordered, chars, width, false);
            }
            return new Gap([], parts, points, ordered, chars, width, false);
        }
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
            // "{2 ordered}" right after it
            int length = end + 2;
            string options = "";
            if (length < rest.Length && rest[length] == '{')
            {
                int close = rest.IndexOf('}', length);
                if (close > 0 && (newline < 0 || close < newline) && !rest[length..close].Contains('#'))
                {
                    options = rest[(length + 1)..close].Trim();
                    length = close + 1;
                }
            }
            int start = processor.GetSourcePosition(slice.Start, out int line, out int column);
            processor.Inline = new BlankInline
            {
                Content = rest[2..end],
                Options = options,
                Span = new SourceSpan(start, start + length - 1),
                Line = line,
                Column = column,
            };
            slice.Start += length;
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
    /// solutions the answer on it, red. A choice ("[[*a | b]]"): the answers to underline, with the solutions the right
    /// ones red. "[[sol: …]]": only a solution (no line), e.g. to a drawing. Outside a test or worksheet the answer as
    /// written. For ILIAS (<paramref name="ilias"/>) each blank with what ILIAS needs (data-gap) and its points.
    /// </summary>
    internal sealed class BlankRenderer(SheetVariant? variant, bool ilias = false) : HtmlObjectRenderer<BlankInline>
    {
        protected override void Write(HtmlRenderer renderer, BlankInline blank)
        {
            Gap gap = Gap.Of(blank);
            if (gap.SolutionOnly)
            {
                if (variant is not { Solution: false })
                {
                    renderer.Write("<span class=\"solution\">").Write(InlineHtml(gap.Answers[0])).Write("</span>");
                }
                return;
            }
            bool solution = variant is not { Solution: false };
            if (ilias)
            {
                renderer.Write("<span class=\"gap\" data-gap=\"").WriteEscape(GapJson(gap)).Write("\">");
            }
            if (gap.IsChoice)
            {
                // the answers to underline, between brackets
                renderer.Write("<span class=\"choice\">");
                for (int i = 0; i < gap.Choices.Count; i++)
                {
                    renderer.Write(i > 0 ? "<span class=\"choice-bar\"> | </span>" : "")
                        .Write(solution && gap.Choices[i].Right ? "<span class=\"choice-right\">" : "<span>")
                        .Write(InlineHtml(gap.Choices[i].Text)).Write("</span>");
                }
                renderer.Write("</span>");
            }
            else if (!solution)
            {
                renderer.Write("<span class=\"blank\" style=\"width: ").Write(gap.Width ?? WidthOf(gap.Answers[0])).Write("\"></span>");
            }
            else
            {
                renderer.Write("<span class=\"blank blank-solution\" style=\"min-width: ").Write(gap.Width ?? WidthOf(gap.Answers.MaxBy(a => a.Length)!)).Write("\">")
                    .Write(string.Join("<span class=\"choice-bar\"> | </span>", gap.Answers.Select(InlineHtml))).Write("</span>");
            }
            if (ilias)
            {
                renderer.Write("</span> <em class=\"gap-points\">(").Write(Points(gap.Points)).Write(" P)</em>");
            }
        }

        // as wide as the answer in handwriting (which is larger than print), and some more: at least 5em
        private static string WidthOf(string answer)
        {
            int letters = HtmlExporter.PlainText(answer).Replace("$", "").Length;
            return Math.Max(5, letters + 3).ToString(CultureInfo.InvariantCulture) + "em";
        }
    }

    // what ILIAS needs of a blank: the choices (as text: ILIAS shows them as they are) and which are right, or the answers
    // to write; its points, whether the choices stay in order, how long an answer may be
    private static string GapJson(Gap gap)
    {
        using var stream = new MemoryStream();
        using (var json = new System.Text.Json.Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteStartArray(gap.IsChoice ? "choices" : "answers");
            foreach (string text in gap.IsChoice ? gap.Choices.Select(c => c.Text) : gap.Answers)
            {
                json.WriteStringValue(HtmlExporter.PlainText(text).Trim());
            }
            json.WriteEndArray();
            if (gap.IsChoice)
            {
                json.WriteStartArray("right");
                for (int i = 0; i < gap.Choices.Count; i++)
                {
                    if (gap.Choices[i].Right)
                    {
                        json.WriteNumberValue(i);
                    }
                }
                json.WriteEndArray();
            }
            json.WriteNumber("points", gap.Points);
            json.WriteBoolean("ordered", gap.Ordered);
            if (gap.Chars is int chars)
            {
                json.WriteNumber("chars", chars);
            }
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // ------------------------------------------------------------------ tasks, points, page breaks, room to write

    // "## Zahlensysteme {3}", "## Zusatzaufgabe {+1}": the points at the end of a task's heading
    [GeneratedRegex(@"\s*\{(\+)?\s*(\d+(?:[.,]\d+)?)\s*(?:P)?\s*\}\s*$")]
    private static partial Regex TaskPoints();

    // a paragraph that is one command: "\page", "\space 3cm", "\points"
    [GeneratedRegex(@"^\\(page|points|space)(?:\s+(\S+))?\s*$")]
    private static partial Regex SheetCommand();

    // a paragraph that is one command on any page: "\toc" (or "\toc 2-3"), "\clear"
    [GeneratedRegex(@"^\\(toc|clear)(?:\s+(\d)(?:\s*-\s*(\d))?)?\s*$")]
    private static partial Regex PageCommand();

    /// <summary>
    /// "\toc": the contents of the page – its headings of levels 2 and 3 (\toc 2-4: 2 to 4), linked (the headings get an
    /// id); "\clear": what floats beside the text (a picture with float=right) ends here.
    /// </summary>
    private static void ReadPageCommands(string text, MarkdownDocument document, Func<int, (int, int)> locate)
    {
        foreach (ParagraphBlock paragraph in document.Descendants<ParagraphBlock>().ToList())
        {
            string source = text.Substring(paragraph.Span.Start, paragraph.Span.Length).Trim();
            if (PageCommand().Match(source) is not { Success: true } command)
            {
                continue;
            }
            string html;
            if (command.Groups[1].Value == "clear")
            {
                html = "<div class=\"clear\"></div>";
            }
            else
            {
                int from = command.Groups[2].Success ? int.Parse(command.Groups[2].Value, CultureInfo.InvariantCulture) : 2;
                int to = command.Groups[3].Success ? int.Parse(command.Groups[3].Value, CultureInfo.InvariantCulture) : command.Groups[2].Success ? from : 3;
                html = Contents(text, document, from, Math.Max(from, to));
            }
            ContainerBlock parent = paragraph.Parent!;
            int index = parent.IndexOf(paragraph);
            parent.RemoveAt(index);
            var block = new HtmlBlock(null) { Type = HtmlBlockType.NonInterruptingBlock, Line = paragraph.Line, Column = paragraph.Column, Span = paragraph.Span };
            block.Lines = new StringLineGroup(html);
            parent.Insert(index, block);
        }
    }

    // the contents: a list of the headings of these levels (nested), each a link; the headings get ids ("bildschirmausgabe-mit-print")
    private static string Contents(string text, MarkdownDocument document, int from, int to)
    {
        var html = new StringBuilder($"<nav class=\"toc\" data-levels=\"{from}-{to}\">");
        var used = new HashSet<string>(StringComparer.Ordinal);
        int depth = 0;
        foreach (HeadingBlock heading in document.Descendants<HeadingBlock>().Where(h => h.Level >= from && h.Level <= to))
        {
            string title = HeadingText(text, heading);
            string id = heading.GetAttributes().Id ?? "";
            if (id.Length == 0)
            {
                string slug = Regex.Replace(title.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss"), @"[^a-z0-9]+", "-").Trim('-');
                id = slug.Length > 0 ? slug : "abschnitt";
                for (int k = 2; !used.Add(id); k++)
                {
                    id = (slug.Length > 0 ? slug : "abschnitt") + "-" + k.ToString(CultureInfo.InvariantCulture);
                }
                heading.GetAttributes().Id = id;
            }
            // nested lists in the items (an item stays open for the deeper ones)
            int level = heading.Level - from + 1;
            if (depth >= level)
            {
                html.Append("</li>");
                for (; depth > level; depth--)
                {
                    html.Append("</ul></li>");
                }
            }
            // (deeper: a list in the open item; a level skipped: an item of its own around it)
            for (bool first = true; depth < level; depth++, first = false)
            {
                html.Append(first ? "<ul>" : "<li><ul>");
            }
            html.Append("<li><a href=\"#").Append(id).Append("\">").Append(InlineHtml(title)).Append("</a>");
        }
        if (depth > 0)
        {
            html.Append("</li>");
            for (; depth > 1; depth--)
            {
                html.Append("</ul></li>");
            }
            html.Append("</ul>");
        }
        return html.Append("</nav>").ToString();
    }

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
        ReadSheetCommands(text, document, locate, errors, tasks);
        return tasks;
    }

    /// <summary>
    /// The commands of a sheet: "\page" (a new page), "\space 3cm" (room) and, with the tasks of a test or worksheet,
    /// "\points" (the table of the points); a document (no tasks) has the first two.
    /// </summary>
    private static void ReadSheetCommands(string text, MarkdownDocument document, Func<int, (int, int)> locate, List<MarkdownProblem> errors,
        List<TestTask>? tasks)
    {
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
                "points" when tasks != null && (!command.Groups[2].Success || command.Groups[2].Value == "+") => GradeTable(tasks, plus: command.Groups[2].Success),
                "points" => "",
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
            if (html.Length == 0)
            {
                (int line, int column) = locate(paragraph.Span.Start);
                errors.Add(new MarkdownProblem(line, column, "\\points needs tasks with points: only in a test or worksheet."));
                continue;
            }
            ContainerBlock parent = paragraph.Parent!;
            int index = parent.IndexOf(paragraph);
            parent.RemoveAt(index);
            var block = new HtmlBlock(null) { Type = HtmlBlockType.NonInterruptingBlock, Line = paragraph.Line, Column = paragraph.Column, Span = paragraph.Span };
            block.Lines = new StringLineGroup(html);
            parent.Insert(index, block);
        }
    }

    // "{7 cs}" at the end of a question's heading: its points, how ILIAS rates written answers (ci, cs, l1, l2)
    [GeneratedRegex(@"\s*\{([^{}]*)\}\s*$")]
    private static partial Regex QuestionOptions();

    private static readonly string[] Ratings = ["ci", "cs", "l1", "l2"];

    /// <summary>
    /// The questions for ILIAS: every heading "## Titel {7}" one, "1. Titel (7 P)"; its kind from what it has – blanks (a
    /// cloze question: its points the sum of the blanks'), "::: match" (the points shared by its pairs) or "::: choice"
    /// (single choice); the heading has its points, the title and the rating of written answers for ILIAS (data-…).
    /// </summary>
    private static List<TestTask> ReadQuestions(string text, MarkdownDocument document, Func<int, (int, int)> locate, List<MarkdownProblem> errors)
    {
        var tasks = new List<TestTask>();
        var headings = document.Descendants<HeadingBlock>().Where(h => h.Level == 2).ToList();
        foreach (HeadingBlock heading in headings)
        {
            (int line, int column) = locate(heading.Span.Start);
            double? given = null;
            string rating = "l2";
            if (heading.Inline?.LastChild is LiteralInline last && QuestionOptions().Match(last.Content.ToString()) is { Success: true } options)
            {
                last.Content = new StringSlice(last.Content.ToString()[..options.Index]);
                foreach (string option in options.Groups[1].Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (double.TryParse(option.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double written0) && written0 > 0)
                    {
                        given = written0;
                    }
                    else if (Ratings.Contains(option))
                    {
                        rating = option;
                    }
                    else
                    {
                        errors.Add(new MarkdownProblem(line, column, $"\"{option}\" in the heading's {{…}}: the points (7) and how written answers count: ci (any case), cs (exact), l1, l2 (one or two letters may differ)."));
                    }
                }
            }
            string title = Regex.Replace(HeadingText(text, heading), @"\s*\{[^{}]*\}\s*$", "");
            // what belongs to the question: up to the next heading of its level or higher
            var blocks = new List<Block>();
            for (Block? block = NextBlock(heading); block != null && block is not HeadingBlock { Level: <= 2 }; block = NextBlock(block))
            {
                blocks.Add(block);
            }
            var gaps = new List<Gap>();
            foreach (BlankInline blank in blocks.SelectMany(b => b is ContainerBlock container ? container.Descendants<LeafBlock>() : [(LeafBlock)b])
                .SelectMany(leaf => leaf.Inline?.Descendants<BlankInline>() ?? []))
            {
                var problems = new List<string>();
                Gap gap = Gap.Of(blank, problems);
                foreach (string problem in problems)
                {
                    errors.Add(new MarkdownProblem(blank.Line + 1, blank.Column + 1, problem));
                }
                if (!gap.SolutionOnly)
                {
                    gaps.Add(gap);
                }
            }
            string html = string.Concat(blocks.OfType<HtmlBlock>().Select(b => b.Lines.ToString()));
            Match match = Regex.Match(html, "<div class=\"match\"[^>]*data-pairs=\"(\\d+)\"");
            bool choice = html.Contains("<div class=\"single-choice\">", StringComparison.Ordinal);
            int kinds = (gaps.Count > 0 ? 1 : 0) + (match.Success ? 1 : 0) + (choice ? 1 : 0);
            double points;
            if (kinds != 1)
            {
                errors.Add(new MarkdownProblem(line, column, kinds == 0
                    ? "A question for ILIAS needs blanks ([[…]]), a \"::: match\" or a \"::: choice\"."
                    : "A question for ILIAS is of one kind: blanks, a \"::: match\" or a \"::: choice\" – not more of them."));
                points = given ?? 0;
            }
            else if (gaps.Count > 0)
            {
                points = gaps.Sum(g => g.Points);
                if (given is double written && Math.Abs(written - points) > 1e-9)
                {
                    errors.Add(new MarkdownProblem(line, column, $"The blanks give {Points(points)} points, the heading says {Points(written)} (give a blank more with {{2}} after it)."));
                }
            }
            else if (match.Success)
            {
                int pairs = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                points = given ?? pairs;
                if (points < pairs || points != Math.Round(points))
                {
                    errors.Add(new MarkdownProblem(line, column, $"A matching with {pairs} pairs needs whole points, at least {pairs} (one for each pair)."));
                }
            }
            else
            {
                points = given ?? 1;
            }
            tasks.Add(new TestTask(tasks.Count + 1, title, points)
            {
                Kind = kinds != 1 ? null : gaps.Count > 0 ? "Lückentext" : match.Success ? "Zuordnung" : "Single Choice",
            });
            heading.Inline?.FirstChild?.InsertBefore(new LiteralInline(tasks.Count.ToString(CultureInfo.InvariantCulture) + ". "));
            heading.Inline?.AppendChild(new LiteralInline($" ({Points(points)} P)"));
            heading.GetAttributes().AddProperty("data-question", title);
            heading.GetAttributes().AddProperty("data-points", points.ToString(CultureInfo.InvariantCulture));
            heading.GetAttributes().AddProperty("data-rating", rating);
        }
        return tasks;
    }

    // the block after this one in the document's order (not inside it)
    private static Block? NextBlock(Block block)
    {
        while (block.Parent is { } parent)
        {
            int index = parent.IndexOf(block);
            if (index + 1 < parent.Count)
            {
                return parent[index + 1];
            }
            if (parent is MarkdownDocument)
            {
                return null;
            }
            block = parent;
        }
        return null;
    }

    /// <summary>
    /// The points of the questions for ILIAS in all groups, to compare them (the work and the one written later should
    /// be worth the same): a row for every question (by its title; "–" where a group does not have it), its kind, the
    /// sums, the sums by kind. <paramref name="page"/> is the page of <paramref name="group"/>; the others are made.
    /// </summary>
    public static string PointsOverview(string text, string? file, Func<string, AnimationData>? loadScript, MarkdownPage page, string? group)
    {
        if (page.Document is not { IsIlias: true } head || head.Groups.Count == 0)
        {
            return "";
        }
        var groups = head.Groups.Select(g => (Group: g, Tasks: g == group ? page.Tasks
            : Parse(text, file, loadScript, variant: new SheetVariant(g, true)).Tasks)).ToList();
        // the questions in their order, each once (a question of only some groups after the one before it)
        var titles = new List<string>();
        foreach ((_, IReadOnlyList<TestTask> tasks) in groups)
        {
            int after = -1;
            foreach (TestTask task in tasks)
            {
                int known = titles.IndexOf(task.Title);
                if (known < 0)
                {
                    titles.Insert(after + 1, task.Title);
                    known = after + 1;
                }
                after = known;
            }
        }
        string Cell(double? points) => points is double p ? Points(p) : "–";
        var html = new StringBuilder("<section class=\"points-overview\">\n<h2>Punkte der Gruppen</h2>\n<table>\n<thead><tr><th>Frage</th><th>Art</th>");
        foreach ((string name, _) in groups)
        {
            html.Append("<th>").Append(WebUtility.HtmlEncode(name)).Append("</th>");
        }
        html.Append("</tr></thead>\n<tbody>\n");
        foreach (string title in titles)
        {
            var found = groups.Select(g => g.Tasks.FirstOrDefault(t => t.Title == title)).ToList();
            string kinds = string.Join(", ", found.Where(t => t?.Kind != null).Select(t => t!.Kind).Distinct());
            html.Append("<tr><td>").Append(InlineHtml(title)).Append("</td><td>").Append(kinds).Append("</td>");
            foreach (TestTask? task in found)
            {
                html.Append("<td>").Append(Cell(task?.Points)).Append("</td>");
            }
            html.Append("</tr>\n");
        }
        var sums = groups.Select(g => g.Tasks.Sum(t => t.Points)).ToList();
        html.Append("<tr class=\"points-sum\"><td>Summe</td><td></td>");
        foreach (double sum in sums)
        {
            html.Append("<td>").Append(Points(sum)).Append("</td>");
        }
        html.Append("</tr>\n");
        foreach (string kind in groups.SelectMany(g => g.Tasks).Select(t => t.Kind).OfType<string>().Distinct())
        {
            html.Append("<tr class=\"points-kind\"><td>davon</td><td>").Append(kind).Append("</td>");
            foreach ((_, IReadOnlyList<TestTask> tasks) in groups)
            {
                html.Append("<td>").Append(Points(tasks.Where(t => t.Kind == kind).Sum(t => t.Points))).Append("</td>");
            }
            html.Append("</tr>\n");
        }
        html.Append("</tbody>\n</table>\n");
        if (sums.Distinct().Count() > 1)
        {
            html.Append("<p class=\"points-differ\">Die Summen sind verschieden: ")
                .Append(string.Join(", ", groups.Select((g, i) => WebUtility.HtmlEncode(g.Group) + " " + Points(sums[i])))).Append(".</p>\n");
        }
        return html.Append("</section>\n").ToString();
    }

    // the grades by the share of the points (percent from which a grade is given): plain and with "+"
    private static readonly (string Grade, int Percent)[] Grades = [("1", 96), ("2", 80), ("3", 60), ("4", 40), ("5", 20)];
    private static readonly (string Grade, int Percent)[] PlusGrades =
        [("1+", 97), ("1", 94), ("2+", 90), ("2", 80), ("3+", 75), ("3", 60), ("4", 40), ("5", 20)];

    // the line for the points and the grade, and the grades with the points they need (of the sum without the extra
    // tasks, rounded up)
    private static string GradeTable(List<TestTask> tasks, bool plus)
    {
        double total = tasks.Where(t => t.Number != null).Sum(t => t.Points);
        var html = new StringBuilder("<p class=\"grade-line\"><span class=\"grade-blank\"></span> / ").Append(Points(total))
            .Append(" Punkte<span class=\"grade-gap\"></span>Note: <span class=\"grade-blank\"></span></p>\n");
        html.Append("<table class=\"points-table\"><tbody><tr><th>Note</th>");
        (string Grade, int Percent)[] grades = plus ? PlusGrades : Grades;
        foreach ((string grade, _) in grades)
        {
            html.Append("<td>").Append(grade).Append("</td>");
        }
        html.Append("</tr><tr><th>ab Punkte</th>");
        foreach ((_, int percent) in grades)
        {
            html.Append("<td>").Append(Points(Math.Ceiling(percent * total / 100))).Append("</td>");
        }
        return html.Append("</tr></tbody></table>").ToString();
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
        if (head.IsIlias)
        {
            info.Add("ILIAS");
        }
        else if (variant.Solution)
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
        string[] fields = head.IsIlias || head.IsDocument ? [] : head.IsTest ? ["Name", "Klasse", "Datum"] : ["Name", "Datum"];
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
