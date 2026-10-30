using System.Globalization;
using System.Text.RegularExpressions;
using GeoAnim.Cli;
using GeoAnim.Figures;
using GeoAnim.Script;

namespace GeoAnim.Tests;

public sealed class FigureTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("geoanim-figure").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private static FigureAnimation Animation(string script) => FigureAnimation.Parse(ScriptCompiler.Compile(script).ToJson());

    private static double Number(string svg, string attribute) =>
        double.Parse(Regex.Match(svg, attribute + "=\"([0-9.]+)pt\"").Groups[1].Value, CultureInfo.InvariantCulture);

    [Fact]
    public void LengthsAreReadInPoints()
    {
        Assert.Equal(72, FigureStyle.ParseLength("1in"));
        Assert.Equal(9, FigureStyle.ParseLength("9pt"));
        Assert.Equal(9, FigureStyle.ParseLength("9"));
        Assert.Equal(FigureStyle.Millimetres(5), FigureStyle.ParseLength("0.5cm")!.Value, 6);
        Assert.Null(FigureStyle.ParseLength("5 Meter"));
        Assert.Null(FigureStyle.ParseLength("-5mm"));
    }

    [Fact]
    public void TheWholeViewHasItsSizeInPoints()
    {
        FigureAnimation animation = Animation("view x -5 .. 5 y -2 .. 2\npoint A = 0 0\nstep Eins");
        FigureImage image = FigureRenderer.Render(animation, animation.EndOf(0), new FigureStyle(10, 0.25));
        Assert.Equal(100, image.Width, 6);
        Assert.Equal(40, image.Height, 6);
        Assert.Contains("width=\"100pt\" height=\"40pt\" viewBox=\"0 0 100 40\"", image.Svg);
        // everything is a path: no text, no font
        Assert.DoesNotContain("<text", image.Svg);
        Assert.Contains("<path", image.Svg);
    }

    [Fact]
    public void AFigureIsCutToWhatItShowsWithAMargin()
    {
        FigureAnimation animation = Animation("view x -10 .. 10 y -10 .. 10\npoint A = 0 0 nolabel\npoint B = 4 0 nolabel\nsegment s = A B\nstep Eins");
        var style = new FigureStyle(10, 0.25);
        double end = animation.EndOf(0);
        FigureImage image = FigureRenderer.Render(animation, end, style, [end]);
        // 4 units wide plus the points and a margin of at least 16 sizes (4 pt) on each side
        Assert.InRange(image.Width, 40 + 8, 40 + 20);
        Assert.InRange(image.Height, 8, 20);
        Assert.True(image.Width < FigureRenderer.Render(animation, end, style).Width);
    }

    [Fact]
    public void RaysAndLinesDoNotWidenTheFigure()
    {
        FigureAnimation animation = Animation("view x -10 .. 10 y -10 .. 10\npoint A = 0 0 nolabel\npoint B = 2 0 nolabel\nline g = A B\nstep Eins");
        double end = animation.EndOf(0);
        FigureImage image = FigureRenderer.Render(animation, end, new FigureStyle(10, 0.25), [end]);
        Assert.True(image.Width < 100, $"{image.Width} pt: the line widened the figure");
    }

    [Fact]
    public void TheStillsOfARowAreCutAlike()
    {
        FigureAnimation animation = Animation("view x -10 .. 10 y -10 .. 10\nstep Eins\npoint A = 0 0 nolabel\nstep Zwei\npoint B = 6 3 nolabel");
        var style = new FigureStyle(10, 0.25);
        double[] times = [animation.EndOf(0), animation.EndOf(1)];
        FigureImage first = FigureRenderer.Render(animation, times[0], style, times);
        FigureImage second = FigureRenderer.Render(animation, times[1], style, times);
        Assert.Equal(first.Width, second.Width);
        Assert.Equal(first.Height, second.Height);
        Assert.True(first.Width > 60);
        // alone, the first would be small
        Assert.True(FigureRenderer.Render(animation, times[0], style, [times[0]]).Width < 20);
    }

    [Fact]
    public void LabelsGrowWithTheSizeFactor()
    {
        FigureAnimation animation = Animation("view x -10 .. 10 y -10 .. 10\ntext t = \"Kreis um $M$\" at 0 0\nstep Eins");
        double end = animation.EndOf(0);
        double small = FigureRenderer.Render(animation, end, new FigureStyle(10, 0.2), [end]).Width;
        double large = FigureRenderer.Render(animation, end, new FigureStyle(10, 0.4), [end]).Width;
        Assert.InRange(large / small, 1.8, 2.2);
    }

    [Fact]
    public void TheFigureCommandTakesItsSizeFromTheBook()
    {
        File.WriteAllText(Path.Combine(folder, "index.yaml"), "title: Buch\nprint:\n  scale: 1cm\n  labels: 10pt\n  labelsize: 20\n");
        string chapter = Directory.CreateDirectory(Path.Combine(folder, "10_Kapitel")).FullName;
        FigureStyle style = Program.StyleOf(chapter);
        Assert.Equal(FigureStyle.Millimetres(10), style.PointsPerUnit, 6);
        Assert.Equal(0.5, style.SizeFactor, 6);
        Assert.Equal(FigureStyle.Default, Program.StyleOf(Directory.CreateTempSubdirectory("geoanim-nobook").FullName));
    }

    [Fact]
    public void TheFigureCommandWritesStillsAndRows()
    {
        string script = Path.Combine(folder, "a.geo");
        File.WriteAllText(script, "view x -5 .. 5 y -5 .. 5\npoint A = 0 0\nstep Eins #eins\npoint B = 2 2\nstep Zwei\npoint C = -2 2\nstep Drei");
        string output = Path.Combine(folder, "bild.svg");
        Assert.Equal(0, Program.Figure([script + "#eins", "-o", output]));
        Assert.True(File.Exists(output));
        Assert.Equal(0, Program.Figure([script + "#2..3", "-o", output]));
        string second = File.ReadAllText(Path.Combine(folder, "bild-2.svg"));
        string third = File.ReadAllText(Path.Combine(folder, "bild-3.svg"));
        Assert.Equal(Number(second, "width"), Number(third, "width"));
        Assert.Equal(0, Program.Figure([script]));
        Assert.True(File.Exists(Path.Combine(folder, "a.svg")));
        Assert.Equal(1, Program.Figure([script + "#vier"]));
    }

    // width, height and colour type of a PNG (2: RGB without alpha)
    private static (int Width, int Height, int ColorType) PngHeader(byte[] png)
    {
        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], png[..8]);
        int Int(int at) => png[at] << 24 | png[at + 1] << 16 | png[at + 2] << 8 | png[at + 3];
        return (Int(16), Int(20), png[25]);
    }

    [Fact]
    public void AFigureAsPngHasItsSizeAtTheResolutionAndNoTransparency()
    {
        FigureAnimation animation = Animation("view x -5 .. 5 y -2 .. 2\npoint A = 0 0\nstep Eins");
        FigureImage image = FigureRenderer.Render(animation, animation.EndOf(0), new FigureStyle(7.2, 0.25));
        (int width, int height, int colorType) = PngHeader(FigurePng.Render(image, 300));
        Assert.Equal(300, width);
        Assert.Equal(120, height);
        Assert.Equal(2, colorType);
    }

    [Fact]
    public void AFormulaIsAPictureWithItsDepthAndItsSourceAsAlternativeText()
    {
        FormulaImage formula = FormulaImage.Render("x_1^2", 10);
        Assert.Equal("x_1^2", formula.AltText);
        Assert.Contains("aria-label=\"x_1^2\"", formula.Svg);
        Assert.DoesNotContain("currentColor", formula.Svg);
        // about a font size high, partly below the baseline (the index)
        Assert.InRange(formula.Height, 8, 20);
        Assert.InRange(formula.Depth, 1, formula.Height / 2);
        Assert.Equal(2, formula.Width / FormulaImage.Render("x_1^2", 5).Width, 3);
        (int width, int _, int colorType) = PngHeader(formula.Png(720));
        Assert.Equal((int)Math.Ceiling(formula.Width * 10), width);
        Assert.Equal(2, colorType);
    }

    [Fact]
    public void TheCommandsWritePngs()
    {
        string script = Path.Combine(folder, "a.geo");
        File.WriteAllText(script, "view x -5 .. 5 y -5 .. 5\npoint A = 0 0\nstep Eins");
        Assert.Equal(0, Program.Figure([script, "-png", "-dpi", "150"]));
        PngHeader(File.ReadAllBytes(Path.Combine(folder, "a.png")));
        string formula = Path.Combine(folder, "f.png");
        Assert.Equal(0, Program.Formula(["abs(A-B)", "-o", formula, "-size", "12pt"]));
        PngHeader(File.ReadAllBytes(formula));
        Assert.Throws<ArgumentException>(() => Program.Figure([script, "-dpi", "1"]));
    }
}

