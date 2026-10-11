using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class WorldAlienBaseHuntingTests
{
    [Fact]
    public void ZeroRangeUsesStrictDistanceAndDestroyedCraftCannotQualify()
    {
        var state = new CraftLogisticsState(100, 0, "STR_OUT", [], new Dictionary<string, int>(), []);
        Assert.False(WorldAlienBaseHunting.Eligible(state, false, 100, new(0, 0), 0));
        Assert.True(WorldAlienBaseHunting.Eligible(state, false, 100, new(0, 0), 1));
        Assert.False(WorldAlienBaseHunting.Eligible(state with { Damage = 100 }, false, 100, new(0, 0), 1));
    }

    [Fact]
    public void HuntingTimelinePreservesReverseLoadOrderAndRejectsAbsentEntry()
    {
        RuntimeWeightedTimeline[] timeline = [new(2, new Dictionary<string, ulong> { ["FIRST"] = 1 }),
            new(2, new Dictionary<string, ulong> { ["SECOND"] = 1 })];
        Assert.Equal("SECOND", Assert.Single(WorldAlienBaseHunting.Weights(timeline, 2)).Key);
        Assert.Throws<InvalidDataException>(() => WorldAlienBaseHunting.Weights(timeline, 1));
        Assert.Throws<InvalidDataException>(() => WorldAlienBaseHunting.Weights([], 0));
    }
}
