using GeoAnim.Viewer;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace GeoAnim.Cli;

/// <summary>
/// index.yaml in the source folder of a book: what the book says about itself (see <see cref="BookSettings"/>).
/// <code>
/// title: Ebene Geometrie Schritt für Schritt
/// description: Von Punkten und Strecken bis zu den Kongruenzsätzen.
/// icon: icons/winkel.svg            # relative to index.yaml; png or svg
/// language: de
/// url: https://mathepedia.de/geometrie/
/// home:
///   title: mathepedia.de
///   url: https://mathepedia.de/
/// footer: © … · [Impressum](https://mathepedia.de/impressum)
/// </code>
/// </summary>
internal static class BookFile
{
    public const string Name = "index.yaml";

    private static readonly string[] Keys = ["title", "description", "icon", "language", "url", "home", "footer"];

    /// <summary>
    /// The settings of the book in <paramref name="root"/>: none without index.yaml. Unknown keys and a missing icon are
    /// warnings; a file that is no YAML is an error (<paramref name="error"/>), and the book is exported without it.
    /// </summary>
    public static BookSettings Read(string root, Action<string> warn, out string? error)
    {
        error = null;
        string path = Path.Combine(root, Name);
        if (!File.Exists(path))
        {
            return BookSettings.None;
        }
        YamlMappingNode map;
        try
        {
            var stream = new YamlStream();
            using (var reader = new StreamReader(path))
            {
                stream.Load(reader);
            }
            if (stream.Documents.Count == 0)
            {
                return BookSettings.None;
            }
            map = stream.Documents[0].RootNode as YamlMappingNode
                ?? throw new YamlException("The file must consist of entries \"key: value\".");
        }
        catch (YamlException e)
        {
            error = $"{Name}: {e.Message} (line {e.Start.Line}).";
            return BookSettings.None;
        }
        string? Text(string key)
        {
            if (!map.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? node))
            {
                return null;
            }
            if (node is YamlScalarNode scalar && !string.IsNullOrWhiteSpace(scalar.Value))
            {
                return scalar.Value.Trim();
            }
            warn($"{Name}: \"{key}\" needs a text.");
            return null;
        }
        foreach (YamlNode key in map.Children.Keys)
        {
            if (key is YamlScalarNode { Value: { } name } && !Keys.Contains(name))
            {
                warn($"{Name}: unknown entry \"{name}\" (possible: {string.Join(", ", Keys)}).");
            }
        }
        PageLink? home = null;
        if (map.Children.TryGetValue(new YamlScalarNode("home"), out YamlNode? homeNode))
        {
            if (homeNode is YamlMappingNode homeMap
                && homeMap.Children.TryGetValue(new YamlScalarNode("url"), out YamlNode? homeUrl) && homeUrl is YamlScalarNode { Value: { } address })
            {
                string title = homeMap.Children.TryGetValue(new YamlScalarNode("title"), out YamlNode? homeTitle) && homeTitle is YamlScalarNode { Value: { } t }
                    ? t
                    : address;
                home = new PageLink(title.Trim(), address.Trim());
            }
            else
            {
                warn($"{Name}: \"home\" needs \"url\" (and \"title\") below it.");
            }
        }
        string? icon = Text("icon");
        string? iconFile = null;
        if (icon != null)
        {
            iconFile = Path.GetFullPath(Path.Combine(root, icon));
            if (!File.Exists(iconFile))
            {
                warn($"{Name}: the icon {icon} is not there ({iconFile}).");
                iconFile = null;
            }
            else if (Path.GetExtension(iconFile).ToLowerInvariant() is not (".svg" or ".png" or ".ico"))
            {
                warn($"{Name}: the icon {icon} should be an svg, png or ico file.");
            }
        }
        string? url = Text("url");
        if (url != null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https"))
            {
                warn($"{Name}: \"url\" must be a whole web address like https://mathepedia.de/geometrie/.");
                url = null;
            }
            else if (!url.EndsWith('/'))
            {
                url += "/";
            }
        }
        return new BookSettings
        {
            Title = Text("title"),
            Description = Text("description"),
            Language = Text("language"),
            Url = url,
            Home = home,
            Footer = Text("footer"),
            IconFile = iconFile,
        };
    }
}
