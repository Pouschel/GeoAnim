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
    /// The picture of a script at the end of a step (-1: the starting picture) as in print, cut to its content: in the size
    /// the script gives ("print scale", "print width"), else the book's (<paramref name="style"/>) – for the print view of
    /// the script's preview.
    /// </summary>
    public static (FigureImage Image, FigureStyle Used) OfScript(FigureAnimation animation, FigureStyle style, int step)
    {
        double viewWidth = animation.XMax - animation.XMin;
        double? unit = animation.PrintScale ?? (animation.PrintWidth is double width && viewWidth > 0 ? width / viewWidth : null);
        FigureStyle used = unit is double u ? style with { PointsPerUnit = u } : style;
        double time = animation.EndOf(step);
        FigureImage image = FigureRenderer.Render(animation, time, used, [time]);
        return (image, used);
    }

    /// <summary>The indices of the steps whose ends the pictures show (-1: the starting picture).</summary>
    public static List<int> Steps(PageFigure figure, FigureAnimation animation) =>
        figure.From == null
            ? [animation.Steps.Count - 1]
            : figure.Still
                ? [figure.To!.Value]
                : Enumerable.Range(figure.From.Value, figure.To!.Value - figure.From.Value + 1).ToList();

    /// <summary>
    /// What stands under each of <paramref name="count"/> pictures: in a row of stills the first line of its step's
    /// description (inline Markdown; no number – in the script the steps may be 3, 4, 5), under a single picture nothing.
    /// </summary>
    public static List<string?> Captions(PageFigure figure, FigureAnimation animation, int count)
    {
        List<int> steps = Steps(figure, animation);
        return Enumerable.Range(0, count).Select(i => count > 1 && i < steps.Count && steps[i] >= 0 && steps[i] < animation.Steps.Count
            && animation.Steps[steps[i]].Caption.Split('\n')[0].Trim() is { Length: > 0 } caption ? caption : null).ToList();
    }

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
