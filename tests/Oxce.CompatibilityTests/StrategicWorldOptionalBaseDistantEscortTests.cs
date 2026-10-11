using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseDistantEscortTests
{
    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 5, false)]
    [InlineData(true, 5, false)]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, true)]
    public void OutOfRangeEscortsIgnoreOptionalLeaderSpeedAndKeepOrderedPursuit(bool reverse, int takeoff, bool zeroRange)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.NotEmpty(TestFixtures.Rows(oracle.RootElement, "escortFuelRows"));
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var range = zeroRange ? 0 : 20;
        var content = StrategicWorldOptionalBaseCraftTests.Content(itemFuel: true, escortRange: range);
        var deletedContent = StrategicWorldOptionalBaseCraftTests.Content("CORE", true, range);
        var survivedContent = StrategicWorldOptionalBaseCraftTests.Content("", true, range);
        var followerBefore = zeroRange ? new WorldPosition(1, 0.6) : new(2, 0.9);
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldOptionalBaseSpeedEscortTests.NormalEscort(
                StrategicWorldOptionalBaseSpeedEscortTests.Snapshot(content, seed, reverse, takeoff))
                .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = 500 } });
            initial = initial.WithCraft("OPTIONAL_NORMAL_ESCORT", craft => craft with
            { Logistics = craft.Logistics! with { Longitude = followerBefore.Longitude, Latitude = followerBefore.Latitude } });
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(deleted);
            var leader = after.Bases[0].Crafts.Single(craft => craft.Id == 1).Logistics!;
            var follower = after.Bases[0].Crafts.Single(craft => craft.Id == 2).Logistics!;
            Assert.Equal(999, leader.Fuel);
            Assert.Equal(980, follower.Fuel);
            Assert.False(follower.LowFuel);
            Assert.Equal(deleted ? 6000 : 500, leader.Speed);
            Assert.Equal(deleted ? WorldTargetKind.Base : WorldTargetKind.AlienBase, leader.Destination!.Kind);
            Assert.Equal(WorldTargetKind.Craft, follower.Destination!.Kind);
            Assert.Equal(1, follower.Destination.Id);
            var leaderBefore = new WorldPosition(1, 0.6);
            var destination = deleted ? new WorldPosition(0.2, 0.1) : new(0.4, 0.2);
            var leaderPosition = takeoff > 0 ? leaderBefore : WorldGeometry.Move(leaderBefore, destination, WorldGeometry.RadianSpeed(leader.Speed));
            Assert.Equal(leaderPosition, new(leader.Longitude, leader.Latitude));
            Assert.Equal(WorldGeometry.Move(followerBefore, reverse ? leaderBefore : leaderPosition, WorldGeometry.RadianSpeed(2000)), new(follower.Longitude, follower.Latitude));
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-base-distant-escort.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, outcomes.Count);
    }

    [Fact]
    public void NearbyAndTransferredPositionAlternativesKeepTheirAtomicBoundary()
    {
        var content = StrategicWorldOptionalBaseCraftTests.Content(itemFuel: true);
        var initial = StrategicWorldOptionalBaseSpeedEscortTests.NormalEscort(
            StrategicWorldOptionalBaseSpeedEscortTests.Snapshot(content, 17, false, 0))
            .WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { Speed = 500 } });
        initial = StrategicWorldOptionalBaseEscortFuelParityTests.WithBoundaryFuel(initial, content, 20);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft escort speed alternatives for uncertain research.");
        var follower = initial.Bases[0].Crafts[1] with { Logistics = initial.Bases[0].Crafts[1].Logistics! with { Longitude = 2, Latitude = 0.9 } };
        initial = initial with
        {
            Bases = [initial.Bases[0] with { Crafts = [initial.Bases[0].Crafts[0]],
            Transfers = [new(101, 1, CampaignTransferKind.Craft, follower.RuleId, 1, Craft: follower)] }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft escort speed alternatives for uncertain research.");
    }
}
