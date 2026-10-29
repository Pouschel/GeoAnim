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
/// "![](file.geo#verschieden..gleich)" (steps by their mark or the name made from their title); "![](#2)" and "![](#)"
/// refer to the page's own script (seite.geo for seite.geo.md). <see cref="From"/> and <see cref="To"/> are null for the
/// whole animation; a single step has both the same. <see cref="Crop"/>: cut to what it shows in the book (attribute
/// "{crop=no}": the whole view).
/// </summary>
public sealed record GeoReference(string File, string? From, string? To, string Caption, int Line, int Column, bool Crop = true)
{
    /// <summary>The address as written: file and steps.</summary>
    public string Source => From == null ? File : File + "#" + (From == To ? From : From + ".." + To);
}

/// <summary>
/// A figure of a page with its animation (JSON for the player): <see cref="From"/> to <see cref="To"/> are the steps shown
/// (indices from 0; both null: the whole animation), <see cref="Still"/> only the picture at the end of <see cref="To"/>
/// (-1: the starting picture). <see cref="Key"/> stays the same as long as all of that does; <see cref="Ratio"/> is the
/// picture's width by its height.
/// </summary>
public sealed record PageFigure(string Key, GeoReference Reference, string AnimationJson, int? From, int? To, bool Still, double Ratio)
{
    /// <summary>The description of the step a still shows (for a caption), or null.</summary>
    public string? StepCaption { get; init; }

    /// <summary>Cut to what it shows (in the book).</summary>
    public bool Crop => Reference.Crop;
}

