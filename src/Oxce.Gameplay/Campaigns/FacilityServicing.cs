namespace Oxce.Gameplay.Campaigns;

/// <summary>Pure BaseFacility::rearm transition at reference 4df3a5e.</summary>
public static class FacilityServicing
{
    public static (int Ammo, bool MissingReported, int Used, bool Notify) Rearm(
        int ammo, int maximum, int rate, int buildTime, bool missingReported, bool itemFuel, int stock)
    {
        if (maximum <= 0 || buildTime > 0) return (ammo, missingReported, 0, false);
        if (ammo < 0 || rate < 0 || stock < 0) throw new InvalidDataException("Facility service values cannot be negative.");
        if (ammo >= maximum) return (ammo, false, 0, false);
        var used = Math.Min(maximum - ammo, rate);
        var notify = itemFuel && stock < used && !missingReported;
        if (itemFuel && stock < used)
        {
            missingReported = true;
            used = stock;
        }
        return (checked(ammo + used), missingReported, itemFuel ? used : 0, notify);
    }
}
