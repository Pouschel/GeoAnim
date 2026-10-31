using System.Globalization;
using System.Net;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using GeoAnim.Figures;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

internal static partial class Program
{
    /// <summary>
    /// geoanim html &lt;dir&gt; [-o book.html]: the whole book in one HTML file, to read through and search in the browser
    /// (not for a web server): the pages one after another as in the preview's book view, the figures as stills in their
    /// size in print (in proportion to the text), formulas as SVG, the contents at the side; references and keywords lead
    /// to their place in the file.
    /// </summary>
    internal static int HtmlBook(List<string> arguments)
    {
        string folder = arguments[0];
        string? output = null;
        for (int i = 1; i < arguments.Count; i++)
        {
            if (arguments[i] is "-o" or "--output" && i + 1 < arguments.Count)
            {
                output = Path.GetFullPath(arguments[++i]);
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
        output ??= Path.Combine(Path.GetDirectoryName(root)!, Path.GetFileName(root) + ".html");
        return new HtmlBookWriter(root).Write(output);
    }

    /// <summary>Makes the HTML file of a book (see <see cref="HtmlBook"/>).</summary>
    private sealed class HtmlBookWriter(string root)
    {
        private readonly Dictionary<string, FigureAnimation> animations = new(StringComparer.Ordinal);
        private BookSettings settings = BookSettings.None;
        private FigureStyle style = FigureStyle.Default;
        private double textSize = 11;
        private int pictures;

        // a page of the book: its source (relative to the book), its id in the file ("p3"), title, number ("2.1"), body
        private sealed record BookPage(string Source, string FullPath, string Id)
        {
            public string Title { get; set; } = "";

            public string? Number { get; set; }

            public string Body { get; set; } = "";
        }

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
            style = new FigureStyle(settings.FigureUnit ?? FigureStyle.Default.PointsPerUnit, settings.FigureSize ?? FigureStyle.Default.SizeFactor);
            textSize = settings.TextSize ?? 11;
            if (sources.Count == 0)
            {
                progress.Error($"{root} has no pages (*.geo.md).");
                progress.Finish("No book");
                return 1;
            }
            List<BookPage> book = sources.Select((source, i) => new BookPage(source, Path.GetFullPath(Path.Combine(root, source)),
                "p" + (i + 1).ToString(CultureInfo.InvariantCulture))).ToList();
            int errors = 0;
            foreach (BookPage entry in book)
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
                entry.Title = HtmlExporter.PlainText(page.Title ?? DisplayName(Path.GetFileName(entry.Source)[..^".geo.md".Length]));
                entry.Number = page.SectionNumber;
                warn = message => progress.Warning($"{entry.Source}:{message}");
                entry.Body = Convert(page, entry, book);
            }
            File.WriteAllText(output, Document(book), new UTF8Encoding(false));
            progress.Finish($"Wrote {output}: {book.Count} page{(book.Count == 1 ? "" : "s")}, {pictures} picture{(pictures == 1 ? "" : "s")}");
            return errors > 0 ? 1 : 0;
        }

        // ------------------------------------------------------------------ a page

        // the HTML of a page in the file: figures as stills, its ids and links made unique in the file ("p3-ax")
        private string Convert(MarkdownPage page, BookPage entry, List<BookPage> book)
        {
            IHtmlDocument document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + page.Html + "</body></html>");
            IHtmlElement body = document.Body!;
            Dictionary<string, PageFigure> figures = page.PageFigures.DistinctBy(f => f.Key).ToDictionary(f => f.Key);
            foreach (IElement item in body.QuerySelectorAll(".geo-item[data-figure]").ToList())
            {
                if (figures.TryGetValue(item.GetAttribute("data-figure")!, out PageFigure? figure))
                {
                    item.InnerHtml = Stills(figure);
                }
            }
            foreach (IElement element in body.QuerySelectorAll("[id]"))
            {
                element.Id = entry.Id + "-" + element.Id;
            }
            foreach (IElement link in body.QuerySelectorAll("a[href]").ToList())
            {
                Relink(link, entry, book);
            }
            // what only the preview needs
            foreach (IElement element in body.QuerySelectorAll("*"))
            {
                foreach (string name in element.Attributes.Select(a => a.Name).Where(n => n.StartsWith("data-", StringComparison.Ordinal)).ToList())
                {
                    element.RemoveAttribute(name);
                }
            }
            if (body.QuerySelector("h1") == null)
            {
                IElement heading = document.CreateElement("h1");
                heading.TextContent = (entry.Number != null ? entry.Number + " " : "") + entry.Title;
                body.Prepend(heading);
            }
            return body.InnerHtml;
        }

