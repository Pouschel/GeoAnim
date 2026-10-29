using SkiaSharp;
using Svg.Skia;

namespace GeoAnim.Figures;

/// <summary>
/// Pictures as PNG for readers that show no SVG (Kindle): an SVG drawn with Svg.Skia at a resolution, on white, without
/// transparency.
/// </summary>
public static class FigurePng
{
    /// <summary>The resolution of the pictures for the book, in pixels per inch.</summary>
    public const double DefaultDpi = 300;

    /// <summary>
    /// <paramref name="svg"/> (with its size in points: width="…pt") as PNG with <paramref name="dpi"/> pixels per inch, on
    /// <paramref name="background"/> (white), opaque.
    /// </summary>
    public static byte[] Render(string svg, double widthInPoints, double heightInPoints, double dpi = DefaultDpi, string background = "#ffffff")
    {
        using var document = new SKSvg();
        using SKPicture picture = document.FromSvg(svg) ?? throw new FormatException("The SVG cannot be read.");
        int width = Math.Max(1, (int)Math.Ceiling(widthInPoints * dpi / 72));
        int height = Math.Max(1, (int)Math.Ceiling(heightInPoints * dpi / 72));
        var info = new SKImageInfo(width, height, SKColorType.Rgb888x, SKAlphaType.Opaque);
        using SKSurface surface = SKSurface.Create(info);
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse(background));
        // Svg.Skia sizes the picture in its own pixels (pt × 96/72): whatever they are, the picture fills the image
        SKRect bounds = picture.CullRect;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            canvas.Scale(width / bounds.Width, height / bounds.Height);
            canvas.Translate(-bounds.Left, -bounds.Top);
        }
        canvas.DrawPicture(picture);
        canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A figure as PNG.</summary>
    public static byte[] Render(FigureImage figure, double dpi = DefaultDpi) => Render(figure.Svg, figure.Width, figure.Height, dpi);
}
