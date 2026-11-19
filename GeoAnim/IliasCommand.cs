using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using GeoAnim.Figures;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

internal static partial class Program
{
    /// <summary>
    /// A question pool for ILIAS ("type: ilias"): for every group a file to import (name-U__qti_1.zip: QTI 1.2 as ILIAS
    /// writes it) and an HTML file to check it (name-U.html: the questions with their answers and points).
    /// </summary>
    private static int Ilias(string file, string text, DocumentHead head, string folder, string stem)
    {
        List<string?> groups = head.Groups.Count > 0 ? head.Groups.Cast<string?>().ToList() : [null];
        var progress = new Progress(groups.Count * 2);
        var html = new HtmlBookWriter(Path.GetDirectoryName(file)!);
        int errors = 0;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        string? overview = null;
        foreach (string? group in groups)
        {
            progress.Step("ILIAS: " + (group != null ? "Gruppe " + group : "Fragen"));
            MarkdownPage page = MarkdownPage.Parse(text, file, Daemon.LoadScript, variant: new SheetVariant(group, true));
            // the problems once (most are the same for every group)
            foreach (MarkdownProblem problem in page.Errors.Where(p => reported.Add("E" + p.Line + ":" + p.Message)))
            {
                progress.Error($"{Path.GetFileName(file)}:{problem.Line}:{problem.Column}: {problem.Message}");
                errors++;
            }
            foreach (MarkdownProblem problem in page.Warnings.Where(p => reported.Add("W" + p.Line + ":" + p.Message)))
            {
                progress.Warning($"{Path.GetFileName(file)}:{problem.Line}:{problem.Column}: {problem.Message}");
            }
            string name = stem + (group != null ? "-" + group : "");
            // the points of all groups (the same in every file)
            overview ??= MarkdownPage.PointsOverview(text, file, Daemon.LoadScript, page, group);
            html.WriteControl(file, page, head, Path.Combine(folder, name + ".html"), overview);
            progress.Step("ILIAS: " + name + "__qti_1.zip");
            var writer = new IliasWriter(file, page);
            WritePool(writer.Pool(), Path.Combine(folder, name + "__qti_1.zip"));
            foreach (string problem in writer.Problems)
            {
                progress.Error($"{Path.GetFileName(file)} ({group}): {problem}");
                errors++;
            }
        }
        progress.Finish($"Wrote the questions of {groups.Count} group{(groups.Count == 1 ? "" : "s")} for ILIAS to {folder}");
        return errors > 0 ? 1 : 0;
    }

