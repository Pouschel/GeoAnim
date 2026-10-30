using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using GeoAnim.Figures;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

internal static partial class Program
{
    /// <summary>
    /// geoanim typst &lt;dir&gt; [-o folder] [-pdf]: the pages of a book as a Typst project for the printed book: book.typ
    /// (title page, contents, the chapters with their pages), pages/*.typ, the figures and formulas as SVG (figures in
    /// their size in print, from "print:" in index.yaml) and style.typ with the look – the book's own if its folder has one
    /// (beside index.yaml), else the standard. With -pdf Typst makes the PDF right away (it must be installed): in the
    /// project's folder, named after it, or as given ("-pdf buch.pdf", relative to the project's folder).
    /// </summary>
    internal static int Typst(List<string> arguments)
    {
        string folder = arguments[0];
        string? output = null;
        bool pdf = false;
        string? pdfName = null;
        for (int i = 1; i < arguments.Count; i++)
        {
            if (arguments[i] is "-o" or "--output" && i + 1 < arguments.Count)
            {
                output = Path.GetFullPath(arguments[++i]);
            }
            else if (arguments[i] is "-pdf" or "--pdf")
            {
                pdf = true;
                if (i + 1 < arguments.Count && arguments[i + 1].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    pdfName = arguments[++i];
                }
            }
            else
            {
                throw new ArgumentException("Unknown option " + arguments[i]);
            }
        }
        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine("Directory not found: " + folder);
            return 1;
        }
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!File.Exists(Path.Combine(root, BookFile.Name)))
        {
            Console.Error.WriteLine($"{root} is no book: it needs an {BookFile.Name}.");
            return 1;
        }
        output ??= Path.Combine(Path.GetDirectoryName(root)!, Path.GetFileName(root) + "-typst");
        int result = new TypstWriter(root).Write(output);
        // the PDF in the project's folder, named after it (or as given)
        string pdfFile = Path.GetFullPath(Path.Combine(output, pdfName ?? Path.GetFileName(output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + ".pdf"));
        return pdf && result == 0 ? CompilePdf(output, pdfFile) : result;
    }

    // "typst compile book.typ book.pdf" in the project's folder
    private static int CompilePdf(string project, string pdf)
    {
        try
        {
            var start = new ProcessStartInfo("typst") { WorkingDirectory = project, UseShellExecute = false, RedirectStandardError = true };
            start.ArgumentList.Add("compile");
            start.ArgumentList.Add("book.typ");
            start.ArgumentList.Add(pdf);
            using Process process = Process.Start(start)!;
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (errors.Length > 0)
            {
                Console.Error.Write(errors);
            }
            if (process.ExitCode == 0)
            {
                Console.WriteLine("Wrote " + pdf);
            }
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine("Typst is not installed (or not on the PATH): https://typst.app – e.g. \"winget install Typst.Typst\". "
                + $"The project is ready in {project}: typst compile book.typ");
            return 1;
        }
    }

    /// <summary>Makes the Typst project of a book (see <see cref="Typst"/>).</summary>
    private sealed partial class TypstWriter(string root)
    {
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FigureAnimation> animations = new(StringComparer.Ordinal);
        private BookSettings settings = BookSettings.None;
        private FigureStyle style = FigureStyle.Default;
        private double textSize = 11;
        private int errors;

        // a page: its source (relative to the book), file in the project, its place in the book (for labels)
        private sealed record TypstPage(string Source, string FullPath, string File, int Index);

        public int Write(string output)
        {
            List<string> sources = Directory.EnumerateFiles(root, "*.geo.md", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Where(relative => !relative.Split('/').Any(part => part.StartsWith('_')))
                .ToList();
            sources.Sort(BookOrder.Instance);
            var progress = new Progress(sources.Count);
            settings = BookFile.Read(root, progress.Warning, out string? bookError);
            if (bookError != null)
            {
                progress.Error(bookError);
            }
            if (sources.Count == 0)
            {
                progress.Error($"{root} has no pages (*.geo.md).");
                progress.Finish("No book");
                return 1;
            }
            style = new FigureStyle(settings.FigureUnit ?? FigureStyle.Default.PointsPerUnit, settings.FigureSize ?? FigureStyle.Default.SizeFactor);
            textSize = settings.TextSize ?? 11;
            var book = sources.Select((source, i) => new TypstPage(source, Path.GetFullPath(Path.Combine(root, source)),
                $"p{(i + 1).ToString("00", CultureInfo.InvariantCulture)}-{Path.GetFileNameWithoutExtension(PageName(source[..^".md".Length]))}.typ", i + 1)).ToList();
            var pages = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (TypstPage entry in book)
            {
                progress.Step(entry.Source);
                string text = File.ReadAllText(entry.FullPath);
                Daemon.BookOfPage of = Daemon.ReadBook(entry.FullPath, text);
                var bookMarks = new Dictionary<string, BookMark>(StringComparer.Ordinal);
                foreach (BookMark mark in of.Marks)
                {
                    bookMarks.TryAdd(mark.Mark.Name, mark);
                }
                MarkdownPage page = MarkdownPage.Parse(text, entry.FullPath, Daemon.LoadScript, bookMarks, of.Numbering, of.KeywordLinks);
                foreach (MarkdownProblem problem in page.Errors)
                {
                    progress.Error($"{entry.Source}:{problem.Line}:{problem.Column}: {problem.Message}");
                    errors++;
                }
                foreach (MarkdownProblem problem in page.Warnings.Concat(of.Warnings))
                {
                    progress.Warning($"{entry.Source}:{problem.Line}:{problem.Column}: {problem.Message}");
                }
                string title = HtmlExporter.PlainText(page.Title ?? DisplayName(Path.GetFileName(entry.Source)[..^".geo.md".Length]));
                warn = message => progress.Warning($"{entry.Source}:{message}");
                pages[entry.File] = "#import \"../style.typ\": *\n\n" + Convert(page, entry, book, title, progress);
            }
            WriteProject(output, book, pages);
            progress.Finish($"Wrote {output}: {book.Count} page{(book.Count == 1 ? "" : "s")}, {files.Count} picture{(files.Count == 1 ? "" : "s")}");
            return errors > 0 ? 1 : 0;
        }

        // ------------------------------------------------------------------ a page

        // the level of a page's heading: below its chapters (a page in the book's folder: 1)
        private static int PageLevel(TypstPage page) => page.Source.Count(c => c == '/') + 1;

        private static string LabelOf(TypstPage page, string? id = null) =>
            "p" + page.Index.ToString(CultureInfo.InvariantCulture) + (id != null ? "-" + Regex.Replace(id, @"[^\p{L}\p{N}_\-.:]", "-") : "");

        private string Convert(MarkdownPage page, TypstPage entry, List<TypstPage> book, string title, Progress progress)
        {
            var parser = new HtmlParser();
            IHtmlDocument document = parser.ParseDocument("<!DOCTYPE html><html><body>" + page.Html + "</body></html>");
            IHtmlElement body = document.Body!;
            // footnotes: their text where they are referred to
            var footnotes = new Dictionary<string, IElement>(StringComparer.Ordinal);
            foreach (IElement note in body.QuerySelectorAll("div.footnotes li[id]").ToList())
            {
                foreach (IElement back in note.QuerySelectorAll("a.footnote-back-ref").ToList())
                {
                    back.Remove();
                }
                footnotes["#" + note.Id] = note;
            }
            foreach (IElement notes in body.QuerySelectorAll("div.footnotes").ToList())
            {
                notes.Remove();
            }
            var context = new PageContext(entry, book, page, footnotes, progress);
            var markup = new StringBuilder();
            markup.Append("#metadata(none) <").Append(LabelOf(entry)).Append(">\n\n");
            if (body.QuerySelector("h1") == null)
            {
                markup.Append("#heading(level: ").Append(PageLevel(entry)).Append(")[")
                    .Append(page.SectionNumber != null ? "#section-number[" + Escape(page.SectionNumber) + "]" : "").Append(Escape(title)).Append("]\n\n");
            }
            markup.Append(Blocks(body.ChildNodes, context));
            // a line break before or after a formula on a line of its own: that line is enough
            string result = Regex.Replace(markup.ToString(), @"#linebreak\(\)\s*(#display-formula)", "$1");
            result = Regex.Replace(result, @"(#display-formula\[(?:[^\[\]]|\[[^\[\]]*\])*\])\s*#linebreak\(\)", "$1");
            return Regex.Replace(result, @"\n{3,}", "\n\n").TrimEnd() + "\n";
        }

        private sealed record PageContext(TypstPage Page, List<TypstPage> Book, MarkdownPage Markdown, Dictionary<string, IElement> Footnotes, Progress Progress)
        {
            public bool PageHeadingDone { get; set; }
        }

        // block-level nodes, each followed by an empty line; loose text and inline elements together as a paragraph
        private string Blocks(IEnumerable<INode> nodes, PageContext context)
        {
            var result = new StringBuilder();
            var inline = new StringBuilder();
            void Flush()
            {
                string paragraph = inline.ToString().Trim();
                if (paragraph.Length > 0)
                {
                    result.Append(Paragraph(paragraph)).Append("\n\n");
                }
                inline.Clear();
            }
            foreach (INode node in nodes)
            {
                if (node is IElement element && IsBlock(element))
                {
                    Flush();
                    result.Append(Block(element, context)).Append("\n\n");
                }
                else
                {
                    inline.Append(Inline(node, context));
                }
            }
            Flush();
            return result.ToString();
        }

        private static bool IsBlock(IElement element) => element.LocalName switch
        {
            "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "ul" or "ol" or "blockquote" or "pre" or "table" or "hr" or "figure" or "div" => true,
            _ => false,
        };

        // a paragraph's markup: "1." at its start would be a numbered list
        private static string Paragraph(string markup) => Regex.Replace(markup, @"^(\d+)\.", @"$1\.");

        private string Block(IElement element, PageContext context)
        {
            string label = element.Id is { Length: > 0 } id && element.LocalName != "figure" ? " <" + LabelOf(context.Page, id) + ">" : "";
            switch (element.LocalName)
            {
                case "p":
                    return Paragraph(Inlines(element.ChildNodes, context).Trim()) + label;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    {
                        int level = element.LocalName[1] - '0';
                        // the page's heading below its chapters, the others below it; only the page's in the contents
                        bool pageHeading = level == 1 && !context.PageHeadingDone;
                        context.PageHeadingDone |= pageHeading;
                        int typstLevel = PageLevel(context.Page) + level - 1;
                        return $"#heading(level: {typstLevel}{(pageHeading ? "" : ", outlined: false")})[{Inlines(element.ChildNodes, context).Trim()}]{label}";
                    }
                case "ul":
                case "ol":
                    {
                        var items = element.Children.Where(c => c.LocalName == "li").Select(li => "[" + Blocks(li.ChildNodes, context).Trim() + "]");
                        string start = element.LocalName == "ol" && element.GetAttribute("start") is { } first ? $"start: {first}, " : "";
                        return (element.LocalName == "ul" ? "#list(" : "#enum(" + start) + string.Join(", ", items) + ")" + label;
                    }
                case "blockquote":
                    return "#quote(block: true)[" + Blocks(element.ChildNodes, context).Trim() + "]" + label;
                case "pre":
                    {
                        IElement? code = element.QuerySelector("code");
                        string? language = code?.ClassList.FirstOrDefault(c => c.StartsWith("language-", StringComparison.Ordinal))?["language-".Length..];
                        return "#raw(" + String((code ?? element).TextContent.TrimEnd('\n')) + ", block: true" + (language != null ? ", lang: " + String(language) : "") + ")";
                    }
                case "hr":
                    return "#line(length: 100%, stroke: 0.4pt)";
                case "table":
                    return Table(element, context) + label;
                case "figure" when element.ClassList.Contains("geo-figure"):
                    return Figure(element, context);
                case "div" when element.ClassList.Contains("statement"):
                    {
                        string kind = element.ClassList.FirstOrDefault(c => c.StartsWith("statement-", StringComparison.Ordinal))?["statement-".Length..] ?? "box";
                        return $"#statement({String(kind)})[{Blocks(element.ChildNodes, context).Trim()}]{label}";
                    }
                case "div" when element.ClassList.Contains("formula-block"):
                    return DisplayFormula(element, context) + label;
                default:
                    return Blocks(element.ChildNodes, context).Trim();
            }
        }

        private string Table(IElement table, PageContext context)
        {
            var rows = table.QuerySelectorAll("tr").ToList();
            int columns = rows.Count == 0 ? 1 : rows.Max(r => r.Children.Count(c => c.LocalName is "td" or "th"));
            var cells = new List<string>();
            foreach (IElement row in rows)
            {
                bool header = row.ParentElement?.LocalName == "thead";
                var line = row.Children.Where(c => c.LocalName is "td" or "th").Select(c => "[" + Inlines(c.ChildNodes, context).Trim() + "]").ToList();
                cells.Add(header ? "table.header(" + string.Join(", ", line) + ")" : string.Join(", ", line));
            }
            return $"#table(columns: {columns}, stroke: 0.4pt, inset: 0.4em, {string.Join(", ", cells.Where(c => c.Length > 0))})";
        }

        private string Inlines(IEnumerable<INode> nodes, PageContext context) => string.Concat(nodes.Select(node => Inline(node, context)));

        private string Inline(INode node, PageContext context)
        {
            if (node is IText text)
            {
                return Escape(Regex.Replace(text.Data, @"\s+", " "));
            }
            if (node is not IElement element)
            {
                return "";
            }
            string label = element.Id is { Length: > 0 } id ? " <" + LabelOf(context.Page, id) + ">" : "";
            string Content() => Inlines(element.ChildNodes, context);
            switch (element.LocalName)
            {
                case "strong" or "b":
                    return "#strong[" + Content() + "]" + label;
                case "em" or "i":
                    return "#emph[" + Content() + "]" + label;
                case "code":
                    return "#raw(" + String(element.TextContent) + ")";
                case "br":
                    return "#linebreak()";
                case "a":
                    return Link(element, Content(), context);
                case "svg" when element.ClassList.Contains("formula"):
                    return Formula(element.GetAttribute("aria-label") ?? "", context);
                case "span" when element.ClassList.Contains("formula-display"):
                    return DisplayFormula(element, context) + label;
                case "span" when element.ClassList.Contains("statement-label"):
                    return "#statement-label[" + Content() + "]";
                case "span" when element.ClassList.Contains("qed"):
                    return "#qed()";
                case "span" when element.ClassList.Contains("section-number"):
                    return "#section-number[" + Escape(element.TextContent) + "]";
                case "span" when element.ClassList.Contains("ref-missing"):
                    return "??";
                case "span" when element.ClassList.Contains("figure-label"):
                    return "#figure-label[" + Content() + "]";
                case "img":
                    return Escape(element.GetAttribute("alt") ?? "");
                default:
                    return Content() + label;
            }
        }

        // a formula on a line of its own, with its number ("(2.3)") if it has a mark
        private string DisplayFormula(IElement element, PageContext context)
        {
            IElement? number = element.QuerySelector(".formula-number");
            number?.Remove();
            return "#display-formula(" + (number != null ? "number: [" + Escape(number.TextContent) + "]" : "") + ")["
                + Inlines(element.ChildNodes, context).Trim() + "]";
        }

        // a link: a footnote, a reference into the book (a label), a page of the web, or its text
        private string Link(IElement link, string content, PageContext context)
        {
            string href = link.GetAttribute("href") ?? "";
            if (link.ClassList.Contains("footnote-ref") && context.Footnotes.TryGetValue(href, out IElement? note))
            {
                return "#footnote[" + Blocks(note.ChildNodes, context).Trim() + "]";
            }
            if (Uri.TryCreate(href, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" or "mailto")
            {
                return "#link(" + String(href) + ")[" + content + "]";
            }
            int hash = href.IndexOf('#');
            string path = WebUtility.UrlDecode(hash < 0 ? href : href[..hash]);
            string? anchor = hash < 0 || hash == href.Length - 1 ? null : href[(hash + 1)..];
            TypstPage? target = path.Length == 0 ? context.Page
                : context.Book.FirstOrDefault(p => string.Equals(p.FullPath, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(context.Page.FullPath)!, path)), StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                return "#link(<" + LabelOf(target, anchor) + ">)[" + content + "]";
            }
            string relative = Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(context.Page.FullPath)!, path))).Replace('\\', '/');
            if (relative.EndsWith(".geo", StringComparison.OrdinalIgnoreCase) && settings.AddressOf(PageName(relative)) is { } address)
            {
                return "#link(" + String(address + (anchor != null ? "#" + anchor : "")) + ")[" + content + "]";
            }
            return content;
        }

        // a formula in the line: its picture as high as it is (in em), lowered by its depth
        private string Formula(string source, PageContext context)
        {
            FormulaImage formula;
            try
            {
                formula = FormulaImage.Render(source, textSize);
            }
            catch (FormatException e)
            {
                context.Progress.Error($"{context.Page.Source}: {e.Message}");
                errors++;
                return "#raw(" + String("$" + source + "$") + ")";
            }
            string file = Picture("formulas", Encoding.UTF8.GetBytes(formula.Svg));
            // the line is as high as the formula, not as its picture: that has a margin of 0.04 em above and below
            double margin = 0.04 * textSize;
            return $"#box(baseline: {Em(formula.Depth - margin)}, height: {Em(formula.Height - 2 * margin)}, "
                + $"move(dy: -{Em(margin)}, image(\"../{file}\", height: {Em(formula.Height)}, alt: {String(source)})))";
        }

        private string Em(double points) => (Math.Round(points / textSize, 3) + 0.0).ToString("0.###", CultureInfo.InvariantCulture) + "em";

        // a figure: its pictures in their size in print, the caption below
        private string Figure(IElement figure, PageContext context)
        {
            var pictures = new List<string>();
            Dictionary<string, PageFigure> known = context.Markdown.PageFigures.DistinctBy(f => f.Key).ToDictionary(f => f.Key);
            foreach (IElement item in figure.QuerySelectorAll(".geo-item, .geo-placeholder"))
            {
                if (item.GetAttribute("data-figure") is { } key && known.TryGetValue(key, out PageFigure? shown))
                {
                    foreach (FigureImage image in Pictures(shown))
                    {
                        pictures.Add($"image(\"../{Picture("figures", Encoding.UTF8.GetBytes(image.Svg))}\")");
                    }
                }
                else
                {
                    pictures.Add("missing-figure(" + String(item.TextContent.Trim()) + ")");
                }
            }
            IElement? caption = figure.QuerySelector("figcaption");
            string captionMarkup = caption != null ? ", caption: [" + Inlines(caption.ChildNodes, context).Trim() + "]" : "";
            string label = figure.Id is { Length: > 0 } id ? " <" + LabelOf(context.Page, id) + ">" : "";
            return "#geo-figure(" + string.Join(", ", pictures) + captionMarkup + ")" + label;
        }

        // the pictures of a figure as in the book: a still, a row of stills (cut alike), or the last picture
        private List<FigureImage> Pictures(PageFigure figure)
        {
            if (!animations.TryGetValue(figure.Key, out FigureAnimation? animation))
            {
                animation = animations[figure.Key] = FigureAnimation.Parse(figure.AnimationJson);
            }
            List<FigureImage> images = BookPictures.Of(figure, animation, style, out FigureStyle used);
            if (BookPictures.TextWarning(figure, animation, used) is { } warning && warned.Add(figure.Key))
            {
                warn?.Invoke($"{figure.Reference.Line}:{figure.Reference.Column}: {warning}");
            }
            return images;
        }

        // the warnings of the figures of the page being made ("line:column: …"), and the figures warned about
        private Action<string>? warn;
        private readonly HashSet<string> warned = new(StringComparer.Ordinal);

        // a picture in the project, once for the same content: its path ("figures/1f2e….svg")
        private string Picture(string folder, byte[] content)
        {
            string name = folder + "/" + System.Convert.ToHexString(SHA256.HashData(content))[..12].ToLowerInvariant() + ".svg";
            files.TryAdd(name, content);
            return name;
        }

        // text for Typst's markup: its special characters escaped
        private static string Escape(string text) => MarkupCharacter().Replace(text, @"\$0");

        [GeneratedRegex(@"[\\#$*_\[\]<>@`~=\-+/]")]
        private static partial Regex MarkupCharacter();

        // a string for Typst's code: "…"
        private static string String(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

        // ------------------------------------------------------------------ the project

        private void WriteProject(string output, List<TypstPage> book, Dictionary<string, string> pages)
        {
            // what an earlier export left (pages, pictures): made anew
            foreach (string folder in new[] { "pages", "figures", "formulas" })
            {
                string path = Path.Combine(output, folder);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                Directory.CreateDirectory(path);
            }
            var utf8 = new UTF8Encoding(false);
            foreach ((string file, string markup) in pages)
            {
                File.WriteAllText(Path.Combine(output, "pages", file), markup, utf8);
            }
            foreach ((string name, byte[] content) in files)
            {
                File.WriteAllBytes(Path.Combine(output, name), content);
            }
            // the look: GeoAnim's standard, always anew; the book's own style.typ (which imports it), else one that only does
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("typst/standard.typ")!)
            using (var reader = new StreamReader(stream))
            {
                File.WriteAllText(Path.Combine(output, "standard.typ"), reader.ReadToEnd(), utf8);
            }
            string own = Path.Combine(root, "style.typ");
            if (File.Exists(own))
            {
                File.Copy(own, Path.Combine(output, "style.typ"), overwrite: true);
            }
            else
            {
                File.WriteAllText(Path.Combine(output, "style.typ"), """
                    // The look of the book: GeoAnim's standard (standard.typ). To change it, put a style.typ beside index.yaml
                    // that imports the standard and changes what it wants (see the start of standard.typ).
                    #import "standard.typ": *

                    """, utf8);
            }
            // the book's icon, for the title page
            foreach (string old in Directory.EnumerateFiles(output, "icon.*"))
            {
                File.Delete(old);
            }
            if (IconFile is { } icon)
            {
                File.Copy(settings.IconFile!, Path.Combine(output, icon), overwrite: true);
            }
            File.WriteAllText(Path.Combine(output, "book.typ"), Main(book), utf8);
        }

        // the name of the book's icon in the project ("icon.svg"), if it has one Typst can show
        private string? IconFile => settings.IconFile is { } file && Path.GetExtension(file).ToLowerInvariant() is ".svg" or ".png" or ".jpg" or ".jpeg"
            ? "icon" + Path.GetExtension(file).ToLowerInvariant()
            : null;

        // book.typ: the template, title page, contents, and the pages under their chapters
        private string Main(List<TypstPage> book)
        {
            string title = settings.Title ?? DisplayName(Path.GetFileName(root));
            string Optional(string? text) => text == null ? "none" : "[" + Escape(text) + "]";
            var main = new StringBuilder();
            main.Append("// made by geoanim typst – it is made anew with every export (the look: style.typ)\n")
                .Append("#import \"style.typ\": *\n\n")
                .Append("#show: book.with(title: ").Append(String(title))
                .Append(settings.Author != null ? ", author: " + String(settings.Author) : "")
                // the font size only if index.yaml says it ("print: text"); else the one of style.typ
                .Append(settings.TextSize is double size ? ", text-size: " + size.ToString("0.##", CultureInfo.InvariantCulture) + "pt" : "")
                .Append(", language: ").Append(String(settings.Language ?? "de")).Append(")\n\n")
                .Append("#title-page(title: [").Append(Escape(title)).Append("], author: ").Append(Optional(settings.Author))
                .Append(", description: ").Append(Optional(settings.Description))
                .Append(IconFile is { } icon ? ", icon: image(" + String(icon) + ")" : "").Append(")\n\n")
                .Append("#contents()\n\n");
            Dictionary<string, string> numbers = settings.Numbering.Headings ? Daemon.BookNumbers(book.Select(p => p.Source)) : [];
            string[] open = [];
            foreach (TypstPage page in book)
            {
                string[] folders = page.Source.Split('/')[..^1];
                // the chapters (folders) that start here
                int same = 0;
                while (same < Math.Min(open.Length, folders.Length) && open[same] == folders[same])
                {
                    same++;
                }
                for (int i = same; i < folders.Length; i++)
                {
                    string path = string.Join("/", folders[..(i + 1)]);
                    string number = numbers.TryGetValue(path, out string? n) ? String(n) : "none";
                    main.Append("#chapter(").Append(number).Append(", [").Append(Escape(DisplayName(folders[i]))).Append("]")
                        .Append(i > 0 ? ", level: " + (i + 1).ToString(CultureInfo.InvariantCulture) : "").Append(")\n\n");
                }
                open = folders;
                main.Append("#include \"pages/").Append(page.File).Append("\"\n\n");
            }
            return main.ToString();
        }
    }
}
