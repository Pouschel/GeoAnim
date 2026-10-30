using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Xhtml;
using GeoAnim.Figures;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

internal static partial class Program
{
    /// <summary>
    /// geoanim epub &lt;dir&gt; [-o book.epub] [-svg]: the pages of a book ("*.geo.md" below the folder with index.yaml, in
    /// book order) as an e-book (EPUB 3, with a table of contents for older readers too). Figures and formulas are pictures:
    /// PNG on white (Kindle shows no SVG in the text), or SVG with -svg. A figure is as wide as the page (pictures side by
    /// side share it); formulas are sized in em, so they grow with the reader's font size (made for "print: text", 11 pt).
    /// </summary>
    internal static int Epub(List<string> arguments)
    {
        string folder = arguments[0];
        string? output = null;
        bool svg = false;
        for (int i = 1; i < arguments.Count; i++)
        {
            if (arguments[i] is "-o" or "--output" && i + 1 < arguments.Count)
            {
                output = Path.GetFullPath(arguments[++i]);
            }
            else if (arguments[i] is "-svg" or "--svg")
            {
                svg = true;
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
        output ??= Path.Combine(Path.GetDirectoryName(root)!, Path.GetFileName(root) + ".epub");
        return new EpubWriter(root, svg).Write(output);
    }

    /// <summary>Makes the e-book of a book (see <see cref="Epub"/>).</summary>
    private sealed class EpubWriter(string root, bool svg)
    {
        // the resolution of pictures: figures as in print, formulas finer (they are small and enlarged with the text)
        private const double FigureDpi = FigurePng.DefaultDpi;
        private const double FormulaDpi = 600;

        private readonly Dictionary<string, byte[]> images = new(StringComparer.Ordinal);
        private readonly Dictionary<string, FigureAnimation> animations = new(StringComparer.Ordinal);
        private BookSettings settings = BookSettings.None;
        private FigureStyle style = FigureStyle.Default;
        private double textSize = 11;

        // a page of the e-book: its source (relative to the book), file in the e-book, title
        private sealed record EpubPage(string Source, string FullPath, string File, string Title)
        {
            // "2.1" (with numbered headings)
            public string? Number { get; set; }

            public string Body { get; set; } = "";
        }

        public int Write(string output)
        {
            List<string> pages = Directory.EnumerateFiles(root, "*.geo*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
                .Where(relative => !relative.Split('/').Any(part => part.StartsWith('_')))
                .Where(relative => relative.EndsWith(".geo.md", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".geo", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var progress = new Progress(pages.Count(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase)));
            settings = BookFile.Read(root, progress.Warning, out string? bookError);
            if (bookError != null)
            {
                progress.Error(bookError);
            }
            style = new FigureStyle(settings.FigureUnit ?? FigureStyle.Default.PointsPerUnit, settings.FigureSize ?? FigureStyle.Default.SizeFactor);
            textSize = settings.TextSize ?? 11;
            // scripts without a page of their own have no place in the e-book
            foreach (string script in pages.Where(p => p.EndsWith(".geo", StringComparison.OrdinalIgnoreCase)
                && !pages.Contains(p + ".md", StringComparer.OrdinalIgnoreCase)))
            {
                if (!pages.Any(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && File.ReadAllText(Path.Combine(root, p)).Contains(Path.GetFileName(script), StringComparison.Ordinal)))
                {
                    progress.Warning($"{script}: a script without a page (seite.geo.md) is not in the e-book.");
                }
            }
            List<string> sources = pages.Where(p => p.EndsWith(".geo.md", StringComparison.OrdinalIgnoreCase)).ToList();
            sources.Sort(BookOrder.Instance);
            if (sources.Count == 0)
            {
                progress.Error($"{root} has no pages (*.geo.md).");
                progress.Finish("No e-book");
                return 1;
            }
            var book = new List<EpubPage>();
            for (int i = 0; i < sources.Count; i++)
            {
                string file = Path.GetFileName(PageName(sources[i][..^".md".Length]));
                book.Add(new EpubPage(sources[i], Path.GetFullPath(Path.Combine(root, sources[i])),
                    $"p{(i + 1).ToString("00", CultureInfo.InvariantCulture)}-{Path.ChangeExtension(file, ".xhtml")}", ""));
            }
            int errors = 0;
            for (int i = 0; i < book.Count; i++)
            {
                EpubPage entry = book[i];
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
                book[i] = entry = entry with { Title = title };
                entry.Number = page.SectionNumber;
                entry.Body = Convert(page, entry, book, progress);
            }
            WriteZip(output, book);
            progress.Finish($"Wrote {output}: {book.Count} page{(book.Count == 1 ? "" : "s")}, {images.Count} picture{(images.Count == 1 ? "" : "s")}");
            return errors > 0 ? 1 : 0;
        }

        // ------------------------------------------------------------------ a page

        // the HTML of a page as the body of an XHTML file: figures and formulas as pictures, links into the e-book
        private string Convert(MarkdownPage page, EpubPage entry, List<EpubPage> book, Progress progress)
        {
            var parser = new HtmlParser();
            IHtmlDocument document = parser.ParseDocument("<!DOCTYPE html><html><body>" + page.Html + "</body></html>");
            IHtmlElement body = document.Body!;
            // formulas first: the captions of figures have them too
            foreach (IElement formula in body.QuerySelectorAll("svg.formula").ToList())
            {
                string source = formula.GetAttribute("aria-label") ?? "";
                formula.Replace(FormulaPicture(document, source, progress, entry));
            }
            Dictionary<string, PageFigure> figures = page.PageFigures.DistinctBy(f => f.Key).ToDictionary(f => f.Key);
            foreach (IElement figure in body.QuerySelectorAll("figure.geo-figure").ToList())
            {
                figure.Replace(Figure(document, figure, figures));
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
            return string.Concat(body.ChildNodes.Select(node => node.ToHtml(XhtmlMarkupFormatter.Instance)));
        }

        // a formula as a picture in the line: as high as it is in em, lowered by its depth
        private IElement FormulaPicture(IHtmlDocument document, string source, Progress progress, EpubPage entry)
        {
            FormulaImage formula;
            try
            {
                formula = FormulaImage.Render(source, textSize);
            }
            catch (FormatException e)
            {
                progress.Error($"{entry.Source}: {e.Message}");
                IElement faulty = document.CreateElement("code");
                faulty.TextContent = "$" + source + "$";
                return faulty;
            }
            string href = svg
                ? Picture("f", Encoding.UTF8.GetBytes(formula.Svg), ".svg")
                : Picture("f", formula.Png(FormulaDpi), ".png");
            IElement image = document.CreateElement("img");
            image.SetAttribute("src", "../" + href);
            image.SetAttribute("alt", source);
            image.SetAttribute("class", "formula");
            image.SetAttribute("style", $"width: {Em(formula.Width)}; height: {Em(formula.Height)}; vertical-align: {Em(-formula.Depth)}");
            return image;
        }

        private string Em(double points) => (Math.Round(points / textSize, 3) == 0 ? 0 : Math.Round(points / textSize, 3)).ToString("0.###", CultureInfo.InvariantCulture) + "em";

        // a figure: its pictures side by side (a row of steps one picture each), the caption below
        private IElement Figure(IHtmlDocument document, IElement figure, Dictionary<string, PageFigure> figures)
        {
            IElement block = document.CreateElement("div");
            block.SetAttribute("class", "figure");
            if (figure.Id is { Length: > 0 } id)
            {
                block.Id = id;
            }
            IElement? caption = figure.QuerySelector("figcaption");
            string alt = caption == null ? "Abbildung" : TextOf(caption).Trim();
            IElement row = document.CreateElement("div");
            row.SetAttribute("class", "figure-row");
            var pictures = new List<IElement>();
            foreach (IElement item in figure.QuerySelectorAll(".geo-item, .geo-placeholder"))
            {
                if (item.GetAttribute("data-figure") is { } key && figures.TryGetValue(key, out PageFigure? shown))
                {
                    foreach (FigureImage image in Pictures(shown))
                    {
                        string href = svg ? Picture("a", Encoding.UTF8.GetBytes(image.Svg), ".svg") : Picture("a", FigurePng.Render(image, FigureDpi), ".png");
                        IElement picture = document.CreateElement("img");
                        picture.SetAttribute("src", "../" + href);
                        picture.SetAttribute("alt", alt);
                        pictures.Add(picture);
                        row.AppendChild(picture);
                    }
                }
                else
                {
                    IElement missing = document.CreateElement("span");
                    missing.SetAttribute("class", "figure-missing");
                    missing.TextContent = item.TextContent.Trim();
                    row.AppendChild(missing);
                }
            }
            // as wide as the page (readers make pictures that wide anyway, whatever else is asked); two or more side by side
            // share it, at most three in a row
            string style = pictures.Count switch
            {
                1 => "width: 100%; margin-left: 0; margin-right: 0",
                2 => "width: 49%",
                _ => "width: 32%",
            };
            foreach (IElement picture in pictures)
            {
                picture.SetAttribute("style", style);
            }
            block.AppendChild(row);
            if (caption != null)
            {
                IElement text = document.CreateElement("p");
                text.SetAttribute("class", "caption");
                foreach (INode node in caption.ChildNodes.ToList())
                {
                    text.AppendChild(node);
                }
                block.AppendChild(text);
            }
            return block;
        }

        // the text of an element with the formulas' sources (for the alternative text of a picture)
        private static string TextOf(INode node) => node switch
        {
            IElement { LocalName: "img" } image => image.GetAttribute("alt") ?? "",
            IText text => text.Data,
            _ => string.Concat(node.ChildNodes.Select(TextOf)),
        };

        // the pictures of a figure as in the book: a still, a row of stills (cut alike), or the last picture
        private List<FigureImage> Pictures(PageFigure figure)
        {
            if (!animations.TryGetValue(figure.Key, out FigureAnimation? animation))
            {
                animation = animations[figure.Key] = FigureAnimation.Parse(figure.AnimationJson);
            }
            List<double> times = figure.From == null
                ? [animation.EndOf(animation.Steps.Count - 1)]
                : figure.Still
                    ? [animation.EndOf(figure.To!.Value)]
                    : Enumerable.Range(figure.From.Value, figure.To!.Value - figure.From.Value + 1).Select(animation.EndOf).ToList();
            return times.Select(time => FigureRenderer.Render(animation, time, style, figure.Crop ? times : null)).ToList();
        }

        // a picture in the e-book, once for the same content: its path ("images/a-1f2e….png")
        private string Picture(string prefix, byte[] content, string extension)
        {
            string name = "images/" + prefix + "-" + System.Convert.ToHexString(SHA256.HashData(content))[..12].ToLowerInvariant() + extension;
            images.TryAdd(name, content);
            return name;
        }

        // a link to a page of the book: to its file in the e-book; to a script without a page: to the web (with "url") or
        // none; others stay as they are
        private void Relink(IElement link, EpubPage entry, List<EpubPage> book)
        {
            string href = link.GetAttribute("href")!;
            if (href.StartsWith('#') || Uri.TryCreate(href, UriKind.Absolute, out _))
            {
                return;
            }
            int hash = href.IndexOf('#');
            string path = WebUtility.UrlDecode(hash < 0 ? href : href[..hash]);
            string anchor = hash < 0 ? "" : href[hash..];
            string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry.FullPath)!, path));
            if (book.FirstOrDefault(p => string.Equals(p.FullPath, target, StringComparison.OrdinalIgnoreCase)) is { } page)
            {
                link.SetAttribute("href", page == entry ? (anchor.Length > 0 ? anchor : "#") : page.File + anchor);
                return;
            }
            string relative = Path.GetRelativePath(root, target).Replace('\\', '/');
            if (relative.EndsWith(".geo", StringComparison.OrdinalIgnoreCase) && settings.AddressOf(PageName(relative)) is { } address)
            {
                link.SetAttribute("href", address + anchor);
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

        private string Language => settings.Language ?? "de";

        private string Title => settings.Title ?? DisplayName(Path.GetFileName(root));

        private static string X(string text) => WebUtility.HtmlEncode(text);

        private void WriteZip(string output, List<EpubPage> book)
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
            using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
            // "mimetype" first and stored, as EPUB wants it
            ZipArchiveEntry mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(mimetype.Open(), new UTF8Encoding(false)))
            {
                writer.Write("application/epub+zip");
            }
            void Text(string name, string content)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
            void Bytes(string name, byte[] content)
            {
                using Stream stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                stream.Write(content);
            }
            Text("META-INF/container.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                  </rootfiles>
                </container>
                """);
            IReadOnlyList<(string Name, int Weight, bool Italic, byte[] Data)> fonts = BookFonts.Files();
            foreach (var font in fonts)
            {
                Bytes("OEBPS/fonts/" + font.Name, font.Data);
            }
            Text("OEBPS/style.css", StyleSheet(fonts));
            Text("OEBPS/text/title.xhtml", Xhtml("Titel", TitlePage()));
            foreach (EpubPage page in book)
            {
                Text("OEBPS/text/" + page.File, Xhtml(page.Title, page.Body));
            }
            foreach ((string name, byte[] content) in images)
            {
                Bytes("OEBPS/" + name, content);
            }
            Text("OEBPS/nav.xhtml", Navigation(book));
            Text("OEBPS/toc.ncx", Ncx(book));
            Text("OEBPS/content.opf", Package(book, fonts));
        }

        private string Xhtml(string title, string body) => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{Language}" lang="{Language}">
            <head>
              <meta charset="UTF-8"/>
              <title>{X(title)}</title>
              <link rel="stylesheet" type="text/css" href="../style.css"/>
            </head>
            <body>
            {body}
            </body>
            </html>
            """;

        private string TitlePage()
        {
            var html = new StringBuilder();
            html.Append("<div class=\"title-page\">\n<h1 class=\"book-title\">").Append(X(Title)).Append("</h1>\n");
            if (settings.Author != null)
            {
                html.Append("<p class=\"book-author\">").Append(X(settings.Author)).Append("</p>\n");
            }
            if (settings.Description != null)
            {
                html.Append("<p class=\"book-description\">").Append(X(settings.Description)).Append("</p>\n");
            }
            return html.Append("</div>").ToString();
        }

        // the pages by their chapters (folders), as nested lists: (chapter or null, page or null, children)
        private sealed record TocEntry(string Label, string? File, List<TocEntry> Children);

        private List<TocEntry> Contents(List<EpubPage> book)
        {
            var top = new List<TocEntry>();
            // chapters and pages with their numbers ("2 Abstandsgeometrie", "2.1 Abstandsaxiome")
            Dictionary<string, string> numbers = settings.Numbering.Headings ? Daemon.BookNumbers(book.Select(p => p.Source)) : [];
            foreach (EpubPage page in book)
            {
                List<TocEntry> level = top;
                string[] folders = page.Source.Split('/')[..^1];
                for (int i = 0; i < folders.Length; i++)
                {
                    string path = string.Join("/", folders[..(i + 1)]);
                    string label = (numbers.TryGetValue(path, out string? number) ? number + " " : "") + DisplayName(folders[i]);
                    if (level.Count == 0 || level[^1].File != null || level[^1].Label != label)
                    {
                        level.Add(new TocEntry(label, null, []));
                    }
                    level = level[^1].Children;
                }
                level.Add(new TocEntry((page.Number != null ? page.Number + " " : "") + page.Title, page.File, []));
            }
            return top;
        }

        // a chapter leads to its first page
        private static string FirstFile(TocEntry entry) => entry.File ?? FirstFile(entry.Children[0]);

        private string Navigation(List<EpubPage> book)
        {
            var html = new StringBuilder();
            void List(List<TocEntry> entries, int depth)
            {
                string indent = new(' ', depth * 2);
                html.Append(indent).Append("<ol>\n");
                foreach (TocEntry entry in entries)
                {
                    html.Append(indent).Append("  <li><a href=\"text/").Append(FirstFile(entry)).Append("\">").Append(X(entry.Label)).Append("</a>");
                    if (entry.Children.Count > 0)
                    {
                        html.Append('\n');
                        List(entry.Children, depth + 2);
                        html.Append(indent).Append("  ");
                    }
                    html.Append("</li>\n");
                }
                html.Append(indent).Append("</ol>\n");
            }
            List(Contents(book), 1);
            return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE html>
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{Language}" lang="{Language}">
                <head>
                  <meta charset="UTF-8"/>
                  <title>{X(Title)}</title>
                  <link rel="stylesheet" type="text/css" href="style.css"/>
                </head>
                <body>
                <nav epub:type="toc" id="toc">
                  <h1>Inhalt</h1>
                {html}</nav>
                <nav epub:type="landmarks" hidden="hidden">
                  <ol>
                    <li><a epub:type="toc" href="nav.xhtml#toc">Inhalt</a></li>
                    <li><a epub:type="bodymatter" href="text/{book[0].File}">{X(book[0].Title)}</a></li>
                  </ol>
                </nav>
                </body>
                </html>
                """;
        }

        // the table of contents for readers of EPUB 2 (and Kindle's older ones)
        private string Ncx(List<EpubPage> book)
        {
            var points = new StringBuilder();
            // a chapter and its first page lead to the same place: the same place in the order
            var orders = new Dictionary<string, int>(StringComparer.Ordinal);
            int id = 0;
            void Points(List<TocEntry> entries, int depth)
            {
                string indent = new(' ', depth * 2);
                foreach (TocEntry entry in entries)
                {
                    id++;
                    string file = FirstFile(entry);
                    if (!orders.TryGetValue(file, out int order))
                    {
                        order = orders[file] = orders.Count + 1;
                    }
                    points.Append(indent).Append("<navPoint id=\"n").Append(id).Append("\" playOrder=\"").Append(order).Append("\">\n")
                        .Append(indent).Append("  <navLabel><text>").Append(X(entry.Label)).Append("</text></navLabel>\n")
                        .Append(indent).Append("  <content src=\"text/").Append(FirstFile(entry)).Append("\"/>\n");
                    Points(entry.Children, depth + 1);
                    points.Append(indent).Append("</navPoint>\n");
                }
            }
            Points(Contents(book), 2);
            return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1" xml:lang="{Language}">
                  <head>
                    <meta name="dtb:uid" content="{Identifier}"/>
                  </head>
                  <docTitle><text>{X(Title)}</text></docTitle>
                  <navMap>
                {points}  </navMap>
                </ncx>
                """;
        }

        // the same for the same book: from its address or title
        private string Identifier
        {
            get
            {
                byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(settings.Url ?? Title));
                hash[6] = (byte)(hash[6] & 0x0f | 0x50);
                hash[8] = (byte)(hash[8] & 0x3f | 0x80);
                string hex = System.Convert.ToHexString(hash[..16]).ToLowerInvariant();
                return $"urn:uuid:{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
            }
        }

        private string Package(List<EpubPage> book, IReadOnlyList<(string Name, int Weight, bool Italic, byte[] Data)> fonts)
        {
            var manifest = new StringBuilder();
            manifest.Append("    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n")
                .Append("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>\n")
                .Append("    <item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>\n")
                .Append("    <item id=\"title\" href=\"text/title.xhtml\" media-type=\"application/xhtml+xml\"/>\n");
            for (int i = 0; i < fonts.Count; i++)
            {
                manifest.Append($"    <item id=\"font{i + 1}\" href=\"fonts/{fonts[i].Name}\" media-type=\"font/ttf\"/>\n");
            }
            for (int i = 0; i < book.Count; i++)
            {
                manifest.Append($"    <item id=\"page{i + 1}\" href=\"text/{book[i].File}\" media-type=\"application/xhtml+xml\"/>\n");
            }
            int number = 0;
            foreach (string name in images.Keys)
            {
                number++;
                string type = name.EndsWith(".svg", StringComparison.Ordinal) ? "image/svg+xml" : "image/png";
                manifest.Append($"    <item id=\"image{number}\" href=\"{name}\" media-type=\"{type}\"/>\n");
            }
            // the table of contents is a page too (as Kindle wants it), after the title
            string spine = "    <itemref idref=\"title\"/>\n    <itemref idref=\"nav\"/>\n" + string.Concat(book.Select((_, i) => $"    <itemref idref=\"page{i + 1}\"/>\n"));
            string modified = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            string author = settings.Author != null ? $"\n    <dc:creator>{X(settings.Author)}</dc:creator>" : "";
            string description = settings.Description != null ? $"\n    <dc:description>{X(settings.Description)}</dc:description>" : "";
            return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id" xml:lang="{Language}">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="book-id">{Identifier}</dc:identifier>
                    <dc:title>{X(Title)}</dc:title>
                    <dc:language>{Language}</dc:language>{author}{description}
                    <meta property="dcterms:modified">{modified}</meta>
                  </metadata>
                  <manifest>
                {manifest}  </manifest>
                  <spine toc="ncx">
                {spine}  </spine>
                </package>
                """;
        }

        // the look: the text in STIX Two Text (as the figures), statements with a line on the left (no background: the
        // pictures of formulas are white), figures centred; nothing a Kindle does not know (no flex, no variables)
        private static string StyleSheet(IReadOnlyList<(string Name, int Weight, bool Italic, byte[] Data)> fonts)
        {
            var css = new StringBuilder();
            foreach (var font in fonts)
            {
                css.Append($"@font-face {{\n  font-family: 'STIX Two Text';\n  font-weight: {font.Weight};\n  font-style: {(font.Italic ? "italic" : "normal")};\n  src: url(fonts/{font.Name});\n}}\n\n");
            }
            css.Append("""
                body {
                  font-family: 'STIX Two Text', serif;
                  line-height: 1.4;
                  -epub-hyphens: auto;
                  hyphens: auto;
                }

                h1, h2, h3, h4 {
                  line-height: 1.25;
                  -epub-hyphens: none;
                  hyphens: none;
                  page-break-after: avoid;
                }

                h1 {
                  margin: 0.5em 0 1em;
                  font-size: 1.6em;
                }

                h2 {
                  margin: 1.5em 0 0.6em;
                  font-size: 1.25em;
                }

                h3 {
                  margin: 1.2em 0 0.5em;
                  font-size: 1.1em;
                }

                p {
                  margin: 0 0 0.7em;
                }

                code {
                  font-family: monospace;
                  font-size: 0.9em;
                }

                table {
                  margin: 0 0 0.8em;
                  border-collapse: collapse;
                }

                th, td {
                  padding: 0.2em 0.6em;
                  border: 1px solid #999;
                }

                img.formula {
                  margin: 0;
                }

                .formula-block,
                .formula-display {
                  display: block;
                  margin: 0.6em 0 0.8em;
                  text-align: center;
                  text-indent: 0;
                }

                .figure {
                  margin: 1em 0 1.2em;
                  text-align: center;
                  page-break-inside: avoid;
                }

                .figure-row img {
                  max-width: 100%;
                  margin: 0 0.4% 0.3em;
                  vertical-align: top;
                }

                .figure-missing {
                  display: inline-block;
                  padding: 2em 1em;
                  border: 1px dashed #c62828;
                  color: #c62828;
                  font-family: monospace;
                  font-size: 0.8em;
                }

                .caption {
                  margin: 0.2em 0 0;
                  font-size: 0.9em;
                  text-align: center;
                }

                .section-number {
                  margin-right: 0.3em;
                }

                .figure-label,
                .statement-label {
                  font-style: normal;
                  font-weight: bold;
                }

                .statement {
                  margin: 1em 0;
                  padding: 0.4em 0.8em;
                  border-left: 3px solid #9fb3cf;
                }

                .statement p:last-child {
                  margin-bottom: 0;
                }

                .statement-axiom,
                .statement-theorem,
                .statement-lemma,
                .statement-corollary {
                  font-style: italic;
                }

                .statement-box {
                  border-left-color: #b8bec7;
                }

                .statement-definition {
                  border-left-color: #8fbf9a;
                }

                .statement-proof,
                .statement-example,
                .statement-remark {
                  padding: 0;
                  border: none;
                }

                .qed {
                  float: right;
                }

                .title-page {
                  margin-top: 30%;
                  text-align: center;
                }

                .book-title {
                  font-size: 2em;
                }

                .book-author {
                  margin-top: 1.5em;
                  font-size: 1.2em;
                }

                .book-description {
                  margin-top: 2em;
                  font-style: italic;
                }

                nav ol {
                  list-style-type: none;
                }
                """);
            return css.ToString();
        }
    }
}
