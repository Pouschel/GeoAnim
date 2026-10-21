namespace GeoAnim;

/// <summary>The drawing order of the kinds of objects (higher on top).</summary>
internal static class Layers
{
    public const int Polygon = 10;
    public const int Curve = 20;
    public const int Point = 30;
    public const int Label = 40;
    public const int Text = 50;
}
