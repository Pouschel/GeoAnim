using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace GeoAnim.Viewer;

/// <summary>
/// The overview of an exported book ("geoanim exportdir"): links to the pages in the order given (the pages of the folder
/// itself first, then a section per subfolder), each with its title and its short description.
/// </summary>
public static partial class IndexPage
{
    /// <summary>
    /// The index for the pages in book order (paths relative to the index, with "/"); its title, description, way back
    /// home and footer from <paramref name="settings"/> (else the folder's name), its icon and address from <paramref name="head"/>.
    /// </summary>
    public static string Create(string folderName, IEnumerable<(string Path, AnimationData Animation)> pages, BookSettings? settings = null, PageHead? head = null)
    {
        settings ??= BookSettings.None;
        string title = settings.Title ?? folderName;
        var html = new StringBuilder();
        html.Append("<!doctype html>\n<html lang=\"").Append(Encode(settings.Language ?? "de")).Append("\">\n");
        html.Append("""
              <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">

            """);
        html.Append("    <title>").Append(Encode(title)).Append("</title>\n");
        if (settings.Description != null)
        {
            html.Append("    <meta name=\"description\" content=\"").Append(Encode(settings.Description)).Append("\">\n");
        }
        if (head != null)
        {
            html.Append(HtmlExporter.HeadTags(head, title, settings.Description ?? title).TrimStart('\n')).Append('\n');
        }
        html.Append("""
                <style>
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
                  ol { margin: 0; padding: 0; list-style: none; }
                  li { margin: 0 0 8px; padding: 10px 14px; border-radius: 6px; background: #fff; box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1); }
                  a { color: #1565c0; font-size: 19px; text-decoration: none; }
                  a:hover { text-decoration: underline; }
                  p { margin: 4px 0 0; color: #5c6370; font-size: 15px; }
                  .home { display: inline-block; margin: 0 0 10px; font-size: 16px; }
                  .intro { margin: 0 0 18px; color: #333; font-size: 18px; }
                  footer { margin: 36px 0 0; color: #6b7280; font-size: 14px; text-align: center; }
                  footer a { font-size: 14px; }
                </style>
              </head>
              <body>
            """);
        if (settings.Home != null)
        {
            html.Append("    <a class=\"home\" href=\"").Append(Encode(settings.Home.Href)).Append("\">← ").Append(Encode(settings.Home.Title)).Append("</a>\n");
        }
        html.Append("    <h1>").Append(Encode(title)).Append("</h1>\n");
        if (settings.Description != null)
        {
            html.Append("    <p class=\"intro\">").Append(Inline(settings.Description)).Append("</p>\n");
        }
        foreach (var folder in pages
            .Where(page => !page.Animation.IsEmpty)
            .GroupBy(page => page.Path.Contains('/') ? page.Path.Substring(0, page.Path.LastIndexOf('/')) : string.Empty))
        {
            // a chapter (subfolder) opens with a click, so that a long book stays clear; the pages of the folder itself
            // stand open above the chapters
            bool chapter = folder.Key.Length > 0;
            if (chapter)
            {
                int count = folder.Count();
                html.Append("    <details data-chapter=\"").Append(Encode(folder.Key)).Append("\">\n")
                    .Append("      <summary><h2>").Append(Encode(folder.Key.Replace("/", " / "))).Append("</h2>")
                    .Append("<span class=\"count\">").Append(count).Append(count == 1 ? " Seite" : " Seiten").Append("</span></summary>\n");
            }
            html.Append("    <ol>\n");
            foreach ((string path, AnimationData animation) in folder)
            {
                string name = TitleOf(path, animation);
                string link = string.Join("/", path.Split('/').Select(Uri.EscapeDataString));
                html.Append("      <li><a href=\"").Append(link).Append("\">").Append(Encode(name)).Append("</a>");
                string description = HtmlExporter.DescriptionOf(animation);
                if (description.Length > 0)
                {
                    html.Append("<p>").Append(Encode(description)).Append("</p>");
                }
                html.Append("</li>\n");
            }
            html.Append("    </ol>\n");
            if (chapter)
            {
                html.Append("    </details>\n");
            }
        }
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
