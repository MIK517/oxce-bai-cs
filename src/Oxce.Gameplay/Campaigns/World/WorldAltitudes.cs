namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// UFO altitude and heading vocabulary. Reference: <c>Ufo::ALTITUDE_STRING</c>,
/// <c>Ufo::getAltitudeInt</c>, <c>Ufo::getVisibility</c> and <c>Ufo::calculateSpeed</c>.
/// </summary>
public static class WorldAltitudes
{
    public const string Ground = "STR_GROUND";
    public const string VeryLow = "STR_VERY_LOW";
    public const string Low = "STR_LOW_UC";
    public const string High = "STR_HIGH_UC";
    public const string VeryHigh = "STR_VERY_HIGH";

    public static IReadOnlyList<string> All { get; } = [Ground, VeryLow, Low, High, VeryHigh];

    /// <summary>Ufo::getAltitudeInt; an unknown altitude returns -1 like the reference.</summary>
    public static int Index(string altitude)
    {
        for (var index = 0; index < All.Count; index++)
            if (string.Equals(All[index], altitude, StringComparison.Ordinal)) return index;
        return -1;
    }

    /// <summary>Ufo::getVisibility.</summary>
    public static int Visibility(int defaultVisibility, string altitude) => altitude switch
    {
        Ground => -30,
        VeryLow => defaultVisibility - 20,
        Low => defaultVisibility - 10,
        High => defaultVisibility,
        VeryHigh => defaultVisibility - 10,
        _ => 0,
    };

    /// <summary>Ufo::calculateSpeed heading names.</summary>
    public static string Direction(double speedLongitude, double speedLatitude)
    {
        var x = speedLongitude;
        var y = -speedLatitude;
        if (WorldGeometry.AreSame(x, 0.0) || WorldGeometry.AreSame(y, 0.0))
        {
            if (WorldGeometry.AreSame(x, 0.0) && WorldGeometry.AreSame(y, 0.0)) return "STR_NONE_UC";
            if (WorldGeometry.AreSame(x, 0.0)) return y > 0 ? "STR_NORTH" : y < 0 ? "STR_SOUTH" : "STR_NONE_UC";
            return x > 0 ? "STR_EAST" : x < 0 ? "STR_WEST" : "STR_NONE_UC";
        }
        var theta = Math.Atan2(y, x) * 180.0 / Math.PI;
        if (theta is < 22.5f and > -22.5f) return "STR_EAST";
        if (theta is < -22.5f and > -67.5f) return "STR_SOUTH_EAST";
        if (theta is < -67.5f and > -112.5f) return "STR_SOUTH";
        if (theta is < -112.5f and > -157.5f) return "STR_SOUTH_WEST";
        if (theta is < -157.5f or > 157.5f) return "STR_WEST";
        if (theta is < 157.5f and > 112.5f) return "STR_NORTH_WEST";
        if (theta is < 112.5f and > 67.5f) return "STR_NORTH";
        return "STR_NORTH_EAST";
    }
}
