using System.Reflection;

namespace GeoAnim.Viewer;

/// <summary>The files of the player (folder view/), embedded in this assembly.</summary>
public static class ViewerFiles
{
    private const string Prefix = "view/";
    private static readonly Assembly Assembly = typeof(ViewerFiles).Assembly;

    /// <summary>The paths of all files, relative to view/ and with "/" (e.g. "fonts/fonts.css").</summary>
    public static IReadOnlyList<string> Paths { get; } = Assembly.GetManifestResourceNames()
        .Select(name => name.Replace('\\', '/'))
        .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
        .Select(name => name.Substring(Prefix.Length))
        .Order(StringComparer.Ordinal)
        .ToList();

    /// <summary>The content of a file, or null if there is none with this path.</summary>
    public static byte[]? Read(string path)
    {
        string? name = Assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.Replace('\\', '/') == Prefix + path);
        if (name == null)
        {
            return null;
        }
        using Stream stream = Assembly.GetManifestResourceStream(name)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>The content of a text file.</summary>
    public static string ReadText(string path) =>
        System.Text.Encoding.UTF8.GetString(Read(path) ?? throw new FileNotFoundException("Viewer file not found: " + path));

    /// <summary>The MIME type for a file name.</summary>
    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".woff2" => "font/woff2",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".txt" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
}
