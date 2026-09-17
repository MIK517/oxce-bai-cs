namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Detection outcome bits. Reference: <c>UfoDetection</c> in <c>Ufo.h</c>.</summary>
[Flags]
public enum UfoDetectionResult
{
    None = 0,
    Radar = 1,
    Hyperwave = 3,
}

/// <summary>A completed radar facility as seen by <c>Base::detect</c>.</summary>
public readonly record struct WorldRadarFacility(int RadarRange, int RadarChance, bool Hyperwave);

/// <summary>A completed facility as seen by <c>Base::getDetectionChance</c>.</summary>
public readonly record struct WorldBaseFacility(int SizeX, int SizeY, bool MindShield, int MindShieldPower, bool Disabled);

/// <summary>
/// UFO detection arithmetic. Reference: <c>Base::detect</c>, <c>Craft::detect</c>,
/// <c>Base::getDetectionChance</c> and <c>GeoscapeState::DetectXCOMBase</c>.
/// Detection scripts run at the call site in <see cref="CampaignState"/>; this type owns
/// the chance arithmetic only.
/// </summary>
public static class WorldDetection
{
    /// <summary>Base::getDetectionChance: bigger bases are easier to find, mind shields hide them.</summary>
    public static int BaseDetectionChance(IEnumerable<WorldBaseFacility> completedFacilities)
    {
        ArgumentNullException.ThrowIfNull(completedFacilities);
        long shields = 0;
        long size = 0;
        foreach (var facility in completedFacilities)
        {
            size += (long)facility.SizeX * facility.SizeY;
            if (facility.MindShield && !facility.Disabled) shields += facility.MindShieldPower;
        }
        if (shields < 0 || size < 0) throw new InvalidDataException("Base detection inputs cannot be negative.");
        return checked((int)((size / 6 + 15) / (shields + 1)));
    }

    /// <summary>Base::detect without its script hook.</summary>
    public static (UfoDetectionResult Type, int Chance) BaseDetection(
        IEnumerable<WorldRadarFacility> completedFacilities,
        int distance,
        int visibility,
        bool alreadyTracked,
        Func<int, bool> percent)
    {
        ArgumentNullException.ThrowIfNull(completedFacilities);
        ArgumentNullException.ThrowIfNull(percent);
        var hyperwave = false;
        var hyperwaveChance = 0;
        var radarChance = 0;
        foreach (var facility in completedFacilities)
        {
            if (facility.RadarRange >= distance)
            {
                if (facility.Hyperwave)
                {
                    if (facility.RadarChance == 100 || percent(facility.RadarChance)) hyperwave = true;
                    hyperwaveChance = checked(hyperwaveChance + facility.RadarChance);
                }
                else
                {
                    radarChance = checked(radarChance + facility.RadarChance);
                }
            }
        }
        if (alreadyTracked)
        {
            if (hyperwave || hyperwaveChance > 0)
                return (hyperwave ? UfoDetectionResult.Hyperwave : UfoDetectionResult.Radar, 100);
            return radarChance > 0 ? (UfoDetectionResult.Radar, 100) : (UfoDetectionResult.None, 0);
        }
        if (hyperwave) return (UfoDetectionResult.Hyperwave, 100);
        return radarChance > 0 ? (UfoDetectionResult.Radar, DetectionChance(radarChance, visibility)) : (UfoDetectionResult.None, 0);
    }

    /// <summary>Craft::detect without its script hook.</summary>
    public static (UfoDetectionResult Type, int Chance) CraftDetection(
        int radarRange, int radarChance, int distance, int visibility, bool alreadyTracked)
    {
        if (distance >= radarRange) return (UfoDetectionResult.None, 0);
        if (radarChance == 100) return (UfoDetectionResult.Radar, 100);
        return (UfoDetectionResult.Radar, alreadyTracked ? 100 : DetectionChance(radarChance, visibility));
    }

    /// <summary>The shared <c>chance * (100 + visibility) / 100</c> reduction.</summary>
    public static int DetectionChance(int chance, int visibility) =>
        checked((int)((long)chance * (100 + visibility) / 100));
}