    // the zip as ILIAS takes it: a folder of the zip's name with the XML of the same name
    private static void WritePool(XDocument pool, string zip)
    {
        string name = Path.GetFileNameWithoutExtension(zip);
        if (File.Exists(zip))
        {
            File.Delete(zip);
        }
        using ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry(name + "/" + name + ".xml", CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(true), Indent = false });
        pool.Save(xml);
    }

    /// <summary>
    /// The questions of a page (for one group, with the solutions) as ILIAS imports them: a cloze question for blanks
    /// (choices and written answers), a matching question for "::: match", a single choice question for "::: choice";
    /// formulas and figures as pictures in the text, code in colour.
    /// </summary>
    private sealed partial class IliasWriter(string file, MarkdownPage page)
    {
        private static int nextNumber = (int)(DateTime.Now - new DateTime(2020, 1, 1)).TotalSeconds;

        private readonly (FigureStyle Style, double TextSize) print = BookPictures.PrintOf(Path.GetDirectoryName(file)!);
        private readonly Dictionary<string, PageFigure> figures = page.PageFigures.DistinctBy(f => f.Key).ToDictionary(f => f.Key);

        /// <summary>What could not be made (a question without its pieces).</summary>
        public List<string> Problems { get; } = [];

        public XDocument Pool()
        {
            IHtmlDocument document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + page.Html + "</body></html>");
            var root = new XElement("questestinterop");
            IElement? heading = null;
            var content = new List<IElement>();
            foreach (IElement element in document.Body!.Children.ToList())
            {
                if (element.LocalName == "h2" && element.HasAttribute("data-question"))
                {
                    if (heading != null)
                    {
                        root.Add(Question(heading, content));
                    }
                    heading = element;
                    content = [];
                }
                else if (heading != null)
                {
                    content.Add(element);
                }
            }
            if (heading != null)
            {
                root.Add(Question(heading, content));
            }
            return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        }

        // ------------------------------------------------------------------ a question

        private XElement Question(IElement heading, List<IElement> content)
        {
            string title = heading.GetAttribute("data-question")!;
            double points = double.Parse(heading.GetAttribute("data-points")!, CultureInfo.InvariantCulture);
            var item = new XElement("item", new XAttribute("ident", "il_0_qst_" + nextNumber++), new XAttribute("title", title));
            var metadata = new XElement("qtimetadata");
            var flow = new XElement("flow");
            var processing = new XElement("resprocessing", new XElement("outcomes", new XElement("decvar")));
            item.Add(new XElement("qticomment"), new XElement("duration", "P0Y0M0DT6H0M0S"), new XElement("itemmetadata", metadata),
                new XElement("presentation", new XAttribute("label", title), flow), processing);
            Field(metadata, "ILIAS_VERSION", "5.4.10 2020-03-04");
            Field(metadata, "AUTHOR", "GeoAnim");
            if (content.FirstOrDefault(e => e.ClassList.Contains("match")) is { } match)
            {
                Matching(item, metadata, flow, processing, content.Where(e => e != match).ToList(), match, (int)Math.Round(points), title);
            }
            else if (content.FirstOrDefault(e => e.ClassList.Contains("single-choice")) is { } choice)
            {
                SingleChoice(metadata, flow, processing, content.Where(e => e != choice).ToList(), choice, points);
            }
            else
            {
                Cloze(metadata, flow, processing, content, heading.GetAttribute("data-rating") ?? "l2", title);
            }
            return item;
        }

        private static void Field(XElement metadata, string label, string entry) =>
            metadata.Add(new XElement("qtimetadatafield", new XElement("fieldlabel", label), new XElement("fieldentry", entry)));

        private static XElement Material(string html, bool xhtml = true) =>
            new("material", new XElement("mattext", xhtml ? new XAttribute("texttype", "text/xhtml") : null, html.Length == 0 ? " " : html));

        private static XElement Condition(XElement test, double points) =>
            new("respcondition", new XAttribute("continue", "Yes"), new XElement("conditionvar", test),
                new XElement("setvar", new XAttribute("action", "Add"), Number(points)));

        private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        // a cloze question: the text around the blanks (each with its points behind it), the blanks: choices or written answers
        private void Cloze(XElement metadata, XElement flow, XElement processing, List<IElement> content, string rating, string title)
        {
            Field(metadata, "QUESTIONTYPE", "CLOZE QUESTION");
            Field(metadata, "feedback_mode", "gapQuestion");
            Field(metadata, "textgaprating", rating);
            Field(metadata, "identicalScoring", "1");
            Field(metadata, "combinations", "W10=");
            var gaps = new List<JsonElement>();
            foreach (IElement content1 in content)
            {
                foreach (IElement gap in new[] { content1 }.Concat(content1.QuerySelectorAll("span.gap")).Where(e => e.ClassList.Contains("gap")).ToList())
                {
                    gaps.Add(JsonDocument.Parse(gap.GetAttribute("data-gap")!).RootElement.Clone());
                    gap.Replace(gap.Owner!.CreateTextNode("\u0001" + (gaps.Count - 1) + "\u0002"));
                }
            }
            if (gaps.Count == 0)
            {
                Problems.Add($"\"{title}\" has no blanks.");
            }
            // ILIAS: the first text is the question (above), the rest the cloze text: so the question is all before the
            // first block with a blank; in the cloze text a list's items paragraphs
            int first = content.FindIndex(e => e.TextContent.Contains('\u0001'));
            first = first < 0 ? content.Count : first;
            flow.Add(Material(Html(content[..first])));
            string[] pieces = ("\u0002" + Html([Lines(content[first..])], inner: true)).Split('\u0001');
            flow.Add(Material(pieces[0][1..]));
            for (int k = 1; k < pieces.Length; k++)
            {
                int close = pieces[k].IndexOf('\u0002');
                int index = int.Parse(pieces[k][..close], CultureInfo.InvariantCulture);
                Gap(flow, processing, gaps[index], "gap_" + index, rating);
                flow.Add(Material(pieces[k][(close + 1)..]));
            }
        }

        // the blocks in one element, a list's items each a paragraph (ILIAS keeps the paragraphs of a cloze text, but not
        // a list that the blanks cut into pieces)
        private static IElement Lines(List<IElement> blocks)
        {
            IDocument document = blocks.Count > 0 ? blocks[0].Owner! : new HtmlParser().ParseDocument("");
            IElement lines = document.CreateElement("div");
            foreach (IElement block in blocks)
            {
                if (block.LocalName is "ul" or "ol")
                {
                    foreach (IElement item in block.Children.Where(c => c.LocalName == "li").ToList())
                    {
                        IElement paragraph = document.CreateElement("p");
                        foreach (INode node in item.ChildNodes.ToList())
                        {
                            paragraph.AppendChild(node);
                        }
                        lines.AppendChild(paragraph);
                    }
                }
                else
                {
                    lines.AppendChild(block);
                }
            }
            return lines;
        }

        private static void Gap(XElement flow, XElement processing, JsonElement gap, string ident, string rating)
        {
            double points = gap.GetProperty("points").GetDouble();
            var response = new XElement("response_str", new XAttribute("ident", ident), new XAttribute("rcardinality", "Single"));
            flow.Add(response);
            if (gap.TryGetProperty("choices", out JsonElement choices))
            {
                var right = gap.GetProperty("right").EnumerateArray().Select(r => r.GetInt32()).ToHashSet();
                var render = new XElement("render_choice", new XAttribute("shuffle", gap.GetProperty("ordered").GetBoolean() ? "No" : "Yes"));
                response.Add(render);
                int i = 0;
                foreach (JsonElement choice in choices.EnumerateArray())
                {
                    string text = choice.GetString()!;
                    render.Add(new XElement("response_label", new XAttribute("ident", i), Material(text, xhtml: false)));
                    processing.Add(Condition(new XElement("varequal", new XAttribute("respident", ident), text), right.Contains(i) ? points : 0));
                    i++;
                }
                return;
            }
            var answers = gap.GetProperty("answers").EnumerateArray().Select(a => a.GetString()!).ToList();
            // as ILIAS rates: without "cs" small letters count too
            if (rating != "cs")
            {
                answers.AddRange(answers.Select(a => a.ToLowerInvariant()).ToList());
            }
            int chars = gap.TryGetProperty("chars", out JsonElement given) ? given.GetInt32() : Math.Max(10, answers.Max(a => a.Length) + 5);
            response.Add(new XElement("render_fib", new XAttribute("fibtype", "String"), new XAttribute("prompt", "Box"), new XAttribute("maxchars", chars)));
            foreach (string answer in answers.Distinct(StringComparer.Ordinal))
            {
                processing.Add(Condition(new XElement("varequal", new XAttribute("respident", ident), answer), points));
            }
        }

        // a single choice question: the text, the answers (ILIAS mixes them), the right one with all the points
        private void SingleChoice(XElement metadata, XElement flow, XElement processing, List<IElement> content, IElement choice, double points)
        {
            Field(metadata, "QUESTIONTYPE", "SINGLE CHOICE QUESTION");
            flow.Add(Material(Html(content)));
            var render = new XElement("render_choice", new XAttribute("shuffle", "Yes"));
            flow.Add(new XElement("response_lid", new XAttribute("ident", "MCSR"), new XAttribute("rcardinality", "Single"), render));
            int i = 0;
            foreach (IElement answer in choice.QuerySelectorAll("li"))
            {
                bool right = answer.QuerySelector(".choice-right") != null;
                foreach (IElement mark in answer.QuerySelectorAll(".choice-right, .choice-wrong, .choice-box").ToList())
                {
                    mark.Remove();
                }
                render.Add(new XElement("response_label", new XAttribute("ident", i), Material("<p>" + Html([answer], inner: true).Trim() + "</p>")));
                processing.Add(Condition(new XElement("varequal", new XAttribute("respident", "MCSR"), i), right ? points : 0));
                processing.Add(Condition(new XElement("not", new XElement("varequal", new XAttribute("respident", "MCSR"), i)), 0));
                i++;
            }
        }

        // a matching question: the definitions (left, each with its points) and the terms (right, also those that fit
        // nothing); the points shared by the pairs; a term of two definitions (or a definition with two terms): n:n, where
        // ILIAS needs minus points for the wrong pairs
        private void Matching(XElement item, XElement metadata, XElement flow, XElement processing, List<IElement> content, IElement match, int points, string title)
        {
            item.Add(new XAttribute("maxattempts", "0"));
            var rows = match.QuerySelectorAll("tbody tr")
                .Select(row => row.QuerySelectorAll("td").ToList())
                .Where(cells => cells.Count == 2)
                .Select(cells => (Left: Html([cells[0]], inner: true).Trim(), Right: Html([cells[1]], inner: true).Trim()))
                .ToList();
            var definitions = rows.Where(r => r.Left.Length > 0).Select(r => r.Left).Distinct().ToList();
            var terms = rows.Select(r => r.Right).Distinct().ToList();
            var pairs = rows.Where(r => r.Left.Length > 0).Distinct().ToList();
            bool many = pairs.Count != definitions.Count || pairs.Select(p => p.Right).Distinct().Count() != pairs.Count;
            if (pairs.Count == 0)
            {
                Problems.Add($"\"{title}\" has no pairs.");
                return;
            }
            // each pair its share, the rest one by one from the first
            int share = Math.Max(1, points / pairs.Count);
            var scores = pairs.ToDictionary(p => p, _ => share);
            for (int k = 0, sum = share * pairs.Count; sum < points; k++, sum++)
            {
                scores[pairs[k % pairs.Count]]++;
            }
            int Id(string text, bool definition) => 1000 + (definition ? definitions.IndexOf(text) : definitions.Count + terms.IndexOf(text));
            Field(metadata, "QUESTIONTYPE", "MATCHING QUESTION");
            Field(metadata, "additional_cont_edit_mode", "");
            Field(metadata, "externalId", (string)item.Attribute("ident")!);
            Field(metadata, "shuffle", match.GetAttribute("data-shuffle") == "all" ? "1" : "2");
            Field(metadata, "thumb_geometry", "80");
            Field(metadata, "matching_mode", many ? "n:n" : "1:1");
            flow.Add(Material(Html(content)));
            var render = new XElement("render_choice", new XAttribute("shuffle", "Yes"));
            flow.Add(new XElement("response_grp", new XAttribute("ident", "MQ"), new XAttribute("rcardinality", "Multiple"), render));
            string group = string.Join(",", terms.Select(t => Id(t, false)));
            foreach (string definition in definitions)
            {
                var own = pairs.Where(p => p.Left == definition).ToList();
                string worth = own.Count == 1 ? $" <em>({scores[own[0]]} P)</em>" : "";
                render.Add(new XElement("response_label", new XAttribute("ident", Id(definition, true)), new XAttribute("match_max", "1"),
                    new XAttribute("match_group", group), Material("<p>" + definition + worth + "</p>")));
            }
            foreach (string term in terms)
            {
                render.Add(new XElement("response_label", new XAttribute("ident", Id(term, false)), Material("<p>" + term + "</p>")));
            }
            foreach (string term in terms)
            {
                foreach (string definition in definitions)
                {
                    int? score = scores.TryGetValue((definition, term), out int own) ? own : many ? -1 : null;
                    if (score is int value)
                    {
                        processing.Add(Condition(new XElement("varsubset", new XAttribute("respident", "MQ"), $"{Id(term, false)},{Id(definition, true)}"), value));
                    }
                }
            }
        }

        // ------------------------------------------------------------------ the text as ILIAS shows it

        // the elements as HTML for ILIAS: formulas and figures as pictures (PNG, in the text), code in colour, without what
        // only the preview needs; letters beyond Latin as character references (as the earlier export did: ILIAS keeps them)
        private string Html(List<IElement> elements, bool inner = false)
        {
            var html = new StringBuilder();
            foreach (IElement element in elements)
            {
                // in a wrapper: what replaces the element itself (a figure by its pictures) stays in it
                IElement wrapper = element.Owner!.CreateElement("div");
                element.Parent?.ReplaceChild(wrapper, element);
                wrapper.AppendChild(element);
                Prepare(wrapper);
                html.Append(inner ? wrapper.FirstElementChild?.InnerHtml ?? "" : wrapper.InnerHtml).Append('\n');
            }
            // XHTML, as ILIAS wants it
            string text = VoidElement().Replace(CodeHighlighting.InlineStyles(html.ToString()), "<$1$2 />");
            var result = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsLetter(c) && c >= 128 && "äöüÄÖÜß".IndexOf(c) < 0)
                {
                    result.Append("&#x").Append(((int)c).ToString("x", CultureInfo.InvariantCulture)).Append(';');
                }
                else
                {
                    result.Append(c);
                }
            }
            return result.ToString();
        }

        [GeneratedRegex(@"<(br|img|hr)\b([^>]*?)\s*/?>")]
        private static partial Regex VoidElement();

        private void Prepare(IElement root)
        {
            IDocument document = root.Owner!;
            foreach (IElement formula in root.QuerySelectorAll("svg.formula").ToList())
            {
                string source = formula.GetAttribute("aria-label") ?? "";
                IElement image = document.CreateElement("img");
                try
                {
                    FormulaImage picture = FormulaImage.Render(source, print.TextSize);
                    image.SetAttribute("src", "data:image/png;base64," + Convert.ToBase64String(picture.Png(300)));
                    image.SetAttribute("style", $"height: {Em(picture.Height)}; vertical-align: {Em(-picture.Depth)}");
                }
                catch (FormatException)
                {
                }
                image.SetAttribute("alt", source);
                formula.Replace(image);
            }
            // figures: their pictures as in print; a row of pictures of one height
            foreach (IElement figure in new[] { root }.Concat(root.QuerySelectorAll("figure.geo-figure")).Where(e => e.ClassList.Contains("geo-figure")).ToList())
            {
                IElement row = document.CreateElement("p");
                row.SetAttribute("style", "text-align: center");
                bool pictures = figure.ClassList.Contains("geo-pictures");
                var ratios = new List<double>();
                var images = new List<IElement>();
                foreach (IElement item in figure.QuerySelectorAll(".geo-item"))
                {
                    if (item.QuerySelector("img") is { } file && item.ClassList.Contains("geo-image"))
                    {
                        images.Add(file);
                        ratios.Add(HtmlBookWriter.RatioOf(item));
                    }
                    else if (item.GetAttribute("data-figure") is { } key && figures.TryGetValue(key, out PageFigure? shown))
                    {
                        foreach (FigureImage still in BookPictures.Of(shown, FigureAnimation.Parse(shown.AnimationJson), print.Style))
                        {
                            IElement image = document.CreateElement("img");
                            image.SetAttribute("src", "data:image/png;base64," + Convert.ToBase64String(FigurePng.Render(still)));
                            image.SetAttribute("alt", "Abbildung");
                            image.SetAttribute("width", Math.Round(still.Width * 96 / 72).ToString(CultureInfo.InvariantCulture));
                            images.Add(image);
                            ratios.Add(still.Width / still.Height);
                        }
                    }
                }
                if (pictures)
                {
                    // a width of about 600 pixels for the text
                    double height = PictureRow.Height(ratios, figure.GetAttribute("data-height"), figure.QuerySelectorAll(".geo-item").Select(i => i.GetAttribute("data-width")).ToList())
                        ?? Math.Min(PictureRow.MaxHeight, 450 / Math.Max(0.1, ratios.Sum()));
                    for (int i = 0; i < images.Count; i++)
                    {
                        images[i].RemoveAttribute("width");
                        images[i].SetAttribute("style", $"height: {Math.Round(height * 96 / 72)}px; width: auto");
                    }
                }
                foreach (IElement image in images)
                {
                    image.RemoveAttribute("data-source");
                    row.AppendChild(image);
                    row.AppendChild(document.CreateTextNode(" "));
                }
                var replacement = new List<INode> { row };
                if (figure.QuerySelector("figcaption") is { } caption)
                {
                    IElement text = document.CreateElement("p");
                    text.SetAttribute("style", "text-align: center; font-size: 0.9em");
                    text.InnerHtml = caption.InnerHtml;
                    replacement.Add(text);
                }
                figure.Replace([.. replacement]);
            }
            foreach (IElement element in new[] { root }.Concat(root.QuerySelectorAll("*")).ToList())
            {
                foreach (string name in element.Attributes.Select(a => a.Name).Where(n => n.StartsWith("data-", StringComparison.Ordinal)).ToList())
                {
                    element.RemoveAttribute(name);
                }
            }
        }

        private string Em(double points) => (Math.Round(points / print.TextSize, 3) + 0.0).ToString("0.###", CultureInfo.InvariantCulture) + "em";
    }
}
