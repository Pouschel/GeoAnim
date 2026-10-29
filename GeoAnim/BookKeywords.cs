using GeoAnim.Script;

namespace GeoAnim.Cli;

/// <summary>
/// The keyword links of "geoanim exportdir" for an editor's preview: a script in a book (below a folder with index.yaml)
/// gets the links to the other scripts' keywords, pointing at those scripts (so that a click opens them), and a warning
/// for a keyword that another script has too. The other scripts are only read, not compiled, and remembered until they
/// change.
/// </summary>
internal static class BookKeywords
{
    private static readonly Dictionary<string, (DateTime Changed, List<PageKeyword> Keywords)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The book a script belongs to: the nearest folder above it with index.yaml, or null.</summary>
    public static string? RootOf(string file)
    {
        for (string? folder = Path.GetDirectoryName(Path.GetFullPath(file)); folder != null; folder = Path.GetDirectoryName(folder))
        {
            if (File.Exists(Path.Combine(folder, BookFile.Name)))
            {
                return folder;
            }
        }
        return null;
    }

    /// <summary>The scripts of a book (relative paths with "/") in book order, without those starting with "_".</summary>
    public static List<string> Scripts(string root) =>
        Directory.EnumerateFiles(root, "*.geo", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(relative => !relative.Split('/').Any(part => part.StartsWith('_')))
            .Order(Program.BookOrder.Instance)
            .ToList();

    /// <summary>
    /// The animation of the script <paramref name="file"/> (its text in <paramref name="source"/>) with links to the other
    /// scripts of its book <paramref name="root"/>, and warnings for its keywords that another script has too (file, line,
    /// message; the first script in book order keeps a keyword, as in the export).
    /// </summary>
    public static AnimationData Link(string root, string file, ScriptSource source, AnimationData animation, List<(string? File, int Line, string Message)> warnings)
    {
        string own = Path.GetRelativePath(root, Path.GetFullPath(file)).Replace('\\', '/');
        List<string> scripts = Scripts(root);
        if (!scripts.Contains(own, StringComparer.OrdinalIgnoreCase))
        {
            scripts.Add(own);
            scripts.Sort(Program.BookOrder.Instance);
        }
        string Place(string script, PageKeyword keyword) =>
            (keyword.File == null ? script : Path.GetRelativePath(root, keyword.File).Replace('\\', '/')) + (keyword.Line > 0 ? ":" + keyword.Line : "");
        var pages = new Dictionary<string, (string Script, PageKeyword Keyword)>(StringComparer.Ordinal);
        foreach (string script in scripts)
        {
            bool isOwn = string.Equals(script, own, StringComparison.OrdinalIgnoreCase);
            foreach (PageKeyword keyword in isOwn ? OwnKeywords(source) : Read(Path.Combine(root, script)))
            {
                if (!pages.TryGetValue(keyword.Form, out var other))
                {
                    pages[keyword.Form] = (script, keyword);
                }
                else if (isOwn)
                {
                    warnings.Add((keyword.File, keyword.Line, $"The keyword {keyword.Form} is already the keyword of {Place(other.Script, other.Keyword)} (links go there)."));
                }
                else if (string.Equals(other.Script, own, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add((other.Keyword.File, other.Keyword.Line, $"The keyword {keyword.Form} is also the keyword of {Place(script, keyword)}."));
                }
            }
        }
        var links = pages.Where(p => !string.Equals(p.Value.Script, own, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => p.Key, p => Keywords.RelativeAddress(own, p.Value.Script) + (p.Value.Keyword.Step > 0 ? "#" + p.Value.Keyword.Step : ""),
                StringComparer.Ordinal);
        return Keywords.Link(animation, links);
    }

    private static List<PageKeyword> OwnKeywords(ScriptSource source)
    {
        try
        {
            return ScriptCompiler.KeywordsOf(source);
        }
        catch (ScriptError)
        {
            return [];
        }
    }

    // the keywords of another script, as saved; a script that cannot be read has none
    internal static List<PageKeyword> Read(string path)
    {
        DateTime changed = File.GetLastWriteTimeUtc(path);
        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var known) && known.Changed == changed)
            {
                return known.Keywords;
            }
        }
        List<PageKeyword> keywords;
        try
        {
            keywords = ScriptCompiler.KeywordsOf(ScriptSource.FromFile(path));
        }
        catch (Exception e) when (e is ScriptError or IOException or UnauthorizedAccessException)
        {
            keywords = [];
        }
        lock (Cache)
        {
            Cache[path] = (changed, keywords);
        }
        return keywords;
    }
}
