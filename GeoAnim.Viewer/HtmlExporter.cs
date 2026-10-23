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

    /// <summary>The length a description is cut to (at a word).</summary>
    internal const int DescriptionLength = 250;

    /// <summary>
    /// The page description: the one from the script, else the step descriptions one after the other – as plain text,
    /// formulas readable ($bar(AB)$ → AB), cut at a word after about <see cref="DescriptionLength"/> characters.
    /// </summary>
    internal static string DescriptionOf(AnimationData animation)
    {
        string text = animation.Description ?? string.Join(" ", animation.Steps
            .Select(step => ReadableFormulas(step.Caption).Trim())
            .Where(caption => caption.Length > 0)
            .Select(caption => ".!?:…".Contains(caption[^1]) ? caption : caption + "."));
        text = WhiteSpace().Replace(PlainText(ReadableFormulas(text)), " ").Trim();
        if (text.Length <= DescriptionLength)
        {
            return text;
        }
        int cut = text.LastIndexOf(' ', DescriptionLength);
        return text.Substring(0, cut > DescriptionLength / 2 ? cut : DescriptionLength).TrimEnd(',', ';', ' ') + " …";
    }

    // AsciiMath in $…$ made readable for a description: bar(AB) → AB, Delta → Δ, ^@ → °
    private static string ReadableFormulas(string text) => FormulaPattern().Replace(text, m =>
    {
        string formula = m.Groups[1].Value;
        formula = DecorationPattern().Replace(formula, d => d.Groups[2].Value);
        foreach ((string name, string letter) in Greek)
        {
            formula = Regex.Replace(formula, $@"\b{name}\b ?", letter);
        }
        return formula.Replace("^@", "°").Replace("*", "·").Replace("!=", "≠").Replace("<=", "≤").Replace(">=", "≥");
    });

    private static readonly (string, string)[] Greek =
    [
        ("Delta", "Δ"), ("Gamma", "Γ"), ("Omega", "Ω"), ("alpha", "α"), ("beta", "β"), ("gamma", "γ"), ("delta", "δ"),
        ("epsilon", "ε"), ("lambda", "λ"), ("mu", "μ"), ("pi", "π"), ("phi", "φ"), ("sigma", "σ"), ("omega", "ω"),
        ("angle", "∠"), ("perp", "⊥"), ("cong", "≅"), ("sim", "∼"),
    ];

    [GeneratedRegex(@"(?<!\\)\$([^$]*)\$")]
    private static partial Regex FormulaPattern();

    [GeneratedRegex(@"\b(bar|vec|hat|overline|overset|ul|tilde|dot)\(([^()]*)\)")]
    private static partial Regex DecorationPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    /// <summary>A title or description as plain text (for the page's title).</summary>
    internal static string PlainText(string text) =>
        MarkdownPattern().Replace(text, m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)
            .Replace('\n', ' ');

    /// <summary>The page for <paramref name="animation"/>; in an exported book with the way to the other pages.</summary>
    public static string Export(AnimationData animation, PageNavigation? navigation = null)
    {
        string html = ViewerFiles.ReadText("player.html");
        html = TitlePattern().Replace(html, "<title>" + WebUtility.HtmlEncode(animation.Title == null ? "GeoAnim" : PlainText(animation.Title)) + "</title>"
            + "\n    <meta name=\"description\" content=\"" + WebUtility.HtmlEncode(DescriptionOf(animation)) + "\">", 1);
        html = StylesheetPattern().Replace(html, m => "<style>\n" + InlineCss(m.Groups[1].Value) + "</style>");
        html = ScriptPattern().Replace(html, m =>
            // the JSON escapes <, > and &, so it cannot end the script element
            "<script>window.GEO_ANIMATION = " + animation.ToJson() + ";"
            + (navigation == null ? "" : "\n      window.GEO_BOOK = " + navigation.ToJson() + ";") + "</script>\n"
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
