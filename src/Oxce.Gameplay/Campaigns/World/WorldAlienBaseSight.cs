namespace Oxce.Gameplay.Campaigns.World;

/// <summary>GeoscapeState::time1Day/time10Minutes alien-base sight checks at 4df3a5e.</summary>
public static class WorldAlienBaseSight
{
    /// <summary>Facility distance is truncated before its exclusive range test.</summary>
    public static int? FacilityChance(double distance, int range, int chance, int buildTime)
    {
        var units = WorldGeometry.XcomDistance(distance);
        return buildTime == 0 && range > units
            ? chance == 0 ? (int)(50 - (long)units * 50 / range) : chance : null;
    }

    /// <summary>Patrol range is inclusive; the floating percentage truncates to an int.</summary>
    public static int? CraftChance(double distance, int range)
    {
        var radians = WorldGeometry.Nautical(range);
        return range > 0 && distance <= radians ? (int)(50 - distance / radians * 50) : null;
    }
}
