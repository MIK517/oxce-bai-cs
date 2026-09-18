using System.Globalization;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Compares the port's world arithmetic with the pinned reference oracle
/// (<c>fixtures/manifests/strategic-world.json</c>). Randomness is scripted on both sides:
/// the probe returns the injected choice, the port receives the same choice through
/// <see cref="IRandomSource"/>.
/// </summary>
public sealed class StrategicWorldFixtureTests
{
    private const string Manifest = "strategic-world";

    [Fact]
    public void CoordinateWrappingAndDistancesMatchTheReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected(Manifest);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "coordinates"))
        {
            var position = WorldPosition.Create(Number(row[0]), Number(row[1]));
            Assert.Equal(Text(row[2]), Fixed(position.Longitude));
            Assert.Equal(Text(row[3]), Fixed(position.Latitude));
            Assert.True(position.IsNormalized);
        }

        var from = WorldPosition.Create(0.4, 0.2);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "distances"))
        {
            var distance = WorldGeometry.Distance(from, Number(row[0]), Number(row[1]));
            Assert.Equal(Text(row[2]), Fixed(distance));
            Assert.Equal(row[3].GetInt32(), WorldGeometry.XcomDistance(distance));
        }
    }

    [Fact]
    public void MovementStepsMatchTheReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected(Manifest);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "movement"))
        {
            var destination = WorldPosition.Create(1.0, 0.6);
            var position = WorldPosition.Create(0.4, 0.2);
            var speedRadian = WorldGeometry.RadianSpeed(row[0].GetInt32());
            Assert.Equal(Text(row[1]), Fixed(speedRadian));
            var vector = (Longitude: 0.0, Latitude: 0.0);
            for (var step = 0; step < 3; step++)
            {
                // MovingTarget::move recomputes the speed vector from the current position first,
                // so the oracle's vector belongs to the last step it took.
                vector = WorldGeometry.SpeedVector(position, destination, speedRadian);
                position = WorldGeometry.Move(position, destination, speedRadian);
                Assert.Equal(Text(row[5 + (step * 2)]), Fixed(position.Longitude));
                Assert.Equal(Text(row[6 + (step * 2)]), Fixed(position.Latitude));
            }
            Assert.Equal(Text(row[2]), Fixed(vector.Longitude));
            Assert.Equal(Text(row[3]), Fixed(vector.Latitude));
            Assert.Equal(row[4].GetInt32() == 1, WorldGeometry.ReachedDestination(position, destination));
        }
    }

    [Fact]
    public void RegionContainmentAndRandomPointsMatchTheReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected(Manifest);
        var region = Areas(
            (350.0, 20.0, -10.0, 10.0),
            (10.0, 40.0, 30.0, 90.0));
        IReadOnlyList<GeographicArea> technical = [];
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "regions"))
        {
            var longitude = Radians(Number(row[0]));
            var latitude = Radians(Number(row[1]));
            Assert.Equal(row[2].GetInt32() == 1, WorldGeometry.InsideRegion(region, longitude, latitude));
            Assert.Equal(row[3].GetInt32() == 1, WorldGeometry.InsideRegion(technical, longitude, latitude));
            Assert.Equal(row[4].GetInt32() == 1, WorldGeometry.InsideRegion(technical, longitude, latitude, true));
        }

        var zoned = Zone(
            new MissionArea(Radians(20.0), Radians(10.0), Radians(5.0), Radians(-5.0), 3, string.Empty),
            new MissionArea(Radians(40.0), Radians(40.0), Radians(15.0), Radians(15.0), 7, "CITY"));
        var rows = TestFixtures.Rows(oracle.RootElement, "randomPoints");
        foreach (var row in rows.Take(rows.Length - 1))
        {
            var random = new ScriptedRandom(Number(row[0]), 1);
            var point = WorldGeometry.RandomPoint(zoned, 0, row[1].GetInt32(), random);
            Assert.Equal(Text(row[2]), Fixed(point.Longitude));
            Assert.Equal(Text(row[3]), Fixed(point.Latitude));
        }
        var isPoint = rows[^1];
        Assert.Equal(isPoint[0].GetInt32() == 1, zoned.MissionZones[0].Areas[0].IsPoint);
        Assert.Equal(isPoint[1].GetInt32() == 1, zoned.MissionZones[0].Areas[1].IsPoint);
    }

    [Fact]
    public void UfoHeadingAltitudeAndVisibilityMatchTheReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected(Manifest);
        var destinations = new[] { (0.4, 0.2), (1.0, 0.2), (0.4, 0.9), (1.0, 0.9), (0.1, 0.05) };
        var headings = TestFixtures.Rows(oracle.RootElement, "ufo");
        Assert.Equal(destinations.Length, headings.Length);
        for (var index = 0; index < headings.Length; index++)
        {
            var position = WorldPosition.Create(0.4, 0.2);
            var destination = WorldPosition.Create(destinations[index].Item1, destinations[index].Item2);
            var (speedLongitude, speedLatitude) =
                WorldGeometry.SpeedVector(position, destination, WorldGeometry.RadianSpeed(2000));
            Assert.Equal(Text(headings[index][0]), WorldAltitudes.Direction(speedLongitude, speedLatitude));
        }

        foreach (var row in TestFixtures.Rows(oracle.RootElement, "visibility"))
        {
            var altitude = Text(row[0]);
            Assert.Equal(row[1].GetInt32(), WorldAltitudes.Index(altitude));
            Assert.Equal(row[2].GetInt32(), WorldAltitudes.Visibility(15, altitude));
        }
    }

    [Fact]
    public void CraftEnduranceMatchesTheReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected(Manifest);
        var basePosition = WorldPosition.Create(0.4, 0.2);
        var craftPosition = WorldPosition.Create(1.0, 0.6);
        var distance = WorldGeometry.Distance(craftPosition, basePosition);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "craft"))
        {
            var refuelItem = row[0].GetInt32() == 1;
            var speedMax = row[1].GetInt32();
            var fuel = row[2].GetInt32();
            Assert.Equal(row[3].GetInt32(), WorldFlight.FuelConsumption(refuelItem, speedMax, speedMax, 0));
            Assert.Equal(row[4].GetInt32(), WorldFlight.FuelConsumption(refuelItem, speedMax, speedMax, 300));
            Assert.Equal(row[5].GetInt32(), WorldFlight.FuelLimit(refuelItem, speedMax, distance));
            Assert.Equal(Text(row[6]), Fixed(WorldFlight.BaseRange(refuelItem, speedMax, fuel)));
            Assert.Equal(Text(row[7]), Fixed(WorldGeometry.CraftSpeedMaxRadian(speedMax)));
        }
    }

    [Fact]
    public void DetectionArithmeticMatchesTheReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected(Manifest);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "detection"))
        {
            var shields = row[0].GetInt32();
            var facilities = new List<WorldBaseFacility>();
            for (var index = 0; index < row[1].GetInt32(); index++) facilities.Add(new(2, 2, false, 0, false));
            for (var index = 0; index < shields; index++) facilities.Add(new(1, 1, true, 1, false));
            Assert.Equal(row[2].GetInt32(), WorldDetection.BaseDetectionChance(facilities));
        }

        foreach (var row in TestFixtures.Rows(oracle.RootElement, "radarChance"))
        {
            var chance = row[0].GetInt32();
            var visibility = row[1].GetInt32();
            Assert.Equal(row[2].GetInt32(), WorldDetection.DetectionChance(chance, visibility));
            var (type, craftChance) = WorldDetection.CraftDetection(100, chance, 10, visibility, false);
            Assert.Equal(UfoDetectionResult.Radar, type);
            Assert.Equal(row[2].GetInt32(), craftChance);
            var (baseType, baseChance) = WorldDetection.BaseDetection(
                [new WorldRadarFacility(100, chance, false)], 10, visibility, false, static _ => false);
            Assert.Equal(UfoDetectionResult.Radar, baseType);
            Assert.Equal(row[2].GetInt32(), baseChance);
        }
    }

    [Fact]
    public void WeightedSelectionAndMissionBookkeepingMatchTheReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected(Manifest);
        var rows = TestFixtures.Rows(oracle.RootElement, "weighted");
        foreach (var row in rows.Take(rows.Length - 1))
        {
            var options = new WorldWeightedOptions();
            options.Set("STR_ALPHA", 3);
            options.Set("STR_BETA", 4);
            options.Set("STR_GAMMA", 0);
            Assert.Equal((ulong)row[2].GetInt32(), options.TotalWeight);
            Assert.Equal(Text(row[1]), options.ChooseAt((ulong)row[0].GetInt32()));
            Assert.Equal(Text(row[1]), options.Choose(new ScriptedRandom(0, row[0].GetInt32() - 1)));
        }
        var emptied = new WorldWeightedOptions();
        emptied.Set("STR_ALPHA", 3);
        emptied.Set("STR_ALPHA", 0);
        Assert.Equal(Text(rows[^1][1]), emptied.Choose(new ScriptedRandom(0, 0)));
        Assert.Equal((ulong)rows[^1][2].GetInt32(), emptied.TotalWeight);

        var locations = TestFixtures.Rows(oracle.RootElement, "missionLocations");
        var strategy = new AlienStrategyState();
        strategy.AddMissionRun("varA");
        strategy.AddMissionRun("varA", 2);
        strategy.AddMissionRun(string.Empty, 5);
        strategy.AddMissionLocation("varA", "REGION_A", 1, 0);
        Assert.Equal(locations[0][0].GetInt32(), strategy.MissionLocations.Count);
        Assert.Equal(locations[0][1].GetInt32() == 1, strategy.ValidMissionLocation("varA", "REGION_A", 1));

        strategy.AddMissionLocation("varA", "REGION_A", 1, 2);
        strategy.AddMissionLocation("varB", "REGION_B", 0, 2);
        Assert.Equal(locations[1][0].GetInt32(), strategy.MissionLocations.Count);
        Assert.Equal(locations[1][1].GetInt32() == 1, strategy.ValidMissionLocation("varA", "REGION_A", 1));
        Assert.Equal(locations[1][2].GetInt32() == 1, strategy.ValidMissionLocation("varA", "REGION_A", 2));
        Assert.Equal(locations[1][3].GetInt32(), strategy.MissionsRun("varA"));
        Assert.Equal(locations[1][4].GetInt32(), strategy.MissionRuns.Count(entry => entry.Key.Length == 0));

        strategy.AddMissionLocation("varA", "REGION_C", 2, 2);
        strategy.AddMissionLocation("varA", "REGION_D", 3, 2);
        // The reference drops the first table entry, so the busy variable erases its own history.
        Assert.Equal(locations[2][0].GetInt32(), strategy.MissionLocations.Count);
        Assert.Equal(locations[2][1].GetInt32(),
            strategy.MissionLocations.TryGetValue("varA", out var varA) ? varA.Count : 0);
        Assert.Equal(locations[2][2].GetInt32(), strategy.MissionLocations.Count(entry => entry.Key == "varB"));
        Assert.Equal(locations[2][3].GetInt32() == 1, strategy.ValidMissionLocation("varA", "REGION_A", 1));

        strategy.AddMissionLocation(string.Empty, string.Empty, 0, 2);
        Assert.Equal(locations[3][0].GetInt32(), strategy.MissionLocations.Count(entry => entry.Key.Length == 0));
        Assert.Equal(locations[3][1].GetInt32() == 1,
            strategy.ValidMissionLocation(string.Empty, string.Empty, 0));
    }

    [Fact]
    public void TrajectorySpeedAndSpawnCountdownMatchTheReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected(Manifest);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "trajectory"))
            Assert.Equal(row[2].GetInt32(), WorldTrajectory.Speed(row[1].GetInt32(), row[0].GetInt32()));
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "countdown"))
        {
            Assert.Equal(row[2].GetInt32(), WorldTrajectory.SpawnTimerSteps(row[0].GetInt32()));
            Assert.Equal(row[3].GetInt32(),
                WorldTrajectory.SpawnCountdown(row[0].GetInt32(), new ScriptedRandom(0, row[1].GetInt32())));
        }
    }

    private static IReadOnlyList<GeographicArea> Areas(params (double LonMin, double LonMax, double LatMin, double LatMax)[] areas) =>
        [.. areas.Select(area => new GeographicArea(
            Radians(area.LonMin), Radians(area.LonMax), Radians(area.LatMin), Radians(area.LatMax)))];

    private static RuntimeRegionRule Zone(params MissionArea[] areas) =>
        new(0, [], [new MissionZone(areas)], new Dictionary<string, ulong>(), 0, string.Empty, null, [], []);

    private static double Radians(double degrees) => degrees * Math.PI / 180.0;

    private static double Number(JsonElement element) => element.GetDouble();

    private static string Text(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();

    private static string Fixed(double value) => value.ToString("F12", CultureInfo.InvariantCulture);

    /// <summary>Injects the reference probe's scripted choices: a fraction and an integer offset.</summary>
    private sealed class ScriptedRandom(double fraction, int choice) : IRandomSource
    {
        public int NextExclusive(int exclusiveMaximum) => Math.Min(exclusiveMaximum - 1, choice);
        public int NextInclusive(int minimum, int maximum) => Math.Min(maximum, minimum + choice);
        public double NextUnit() => fraction;
    }
}
