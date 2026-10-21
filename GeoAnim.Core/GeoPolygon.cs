namespace GeoAnim;

/// <summary>A filled polygon through three or more points; it fades in.</summary>
public sealed class GeoPolygon : GeoObject
{
    private Primitive? polygon;

    internal GeoPolygon(string name, IReadOnlyList<GeoPoint> vertices, Options options)
        : base(name, options, vertices.ToArray<GeoObject>())
    {
        if (vertices.Count < 3)
        {
            throw new GeoException($"The polygon {name} needs at least 3 points.");
        }
        Vertices = vertices;
    }

    public override string Kind => "polygon";

    public IReadOnlyList<GeoPoint> Vertices { get; }

    internal override double DefaultDuration => 600;

    internal override Easing DefaultEasing => Easing.EaseOut;

    internal override void Compute() => IsDefined = Vertices.All(v => v.IsDefined);

    internal override void Render(RenderContext context, Span span)
    {
        if (polygon == null)
        {
            IsDrawn = true;
            polygon = context.Scene.Create("polygon", Layers.Polygon);
        }
        polygon.Animate("opacity", span, Visibility * Reveal);
        if (IsDefined)
        {
            polygon.Animate("points", span, Vertices.SelectMany(v => new[] { v.Position.X, v.Position.Y }).ToArray());
        }
        Color color = ColorOr(Defaults.LineColor);
        polygon.Animate("color", span, color.ToHex());
        polygon.Animate("width", span, WidthOr(Defaults.LineWidth));
        polygon.Animate("fill", span, (Options.Fill ?? color).ToHex());
        polygon.Animate("fillOpacity", span, Options.FillOpacity ?? Defaults.FillOpacity);
    }
}
