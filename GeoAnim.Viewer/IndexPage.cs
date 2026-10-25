using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace GeoAnim.Viewer;

/// <summary>A term of the keyword index: the page (and step) that explains it, the pages that mention it.</summary>
public sealed record KeywordEntry(string Term, PageLink Page, IReadOnlyList<PageLink> UsedOn);

/// <summary>
/// The overview of an exported book ("geoanim exportdir"): links to the pages in the order given (the pages of the folder
/// itself first, then a section per subfolder), each with its title and its short description.
/// </summary>
public static partial class IndexPage
{
    /// <summary>
    /// The index for the pages in book order (paths relative to the index, with "/"); its title, description, way back
    /// home and footer from <paramref name="settings"/> (else the folder's name), its icon and address from <paramref name="head"/>;
    /// <paramref name="keywordIndex"/>: the address of the keyword index, linked below the description.
    /// </summary>
    public static string Create(string folderName, IEnumerable<(string Path, AnimationData Animation)> pages, BookSettings? settings = null, PageHead? head = null,
        IReadOnlyDictionary<string, string>? chapters = null, string? keywordIndex = null)
    {
        settings ??= BookSettings.None;
        string title = settings.Title ?? folderName;
        var html = new StringBuilder();
        Begin(html, title, settings.Description, settings, head, "");
        if (settings.Home != null)
        {
            html.Append("    <a class=\"home\" href=\"").Append(Encode(settings.Home.Href)).Append("\">← ").Append(Encode(settings.Home.Title)).Append("</a>\n");
        }
        html.Append("    <h1>").Append(Encode(title)).Append("</h1>\n");
        if (settings.Description != null)
        {
            html.Append("    <p class=\"intro\">").Append(Inline(settings.Description)).Append("</p>\n");
        }
        if (keywordIndex != null)
        {
            html.Append("    <a class=\"register\" href=\"").Append(Encode(keywordIndex)).Append("\">Stichwortverzeichnis</a>\n");
        }
        // a chapter (folder) opens with a click, so that a long book stays clear, and holds its pages and its own chapters
        // in book order; the pages of the book's folder itself stand open
        List<(string Path, AnimationData Animation)> shown = pages.Where(page => !page.Animation.IsEmpty).ToList();
        string FolderOf(string path) => path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
        void Write(string folder, int depth)
        {
            string indent = new(' ', 4 + 2 * depth);
            bool inList = false;
            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach ((string path, AnimationData animation) in shown)
            {
                string at = FolderOf(path);
                if (at == folder)
                {
                    if (!inList)
                    {
                        html.Append(indent).Append("<ol>\n");
                        inList = true;
                    }
                    string link = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
                    html.Append(indent).Append("  <li><a href=\"").Append(link).Append("\">").Append(Encode(TitleOf(path, animation))).Append("</a>");
                    string description = HtmlExporter.DescriptionOf(animation);
                    if (description.Length > 0)
                    {
                        html.Append("<p>").Append(Encode(description)).Append("</p>");
                    }
                    html.Append("</li>\n");
                    continue;
                }
                // a page further down: the chapter directly below this folder that holds it, once, where its first page is
                if (!(folder.Length == 0 || at.StartsWith(folder + "/", StringComparison.Ordinal)))
                {
                    continue;
                }
                string rest = folder.Length == 0 ? at : at[(folder.Length + 1)..];
                string chapter = (folder.Length == 0 ? "" : folder + "/") + rest.Split('/')[0];
                if (!done.Add(chapter))
                {
                    continue;
                }
                if (inList)
                {
                    html.Append(indent).Append("</ol>\n");
                    inList = false;
                }
                int count = shown.Count(page => FolderOf(page.Path) == chapter || page.Path.StartsWith(chapter + "/", StringComparison.Ordinal));
                string heading = chapters != null && chapters.TryGetValue(chapter, out string? name) ? name : chapter[(chapter.LastIndexOf('/') + 1)..];
                string tag = depth == 0 ? "h2" : "h3";
                html.Append(indent).Append("<details data-chapter=\"").Append(Encode(chapter)).Append("\">\n")
                    .Append(indent).Append("  <summary><").Append(tag).Append('>').Append(Encode(heading)).Append("</").Append(tag).Append('>')
                    .Append("<span class=\"count\">").Append(count).Append(count == 1 ? " Seite" : " Seiten").Append("</span></summary>\n");
                Write(chapter, depth + 1);
                html.Append(indent).Append("</details>\n");
            }
            if (inList)
            {
                html.Append(indent).Append("</ol>\n");
            }
        }
        Write("", 0);
        if (settings.Footer != null)
        {
            html.Append("    <footer>").Append(Inline(settings.Footer)).Append("</footer>\n");
        }
        // the chapters opened last time stay open (in this browser)
        html.Append("""
                <script>
                  (() => {
                    const key = 'geoanim-open:' + location.pathname
                    const chapters = [...document.querySelectorAll('details[data-chapter]')]
                    let open = []
                    try { open = JSON.parse(localStorage.getItem(key) || '[]') } catch (error) {}
                    for (const chapter of chapters) {
                      chapter.open = open.includes(chapter.dataset.chapter)
                      chapter.addEventListener('toggle', () => {
                        try { localStorage.setItem(key, JSON.stringify(chapters.filter(c => c.open).map(c => c.dataset.chapter))) } catch (error) {}
                      })
                    }
                  })()
                </script>

            """);
        html.Append("  </body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>
    /// The keyword index of an exported book: the terms in alphabetical order under their initials, each with the page
    /// (and step) that explains it and the pages that mention it (addresses relative to the index).
    /// </summary>
    public static string CreateKeywordIndex(string folderName, IEnumerable<KeywordEntry> entries, string overview, BookSettings? settings = null, PageHead? head = null)
    {
        settings ??= BookSettings.None;
        string book = settings.Title ?? folderName;
        const string title = "Stichwortverzeichnis";
        var html = new StringBuilder();
        Begin(html, title + " – " + book, "Alle Begriffe aus „" + book + "“ mit der Seite, die sie erklärt.", settings, head, """
                  .letters { display: flex; flex-wrap: wrap; gap: 4px 10px; margin: 0 0 8px; }
                  .letters a { font-size: 17px; }
                  .entries li { display: flex; flex-wrap: wrap; align-items: baseline; gap: 2px 12px; }
                  .term { font-size: 19px; font-weight: 600; }
                  .page { font-size: 16px; }
                  .used { flex-basis: 100%; margin: 2px 0 0; color: #5c6370; font-size: 15px; }
                  .used a { font-size: 15px; }
            """);
        html.Append("    <a class=\"home\" href=\"").Append(Encode(overview)).Append("\">← ").Append(Encode(book)).Append("</a>\n");
        html.Append("    <h1>").Append(title).Append("</h1>\n");
        CompareInfo compare = CultureInfo.GetCultureInfo(settings.Language ?? "de").CompareInfo;
        List<KeywordEntry> sorted = entries.OrderBy(e => e.Term, StringComparer.Create(CultureInfo.GetCultureInfo(settings.Language ?? "de"), CompareOptions.IgnoreCase))
            .ThenBy(e => e.Page.Title, StringComparer.Ordinal).ToList();
        // by initial: umlauts and accents with their letter (Ä with A), digits and the rest under #
        static string Initial(string term)
        {
            char first = term.Normalize(NormalizationForm.FormD)[0];
            return char.IsLetter(first) ? char.ToUpperInvariant(first).ToString() : "#";
        }
        List<IGrouping<string, KeywordEntry>> groups = sorted.GroupBy(e => Initial(e.Term)).ToList();
        html.Append("    <nav class=\"letters\">");
        foreach (IGrouping<string, KeywordEntry> group in groups)
        {
            html.Append("<a href=\"#").Append(Encode(Anchor(group.Key))).Append("\">").Append(Encode(group.Key)).Append("</a>");
        }
        html.Append("</nav>\n");
        foreach (IGrouping<string, KeywordEntry> group in groups)
        {
            html.Append("    <h2 id=\"").Append(Encode(Anchor(group.Key))).Append("\">").Append(Encode(group.Key)).Append("</h2>\n    <ol class=\"entries\">\n");
            foreach (KeywordEntry entry in group)
            {
                html.Append("      <li><a class=\"term\" href=\"").Append(Encode(entry.Page.Href)).Append("\">").Append(Encode(entry.Term)).Append("</a>")
                    .Append("<span class=\"page\">").Append(Encode(entry.Page.Title)).Append("</span>");
                if (entry.UsedOn.Count > 0)
                {
                    html.Append("<p class=\"used\">erwähnt in: ")
                        .Append(string.Join(" · ", entry.UsedOn.Select(u => "<a href=\"" + Encode(u.Href) + "\">" + Encode(u.Title) + "</a>")))
                        .Append("</p>");
                }
                html.Append("</li>\n");
            }
            html.Append("    </ol>\n");
        }
        if (settings.Footer != null)
        {
            html.Append("    <footer>").Append(Inline(settings.Footer)).Append("</footer>\n");
        }
        html.Append("  </body>\n</html>\n");
        return html.ToString();
    }

    /// <summary>
    /// The contact sheet of an exported book ("geoanim exportdir -sheet"), for the author: for every page in book order
    /// the starting picture and the end of every step as stills of the real page ("page.html?still#3"), each a link to the
    /// page at that step; the width of the pictures can be switched (450, 720, 1080 pixels), since texts scale with it.
    /// </summary>
    public static string CreateSheet(string folderName, IEnumerable<(string Path, AnimationData Animation)> pages, BookSettings? settings = null)
    {
        settings ??= BookSettings.None;
        string book = settings.Title ?? folderName;
        var html = new StringBuilder();
        Begin(html, "Kontaktbogen – " + book, null, settings, null, """
                  body { max-width: none; }
                  .sizes { position: sticky; top: 0; z-index: 1; display: flex; gap: 8px; align-items: center; margin: 0 0 12px; padding: 8px 0;
                           background: #eceef2; font-size: 16px; }
                  .sizes button { padding: 4px 12px; border: 1px solid #c5cad3; border-radius: 6px; background: #fff; font: inherit; cursor: pointer; }
                  .sizes button.on { border-color: #1565c0; color: #1565c0; }
                  section { margin: 0 0 28px; }
                  h2 { margin: 0 0 8px; color: #222; }
                  h2 small { color: #6b7280; font-size: 14px; font-weight: normal; }
                  .stills { display: flex; flex-wrap: wrap; gap: 14px; }
                  figure { margin: 0; }
                  .frame { position: relative; display: block; width: var(--w); background: #fff; box-shadow: 0 1px 3px rgba(0, 0, 0, 0.15); }
                  .frame iframe { display: block; width: 100%; height: 100%; border: 0; pointer-events: none; }
                  figcaption { width: var(--w); margin: 4px 0 0; color: #4a5160; font-size: 14px; }
                  figcaption b { color: #6b7280; font-weight: normal; margin-right: 0.4em; }
            """);
        html.Append("    <h1>Kontaktbogen</h1>\n");
        html.Append("    <div class=\"sizes\">Bildbreite: <button data-w=\"450\">450 px</button><button data-w=\"720\">720 px</button><button data-w=\"1080\">1080 px</button></div>\n");
        foreach ((string path, AnimationData animation) in pages.Where(p => !p.Animation.IsEmpty))
        {
            string link = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
            string ratio = (animation.View.Width / animation.View.Height).ToString("0.####", CultureInfo.InvariantCulture);
            html.Append("    <section>\n      <h2>").Append(Encode(TitleOf(path, animation))).Append(" <small>").Append(Encode(path)).Append("</small></h2>\n")
                .Append("      <div class=\"stills\">\n");
            var stills = new List<(int Step, string Caption)> { (0, "Start") };
            stills.AddRange(animation.Steps.Select((step, i) => (i + 1, HtmlExporter.PlainText(step.Caption))));
            foreach ((int step, string caption) in stills)
            {
                html.Append("        <figure><a class=\"frame\" style=\"aspect-ratio: ").Append(ratio).Append("\" href=\"").Append(link).Append('#').Append(step)
                    .Append("\"><iframe loading=\"lazy\" tabindex=\"-1\" title=\"").Append(Encode(caption)).Append("\" src=\"").Append(link).Append("?still#").Append(step)
                    .Append("\"></iframe></a><figcaption>").Append(step > 0 ? "<b>" + step + "</b>" : "").Append(Encode(caption)).Append("</figcaption></figure>\n");
            }
            html.Append("      </div>\n    </section>\n");
        }
        // the width of the pictures: remembered in this browser
        html.Append("""
                <script>
                  (() => {
                    const key = 'geoanim-sheet-width'
                    const buttons = [...document.querySelectorAll('.sizes button')]
                    const set = width => {
                      document.body.style.setProperty('--w', width + 'px')
                      buttons.forEach(b => b.classList.toggle('on', b.dataset.w === String(width)))
                      try { localStorage.setItem(key, width) } catch (error) {}
                    }
                    let width = 450
                    try { width = Number(localStorage.getItem(key)) || 450 } catch (error) {}
                    set(width)
                    buttons.forEach(b => b.addEventListener('click', () => set(Number(b.dataset.w))))
                  })()
                </script>

            """);
        html.Append("  </body>\n</html>\n");
        return html.ToString();
    }

    private static string Anchor(string initial) => initial == "#" ? "andere" : initial;

    // the start of a page of the book (overview, keyword index) up to <body>: head, its tags and the common styles
    private static void Begin(StringBuilder html, string title, string? description, BookSettings settings, PageHead? head, string css)
    {
        html.Append("<!doctype html>\n<html lang=\"").Append(Encode(settings.Language ?? "de")).Append("\">\n");
        html.Append("""
              <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">

            """);
        html.Append("    <title>").Append(Encode(title)).Append("</title>\n");
        if (description != null)
        {
            html.Append("    <meta name=\"description\" content=\"").Append(Encode(description)).Append("\">\n");
        }
        if (head != null)
        {
            html.Append(HtmlExporter.HeadTags(head, title, description ?? title).TrimStart('\n')).Append('\n');
        }
        html.Append("    <style>\n").Append(Style).Append(css).Append("    </style>\n  </head>\n  <body>\n");
    }

    private const string Style = """
                  body { max-width: 760px; margin: 0 auto; padding: 24px 16px 48px; background: #eceef2; color: #222;
                         font-family: 'STIX Two Text', Georgia, 'Times New Roman', serif; line-height: 1.4; }
                  h1 { font-size: 30px; margin: 0 0 18px; }
                  h2 { font-size: 21px; margin: 30px 0 8px; color: #4a5160; }
                  details { margin: 0 0 10px; border-radius: 8px; background: #fff; box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1); }
                  summary { display: flex; align-items: baseline; gap: 12px; padding: 14px 18px; cursor: pointer; list-style: none; user-select: none; }
                  summary::-webkit-details-marker { display: none; }
                  summary::before { content: '▸'; color: #6b7280; transition: transform 0.15s; }
                  details[open] > summary::before { transform: rotate(90deg); }
                  summary:hover h2 { color: #1565c0; }
                  summary h2 { flex: 1; margin: 0; color: #222; }
                  .count { color: #6b7280; font-size: 15px; white-space: nowrap; }
                  details ol { padding: 0 14px 14px; }
                  details li { background: #f6f7f9; box-shadow: none; }
                  details details { margin: 0 14px 10px; background: #f6f7f9; box-shadow: none; }
                  details details summary { padding: 10px 14px; }
                  summary h3 { flex: 1; margin: 0; color: #222; font-size: 18px; }
                  summary:hover h3 { color: #1565c0; }
                  details details li { background: #fff; }
                  ol { margin: 0; padding: 0; list-style: none; }
                  li { margin: 0 0 8px; padding: 10px 14px; border-radius: 6px; background: #fff; box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1); }
                  a { color: #1565c0; font-size: 19px; text-decoration: none; }
                  a:hover { text-decoration: underline; }
                  p { margin: 4px 0 0; color: #5c6370; font-size: 15px; }
                  .register { display: inline-block; margin: 0 0 18px; font-size: 17px; }
                  .home { display: inline-block; margin: 0 0 10px; font-size: 16px; }
                  .intro { margin: 0 0 18px; color: #333; font-size: 18px; }
                  footer { margin: 36px 0 0; color: #6b7280; font-size: 14px; text-align: center; }
                  footer a { font-size: 14px; }
            """;

    // only what HTML needs (umlauts and © stay as they are: the page is UTF-8)
    private static string Encode(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    // a line with Markdown links ([text](address), <address>) and **bold** as HTML; everything else as text
    private static string Inline(string text)
    {
        string html = Encode(text);
        html = Regex.Replace(html, @"\[([^\]]*)\]\(([^)\s]+)\)", m => $"<a href=\"{m.Groups[2].Value}\">{m.Groups[1].Value}</a>");
        html = Regex.Replace(html, @"&lt;((?:https?://|mailto:)[^\s&]+)&gt;", m => $"<a href=\"{m.Groups[1].Value}\">{m.Groups[1].Value}</a>");
        return Regex.Replace(html, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
    }

    /// <summary>The name of a page in the overview and the navigation: its title as plain text, else its file name.</summary>
    public static string TitleOf(string path, AnimationData animation) =>
        animation.Title == null ? Path.GetFileNameWithoutExtension(path) : HtmlExporter.PlainText(animation.Title);

    /// <summary>Text with numbers compared by their value: "Prop2" before "Prop10".</summary>
    public sealed partial class NaturalOrder : IComparer<string>
    {
        public static NaturalOrder Instance { get; } = new();

        [GeneratedRegex(@"\d+|\D+")]
        private static partial Regex Parts();

        public int Compare(string? x, string? y)
        {
            string[] a = Parts().Matches(x ?? "").Select(m => m.Value).ToArray();
            string[] b = Parts().Matches(y ?? "").Select(m => m.Value).ToArray();
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                bool numbers = char.IsDigit(a[i][0]) && char.IsDigit(b[i][0]);
                int result = numbers
                    ? decimal.Parse(a[i]).CompareTo(decimal.Parse(b[i]))
                    : string.Compare(a[i], b[i], StringComparison.OrdinalIgnoreCase);
                if (result != 0)
                {
                    return result;
                }
            }
            return a.Length.CompareTo(b.Length);
        }
    }
}
