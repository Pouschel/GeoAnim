namespace GeoAnim;

/// <summary>All primitives of an animation.</summary>
internal sealed class Scene
{
    private readonly List<Primitive> primitives = new();

    public IReadOnlyList<Primitive> Primitives => primitives;

    /// <summary>A new primitive of <paramref name="owner"/> (for an editor: which object a part of the picture belongs to).</summary>
    public Primitive Create(GeoObject owner, string type, int layer)
    {
        var primitive = new Primitive(primitives.Count + 1, type, layer) { Owner = owner };
        primitives.Add(primitive);
        return primitive;
    }
}
