namespace GeoAnim;

/// <summary>
/// A text at coordinates or next to a point (it then moves with the point); it fades in. A text at coordinates has a
/// free point of its own as its position (not a named object), so it can move like a point.
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

    private GeoText(string name, string content, GeoPoint anchor, Options options, bool ownPosition)
        : base(name, options, anchor)
    {
        Content = content;
        this.anchor = anchor;
        OwnPosition = ownPosition ? anchor : null;
    }

    /// <summary>The position of a text at coordinates (a free point, it can move); null for a text next to a point.</summary>
    internal GeoPoint? OwnPosition { get; }

    public override string Kind => "text";

    public string Content { get; }

    internal override double DefaultDuration => 400;

    internal override Easing DefaultEasing => Easing.EaseOut;

    private Vec Position => anchor.Position;

    internal override void Compute() => IsDefined = anchor.IsDefined;

    internal override void Render(RenderContext context, Span span)
    {
        if (text == null)
        {
            IsDrawn = true;
            text = context.Scene.Create("text", Layers.Text);
            text.Animate("text", span, Content);
            text.Animate("anchor", span, OwnPosition != null ? "middle" : "start");
        }
        // an offset is in coordinates; the default distance from a point is in points
        Vec offset = Options.Offset ?? new Vec(0, 0);
        Vec distance = Options.Offset == null && OwnPosition == null ? Defaults.LabelOffset : new Vec(0, 0);
        text.Animate("opacity", span, Visibility * Reveal);
        if (IsDefined)
        {
            text.Animate("x", span, Position.X + offset.X);
            text.Animate("y", span, Position.Y + offset.Y);
        }
        RenderShift(text, span);
        text.Animate("dx", span, distance.X);
        text.Animate("dy", span, distance.Y);
        text.Animate("size", span, Options.Size ?? Defaults.TextSize);
        text.Animate("color", span, ColorOr(Defaults.LineColor).ToHex());
    }
}
