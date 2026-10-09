using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldRegionalNewLeaderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void IdleEscortUsesTheActualFirstRegionalLeaderWithoutGuessingItsIdentity(bool cache, bool multiple)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = Content(cache, multiple);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("LEADER_REGION")].Value;
        for (ulong seed = 1; seed <= 16; seed++)
        {
            var initial = Snapshot(content, seed);
            var random = new SplitMix64RandomSource(seed);
            var origins = new WorldPosition[3];
            var destinations = new WorldPosition[3];
            for (var wave = 0; wave < 3; wave++)
            {
                origins[wave] = WorldGeometry.RandomPoint(region, 5, -1, random);
                destinations[wave] = WorldGeometry.RandomPoint(region, 5, -1, random);
                if (wave < 2) WorldTrajectory.SpawnCountdown(0, random);
            }
            for (var actor = 0; actor < 4; actor++) random.NextInclusive(0, 99);
            for (var wave = 0; wave < 3; wave++)
                if (WorldGeometry.Distance(origins[wave], destinations[wave]) <= WorldGeometry.RadianSpeed(2200))
                {
                    _ = WorldGeometry.RandomPoint(region, 5, -1, random);
                    _ = WorldGeometry.RandomPoint(region, 5, -1, random);
                }
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(random.State, after.RandomState);
            Assert.Equal(4, after.World.Ufos.Count);
            var source = after.World.Ufos[0];
            var leader = after.World.Ufos[1];
            Assert.Equal(9, source.UniqueId);
            Assert.True(source.Escorting);
            Assert.False(source.Hunting);
            Assert.Equal(leader.UniqueId, source.Destination!.UniqueId);
            Assert.Equal(WorldTargetKind.Ufo, source.Destination.Kind);
            Assert.Equal(initial.World.Ufos[0].Destination!.Position, source.OriginalWaypoint);
            Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, origins[0], WorldGeometry.RadianSpeed(2200)), source.Position);
            Assert.Equal(2, source.TrajectoryPoint);
            Assert.Equal(2200, source.Speed);
            Assert.Equal(UfoStatus.Flying, source.Status);
            Assert.Equal(4, Assert.Single(after.World.Missions).LiveUfos);
            Assert.Equal(12, after.Regions[0].ActivityAlien[^1]);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "regional-new-leader.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 3);
            AdvanceUnblocked(campaign, 2);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
    }

    internal static RuntimeContent Content(bool cache, bool multiple = false) =>
        StrategicWorldNewbornFirstArrivalTests.Content(cache, false, extraRules: $$$"""
            regions:
              - type: LEADER_REGION
                areas: [[0, 30, -20, 20]]
                missionZones:
                  - [[8, 8.02, 2, 2.02]]
                  - [[8, 8.02, 2, 2.02]]
                  - [[8, 8.02, 2, 2.02]]
                  - [[8, 8.02, 2, 2.02]]
                  - [[10, 10, 8, 8]]
                  - [[8, 8.02, 2, 2.02]{{{(multiple ? ", [8.04, 8.06, 2, 2.02]" : "")}}}]
            """);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed)
    {
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed);
        var position = new WorldPosition(Degrees(8.1), Degrees(2));
        return initial with
        {
            Bases = [initial.Bases[0] with { Crafts = [], Soldiers = [] }],
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RegionId = "LEADER_REGION", LiveUfos = 1 }],
                Ufos = [new(9, "FIRST_ARRIVAL_UFO", 4, "FIRST_ARRIVAL_AIR", 2,
                    position.Longitude, position.Latitude, UfoStatus.Flying, WorldAltitudes.High)
                {
                    Id = 9, HunterKiller = true, Escort = true, Speed = 550, Shield = 10,
                    MissionWaveNumber = 0, Destination = WorldTargetReference.ForWaypoint(0, position),
                }],
            },
        };
    }
}
