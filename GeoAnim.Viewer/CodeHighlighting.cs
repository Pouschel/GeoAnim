using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace GeoAnim.Viewer;

/// <summary>
/// Code in colour: a block "```eleu" (the language of EleuStudio, its words as in its VS Code extension) with spans
/// "tk-…" (keyword, function, the cat's functions, constant, string, number, comment); page.css colours them, for ILIAS
/// <see cref="InlineStyles"/> writes the colours into them. Other blocks stay as they are.
/// </summary>
public static partial class CodeHighlighting
{
    private static readonly Dictionary<string, string> Colors = new(StringComparer.Ordinal)
    {
        ["tk-kw"] = "#a626a4",
        ["tk-fn"] = "#4078f2",
        ["tk-api"] = "#0184bc",
        ["tk-const"] = "#986801",
        ["tk-str"] = "#50a14f",
        ["tk-num"] = "#986801",
        ["tk-com"] = "#8a8f98",
    };

    [GeneratedRegex("""
        (?<com>//.*)
        |(?<str>"(?:\\.|[^"\\])*"?)
        |(?<num>\b\d+(?:\.\d+)?\b)
        |(?<kw>\b(?:else|for|if|while|break|continue|return|repeat|assert|and|or|class|function|fun|var)\b)
        |(?<const>\b(?:nil|true|false|super|this|PI)\b)
        |(?<fn>\b(?:clock|sqrt|abs|acos|asin|ceil|cos|floor|log10|sin|pow|random|typeof|toString|print|toFixed|parseInt|parseFloat|parseNum|parseNumber|len|charAt|at|setAt|substr|indexOf|lastIndexOf|toLowerCase|toUpperCase)\b)
        |(?<api>\b(?:_puzzle|_isSolved|move|push|take|drop|turn|paint|setShape|color|seeing|read|readNumber|write)\b)
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex EleuToken();

    /// <summary>Whether blocks of this language ("eleu") get colours.</summary>
    public static bool Knows(string? language) => string.Equals(language?.Trim(), "eleu", StringComparison.OrdinalIgnoreCase);

    /// <summary>The code as HTML with its words in spans "tk-…".</summary>
    public static string Highlight(string code)
    {
        var html = new StringBuilder();
        int at = 0;
        foreach (Match token in EleuToken().Matches(code))
        {
            html.Append(WebUtility.HtmlEncode(code[at..token.Index]));
            string kind = new[] { "com", "str", "num", "kw", "const", "fn", "api" }.First(k => token.Groups[k].Success);
            html.Append("<span class=\"tk-").Append(kind).Append("\">").Append(WebUtility.HtmlEncode(token.Value)).Append("</span>");
            at = token.Index + token.Length;
        }
        return html.Append(WebUtility.HtmlEncode(code[at..])).ToString();
    }

    /// <summary>The colours as styles of the spans (for ILIAS, which has no page.css).</summary>
    public static string InlineStyles(string html) =>
        TokenSpan().Replace(html, m => Colors.TryGetValue(m.Groups[1].Value, out string? color)
            ? $"<span style=\"color: {color}{(m.Groups[1].Value == "tk-com" ? "; font-style: italic" : "")}\">"
            : m.Value);

    [GeneratedRegex("<span class=\"(tk-[a-z]+)\">")]
    private static partial Regex TokenSpan();

    /// <summary>A code block: "```eleu" in colour, others as Markdig writes them.</summary>
    internal sealed class Renderer(CodeBlockRenderer original) : HtmlObjectRenderer<CodeBlock>
    {
        protected override void Write(HtmlRenderer renderer, CodeBlock block)
        {
            if (block is not FencedCodeBlock fenced || !Knows(fenced.Info))
            {
                original.Write(renderer, block);
                return;
            }
            renderer.EnsureLine();
            renderer.Write("<pre").WriteAttributes(block).Write("><code class=\"language-eleu\">");
            renderer.Write(Highlight(string.Join("\n", fenced.Lines.Lines.Take(fenced.Lines.Count).Select(l => l.ToString()))));
            renderer.WriteLine("</code></pre>");
        }
    }
}
