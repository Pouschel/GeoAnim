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
    [GeneratedRegex(@"\[([^\]]*)\]\([^)\s]*\)|<([^>\s]+)>|\\([*_`${}])|\*\*|[*`$]|(?<![\p{L}\p{N}])_|_(?![\p{L}\p{N}])")]
    private static partial Regex MarkdownPattern();

    /// <summary>The length a description is cut to (at a word).</summary>
    internal const int DescriptionLength = 250;

    /// <summary>
    /// The page description: the one from the script, else the step descriptions one after the other – as plain text,
    /// formulas readable ($bar(AB)$ → AB), cut at a word after about <see cref="DescriptionLength"/> characters.
    /// </summary>
    internal static string DescriptionOf(AnimationData animation)
    {
        string text = animation.Description != null ? animation.WithFinalNumbers(animation.Description) : string.Join(" ", animation.Steps
            .Select(step => ReadableFormulas(animation.WithFinalNumbers(step.Caption)).Trim())
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
        NoLink().Replace(MarkdownPattern().Replace(text, m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value), "")
            .Replace('\n', ' ');

    // a backslash before a letter ("\Seite": no keyword link there) is not shown
    [GeneratedRegex(@"\\(?=\p{L})")]
    private static partial Regex NoLink();

    /// <summary>
    /// The page for <paramref name="animation"/>; in an exported book with the way to the other pages. Without
    /// <paramref name="assets"/> the page has everything inside (it works alone); with it, the page links to the player's
    /// files in that folder (relative address, e.g. "../assets/"; see <see cref="WriteAssets"/>), so that a book on a
    /// web server loads them once for all its pages.
    /// </summary>
    public static string Export(AnimationData animation, PageNavigation? navigation = null, string? assets = null, PageHead? head = null)
    {
        string html = ViewerFiles.ReadText("player.html");
        string title = animation.Title == null ? "GeoAnim" : PlainText(animation.WithFinalNumbers(animation.Title));
        string description = DescriptionOf(animation);
        html = TitlePattern().Replace(html, "<title>" + WebUtility.HtmlEncode(title) + "</title>"
            + "\n    <meta name=\"description\" content=\"" + WebUtility.HtmlEncode(description) + "\">"
            + (head == null ? "" : HeadTags(head, title, description)), 1);
        if (head?.Language != null)
        {
            html = html.Replace("<html lang=\"en\">", "<html lang=\"" + WebUtility.HtmlEncode(head.Language) + "\">", StringComparison.Ordinal);
        }
        html = StylesheetPattern().Replace(html, m => assets == null
            ? "<style>\n" + InlineCss(m.Groups[1].Value) + "</style>"
            : $"<link rel=\"stylesheet\" href=\"{assets}{m.Groups[1].Value}?v={Version(m.Groups[1].Value)}\">");
        html = ScriptPattern().Replace(html, m =>
            // the JSON escapes <, > and &, so it cannot end the script element
            "<script>window.GEO_ANIMATION = " + animation.ToJson() + ";"
            + (navigation == null ? "" : "\n      window.GEO_BOOK = " + navigation.ToJson() + ";") + "</script>\n"
            + (assets == null
                ? "    <script>\n" + EscapeScript(ViewerFiles.ReadText(m.Groups[1].Value)) + "</script>"
                : $"    <script src=\"{assets}{m.Groups[1].Value}?v={Version(m.Groups[1].Value)}\"></script>"));
        return html;
    }

    /// <summary>
    /// The tags of a page's head in a book: icon, canonical address and the preview when the page is shared (Open Graph).
    /// </summary>
    internal static string HeadTags(PageHead head, string title, string description)
    {
        var tags = new System.Text.StringBuilder();
        string Attribute(string value) => WebUtility.HtmlEncode(value);
        if (head.Icon != null)
        {
            tags.Append("\n    <link rel=\"icon\" href=\"").Append(Attribute(head.Icon)).Append("\">");
        }
        if (head.Address != null)
        {
            tags.Append("\n    <link rel=\"canonical\" href=\"").Append(Attribute(head.Address)).Append("\">");
            tags.Append("\n    <meta property=\"og:type\" content=\"article\">");
            tags.Append("\n    <meta property=\"og:url\" content=\"").Append(Attribute(head.Address)).Append("\">");
            tags.Append("\n    <meta property=\"og:title\" content=\"").Append(Attribute(title)).Append("\">");
            tags.Append("\n    <meta property=\"og:description\" content=\"").Append(Attribute(description)).Append("\">");
            if (head.SiteName != null)
            {
                tags.Append("\n    <meta property=\"og:site_name\" content=\"").Append(Attribute(head.SiteName)).Append("\">");
            }
        }
        return tags.ToString();
    }

    /// <summary>
    /// Writes the player's files (script, stylesheets, fonts) into <paramref name="folder"/> for pages exported with
    /// assets; files that are already up to date are left alone.
    /// </summary>
    public static void WriteAssets(string folder)
    {
        foreach (string path in ViewerFiles.Paths.Where(p => p != "player.html"))
        {
            string target = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            byte[] content = ViewerFiles.Read(path)!;
            if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(content))
            {
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, content);
        }
    }

    // a short fingerprint of a player file: a new version gets a new address, so that browsers do not keep the old one
    private static string Version(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ViewerFiles.Read(path)!))[..10].ToLowerInvariant();

    /// <summary>
    /// The player page without an animation, for an editor's preview: it gets its animations as messages
    /// ({ type: "animation" | "error" | "step", … }, see player.js) instead of fetching them from "geoanim serve".
    /// </summary>
    public static string ExportPlayer() => Embedded("player.html");

    /// <summary>
    /// The page for a book page in Markdown, for an editor's preview: it gets the page as messages
    /// ({ type: "page", html, figures, error } and { type: "line", line }, see page.js). It carries the player for its
    /// figures (window.GEO_FIGURE_PLAYER: the player page; each figure gets one in a frame, with its animation and its
    /// part in place of the comment <see cref="FigureMark"/>).
    /// </summary>
    public static string ExportPagePreview() =>
        Embedded("page.html", "window.GEO_EMBEDDED = true;\n      window.GEO_FIGURE_PLAYER = "
            + System.Text.Json.JsonSerializer.Serialize(Embedded("player.html", FigureMark)) + ";");

    /// <summary>The place in the player page of a figure where its animation and its part (window.GEO_FIGURE) go.</summary>
    public const string FigureMark = "/*GEO_FIGURE*/";

    // a page with its stylesheets and script inside, for a webview; the preamble is a script before its own
    private static string Embedded(string page, string preamble = "window.GEO_EMBEDDED = true;")
    {
        string html = ViewerFiles.ReadText(page);
        html = StylesheetPattern().Replace(html, m => "<style>\n" + InlineCss(m.Groups[1].Value) + "</style>");
        html = ScriptPattern().Replace(html, m =>
            "<script>" + EscapeScript(preamble) + "</script>\n"
            + "    <script>\n" + EscapeScript(ViewerFiles.ReadText(m.Groups[1].Value)) + "</script>");
        return html;
    }

    /// <summary>The look of a book page as in the preview (fonts inside), for a page of its own.</summary>
    internal static string PageStyles() => InlineCss("fonts/fonts.css") + "\n" + InlineCss("page.css");

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
