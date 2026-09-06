namespace Oxce.Gameplay.Campaigns;

/// <summary>Arithmetic from RuleItem, Base and TransferItemsState at reference 4df3a5e.</summary>
public static class StrategicLogisticsMath
{
    public static int AdjustedItemPrice(int basePrice, int coefficient) =>
        unchecked((int)((long)basePrice * coefficient / 100));

    public static bool StoresOverfull(int capacity, double used, double offset = 0)
    {
        var scaled = (used + offset) * 100;
        if (!double.IsFinite(scaled) || scaled < int.MinValue || scaled > int.MaxValue)
            throw new InvalidDataException("Storage quantity exceeds the reference integer range.");
        return (int)scaled > unchecked(capacity * 100);
    }

    public static double TransferDistance(double sourceLongitude, double sourceLatitude,
        double destinationLongitude, double destinationLatitude)
    {
        const double radius = 51.2;
        var x = radius * Math.Cos(destinationLatitude) * Math.Cos(destinationLongitude) -
            radius * Math.Cos(sourceLatitude) * Math.Cos(sourceLongitude);
        var y = radius * Math.Cos(destinationLatitude) * Math.Sin(destinationLongitude) -
            radius * Math.Cos(sourceLatitude) * Math.Sin(sourceLongitude);
        var z = radius * -Math.Sin(destinationLatitude) - radius * -Math.Sin(sourceLatitude);
        var distance = Math.Sqrt(x * x + y * y + z * z);
        if (!double.IsFinite(distance)) throw new InvalidDataException("Transfer coordinates must be finite.");
        return distance;
    }

    public static int TransferHours(double distance) => checked((int)Math.Floor(6 + distance / 10));

    public static int TransferUnitCost(double distance, CampaignTransferKind kind) =>
        checked((int)(distance * (kind == CampaignTransferKind.Craft ? 25 : kind == CampaignTransferKind.Item ? 1 : 5)));

    public static int TransferTotal(int subtotal, int multiplier, int divisor)
    {
        if (divisor == 0) throw new InvalidDataException("Transfer cost divisor must not be zero.");
        return Math.Max(1, unchecked(subtotal * multiplier) / divisor);
    }
}
