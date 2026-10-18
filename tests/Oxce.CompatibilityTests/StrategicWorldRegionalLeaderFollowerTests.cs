using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldRegionalLeaderFollowerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FlyingFollowerChainsUseNativeSavedOrderAndRealRegionalLeader(bool reverse, bool pursuit)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        Assert.Equal(6, TestFixtures.Rows(oracle.RootElement, "idleEscortFollowerMovement").Length);
        var content = StrategicWorldRegionalNewLeaderTests.Content(true,
            "crafts: [{type: SHIP, radarRange: 20000, radarChance: 100}]\n");
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("LEADER_REGION")].Value;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = Snapshot(content, seed, reverse);
            var origin = WorldGeometry.RandomPoint(region, 5, -1, new SplitMix64RandomSource(seed));
            var expected = new Dictionary<int, WorldPosition>();
            foreach (var actor in initial.World.Ufos)
            {
                var targetId = actor.Destination!.UniqueId;
                var target = actor.UniqueId == 9 ? origin : expected.TryGetValue(targetId, out var moved)
                    ? moved : initial.World.Ufos.Single(ufo => ufo.UniqueId == targetId).Position;
                expected.Add(actor.UniqueId, WorldGeometry.Move(actor.Position, target, WorldGeometry.RadianSpeed(2200)));
            }
            if (pursuit)
            {
                initial = initial with { Bases = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed).Bases };
                var target = initial.World.Ufos.Single(ufo => ufo.UniqueId == 11);
                initial = initial.WithCraft("SHIP", craft => craft with
                {
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_OUT",
                        Damage = 0,
                        Fuel = 1000,
                        Speed = 6000,
                        Takeoff = 0,
                        Longitude = 1,
                        Latitude = 0.6,
                        Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, target.Id,
                            target.Longitude, target.Latitude)
                        { UniqueId = target.UniqueId },
                    },
                });
            }
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(6, after.World.Ufos.Count);
            Assert.Equal(6, Assert.Single(after.World.Missions).LiveUfos);
            Assert.Equal(18, after.Regions[0].ActivityAlien[^1]);
            var leader = after.World.Ufos[3];
            foreach (var actor in after.World.Ufos.Take(3))
            {
                Assert.Equal(expected[actor.UniqueId], actor.Position);
                Assert.True(actor.Escorting);
                Assert.False(actor.Hunting);
                Assert.Equal(UfoStatus.Flying, actor.Status);
                Assert.Equal(2, actor.TrajectoryPoint);
                Assert.Equal(2200, actor.Speed);
                Assert.Equal(actor.UniqueId == 9 ? leader.UniqueId : actor.UniqueId - 1, actor.Destination!.UniqueId);
            }
            if (pursuit)
            {
                var craft = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP").Logistics!;
                Assert.Equal(11, craft.Destination!.UniqueId);
                Assert.Equal(expected[11], craft.Destination.Position);
                Assert.Equal(WorldGeometry.Move(new(1, 0.6), expected[11], WorldGeometry.RadianSpeed(6000)),
                    new(craft.Longitude, craft.Latitude));
            }
            campaign = ReloadRepeatedly(content, campaign, seed, "regional-leader-chain.sav", 3, after);
            var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 3);
            AdvanceUnblocked(campaign, 2);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
    }

    [Fact]
    public void MalformedGroundedFollowerRefusesBeforeAnyBirth()
    {
        var content = StrategicWorldRegionalNewLeaderTests.Content();
        var initial = Snapshot(content, 17, false);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0], initial.World.Ufos[1] with
                    { Status = UfoStatus.Landed, Altitude = WorldAltitudes.Ground, Speed = 0, SecondsRemaining = 0 }, initial.World.Ufos[2]],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Regional newborn leader followers require flying escort forecasts.");
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool reverse)
    {
        var initial = StrategicWorldRegionalNewLeaderTests.Snapshot(content, seed);
        var source = initial.World.Ufos[0] with { Detected = true };
        var follower = source with
        {
            UniqueId = 10,
            Id = 10,
            Longitude = Degrees(8.13),
            Latitude = Degrees(2.01),
            Speed = 2200,
            Escorting = true,
            OriginalWaypoint = new(Degrees(10), Degrees(8)),
            Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, source.Id,
                source.Longitude, source.Latitude)
            { UniqueId = source.UniqueId },
        };
        var last = follower with
        {
            UniqueId = 11,
            Id = 11,
            Longitude = Degrees(8.17),
            Latitude = Degrees(2.03),
            Destination = follower.Destination! with
            {
                Id = follower.Id,
                UniqueId = follower.UniqueId,
                Longitude = follower.Longitude,
                Latitude = follower.Latitude
            },
        };
        return initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 3 }],
                Ufos = reverse ? [last, follower, source] : [source, follower, last],
            },
        };
    }
}
