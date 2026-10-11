using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseZeroSpeedEscortTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void AirborneOwnSpeedFallbackMatchesOptionalLeaderReturnCap(bool reverse, int takeoff)
        => StrategicWorldOptionalBaseSpeedEscortTests.AssertEquivalentEscort(reverse, takeoff, true, true);

    [Fact]
    public void DifferentOwnSpeedAndTransferInputsRetainTheirAtomicFuelBoundary()
    {
        var content = StrategicWorldOptionalBaseCraftTests.Content(itemFuel: true);
        var initial = StrategicWorldOptionalBaseSpeedEscortTests.NormalEscort(
            StrategicWorldOptionalBaseSpeedEscortTests.Snapshot(content, 17, false, 0))
            .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = 0 } });
        var trial = initial with
        {
            Bases = [initial.Bases[0] with { Crafts =
            [initial.Bases[0].Crafts[0], initial.Bases[0].Crafts[1] with { Logistics = initial.Bases[0].Crafts[1].Logistics! with
            { Speed = 500 } }] }]
        };
        trial = StrategicWorldOptionalBaseEscortFuelParityTests.WithBoundaryFuel(trial, content, 20);
        AssertTimeBlocked(CampaignState.Restore(trial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft escort speed alternatives for uncertain research.");
        var follower = initial.Bases[0].Crafts[1];
        initial = initial with
        {
            Bases = [initial.Bases[0] with { Crafts = [initial.Bases[0].Crafts[0]],
            Transfers = [new(101, 1, CampaignTransferKind.Craft, follower.RuleId, 1, Craft: follower)] }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft escort speed alternatives for uncertain research.");
    }
}
