namespace GeoAnim;

/// <summary>How a value changes during an animation.</summary>
public enum Easing
{
    /// <summary>At constant speed.</summary>
    Linear,

    /// <summary>Starts slowly.</summary>
    EaseIn,

    /// <summary>Ends slowly.</summary>
    EaseOut,

    /// <summary>Starts and ends slowly.</summary>
    EaseInOut,

    /// <summary>Jumps to the new value at once.</summary>
    Step,
}

internal static class EasingFunctions
{
    /// <summary>The eased progress for the progress <paramref name="u"/> (0 to 1); the same formulas as the player.</summary>
    public static double Apply(Easing easing, double u)
    {
        u = Math.Clamp(u, 0, 1);
        return easing switch
        {
            Easing.EaseIn => u * u * u,
            Easing.EaseOut => 1 - Math.Pow(1 - u, 3),
            Easing.EaseInOut => u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2,
            Easing.Step => u < 1 ? 0 : 1,
            _ => u,
        };
    }

    /// <summary>The code of the easing in the animation data ("l" is the default and left out).</summary>
    public static string Code(Easing easing) => easing switch
    {
        Easing.EaseIn => "i",
        Easing.EaseOut => "o",
        Easing.EaseInOut => "io",
        Easing.Step => "s",
        _ => "l",
    };
}
