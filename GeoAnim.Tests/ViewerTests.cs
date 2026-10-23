using System.Text.Json;
using System.Text.RegularExpressions;
using GeoAnim.Script;
using GeoAnim.Viewer;

namespace GeoAnim.Tests;

public class ViewerTests
{
    [Fact]
    public void PlayerFilesAreEmbedded()
    {
        Assert.Equal(
            ["fonts/LICENSE-stix-two-text.txt", "fonts/fonts.css", "fonts/stix-two-text-latin-400-italic.woff2",
             "fonts/stix-two-text-latin-400-normal.woff2", "fonts/stix-two-text-latin-700-normal.woff2",
             "player.css", "player.html", "player.js"],
            ViewerFiles.Paths);
        Assert.Null(ViewerFiles.Read("missing.js"));
    }

    [Fact]
    public void ExportedFileContainsEverything()
    {
        AnimationData animation = ScriptCompiler.Compile(File.ReadAllText(Repository.PathOf("Samples", "circumcircle.geo")));
        string html = HtmlExporter.Export(animation);

        Assert.Contains("<title>Umkreis eines Dreiecks</title>", html);
        // nothing is loaded from elsewhere
        string markup = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", string.Empty, RegexOptions.Singleline);
        Assert.DoesNotMatch("""\s(src|href)\s*=""", markup);
        foreach (Match style in Regex.Matches(html, "<style>(.*?)</style>", RegexOptions.Singleline))
        {
            Assert.DoesNotMatch("""url\((?!data:)""", style.Groups[1].Value);
        }
        Assert.Equal(3, Regex.Matches(html, "url\\(data:font/woff2;base64,").Count);
        // the animation, readable again
        Match data = Regex.Match(html, "window.GEO_ANIMATION = (.*?);</script>", RegexOptions.Singleline);
        Assert.Equal(animation.ToJson(), data.Groups[1].Value);
        Assert.Contains("function valueAt", html);
        Assert.True(html.Length < 200_000, "size " + html.Length);
    }

    [Fact]
    public void PageTitleIsPlainText()
    {
        Assert.Equal("Satz I.4 über Dreiecke, k_1 und a",
            HtmlExporter.PlainText("Satz **I.4** über *Dreiecke*, `k_1` und $a$"));
        Assert.Equal("siehe Satz 1 und Prop2.html, 5 $", HtmlExporter.PlainText("siehe [Satz 1](Prop1.html) und <Prop2.html>, 5 \\$"));
        Assert.Contains("<title>Satz I.4</title>", HtmlExporter.Export(new Construction { Title = "Satz **I.4**" }.Build()));
    }

    [Fact]
    public void PagesDescribeThemselves()
    {
        AnimationData auto = ScriptCompiler.Compile("""
            title Satz
            point A = 0 0
            step Gegeben ist das Dreieck $Delta ABC$
            step Zeichne um $B$ einen Kreis mit dem Radius $bar(BC)$:
            step Die Winkel $alpha$ und $beta$ sind **gleich** ("<Prop5.html>")
            """);
        string html = HtmlExporter.Export(auto);
        Assert.Contains("<meta name=\"description\" content=\"Gegeben ist das Dreieck ΔABC. Zeichne um B einen Kreis mit dem Radius BC: "
            + "Die Winkel α und β sind gleich (&quot;Prop5.html&quot;).\">", html);
        // given in the script, over several lines; a long one is cut at a word
        AnimationData given = ScriptCompiler.Compile("description Euklid I.2: eine Strecke\n| antragen\npoint A = 0 0");
        Assert.Contains("content=\"Euklid I.2: eine Strecke antragen\"", HtmlExporter.Export(given));
        // it is also shown on the starting picture (only one from the script), with its formulas
        Assert.Equal("Euklid I.2: eine Strecke antragen", JsonDocument.Parse(given.ToJson()).RootElement.GetProperty("description").GetString());
        Assert.False(JsonDocument.Parse(auto.ToJson()).RootElement.TryGetProperty("description", out _));
        Assert.True(ScriptCompiler.Compile("description Um $M$\npoint A = 0 0").Formulas.ContainsKey("M"));
        Assert.Contains("no closing $", Assert.Throws<ScriptError>(() => ScriptCompiler.Compile("description Um $M")).Message);
        string longOne = HtmlExporter.DescriptionOf(ScriptCompiler.Compile("description " + string.Join(" ", Enumerable.Repeat("Wort", 100))));
        Assert.True(longOne.Length <= HtmlExporter.DescriptionLength + 2 && longOne.EndsWith("Wort …"), longOne);
        Assert.Contains("only possible", Assert.Throws<ScriptError>(() => ScriptCompiler.Compile("point A = 0 0\nstep s\ndescription x")).Message.Replace("must come before the first step", "only possible"));
    }

    [Fact]
    public void TitleIsEscaped()
    {
        var c = new Construction { Title = "A < B & C" };
        string html = HtmlExporter.Export(c.Build());
        Assert.Contains("<title>A &lt; B &amp; C</title>", html);
        Assert.Equal("A < B & C", JsonDocument.Parse(Regex.Match(html, "window.GEO_ANIMATION = (.*?);</script>").Groups[1].Value)
            .RootElement.GetProperty("title").GetString());
    }
}
