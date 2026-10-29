using System.Globalization;
using System.Net;
using GeoAnim.Formulas;

namespace GeoAnim.Figures;

/// <summary>
/// A formula of the text as a picture of its own (for readers that show no SVG in the text, Kindle): SVG with its size in
/// points (with a small margin), its depth below the baseline (for vertical-align) and the AsciiMath as alternative
/// text.
/// </summary>
public sealed record FormulaImage(string Svg, double Width, double Height, double Depth, string AltText)
{
    /// <summary>
    /// <paramref name="asciiMath"/> in the font size <paramref name="size"/> (pt) and a colour; throws
    /// <see cref="FormatException"/> if it cannot be rendered.
    /// </summary>
    public static FormulaImage Render(string asciiMath, double size, string color = "#000000")
    {
        Formula tight = MathRenderer.Render(asciiMath);
        // a little room around MathJax's box: italic letters and bars reach out of it a bit
        const double Side = 60, Above = 40;
        Formula formula = tight with { X = tight.X - Side, Y = tight.Y - Above, Width = tight.Width + 2 * Side, Height = tight.Height + 2 * Above };
        double scale = size / 1000;
        double width = formula.Width * scale, height = formula.Height * scale;
        static string N(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
        string svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"{N(width)}pt\" height=\"{N(height)}pt\""
            + $" viewBox=\"{N(formula.X)} {N(formula.Y)} {N(formula.Width)} {N(formula.Height)}\" role=\"img\" aria-label=\"{WebUtility.HtmlEncode(asciiMath)}\">"
            + formula.Svg.Replace("currentColor", color, StringComparison.Ordinal) + "</svg>";
        return new FormulaImage(svg, width, height, (formula.Y + formula.Height) * scale, asciiMath);
    }

    /// <summary>The formula as PNG on white.</summary>
    public byte[] Png(double dpi = FigurePng.DefaultDpi) => FigurePng.Render(Svg, Width, Height, dpi);
}
