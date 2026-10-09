using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseZeroSpeedEscortTests
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
    public void AirborneOwnSpeedFallbackMatchesOptionalLeaderReturnCap(bool cache, bool reverse, int takeoff)
        => StrategicWorldOptionalBaseSpeedEscortTests.AssertEquivalentEscort(cache, reverse, takeoff, true, true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundedOrDifferentOwnSpeedRetainTheirAtomicFuelBoundary(bool cache)
    {
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache, itemFuel: true);
        var initial = StrategicWorldOptionalBaseSpeedEscortTests.NormalEscort(
            StrategicWorldOptionalBaseSpeedEscortTests.Snapshot(content, 17, false, 0))
            .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = 0 } });
        for (var variant = 0; variant < 2; variant++)
        {
            var grounded = variant != 0;
            var trial = initial with
            {
                Bases = [initial.Bases[0] with { Crafts =
                [initial.Bases[0].Crafts[0], initial.Bases[0].Crafts[1] with { Logistics = initial.Bases[0].Crafts[1].Logistics! with
                { Speed = 500, Status = grounded ? "STR_READY" : "STR_OUT" } }] }]
            };
            AssertTimeBlocked(CampaignState.Restore(trial, content, new SplitMix64RandomSource(17)),
                "Alien base self-destruction requires craft escort speed alternatives for uncertain research.");
        }
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
