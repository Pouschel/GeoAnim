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
            ["fonts/LICENSE-lato.txt", "fonts/fonts.css", "fonts/lato-latin-400-normal.woff2", "fonts/lato-latin-700-normal.woff2",
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
        Assert.Equal(2, Regex.Matches(html, "url\\(data:font/woff2;base64,").Count);
        // the animation, readable again
        Match data = Regex.Match(html, "window.GEO_ANIMATION = (.*?);</script>", RegexOptions.Singleline);
        Assert.Equal(animation.ToJson(), data.Groups[1].Value);
        Assert.Contains("function valueAt", html);
        Assert.True(html.Length < 200_000, "size " + html.Length);
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
