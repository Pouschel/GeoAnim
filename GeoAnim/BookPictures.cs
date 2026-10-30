using GeoAnim.Figures;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

/// <summary>The pictures of a figure of a page as in the book (print, e-book, the preview's book view).</summary>
internal static class BookPictures
{
    /// <summary>
    /// The pictures of a figure: a still, a row of stills (the steps from … to, cut alike) or, for a whole animation, its
    /// last picture; each cut to its content unless the figure says "crop=no". Their size: the figure's ("{width=8cm}" –
    /// of all its pictures together, after cutting – or "{scale=6mm}"), else the script's ("print scale", "print width"),
    /// else the book's (<paramref name="style"/>); the labels always as the book says.
    /// </summary>
    public static List<FigureImage> Of(PageFigure figure, FigureAnimation animation, FigureStyle style) => Of(figure, animation, style, out _);

    /// <inheritdoc cref="Of(PageFigure, FigureAnimation, FigureStyle)"/>
    public static List<FigureImage> Of(PageFigure figure, FigureAnimation animation, FigureStyle style, out FigureStyle used)
    {
        List<double> times = Times(figure, animation);
        List<FigureImage> Render(FigureStyle s) => times.Select(time => FigureRenderer.Render(animation, time, s, figure.Crop ? times : null)).ToList();
        double viewWidth = animation.XMax - animation.XMin;
        double? unit = (figure.Reference.Scale is { } scale ? FigureStyle.ParseLength(scale) : null)
            ?? animation.PrintScale ?? (animation.PrintWidth is double width && viewWidth > 0 ? width / viewWidth : null);
        used = unit is double u ? style with { PointsPerUnit = u } : style;
        List<FigureImage> images = Render(used);
        // a width of the figure: the scale that makes its pictures that wide together (twice: the margins do not grow with it)
        if (figure.Reference.Width is { } wanted && FigureStyle.ParseLength(wanted) is double target)
        {
            for (int round = 0; round < 2; round++)
            {
                double total = images.Sum(i => i.Width);
                if (total <= 0 || Math.Abs(total - target) < 0.5)
                {
                    break;
                }
                used = used with { PointsPerUnit = used.PointsPerUnit * target / total };
                images = Render(used);
            }
        }
        return images;
    }

    /// <summary>
    /// A warning if the figure's texts are larger next to its drawing than in the player (then a text placed freely may
    /// run into a line): the player's texts are a part of its width (size 34 of 1280 pixels), the book's have a fixed size.
    /// Whatever gave its size; only "{nowarn}" at the figure (checked in print, it fits) turns it off.
    /// </summary>
    public static string? TextWarning(PageFigure figure, FigureAnimation animation, FigureStyle used)
    {
        if (figure.Reference.NoWarn)
        {
            return null;
        }
        double printWidth = (animation.XMax - animation.XMin) * used.PointsPerUnit;
        double ratio = printWidth > 0 ? 1280 * used.SizeFactor / printWidth : 1;
        return ratio > 1.2
            ? $"{figure.Reference.File}: in print its texts are {ratio.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} times as large next to the drawing "
                + $"as in the player ({(used.PointsPerUnit * 25.4 / 72).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} mm a unit) – check texts placed freely (\"at x y\"), "
                + "or make the figure larger (scale, width); {nowarn} at the figure if it fits."
            : null;
    }

    /// <summary>The indices of the steps whose ends the pictures show (-1: the starting picture).</summary>
    public static List<int> Steps(PageFigure figure, FigureAnimation animation) =>
        figure.From == null
            ? [animation.Steps.Count - 1]
            : figure.Still
                ? [figure.To!.Value]
                : Enumerable.Range(figure.From.Value, figure.To!.Value - figure.From.Value + 1).ToList();

    private static List<double> Times(PageFigure figure, FigureAnimation animation) => Steps(figure, animation).Select(animation.EndOf).ToList();

    /// <summary>The size of the figures and the font size of the text of the book in <paramref name="folder"/> or above it (quietly).</summary>
    public static (FigureStyle Style, double TextSize) PrintOf(string folder)
    {
        for (DirectoryInfo? directory = new(folder); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, BookFile.Name)))
            {
                BookSettings settings = BookFile.Read(directory.FullName, _ => { }, out _);
                return (new FigureStyle(settings.FigureUnit ?? FigureStyle.Default.PointsPerUnit, settings.FigureSize ?? FigureStyle.Default.SizeFactor),
                    settings.TextSize ?? 11);
            }
        }
        return (FigureStyle.Default, 11);
    }
}
