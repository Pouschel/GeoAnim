using System.Globalization;
using GeoAnim.Figures;
using GeoAnim.Viewer;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace GeoAnim.Cli;

/// <summary>
/// index.yaml in the source folder of a book: what the book says about itself (see <see cref="BookSettings"/>).
/// <code>
/// title: Ebene Geometrie Schritt für Schritt
/// author: Thomas Steinfeld              # for the e-book
/// description: Von Punkten und Strecken bis zu den Kongruenzsätzen.
/// icon: icons/winkel.svg            # relative to index.yaml; png or svg
/// language: de
/// url: https://mathepedia.de/geometrie/
/// home:
///   title: mathepedia.de
///   url: https://mathepedia.de/
/// footer: © … · [Impressum](https://mathepedia.de/impressum)
/// print:                            # the figures of the book (E-book, print)
///   scale: 5mm                      # a coordinate unit (mm, cm, pt, in)
///   labels: 9pt                     # the font size of the labels …
///   labelsize: 34                   # … of this size in the scripts (all sizes in the same ratio)
///   text: 11pt                      # the font size of the text the figures go with
/// numbering:                        # how figures, statements and headings are numbered (these are the defaults)
///   within: chapter                 # counters start again in each chapter: Satz 2.14 (book: Satz 47; page: Satz 2.1.3)
///   axiom: own book                 # a counter of its own, through the book: Axiom 5 ("own" alone: the same for axioms)
///   figure: own                     # a counter of its own: Abb. 2.3
///   example: own                    # Beispiel 2.1
///   equation: own                   # formulas with a mark: (2.1)
///   default: shared                 # all other numbered statements one counter: Definition 2.1, Satz 2.2, …
///   headings: yes                   # chapters and pages numbered: 2 Abstandsgeometrie, 2.1 Abstandsaxiome
/// </code>
/// </summary>
internal static class BookFile
{
    public const string Name = "index.yaml";

    private static readonly string[] Keys = ["title", "author", "description", "icon", "language", "url", "home", "footer", "print", "numbering"];

