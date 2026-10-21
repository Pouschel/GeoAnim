using System.Globalization;

namespace GeoAnim;

/// <summary>An RGB colour; written as "#rrggbb" or one of the names in <see cref="Names"/>.</summary>
public readonly record struct Color(byte R, byte G, byte B)
{
    /// <summary>The colour names the script language knows.</summary>
    public static IReadOnlyDictionary<string, Color> Names { get; } = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = FromHex(0x222222),
        ["white"] = FromHex(0xffffff),
        ["gray"] = FromHex(0x888888),
        ["grey"] = FromHex(0x888888),
        ["lightgray"] = FromHex(0xd0d0d0),
        ["lightgrey"] = FromHex(0xd0d0d0),
        ["red"] = FromHex(0xd32f2f),
        ["darkred"] = FromHex(0x8e1b1b),
        ["green"] = FromHex(0x2e7d32),
        ["lightgreen"] = FromHex(0xa5d6a7),
        ["blue"] = FromHex(0x1565c0),
        ["lightblue"] = FromHex(0x90caf9),
        ["navy"] = FromHex(0x0d2a6b),
        ["orange"] = FromHex(0xef6c00),
        ["yellow"] = FromHex(0xf9a825),
        ["purple"] = FromHex(0x6a1b9a),
        ["violet"] = FromHex(0x8e24aa),
        ["pink"] = FromHex(0xd81b60),
        ["brown"] = FromHex(0x6d4c41),
        ["teal"] = FromHex(0x00897b),
        ["cyan"] = FromHex(0x00acc1),
        ["magenta"] = FromHex(0xc2185b),
    };

    public static Color FromHex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>Reads "#rrggbb", "#rgb" or a colour name.</summary>
    public static Color Parse(string text)
    {
        if (Names.TryGetValue(text, out Color named))
        {
            return named;
        }
        string hex = text.StartsWith('#') ? text.Substring(1) : string.Empty;
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }
        if (hex.Length == 6 && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            return FromHex(rgb);
        }
        throw new GeoException($"Unknown colour \"{text}\" (use a name like red or blue, or #rrggbb).");
    }

    public string ToHex() => $"#{R:x2}{G:x2}{B:x2}";

    public override string ToString() => ToHex();
}
