using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseCappedEscortTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 5)]
    [InlineData(true, false, 5)]
    [InlineData(false, true, 5)]
    [InlineData(true, true, 5)]
    public void OrdinaryCappedEscortsKeepEquivalentConsumptionAndOrderedMovement(bool cache, bool reverse, int takeoff)
        => StrategicWorldOptionalBaseSpeedEscortTests.AssertEquivalentEscort(cache, reverse, takeoff, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentCapsAndInvalidFollowerStatisticsRemainAtomic(bool cache)
    {
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache, itemFuel: true);
        var initial = StrategicWorldOptionalBaseSpeedEscortTests.NormalEscort(
            StrategicWorldOptionalBaseSpeedEscortTests.Snapshot(content, 17, false, 0));
        initial = initial with
        {
            Bases = [initial.Bases[0] with { Crafts =
            [initial.Bases[0].Crafts[0] with { Logistics = initial.Bases[0].Crafts[0].Logistics! with { Speed = 500 } }, initial.Bases[0].Crafts[1]] }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft escort speed alternatives for uncertain research.");
        initial = StrategicWorldOptionalBaseSpeedEscortTests.NormalEscort(
            StrategicWorldOptionalBaseSpeedEscortTests.Snapshot(content, 17, false, 0));
        initial = initial with
        {
            Bases = [initial.Bases[0] with { Crafts =
            [initial.Bases[0].Crafts[0], initial.Bases[0].Crafts[1] with { Logistics = initial.Bases[0].Crafts[1].Logistics! with
            { Weapons = [new("SPEED_EXCESS", 0), null] } }] }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Craft speed exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EquivalentReturnDependencyScansRetainTheirAggregateWorkLimit(bool cache)
    {
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache, itemFuel: true);
        var initial = StrategicWorldOptionalBaseSpeedCraftTests.Snapshot(content, 17, true, 0);
        var craft = initial.Bases[0].Crafts[0];
        initial = initial with { Bases = [initial.Bases[0] with { Crafts = Enumerable.Range(1, 1100).Select(id => craft with { Id = id }).ToArray() }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base activity and sight checks exceed the supported work limit.");
    }
}
