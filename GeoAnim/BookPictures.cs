using GeoAnim.Figures;
using GeoAnim.Viewer;

namespace GeoAnim.Cli;

/// <summary>The pictures of a figure of a page as in the book (print, e-book, the preview's book view).</summary>
internal static class BookPictures
{
    /// <summary>
    /// The pictures of a figure: a still, a row of stills (the steps from … to, cut alike) or, for a whole animation, its
    /// last picture; each cut to its content unless the figure says "crop=no".
    /// </summary>
    public static List<FigureImage> Of(PageFigure figure, FigureAnimation animation, FigureStyle style)
    {
        List<double> times = Times(figure, animation);
        return times.Select(time => FigureRenderer.Render(animation, time, style, figure.Crop ? times : null)).ToList();
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