        // the pictures of a figure as in print, in proportion to the text (a point of the book's text is 1/textSize em), in a
        // row of stills each with its step's description – as the preview's book view shows them
        private string Stills(PageFigure figure)
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
            List<string?> captions = BookPictures.Captions(figure, animation, images.Count);
            var html = new StringBuilder("<div class=\"geo-print\">");
            for (int i = 0; i < images.Count; i++)
            {
                pictures++;
                string width = (Math.Round(images[i].Width / textSize, 3) + 0.0).ToString("0.###", CultureInfo.InvariantCulture) + "em";
                html.Append("<div class=\"geo-print-picture\" style=\"width: ").Append(width).Append("\">")
                    .Append("<img src=\"data:image/svg+xml;base64,").Append(System.Convert.ToBase64String(Encoding.UTF8.GetBytes(images[i].Svg)))
                    .Append("\" alt=\"Abbildung\" width=\"").Append(Math.Round(images[i].Width).ToString(CultureInfo.InvariantCulture))
                    .Append("\" height=\"").Append(Math.Round(images[i].Height).ToString(CultureInfo.InvariantCulture)).Append("\">");
                if (captions[i] is { } caption)
                {
                    html.Append("<div class=\"geo-print-caption\">").Append(MarkdownPage.InlineHtml(caption)).Append("</div>");
                }
                html.Append("</div>");
            }
            return html.Append("</div>").ToString();
        }

        // the warnings of the figures of the page being made ("line:column: …"), and the figures warned about
        private Action<string>? warn;
        private readonly HashSet<string> warned = new(StringComparer.Ordinal);

        // a link into the book: to its place in the file; to a script without a page: to the web (with "url") or none;
        // others stay as they are
        private void Relink(IElement link, BookPage entry, List<BookPage> book)
        {
            string href = link.GetAttribute("href")!;
            if (Uri.TryCreate(href, UriKind.Absolute, out _))
            {
                return;
            }
            if (href.StartsWith('#'))
            {
                link.SetAttribute("href", href.Length > 1 ? "#" + entry.Id + "-" + href[1..] : "#" + entry.Id);
                return;
            }
            int hash = href.IndexOf('#');
            string path = WebUtility.UrlDecode(hash < 0 ? href : href[..hash]);
            string? anchor = hash < 0 || hash == href.Length - 1 ? null : href[(hash + 1)..];
            string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry.FullPath)!, path));
            if (book.FirstOrDefault(p => string.Equals(p.FullPath, target, StringComparison.OrdinalIgnoreCase)) is { } page)
            {
                link.SetAttribute("href", "#" + page.Id + (anchor != null ? "-" + anchor : ""));
                return;
            }
            string relative = Path.GetRelativePath(root, target).Replace('\\', '/');
            if (relative.EndsWith(".geo", StringComparison.OrdinalIgnoreCase) && settings.AddressOf(PageName(relative)) is { } address)
            {
                link.SetAttribute("href", address + (anchor != null ? "#" + anchor : ""));
                return;
            }
            // no place for it: the text without the link
            foreach (INode node in link.ChildNodes.ToList())
            {
                link.Parent!.InsertBefore(node, link);
            }
            link.Remove();
        }

        // ------------------------------------------------------------------ the file

        private string Title => settings.Title ?? DisplayName(Path.GetFileName(root));

        private static string X(string text) => WebUtility.HtmlEncode(text);

        private string Document(List<BookPage> book)
        {
            var html = new StringBuilder();
            html.Append($"""
                <!doctype html>
                <html lang="{X(settings.Language ?? "de")}">
                <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{X(Title)}</title>
                <style>
                {HtmlExporter.PageStyles()}
                {StyleSheet}
                </style>
                </head>
                <body>
                <nav id="contents">
                <a class="contents-title" href="#top">{X(Title)}</a>
                {Contents(book)}</nav>
                <main id="page">
                <header id="top" class="title-page">

                """);
            if (settings.Author != null)
            {
                html.Append("<p class=\"book-author\">").Append(X(settings.Author)).Append("</p>\n");
            }
            html.Append("<h1 class=\"book-title\">").Append(X(Title)).Append("</h1>\n");
            if (settings.IconFile is { } icon && Path.GetExtension(icon).ToLowerInvariant() is var extension && extension is ".svg" or ".png")
            {
                html.Append("<p class=\"book-icon\"><img src=\"data:image/").Append(extension == ".svg" ? "svg+xml" : "png").Append(";base64,")
                    .Append(System.Convert.ToBase64String(File.ReadAllBytes(icon))).Append("\" alt=\"\"></p>\n");
            }
            if (settings.Description != null)
            {
                html.Append("<p class=\"book-description\">").Append(X(settings.Description)).Append("</p>\n");
            }
            html.Append("</header>\n");
            foreach (BookPage page in book)
            {
                html.Append("<section class=\"book-page\" id=\"").Append(page.Id).Append("\">\n").Append(page.Body).Append("\n</section>\n");
            }
            html.Append("</main>\n</body>\n</html>\n");
            return html.ToString();
        }

        // the contents: chapters (folders) with their pages, numbered as in the book
        private string Contents(List<BookPage> book)
        {
            Dictionary<string, string> numbers = settings.Numbering.Headings ? Daemon.BookNumbers(book.Select(p => p.Source)) : [];
            var html = new StringBuilder("<ol>\n");
            string[] open = [];
            foreach (BookPage page in book)
            {
                string[] folders = page.Source.Split('/')[..^1];
                int same = 0;
                while (same < open.Length && same < folders.Length && open[same] == folders[same])
                {
                    same++;
                }
                for (int i = open.Length; i > same; i--)
                {
                    html.Append("</ol></li>\n");
                }
                for (int i = same; i < folders.Length; i++)
                {
                    string path = string.Join("/", folders[..(i + 1)]);
                    string label = (numbers.TryGetValue(path, out string? number) ? number + " " : "") + DisplayName(folders[i]);
                    html.Append("<li class=\"contents-chapter\"><a href=\"#").Append(page.Id).Append("\">").Append(X(label)).Append("</a><ol>\n");
                }
                open = folders;
                html.Append("<li><a href=\"#").Append(page.Id).Append("\">").Append(X((page.Number != null ? page.Number + " " : "") + page.Title)).Append("</a></li>\n");
            }
            for (int i = open.Length; i > 0; i--)
            {
                html.Append("</ol></li>\n");
            }
            return html.Append("</ol>\n").ToString();
        }

        // what the preview's look lacks: the contents at the side (above the text on narrow screens), the title page, a
        // line between the pages
        private const string StyleSheet = """
            html {
              scroll-padding-top: 1em;
            }

            #page {
              min-height: 0;
            }

            #contents {
              box-sizing: border-box;
              max-width: 40em;
              margin: 0 auto;
              padding: 1em 2em;
              background: #f7f8fa;
              font: 14px/1.4 'Segoe UI', system-ui, sans-serif;
            }

            #contents ol {
              margin: 0;
              padding-left: 1.1em;
              list-style: none;
            }

            #contents > ol {
              padding-left: 0;
            }

            #contents li {
              margin: 0.15em 0;
            }

            #contents a {
              color: #333;
              text-decoration: none;
            }

            #contents a:hover {
              color: #1565c0;
              text-decoration: underline;
            }

            .contents-title {
              display: block;
              margin-bottom: 0.6em;
              font-weight: 700;
            }

            .contents-chapter > a {
              font-weight: 600;
            }

            @media (min-width: 1100px) {
              #contents {
                position: fixed;
                top: 0;
                bottom: 0;
                left: 0;
                width: calc(50vw - 336px);
                max-width: 320px;
                overflow-y: auto;
                background: none;
              }
            }

            .title-page {
              padding: 3em 0 2em;
              text-align: center;
            }

            .book-title {
              font-size: 2em;
            }

            .book-author {
              font-size: 1.2em;
            }

            .book-icon img {
              width: 8em;
            }

            .book-description {
              font-style: italic;
            }

            .book-page {
              padding-top: 1em;
              border-top: 1px solid #e3e6eb;
            }
            """;
    }
}
