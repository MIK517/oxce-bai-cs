using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseSpeedEscortTests
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
    public void ItemFuelEscortsKeepTheirBurnAcrossOptionalLeaderSpeedChanges(bool cache, bool reverse, int takeoff)
        => AssertEquivalentEscort(cache, reverse, takeoff, false);

    internal static void AssertEquivalentEscort(bool cache, bool reverse, int takeoff, bool ordinary)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.NotEmpty(TestFixtures.Rows(oracle.RootElement, "escortFuelRows"));
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache, itemFuel: true);
        var deletedContent = StrategicWorldOptionalBaseCraftTests.Content(cache, "CORE", true);
        var survivedContent = StrategicWorldOptionalBaseCraftTests.Content(cache, "", true);
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, reverse, takeoff);
            if (ordinary) initial = NormalEscort(initial);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(deleted);
            var leader = after.Bases[0].Crafts.Single(craft => craft.Id == 1).Logistics!;
            var follower = after.Bases[0].Crafts.Single(craft => craft.Id == 2).Logistics!;
            Assert.Equal(999, leader.Fuel);
            Assert.Equal(ordinary ? 980 : 999, follower.Fuel);
            Assert.False(leader.LowFuel);
            Assert.False(follower.LowFuel);
            Assert.Equal(deleted ? 6000 : 3000, leader.Speed);
            Assert.Equal(WorldTargetKind.Craft, follower.Destination!.Kind);
            Assert.Equal(1, follower.Destination.Id);
            var leaderBefore = new WorldPosition(1, 0.6);
            var leaderPosition = new WorldPosition(leader.Longitude, leader.Latitude);
            var followerPosition = new WorldPosition(follower.Longitude, follower.Latitude);
            Assert.Equal(WorldGeometry.Move(new(1.0004, 0.6002), reverse ? leaderBefore : leaderPosition, WorldGeometry.RadianSpeed(ordinary ? 2000 : 6000)), followerPosition);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            if (!ordinary) Assert.Equal(reverse ? 2 : 1, after.World.Ufos[0].Destination!.Id);
            else Assert.True(after.World.Ufos[0].Destination!.Id is 1 or 2);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "optional-base-speed-escort.sav");
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
    public void OrdinaryEscortBurnAlternativesStillStopBeforeResearch(bool cache)
    {
        var content = StrategicWorldOptionalBaseCraftTests.Content(cache, itemFuel: true);
        var initial = Snapshot(content, 17, false, 0);
        initial = initial with
        {
            Bases = [initial.Bases[0] with { Crafts =
            [initial.Bases[0].Crafts[0], initial.Bases[0].Crafts[1] with { RuleId = "SHIP_SLOW_REFUEL" }] }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien base self-destruction requires craft escort speed alternatives for uncertain research.");
    }

    internal static CampaignSnapshot NormalEscort(CampaignSnapshot initial) => initial with
    {
        Bases = [initial.Bases[0] with { Crafts = initial.Bases[0].Crafts.Select(craft => craft.Id != 2 ? craft : craft with
        { RuleId = "OPTIONAL_NORMAL_ESCORT", Logistics = craft.Logistics! with { Speed = 2000 } }).ToArray() }],
    };

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool reverse, int takeoff)
    {
        var initial = StrategicWorldOptionalBaseSpeedCraftTests.Snapshot(content, seed, true, takeoff);
        var leader = initial.Bases[0].Crafts[0];
        var follower = leader with
        {
            Id = 2,
            Logistics = leader.Logistics! with
            {
                Speed = 6000,
                Takeoff = 0,
                Longitude = 1.0004,
                Latitude = 0.6002,
                Destination = new(WorldTargetKind.Craft, "SHIP", leader.Id, 1, 0.6),
            },
        };
        return initial with { Bases = [initial.Bases[0] with { Crafts = reverse ? [follower, leader] : [leader, follower] }] };
    }
}
