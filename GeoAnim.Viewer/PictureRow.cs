using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GeoAnim.Viewer;

/// <summary>
/// A picture from a file in a figure ("![](bilder/halo.jpg)"): the file (full path), what it is ("image/png") and its
/// width by its height (from the file; its DPI do not count); <see cref="Width"/>: "{width=5cm}" at it, or null.
/// </summary>
public sealed record PageImage(string Path, string MediaType, double Ratio, string? Width)
{
    /// <summary>The file in the page (HTML): as a data address, so that the preview and a single HTML file have it.</summary>
    public string DataUri => "data:" + MediaType + ";base64," + Convert.ToBase64String(File.ReadAllBytes(Path));

    /// <summary>The usual extension of its kind (".png").</summary>
    public string Extension => PictureRow.ExtensionOf(MediaType);
}

/// <summary>
/// A row of pictures of the same height – a figure with pictures from files (or "{height=4cm}"): the height from
/// "height" at the figure, else from "width" at one of its pictures, else so that the row fills the width of the text,
/// at most <see cref="MaxHeight"/>. Narrower columns make the row smaller, never larger.
/// </summary>
public static partial class PictureRow
{
    /// <summary>The highest a row gets without a size given: 8 cm (in pt).</summary>
    public const double MaxHeight = 8 / 2.54 * 72;

    /// <summary>Between the pictures, in em of the text (as in page.css).</summary>
    public const double Gap = 0.6;

    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
    };

    /// <summary>A picture file by its address ("bilder/halo.jpg"; not on the web).</summary>
    public static bool IsPicture(string? url) =>
        url != null && !url.Contains("://", StringComparison.Ordinal) && !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        && MediaTypes.ContainsKey(System.IO.Path.GetExtension(url.Split('#', '?')[0]));

    public static string MediaTypeOf(string file) => MediaTypes[System.IO.Path.GetExtension(file)];

    public static string ExtensionOf(string mediaType) => mediaType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        _ => ".svg",
    };

    /// <summary>A length in pt ("4cm", "40mm", "120pt", "2in"), or null (none, or "40%").</summary>
    public static double? Points(string? length)
    {
        if (length == null || LengthPattern().Match(length.Trim()) is not { Success: true } match)
        {
            return null;
        }
        double value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return match.Groups[2].Value switch
        {
            "cm" => value / 2.54 * 72,
            "mm" => value / 25.4 * 72,
            "in" => value * 72,
            _ => value,
        };
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)(cm|mm|pt|in)$")]
    private static partial Regex LengthPattern();

    /// <summary>
    /// The height of the row in pt: <paramref name="height"/> ("4cm"), else from the first picture with a width
    /// (<paramref name="widths"/>, one per picture), else null (it fills the width, at most <see cref="MaxHeight"/>).
    /// </summary>
    public static double? Height(IReadOnlyList<double> ratios, string? height, IReadOnlyList<string?> widths)
    {
        if (Points(height) is double given)
        {
            return given;
        }
        for (int i = 0; i < ratios.Count && i < widths.Count; i++)
        {
            if (Points(widths[i]) is double width && ratios[i] > 0)
            {
                return width / ratios[i];
            }
        }
        return null;
    }

    /// <summary>
    /// The width of the row's element in CSS (its pictures "flex: ratio 1 0" inside, gaps of <see cref="Gap"/> em), in em of
    /// the text: "min(100%, 31.25em)"; <paramref name="textSize"/>: the text's size in pt.
    /// </summary>
    public static string CssWidth(IReadOnlyList<double> ratios, string? height, IReadOnlyList<string?> widths, double textSize)
    {
        double rowHeight = Height(ratios, height, widths) ?? MaxHeight;
        double em = ratios.Sum() * rowHeight / textSize + Gap * Math.Max(0, ratios.Count - 1);
        return "min(100%, " + Math.Round(em, 3).ToString("0.###", CultureInfo.InvariantCulture) + "em)";
    }

    /// <summary>The width by the height of a picture file (PNG, JPEG, GIF, WebP, SVG), or null if it cannot be read.</summary>
    public static double? RatioOf(byte[] data, string mediaType)
    {
        try
        {
            (double width, double height)? size = mediaType switch
            {
                "image/png" => data.Length >= 24 ? (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16)), BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20))) : null,
                "image/gif" => data.Length >= 10 ? (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6)), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8))) : null,
                "image/jpeg" => JpegSize(data),
                "image/webp" => WebpSize(data),
                _ => SvgSize(Encoding.UTF8.GetString(data)),
            };
            return size is { width: > 0, height: > 0 } known ? known.width / known.height : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    // the size in a JPEG's frame header (SOF0 … SOF15, not DHT, JPG, DAC)
    private static (double, double)? JpegSize(byte[] data)
    {
        int i = 2;
        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF)
            {
                return null;
            }
            byte marker = data[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i + 2));
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i + 7)), BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i + 5)));
            }
            i += 2 + length;
        }
        return null;
    }

    private static (double, double)? WebpSize(byte[] data)
    {
        if (data.Length < 30 || Encoding.ASCII.GetString(data, 8, 4) != "WEBP")
        {
            return null;
        }
        return Encoding.ASCII.GetString(data, 12, 4) switch
        {
            "VP8 " => (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(26)) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(28)) & 0x3FFF),
            "VP8L" => (1 + ((data[21] | (data[22] << 8)) & 0x3FFF), 1 + (((data[22] >> 6) | (data[23] << 2) | (data[24] << 10)) & 0x3FFF)),
            "VP8X" => (1 + (data[24] | (data[25] << 8) | (data[26] << 16)), 1 + (data[27] | (data[28] << 8) | (data[29] << 16))),
            _ => null,
        };
    }

    // viewBox, else width and height of the <svg>
    private static (double, double)? SvgSize(string svg)
    {
        Match root = Regex.Match(svg, @"<svg\b[^>]*>", RegexOptions.IgnoreCase);
        if (!root.Success)
        {
            return null;
        }
        Match box = Regex.Match(root.Value, @"viewBox\s*=\s*[""']\s*[-\d.eE]+[\s,]+[-\d.eE]+[\s,]+([\d.eE]+)[\s,]+([\d.eE]+)");
        if (box.Success)
        {
            return (double.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture));
        }
        Match width = Regex.Match(root.Value, @"\bwidth\s*=\s*[""']([\d.]+)"), height = Regex.Match(root.Value, @"\bheight\s*=\s*[""']([\d.]+)");
        return width.Success && height.Success
            ? (double.Parse(width.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(height.Groups[1].Value, CultureInfo.InvariantCulture))
            : null;
    }
}
