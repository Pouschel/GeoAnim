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

    /// <summary>
    /// Where the label of a segment, ray or line sits: at A + t·(B − A) (default 0.5, the middle of AB).
    /// </summary>
    public double? LabelPosition { get; init; }

    /// <summary>Radius of an angle's sector in units (default: sized to its label).</summary>
    public double? Radius { get; init; }

    /// <summary>Font size of the label of a point, segment, ray or line, in points.</summary>
    public double? LabelSize { get; init; }

    /// <summary>The label of a point (default: its name) or of a segment, ray or line (default: none).</summary>
    public string? Label { get; init; }

    /// <summary>Arrowheads of a segment or ray: at its end (B), its start (A) or both ends (dimension arrows).</summary>
    public Arrow Arrow { get; init; }

    /// <summary>A point without label.</summary>
    public bool NoLabel { get; init; }

    /// <summary>
    /// Offset of a text (or point label) in units of the coordinate system: the start of the text's baseline (its centre
    /// for a text at fixed coordinates) lies at the position plus this offset. Without it, a label sits a little to the
    /// upper right of its point.
    /// </summary>
    public Vec? Offset { get; init; }

    /// <summary>
    /// Shift of a label (of a point, segment, ray, line, angle or a text next to a point) in units of the coordinate
    /// system, from where it stands by default: (0, 0) changes nothing.
    /// </summary>
    public Vec? Shift { get; init; }

    /// <summary>How a text stands at its place: beginning there, centred (default for a text at coordinates) or ending there.</summary>
    public TextAlign? Align { get; init; }

    /// <summary>A reflection with the points running straight across (instead of folding over, or turning around a point).</summary>
    public bool Straight { get; init; }

    /// <summary>Define the object without showing it (show it later with Show).</summary>
    public bool Hidden { get; init; }

    /// <summary>How long it takes to draw the object, in milliseconds.</summary>
    public double? Duration { get; init; }

    /// <summary>How the drawing runs.</summary>
    public Easing? Easing { get; init; }

    public static Options None { get; } = new();
}

/// <summary>Where a segment or ray has an arrowhead.</summary>
public enum Arrow
{
    None,
    End,
    Start,
    Both,
}
