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

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void RegionalAttachmentPreservesFarCraftPursuitAndGuardsCloseCombat(bool cache, bool close, bool takeoff)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(cache, true, "crafts: [{type: SHIP, radarRange: 20000, radarChance: 100}]\n");
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = Snapshot(content, seed);
            var source = initial.World.Ufos[0] with { Detected = true };
            var from = close ? source.Position : new WorldPosition(1, 0.6);
            initial = initial with
            {
                Bases = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed).Bases,
                World = initial.World with { Ufos = [source] },
            };
            initial = initial.WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT",
                    Fuel = 1000,
                    Damage = 0,
                    Speed = 6000,
                    Takeoff = takeoff ? 1 : 0,
                    Longitude = from.Longitude,
                    Latitude = from.Latitude,
                    Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, source.Id,
                        source.Longitude, source.Latitude)
                    { UniqueId = source.UniqueId },
                },
            });
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            if (close)
            {
                AssertTimeBlocked(campaign, "Loaded idle escorts require same-tick newborn craft pursuit forecasts.");
                continue;
            }
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var escort = after.World.Ufos[0];
            var state = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP").Logistics!;
            Assert.True(escort.Escorting);
            Assert.True(escort.Detected);
            Assert.Equal(escort.UniqueId, state.Destination!.UniqueId);
            Assert.Equal(escort.Position, state.Destination.Position);
            var expected = takeoff ? from : WorldGeometry.Move(from, escort.Position, WorldGeometry.RadianSpeed(6000));
            Assert.Equal(expected, new(state.Longitude, state.Latitude));
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "regional-leader-pursuit.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 3);
            AdvanceUnblocked(campaign, 2);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalDailyInterruptionCannotProveGuaranteedPointLeaderAttachment(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = StrategicWorldNewbornFirstArrivalTests.Content(cache, false, interrupt: "BONUS", extraRules: """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
              - {name: BONUS, cost: 1}
              - {name: OTHER, cost: 1}
            """);
        var initial = Snapshot(content, 17);
        initial = initial with
        {
            Time = new(2, 2, 1, 1999, 23, 59, 55),
            Bases = [initial.Bases[0] with { Research = [new("CORE", 0, 1, 1)] }],
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RegionId = "REGION" }],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Loaded idle escorts require same-tick newborn waypoint forecasts.");
    }

    internal static RuntimeContent Content(bool cache, bool multiple = false, string extraRules = "") =>
        StrategicWorldNewbornFirstArrivalTests.Content(cache, false, extraRules: $$$"""
            {{{extraRules}}}
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
