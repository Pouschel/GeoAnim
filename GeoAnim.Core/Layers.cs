namespace GeoAnim;

/// <summary>The drawing order of the kinds of objects (higher on top).</summary>
internal static class Layers
{
    /// <summary>An object with "back": below everything (also below the fills of polygons and angles).</summary>
    public const int Back = 0;

    public const int Polygon = 10;
    public const int Curve = 20;
    public const int Point = 30;
    public const int Label = 40;
    public const int Text = 50;

    /// <summary>An object with "front": above everything but texts (also above points and labels).</summary>
    public const int Front = 45;

    /// <summary>The layer of an object of a kind usually in <paramref name="normal"/>, with its "back" or "front".</summary>
    public static int Of(Options options, int normal) => options.Stacking switch
    {
        Stacking.Back => Back,
        Stacking.Front => Math.Max(normal, Front),
        _ => normal,
    };
}

/// <summary>Where an object lies among the others: where its kind lies, below or above everything.</summary>
public enum Stacking
{
    Normal,
    Back,
    Front,
}
