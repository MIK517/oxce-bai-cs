namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// Craft endurance arithmetic used by patrol, pursuit and return decisions.
/// Reference: <c>Craft::getFuelConsumption</c>, <c>getFuelLimit</c>, <c>getBaseRange</c>.
/// </summary>
public static class WorldFlight
{
    /// <summary>Craft::getFuelConsumption. Item-fuelled craft always burn one unit.</summary>
    public static int FuelConsumption(bool refuelItem, int speedMax, int speed, int escortSpeed)
    {
        if (refuelItem) return 1;
        if (escortSpeed > 0) return Math.Max(speedMax / 200, Math.Min(escortSpeed / 100, speedMax / 100));
        return (int)Math.Floor(speed / 100.0);
    }

    /// <summary>Craft::getFuelLimit: the fuel needed to reach the base from the current position.</summary>
    public static int FuelLimit(bool refuelItem, int speedMax, double distanceToBase)
    {
        var speedMaxRadian = RequireSpeed(speedMax);
        var value = Math.Floor(FuelConsumption(refuelItem, speedMax, speedMax, 0) * distanceToBase / speedMaxRadian);
        if (!double.IsFinite(value) || value is < int.MinValue or > int.MaxValue)
            throw new InvalidDataException("Craft fuel limit is outside the supported range.");
        return (int)value;
    }

    /// <summary>Craft::getBaseRange: how far the craft can fly and still return.</summary>
    public static double BaseRange(bool refuelItem, int speedMax, int fuel)
    {
        var speedMaxRadian = RequireSpeed(speedMax);
        var consumption = FuelConsumption(refuelItem, speedMax, speedMax, 0);
        if (consumption == 0) throw new InvalidDataException("Craft fuel consumption cannot be zero.");
        return fuel / 2.0 / consumption * speedMaxRadian;
    }

    private static double RequireSpeed(int speedMax)
    {
        if (speedMax <= 0) throw new InvalidDataException("Craft movement requires a positive maximum speed.");
        return WorldGeometry.CraftSpeedMaxRadian(speedMax);
    }
}
