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
    /// <summary>The figure's size in print: a unit's length ("{scale=6mm}"), or null.</summary>
    public string? Scale { get; init; }

    /// <summary>The figure's width in print after cutting ("{width=8cm}"; a percentage is for the web), or null.</summary>
    public string? Width { get; init; }

    /// <summary>"{nowarn}": once no warning about the size of its texts in print; there is none any more (the print view shows them), it is still allowed.</summary>
    public bool NoWarn { get; init; }

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
        .Use<BlankExtension>()
        .Build();

    private static readonly string[] FrontMatterKeys = ["title", "description", "keywords", "type", "subject", "time", "groups", "seed", "generators"];

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
    /// placeholders. <paramref name="bookMarks"/> are the marks of the book's other pages, for references to them;
    /// <paramref name="numbering"/> the numbers of the page in the book (its section, the counts of the pages before it).
    /// </summary>
    /// <paramref name="usedIn"/>: where the marks of the book are used (<see cref="BookUses"/>); given, a statement with a
    /// mark shows them under it (for the author: in the preview and the book as one HTML file). <paramref name="variant"/>:
    /// for a test or worksheet which sheet (default: with the solutions, for the first group).
    public static MarkdownPage Parse(string text, string? file = null, Func<string, AnimationData>? loadScript = null,
        IReadOnlyDictionary<string, BookMark>? bookMarks = null, PageNumbering? numbering = null, IReadOnlyDictionary<string, KeywordLink>? keywordLinks = null,
        IReadOnlyDictionary<string, IReadOnlyList<MarkUse>>? usedIn = null, SheetVariant? variant = null) =>
        Build(text, file, loadScript, bookMarks, numbering ?? PageNumbering.None, keywordLinks, usedIn, render: true, variant);

    /// <summary>
    /// The marks of a page (for the references of the other pages) and how many of each counter it has, without making its
    /// HTML; their labels as on a page alone (<see cref="Renumbered"/> gives them the numbers of the page in the book).
    /// </summary>
    public static MarksOfPage ReadMarks(string text, BookNumbering? book = null)
    {
        MarkdownPage page = Build(text, null, null, null, (PageNumbering.None with { Book = book ?? BookNumbering.Default }), null, null, render: false);
        return new MarksOfPage(page.Marks, page.Counts) { Keywords = page.KeywordDefinitions, Title = page.Title, References = page.References };
    }

    /// <summary>The references of the page ("§name"), in their order.</summary>
    public IReadOnlyList<PageReference> References { get; private set; } = [];

    /// <summary>How many of each counter the page has (for the numbers of the pages after it).</summary>
    public IReadOnlyDictionary<string, int> Counts { get; private set; } = new Dictionary<string, int>();

    /// <summary>The number of the page in the book ("2.1"), if it has one.</summary>
    public string? SectionNumber { get; private set; }

    private static MarkdownPage Build(string text, string? file, Func<string, AnimationData>? loadScript,
        IReadOnlyDictionary<string, BookMark>? bookMarks, PageNumbering numbering, IReadOnlyDictionary<string, KeywordLink>? keywordLinks,
        IReadOnlyDictionary<string, IReadOnlyList<MarkUse>>? usedIn, bool render, SheetVariant? variant = null)
    {
        var errors = new List<MarkdownProblem>();
        var warnings = new List<MarkdownProblem>();
        // "$$" … "$$ {#gl-name}": the mark of a formula at its end, where Markdig wants it at the start
        text = MoveFormulaMarks(text);
        MarkdownDocument document = Markdown.Parse(text, Pipeline);
        Func<int, (int, int)> locate = Locator(text);

        // the front matter
        string? title = null, description = null;
        var keywords = new List<string>();
        var head = new Dictionary<string, object>(StringComparer.Ordinal);
        if (document.Descendants<YamlFrontMatterBlock>().FirstOrDefault() is { } front)
        {
            ReadFrontMatter(front, ref title, ref description, keywords, head, errors, warnings);
            front.Parent?.Remove(front);
        }
        // a test or worksheet: a sheet of its own (with its tasks); its head as it shows on the sheet
        DocumentHead? sheet = head.TryGetValue("type", out object? type) ? new DocumentHead((string)type)
        {
            Title = title,
            Subject = head.GetValueOrDefault("subject") as string,
            Time = head.GetValueOrDefault("time") as string,
            Groups = head.GetValueOrDefault("groups") as List<string> ?? [],
            Seed = head.GetValueOrDefault("seed") as int?,
            Generators = head.GetValueOrDefault("generators") is List<string> projects
                ? projects.Select(p => Path.GetFullPath(p, file != null ? Path.GetDirectoryName(Path.GetFullPath(file))! : Directory.GetCurrentDirectory())).ToList()
                : [],
        } : null;
        // its groups' differences ("::: let", "::: pick", "{{…}}") worked out for the sheet's group, then read again
        if (sheet != null)
        {
            variant ??= new SheetVariant(sheet.Groups.FirstOrDefault(), Solution: true);
            // questions for ILIAS: always with their answers (the students see them in ILIAS); a document has none
            if (sheet.IsIlias || sheet.IsDocument)
            {
                variant = variant with { Solution = true };
            }
            int groupIndex = variant.Group != null ? Math.Max(0, sheet.Groups.ToList().IndexOf(variant.Group)) : 0;
            string expanded = SheetScript.Expand(text, sheet.Seed ?? 1, groupIndex, errors, variant.Group, sheet.Generators,
                new SheetOptions { Groups = sheet.Groups, Solution = variant.Solution || sheet.IsIlias, Ilias = sheet.IsIlias, Warnings = warnings });
            if (expanded != text)
            {
                text = expanded;
                document = Markdown.Parse(text, Pipeline);
                locate = Locator(text);
                document.Descendants<YamlFrontMatterBlock>().FirstOrDefault()?.Parent?.Remove(document.Descendants<YamlFrontMatterBlock>().First());
            }
        }

        // structure diagrams as pictures
        string drawn = DrawNassi(text, variant?.Solution ?? true, errors);
        if (drawn != text)
        {
            text = drawn;
            document = Markdown.Parse(text, Pipeline);
            locate = Locator(text);
            document.Descendants<YamlFrontMatterBlock>().FirstOrDefault()?.Parent?.Remove(document.Descendants<YamlFrontMatterBlock>().First());
        }

        RescueAttributes(document, text);
        AttachFormulaMarks(document);
        string? heading = document.Descendants<HeadingBlock>().FirstOrDefault(h => h.Level == 1) is { } first
            ? Regex.Replace(HeadingText(text, first), @"\s*\{[^{}]*\}\s*$", "")
            : null;
        List<PageKeywordDefinition> keywordDefinitions = ReadKeywords(text, document, keywords, locate, errors);

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
            // a row of pictures of one height: with pictures from files or "{height=…}"
            bool row = images.Any(i => PictureRow.IsPicture(i.Url) || i.TryGetAttributes()?.Properties?.Any(p => p.Key == "height") == true);
            foreach (LinkInline image in images)
            {
                if (PictureRow.IsPicture(image.Url))
                {
                    PageImage? picture = PictureOf(folder, image, locate, errors, warnings, figureOptions);
                    shown.Add(new FigureItem(image, image.Url!, null, null, Faulty: picture == null)
                    {
                        Picture = picture,
                        Caption = image.FirstChild == null ? "" : CaptionOf(text, image),
                    });
                    continue;
                }
                GeoReference? reference = ReferenceOf(text, file, image, locate, errors, warnings, figureOptions, row);
                if (reference != null && row && reference.From != reference.To)
                {
                    errors.Add(new MarkdownProblem(reference.Line, reference.Column,
                        $"\"{reference.Source}\": a row of pictures takes one picture of a script (#2, or the whole: its last one), not a range of steps."));
                    shown.Add(new FigureItem(image, image.Url!, reference, null, Faulty: true));
                    continue;
                }
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
            var block = new GeoFigureBlock(shown) { Line = paragraph.Line, Column = paragraph.Column, Span = paragraph.Span, Row = row };
            // float and width (for the web and e-books later; in print the figure stays centred); a row's height
            foreach ((string key, string value) in figureOptions)
            {
                if (key != "row-width")
                {
                    block.GetAttributes().AddProperty("data-" + key, value);
                }
            }
            parent.Insert(index, block);
        }

        MovePunctuation(document);
        KeepPunctuation(document);

        // numbers and marks of figures, statements and sections; then the references to them
        List<PageMark> marks = Number(text, document, locate, errors, numbering, out Dictionary<string, int> counts, out PageMark? section);
        // a test or worksheet: "Abb. 1" only at a figure with a caption or a mark (it is spoken of as "the picture on the right")
        if (sheet != null)
        {
            foreach (GeoFigureBlock figure in document.Descendants<GeoFigureBlock>())
            {
                if (figure.Items.All(i => (i.Reference?.Caption ?? i.Caption).Length == 0 && i.Image.TryGetAttributes()?.Id == null))
                {
                    figure.Label = "";
                }
            }
        }
        string? page = file == null ? null : Path.GetFullPath(file);
        List<PageReference> pageReferences = ResolveReferences(document, locate, marks, section, page, bookMarks, errors);
        if (!render)
        {
            return new MarkdownPage("")
            {
                Marks = marks, Counts = counts, KeywordDefinitions = keywordDefinitions, FrontTitle = title, Heading = heading, References = pageReferences,
            };
        }
        CheckMarks(marks, page, bookMarks, errors);
        LinkKeywords(document, keywordLinks ?? new Dictionary<string, KeywordLink>());
        List<TestTask> tasks = sheet == null || sheet.IsDocument ? [] : sheet.IsIlias ? ReadQuestions(text, document, locate, errors) : ReadTasks(text, document, locate, errors);
        ReadPageCommands(text, document, locate);
        variant ??= sheet != null ? new SheetVariant(sheet.Groups.FirstOrDefault(), Solution: true) : null;

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
        if (renderer.ObjectRenderers.FindExact<Markdig.Renderers.Html.CodeBlockRenderer>() is { } codeBlocks)
        {
            renderer.ObjectRenderers.Replace<Markdig.Renderers.Html.CodeBlockRenderer>(new CodeHighlighting.Renderer(codeBlocks));
        }
        renderer.ObjectRenderers.Replace<Markdig.Extensions.CustomContainers.HtmlCustomContainerRenderer>(new StatementRenderer(usedIn, page));
        renderer.ObjectRenderers.Insert(0, new MarkReferenceRenderer());
        renderer.ObjectRenderers.Replace<BlankRenderer>(new BlankRenderer(variant, sheet?.IsIlias == true));
        // (a document has its own heading)
        if (sheet is { IsDocument: false })
        {
            writer.Write(HeadHtml(sheet, variant!, heading));
        }
        renderer.Render(document);
        writer.Flush();

        return new MarkdownPage(writer.ToString())
        {
            Document = sheet,
            Tasks = tasks,
            FrontTitle = title,
            Heading = heading,
            Description = description,
            Keywords = keywords,
            Figures = figures,
            PageFigures = pageFigures,
            Marks = marks,
            Counts = counts,
            References = pageReferences,
            SectionNumber = numbering.Book.Headings ? numbering.Section : null,
            KeywordDefinitions = keywordDefinitions,
            Errors = errors.OrderBy(e => e.Line).ThenBy(e => e.Column).ToList(),
            Warnings = warnings,
        };
    }

    private static void ReadFrontMatter(YamlFrontMatterBlock front, ref string? title, ref string? description, List<string> keywords,
        Dictionary<string, object> head, List<MarkdownProblem> errors, List<MarkdownProblem> warnings)
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
                // a test or worksheet
                case "type":
                    if (value is YamlScalarNode { Value: { } kind } && DocumentTypes.Contains(kind.Trim()))
                    {
                        head["type"] = kind.Trim();
                    }
                    else
                    {
                        errors.Add(new MarkdownProblem(line, column, $"\"type\" is {string.Join(" or ", DocumentTypes)} (a sheet of its own)."));
                    }
                    break;
                case "subject" or "time":
                    if (value is YamlScalarNode { Value: { } text })
                    {
                        head[key] = text.Trim();
                    }
                    else
                    {
                        errors.Add(new MarkdownProblem(line, column, $"\"{key}\" must be a text."));
                    }
                    break;
                case "groups":
                    // [A, B, C], one per line with "-", or "A B C"
                    {
                        IEnumerable<YamlNode> names = value is YamlSequenceNode list ? list.Children : [value];
                        var groups = names.OfType<YamlScalarNode>().SelectMany(n => (n.Value ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)).ToList();
                        if (groups.Count == 0 || groups.Distinct(StringComparer.Ordinal).Count() != groups.Count)
                        {
                            errors.Add(new MarkdownProblem(line, column, "\"groups\" are names, each once: groups: [A, B, C]."));
                        }
                        else
                        {
                            head["groups"] = groups;
                        }
                    }
                    break;
                case "generators":
                    // C# projects: one, or a list ([a.csproj, b.csproj] or one per line with "-")
                    {
                        IEnumerable<YamlNode> names = value is YamlSequenceNode list ? list.Children : [value];
                        var projects = names.OfType<YamlScalarNode>().Select(n => (n.Value ?? "").Trim()).Where(n => n.Length > 0).ToList();
                        if (projects.Count == 0 || projects.Any(p => !p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
                        {
                            errors.Add(new MarkdownProblem(line, column, "\"generators\" are C# projects: generators: ../MintTemplates/MintTemplates.csproj."));
                        }
                        else
                        {
                            head["generators"] = projects;
                        }
                    }
                    break;
                case "seed":
                    if (value is YamlScalarNode { Value: { } seed } && int.TryParse(seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    {
                        head["seed"] = number;
                    }
                    else
                    {
                        errors.Add(new MarkdownProblem(line, column, "\"seed\" must be a whole number."));
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

    // the images of a paragraph that has nothing else (besides spaces and line breaks), if they are all scripts or picture files
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
                case LinkInline { IsImage: true } image when IsScript(image.Url) || PictureRow.IsPicture(image.Url):
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
    private static readonly string[] FigureAttributes = ["crop", "float", "width", "height", "scale", "nowarn"];

    // "file.geo", "file.geo#2", "file.geo#3..4" (also "3-4"), "file.geo#name", "file.geo#from..to"; "#…" the page's own
    // script
    // (row: in a row of pictures of one height – its width is its own, the script keeps the book's scale)
    private static GeoReference? ReferenceOf(string text, string? page, LinkInline image, Func<int, (int, int)> locate,
        List<MarkdownProblem> errors, List<MarkdownProblem> warnings, Dictionary<string, string> figureOptions, bool row)
    {
        string url = image.Url!;
        (int line, int column) = locate(image.Span.Start);
        string caption = image.FirstChild == null ? "" : CaptionOf(text, image);
        bool crop = true;
        string? scale = null, width = null;
        bool noWarn = false;
        foreach ((string key, string? raw) in image.TryGetAttributes()?.Properties ?? [])
        {
            string value = (raw ?? "").Trim();
            switch (key)
            {
                case "crop" when value is "yes" or "no":
                    crop = value == "yes";
                    break;
                case "nowarn" when value.Length == 0:
                    noWarn = true;
                    break;
                case "width" or "height" when row:
                    RowSize(key, value, line, column, errors, figureOptions);
                    break;
                case "height":
                    errors.Add(new MarkdownProblem(line, column, $"\"height={value}\": write the row's height like height=4cm (mm, cm, pt, in)."));
                    break;
                case "float" when value is "left" or "right":
                case "width" when WidthPattern().IsMatch(value):
                case "scale" when WidthPattern().IsMatch(value) && !value.EndsWith('%'):
                    if (figureOptions.TryGetValue(key, out string? other) && other != value)
                    {
                        errors.Add(new MarkdownProblem(line, column, $"The images of this figure have different values for \"{key}\" ({other}, {value})."));
                    }
                    figureOptions[key] = value;
                    if (key == "scale")
                    {
                        scale = value;
                    }
                    else if (key == "width" && !value.EndsWith('%'))
                    {
                        width = value;
                    }
                    break;
                case "crop" or "float" or "width" or "scale":
                    errors.Add(new MarkdownProblem(line, column, key switch
                    {
                        "crop" => $"\"crop={value}\": write crop=yes or crop=no.",
                        "float" => $"\"float={value}\": write float=left or float=right.",
                        "scale" => $"\"scale={value}\": write the length of a unit like scale=6mm (mm, cm, pt, in).",
                        _ => $"\"width={value}\": write a width like width=8cm (mm, cm, pt, in) or width=40%.",
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
            return new GeoReference(file, null, null, caption, line, column, crop) { Scale = scale, Width = width, NoWarn = noWarn };
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
        return new GeoReference(file, from, to, caption, line, column, crop) { Scale = scale, Width = width, NoWarn = noWarn };
    }

    [GeneratedRegex(@"^\d+(\.\d+)?(%|mm|cm|pt|in)$")]
    private static partial Regex WidthPattern();

    // in a row of pictures: "height=4cm" (the row's, the same at all its pictures) or "width=5cm" (one picture's: the row
    // follows it); one of them
    private static void RowSize(string key, string value, int line, int column, List<MarkdownProblem> errors, Dictionary<string, string> figureOptions)
    {
        if (PictureRow.Points(value) == null)
        {
            errors.Add(new MarkdownProblem(line, column, $"\"{key}={value}\": in a row of pictures write a length like {key}=4cm (mm, cm, pt, in)."));
            return;
        }
        if (key == "height" && figureOptions.TryGetValue("height", out string? other) && other != value)
        {
            errors.Add(new MarkdownProblem(line, column, $"The images of this figure have different values for \"height\" ({other}, {value})."));
            return;
        }
        string otherKey = key == "height" ? "row-width" : "height";
        if (figureOptions.ContainsKey(otherKey) || key == "width" && figureOptions.ContainsKey("row-width"))
        {
            errors.Add(new MarkdownProblem(line, column,
                "A row of pictures has one size: the row's height (height=4cm) or the width of one of its pictures (width=5cm) – the others follow."));
            return;
        }
        figureOptions[key == "height" ? "height" : "row-width"] = value;
    }

    // a picture from a file: its size and kind; its width (in the row), the figure's height and float; null if it is missing
    private static PageImage? PictureOf(string? folder, LinkInline image, Func<int, (int, int)> locate,
        List<MarkdownProblem> errors, List<MarkdownProblem> warnings, Dictionary<string, string> figureOptions)
    {
        (int line, int column) = locate(image.Span.Start);
        string? width = null;
        foreach ((string key, string? raw) in image.TryGetAttributes()?.Properties ?? [])
        {
            string value = (raw ?? "").Trim();
            switch (key)
            {
                case "width" or "height":
                    int before = errors.Count;
                    RowSize(key, value, line, column, errors, figureOptions);
                    if (key == "width" && errors.Count == before)
                    {
                        width = value;
                    }
                    break;
                case "float" when value is "left" or "right":
                    figureOptions["float"] = value;
                    break;
                case "float":
                    errors.Add(new MarkdownProblem(line, column, $"\"float={value}\": write float=left or float=right."));
                    break;
                case "crop" or "scale" or "nowarn":
                    warnings.Add(new MarkdownProblem(line, column, $"\"{key}\" is for figures from scripts, not for a picture from a file."));
                    break;
                default:
                    warnings.Add(new MarkdownProblem(line, column,
                        $"Unknown attribute \"{key}\" of a picture (known: width, height, float)."));
                    break;
            }
        }
        string url = Uri.UnescapeDataString(image.Url!.Split('#', '?')[0]);
        if (folder == null)
        {
            errors.Add(new MarkdownProblem(line, column, $"\"{url}\": the page must be saved first (the picture is found next to it)."));
            return null;
        }
        string path = Path.GetFullPath(Path.Combine(folder, url.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(path))
        {
            errors.Add(new MarkdownProblem(line, column, $"The picture \"{url}\" does not exist."));
            return null;
        }
        byte[] data = File.ReadAllBytes(path);
        string mediaType = PictureRow.MediaTypeOf(path, data);
        if (PictureRow.RatioOf(data, mediaType) is not double ratio)
        {
            errors.Add(new MarkdownProblem(line, column, $"\"{url}\": its size cannot be read (is it a {Path.GetExtension(path).TrimStart('.').ToUpperInvariant()} at all?)."));
            return null;
        }
        return new PageImage(path, mediaType, ratio, width);
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

    // the relations at which a formula may break or a chain is aligned, longest first; arrows that start like them are
    // passed over; ":=" and "=:" (definitions) as one
    private static readonly string[] Relations = ["<=>", ":=", "=:", "=>", "<=", ">=", "!=", "~~", "~=", "-=", "=", "<", ">"];
    private static readonly string[] NoRelations = ["|->", "->", "<-", "<<", ">>", "-<", ">-", "-:"];

    // a formula in the text up to this wide (in em, as it is shown: "sigma" is one letter) is not cut
    private const double ShortFormula = 10;

    // whether a formula is short enough to stay whole (a faulty one: yes, it is shown as its source with the error)
    private static bool IsShort(string asciiMath)
    {
        try
        {
            return MathRenderer.Render(asciiMath).Width <= ShortFormula * 1000;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    /// <summary>
    /// A formula cut before its relations outside brackets and texts ("a = b + c <= d" → "a", "= b + c", "<= d"): the places
    /// where a line may break (one piece: none).
    /// </summary>
    internal static List<string> PiecesOf(string source)
    {
        var pieces = new List<string>();
        int depth = 0, start = 0;
        bool quoted = false;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (quoted)
            {
                continue;
            }
            if (c is '(' or '[' or '{')
            {
                depth++;
                continue;
            }
            if (c is ')' or ']' or '}')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }
            if (depth > 0)
            {
                continue;
            }
            string? arrow = NoRelations.FirstOrDefault(a => string.CompareOrdinal(source, i, a, 0, a.Length) == 0);
            if (arrow != null)
            {
                i += arrow.Length - 1;
                continue;
            }
            string? relation = Relations.FirstOrDefault(r => string.CompareOrdinal(source, i, r, 0, r.Length) == 0);
            if (relation == null)
            {
                continue;
            }
            if (source[start..i].Trim().Length > 0)
            {
                pieces.Add(source[start..i].Trim());
                start = i;
            }
            i += relation.Length - 1;
        }
        pieces.Add(source[start..].Trim());
        return pieces.Where(p => p.Length > 0).ToList();
    }

    /// <summary>
    /// A chain of equations on lines of their own, aligned at the relations: the first line ("a = b") and lines that start
    /// with a relation ("= c"); a line without one continues the line before. Its rows: the left side (only the first row)
    /// and the rest from the relation on. Null if it is no chain (one line, or no line after the first starts with a
    /// relation).
    /// </summary>
    internal static List<(string? Left, string Right)>? ChainOf(string source)
    {
        List<string> lines = source.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        bool StartsWithRelation(string line) => Relations.Any(r => line.StartsWith(r, StringComparison.Ordinal))
            && !NoRelations.Any(a => line.StartsWith(a, StringComparison.Ordinal));
        if (lines.Count < 2 || !lines.Skip(1).Any(StartsWithRelation))
        {
            return null;
        }
        List<string> first = PiecesOf(lines[0]);
        var rows = new List<(string? Left, string Right)>
        {
            first.Count > 1 && !StartsWithRelation(lines[0]) ? (first[0], string.Join(" ", first.Skip(1))) : (null, lines[0]),
        };
        foreach (string line in lines.Skip(1))
        {
            if (StartsWithRelation(line))
            {
                rows.Add((null, line));
            }
            else
            {
                rows[^1] = (rows[^1].Left, rows[^1].Right + " " + line);
            }
        }
        // a first line without a relation is the left side of the rows after it
        if (first.Count == 1 && !StartsWithRelation(lines[0]) && rows.Count > 1)
        {
            rows[1] = (rows[0].Right, rows[1].Right);
            rows.RemoveAt(0);
        }
        return rows;
    }

    // a formula's markup: a chain as rows of left side and rest (aligned at the relation), else a formula cut at its
    // relations (where a line may break: a thick space and a break opportunity before each relation); "before" and
    // "after": the punctuation before its first part and after its last (in the line: on one line with it)
    private static void WriteFormula(HtmlRenderer renderer, string source, bool display, string before, string after, Action<FormatException> error)
    {
        // a part with the punctuation next to it, unbroken
        void Kept(string start, string svg, string end)
        {
            // on a line of its own anyway: no span
            if (display || start.Length == 0 && end.Length == 0)
            {
                renderer.Write(svg).WriteEscape(end);
                return;
            }
            renderer.Write(NoBreak).WriteEscape(start).Write(svg).WriteEscape(end).Write("</span>");
        }
        string Svg(string part)
        {
            try
            {
                return FormulaSvg(part);
            }
            catch (FormatException e)
            {
                error(e);
                return FaultyFormula(part);
            }
        }
        if (display && ChainOf(source) is { } rows)
        {
            renderer.Write("<span class=\"formula-rows\">");
            for (int i = 0; i < rows.Count; i++)
            {
                renderer.Write("<span class=\"formula-row\"><span class=\"formula-left\">").Write(rows[i].Left != null ? Svg(rows[i].Left!) : "")
                    .Write("</span><span class=\"formula-right\">").Write(Svg(rows[i].Right));
                if (i == rows.Count - 1)
                {
                    renderer.WriteEscape(after);
                }
                renderer.Write("</span></span>");
            }
            renderer.Write("</span>");
            return;
        }
        string single = Regex.Replace(source, @"\s*\n\s*", " ");
        // a short formula stays whole (it fits on a line anyway, and "M" at the end of one and "!= Q" on the next looks odd)
        List<string> pieces = display || IsShort(single) ? [single] : PiecesOf(single);
        if (pieces.Count < 2)
        {
            Kept(before, Svg(single), after);
            return;
        }
        renderer.Write("<span class=\"formula-split\">");
        for (int i = 0; i < pieces.Count; i++)
        {
            if (i > 0)
            {
                renderer.Write("<span class=\"formula-gap\"></span><wbr>");
            }
            Kept(i == 0 ? before : "", Svg(pieces[i]), i == pieces.Count - 1 ? after : "");
        }
        renderer.Write("</span>");
    }

    // formula and punctuation on one line (the style in the element: it holds in every viewer, also without page.css)
    private const string NoBreak = "<span class=\"formula-nobr\" style=\"white-space:nowrap\">";

    private sealed class FormulaInlineRenderer(List<MarkdownProblem> errors, Func<int, (int, int)> locate) : HtmlObjectRenderer<MathInline>
    {
        protected override void Write(HtmlRenderer renderer, MathInline math)
        {
            string source = math.Content.ToString().Trim();
            // "$$…$$" in a paragraph: centred on a line of its own, with the punctuation after it ("$$…$$."); with a mark
            // its number on the right
            bool display = math.DelimiterCount == 2;
            string? number = math.GetData(typeof(FormulaNumber)) as string;
            if (display)
            {
                renderer.Write("<span class=\"formula-display").Write(number != null ? " formula-numbered" : "")
                    .Write(ChainOf(math.Content.ToString()) != null ? " formula-chain" : "").Write('"');
                if (math.TryGetAttributes()?.Id is { } id)
                {
                    renderer.Write(" id=\"").WriteEscape(id).Write('"');
                }
                renderer.Write('>');
            }
            var kept = math.GetData(typeof(InlinePunctuation)) as InlinePunctuation;
            string before = kept?.Before ?? "";
            string after = display ? math.GetData(typeof(DisplayPunctuation)) as string ?? "" : kept?.After ?? "";
            WriteFormula(renderer, math.Content.ToString(), display, before, after, e =>
            {
                (int line, int column) = locate(math.Span.Start);
                errors.Add(new MarkdownProblem(line, column, e.Message));
            });
            if (display)
            {
                if (number != null)
                {
                    renderer.Write("<span class=\"formula-number\">").WriteEscape(number).Write("</span>");
                }
                renderer.Write("</span>");
            }
        }
    }

    // the number of a formula with a mark ("(2.3)")
    private sealed class FormulaNumber;

    // "$$" on a line of its own … "$$ {#gl-name}" (the mark at the end): the mark moved to the opening line, as Markdig
    // reads it there; "$$." (a full stop, comma … after the closing "$$", which Markdig would not take for its end): the
    // punctuation moved to the formula's last line; the lines stay where they are
    private static string MoveFormulaMarks(string text)
    {
        if (!text.Contains("$$", StringComparison.Ordinal))
        {
            return text;
        }
        string[] lines = text.Split('\n');
        int open = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (open < 0)
            {
                if (Regex.IsMatch(line, @"^\s{0,3}\$\$\s*(\{[^{}]*\})?\s*$"))
                {
                    open = i;
                }
                continue;
            }
            Match punctuated = Regex.Match(line, @"^(\s{0,3}\$\$)\s*([.,;:!?])\s*(\{[^{}]*\})?\s*$");
            if (punctuated.Success)
            {
                // the punctuation at the end of the last line with content before it
                for (int k = i - 1; k > open; k--)
                {
                    if (lines[k].Trim().Length > 0)
                    {
                        string lastEnd = lines[k].EndsWith('\r') ? "\r" : "";
                        lines[k] = lines[k].TrimEnd('\r').TrimEnd() + punctuated.Groups[2].Value + lastEnd;
                        break;
                    }
                }
                lines[i] = punctuated.Groups[1].Value + (punctuated.Groups[3].Success ? " " + punctuated.Groups[3].Value : "") + (lines[i].EndsWith('\r') ? "\r" : "");
                line = lines[i].TrimEnd('\r');
                if (!punctuated.Groups[3].Success)
                {
                    open = -1;
                    continue;
                }
            }
            Match closing = Regex.Match(line, @"^(\s{0,3}\$\$)\s*(\{[^{}]*\})\s*$");
            if (closing.Success)
            {
                string end = lines[i].EndsWith('\r') ? "\r" : "";
                string opening = lines[open].TrimEnd('\r');
                string openEnd = lines[open].EndsWith('\r') ? "\r" : "";
                lines[open] = opening.TrimEnd() + " " + closing.Groups[2].Value + openEnd;
                lines[i] = closing.Groups[1].Value + end;
                open = -1;
            }
            else if (Regex.IsMatch(line, @"^\s{0,3}\$\$\s*$"))
            {
                open = -1;
            }
        }
        return string.Join('\n', lines);
    }

    // "$$a^2+b^2=c^2$$ {#gl-name}" in a paragraph: Markdig gives the mark (after a space) to the paragraph; it belongs to
    // the formula before it (the space goes, so that the punctuation after it stays with the formula)
    private static void AttachFormulaMarks(MarkdownDocument document)
    {
        foreach (ParagraphBlock paragraph in document.Descendants<ParagraphBlock>())
        {
            if (paragraph.TryGetAttributes()?.Id is not { } id || paragraph.Inline == null)
            {
                continue;
            }
            MathInline? formula = paragraph.Inline.Descendants<MathInline>().LastOrDefault(m => m.DelimiterCount == 2);
            if (formula == null || formula.TryGetAttributes()?.Id != null)
            {
                continue;
            }
            formula.GetAttributes().Id = id;
            paragraph.GetAttributes().Id = null;
            if (formula.NextSibling is LiteralInline space && space.Content.ToString().Trim().Length == 0)
            {
                space.Remove();
            }
        }
    }

    // the punctuation that follows a "$$…$$" in a paragraph and is shown with it
    private sealed class DisplayPunctuation;

    // moves a full stop, comma … right after a "$$…$$" into the formula's line
    private static void MovePunctuation(MarkdownDocument document)
    {
        foreach (MathInline math in document.Descendants<MathInline>())
        {
            if (math.DelimiterCount == 2 && math.NextSibling is LiteralInline next && next.Content.Length > 0 && ".,;:!?".Contains(next.Content.CurrentChar))
            {
                math.SetData(typeof(DisplayPunctuation), next.Content.CurrentChar.ToString());
                next.Content.Start++;
            }
        }
    }

    // the punctuation right before and after a formula in the line ("($x$)," "„$a$“"), kept with it
    private sealed class InlinePunctuation(string before, string after)
    {
        public string Before { get; } = before;

        public string After { get; } = after;
    }

    // what may stand at the end of a line before a formula, and at the start of one after it, if not kept with it
    private const string OpeningPunctuation = "([{„‚»‹";
    private const string ClosingPunctuation = ".,;:!?)]}“‘«›…-";

    /// <summary>
    /// Takes the punctuation right before and after a formula in the line from the text around it: the formula is a picture,
    /// and a browser would break between it and a comma after it (or an opening bracket before it); written together they
    /// stay on one line.
    /// </summary>
    private static void KeepPunctuation(MarkdownDocument document)
    {
        foreach (MathInline math in document.Descendants<MathInline>())
        {
            if (math.DelimiterCount != 1)
            {
                continue;
            }
            string before = "", after = "";
            if (math.PreviousSibling is LiteralInline previous)
            {
                string text = previous.Content.ToString();
                int count = text.Length - text.TrimEnd(OpeningPunctuation.ToCharArray()).Length;
                before = text[^count..];
                previous.Content.End -= count;
            }
            if (math.NextSibling is LiteralInline next)
            {
                string text = next.Content.ToString();
                int count = text.Length - text.TrimStart(ClosingPunctuation.ToCharArray()).Length;
                after = text[..count];
                next.Content.Start += count;
            }
            if (before.Length > 0 || after.Length > 0)
            {
                math.SetData(typeof(InlinePunctuation), new InlinePunctuation(before, after));
            }
        }
    }

    // $$…$$ on lines of their own: a formula centred on a line of its own
    private sealed class FormulaBlockRenderer(List<MarkdownProblem> errors, Func<int, (int, int)> locate) : HtmlObjectRenderer<MathBlock>
    {
        protected override void Write(HtmlRenderer renderer, MathBlock math)
        {
            string source = string.Join("\n", math.Lines.Lines.Take(math.Lines.Count).Select(l => l.Slice.ToString().Trim())).Trim();
            string? number = math.GetData(typeof(FormulaNumber)) as string;
            renderer.Write("<div class=\"formula-block").Write(number != null ? " formula-numbered" : "").Write(ChainOf(source) != null ? " formula-chain" : "")
                .Write('"').WriteAttributes(math).Write('>');
            WriteFormula(renderer, source, true, "", "", e =>
            {
                (int line, int column) = locate(math.Span.Start);
                errors.Add(new MarkdownProblem(line, column, e.Message));
            });
            if (number != null)
            {
                renderer.Write("<span class=\"formula-number\">").WriteEscape(number).Write("</span>");
            }
            renderer.WriteLine("</div>");
        }
    }

    // ------------------------------------------------------------------ structure diagrams

    [GeneratedRegex(@"^\s*:::\s*nassi\b\s*(.*?)\s*$")]
    private static partial Regex NassiStart();

    /// <summary>
    /// "::: nassi" … ":::" (a structure diagram in lines like pseudocode; "::: nassi numbered": its boxes numbered,
    /// "::: nassi ja|nein": these words at the branches instead of true/false) as a picture: an SVG in a figure, on the
    /// block's first line (the others empty, so that the lines stay where they were). The SVG is as wide in em as it is
    /// in points by its text's size, so that it grows with the text.
    /// </summary>
    private static string DrawNassi(string text, bool solution, List<MarkdownProblem> errors)
    {
        string[] lines = text.Split('\n');
        bool fenced = false, changed = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }
            if (fenced || NassiStart().Match(line) is not { Success: true } start)
            {
                continue;
            }
            int close = i + 1;
            while (close < lines.Length && lines[close].Trim() != ":::")
            {
                close++;
            }
            var options = new GeoAnim.Figures.NassiOptions { Solution = solution };
            foreach (string option in start.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (option == "numbered")
                {
                    options = options with { Numbered = true };
                }
                else if (option.Split('|') is [{ Length: > 0 } yes, { Length: > 0 } no])
                {
                    options = options with { True = yes, False = no };
                }
                else
                {
                    errors.Add(new MarkdownProblem(i + 1, 1, $"\"{option}\" after ::: nassi: \"numbered\" (numbers in the boxes) or the words at the branches (ja|nein)."));
                }
            }
            var problems = new List<(int Line, string Message)>();
            GeoAnim.Figures.NassiImage image = GeoAnim.Figures.NassiDiagram.Render(
                lines[(i + 1)..Math.Min(close, lines.Length)].Select(l => l.TrimEnd('\r')).ToList(), options, problems, i + 2);
            foreach ((int at, string message) in problems)
            {
                errors.Add(new MarkdownProblem(at, 1, message));
            }
            string width = (Math.Round(image.Width / options.Size, 3) + 0.0).ToString("0.###", CultureInfo.InvariantCulture);
            // (its width on the figure: the SVG stays as it is for other uses – PNG, Typst)
            string svg = image.Svg.Replace("<svg ", "<svg class=\"nassi-picture\" ", StringComparison.Ordinal);
            lines[i] = $"<figure class=\"nassi\" data-width=\"{width}\" style=\"--width: {width}em\">{svg}</figure>";
            for (int k = i + 1; k <= Math.Min(close, lines.Length - 1); k++)
            {
                lines[k] = "";
            }
            i = Math.Min(close, lines.Length - 1);
            changed = true;
        }
        return changed ? string.Join("\n", lines) : text;
    }

    // ------------------------------------------------------------------ figures

    // the figures of a paragraph; a faulty address has no reference
    // a figure of a paragraph: its address, its reference (null: a faulty address) and its animation (null: none loaded)
    private sealed record FigureItem(LinkInline Image, string Source, GeoReference? Reference, PageFigure? Figure, bool Faulty)
    {
        /// <summary>A picture from a file (null: a script's).</summary>
        public PageImage? Picture { get; init; }

        /// <summary>A picture's caption ("![…](halo.jpg)"; a script's is its reference's).</summary>
        public string Caption { get; init; } = "";

        /// <summary>Its width in a row of pictures ("{width=5cm}"), or null.</summary>
        public string? RowWidth => Picture?.Width ?? (Image.TryGetAttributes()?.Properties?.FirstOrDefault(p => p.Key == "width").Value?.Trim() is { } width && PictureRow.Points(width) != null ? width : null);
    }

    // the figures of a paragraph
    private sealed class GeoFigureBlock(List<FigureItem> items) : LeafBlock(null)
    {
        public List<FigureItem> Items { get; } = items;

        /// <summary>"Abb. 3"</summary>
        public string Label { get; set; } = "";

        /// <summary>A row of pictures of one height (with pictures from files, or "{height=…}").</summary>
        public bool Row { get; init; }
    }

    // the figures side by side, each an element for the player (data-figure: its key; --ratio: the picture's width by its
    // height); without an animation a box with the address
    private sealed class GeoFigureRenderer : HtmlObjectRenderer<GeoFigureBlock>
    {
        protected override void Write(HtmlRenderer renderer, GeoFigureBlock figure)
        {
            // float=right: beside the text on screens (a class, as data-… is not kept everywhere); in print centred
            string floating = figure.GetAttributes().Properties?.FirstOrDefault(p => p.Key == "data-float").Value is { } side ? " geo-float-" + side : "";
            renderer.Write("<figure class=\"geo-figure").Write(figure.Row ? " geo-pictures" : "").Write(floating).Write('"').WriteAttributes(figure).WriteLine('>');
            renderer.WriteLine("<div class=\"geo-row\">");
            var captions = new StringBuilder();
            foreach (FigureItem item in figure.Items)
            {
                // in a row: a picture's own width
                string width = figure.Row && item.RowWidth is { } own ? " data-width=\"" + own + "\"" : "";
                if (item.Picture is { } picture)
                {
                    renderer.Write("<div class=\"geo-item geo-image\"").Write(width).Write(" style=\"--ratio: ")
                        .Write(picture.Ratio.ToString("0.####", CultureInfo.InvariantCulture)).Write("\"><img src=\"").Write(picture.DataUri)
                        .Write("\" alt=\"").WriteEscape(item.Caption.Length > 0 ? HtmlExporter.PlainText(item.Caption) : "Bild")
                        .Write("\" data-source=\"").WriteEscape(picture.Path).WriteLine("\"></div>");
                    if (item.Caption.Length > 0)
                    {
                        captions.Append(captions.Length > 0 ? " · " : "").Append(item.Caption);
                    }
                    continue;
                }
                if (item.Figure is { } loaded)
                {
                    renderer.Write("<div class=\"geo-item ").Write(loaded.Still ? "geo-still" : "geo-player")
                        .Write("\" data-figure=\"").Write(loaded.Key).Write('"').Write(width)
                        .Write(" style=\"--ratio: ").Write(loaded.Ratio.ToString("0.####", CultureInfo.InvariantCulture)).WriteLine("\"></div>");
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
            if (captions.Length == 0 && figure.Label.Length > 0 && figure.Items is [{ Figure.StepCaption: { Length: > 0 } stepCaption }])
            {
                captions.Append(stepCaption);
            }
            // (on a test or worksheet a figure has a number only with a caption or a mark)
            if (figure.Label.Length > 0 || captions.Length > 0)
            {
                renderer.Write("<figcaption><span class=\"figure-label\">").WriteEscape(figure.Label).Write("</span>");
                if (captions.Length > 0)
                {
                    // the caption is Markdown too (formulas, emphasis)
                    renderer.Write(figure.Label.Length > 0 ? " " : "").Write(InlineHtml(captions.ToString()));
                }
                renderer.WriteLine("</figcaption>");
            }
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
            json + "|" + from + "|" + to + "|" + still + "|" + reference.Crop + "|" + reference.Scale + "|" + reference.Width + "|" + reference.NoWarn)))[..16].ToLowerInvariant();
        ViewWindow view = animation.View;
        double ratio = (view.XMax - view.XMin) / (view.YMax - view.YMin);
        string? stepCaption = still && to >= 0 ? animation.Steps[to.Value].Caption : null;
        return new PageFigure(key, reference, json, from, to, still, ratio) { StepCaption = stepCaption };
    }

    /// <summary>
    /// The index (from 0) of a step by its number (from 1; 0: the starting picture, -1), its mark ("step … #gleich") or
    /// its name (the step's text in small letters, with "-" between the words: "Gleiche Punkte" → "gleiche-punkte").
    /// </summary>
    public static int StepIndex(GeoReference reference, AnimationData animation, string step)
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
    // (with the punctuation next to them on one line)
    private static string RenderFormulasIn(string html) => MathSpan().Replace(html, m =>
    {
        string source = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
        string svg;
        try
        {
            svg = FormulaSvg(source);
        }
        catch (FormatException)
        {
            svg = FaultyFormula(source);
        }
        return m.Groups[1].Length + m.Groups[3].Length == 0 ? svg : NoBreak + m.Groups[1].Value + svg + m.Groups[3].Value + "</span>";
    });

    [GeneratedRegex(@"([(\[{„‚»‹]*)<span class=""math"">\\\((.*?)\\\)</span>([.,;:!?)\]}“‘«›…-]*)")]
    private static partial Regex MathSpan();
}
