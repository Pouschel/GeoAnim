using System.Text.Json;
using GeoAnim.Formulas;
using GeoAnim.Script;

namespace GeoAnim.Tests;

public class FormulaTests
{
    [Fact]
    public void TextsAreSplitIntoTextAndFormulas()
    {
        Assert.Equal(
            [new TextRun("Kreis um ", false), new TextRun("A", true), new TextRun(" mit Radius ", false), new TextRun("|AB|", true)],
            MathText.Split("Kreis um $A$ mit Radius $ |AB| $"));
        Assert.Equal([new TextRun("5 $ und 3 $", false)], MathText.Split("5 \\$ und 3 \\$"));
        Assert.False(MathText.HasFormula("kein \\$ Geld"));
        Assert.Contains("character 4 has no closing $", Assert.Throws<FormatException>(() => MathText.Split("ab $x")).Message);
        Assert.Contains("empty", Assert.Throws<FormatException>(() => MathText.Split("a $ $ b")).Message);
    }

    [Fact]
    public void FormulasBecomeSvgPaths()
    {
        Formula formula = MathRenderer.Render("|AB|");
        // MathJax units: thousandths of an em, the baseline at y = 0
        Assert.True(formula.Y < 0 && formula.Height > -formula.Y && formula.Width > 1000);
        Assert.Contains("<path", formula.Svg);
        Assert.DoesNotContain("data-", formula.Svg);
        Assert.Contains("currentColor", formula.Svg);
        Assert.Same(formula, MathRenderer.Render("|AB|"));
        // characters from the fonts loaded on demand
        Assert.Contains("<path", MathRenderer.Render("NN sub RR").Svg);
    }

    [Fact]
    public void AnimationCarriesTheFormulasOfAllTexts()
    {
        AnimationData animation = ScriptCompiler.Compile("""
            title Satz über $alpha$
            point P_1 = 1 1
            point Q = 2 2 label "$beta'$" offset 0.1 0.1
            point R = 3 3 nolabel
            text t = "Winkel $gamma$" at 5 5
            step Kreis mit Radius $|AB|$ für 5 \$
            """);
        Assert.Equal(["P_1", "alpha", "beta'", "gamma", "|AB|"], animation.Formulas.Keys.Order(StringComparer.Ordinal));
        JsonElement math = JsonDocument.Parse(animation.ToJson()).RootElement.GetProperty("math");
        Assert.Equal(4, math.GetProperty("|AB|").GetProperty("box").GetArrayLength());
        Assert.StartsWith("<", math.GetProperty("alpha").GetProperty("svg").GetString());
        Assert.Equal("Kreis mit Radius $|AB|$ für 5 \\$", animation.Steps[0].Caption);
    }

    [Fact]
    public void IncompleteFormulasAreErrorsWithTheirLine()
    {
        Assert.Equal("Line 2, column 1: The formula at character 7 has no closing $ (write \\$ for a dollar sign).",
            Assert.Throws<ScriptError>(() => ScriptCompiler.Compile("point A = 0 0\nstep Kreis $k")).ToString());
        Assert.Contains("no closing $", Assert.Throws<ScriptError>(() => ScriptCompiler.Compile("text t = \"a $b\" at 1 1")).Message);
        Assert.Contains("no closing $", Assert.Throws<ScriptError>(() => ScriptCompiler.Compile("point A = 0 0 label \"$A\"")).Message);
        Assert.Contains("no closing $", Assert.Throws<ScriptError>(() => ScriptCompiler.Compile("title Preis 5$")).Message);
    }
}
