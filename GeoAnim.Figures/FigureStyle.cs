using System.Globalization;
using System.Text.RegularExpressions;

namespace GeoAnim.Figures;

/// <summary>
/// The size of the figures of a book: <see cref="PointsPerUnit"/> is the length of a coordinate unit in points (pt,
/// 1/72 inch), <see cref="SizeFactor"/> what a size of the scripts (texts, line widths, points – "points at a player
/// 1280 pixels wide") is in pt. Example: "scale: 5mm" and "labels: 9pt" for "labelsize: 34" → 14.17 pt per unit, 9/34 pt
/// per size.
/// </summary>
public sealed partial record FigureStyle(double PointsPerUnit, double SizeFactor)
{
    /// <summary>Without settings: 5 mm per unit, size 34 as 9 pt.</summary>
    public static FigureStyle Default { get; } = new(Millimetres(5), 9.0 / 34);

    /// <summary>Points for millimetres.</summary>
    public static double Millimetres(double mm) => mm * 72 / 25.4;

    /// <summary>
    /// A length with its unit ("5mm", "0.5cm", "9pt", "0.2in") in points; null if it is none (a number alone is taken as
    /// points).
    /// </summary>
    public static double? ParseLength(string text)
    {
        Match match = LengthPattern().Match(text.Trim());
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
        {
            return null;
        }
        return match.Groups[2].Value.ToLowerInvariant() switch
        {
            "mm" => Millimetres(value),
            "cm" => Millimetres(value * 10),
            "in" => value * 72,
            _ => value,
        };
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*(mm|cm|pt|in)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LengthPattern();
}