    private static readonly string[] PrintKeys = ["scale", "labels", "labelsize", "text"];

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
        (double? figureUnit, double? figureSize, double? textSize) = ReadPrint(map, warn);
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
            FigureUnit = figureUnit,
            FigureSize = figureSize,
            TextSize = textSize,
            Author = Text("author"),
            Numbering = ReadNumbering(map, warn),
        };
    }

    private static readonly string[] NumberingKeys = ["within", "default", "headings", .. BookNumbering.Kinds, "def"];

    // "numbering:": the defaults changed by what it says
    private static BookNumbering ReadNumbering(YamlMappingNode map, Action<string> warn)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode("numbering"), out YamlNode? node))
        {
            return BookNumbering.Default;
        }
        if (node is not YamlMappingNode numbering)
        {
            warn($"{Name}: \"numbering\" needs entries below it like \"axiom: own book\".");
            return BookNumbering.Default;
        }
        BookNumbering result = BookNumbering.Default;
        var own = new Dictionary<string, NumberScope?>(result.Own);
        static NumberScope? ScopeOf(string word) => word switch
        {
            "book" => NumberScope.Book,
            "chapter" => NumberScope.Chapter,
            "page" => NumberScope.Page,
            _ => null,
        };
        foreach ((YamlNode keyNode, YamlNode valueNode) in numbering.Children)
        {
            string key = (keyNode as YamlScalarNode)?.Value ?? "";
            string value = ((valueNode as YamlScalarNode)?.Value ?? "").Trim().ToLowerInvariant();
            string[] words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            key = key == "def" ? "definition" : key;
            if (!NumberingKeys.Contains(key))
            {
                warn($"{Name}: unknown entry \"numbering: {key}\" (possible: {string.Join(", ", NumberingKeys.Where(k => k != "def"))}).");
                continue;
            }
            switch (key)
            {
                case "within":
                    if (ScopeOf(value) is { } within)
                    {
                        result = result with { Within = within };
                    }
                    else
                    {
                        warn($"{Name}: \"numbering: within\" is book, chapter or page, not \"{value}\".");
                    }
                    break;
                case "headings":
                    if (value is "yes" or "true" or "no" or "false" || int.TryParse(value, out _))
                    {
                        result = result with { Headings = value is "yes" or "true" || (int.TryParse(value, out int levels) && levels > 0) };
                    }
                    else
                    {
                        warn($"{Name}: \"numbering: headings\" is yes or no, not \"{value}\".");
                    }
                    break;
                default:
                    // "own", "own book", "shared", "shared chapter" (the shared counter's scope: only with "default")
                    NumberScope? scope = words.Length > 1 ? ScopeOf(words[1]) : null;
                    if (words.Length is 0 or > 2 || words[0] is not ("own" or "shared") || (words.Length == 2 && scope == null)
                        || (key == "default" && words[0] == "own"))
                    {
                        warn($"{Name}: \"numbering: {key}\" is " + (key == "default" ? "shared (and book, chapter or page)" : "own or shared (own with book, chapter or page)")
                            + $", not \"{value}\".");
                        break;
                    }
                    if (key == "default")
                    {
                        result = result with { SharedScope = scope };
                    }
                    else if (words[0] == "own")
                    {
                        // without a scope the kind's standard one (axioms: through the book)
                        own[key] = scope ?? BookNumbering.Default.Own.GetValueOrDefault(key);
                    }
                    else
                    {
                        if (scope != null)
                        {
                            warn($"{Name}: \"numbering: {key}: shared\" counts with the others; their scope is set by \"default\".");
                        }
                        own.Remove(key);
                    }
                    break;
            }
        }
        return result with { Own = own };
    }

    // "print:" with "scale: 5mm", "labels: 9pt", "labelsize: 34" and "text: 11pt": points per unit, per size of the scripts
    // and the font size of the text
    private static (double? Unit, double? Size, double? Text) ReadPrint(YamlMappingNode map, Action<string> warn)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode("print"), out YamlNode? node))
        {
            return (null, null, null);
        }
        if (node is not YamlMappingNode print)
        {
            warn($"{Name}: \"print\" needs \"scale\", \"labels\", \"labelsize\" and \"text\" below it.");
            return (null, null, null);
        }
        string? Value(string key) =>
            print.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) && value is YamlScalarNode { Value: { } text } ? text.Trim() : null;
        foreach (YamlNode key in print.Children.Keys)
        {
            if (key is YamlScalarNode { Value: { } name } && !PrintKeys.Contains(name))
            {
                warn($"{Name}: unknown entry \"print: {name}\" (possible: {string.Join(", ", PrintKeys)}).");
            }
        }
        double? unit = null, size = null;
        if (Value("scale") is { } scale)
        {
            unit = FigureStyle.ParseLength(scale);
            if (unit == null)
            {
                warn($"{Name}: \"print: scale\" must be a length like 5mm (mm, cm, pt or in).");
            }
        }
        double? labels = null;
        if (Value("labels") is { } labelText)
        {
            labels = FigureStyle.ParseLength(labelText);
            if (labels == null)
            {
                warn($"{Name}: \"print: labels\" must be a font size like 9pt.");
            }
        }
        double labelSize = 34;
        if (Value("labelsize") is { } sizeText
            && !(double.TryParse(sizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out labelSize) && labelSize > 0))
        {
            warn($"{Name}: \"print: labelsize\" must be a size of the scripts like 34.");
            labelSize = 34;
        }
        if (labels != null)
        {
            size = labels / labelSize;
        }
        double? text = null;
        if (Value("text") is { } textSize)
        {
            text = FigureStyle.ParseLength(textSize);
            if (text == null)
            {
                warn($"{Name}: \"print: text\" must be a font size like 11pt.");
            }
        }
        return (unit, size, text);
    }
}
