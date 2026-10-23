using System.Text.Encodings.Web;
using System.Text.Json;

namespace GeoAnim.Viewer;

/// <summary>A page of an exported book: its title and its address relative to the page that links to it.</summary>
public sealed record PageLink(string Title, string Href);

/// <summary>
/// The way through an exported book ("geoanim exportdir") for one page: the overview, the page before and the page
/// after it (null at the start and the end). The player shows them in its menu, the next page also below the last step.
/// </summary>
public sealed record PageNavigation(string Index, PageLink? Previous, PageLink? Next)
{
    /// <summary>As JSON for the player (window.GEO_BOOK); &lt;, &gt; and &amp; are escaped, so it fits into a script element.</summary>
    public string ToJson() => JsonSerializer.Serialize(new
    {
        index = Index,
        previous = Previous == null ? null : new { title = Previous.Title, href = Previous.Href },
        next = Next == null ? null : new { title = Next.Title, href = Next.Href },
    }, Options);

    private static readonly JsonSerializerOptions Options = new() { Encoder = JavaScriptEncoder.Default };
}
