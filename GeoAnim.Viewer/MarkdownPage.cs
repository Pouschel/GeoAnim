using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using GeoAnim.Formulas;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace GeoAnim.Viewer;

/// <summary>A mistake in a Markdown page: line and column from 1.</summary>
public sealed record MarkdownProblem(int Line, int Column, string Message);

/// <summary>
/// A figure from a script: "![caption](file.geo)", "![](file.geo#2)", "![](file.geo#3..4)" (steps 3 to 4) or
/// "![](file.geo#verschieden..gleich)" (steps by their mark or the name made from their title). <see cref="From"/> and
/// <see cref="To"/> are null for the whole animation; a single step has both the same.
/// </summary>
public sealed record GeoReference(string File, string? From, string? To, string Caption, int Line, int Column)
{
    /// <summary>The address as written: file and steps.</summary>
    public string Source => From == null ? File : File + "#" + (From == To ? From : From + ".." + To);
}

/// <summary>
/// A page of a book in Markdown (CommonMark with tables, footnotes and attributes): a YAML block at the start
/// ("front matter": title, description, keywords), formulas as AsciiMath between $…$ (block formulas between $$…$$),
/// figures from .geo scripts. <see cref="Parse"/> turns it into HTML: formulas as SVG (as in the player), figures as
/// placeholders for now; the blocks carry their line ("data-line") for an editor's preview.
/// </summary>
public sealed partial class MarkdownPage
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UseMathematics()
        .UsePipeTables()
        .UseFootnotes()
        .UseGenericAttributes()
        .UsePreciseSourceLocation()
        .Build();

    private static readonly string[] FrontMatterKeys = ["title", "description", "keywords"];

    private MarkdownPage(string html)
    {
        Html = html;
    }

    /// <summary>The title from the front matter, or null.</summary>
    public string? FrontTitle { get; private set; }

    /// <summary>The text of the first heading "# …" (Markdown, formulas as $…$), or null.</summary>
    public string? Heading { get; private set; }

    /// <summary>The title of the page (menu, table of contents, page head): the front matter's, else the first heading.</summary>
    public string? Title => FrontTitle ?? Heading;

    /// <summary>The description from the front matter, or null.</summary>
    public string? Description { get; private set; }

    /// <summary>The keywords from the front matter, as written ("Punkt/e/en").</summary>
    public IReadOnlyList<string> Keywords { get; private set; } = [];

    /// <summary>The figures from scripts, in their order.</summary>
    public IReadOnlyList<GeoReference> Figures { get; private set; } = [];

    /// <summary>The page body as HTML.</summary>
    public string Html { get; }

    /// <summary>Mistakes: the page is still made, with the faulty parts marked.</summary>
    public IReadOnlyList<MarkdownProblem> Errors { get; private set; } = [];

    /// <summary>Doubtful things (e.g. an unknown key in the front matter).</summary>
    public IReadOnlyList<MarkdownProblem> Warnings { get; private set; } = [];

    /// <summary>
    /// Reads a page. <paramref name="file"/> (its full path) is for the figures: their scripts are looked for beside it;
    /// without it they are not checked.
    /// </summary>
    public static MarkdownPage Parse(string text, string? file = null)
    {
        var errors = new List<MarkdownProblem>();
        var warnings = new List<MarkdownProblem>();
        MarkdownDocument document = Markdown.Parse(text, Pipeline);
        Func<int, (int, int)> locate = Locator(text);

        // the front matter
        string? title = null, description = null;
        var keywords = new List<string>();
        if (document.Descendants<YamlFrontMatterBlock>().FirstOrDefault() is { } front)
        {
            ReadFrontMatter(front, ref title, ref description, keywords, errors, warnings);
            front.Parent?.Remove(front);
        }

        string? heading = document.Descendants<HeadingBlock>().FirstOrDefault(h => h.Level == 1) is { } first
            ? HeadingText(text, first)
            : null;

        // paragraphs of figures only become figure blocks
        var figures = new List<GeoReference>();
        string? folder = file == null ? null : Path.GetDirectoryName(Path.GetFullPath(file));
        foreach (ParagraphBlock paragraph in document.Descendants<ParagraphBlock>().ToList())
        {
            if (FiguresOf(paragraph) is not { } images)
            {
                continue;
            }
            var references = new List<GeoReference>();
            var shown = new List<(string Source, GeoReference? Reference)>();
            foreach (LinkInline image in images)
            {
                GeoReference? reference = ReferenceOf(text, image, locate, errors);
                shown.Add((image.Url!, reference));
                if (reference == null)
                {
                    continue;
                }
                if (folder != null && !File.Exists(Path.Combine(folder, reference.File.Replace('/', Path.DirectorySeparatorChar))))
                {
                    errors.Add(new MarkdownProblem(reference.Line, reference.Column, $"The script \"{reference.File}\" does not exist."));
                }
                references.Add(reference);
            }
            figures.AddRange(references);
            ContainerBlock parent = paragraph.Parent!;
            int index = parent.IndexOf(paragraph);
            parent.RemoveAt(index);
            parent.Insert(index, new GeoFigureBlock(shown) { Line = paragraph.Line, Column = paragraph.Column, Span = paragraph.Span });
        }

        // a "$" that does not start a formula (no closing one, or a space after it): as in scripts a mistake
        foreach (LiteralInline literal in document.Descendants<LiteralInline>())
        {
            if (literal.IsFirstCharacterEscaped && literal.Content.Length == 1)
            {
                continue;
            }
            int dollar = literal.Content.ToString().IndexOf('$', literal.IsFirstCharacterEscaped ? 1 : 0);
            if (dollar >= 0)
            {
                // the escaped "$" is written "\$": one character more before the others
                (int line, int column) = locate(literal.Span.Start + dollar + (literal.IsFirstCharacterEscaped ? 1 : 0));
                errors.Add(new MarkdownProblem(line, column,
                    "This $ starts no formula: a formula needs a closing $ and no space after the first one (write \\$ for a dollar sign)."));
            }
        }

        // every block with its line, for an editor's preview to scroll along
        foreach (Block block in document.Descendants<Block>())
        {
            if (block is not ListBlock and not Markdig.Extensions.Footnotes.FootnoteGroup)
            {
                block.GetAttributes().AddPropertyIfNotExist("data-line", (block.Line + 1).ToString(CultureInfo.InvariantCulture));
            }
        }

        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.Replace<HtmlMathInlineRenderer>(new FormulaInlineRenderer(errors, locate));
        renderer.ObjectRenderers.Replace<HtmlMathBlockRenderer>(new FormulaBlockRenderer(errors, locate));
        renderer.ObjectRenderers.Insert(0, new GeoFigureRenderer());
        renderer.Render(document);
        writer.Flush();

        return new MarkdownPage(writer.ToString())
        {
            FrontTitle = title,
            Heading = heading,
            Description = description,
            Keywords = keywords,
            Figures = figures,
            Errors = errors.OrderBy(e => e.Line).ThenBy(e => e.Column).ToList(),
            Warnings = warnings,
        };
    }

    private static void ReadFrontMatter(YamlFrontMatterBlock front, ref string? title, ref string? description, List<string> keywords,
        List<MarkdownProblem> errors, List<MarkdownProblem> warnings)
    {
        // the YAML between the lines "---"; its lines count from the line after the first "---"
        int firstLine = front.Line + 2;
        string yaml = string.Join("\n", front.Lines.Lines.Take(front.Lines.Count).Select(l => l.Slice.ToString()));
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException e)
        {
            errors.Add(new MarkdownProblem(firstLine + (int)e.Start.Line - 1, (int)e.Start.Column, "Front matter: " + e.Message));
            return;
        }
        if (stream.Documents.Count == 0)
        {
            return;
        }
        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            errors.Add(new MarkdownProblem(firstLine, 1, "The front matter must be keys with values (\"title: …\")."));
            return;
        }
        foreach ((YamlNode keyNode, YamlNode value) in root.Children)
        {
            string key = keyNode is YamlScalarNode scalarKey ? scalarKey.Value ?? "" : "";
            int line = firstLine + (int)keyNode.Start.Line - 1;
            int column = (int)keyNode.Start.Column;
            switch (key)
            {
                case "title" or "description":
                    if (value is not YamlScalarNode { Value: { } scalar })
                    {
                        errors.Add(new MarkdownProblem(line, column, $"\"{key}\" must be a text."));
                    }
                    else if (key == "title")
                    {
                        title = scalar.Trim();
                    }
                    else
                    {
                        description = scalar.Trim();
                    }
                    break;
                case "keywords":
                    // a list ([Punkt/e/en, Lage] or one per line with "-"), or one keyword
                    IEnumerable<YamlNode> items = value is YamlSequenceNode sequence ? sequence.Children : [value];
                    foreach (YamlNode item in items)
                    {
                        if (item is YamlScalarNode { Value: { } word } && word.Trim().Length > 0)
                        {
                            keywords.Add(word.Trim());
                        }
                        else
                        {
                            errors.Add(new MarkdownProblem(firstLine + (int)item.Start.Line - 1, (int)item.Start.Column, "A keyword must be a word (\"Punkt/e/en\")."));
                        }
                    }
                    break;
                default:
                    warnings.Add(new MarkdownProblem(line, column,
                        $"Unknown key \"{key}\" in the front matter (known: {string.Join(", ", FrontMatterKeys)})."));
                    break;
            }
        }
    }

    // line and column (from 1) of a position in the text
    private static Func<int, (int, int)> Locator(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }
        return position =>
        {
            int index = starts.BinarySearch(Math.Max(0, position));
            int line = index >= 0 ? index : ~index - 1;
            return (line + 1, position - starts[line] + 1);
        };
    }

    // the heading as written, without the marks "#" before (and after) it
    private static string HeadingText(string text, HeadingBlock heading)
    {
        string line = text.Substring(heading.Span.Start, heading.Span.Length);
        return HeadingMarks().Replace(line.Trim(), "").Trim();
    }

    [GeneratedRegex(@"^#+\s*|\s+#+$")]
    private static partial Regex HeadingMarks();

    // the images of a paragraph that has nothing else (besides spaces and line breaks), if they are all scripts
    private static List<LinkInline>? FiguresOf(ParagraphBlock paragraph)
    {
        if (paragraph.Inline == null)
        {
            return null;
        }
        var images = new List<LinkInline>();
        foreach (Inline inline in paragraph.Inline)
        {
            switch (inline)
            {
                case LinkInline { IsImage: true } image when IsScript(image.Url):
                    images.Add(image);
                    break;
                case LineBreakInline:
                case LiteralInline literal when literal.Content.IsEmptyOrWhitespace():
                    break;
                default:
                    return null;
            }
        }
        return images.Count > 0 ? images : null;
    }

    private static bool IsScript(string? url) =>
        url != null && Regex.IsMatch(url.Split('#')[0], @"\.geo$", RegexOptions.IgnoreCase);

    // "file.geo", "file.geo#2", "file.geo#3..4" (also "3-4"), "file.geo#name", "file.geo#from..to"
    private static GeoReference? ReferenceOf(string text, LinkInline image, Func<int, (int, int)> locate, List<MarkdownProblem> errors)
    {
        string url = image.Url!;
        (int line, int column) = locate(image.Span.Start);
        string caption = image.FirstChild == null ? "" : CaptionOf(text, image);
        int hash = url.IndexOf('#');
        string file = Uri.UnescapeDataString(hash < 0 ? url : url.Substring(0, hash));
        if (hash < 0)
        {
            return new GeoReference(file, null, null, caption, line, column);
        }
        string steps = url.Substring(hash + 1);
        Match match = StepPattern().Match(steps);
        if (!match.Success)
        {
            errors.Add(new MarkdownProblem(line, column,
                $"\"#{steps}\" is no step: write a number (#2), a name (#gleich) or a range (#3..4, #verschieden..gleich)."));
            return null;
        }
        string from = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[3].Value;
        string to = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[4].Success ? match.Groups[4].Value : from;
        return new GeoReference(file, from, to, caption, line, column);
    }

    // "3..4", "3-4" (numbers only: names have hyphens), "verschieden..gleich", "2", "gleich"
    [GeneratedRegex(@"^(?:(\d+)-(\d+)|([\p{L}\p{N}_-]+)(?:\.\.([\p{L}\p{N}_-]+))?)$")]
    private static partial Regex StepPattern();

    // the text between "![" and "]" as written
    private static string CaptionOf(string text, LinkInline image)
    {
        int start = image.FirstChild!.Span.Start;
        Inline last = image.LastChild!;
        int end = last.Span.End;
        return start >= 0 && end >= start ? text.Substring(start, end - start + 1).Trim() : "";
    }

    // ------------------------------------------------------------------ formulas

    /// <summary>A formula as an svg element in the text, sized in em like the player's (it scales with the font).</summary>
    internal static string FormulaSvg(string asciiMath)
    {
        Formula formula = MathRenderer.Render(asciiMath);
        string Em(double thousandths) => (thousandths / 1000).ToString("0.###", CultureInfo.InvariantCulture) + "em";
        string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        return $"<svg class=\"formula\" viewBox=\"{Number(formula.X)} {Number(formula.Y)} {Number(formula.Width)} {Number(formula.Height)}\""
            + $" width=\"{Em(formula.Width)}\" height=\"{Em(formula.Height)}\" style=\"vertical-align:{Em(-(formula.Y + formula.Height))}\""
            + $" overflow=\"visible\" role=\"img\" aria-label=\"{WebUtility.HtmlEncode(asciiMath)}\">{formula.Svg}</svg>";
    }

    private static string FaultyFormula(string asciiMath) =>
        "<span class=\"formula-error\">$" + WebUtility.HtmlEncode(asciiMath) + "$</span>";

    private sealed class FormulaInlineRenderer(List<MarkdownProblem> errors, Func<int, (int, int)> locate) : HtmlObjectRenderer<MathInline>
    {
        protected override void Write(HtmlRenderer renderer, MathInline math)
        {
            string source = math.Content.ToString().Trim();
            try
            {
                renderer.Write(FormulaSvg(source));
            }
            catch (FormatException e)
            {
                (int line, int column) = locate(math.Span.Start);
                errors.Add(new MarkdownProblem(line, column, e.Message));
                renderer.Write(FaultyFormula(source));
            }
        }
    }

    // $$…$$ on lines of their own: a formula centred on a line of its own
    private sealed class FormulaBlockRenderer(List<MarkdownProblem> errors, Func<int, (int, int)> locate) : HtmlObjectRenderer<MathBlock>
    {
        protected override void Write(HtmlRenderer renderer, MathBlock math)
        {
            string source = string.Join(" ", math.Lines.Lines.Take(math.Lines.Count).Select(l => l.Slice.ToString().Trim())).Trim();
            renderer.Write("<div class=\"formula-block\"").WriteAttributes(math).Write('>');
            try
            {
                renderer.Write(FormulaSvg(source));
            }
            catch (FormatException e)
            {
                (int line, int column) = locate(math.Span.Start);
                errors.Add(new MarkdownProblem(line, column, e.Message));
                renderer.Write(FaultyFormula(source));
            }
            renderer.WriteLine("</div>");
        }
    }

    // ------------------------------------------------------------------ figures

    // the figures of a paragraph; a faulty address has no reference
    private sealed class GeoFigureBlock(List<(string Source, GeoReference? Reference)> figures) : LeafBlock(null)
    {
        public List<(string Source, GeoReference? Reference)> Figures { get; } = figures;
    }

    // for now a placeholder with the address (the player and the pictures come later)
    private sealed class GeoFigureRenderer : HtmlObjectRenderer<GeoFigureBlock>
    {
        protected override void Write(HtmlRenderer renderer, GeoFigureBlock figure)
        {
            renderer.Write("<figure class=\"geo-figure\"").WriteAttributes(figure).WriteLine('>');
            var captions = new StringBuilder();
            foreach ((string source, GeoReference? reference) in figure.Figures)
            {
                if (reference == null)
                {
                    renderer.Write("<span class=\"geo-placeholder geo-faulty\">").WriteEscape(source).WriteLine("</span>");
                    continue;
                }
                renderer.Write("<a class=\"geo-placeholder\" href=\"").WriteEscapeUrl(reference.Source).Write("\">")
                    .WriteEscape(reference.Source).WriteLine("</a>");
                if (reference.Caption.Length > 0)
                {
                    captions.Append(captions.Length > 0 ? " · " : "").Append(reference.Caption);
                }
            }
            if (captions.Length > 0)
            {
                // the caption is Markdown too (formulas, emphasis)
                string html = Markdown.ToHtml(captions.ToString(), Pipeline).Trim();
                html = Regex.Replace(html, "^<p>|</p>$", "");
                renderer.Write("<figcaption>").Write(RenderFormulasIn(html)).WriteLine("</figcaption>");
            }
            renderer.WriteLine("</figure>");
        }
    }

    // the formulas Markdig leaves in a caption (<span class="math">\(…\)</span>) as SVG
    private static string RenderFormulasIn(string html) => MathSpan().Replace(html, m =>
    {
        string source = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
        try
        {
            return FormulaSvg(source);
        }
        catch (FormatException)
        {
            return FaultyFormula(source);
        }
    });

    [GeneratedRegex(@"<span class=""math"">\\\((.*?)\\\)</span>")]
    private static partial Regex MathSpan();
}
