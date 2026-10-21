using System.Text;

namespace GeoAnim.Formulas;

/// <summary>A piece of a text: plain text or a formula (AsciiMath).</summary>
public readonly record struct TextRun(string Text, bool IsFormula);

/// <summary>
/// Texts with formulas: AsciiMath between dollar signs ("Kreis um $A$ mit Radius $|AB|$"); "\$" is a dollar sign.
/// </summary>
public static class MathText
{
    /// <summary>Whether the text has a formula (a quick check: a dollar sign that is not "\$").</summary>
    public static bool HasFormula(string text) => Split(text).Any(run => run.IsFormula);

    /// <summary>The text in pieces; throws <see cref="FormatException"/> for a formula without closing $ or an empty one.</summary>
    public static IReadOnlyList<TextRun> Split(string text)
    {
        var runs = new List<TextRun>();
        var current = new StringBuilder();
        bool inFormula = false;
        int formulaStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] == '$')
            {
                current.Append('$');
                i++;
            }
            else if (c == '$')
            {
                if (inFormula && current.ToString().Trim().Length == 0)
                {
                    throw new FormatException($"The formula at character {formulaStart + 1} is empty.");
                }
                if (current.Length > 0)
                {
                    runs.Add(new TextRun(inFormula ? current.ToString().Trim() : current.ToString(), inFormula));
                    current.Clear();
                }
                inFormula = !inFormula;
                formulaStart = i;
            }
            else
            {
                current.Append(c);
            }
        }
        if (inFormula)
        {
            throw new FormatException($"The formula at character {formulaStart + 1} has no closing $ (write \\$ for a dollar sign).");
        }
        if (current.Length > 0)
        {
            runs.Add(new TextRun(current.ToString(), false));
        }
        return runs;
    }

    /// <summary>The formulas of a text, in their order.</summary>
    public static IEnumerable<string> Formulas(string text) => Split(text).Where(r => r.IsFormula).Select(r => r.Text);
}
