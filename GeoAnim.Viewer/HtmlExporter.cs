using System.Net;
using System.Text.RegularExpressions;

namespace GeoAnim.Viewer;

/// <summary>
/// Makes one self-contained HTML file from an animation: the player page with its stylesheets, its script, the font
/// (as data: URL) and the animation inside. It plays when opened directly from disk.
/// </summary>
public static partial class HtmlExporter
{
    [GeneratedRegex("""<link rel="stylesheet" href="([^"]+)">""")]
    private static partial Regex StylesheetPattern();

    [GeneratedRegex("""<script src="([^"]+)"></script>""")]
    private static partial Regex ScriptPattern();

    [GeneratedRegex("""url\(([^)]+)\)""")]
    private static partial Regex CssUrlPattern();

    [GeneratedRegex("<title>[^<]*</title>")]
    private static partial Regex TitlePattern();

    // Markdown marks and formula dollars, for the page title: [text](address) → text, <address> → address
    [GeneratedRegex(@"\[([^\]]*)\]\([^)\s]*\)|<([^>\s]+)>|\\([*_`$])|\*\*|[*`$]|(?<![\p{L}\p{N}])_|_(?![\p{L}\p{N}])")]
    private static partial Regex MarkdownPattern();

    /// <summary>A title or description as plain text (for the page's title).</summary>
    internal static string PlainText(string text) =>
        MarkdownPattern().Replace(text, m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);

    /// <summary>The page for <paramref name="animation"/>.</summary>
    public static string Export(AnimationData animation)
    {
        string html = ViewerFiles.ReadText("player.html");
        html = TitlePattern().Replace(html, "<title>" + WebUtility.HtmlEncode(animation.Title == null ? "GeoAnim" : PlainText(animation.Title)) + "</title>", 1);
        html = StylesheetPattern().Replace(html, m => "<style>\n" + InlineCss(m.Groups[1].Value) + "</style>");
        html = ScriptPattern().Replace(html, m =>
            // the JSON escapes <, > and &, so it cannot end the script element
            "<script>window.GEO_ANIMATION = " + animation.ToJson() + ";</script>\n"
            + "    <script>\n" + EscapeScript(ViewerFiles.ReadText(m.Groups[1].Value)) + "</script>");
        return html;
    }

    /// <summary>
    /// The player page without an animation, for an editor's preview: it gets its animations as messages
    /// ({ type: "animation" | "error" | "step", … }, see player.js) instead of fetching them from "geoanim serve".
    /// </summary>
    public static string ExportPlayer()
    {
        string html = ViewerFiles.ReadText("player.html");
        html = StylesheetPattern().Replace(html, m => "<style>\n" + InlineCss(m.Groups[1].Value) + "</style>");
        html = ScriptPattern().Replace(html, m =>
            "<script>window.GEO_EMBEDDED = true;</script>\n"
            + "    <script>\n" + EscapeScript(ViewerFiles.ReadText(m.Groups[1].Value)) + "</script>");
        return html;
    }

    private static string InlineCss(string path)
    {
        string folder = path.Contains('/') ? path.Substring(0, path.LastIndexOf('/') + 1) : string.Empty;
        return CssUrlPattern().Replace(ViewerFiles.ReadText(path), m =>
        {
            string url = m.Groups[1].Value.Trim('\'', '"', ' ');
            if (url.StartsWith("data:", StringComparison.Ordinal))
            {
                return m.Value;
            }
            byte[] content = ViewerFiles.Read(folder + url) ?? throw new FileNotFoundException("Viewer file not found: " + folder + url);
            string mime = ViewerFiles.ContentType(url).Split(';')[0];
            return "url(data:" + mime + ";base64," + Convert.ToBase64String(content) + ")";
        });
    }

    private static string EscapeScript(string code) => code.Replace("</", "<\\/", StringComparison.Ordinal);
}
