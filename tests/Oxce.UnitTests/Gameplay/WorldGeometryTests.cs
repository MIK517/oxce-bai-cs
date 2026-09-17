using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class WorldGeometryTests
{
    [Fact]
    public void LatitudePastAPoleFlipsTheLongitude()
    {
        var position = WorldPosition.Create(1.0, Math.PI / 2 + 0.25);
        Assert.Equal(Math.PI - (Math.PI / 2 + 0.25), position.Latitude, 12);
        Assert.Equal(1.0 + Math.PI, position.Longitude, 12);
        Assert.True(position.IsNormalized);

        var southern = WorldPosition.Create(1.0, -Math.PI / 2 - 0.25);
        Assert.Equal(-Math.PI + Math.PI / 2 + 0.25, southern.Latitude, 12);
        Assert.Equal(1.0 + Math.PI, southern.Longitude, 12);
    }

    [Fact]
    public void CorruptCoordinatesAreRejectedInsteadOfLoopingForever()
    {
        Assert.Throws<InvalidDataException>(() => WorldPosition.Create(double.NaN, 0));
        Assert.Throws<InvalidDataException>(() => WorldPosition.Create(0, double.PositiveInfinity));
        Assert.Throws<InvalidDataException>(() => WorldPosition.Create(WorldPosition.CoordinateLimit * 2, 0));
        Assert.Throws<InvalidDataException>(() => WorldGeometry.XcomDistance(double.NaN));
    }

    [Fact]
    public void MovementStopsExactlyOnTheDestination()
    {
        var destination = WorldPosition.Create(0.4001, 0.2);
        var position = WorldPosition.Create(0.4, 0.2);
        position = WorldGeometry.Move(position, destination, WorldGeometry.RadianSpeed(100_000));
        Assert.Equal(destination, position);
        Assert.True(WorldGeometry.ReachedDestination(position, destination));
        Assert.Equal(position, WorldGeometry.Move(position, destination, WorldGeometry.RadianSpeed(100_000)));
    }

    [Fact]
    public void MissionZoneAndAreaFailuresAreDiagnosed()
    {
        var region = new RuntimeRegionRule(0, [], [new MissionZone([])], new Dictionary<string, ulong>(), 0,
            string.Empty, null, [], []);
        var random = new FixedRandom(0);
        var missingZone = Assert.Throws<InvalidDataException>(() => WorldGeometry.RandomPoint(region, 1, -1, random));
        Assert.Contains("only defines 1 zones", missingZone.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => WorldGeometry.RandomPoint(region, 0, -1, random));

        var single = new RuntimeRegionRule(0, [], [new MissionZone([new MissionArea(0, 1, 0, 1, 0, "")])],
            new Dictionary<string, ulong>(), 0, string.Empty, null, [], []);
        Assert.Throws<InvalidDataException>(() => WorldGeometry.RandomPoint(single, 0, 3, random));
        var point = WorldGeometry.RandomPoint(single, 0, 0, new FixedRandom(0, 0.5));
        Assert.Equal(0.5, point.Longitude, 12);
        Assert.Equal(0.5, point.Latitude, 12);
    }

    [Fact]
    public void CraftEnduranceRejectsUnsupportedRules()
    {
        Assert.Throws<InvalidDataException>(() => WorldFlight.FuelLimit(false, 0, 1));
        Assert.Throws<InvalidDataException>(() => WorldFlight.BaseRange(false, 0, 10));
        // A craft slower than 100 burns no fuel per step, so a range would divide by zero.
        Assert.Throws<InvalidDataException>(() => WorldFlight.BaseRange(false, 60, 10));
        Assert.Equal(1, WorldFlight.FuelConsumption(true, 60, 60, 0));
    }

    [Fact]
    public void WeightedOptionsFollowOrdinalOrderAndRejectOverflow()
    {
        var options = new WorldWeightedOptions();
        options.Set("B", 2);
        options.Set("A", 1);
        Assert.Equal<string>(["A", "B"], [.. options.Names]);
        Assert.Equal("A", options.ChooseAt(1));
        Assert.Equal("B", options.ChooseAt(2));
        Assert.Equal("B", options.ChooseAt(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.ChooseAt(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.ChooseAt(0));

        options.Set("B", 0);
        Assert.Equal(1UL, options.TotalWeight);
        Assert.Equal(string.Empty, new WorldWeightedOptions().Choose(new FixedRandom(0)));
        Assert.Throws<InvalidDataException>(() => options.Set("C", long.MaxValue));
    }

    [Fact]
    public void AlienStrategyRefreshesAnExhaustedRegionTable()
    {
        (string, ulong, IEnumerable<KeyValuePair<string, ulong>>)[] regions =
        [
            ("REGION_A", 10, new Dictionary<string, ulong> { ["MISSION_A"] = 4 }),
            ("REGION_B", 20, new Dictionary<string, ulong> { ["MISSION_B"] = 6 }),
        ];
        var strategy = new AlienStrategyState();
        strategy.Initialize(regions);
        Assert.True(strategy.ValidMissionRegion("REGION_A"));
        Assert.Equal("MISSION_A", strategy.ChooseRandomMission("REGION_A", new FixedRandom(0)));

        strategy.RemoveMission("REGION_A", "MISSION_A");
        Assert.False(strategy.ValidMissionRegion("REGION_A"));
        Assert.Equal(0UL, strategy.RegionChances["REGION_A"]);
        Assert.Equal(string.Empty, strategy.ChooseRandomMission("REGION_A", new FixedRandom(0)));

        strategy.RemoveMission("REGION_B", "MISSION_B");
        Assert.True(strategy.RegionChances.IsEmpty);
        // An exhausted table is rebuilt from the rules before the next choice.
        Assert.Equal("REGION_A", strategy.ChooseRandomRegion(new FixedRandom(0), regions));
        Assert.True(strategy.ValidMissionRegion("REGION_B"));
    }

    private sealed class FixedRandom(int choice, double unit = 0) : IRandomSource
    {
        public int NextExclusive(int exclusiveMaximum) => Math.Min(exclusiveMaximum - 1, choice);
        public int NextInclusive(int minimum, int maximum) => Math.Min(maximum, minimum + choice);
        public double NextUnit() => unit;
    }
}
