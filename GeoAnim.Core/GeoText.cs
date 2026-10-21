namespace GeoAnim;

/// <summary>A text at fixed coordinates or next to a point (it then moves with the point); it fades in.</summary>
public sealed class GeoText : GeoObject
{
    private readonly Vec? fixedPosition;
    private readonly GeoPoint? anchor;
    private Primitive? text;

    internal GeoText(string name, string content, Vec position, Options options)
        : base(name, options)
    {
        Content = content;
        fixedPosition = position;
    }

    internal GeoText(string name, string content, GeoPoint anchor, Options options)
        : base(name, options, anchor)
    {
        Content = content;
        this.anchor = anchor;
    }

    public override string Kind => "text";

    public string Content { get; }

    internal override double DefaultDuration => 400;

    internal override Easing DefaultEasing => Easing.EaseOut;

    private Vec Position => anchor?.Position ?? fixedPosition!.Value;

    internal override void Compute() => IsDefined = anchor?.IsDefined ?? true;

    internal override void Render(RenderContext context, Span span)
    {
        if (text == null)
        {
            IsDrawn = true;
            text = context.Scene.Create("text", Layers.Text);
            text.Animate("text", span, Content);
            text.Animate("anchor", span, anchor == null ? "middle" : "start");
        }
        Vec offset = Options.Offset ?? (anchor == null ? new Vec(0, 0) : Defaults.LabelOffset);
        text.Animate("opacity", span, Visibility * Reveal);
        if (IsDefined)
        {
            text.Animate("x", span, Position.X);
            text.Animate("y", span, Position.Y);
        }
        text.Animate("dx", span, offset.X);
        text.Animate("dy", span, offset.Y);
        text.Animate("size", span, Options.Size ?? Defaults.TextSize);
        text.Animate("color", span, ColorOr(Defaults.LineColor).ToHex());
    }
}
