using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornRegionalArrivalTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RectangleBirthsPreserveSelectedArrivalCoordinatesAndDiscardedWaypointDraws(bool cache, bool mixed)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var content = Content(cache, mixed, 0);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("FIRST_REGION")].Value;
        var arrivedSeen = false;
        var flyingSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, 0);
            var expectedRandom = new SplitMix64RandomSource(seed);
            var origins = new WorldPosition[3];
            var destinations = new WorldPosition[3];
            for (var index = 0; index < 3; index++)
            {
                origins[index] = WorldGeometry.RandomPoint(region, 5, -1, expectedRandom);
                destinations[index] = WorldGeometry.RandomPoint(region, 5, -1, expectedRandom);
                if (index < 2) WorldTrajectory.SpawnCountdown(0, expectedRandom);
            }
            for (var index = 0; index < 3; index++) expectedRandom.NextInclusive(0, 99);
            var expected = new (WorldPosition Position, WorldPosition Destination, bool Arrived)[3];
            for (var index = 0; index < 3; index++)
            {
                var arrived = WorldGeometry.Distance(origins[index], destinations[index]) <= WorldGeometry.RadianSpeed(2200);
                var position = WorldGeometry.Move(origins[index], destinations[index], WorldGeometry.RadianSpeed(2200));
                if (arrived)
                {
                    _ = WorldGeometry.RandomPoint(region, 5, -1, expectedRandom);
                    destinations[index] = WorldGeometry.RandomPoint(region, 5, -1, expectedRandom);
                }
                expected[index] = (position, destinations[index], arrived);
                arrivedSeen |= arrived;
                flyingSeen |= !arrived;
            }
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(expectedRandom.State, after.RandomState);
            Assert.Equal(3, after.World.Ufos.Count);
            for (var index = 0; index < 3; index++)
            {
                var ufo = after.World.Ufos[index];
                Assert.Equal(expected[index].Position, ufo.Position);
                Assert.Equal(expected[index].Destination, ufo.Destination!.Position);
                Assert.Equal(expected[index].Arrived ? 1 : 0, ufo.TrajectoryPoint);
                Assert.Equal(expected[index].Arrived ? 1760 : 2200, ufo.Speed);
            }
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "newborn-regional-first.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
        }
        Assert.True(arrivedSeen);
        Assert.Equal(mixed, flyingSeen);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void DailyAndHuntingGeneratorsUseTheSameFirstArrivalProof(bool cache, int generation)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        var content = Content(cache, false, generation);
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = Snapshot(content, seed, generation);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(generation == 3 ? 6 : 3, after.World.Ufos.Count);
            Assert.All(after.World.Ufos, ufo => Assert.Equal(1, ufo.TrajectoryPoint));
            Assert.Equal(generation is 1 or 3 ? 9 : 0, after.Regions[0].ActivityAlien[^1]);
            Assert.Equal(generation is 1 or 3 ? 1 : 0, Assert.Single(after.World.AlienBases).GenMissionCount);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "generated-newborn-first.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            var control = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 2);
            AdvanceUnblocked(campaign, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
    }

    internal static RuntimeContent Content(bool cache, bool mixed, int generation, int firstAltitude = 3, int groundTimer = 0,
        int arrivalAltitude = 2) =>
        StrategicWorldNewbornFirstArrivalTests.Content(cache, false, extraRules: $$$"""
            regions:
              - type: FIRST_REGION
                areas: [[0, 30, -20, 20]]
                missionZones:
                  - [[8, 8.02, 2, 2.02]]
                  - [[8, 8.02, 2, 2.02]]
                  - [[8, 8.02, 2, 2.02]]
                  - [[8, 8.02, 2, 2.02]]
                  - [[10, 10, 8, 8]]
                  - [[8, {{{(mixed ? "8.25" : "8.02")}}}, 2, {{{(mixed ? "2.25" : "2.02")}}}]]
            alienDeployments:
              - type: FIRST_GENERATOR
                markerName: FIRST_SOURCE
                points: 0
                genMission: {FIRST_ARRIVAL_MISSION: 1}
                genMissionFreq: {{{(generation is 1 or 3 ? 100 : 0)}}}
                genMissionLimit: 1
                baseDetectionRange: 3000
                baseDetectionChance: 100
                huntMissionMaxFrequency: 60
                huntMissionWeights: {0: {FIRST_ARRIVAL_MISSION: 1}}
            """, generationRegion: "FIRST_REGION", firstAltitude: firstAltitude, groundTimer: groundTimer,
            arrivalAltitude: arrivalAltitude);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedFirstArrivalsComposeForSeventyTwoHoursAndHourlyReloads(bool cache)
    {
        var content = Content(cache, false, 3);
        for (ulong seed = 1; seed <= 2; seed++)
        {
            var initial = Snapshot(content, seed, 3);
            var batched = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var reloaded = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            for (var day = 0; day < 3; day++)
            {
                AdvanceUnblocked(batched, 17280);
                for (var hour = 0; hour < 24; hour++)
                {
                    AdvanceUnblocked(reloaded, 720);
                    reloaded = Reload(content, reloaded, seed, "newborn-regional-72h.sav");
                }
                Assert.Equivalent(batched.Capture(), reloaded.Capture(), strict: true);
                var after = batched.Capture();
                Assert.Single(after.World.AlienBases);
                Assert.Equal(1, after.World.AlienBases[0].GenMissionCount);
                Assert.InRange(after.World.Ufos.Count, 0, 6);
                Assert.InRange(after.World.Missions.Count, 0, 2);
                Assert.Empty(after.World.MissionSites);
            }
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void GeneratedCloseBirthsReserveFullIdentityEnvelope(bool cache, int generation)
    {
        var content = Content(cache, false, generation);
        var initial = Snapshot(content, 17, generation).WithNextId("STR_UFO_UNIQUE", int.MaxValue - 2);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO unique IDs are exhausted or collide with a saved UFO.");
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int generation)
    {
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed);
        if (generation == 0) return initial with
        { World = initial.World with { Missions = [initial.World.Missions[0] with { RegionId = "FIRST_REGION" }] } };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            { Status = "STR_OUT", Speed = 0, Fuel = 100, Destination = WorldTargetReference.ForWaypoint(1, new(1, 0.6)) },
        });
        return initial with
        {
            Time = new(2, 2, 1, 1999, 23, 59, 55),
            Bases = [initial.Bases[0] with { Soldiers = [], Crafts = generation == 1 ? [] : initial.Bases[0].Crafts }],
            World = initial.World with
            {
                Missions = [],
                Waypoints = [new(1, 1, 0.6)],
                AlienBases = [new(6, "FIRST_GENERATOR", "RACE_A", Degrees(8.01), Degrees(2.01)) { MinutesSinceLastHuntMissionGeneration = 50 }],
            },
        };
    }
}
