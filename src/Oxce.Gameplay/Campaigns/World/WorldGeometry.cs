using Oxce.Core.Random;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// A point on the globe in reference units: longitude in [0, 2π), latitude in [-π/2, π/2].
/// Mirrors <c>Target::setLongitude</c> and <c>Target::setLatitude</c>, including the
/// longitude flip when a latitude crosses a pole.
/// </summary>
public readonly record struct WorldPosition(double Longitude, double Latitude)
{
    /// <summary>The largest coordinate the wrapping loops accept before it is treated as corrupt.</summary>
    public const double CoordinateLimit = 1_000_000.0;

    public static WorldPosition Origin { get; } = new(0, 0);

    public WorldPosition WithLongitude(double longitude)
    {
        Validate(longitude);
        while (longitude < 0) longitude += 2 * Math.PI;
        while (longitude >= 2 * Math.PI) longitude -= 2 * Math.PI;
        return this with { Longitude = longitude };
    }

    public WorldPosition WithLatitude(double latitude)
    {
        Validate(latitude);
        if (latitude < -Math.PI / 2) return WithLongitude(Longitude + Math.PI) with { Latitude = -Math.PI - latitude };
        if (latitude > Math.PI / 2) return WithLongitude(Longitude - Math.PI) with { Latitude = Math.PI - latitude };
        return this with { Latitude = latitude };
    }

    /// <summary>Applies both setters in the reference order used by <c>MovingTarget::move</c>.</summary>
    public static WorldPosition Create(double longitude, double latitude) =>
        Origin.WithLongitude(longitude).WithLatitude(latitude);

    public bool IsNormalized =>
        double.IsFinite(Longitude) && double.IsFinite(Latitude) &&
        Longitude >= 0 && Longitude < 2 * Math.PI &&
        Latitude >= -Math.PI / 2 && Latitude <= Math.PI / 2;

    private static void Validate(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > CoordinateLimit)
            throw new InvalidDataException("A globe coordinate is outside the supported range.");
    }
}

