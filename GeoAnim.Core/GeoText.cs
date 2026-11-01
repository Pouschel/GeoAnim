namespace GeoAnim;

/// <summary>
/// A text at coordinates, next to a point (it then moves with the point) or below another text (a line further down,
/// with the same alignment; the player measures the text above); it fades in. A text at coordinates has a free point of
/// its own as its position (not a named object), so it can move like a point.
/// </summary>
public sealed class GeoText : GeoObject
{
    private readonly GeoPoint anchor;
    private Primitive? text;

    internal GeoText(string name, string content, Vec position, Options options)
        : this(name, content, GeoPoint.Free(name + "#position", position, Options.None), options, ownPosition: true)
    {
    }

    internal GeoText(string name, string content, GeoPoint anchor, Options options)
        : this(name, content, anchor, options, ownPosition: false)
    {
    }

    /// <summary>A text one line below <paramref name="above"/> (plus <paramref name="gap"/> units), aligned like it.</summary>
    internal GeoText(string name, string content, GeoText above, double gap, Options options)
        : base(name, options, above)
    {
        if (above.OwnPosition == null && above.Above == null)
        {
            throw new GeoException($"A text can stand below a text at coordinates or below another such text, not below the text {above.Name} next to a point.");
        }
        Content = content;
        Above = above;
        Gap = gap;
        anchor = above.anchor;
    }

    private GeoText(string name, string content, GeoPoint anchor, Options options, bool ownPosition)
        : base(name, options, anchor)
    {
        Content = content;
        this.anchor = anchor;
        OwnPosition = ownPosition ? anchor : null;
    }

    /// <summary>The position of a text at coordinates (a free point, it can move); null for a text next to a point or below a text.</summary>
    internal GeoPoint? OwnPosition { get; }

    /// <summary>The text this one stands below, or null.</summary>
    internal GeoText? Above { get; }

    /// <summary>The extra space below the text above, in units.</summary>
    internal double Gap { get; }

    public override string Kind => "text";

    public string Content { get; }

    internal override double DefaultDuration => 400;

    internal override Easing DefaultEasing => Easing.EaseOut;

    private Vec Position => anchor.Position;

    // a text below another one: the alignment of the first text of the block, unless it has one of its own
    private TextAlign Alignment => Options.Align ?? Above?.Alignment ?? (OwnPosition != null ? TextAlign.Center : TextAlign.Left);

    internal override void Compute() => IsDefined = Above?.IsDefined ?? anchor.IsDefined;

    /// <summary>The primitive (created invisible if the text is not drawn yet): a text below it refers to it.</summary>
    internal Primitive PrimitiveFor(RenderContext context, Span span)
    {
        if (text == null)
        {
            // the text above first: the player lays out a block from the top
            Primitive? above = Above?.PrimitiveFor(context, span);
            text = context.Scene.Create(this, "text", Layers.Of(Options, Layers.Text));
            text.Animate("text", span, Content);
            text.Animate("anchor", span, Alignment switch { TextAlign.Left => "start", TextAlign.Right => "end", _ => "middle" });
            if (above != null)
            {
                text.Animate("below", span, (double)above.Id);
                text.Animate("belowGap", span, Gap);
            }
            text.Animate("opacity", span, 0.0);
            WriteLayout(span);
        }
        return text;
    }

    internal override void Render(RenderContext context, Span span)
    {
        IsDrawn = true;
        PrimitiveFor(context, span);
        text!.Animate("opacity", span, Visibility * Reveal);
        WriteLayout(span);
    }

    /// <summary>For the ghost in an editor (a text at coordinates or at a point): its text and place.</summary>
    internal void WriteGhost(Primitive ghost, Span span)
    {
        ghost.Animate("text", span, Content);
        ghost.Animate("anchor", span, Alignment switch { TextAlign.Left => "start", TextAlign.Right => "end", _ => "middle" });
        ghost.Animate("size", span, Options.Size ?? Defaults.TextSize);
        RenderShift(ghost, span);
        WritePlace(ghost, span);
    }

    // where it stands: with "labeldir" like a point's label – its box in that direction from its place, its edge
    // "labelgap" points away (beside a point: from the point's edge), so that it keeps its distance in print whatever the
    // scale; else at its place (beside a point: the default distance in points), "offset" in coordinates
    private void WritePlace(Primitive primitive, Span span)
    {
        if (Options.LabelDir is double direction)
        {
            double radians = direction * Math.PI / 180;
            if (IsDefined)
            {
                primitive.Animate("x", span, Position.X);
                primitive.Animate("y", span, Position.Y);
            }
            double pointSize = OwnPosition == null ? anchor.Options.Size ?? Defaults.PointSize : 0;
            AnimateFromStart(primitive, "dir", Span.At(span.Start), 1.0, 0.0);
            AnimateFromStart(primitive, "nx", span, Math.Cos(radians), 0.0);
            AnimateFromStart(primitive, "ny", span, Math.Sin(radians), 0.0);
            AnimateFromStart(primitive, "gap", span, pointSize + (Options.LabelGap ?? Defaults.LabelGap), 0.0);
            return;
        }
        Vec offset = Options.Offset ?? new Vec(0, 0);
        Vec distance = Options.Offset == null && OwnPosition == null ? Defaults.LabelOffset : new Vec(0, 0);
        if (IsDefined)
        {
            primitive.Animate("x", span, Position.X + offset.X);
            primitive.Animate("y", span, Position.Y + offset.Y);
        }
        primitive.Animate("dx", span, distance.X);
        primitive.Animate("dy", span, distance.Y);
    }

    private void WriteLayout(Span span)
    {
        // an offset is in coordinates; the default distance from a point is in points
        RenderShift(text!, span);
        text!.Animate("size", span, (Options.Size ?? Defaults.TextSize) * (1 + 0.25 * Highlight));
        text.Animate("color", span, ColorOr(Defaults.LineColor).ToHex());
        if (Options.Halo)
        {
            text.Animate("halo", span, 1.0);
        }
        if (Above != null)
        {
            // the player places it below the text above
            return;
        }
        WritePlace(text, span);
    }
}

/// <summary>How a text stands at its place: beginning there, centred on it, or ending there.</summary>
public enum TextAlign
{
    Left,
    Center,
    Right,
}
