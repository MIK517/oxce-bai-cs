using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldPassiveLeaderFollowerTests
{
    [Theory]
    [InlineData(false, false, "ignored")]
    [InlineData(true, false, "ignored")]
    [InlineData(false, true, "ignored")]
    [InlineData(true, true, "ignored")]
    [InlineData(false, false, "crashed")]
    [InlineData(true, false, "crashed")]
    [InlineData(false, true, "crashed")]
    [InlineData(true, true, "crashed")]
    [InlineData(false, false, "expired")]
    [InlineData(true, false, "expired")]
    [InlineData(false, true, "expired")]
    [InlineData(true, true, "expired")]
    [InlineData(false, false, "destroyed")]
    [InlineData(true, false, "destroyed")]
    [InlineData(false, true, "destroyed")]
    [InlineData(true, true, "destroyed")]
    [InlineData(false, false, "reactivated")]
    [InlineData(true, false, "reactivated")]
    [InlineData(false, true, "reactivated")]
    [InlineData(true, true, "reactivated")]
    public void PassiveFollowersCutPositionDependenceAfterTimedTransitions(bool cache, bool reverse, string kind)
    {
        TestFixtures.LoadVerifiedManifest("strategic-crashed-ufo");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        var content = StrategicWorldRegionalNewLeaderTests.Content(cache, true, """
            alienDeployments:
              - {type: PASSIVE_SITE, markerName: PASSIVE_SITE, duration: [1, 1], points: 0, despawnPenalty: 0}
            """);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("LEADER_REGION")].Value;
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var initial = StrategicWorldRegionalLeaderFollowerTests.Snapshot(content, seed, reverse);
            var node = initial.World.Ufos.Single(ufo => ufo.UniqueId == 10) with
            {
                Status = kind is "ignored" or "reactivated" ? UfoStatus.IgnoreMe : kind == "destroyed" ? UfoStatus.Destroyed : UfoStatus.Crashed,
                Altitude = WorldAltitudes.Ground,
                Speed = 0,
                SecondsRemaining = kind == "expired" ? 1800 : kind == "crashed" ? 3600 : 10,
            };
            initial = initial with
            {
                World = initial.World with
                {
                    Ufos = initial.World.Ufos.Select(ufo => ufo.UniqueId == 10 ? node : ufo).ToArray(),
                    MissionSites = kind == "reactivated" ? [new(1, "FIRST_ARRIVAL_MISSION", "PASSIVE_SITE", "RACE_A",
                        node.Longitude, node.Latitude, 0) { Detected = true, UfoUniqueId = node.UniqueId }] : [],
                },
            };
            var origin = WorldGeometry.RandomPoint(region, 5, -1, new SplitMix64RandomSource(seed));
            var expected = new Dictionary<int, WorldPosition>();
            foreach (var actor in initial.World.Ufos)
            {
                var targetId = actor.Destination!.UniqueId;
                var target = actor.UniqueId == 9 ? origin : expected.TryGetValue(targetId, out var moved)
                    ? moved : initial.World.Ufos.Single(ufo => ufo.UniqueId == targetId).Position;
                expected.Add(actor.UniqueId, actor.UniqueId == 10 && kind != "reactivated" ? actor.Position :
                    WorldGeometry.Move(actor.Position, target, WorldGeometry.RadianSpeed(2200)));
            }
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var removed = kind is "expired" or "destroyed";
            Assert.Empty(after.World.MissionSites);
            Assert.Equal(removed ? 5 : 6, after.World.Ufos.Count);
            Assert.Equal(removed ? 5 : 6, Assert.Single(after.World.Missions).LiveUfos);
            Assert.Equal(15, after.Regions[0].ActivityAlien[^1]);
            foreach (var actor in after.World.Ufos.Where(ufo => ufo.UniqueId is 9 or 10 or 11))
                Assert.Equal(expected[actor.UniqueId], actor.Position);
            var source = after.World.Ufos.Single(ufo => ufo.UniqueId == 9);
            Assert.True(source.Escorting);
            var follower = after.World.Ufos.Single(ufo => ufo.UniqueId == 11);
            Assert.Equal(!removed, follower.Escorting);
            if (removed)
            {
                Assert.Equal(WorldTargetKind.Waypoint, follower.Destination!.Kind);
                Assert.Equal(initial.World.Ufos.Single(ufo => ufo.UniqueId == 11).OriginalWaypoint, follower.Destination.Position);
            }
            else
            {
                var actual = after.World.Ufos.Single(ufo => ufo.UniqueId == 10);
                Assert.Equal(kind == "reactivated" ? UfoStatus.Flying : node.Status, actual.Status);
                Assert.Equal(kind == "crashed" ? 1800 : 10, actual.SecondsRemaining);
                Assert.Equal(kind == "reactivated" ? 2200 : 0, actual.Speed);
                Assert.Equal(10, actual.Shield);
                Assert.Equal(source.Position, actual.Destination!.Position);
                Assert.Equal(10, follower.Destination!.UniqueId);
            }
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "passive-leader-chain.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 3);
            AdvanceUnblocked(campaign, 2);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
    }
}
