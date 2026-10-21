namespace GeoAnim;

/// <summary>All primitives of an animation.</summary>
internal sealed class Scene
{
    private readonly List<Primitive> primitives = new();

    public IReadOnlyList<Primitive> Primitives => primitives;

    public Primitive Create(string type, int layer)
    {
        var primitive = new Primitive(primitives.Count + 1, type, layer);
        primitives.Add(primitive);
        return primitive;
    }
}
