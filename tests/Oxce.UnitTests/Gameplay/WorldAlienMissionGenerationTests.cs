using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class WorldAlienMissionGenerationTests
{
    [Theory]
    [InlineData(0UL, "EARLY")]
    [InlineData(1UL, "EARLY")]
    [InlineData(2UL, "LATE")]
    [InlineData(ulong.MaxValue, "LATE")]
    public void TimelineSelectsLatestApplicableEntry(ulong month, string expected)
    {
        RuntimeWeightedTimeline[] timeline = [new(0, new Dictionary<string, ulong> { ["EARLY"] = 1 }),
            new(2, new Dictionary<string, ulong> { ["LATE"] = 1 })];
        Assert.Equal(expected, Assert.Single(WorldAlienMissionGeneration.WeightsAt(timeline, month, race: false)).Key);
    }

    [Fact]
    public void BeforeFirstRaceEntryIsEmptyButRegionRejectsIntentionally()
    {
        RuntimeWeightedTimeline[] timeline = [new(2, new Dictionary<string, ulong> { ["LATE"] = 1 })];
        var random = new SplitMix64RandomSource(17);
        Assert.Equal(string.Empty, WorldAlienMissionGeneration.Choose(
            WorldAlienMissionGeneration.WeightsAt(timeline, 0, race: true), random));
        Assert.Throws<InvalidDataException>(() => WorldAlienMissionGeneration.WeightsAt(timeline, 0, race: false));
        Assert.Equal(17UL, random.State);
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(2, "A")]
    [InlineData(3, "A")]
    [InlineData(4, "Z")]
    public void WeightedChoicesUseOrdinalOrderIncludingEmptyKey(int roll, string expected)
    {
        var weights = new Dictionary<string, ulong> { ["Z"] = 1, ["A"] = 2, [""] = 1, ["ignored"] = 0 };
        Assert.Equal(expected, WorldAlienMissionGeneration.Choose(weights, new Roll(roll)));
    }

    [Fact]
    public void ExcessiveTotalsFailBeforeDrawingRandomness()
    {
        var random = new SplitMix64RandomSource(17);
        Assert.Throws<InvalidDataException>(() => WorldAlienMissionGeneration.Choose(
            new Dictionary<string, ulong> { ["A"] = int.MaxValue, ["B"] = 1 }, random));
        Assert.Equal(17UL, random.State);
    }

    private sealed class Roll(int value) : IRandomSource
    {
        public int NextInclusive(int minimum, int maximum)
        {
            Assert.Equal((1, 4), (minimum, maximum));
            return value;
        }
        public int NextExclusive(int maximum) => throw new InvalidOperationException();
        public double NextUnit() => throw new InvalidOperationException();
    }
}
