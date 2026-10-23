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
    /// <summary>The index for the pages in book order (paths relative to the index, with "/").</summary>
    public static string Create(string title, IEnumerable<(string Path, AnimationData Animation)> pages)
    {
        var html = new StringBuilder();
        html.Append("""
            <!doctype html>
            <html lang="de">
              <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
            """);
        html.Append("    <title>").Append(Encode(title)).Append("</title>\n");
        html.Append("""
                <style>
                  body { max-width: 760px; margin: 0 auto; padding: 24px 16px 48px; background: #eceef2; color: #222;
                         font-family: 'STIX Two Text', Georgia, 'Times New Roman', serif; line-height: 1.4; }
                  h1 { font-size: 30px; margin: 0 0 18px; }
                  h2 { font-size: 21px; margin: 30px 0 8px; color: #4a5160; }
                  ol { margin: 0; padding: 0; list-style: none; }
                  li { margin: 0 0 8px; padding: 10px 14px; border-radius: 6px; background: #fff; box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1); }
                  a { color: #1565c0; font-size: 19px; text-decoration: none; }
                  a:hover { text-decoration: underline; }
                  p { margin: 4px 0 0; color: #5c6370; font-size: 15px; }
                </style>
              </head>
              <body>
            """);
        html.Append("    <h1>").Append(Encode(title)).Append("</h1>\n");
        foreach (var folder in pages
            .Where(page => !page.Animation.IsEmpty)
            .GroupBy(page => page.Path.Contains('/') ? page.Path.Substring(0, page.Path.LastIndexOf('/')) : string.Empty))
        {
            if (folder.Key.Length > 0)
            {
                html.Append("    <h2>").Append(Encode(folder.Key.Replace("/", " / "))).Append("</h2>\n");
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
        }
        html.Append("  </body>\n</html>\n");
        return html.ToString();
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

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