public sealed class EpubTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("geoanim-epub").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private void Write(string name, string content)
    {
        string path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Read(System.IO.Compression.ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void TheBookBecomesAnEpubWithPicturesAndLinksBetweenItsPages()
    {
        Write("index.yaml", "title: Buch\nauthor: Thomas\nlanguage: de\n");
        Write("10_Kapitel/10_Erste.geo", "view x -5 .. 5 y -5 .. 5\nstep Eins #eins\npoint A = 0 0\nstep Zwei\npoint B = 2 2");
        Write("10_Kapitel/10_Erste.geo.md", "# Erste Seite\n\nDer **Punkt**{kw} $A$ und $$x^2$$.\n\n![Ein Punkt](#eins)\n\n![](#1..2)\n\n::: axiom {#ax} Eins\nText.\n:::\n");
        Write("10_Kapitel/20_Zweite.geo.md", "# Zweite Seite\n\nNach §ax gilt es für jeden Punkt.\n");
        string output = Path.Combine(folder, "buch.epub");
        Assert.Equal(0, Program.Epub([folder, "-o", output]));
        using System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(output);
        // the mimetype first and stored
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength);
        string first = Read(zip, "OEBPS/text/p01-Erste.xhtml");
        string second = Read(zip, "OEBPS/text/p02-Zweite.xhtml");
        // well-formed XHTML
        System.Xml.Linq.XDocument.Parse(first);
        System.Xml.Linq.XDocument.Parse(second);
        Assert.DoesNotContain("<svg", first);
        Assert.DoesNotContain("data-line", first);
        Assert.Contains("alt=\"A\" class=\"formula\"", first);
        Assert.Matches("<img src=\"../images/a-[0-9a-f]+.png\" alt=\"Abb. 1.1 Ein Punkt\"", first);
        // the row of two steps: two pictures, the still one
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(first, "images/a-").Count);
        // as wide as the page; two side by side share it
        Assert.Contains("style=\"width: 100%; margin-left: 0; margin-right: 0\"", first);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(first, "style=\"width: 49%\"").Count);
        // references and keywords lead to the other file
        Assert.Contains("href=\"p01-Erste.xhtml#ax\"", second);
        Assert.Matches("<a href=\"p01-Erste.xhtml#[^\"]+\"[^>]*>Punkt</a>", second);
        string package = Read(zip, "OEBPS/content.opf");
        Assert.Contains("<dc:creator>Thomas</dc:creator>", package);
        Assert.Contains("properties=\"nav\"", package);
        Assert.Contains("Kapitel", Read(zip, "OEBPS/nav.xhtml"));
        Assert.NotNull(zip.GetEntry("OEBPS/toc.ncx"));
        Assert.NotNull(zip.GetEntry("OEBPS/fonts/stix-two-text-400-normal.ttf"));
    }

    [Fact]
    public void AFolderWithoutIndexYamlIsNoBook()
    {
        Write("a.geo.md", "# A");
        Assert.Equal(1, Program.Epub([folder]));
    }
}

