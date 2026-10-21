namespace GeoAnim.Tests;

/// <summary>Files of the repository (found from the test assembly upwards).</summary>
internal static class Repository
{
    public static string Root { get; } = FindRoot();

    public static string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder != null && !File.Exists(Path.Combine(folder, "GeoAnim.slnx")))
        {
            folder = Path.GetDirectoryName(folder);
        }
        return folder ?? throw new InvalidOperationException("GeoAnim.slnx not found above " + AppContext.BaseDirectory);
    }
}
