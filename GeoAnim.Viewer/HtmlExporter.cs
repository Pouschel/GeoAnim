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

    /// <summary>The page for <paramref name="animation"/>.</summary>
    public static string Export(AnimationData animation)
    {
        string html = ViewerFiles.ReadText("player.html");
        html = TitlePattern().Replace(html, "<title>" + WebUtility.HtmlEncode(animation.Title ?? "GeoAnim") + "</title>", 1);
        html = StylesheetPattern().Replace(html, m => "<style>\n" + InlineCss(m.Groups[1].Value) + "</style>");
        html = ScriptPattern().Replace(html, m =>
            // the JSON escapes <, > and &, so it cannot end the script element
            "<script>window.GEO_ANIMATION = " + animation.ToJson() + ";</script>\n"
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
