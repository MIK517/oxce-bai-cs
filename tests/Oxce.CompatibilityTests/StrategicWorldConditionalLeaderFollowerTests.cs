using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldConditionalLeaderFollowerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ConditionalAirArrivalPreservesOrderedFollowerChains(bool reverse, bool pursuit)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        Assert.Equal(6, TestFixtures.Rows(oracle.RootElement, "idleEscortFollowerMovement").Length);
        var content = StrategicWorldConditionalLeaderArrivalTests.Content("crafts: [{type: SHIP, radarRange: 20000, radarChance: 100}]\n");
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value;
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var initial = StrategicWorldRegionalLeaderFollowerTests.Snapshot(content, seed, reverse);
            initial = initial with
            {
                World = initial.World with
                {
                    Missions = [initial.World.Missions[0] with { RegionId = "REGION" }],
                    Ufos = initial.World.Ufos.Select(ufo => ufo.UniqueId == 9 ? ufo with { TrajectoryPoint = 0 } : ufo).ToArray(),
                },
            };
            var origin = WorldGeometry.RandomPoint(region, 5, -1, new SplitMix64RandomSource(seed));
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
            var leader = after.World.Ufos.Skip(3).FirstOrDefault(ufo => !ufo.HunterKiller);
            var hasLeader = leader is not null;
            var expected = new Dictionary<int, WorldPosition>();
            foreach (var actor in initial.World.Ufos)
            {
                var targetId = actor.Destination!.UniqueId;
                var target = actor.UniqueId == 9 ? hasLeader ? origin : actor.Position : expected.TryGetValue(targetId, out var moved)
                    ? moved : initial.World.Ufos.Single(ufo => ufo.UniqueId == targetId).Position;
                expected.Add(actor.UniqueId, actor.UniqueId == 9 && !hasLeader ? actor.Position :
                    WorldGeometry.Move(actor.Position, target, WorldGeometry.RadianSpeed(2200)));
            }
            Assert.Equal(6, after.World.Ufos.Count);
            Assert.Equal(6, Assert.Single(after.World.Missions).LiveUfos);
            Assert.Equal(18, after.Regions[0].ActivityAlien[^1]);
            foreach (var actor in after.World.Ufos.Take(3))
            {
                Assert.Equal(expected[actor.UniqueId], actor.Position);
                Assert.Equal(actor.UniqueId != 9 || hasLeader, actor.Escorting);
                Assert.False(actor.Hunting);
                Assert.Equal(UfoStatus.Flying, actor.Status);
                Assert.Equal(actor.UniqueId == 9 ? hasLeader ? 0 : 1 : 2, actor.TrajectoryPoint);
                Assert.Equal(actor.UniqueId == 9 && !hasLeader ? 1760 : 2200, actor.Speed);
                if (actor.UniqueId != 9 || hasLeader)
                    Assert.Equal(actor.UniqueId == 9 ? leader!.UniqueId : actor.UniqueId - 1, actor.Destination!.UniqueId);
                else Assert.Equal(WorldTargetKind.Waypoint, actor.Destination!.Kind);
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

}
