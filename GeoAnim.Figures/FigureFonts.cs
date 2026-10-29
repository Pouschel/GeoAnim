using System.Reflection;
using SkiaSharp;

namespace GeoAnim.Figures;

/// <summary>
/// The fonts of the figures (STIX Two Text, as in the player): widths of texts and their outlines as SVG path data, so
/// that a figure needs no font where it is shown. Thread-safe.
/// </summary>
internal static class FigureFonts
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<(bool Bold, bool Italic), SKTypeface> Typefaces = new();

    private static SKTypeface Typeface(bool bold, bool italic)
    {
        lock (Gate)
        {
            if (!Typefaces.TryGetValue((bold, italic), out SKTypeface? typeface))
            {
                // there is no bold italic: bold then
                string name = bold ? "stix-two-text-700-normal.ttf" : italic ? "stix-two-text-400-italic.ttf" : "stix-two-text-400-normal.ttf";
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("fonts/" + name)
                    ?? throw new InvalidOperationException("Font not found: " + name);
                typeface = SKTypeface.FromStream(stream) ?? throw new InvalidOperationException("Font cannot be read: " + name);
                Typefaces[(bold, italic)] = typeface;
            }
            return typeface;
        }
    }

    // exact outlines and widths: no hinting, no rounding to whole pixels (sizes are points here, not pixels)
    private static SKFont Font(bool bold, bool italic, double size) =>
        new(Typeface(bold, italic), (float)size) { Hinting = SKFontHinting.None, Subpixel = true, LinearMetrics = true, Edging = SKFontEdging.Antialias };

    /// <summary>The width of a text in the size (in the same unit as the size).</summary>
    public static double Width(string text, double size, bool bold, bool italic)
    {
        lock (Gate)
        {
            using SKFont font = Font(bold, italic, size);
            return font.MeasureText(text);
        }
    }

    /// <summary>The outline of a text with its baseline starting at (x, y), as SVG path data ("" for spaces only).</summary>
    public static string Outline(string text, double x, double y, double size, bool bold, bool italic)
    {
        lock (Gate)
        {
            using SKFont font = Font(bold, italic, size);
            using SKPath path = font.GetTextPath(text, new SKPoint((float)x, (float)y));
            return path.ToSvgPathData();
        }
    }
}

/// <summary>The fonts of the figures as files (TrueType), e.g. to embed them in an e-book, and their licence.</summary>
public static class BookFonts
{
    /// <summary>The font files: name ("stix-two-text-400-normal.ttf"), weight (400, 700), italic, content.</summary>
    public static IReadOnlyList<(string Name, int Weight, bool Italic, byte[] Data)> Files()
    {
        (string Name, int Weight, bool Italic)[] fonts =
        [
            ("stix-two-text-400-normal.ttf", 400, false),
            ("stix-two-text-400-italic.ttf", 400, true),
            ("stix-two-text-700-normal.ttf", 700, false),
        ];
        return fonts.Select(font =>
        {
            using Stream stream = typeof(BookFonts).Assembly.GetManifestResourceStream("fonts/" + font.Name)
                ?? throw new InvalidOperationException("Font not found: " + font.Name);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return (font.Name, font.Weight, font.Italic, memory.ToArray());
        }).ToList();
    }
}