public sealed class TypstTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("geoanim-typst").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private void Write(string name, string content)
    {
        string path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void TheBookBecomesATypstProject()
    {
        Write("index.yaml", "title: Buch\nauthor: Thomas\n");
        Write("10_Kapitel/10_Erste.geo", "view x -5 .. 5 y -5 .. 5\nstep Eins #eins\npoint A = 0 0\nstep Zwei\npoint B = 2 2");
        Write("10_Kapitel/10_Erste.geo.md", "# Erste Seite\n\nDer **Punkt**{kw} $A$ hat *Stern* a_b_c # und [x].\n\n1. eins\n2. zwei\n\n![Ein Punkt](#eins){#abb-punkt}\n\n::: axiom {#ax} Eins\nText.\n:::\n\n::: proof\nKlar.\n:::\n");
        Write("10_Kapitel/20_Zweite.geo.md", "# Zweite Seite\n\nNach §ax und §abb-punkt gilt es für jeden Punkt.[^1]\n\n[^1]: Eine Fußnote.\n");
        string output = Path.Combine(folder, "out");
        Assert.Equal(0, Program.Typst([folder, "-o", output]));

        string main = File.ReadAllText(Path.Combine(output, "book.typ"));
        Assert.Contains("#show: book.with(title: \"Buch\", author: \"Thomas\", text-size: 11pt, language: \"de\")", main);
        Assert.Contains("#chapter(\"1\", [Kapitel])", main);
        Assert.Contains("#include \"pages/p01-Erste.typ\"", main);
        Assert.True(File.Exists(Path.Combine(output, "style.typ")));
        string first = File.ReadAllText(Path.Combine(output, "pages", "p01-Erste.typ"));
        Assert.Contains("#heading(level: 2)[#section-number[1.1] Erste Seite]", first);
        // text escaped, formulas and figures as pictures
        Assert.Contains("#emph[Stern] a\\_b\\_c \\# und \\[x\\]", first);
        Assert.Matches(@"#box\(baseline: [-0-9.]+em, height: [0-9.]+em, move\(dy: -0.04em, image\(""../formulas/[0-9a-f]+.svg""", first);
        Assert.Matches(@"#geo-figure\(image\(""../figures/[0-9a-f]+.svg""\), caption: \[#figure-label\[Abb. 1.1\] Ein Punkt\]\) <p1-abb-punkt>", first);
        Assert.Contains("#statement(\"axiom\")[#statement-label[Axiom 1 (Eins).] Text.] <p1-ax>", first);
        Assert.Contains("#statement(\"proof\")[#statement-label[Beweis.] Klar. #qed()]", first);
        Assert.Contains("#enum([eins], [zwei])", first);
        Assert.Contains("#strong[Punkt] <p1-kw-punkt>", first);
        string second = File.ReadAllText(Path.Combine(output, "pages", "p02-Zweite.typ"));
        Assert.Contains("#link(<p1-ax>)[Axiom 1]", second);
        Assert.Contains("#link(<p1-abb-punkt>)[Abb. 1.1]", second);
        Assert.Matches(@"#link\(<p1-kw-punkt>\)\[Punkt\]\.#footnote\[Eine Fußnote\.\]", second);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(output, "figures"), "*.svg"));

        // the book's own look
        Write("style.typ", "// mein Stil\n");
        Assert.Equal(0, Program.Typst([folder, "-o", output]));
        Assert.Equal("// mein Stil\n", File.ReadAllText(Path.Combine(output, "style.typ")));
    }
}
