namespace GeoAnim;

/// <summary>
/// Optional settings of an object and of how it appears. Everything left null takes the default of the kind of object.
/// </summary>
public sealed record Options
{
    /// <summary>Colour of the point, line, circle, text or polygon outline.</summary>
    public Color? Color { get; init; }

    /// <summary>Line width in points (1 pt = 1 pixel in a player 1280 pixels wide; scales with the player).</summary>
    public double? Width { get; init; }

    /// <summary>Fill colour of a polygon.</summary>
    public Color? Fill { get; init; }

    /// <summary>Opacity of the fill of a polygon (0 to 1).</summary>
    public double? FillOpacity { get; init; }

    /// <summary>Radius of a point or font size of a text and of a point's label, in points.</summary>
    public double? Size { get; init; }

    /// <summary>Font size of a point's label, in points.</summary>
    public double? LabelSize { get; init; }

    /// <summary>The label of a point (default: its name).</summary>
    public string? Label { get; init; }

    /// <summary>A point without label.</summary>
    public bool NoLabel { get; init; }

    /// <summary>
    /// Offset of a text (or point label) in units of the coordinate system: the start of the text's baseline (its centre
    /// for a text at fixed coordinates) lies at the position plus this offset. Without it, a label sits a little to the
    /// upper right of its point.
    /// </summary>
    public Vec? Offset { get; init; }

    /// <summary>Define the object without showing it (show it later with Show).</summary>
    public bool Hidden { get; init; }

    /// <summary>How long it takes to draw the object, in milliseconds.</summary>
    public double? Duration { get; init; }

    /// <summary>How the drawing runs.</summary>
    public Easing? Easing { get; init; }

    public static Options None { get; } = new();
}
