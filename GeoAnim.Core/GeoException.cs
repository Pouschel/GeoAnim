namespace GeoAnim;

/// <summary>A mistake in a construction, e.g. two circles that do not intersect or a name used twice.</summary>
public class GeoException(string message) : Exception(message);
