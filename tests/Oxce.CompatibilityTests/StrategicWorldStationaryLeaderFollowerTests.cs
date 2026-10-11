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
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void LandedFollowerCutsRegionalPositionDependencyWithoutMoving(bool reverse, int timer)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var content = StrategicWorldRegionalNewLeaderTests.Content(true);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("LEADER_REGION")].Value;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = Snapshot(content, seed, reverse, timer);
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
            Assert.Equal(timer == 5 ? UfoStatus.Flying : UfoStatus.Landed, actualGround.Status);
            Assert.Equal(timer == 5 ? WorldAltitudes.VeryLow : WorldAltitudes.Ground, actualGround.Altitude);
            Assert.Equal(timer - 5, actualGround.SecondsRemaining);
            Assert.Equal(timer == 5 ? 2200 : 0, actualGround.Speed);
            if (timer == 5)
            {
                var target = reverse ? source.Position : actualSource.Position;
                var vector = WorldGeometry.SpeedVector(grounded.Position, target, WorldGeometry.RadianSpeed(2200));
                Assert.Equal(vector.Longitude, actualGround.SpeedLongitude);
                Assert.Equal(vector.Latitude, actualGround.SpeedLatitude);
                Assert.Equal(WorldAltitudes.Direction(vector.Longitude, vector.Latitude), actualGround.Direction);
            }
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

    [Theory]
    [InlineData(10, "ground-far")]
    [InlineData(5, "ground-far")]
    [InlineData(10, "chain-far")]
    [InlineData(5, "chain-far")]
    [InlineData(10, "ground-close")]
    [InlineData(5, "ground-close")]
    public void StationaryAndLiftingLinksRetainCraftArrivalOwnership(int timer, string kind)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = StrategicWorldRegionalNewLeaderTests.Content(true,
            "crafts: [{type: SHIP, allowLanding: true, radarRange: 20000, radarChance: 100}]\n");
        var initial = Snapshot(content, 41, false, timer);
        var target = initial.World.Ufos.Single(ufo => ufo.UniqueId == (kind == "chain-far" ? 11 : 10));
        var close = kind == "ground-close";
        var from = close ? target.Position : new WorldPosition(1, 0.6);
        initial = initial with { Bases = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, 41).Bases };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 1000,
                Damage = 0,
                Speed = 6000,
                Takeoff = 0,
                Longitude = from.Longitude,
                Latitude = from.Latitude,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, target.Id,
                    target.Longitude, target.Latitude)
                { UniqueId = target.UniqueId },
            },
        });
        var owner = initial.Bases[0];
        initial = initial with
        {
            Bases = [owner with
            {
                Soldiers = [owner.Soldiers[0] with
                    { Personal = owner.Soldiers[0].Personal! with { CraftType = "SHIP", CraftId = 1 } }, .. owner.Soldiers.Skip(1)],
            }],
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(41));
        if (close)
        {
            AssertTimeBlocked(campaign, timer == 5 ? "Craft interception requires strategic combat (Phase 6 branch 5)."
                : "Craft landing requires deployment (Phase 6 branch 5).");
            return;
        }
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var actualTarget = after.World.Ufos.Single(ufo => ufo.UniqueId == target.UniqueId);
        var craft = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal(target.UniqueId, craft.Destination!.UniqueId);
        Assert.Equal(actualTarget.Position, craft.Destination.Position);
        Assert.Equal(WorldGeometry.Move(from, actualTarget.Position, WorldGeometry.RadianSpeed(6000)),
            new(craft.Longitude, craft.Latitude));
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 41, "stationary-leader-pursuit.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
        var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(41));
        AdvanceUnblocked(control, 3);
        AdvanceUnblocked(campaign, 2);
        Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
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