/// <summary>
/// Globe arithmetic shared by world simulation: distances, movement, region containment and
/// land queries. Reference: <c>Target.cpp</c>, <c>MovingTarget.cpp</c>, <c>Mod/RuleRegion.cpp</c>,
/// <c>Geoscape/Globe.cpp</c> and <c>fmath.h</c> at reference commit 4df3a5e.
/// </summary>
public static class WorldGeometry
{
    /// <summary>fmath.h AreSame(double, double).</summary>
    public static bool AreSame(double left, double right) =>
        Math.Abs(left - right) <= double.Epsilon * Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));

    /// <summary>fmath.h Nautical: converts reference distance units to radians.</summary>
    public static double Nautical(double value) => value * (1 / 60.0) * (Math.PI / 180.0);

    /// <summary>fmath.h XcomDistance: the inverse of <see cref="Nautical"/>, truncated like the reference cast.</summary>
    public static int XcomDistance(double radians)
    {
        var value = radians * 60.0 * (180.0 / Math.PI);
        if (!double.IsFinite(value) || value is < int.MinValue or > int.MaxValue)
            throw new InvalidDataException("A globe distance is outside the supported range.");
        return (int)value;
    }

    public static double Distance(WorldPosition from, WorldPosition to) => Distance(from, to.Longitude, to.Latitude);

    /// <summary>Target::getDistance.</summary>
    public static double Distance(WorldPosition from, double longitude, double latitude)
    {
        if (AreSame(longitude, from.Longitude) && AreSame(latitude, from.Latitude)) return 0.0;
        return Math.Acos(Math.Cos(from.Latitude) * Math.Cos(latitude) * Math.Cos(longitude - from.Longitude) +
            Math.Sin(from.Latitude) * Math.Sin(latitude));
    }

    /// <summary>MovingTarget::calculateRadianSpeed.</summary>
    public static double RadianSpeed(int speed) => Nautical(speed) / 720.0;

    /// <summary>Craft::recalcSpeedMaxRadian.</summary>
    public static double CraftSpeedMaxRadian(int speedMax) => RadianSpeed(speedMax) * 120;

    /// <summary>MovingTarget::calculateSpeed with the reference meeting point (the destination itself).</summary>
    public static (double Longitude, double Latitude) SpeedVector(
        WorldPosition current, WorldPosition destination, double speedRadian)
    {
        var deltaLongitude = Math.Sin(destination.Longitude - current.Longitude) * Math.Cos(destination.Latitude);
        var deltaLatitude = Math.Cos(current.Latitude) * Math.Sin(destination.Latitude) -
            Math.Sin(current.Latitude) * Math.Cos(destination.Latitude) * Math.Cos(destination.Longitude - current.Longitude);
        var length = Math.Sqrt(deltaLongitude * deltaLongitude + deltaLatitude * deltaLatitude);
        var speedLatitude = deltaLatitude / length * speedRadian;
        var speedLongitude = deltaLongitude / length * speedRadian / Math.Cos(current.Latitude + speedLatitude);
        return double.IsNaN(speedLongitude) || double.IsNaN(speedLatitude) ? (0, 0) : (speedLongitude, speedLatitude);
    }

    /// <summary>MovingTarget::reachedDestination.</summary>
    public static bool ReachedDestination(WorldPosition current, WorldPosition destination) =>
        AreSame(destination.Longitude, current.Longitude) && AreSame(destination.Latitude, current.Latitude);

    /// <summary>One MovingTarget::move step toward a stationary meeting point.</summary>
    public static WorldPosition Move(WorldPosition current, WorldPosition destination, double speedRadian)
    {
        var (speedLongitude, speedLatitude) = SpeedVector(current, destination, speedRadian);
        if (Distance(current, destination) > speedRadian)
            return current.WithLongitude(current.Longitude + speedLongitude).WithLatitude(current.Latitude + speedLatitude);
        return current.WithLongitude(destination.Longitude).WithLatitude(destination.Latitude);
    }

    /// <summary>RuleRegion::insideRegion.</summary>
    public static bool InsideRegion(
        IReadOnlyList<GeographicArea> areas, double longitude, double latitude, bool ignoreTechnicalRegion = false)
    {
        ArgumentNullException.ThrowIfNull(areas);
        if (ignoreTechnicalRegion && areas.Count == 0) return true;
        foreach (var area in areas)
        {
            var insideLongitude = area.LongitudeMinimum <= area.LongitudeMaximum
                ? longitude >= area.LongitudeMinimum && longitude < area.LongitudeMaximum
                : (longitude >= area.LongitudeMinimum && longitude < 2 * Math.PI) ||
                    (longitude >= 0 && longitude < area.LongitudeMaximum);
            // Both poles can belong to a region, so the inclusive bound follows the hemisphere.
            var insideLatitude = latitude > 0
                ? latitude > area.LatitudeMinimum && latitude <= area.LatitudeMaximum
                : latitude >= area.LatitudeMinimum && latitude < area.LatitudeMaximum;
            if (insideLongitude && insideLatitude) return true;
        }
        return false;
    }

    public static bool InsideRegion(RuntimeRegionRule region, WorldPosition position, bool ignoreTechnicalRegion = false) =>
        InsideRegion(region.Areas, position.Longitude, position.Latitude, ignoreTechnicalRegion);

    public static bool InsideCountry(RuntimeCountryRule country, WorldPosition position) =>
        InsideRegion(country.Areas, position.Longitude, position.Latitude);

    /// <summary>RuleRegion::getRandomPoint; the area index is chosen by the caller's source when it is -1.</summary>
    public static WorldPosition RandomPoint(RuntimeRegionRule region, int zone, int area, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(random);
        var areas = MissionAreas(region, zone);
        var index = area != -1 ? area : random.NextInclusive(0, areas.Count - 1);
        if ((uint)index >= (uint)areas.Count)
            throw new InvalidDataException($"Mission zone {zone} has no area {index}.");
        var selected = areas[index];
        var longitudeMinimum = Math.Min(selected.LongitudeMinimum, selected.LongitudeMaximum);
        var longitudeMaximum = Math.Max(selected.LongitudeMinimum, selected.LongitudeMaximum);
        var latitudeMinimum = Math.Min(selected.LatitudeMinimum, selected.LatitudeMaximum);
        var latitudeMaximum = Math.Max(selected.LatitudeMinimum, selected.LatitudeMaximum);
        return new WorldPosition(
            RandomDouble(random, longitudeMinimum, longitudeMaximum),
            RandomDouble(random, latitudeMinimum, latitudeMaximum));
    }

    /// <summary>RNG::generate(double, double).</summary>
    public static double RandomDouble(IRandomSource random, double minimum, double maximum)
    {
        ArgumentNullException.ThrowIfNull(random);
        return minimum + random.NextUnit() * (maximum - minimum);
    }

    public static IReadOnlyList<MissionArea> MissionAreas(RuntimeRegionRule region, int zone)
    {
        ArgumentNullException.ThrowIfNull(region);
        if ((uint)zone >= (uint)region.MissionZones.Count)
            throw new InvalidDataException(
                $"A mission tried to use zone {zone}, but the region only defines {region.MissionZones.Count} zones.");
        var areas = region.MissionZones[zone].Areas;
        if (areas.Count == 0) throw new InvalidDataException($"Mission zone {zone} has no areas.");
        return areas;
    }

    /// <summary>Globe::insideLand: a polygon whose texture is a cosmetic ocean is not land.</summary>
    public static bool InsideLand(RuntimeGlobe globe, WorldPosition position)
    {
        ArgumentNullException.ThrowIfNull(globe);
        var texture = StrategicGeography.TextureAt(globe, position.Longitude, position.Latitude);
        if (texture is not { } id) return false;
        return !(globe.Textures.TryGetValue(id, out var rule) && rule.IsOcean);
    }

    /// <summary>Globe::insideFakeUnderwaterTexture.</summary>
    public static bool InsideFakeUnderwaterTexture(RuntimeGlobe globe, WorldPosition position)
    {
        ArgumentNullException.ThrowIfNull(globe);
        var texture = StrategicGeography.TextureAt(globe, position.Longitude, position.Latitude);
        return texture is { } id && globe.Textures.TryGetValue(id, out var rule) && rule.FakeUnderwater;
    }
}
