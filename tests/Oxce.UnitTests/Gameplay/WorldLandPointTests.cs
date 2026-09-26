using Oxce.Core.Random;
using Oxce.Formats.Terrain;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class WorldLandPointTests
{
    [Fact]
    public void FirstAreaCityBypassesChecksEvenWhenAnotherAreaIsSelected()
    {
        var region = Region(new MissionArea(0.1, 0.1, 0.1, 0.1, 0, "city"),
            new MissionArea(0.2, 0.4, 0.2, 0.4, 0, ""));
        var random = new LandingRandom([1]);

        var point = WorldGeometry.LandPoint(region, RuntimeGlobe.Empty, 0, 50, random);

        Assert.Equal(0.3, point.Longitude, 12);
        Assert.Equal(0.3, point.Latitude, 12);
        Assert.Equal(1, random.IntegerCalls);
        Assert.Equal(2, random.UnitCalls);
    }

    [Fact]
    public void ImpossibleAreaForcesTheHundredthCandidateAndRollsChanceOnlyOnce()
    {
        var region = Region(new MissionArea(0.2, 0.4, 0.2, 0.4, 0, ""));
        var random = new LandingRandom([49]);

        var point = WorldGeometry.LandPoint(region, RuntimeGlobe.Empty, 0, 50, random);

        Assert.Equal(0.3, point.Longitude, 12);
        Assert.Equal(101, random.IntegerCalls);
        Assert.Equal(200, random.UnitCalls);
    }

    [Theory]
    [InlineData(0, -1, 1, 0.15)]
    [InlineData(100, -1, 0, 0.45)]
    [InlineData(50, 49, 0, 0.45)]
    [InlineData(50, 50, 1, 0.15)]
    public void FakeWaterPreferenceRejectsTheWrongTexture(int chance, int roll, int firstArea, double expectedLongitude)
    {
        var region = Region(new MissionArea(0.1, 0.2, -0.1, 0.1, 0, ""),
            new MissionArea(0.4, 0.5, -0.1, 0.1, 1, ""));
        var globe = new RuntimeGlobe([Polygon(0, 0, 0.3), Polygon(1, 0.3, 0.6)],
            new Dictionary<int, RuntimeGlobeTexture> { [0] = new(), [1] = new(FakeUnderwater: true) });
        // RNG::percent accepts rolls below the percentage, excluding equality.
        // Each case offers the wrong texture first, then the requested texture.
        var random = new LandingRandom(roll < 0 ? [firstArea, 1 - firstArea] : [roll, firstArea, 1 - firstArea]);

        var point = WorldGeometry.LandPoint(region, globe, 0, chance, random);

        Assert.Equal(expectedLongitude, point.Longitude, 12);
        Assert.Equal(roll < 0 ? 2 : 3, random.IntegerCalls);
        Assert.Equal(4, random.UnitCalls);
    }

    [Fact]
    public void LandOutsideRegionIsRejectedBeforeAcceptingAnInsideCandidate()
    {
        var region = Region(new MissionArea(1.1, 1.2, -0.1, 0.1, 0, ""),
            new MissionArea(0.1, 0.2, -0.1, 0.1, 0, ""));
        var globe = new RuntimeGlobe([Polygon(0, 1, 1.3), Polygon(0, 0, 0.3)],
            new Dictionary<int, RuntimeGlobeTexture>());
        var random = new LandingRandom([0, 1]);

        var point = WorldGeometry.LandPoint(region, globe, 0, 0, random);

        Assert.Equal(0.15, point.Longitude, 12);
        Assert.Equal(2, random.IntegerCalls);
    }

    private static RuntimeRegionRule Region(params MissionArea[] areas) =>
        new(0, [new GeographicArea(0, 1, -0.5, 0.5)], [new MissionZone(areas)],
            new Dictionary<string, ulong>(), 0, string.Empty, null, [], []);

    private static WorldPolygon Polygon(int texture, double west, double east) =>
        new(texture, [new WorldPoint(west, -0.5), new WorldPoint(east, -0.5),
            new WorldPoint(east, 0.5), new WorldPoint(west, 0.5)]);

    private sealed class LandingRandom(int[] choices) : IRandomSource
    {
        public int IntegerCalls { get; private set; }
        public int UnitCalls { get; private set; }
        public int NextExclusive(int exclusiveMaximum) => NextInclusive(0, exclusiveMaximum - 1);
        public int NextInclusive(int minimum, int maximum)
        {
            var index = IntegerCalls++;
            return index < choices.Length ? Math.Clamp(choices[index], minimum, maximum) : minimum;
        }
        public double NextUnit() { UnitCalls++; return 0.5; }
    }
}
