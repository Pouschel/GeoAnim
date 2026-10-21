using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Jint;
using Jint.Native;

namespace GeoAnim.Formulas;

/// <summary>
/// A formula as SVG: the content of an svg element (paths, filled with currentColor) and its viewBox in thousandths of
/// an em, with y downwards and the baseline at y = 0 (so <see cref="Y"/> is negative: the height above the baseline).
/// </summary>
public sealed record Formula(double X, double Y, double Width, double Height, string Svg);

/// <summary>
/// Turns AsciiMath into SVG with MathJax, which runs in the JavaScript engine Jint. The engine starts with the first
/// formula (under a second) and is kept; results are cached. Thread-safe.
/// </summary>
public static partial class MathRenderer
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Formula> Cache = new(StringComparer.Ordinal);
    private static readonly Assembly Assembly = typeof(MathRenderer).Assembly;
    private static readonly List<JsValue> Timers = new();
    private static Engine? engine;

    /// <summary>The formula as SVG; throws <see cref="FormatException"/> if MathJax cannot render it.</summary>
    public static Formula Render(string asciiMath)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(asciiMath, out Formula? cached))
            {
                return cached;
            }
            Engine js = engine ??= Start();
            js.SetValue("formula", asciiMath);
            Await(js, """
                MathJax.asciimath2svgPromise(formula).then(function (node) {
                  var adaptor = MathJax.startup.adaptor;
                  var svg = adaptor.firstChild(node);
                  var errors = adaptor.tags(svg, 'g').filter(function (g) { return adaptor.getAttribute(g, 'data-mml-node') === 'merror'; });
                  return { box: adaptor.getAttribute(svg, 'viewBox'), svg: adaptor.innerHTML(svg), error: errors.length > 0 };
                })
                """);
            JsValue result = js.Evaluate("__result.value");
            if (result.Get("error").AsBoolean())
            {
                throw new FormatException($"The formula \"{asciiMath}\" cannot be rendered.");
            }
            double[] box = result.Get("box").AsString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
                .ToArray();
            // the data-* attributes only serve MathJax's own menus and accessibility
            string svgContent = DataAttribute().Replace(result.Get("svg").AsString(), string.Empty);
            var formula = new Formula(box[0], box[1], box[2], box[3], svgContent);
            Cache[asciiMath] = formula;
            return formula;
        }
    }

    [GeneratedRegex("\\s(data-[a-z-]+|xmlns(:xlink)?)=\"[^\"]*\"")]
    private static partial Regex DataAttribute();

    private static Engine Start()
    {
        var js = new Engine(options => options.Strict(false));
        // MathJax loads its components and the font data on demand through this function
        js.SetValue("hostRequire", new Func<string, JsValue>(path =>
        {
            js.Execute(ReadResource(path));
            return JsValue.Undefined;
        }));
        js.SetValue("setTimeout", new Func<JsValue, JsValue, int>((callback, _) =>
        {
            Timers.Add(callback);
            return Timers.Count;
        }));
        js.SetValue("clearTimeout", new Action<JsValue>(_ => { }));
        js.Execute("""
            var global = this, globalThis = this;
            var console = { log: function () {}, warn: function () {}, error: function () {} };
            var MathJax = {
              loader: {
                load: ['input/asciimath', 'output/svg', 'adaptors/liteDOM'],
                paths: { mathjax: 'mathjax', fonts: 'fonts' },
                require: hostRequire
              },
              asciimath: { displaystyle: false },
              output: { font: 'mathjax-stix2' },
              svg: { fontCache: 'none' },
              startup: { typeset: false }
            };
            """);
        js.Execute(ReadResource("mathjax/startup.js"));
        Await(js, "MathJax.startup.promise");
        return js;
    }

    // "mathjax/input/asciimath.js" or "fonts/mathjax-stix2-font/svg/dynamic/greek.js" (".js" may be missing)
    private static string ReadResource(string path)
    {
        string name = path.StartsWith("fonts/", StringComparison.Ordinal) ? path.Substring("fonts/".Length) : path;
        if (!name.EndsWith(".js", StringComparison.Ordinal))
        {
            name += ".js";
        }
        string? resource = Assembly.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == name)
            ?? throw new FormatException("MathJax file not found: " + path);
        using var reader = new StreamReader(Assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd();
    }

    // waits for a promise, running the timers MathJax sets meanwhile; the result is in __result
    private static void Await(Engine js, string promise)
    {
        js.Execute("var __result = { done: false }; Promise.resolve(" + promise + ").then("
            + "function (v) { __result.done = true; __result.value = v; },"
            + "function (e) { __result.done = true; __result.error = String(e && e.message || e); });");
        for (int round = 0; round < 10_000; round++)
        {
            js.Advanced.ProcessTasks();
            if (js.Evaluate("__result.done").AsBoolean())
            {
                JsValue error = js.Evaluate("__result.error");
                if (!error.IsUndefined())
                {
                    throw new FormatException("MathJax: " + error.AsString());
                }
                return;
            }
            List<JsValue> due = Timers.ToList();
            Timers.Clear();
            foreach (JsValue callback in due)
            {
                js.Invoke(callback);
            }
        }
        throw new FormatException("MathJax did not finish.");
    }
}
