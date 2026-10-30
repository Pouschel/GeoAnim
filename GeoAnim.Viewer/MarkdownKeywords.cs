using System.Text;
using System.Text.RegularExpressions;
using Markdig.Extensions.CustomContainers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace GeoAnim.Viewer;

/// <summary>
/// A keyword of a page: its term (the name in the keyword index), its word forms and the place it leads to (the id of
/// an element on the page; null: the page itself) – from the front matter ("keywords: [Punkt/e/en]"), a term in bold or
/// italics ("**Strahl**{kw=/s/en}"), a heading or a statement ("{kw=Strecke/n}").
/// </summary>
public sealed record PageKeywordDefinition(string Term, IReadOnlyList<string> Forms, string? Anchor, int Line, int Column);

/// <summary>A keyword of another page of the book: where it leads (address relative to the page) and that page's title.</summary>
public sealed record KeywordLink(string Href, string Title);

public sealed partial class MarkdownPage
{
    /// <summary>The keywords of the page (front matter and "{kw}"), in their order.</summary>
    public IReadOnlyList<PageKeywordDefinition> KeywordDefinitions { get; private set; } = [];

    // the keywords at terms, headings and statements: "**Haus**{kw=/es/e,Häuser/n}" – the bold word with endings ("/es/e"),
    // and further forms of the same term ("Häuser/n"); "{kw}" alone: the word as it is; "{kw=Gerade/n}" (no "/" first):
    // the forms written out. The attribute leaves the element; it gets an id to be linked to
    private static List<PageKeywordDefinition> ReadKeywords(string text, MarkdownDocument document, IEnumerable<string> frontKeywords,
        Func<int, (int, int)> locate, List<MarkdownProblem> errors)
    {
        var found = new List<PageKeywordDefinition>();
        foreach (string keyword in frontKeywords)
        {
            try
            {
                foreach ((string term, List<string> forms) in GeoAnim.Keywords.ParseTerms(keyword))
                {
                    found.Add(new PageKeywordDefinition(term, forms, null, 1, 1));
                }
            }
            catch (GeoException e)
            {
                errors.Add(new MarkdownProblem(1, 1, "Front matter: " + e.Message));
            }
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (MarkdownObject element in document.Descendants())
        {
            if (element is not (EmphasisInline or HeadingBlock or CustomContainer) || element.TryGetAttributes() is not { Properties: { } properties } attributes)
            {
                continue;
            }
            int index = properties.FindIndex(p => p.Key == "kw");
            if (index < 0)
            {
                continue;
            }
            string value = (properties[index].Value ?? "").Trim();
            properties.RemoveAt(index);
            (int line, int column) = locate(element.Span.Start);
            string word = element switch
            {
                EmphasisInline emphasis => PlainWords(emphasis),
                HeadingBlock heading => HeadingText(text, heading),
                CustomContainer container => StatementOf(container, text.Split('\n'), locate, []) is { Title: { } title } ? title : "",
                _ => "",
            };
            word = Regex.Replace(word, @"\s*\{[^{}]*\}\s*$", "").Trim();
            try
            {
                (string term, List<string> forms) = KeywordForms(word, value);
                attributes.Id ??= UniqueId("kw-" + StepName(term), ids);
                ids.Add(attributes.Id);
                found.Add(new PageKeywordDefinition(term, forms, attributes.Id, line, column));
            }
            catch (GeoException e)
            {
                errors.Add(new MarkdownProblem(line, column, e.Message));
            }
        }
        return found;
    }

    // attributes that Markdig leaves as text because a value has spaces ("## Metrische Räume {kw=Metrik/en, metrische/r
    // Raum}", "**Haus**{kw=/es, Häuser/n}"): read here, at the end of a heading and right after an emphasis
    private static void RescueAttributes(MarkdownDocument document, string text)
    {
        string[] lines = text.Split('\n');
        foreach (MarkdownObject element in document.Descendants().ToList())
        {
            // the opening line of a statement ("::: example {#bsp kw=euklidische Ebene} Titel"): Markdig takes "Ebene" for
            // an attribute of its own; read them again from the line
            if (element is CustomContainer container && container.Line < lines.Length
                && Regex.Match(lines[container.Line].TrimEnd('\r'), @"^\s*:{3,}[^{}]*\{([^{}]*)\}") is { Success: true } opening)
            {
                HtmlAttributes attributes = container.GetAttributes();
                attributes.Properties?.Clear();
                ParseAttributes(opening.Groups[1].Value, attributes);
                continue;
            }
            // a word in bold whose attributes Markdig read itself ("**Umgekehrte Dreiecksungleichung**{kw=umgekehrte
            // Dreiecksungleichung}" – "Dreiecksungleichung" as an attribute of its own): read again from the text after it
            if (element is EmphasisInline marked && marked.TryGetAttributes() is { } read && marked.Span.End + 1 < text.Length
                && text[marked.Span.End + 1] == '{' && LeadingAttributes().Match(text.Substring(marked.Span.End + 1, Math.Min(500, text.Length - marked.Span.End - 1))) is { Success: true } after)
            {
                read.Properties?.Clear();
                ParseAttributes(after.Groups[1].Value, read);
                continue;
            }
            // a heading likewise ("## Metrische Räume {kw=metrische/r/n Raum/es}"), if Markdig took its attributes
            if (element is HeadingBlock readHeading && readHeading.TryGetAttributes() is { Properties.Count: > 0 } headingAttributes
                && readHeading.Line < lines.Length && TrailingAttributes().Match(lines[readHeading.Line].TrimEnd('\r')) is { Success: true } trailing
                && !(readHeading.Inline?.LastChild is LiteralInline text2 && text2.Content.ToString().Contains('{')))
            {
                headingAttributes.Properties?.Clear();
                ParseAttributes(trailing.Groups[1].Value, headingAttributes);
                continue;
            }
            if (element is HeadingBlock { Inline.LastChild: LiteralInline last } heading)
            {
                string content = last.Content.ToString();
                Match match = TrailingAttributes().Match(content);
                if (match.Success && ParseAttributes(match.Groups[1].Value, heading.GetAttributes()))
                {
                    last.Content = new Markdig.Helpers.StringSlice(content.Substring(0, match.Index).TrimEnd());
                }
            }
            else if (element is EmphasisInline { NextSibling: LiteralInline next } emphasis)
            {
                string content = next.Content.ToString();
                Match match = LeadingAttributes().Match(content);
                if (match.Success && ParseAttributes(match.Groups[1].Value, emphasis.GetAttributes()))
                {
                    next.Content = new Markdig.Helpers.StringSlice(content.Substring(match.Length));
                }
            }
        }
    }

    [GeneratedRegex(@"\s*\{([^{}]*)\}\s*$")]
    private static partial Regex TrailingAttributes();

    [GeneratedRegex(@"^\{([^{}]*)\}")]
    private static partial Regex LeadingAttributes();

    // "#name", ".class", "key=value" (a value up to the next attribute, spaces and commas allowed; or in quotes), "kw";
    // false if it does not look like attributes (then it stays text)
    private static bool ParseAttributes(string text, HtmlAttributes attributes)
    {
        MatchCollection parts = AttributeStart().Matches(text);
        if (parts.Count == 0 || text.Substring(0, parts[0].Index).Trim().Length > 0)
        {
            return false;
        }
        for (int i = 0; i < parts.Count; i++)
        {
            Match part = parts[i];
            int end = i + 1 < parts.Count ? parts[i + 1].Index : text.Length;
            string token = part.Groups[1].Value;
            string value = text.Substring(part.Index + part.Length, end - part.Index - part.Length).Trim().Trim('"');
            if (token.StartsWith('#'))
            {
                attributes.Id ??= token.Substring(1);
            }
            else if (token.StartsWith('.'))
            {
                attributes.AddClass(token.Substring(1));
            }
            else
            {
                string key = token.TrimEnd('=');
                attributes.Properties?.RemoveAll(p => p.Key == key);
                attributes.AddProperty(key, token.EndsWith('=') ? value : "");
            }
        }
        return true;
    }

    [GeneratedRegex(@"(?:^|\s)(#[\p{L}\p{N}_-]+|\.[\p{L}\p{N}_-]+|[\p{L}]+=|kw(?=\s|$))")]
    private static partial Regex AttributeStart();

    // the term and its forms from the marked word and the attribute's value
    private static (string Term, List<string> Forms) KeywordForms(string word, string value)
    {
        var parts = value.Length == 0 ? [""] : value.Split(',').Select(p => p.Trim()).ToList();
        string term = "";
        var forms = new List<string>();
        foreach (string part in parts)
        {
            // "" or "/es/e": the marked word (with endings); else forms written out
            string written = part.Length == 0 || part.StartsWith('/') ? word + part : part;
            if (written.Length == 0)
            {
                throw new GeoException("A keyword needs a word: write it in bold (**Strahl**{kw}) or give it ({kw=Strahl/s/en}).");
            }
            List<(string Term, List<string> Forms)> terms = GeoAnim.Keywords.ParseTerms(written);
            if (term.Length == 0)
            {
                term = terms[0].Term;
            }
            forms.AddRange(terms.SelectMany(t => t.Forms));
        }
        return (term, forms.Distinct(StringComparer.Ordinal).ToList());
    }

    private static string UniqueId(string id, HashSet<string> taken)
    {
        string unique = id;
        for (int n = 2; taken.Contains(unique); n++)
        {
            unique = id + "-" + n;
        }
        return unique;
    }

    // the words of an emphasis, without formulas
    private static string PlainWords(ContainerInline container)
    {
        var words = new StringBuilder();
        foreach (Inline inline in container)
        {
            words.Append(inline switch
            {
                LiteralInline literal => literal.Content.ToString(),
                ContainerInline inner => PlainWords(inner),
                LineBreakInline => " ",
                _ => "",
            });
        }
        return words.ToString().Trim();
    }

    // ------------------------------------------------------------------ links

    // the first occurrence of a keyword of another page in each section (up to the next heading) becomes a link to it;
    // not in headings, links or at the place of a keyword itself; "\Wort": no link (the backslash is not shown)
    private static void LinkKeywords(MarkdownDocument document, IReadOnlyDictionary<string, KeywordLink> links)
    {
        Regex? pattern = links.Count == 0 ? null : new Regex(@"(\\?)(?<![\p{L}\p{N}])(" + string.Join("|", links.Keys
            .OrderByDescending(f => f.Length).ThenBy(f => f, StringComparer.Ordinal)
            .Select(f => Regex.Escape(f).Replace("\\ ", " +"))) + @")(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);
        var linked = new HashSet<string>(StringComparer.Ordinal);
        foreach (MarkdownObject element in document.Descendants().ToList())
        {
            if (element is HeadingBlock)
            {
                linked.Clear();
                continue;
            }
            if (element is not LiteralInline literal || InHeadingOrLink(literal))
            {
                continue;
            }
            string content = literal.Content.ToString();
            if (!content.Contains('\\') && pattern == null)
            {
                continue;
            }
            var pieces = new List<Inline>();
            int position = 0;
            bool changed = false;
            IEnumerable<Match> matches = pattern == null ? [] : pattern.Matches(content);
            foreach (Match match in matches)
            {
                string form = Regex.Replace(match.Groups[2].Value, " +", " ");
                KeywordLink target = links[form];
                bool escaped = match.Groups[1].Length > 0;
                if (!escaped && !linked.Add(target.Href))
                {
                    continue;
                }
                if (match.Index > position)
                {
                    pieces.Add(new LiteralInline(content.Substring(position, match.Index - position)));
                }
                position = match.Index + match.Length;
                changed = true;
                if (escaped)
                {
                    pieces.Add(new LiteralInline(match.Groups[2].Value));
                    continue;
                }
                var link = new LinkInline(target.Href, form + " – " + target.Title);
                link.GetAttributes().AddClass("keyword");
                link.AppendChild(new LiteralInline(match.Groups[2].Value));
                pieces.Add(link);
            }
            // "\Wort" before a word that is no keyword: the backslash is not shown either
            string rest = content.Substring(position);
            string unescaped = Regex.Replace(rest, @"\\(?=\p{L})", "");
            if (!changed && unescaped == rest)
            {
                continue;
            }
            if (unescaped.Length > 0)
            {
                pieces.Add(new LiteralInline(unescaped));
            }
            ReplaceBy(literal, pieces);
        }
    }

    // an inline replaced by others, in their order (each after the one before it: InsertBefore leaves the links between the
    // inlines broken when it is used again on an inline that was put in that way)
    internal static void ReplaceBy(Inline inline, List<Inline> pieces)
    {
        Inline current = inline;
        foreach (Inline piece in pieces)
        {
            current.InsertAfter(piece);
            current = piece;
        }
        inline.Remove();
    }

    // in a heading, a link or the marked term of a keyword
    private static bool InHeadingOrLink(LiteralInline literal)
    {
        for (ContainerInline? parent = literal.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is LinkInline || parent.TryGetAttributes()?.Id?.StartsWith("kw-", StringComparison.Ordinal) == true)
            {
                return true;
            }
        }
        Block? block = literal.Parent?.ParentBlock ?? FindBlock(literal);
        return block is HeadingBlock;
    }

    private static Block? FindBlock(Inline inline)
    {
        Inline current = inline;
        while (current.Parent != null)
        {
            current = current.Parent;
        }
        return (current as ContainerInline)?.ParentBlock;
    }
}
