namespace GeoAnim.Viewer;

/// <summary>
/// What an exported book says about itself (index.yaml in its source folder): title and description of the overview,
/// the language of the pages, the address on the web (for canonical links, previews and the sitemap), the way back to
/// the own website, a footer (imprint, privacy) and an icon.
/// </summary>
public sealed record BookSettings
{
    public string? Title { get; init; }

    public string? Description { get; init; }

    /// <summary>The language of the pages ("de"); without it the player's own.</summary>
    public string? Language { get; init; }

    /// <summary>The address of the book on the web, ending with "/" (https://mathepedia.de/geometrie/).</summary>
    public string? Url { get; init; }

    /// <summary>The way back to the own website (absolute address).</summary>
    public PageLink? Home { get; init; }

    /// <summary>A line below the overview and in the menu, with Markdown links.</summary>
    public string? Footer { get; init; }

    /// <summary>The icon file (full path), png or svg.</summary>
    public string? IconFile { get; init; }

    public static BookSettings None { get; } = new();

    /// <summary>The absolute address of a page of the book (path relative to its root, with "/"), or null without <see cref="Url"/>.</summary>
    public string? AddressOf(string page) => Url == null ? null
        : Url + string.Join("/", page.Split('/').Select(part => Uri.EscapeDataString(part))).Replace("index.html", "", StringComparison.Ordinal);
}

/// <summary>
/// The head of a page in a book: its language, its icon (an address or a data: URL), its address on the web (canonical
/// link and preview) and the name of the book (for previews).
/// </summary>
public sealed record PageHead(string? Language = null, string? Icon = null, string? Address = null, string? SiteName = null);
