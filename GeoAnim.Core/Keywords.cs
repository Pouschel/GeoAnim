using System.Text;
using System.Text.RegularExpressions;

namespace GeoAnim;

/// <summary>
/// Keywords of a page ("keyword Punkt/e/en/es, Satz/es des Pythagoras"): "geoanim exportdir" turns their first occurrence in each step
/// of the other pages into a link to the page.
/// </summary>
public static class Keywords
{
    /// <summary>
    /// The word forms of a keyword line: terms separated by commas, each one word or several; a word may carry endings
    /// after "/" ("Punkt/e/en": Punkt, Punkte, Punkten), and a term stands for all combinations of its words' forms
    /// ("rechte/r/n Winkel/s": rechte Winkel, rechte Winkels, rechter Winkel, …, rechten Winkels).
    /// </summary>
    public static List<string> Parse(string text)
    {
        if (text.Contains('"'))
        {
            throw new GeoException("Keywords are separated by commas, without quotes: keyword Satz/es des Pythagoras, Punkt/e/en.");
        }
        var forms = new List<string>();
        List<string> terms = text.Split(',').ToList();
        // a comma at the end (a list continued with "|") is allowed
        if (terms.Count > 1 && terms[^1].Trim().Length == 0)
        {
            terms.RemoveAt(terms.Count - 1);
        }
        foreach (string term in terms)
        {
            string[] words = term.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                throw new GeoException(text.Trim().Length == 0
                    ? "\"keyword\" needs words, like keyword Punkt/e/en, Satz/es des Pythagoras."
                    : "There is an empty keyword between two commas.");
            }
            // all combinations of the forms of the words
            var combinations = new List<string> { "" };
            foreach (string word in words)
            {
                List<string> wordForms = WordForms(word);
                combinations = combinations.SelectMany(start => wordForms.Select(form => start.Length == 0 ? form : start + " " + form)).ToList();
                if (combinations.Count > 1000)
                {
                    throw new GeoException($"\"{term.Trim()}\" has too many combinations of endings.");
                }
            }
            forms.AddRange(combinations);
        }
        return forms;
    }

    // "Punkt/e/en" → Punkt, Punkte, Punkten
    private static List<string> WordForms(string word)
    {
        string[] parts = word.Split('/');
        string stem = parts[0];
        if (stem.Length == 0 || !stem.All(c => char.IsLetterOrDigit(c) || c == '-'))
        {
            throw new GeoException($"\"{word}\" is not a word of a keyword: letters, endings after / (like Punkt/e/en); terms are separated by commas.");
        }
        var forms = new List<string> { stem };
        foreach (string ending in parts.Skip(1))
        {
            if (ending.Length == 0 || !ending.All(char.IsLetter))
            {
                throw new GeoException($"\"{word}\": an ending after / consists of letters (like Punkt/e/en).");
            }
            forms.Add(stem + ending);
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
