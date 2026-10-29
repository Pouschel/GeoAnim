using System.Globalization;
using GeoAnim.Figures;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

internal static partial class Program
{
    /// <summary>
    /// geoanim figure &lt;file.geo&gt;[#steps] [-o file.svg] [-nocrop]: the picture of a script as in the book, as SVG in
    /// points – after a step ("#3", "#0" the starting picture, "#gleich" by its mark or name), a row of pictures ("#2..4",
    /// one file for each step, all cut alike) or without steps the last picture. The size comes from "print:" in the
    /// index.yaml of the book (the nearest one in the script's folder or above it).
    /// </summary>
    internal static int Figure(List<string> arguments)
    {
        string source = arguments[0];
        string? output = null;
        bool crop = true;
        for (int i = 1; i < arguments.Count; i++)
        {
            if (arguments[i] is "-o" or "--output" && i + 1 < arguments.Count)
            {
                output = Path.GetFullPath(arguments[++i]);
            }
            else if (arguments[i] is "-nocrop" or "--nocrop")
            {
                crop = false;
            }
            else
            {
                throw new ArgumentException("Unknown option " + arguments[i]);
            }
        }
        // "file.geo#3": the file and its steps
        int hash = source.IndexOf('#', StringComparison.Ordinal);
        string file = hash < 0 ? source : source[..hash];
        string? steps = hash < 0 || hash == source.Length - 1 ? null : source[(hash + 1)..];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine("File not found: " + file);
            return 1;
        }
        AnimationData? animation = CompileFile(file, Path.GetFileName(file));
        if (animation == null)
        {
            return 1;
        }
        FigureStyle style = StyleOf(Path.GetDirectoryName(Path.GetFullPath(file))!);
        // the steps shown: indices from 0 (-1: the starting picture)
        List<int> shown;
        try
        {
            shown = StepsOf(file, steps, animation);
        }
        catch (FormatException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
        var figure = FigureAnimation.Parse(animation.ToJson());
        List<double> times = shown.Select(figure.EndOf).ToList();
        string baseName = output ?? Path.ChangeExtension(Path.GetFullPath(file), ".svg");
        for (int i = 0; i < shown.Count; i++)
        {
            FigureImage image = FigureRenderer.Render(figure, times[i], style, crop ? times : null);
            // one picture: as named; a row: "-3" for the third step (as numbered in the script)
            string target = steps == null || shown.Count == 1 && output != null
                ? baseName
                : Path.Combine(Path.GetDirectoryName(baseName)!,
                    Path.GetFileNameWithoutExtension(baseName) + "-" + (shown[i] + 1).ToString(CultureInfo.InvariantCulture) + Path.GetExtension(baseName));
            File.WriteAllText(target, image.Svg);
            Console.WriteLine($"{target} ({Millimetres(image.Width)} × {Millimetres(image.Height)} mm)");
        }
        return 0;
    }

    private static string Millimetres(double points) => (points * 25.4 / 72).ToString("0.0", CultureInfo.InvariantCulture);

    // "3", "0", "gleich", "2..4", "2-4" → the indices of the steps; none: the last picture
    private static List<int> StepsOf(string file, string? steps, AnimationData animation)
    {
        if (steps == null)
        {
            return [animation.Steps.Count - 1];
        }
        string[] range = steps.Contains("..", StringComparison.Ordinal) ? steps.Split("..")
            : System.Text.RegularExpressions.Regex.IsMatch(steps, @"^\d+-\d+$") ? steps.Split('-') : [steps, steps];
        if (range.Length != 2 || range[0].Length == 0 || range[1].Length == 0)
        {
            throw new FormatException($"\"#{steps}\" is no step: write a number (#2), a name (#gleich) or a range (#3..4).");
        }
        var reference = new GeoReference(Path.GetFileName(file), range[0], range[1], "", 0, 0);
        int from = MarkdownPage.StepIndex(reference, animation, range[0]);
        int to = MarkdownPage.StepIndex(reference, animation, range[1]);
        if (to < from)
        {
            throw new FormatException($"{reference.File}: the steps #{steps} are in the wrong order.");
        }
        return Enumerable.Range(from, to - from + 1).ToList();
    }

    /// <summary>The size of the figures from the index.yaml of the book in <paramref name="folder"/> or above it.</summary>
    internal static FigureStyle StyleOf(string folder)
    {
        for (DirectoryInfo? directory = new(folder); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, BookFile.Name)))
            {
                // only what is about the figures: the rest of the book does not matter here
                BookSettings settings = BookFile.Read(directory.FullName, warning =>
                {
                    if (warning.Contains("\"print", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine(warning);
                    }
                }, out string? error);
                if (error != null)
                {
                    Console.Error.WriteLine(error);
                }
                return new FigureStyle(settings.FigureUnit ?? FigureStyle.Default.PointsPerUnit, settings.FigureSize ?? FigureStyle.Default.SizeFactor);
            }
        }
        return FigureStyle.Default;
    }
}
