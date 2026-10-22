using System.Text;
using System.Text.RegularExpressions;

namespace GeoAnim;

/// <summary>
/// Keywords of a page ("keyword Punkt/e/en/es Strecke/n"): "geoanim exportdir" turns their first occurrence in each step
/// of the other pages into a link to the page.
/// </summary>
public static class Keywords
{
    /// <summary>
    /// The word forms of a keyword line: words or phrases in quotes, separated by spaces; "Punkt/e/en" is the stem alone
    /// or with one of the endings (Punkt, Punkte, Punkten); in a phrase the endings belong to its last word.
    /// </summary>
    public static List<string> Parse(string text)
    {
        var forms = new List<string>();
        int i = 0;
        while (true)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }
            if (i >= text.Length)
            {
                break;
            }
            string entry;
            if (text[i] == '"')
            {
                int close = text.IndexOf('"', i + 1);
                if (close < 0)
                {
                    throw new GeoException("A keyword in quotes has no closing \".");
                }
                entry = text.Substring(i + 1, close - i - 1);
                i = close + 1;
                if (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    throw new GeoException($"After the keyword \"{entry}\" comes a space (endings go inside the quotes: \"rechter Winkel/s\").");
                }
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
                entry = text[start..i];
            }
            string[] parts = entry.Split('/');
            string stem = Regex.Replace(parts[0].Trim(), @"\s+", " ");
            if (stem.Length == 0 || !stem.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-'))
            {
                throw new GeoException($"\"{entry}\" is not a keyword: words of letters, endings after / (like Punkt/e/en).");
            }
            forms.Add(stem);
            foreach (string ending in parts.Skip(1))
            {
                if (ending.Length == 0 || !ending.All(char.IsLetter))
                {
                    throw new GeoException($"\"{entry}\": an ending after / consists of letters (like Punkt/e/en).");
                }
                forms.Add(stem + ending);
            }
        }
        if (forms.Count == 0)
        {
            throw new GeoException("\"keyword\" needs words, like keyword Punkt/e/en.");
        }
        return forms;
    }

    /// <summary>
    /// The animation with links in its step descriptions and explanations: the first occurrence of a word form in a step
    /// (description and info together) links to its page (<paramref name="links"/>: form → address). Only whole words
    /// count, with the case as given, the longest form first; formulas, code and existing links stay as they are.
    /// </summary>
    public static AnimationData Link(AnimationData animation, IReadOnlyDictionary<string, string> links)
    {
        if (links.Count == 0)
        {
            return animation;
        }
        // the longest forms first ("rechter Winkel" before "Winkel"); the space of a phrase also matches several
        string alternatives = string.Join("|", links.Keys.OrderByDescending(f => f.Length).ThenBy(f => f, StringComparer.Ordinal)
            .Select(f => Regex.Escape(f).Replace("\\ ", " +")));
        var pattern = new Regex(@"\G(?<![\p{L}\p{N}])(?:" + alternatives + @")(?![\p{L}\p{N}])", RegexOptions.CultureInvariant);
        string Address(string match) => links[Regex.Replace(match, " +", " ")];

        var steps = new List<AnimationStep>();
        foreach (AnimationStep step in animation.Steps)
        {
            var linked = new HashSet<string>(StringComparer.Ordinal);
            string caption = LinkText(step.Caption, pattern, Address, linked);
            string? info = step.Info == null ? null : LinkText(step.Info, pattern, Address, linked);
            steps.Add(step with { Caption = caption, Info = info });
        }
        return animation.WithSteps(steps);
    }

    // a text with links: what is not plain text (\x, $formula$, `code`, [link](address), <address>) is copied
    private static string LinkText(string text, Regex pattern, Func<string, string> address, HashSet<string> linked)
    {
        var result = new StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            int skip = c switch
            {
                '\\' => Math.Min(2, text.Length - i),
                '$' => Closing(text, i, '$'),
                '`' => Closing(text, i, '`'),
                '[' => Match(LinkPattern, text, i),
                '<' => Match(AutolinkPattern, text, i),
                _ => 0,
            };
            if (skip > 0)
            {
                result.Append(text, i, skip);
                i += skip;
                continue;
            }
            Match match = pattern.Match(text, i);
            if (match.Success)
            {
                string target = address(match.Value);
                if (linked.Add(target))
                {
                    result.Append('[').Append(match.Value).Append("](").Append(target).Append(')');
                }
                else
                {
                    result.Append(match.Value);
                }
                i += match.Length;
                continue;
            }
            result.Append(c);
            i++;
        }
        return result.ToString();
    }

    // the length up to the closing mark (a formula: not after a backslash), or the rest of the text
    private static int Closing(string text, int i, char mark)
    {
        for (int k = i + 1; k < text.Length; k++)
        {
            if (text[k] == mark && !(mark == '$' && text[k - 1] == '\\'))
            {
                return k + 1 - i;
            }
        }
        return text.Length - i;
    }

    private static int Match(Regex regex, string text, int i)
    {
        Match match = regex.Match(text, i);
        return match.Success ? match.Length : 0;
    }

    private static readonly Regex LinkPattern = new(@"\G\[[^\]]*\]\([^)\s]*\)");
    private static readonly Regex AutolinkPattern = new(@"\G<[^\s<>]+>");

    /// <summary>The address of <paramref name="target"/> from the page <paramref name="page"/> (both relative to the same folder, with "/").</summary>
    public static string RelativeAddress(string page, string target)
    {
        string folder = Path.GetDirectoryName(page.Replace('/', Path.DirectorySeparatorChar)) ?? "";
        string relative = Path.GetRelativePath(folder.Length == 0 ? "." : folder, target.Replace('/', Path.DirectorySeparatorChar))
            .Replace(Path.DirectorySeparatorChar, '/');
        // an address in Markdown has no spaces or parentheses
        return string.Join("/", relative.Split('/').Select(part => part == ".." ? part
            : Uri.EscapeDataString(part).Replace("(", "%28").Replace(")", "%29")));
    }
}
