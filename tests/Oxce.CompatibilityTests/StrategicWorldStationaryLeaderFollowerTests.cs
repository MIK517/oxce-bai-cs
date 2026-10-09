using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldStationaryLeaderFollowerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void LandedFollowerCutsRegionalPositionDependencyWithoutMoving(bool cache, bool reverse)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var content = StrategicWorldRegionalNewLeaderTests.Content(cache, true);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("LEADER_REGION")].Value;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = Snapshot(content, seed, reverse, 10);
            var source = initial.World.Ufos.Single(ufo => ufo.UniqueId == 9);
            var grounded = initial.World.Ufos.Single(ufo => ufo.UniqueId == 10);
            var follower = initial.World.Ufos.Single(ufo => ufo.UniqueId == 11);
            var origin = WorldGeometry.RandomPoint(region, 5, -1, new SplitMix64RandomSource(seed));
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var actualSource = after.World.Ufos.Single(ufo => ufo.UniqueId == 9);
            var actualGround = after.World.Ufos.Single(ufo => ufo.UniqueId == 10);
            var actualFollower = after.World.Ufos.Single(ufo => ufo.UniqueId == 11);
            Assert.True(actualSource.Escorting);
            Assert.Equal(WorldGeometry.Move(source.Position, origin, WorldGeometry.RadianSpeed(2200)), actualSource.Position);
            Assert.Equal(grounded.Position, actualGround.Position);
            Assert.Equal(UfoStatus.Landed, actualGround.Status);
            Assert.Equal(WorldAltitudes.Ground, actualGround.Altitude);
            Assert.Equal(5, actualGround.SecondsRemaining);
            Assert.Equal(0, actualGround.Speed);
            Assert.Equal(10, actualGround.Shield);
            Assert.True(actualGround.Escorting);
            Assert.Equal(actualSource.Position, actualGround.Destination!.Position);
            Assert.Equal(WorldGeometry.Move(follower.Position, grounded.Position, WorldGeometry.RadianSpeed(2200)), actualFollower.Position);
            Assert.Equal(10, actualFollower.Destination!.UniqueId);
            Assert.Equal(6, Assert.Single(after.World.Missions).LiveUfos);
            Assert.Equal(21, after.Regions[0].ActivityAlien[^1]);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "stationary-leader-chain.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 3);
            AdvanceUnblocked(campaign, 2);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool reverse, int timer)
    {
        var initial = StrategicWorldRegionalLeaderFollowerTests.Snapshot(content, seed, reverse);
        return initial with
        {
            World = initial.World with
            {
                Ufos = initial.World.Ufos.Select(ufo => ufo.UniqueId == 10 ? ufo with
                { Status = UfoStatus.Landed, Altitude = WorldAltitudes.Ground, Speed = 0, SecondsRemaining = timer } : ufo).ToArray(),
            },
        };
    }
}
