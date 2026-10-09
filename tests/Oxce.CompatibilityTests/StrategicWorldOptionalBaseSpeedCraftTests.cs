using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseSpeedCraftTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, false, 5)]
    [InlineData(true, false, 5)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 5)]
    [InlineData(true, true, 5)]
    public void IdenticalBurnOptionalReturnsKeepHuntingAndBothFlightSpeeds(bool cache, bool itemFuel, int takeoff)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        Assert.NotEmpty(TestFixtures.Rows(oracle.RootElement, "craft"));
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache, itemFuel: itemFuel);
        var deletedContent = StrategicWorldOptionalBaseCraftTests.Content(cache, "CORE", itemFuel);
        var survivedContent = StrategicWorldOptionalBaseCraftTests.Content(cache, "", itemFuel);
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, itemFuel, takeoff);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(deleted);
            var state = after.Bases[0].Crafts[0].Logistics!;
            Assert.Equal(itemFuel ? 999 : 940, state.Fuel);
            Assert.False(state.LowFuel);
            Assert.Equal(deleted ? 6000 : itemFuel ? 3000 : 6050, state.Speed);
            Assert.Equal(Math.Max(0, takeoff - 1), state.Takeoff);
            Assert.Equal(deleted ? WorldTargetKind.Base : WorldTargetKind.AlienBase, state.Destination!.Kind);
            var destination = deleted ? new WorldPosition(0.2, 0.1) : new(0.4, 0.2);
            var expected = takeoff > 0 ? new WorldPosition(1, 0.6) : WorldGeometry.Move(new(1, 0.6), destination, WorldGeometry.RadianSpeed(state.Speed));
            Assert.Equal(expected, new(state.Longitude, state.Latitude));
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-base-speed-craft.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, outcomes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsumedBurnAndInvalidSavedSpeedRemainAtomic(bool cache)
    {
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache);
        var initial = Snapshot(content, 17, false, 0).WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = 3000 } });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft speed alternatives for uncertain research.");
        content = StrategicWorldOptionalBaseCraftTests.Content(cache, itemFuel: true);
        initial = Snapshot(content, 17, true, 0).WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = -1 } });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft speed alternatives for uncertain research.");
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool itemFuel, int takeoff) =>
        StrategicWorldOptionalBaseAirborneCraftTests.Snapshot(content, seed, takeoff, false, true).WithCraft("SHIP", craft => craft with
        { Logistics = craft.Logistics! with { Speed = itemFuel ? 3000 : 6050 } });
}
