using System.Text.Json;
using System.Text.RegularExpressions;
using GeoAnim.Script;

namespace GeoAnim.Tests;

/// <summary>The syntax highlighting (editors/vscode) must know the words of the language.</summary>
public class GrammarTests
{
    private static readonly string Grammar = File.ReadAllText(Repository.PathOf("editors", "vscode", "syntaxes", "geo.tmLanguage.json"));

    [Fact]
    public void GrammarIsValidJsonForGeoFiles()
    {
        JsonElement root = JsonDocument.Parse(Grammar).RootElement;
        Assert.Equal("source.geo", root.GetProperty("scopeName").GetString());
        Assert.Equal("geo", root.GetProperty("fileTypes")[0].GetString());
    }

    [Fact]
    public void GrammarKnowsAllKeywordsAndColours()
    {
        // every word appears in a regular expression of the grammar, as an alternative or a single word
        foreach (string word in Parser.Keywords.Concat(Color.Names.Keys))
        {
            Assert.True(Regex.IsMatch(Grammar, $@"[(|]{Regex.Escape(word)}[)|]|\\\\b{Regex.Escape(word)}\\\\b|\({Regex.Escape(word)}\)"),
                $"The grammar does not know \"{word}\".");
        }
    }
}