/// <summary>
/// A page of a book in Markdown (CommonMark with tables, footnotes and attributes): a YAML block at the start
/// ("front matter": title, description, keywords), formulas as AsciiMath between $…$ (block formulas between $$…$$),
/// figures from .geo scripts. <see cref="Parse"/> turns it into HTML: formulas as SVG (as in the player), figures as
/// elements for the player (<see cref="PageFigures"/>, "data-figure" with their key); the blocks carry their line
/// ("data-line") for an editor's preview.
/// </summary>
public sealed partial class MarkdownPage
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UseMathematics()
        .UsePipeTables()
        .UseFootnotes()
        .UseCustomContainers()
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

    /// <summary>The figures with their animations (only with a loader for the scripts), in their order.</summary>
    public IReadOnlyList<PageFigure> PageFigures { get; private set; } = [];

    /// <summary>The marks of the page (figures, statements, sections with "{#name}"), in their order.</summary>
    public IReadOnlyList<PageMark> Marks { get; private set; } = [];

    /// <summary>The page body as HTML.</summary>
    public string Html { get; }

    /// <summary>Mistakes: the page is still made, with the faulty parts marked.</summary>
    public IReadOnlyList<MarkdownProblem> Errors { get; private set; } = [];

    /// <summary>Doubtful things (e.g. an unknown key in the front matter).</summary>
    public IReadOnlyList<MarkdownProblem> Warnings { get; private set; } = [];

    /// <summary>
    /// Reads a page. <paramref name="file"/> (its full path) is for the figures: their scripts are looked for beside it;
    /// without it they are not checked. <paramref name="loadScript"/> compiles a script (by its full path) for its figure;
    /// it throws <see cref="FormatException"/> with the message for a faulty one. Without it the figures stay
    /// placeholders. <paramref name="bookMarks"/> are the marks of the book's other pages, for references to them.
    /// </summary>
    public static MarkdownPage Parse(string text, string? file = null, Func<string, AnimationData>? loadScript = null,
        IReadOnlyDictionary<string, BookMark>? bookMarks = null) => Build(text, file, loadScript, bookMarks, render: true);

    /// <summary>The marks of a page (for the references of the other pages), without making its HTML.</summary>
    public static IReadOnlyList<PageMark> ReadMarks(string text) => Build(text, null, null, null, render: false).Marks;

    private static MarkdownPage Build(string text, string? file, Func<string, AnimationData>? loadScript,
        IReadOnlyDictionary<string, BookMark>? bookMarks, bool render)
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
        var pageFigures = new List<PageFigure>();
        string? folder = file == null ? null : Path.GetDirectoryName(Path.GetFullPath(file));
        foreach (ParagraphBlock paragraph in document.Descendants<ParagraphBlock>().ToList())
        {
            if (FiguresOf(paragraph) is not { } images)
            {
                continue;
            }
            var references = new List<GeoReference>();
            var shown = new List<FigureItem>();
            var figureOptions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (LinkInline image in images)
            {
                GeoReference? reference = ReferenceOf(text, file, image, locate, errors, warnings, figureOptions);
                if (reference == null)
                {
                    shown.Add(new FigureItem(image, image.Url!, null, null, Faulty: true));
                    continue;
                }
                references.Add(reference);
                string? script = folder == null ? null : Path.Combine(folder, reference.File.Replace('/', Path.DirectorySeparatorChar));
                if (script != null && !File.Exists(script))
                {
                    errors.Add(new MarkdownProblem(reference.Line, reference.Column, $"The script \"{reference.File}\" does not exist."));
                    shown.Add(new FigureItem(image, image.Url!, reference, null, Faulty: true));
                    continue;
                }
                PageFigure? figure = null;
                if (script != null && loadScript != null)
                {
                    try
                    {
                        figure = FigureOf(reference, loadScript(script));
                        pageFigures.Add(figure);
                    }
                    catch (FormatException e)
                    {
                        errors.Add(new MarkdownProblem(reference.Line, reference.Column, e.Message));
                    }
                    catch (IOException e)
                    {
                        errors.Add(new MarkdownProblem(reference.Line, reference.Column, $"{reference.File}: {e.Message}"));
                    }
                }
                shown.Add(new FigureItem(image, image.Url!, reference, figure, Faulty: figure == null && loadScript != null && script != null));
            }
            figures.AddRange(references);
            ContainerBlock parent = paragraph.Parent!;
            int index = parent.IndexOf(paragraph);
            parent.RemoveAt(index);
            var block = new GeoFigureBlock(shown) { Line = paragraph.Line, Column = paragraph.Column, Span = paragraph.Span };
            // float and width (for the web and e-books later; in print the figure stays centred)
            foreach ((string key, string value) in figureOptions)
            {
                block.GetAttributes().AddProperty("data-" + key, value);
            }
            parent.Insert(index, block);
        }

        // numbers and marks of figures, statements and sections; then the references to them
        List<PageMark> marks = Number(text, document, locate, errors, render);
        if (!render)
        {
            return new MarkdownPage("") { Marks = marks };
        }
        string? page = file == null ? null : Path.GetFullPath(file);
        CheckMarks(marks, page, bookMarks, errors);
        ResolveReferences(document, locate, marks, page, bookMarks, errors);

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
        renderer.ObjectRenderers.Replace<Markdig.Extensions.CustomContainers.HtmlCustomContainerRenderer>(new StatementRenderer());
        renderer.ObjectRenderers.Insert(0, new MarkReferenceRenderer());
        renderer.Render(document);
        writer.Flush();

        return new MarkdownPage(writer.ToString())
        {
            FrontTitle = title,
            Heading = heading,
            Description = description,
            Keywords = keywords,
            Figures = figures,
            PageFigures = pageFigures,
            Marks = marks,
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

    // a script ("file.geo…") or the page's own ("#…": an image cannot point to a place on its page)
    private static bool IsScript(string? url) =>
        url != null && (url.StartsWith('#') || Regex.IsMatch(url.Split('#')[0], @"\.geo$", RegexOptions.IgnoreCase));

    // the attributes of a figure: "{crop=no}" for the image, "{float=right width=40%}" for the whole figure
    private static readonly string[] FigureAttributes = ["crop", "float", "width"];

    // "file.geo", "file.geo#2", "file.geo#3..4" (also "3-4"), "file.geo#name", "file.geo#from..to"; "#…" the page's own
    // script
    private static GeoReference? ReferenceOf(string text, string? page, LinkInline image, Func<int, (int, int)> locate,
        List<MarkdownProblem> errors, List<MarkdownProblem> warnings, Dictionary<string, string> figureOptions)
    {
        string url = image.Url!;
        (int line, int column) = locate(image.Span.Start);
        string caption = image.FirstChild == null ? "" : CaptionOf(text, image);
        bool crop = true;
        foreach ((string key, string? raw) in image.TryGetAttributes()?.Properties ?? [])
        {
            string value = (raw ?? "").Trim();
            switch (key)
            {
                case "crop" when value is "yes" or "no":
                    crop = value == "yes";
                    break;
                case "float" when value is "left" or "right":
                case "width" when WidthPattern().IsMatch(value):
                    if (figureOptions.TryGetValue(key, out string? other) && other != value)
                    {
                        errors.Add(new MarkdownProblem(line, column, $"The images of this figure have different values for \"{key}\" ({other}, {value})."));
                    }
                    figureOptions[key] = value;
                    break;
                case "crop" or "float" or "width":
                    errors.Add(new MarkdownProblem(line, column, key switch
                    {
                        "crop" => $"\"crop={value}\": write crop=yes or crop=no.",
                        "float" => $"\"float={value}\": write float=left or float=right.",
                        _ => $"\"width={value}\": write a width like width=40% or width=60mm.",
                    }));
                    break;
                default:
                    warnings.Add(new MarkdownProblem(line, column,
                        $"Unknown attribute \"{key}\" of a figure (known: {string.Join(", ", FigureAttributes)})."));
                    break;
            }
        }
        int hash = url.IndexOf('#');
        string file = Uri.UnescapeDataString(hash < 0 ? url : url.Substring(0, hash));
        if (hash == 0)
        {
            // the page's own script: seite.geo for seite.geo.md
            string? name = page == null ? null : Path.GetFileName(page);
            if (name == null || !name.EndsWith(".geo.md", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new MarkdownProblem(line, column, name == null
                    ? $"\"{url}\" means the page's own script: the page must be saved first."
                    : $"\"{url}\" means the page's own script, but \"{name}\" is not named after one (seite.geo.md for seite.geo)."));
                return null;
            }
            file = name.Substring(0, name.Length - ".md".Length);
        }
        if (hash < 0 || hash == url.Length - 1)
        {
            return new GeoReference(file, null, null, caption, line, column, crop);
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
        return new GeoReference(file, from, to, caption, line, column, crop);
    }

    [GeneratedRegex(@"^\d+(\.\d+)?(%|mm)$")]
    private static partial Regex WidthPattern();

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
    // a figure of a paragraph: its address, its reference (null: a faulty address) and its animation (null: none loaded)
    private sealed record FigureItem(LinkInline Image, string Source, GeoReference? Reference, PageFigure? Figure, bool Faulty);

    // the figures of a paragraph
    private sealed class GeoFigureBlock(List<FigureItem> items) : LeafBlock(null)
    {
        public List<FigureItem> Items { get; } = items;

        /// <summary>"Abb. 3"</summary>
        public string Label { get; set; } = "";
    }

    // the figures side by side, each an element for the player (data-figure: its key; --ratio: the picture's width by its
    // height); without an animation a box with the address
    private sealed class GeoFigureRenderer : HtmlObjectRenderer<GeoFigureBlock>
    {
        protected override void Write(HtmlRenderer renderer, GeoFigureBlock figure)
        {
            renderer.Write("<figure class=\"geo-figure\"").WriteAttributes(figure).WriteLine('>');
            renderer.WriteLine("<div class=\"geo-row\">");
            var captions = new StringBuilder();
            foreach (FigureItem item in figure.Items)
            {
                if (item.Figure is { } loaded)
                {
                    renderer.Write("<div class=\"geo-item ").Write(loaded.Still ? "geo-still" : "geo-player")
                        .Write("\" data-figure=\"").Write(loaded.Key)
                        .Write("\" style=\"--ratio: ").Write(loaded.Ratio.ToString("0.####", CultureInfo.InvariantCulture)).WriteLine("\"></div>");
                }
                else
                {
                    renderer.Write("<span class=\"geo-placeholder").Write(item.Faulty ? " geo-faulty" : "").Write("\">")
                        .WriteEscape(item.Reference?.Source ?? item.Source).WriteLine("</span>");
                }
                if (item.Reference is { Caption.Length: > 0 } reference)
                {
                    captions.Append(captions.Length > 0 ? " · " : "").Append(reference.Caption);
                }
            }
            renderer.WriteLine("</div>");
            // without a caption of its own: a still's step description from the script
            if (captions.Length == 0 && figure.Items is [{ Figure.StepCaption: { Length: > 0 } stepCaption }])
            {
                captions.Append(stepCaption);
            }
            renderer.Write("<figcaption><span class=\"figure-label\">").WriteEscape(figure.Label).Write("</span>");
            if (captions.Length > 0)
            {
                // the caption is Markdown too (formulas, emphasis)
                renderer.Write(" ").Write(InlineHtml(captions.ToString()));
            }
            renderer.WriteLine("</figcaption>");
            renderer.WriteLine("</figure>");
        }
    }

    // ------------------------------------------------------------------ steps of a figure

    // the figure for a reference: the whole animation, a still (one step) or the steps from … to
    private static PageFigure FigureOf(GeoReference reference, AnimationData animation)
    {
        int? from = null, to = null;
        bool still = false;
        if (reference.From != null)
        {
            from = StepIndex(reference, animation, reference.From);
            to = StepIndex(reference, animation, reference.To!);
            still = reference.From == reference.To;
            if (!still && from < 0)
            {
                throw new FormatException($"{reference.File}: a range of steps starts at step 1 or later (#0 is the starting picture).");
            }
            if (to < from)
            {
                throw new FormatException($"{reference.File}: the steps #{reference.From}..{reference.To} are in the wrong order.");
            }
        }
        string json = animation.ToJson();
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
            json + "|" + from + "|" + to + "|" + still + "|" + reference.Crop)))[..16].ToLowerInvariant();
        ViewWindow view = animation.View;
        double ratio = (view.XMax - view.XMin) / (view.YMax - view.YMin);
        string? stepCaption = still && to >= 0 ? animation.Steps[to.Value].Caption : null;
        return new PageFigure(key, reference, json, from, to, still, ratio) { StepCaption = stepCaption };
    }

    /// <summary>
    /// The index (from 0) of a step by its number (from 1; 0: the starting picture, -1), its mark ("step … #gleich") or
    /// its name (the step's text in small letters, with "-" between the words: "Gleiche Punkte" → "gleiche-punkte").
    /// </summary>
    internal static int StepIndex(GeoReference reference, AnimationData animation, string step)
    {
        IReadOnlyList<AnimationStep> steps = animation.Steps;
        if (step.All(char.IsAsciiDigit))
        {
            int number = int.Parse(step, CultureInfo.InvariantCulture);
            if (number > steps.Count)
            {
                throw new FormatException($"{reference.File} has {steps.Count} steps, not {number}.");
            }
            return number - 1;
        }
        int marked = steps.ToList().FindIndex(s => s.Mark == step);
        if (marked >= 0)
        {
            return marked;
        }
        List<int> named = Enumerable.Range(0, steps.Count).Where(i => StepName(steps[i].Caption) == step).ToList();
        if (named.Count == 1)
        {
            return named[0];
        }
        if (named.Count > 1)
        {
            throw new FormatException($"{reference.File}: the steps {named[0] + 1} and {named[1] + 1} are both called \"{step}\"; give one a mark (\"step … #name\").");
        }
        string known = string.Join(", ", steps.Select(s => "#" + (s.Mark ?? StepName(s.Caption))).Where(n => n.Length > 1));
        throw new FormatException($"{reference.File} has no step \"#{step}\"" + (known.Length > 0 ? $" (there are {known})." : "."));
    }

    /// <summary>The name of a step from its text: "Gleiche Punkte" → "gleiche-punkte", "Die Strecke $bar(AB)$" → "die-strecke-ab".</summary>
    public static string StepName(string caption)
    {
        string first = caption.Split('\n')[0];
        string plain = HtmlExporter.PlainText(DecorationPattern().Replace(first, d => d.Groups[2].Value)).ToLowerInvariant();
        var name = new StringBuilder();
        foreach (char c in plain.Normalize(NormalizationForm.FormC))
        {
            name.Append(c switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'ß' => "ss",
                _ when char.IsAsciiLetterOrDigit(c) => c.ToString(),
                // é → e: the letter without its accent; anything else separates words
                _ when char.IsLetter(c) => string.Concat(c.ToString().Normalize(NormalizationForm.FormD).Where(char.IsAsciiLetter)),
                _ => "-",
            });
        }
        return Regex.Replace(name.ToString(), "-{2,}", "-").Trim('-');
    }

    // bar(AB) → AB (in names of steps)
    [GeneratedRegex(@"\b(bar|vec|hat|overline|ul|tilde|dot)\(([^()]*)\)")]
    private static partial Regex DecorationPattern();

    // a line of Markdown (a caption, a title) as HTML within a paragraph, formulas as SVG
    internal static string InlineHtml(string markdown)
    {
        string html = Markdown.ToHtml(markdown, Pipeline).Trim();
        return RenderFormulasIn(Regex.Replace(html, "^<p>|</p>$", ""));
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
