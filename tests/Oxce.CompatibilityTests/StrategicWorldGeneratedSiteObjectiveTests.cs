using System.Globalization;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldAlienBaseGenerationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedSiteObjectivePersistsSelectedAreaAndConvertsAfterTravel(bool cache, bool hunting)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        foreach (var landing in new[] { false, true })
        {
            var content = GeneratedSiteObjectiveContent(cache, hunting: hunting, landing: landing);
            HashSet<int> selected = [];
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var initial = GeneratedSiteObjectiveSnapshot(content, hunting: hunting, seed: seed);
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)),
                    seed, "daily-objective-before.sav");
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var mission = Assert.Single(after.World.Missions);
                selected.Add(mission.MissionSiteZoneArea);
                Assert.Equal((1, 1), (mission.NextWave, mission.LiveUfos));
                var native = TestFixtures.Rows(oracle.RootElement, "selectedSiteRows").Single(row =>
                    row[0].GetInt32() == (landing ? 1 : 0) && row[1].GetInt32() == mission.MissionSiteZoneArea);
                var born = Assert.Single(after.World.Ufos);
                Assert.Equal(native[2].GetString(), born.Destination!.Longitude.ToString("F9", CultureInfo.InvariantCulture));
                Assert.Equal(native[3].GetString(), born.Destination.Latitude.ToString("F9", CultureInfo.InvariantCulture));
                Assert.Equal(initial.Regions[0].ActivityAlien[^1] + (hunting ? 0 : 3), after.Regions[0].ActivityAlien[^1]);
                Assert.Equal(hunting ? 0 : 1, after.World.AlienBases[0].GenMissionCount);
                if (hunting)
                {
                    Assert.False(born.Detected);
                    Assert.Equal(0, after.World.AlienBases[0].MinutesSinceLastHuntMissionGeneration);
                }
                Assert.Empty(after.World.MissionSites);
                campaign = Reload(content, campaign, seed, "daily-objective-born.sav");
                AdvanceUnblocked(campaign, 500);
                var site = Assert.Single(campaign.Capture().World.MissionSites);
                Assert.Equal("GENERATED_SITE_DEPLOY", site.DeploymentId);
                Assert.Equal(10, site.Texture);
                // The reference enters the next altitude at its current destination;
                // the newly selected departure point does not relocate this landing.
                Assert.Equal(born.Destination.Longitude, site.Longitude);
                Assert.Equal(born.Destination.Latitude, site.Latitude);
                campaign = Reload(content, campaign, seed, "daily-objective-converted.sav");
                AdvanceUnblocked(campaign, 2000);
                Reload(content, campaign, seed, "daily-objective-expired.sav");
                Assert.Empty(campaign.Capture().World.MissionSites);
                Assert.Empty(campaign.Capture().World.Ufos);
            }
            Assert.Equal(Enumerable.Range(0, 2), selected.Order());
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 257)]
    [InlineData(true, 257)]
    public void GeneratedSiteObjectiveRefusesEmptyOrExcessiveAreaChoicesAtomically(bool cache, int areas)
    {
        foreach (var hunting in new[] { false, true })
        {
            var content = GeneratedSiteObjectiveContent(cache, hunting: hunting, areaCount: areas);
            var initial = GeneratedSiteObjectiveSnapshot(content, hunting: hunting, seed: 41);
            AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)), areas == 0
                ? "Mission zone 1 has no areas."
                : "Immediate generated site areas exceed the supported limit.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedSiteObjectiveOpeningsComposeForSeventyTwoHours(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        foreach (var hunting in new[] { false, true })
        {
            var content = GeneratedSiteObjectiveContent(cache, hunting);
            var initial = GeneratedSiteObjectiveSnapshot(content, hunting, 41) with
            { Time = new(1, 1, 1, 1999, 23, 59, 55) };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(41));
            AdvanceUnblocked(campaign, 1);
            Assert.Single(campaign.Capture().World.Ufos);
            campaign = Reload(content, campaign, 41, "site-composition-birth.sav");
            for (var day = 0; day < 3; day++)
            {
                AdvanceUnblocked(campaign, 17_280);
                campaign = Reload(content, campaign, 41, $"site-composition-day-{day}.sav");
            }
            var after = campaign.Capture();
            Assert.Equal(5, after.Time.Day);
            Assert.Empty(after.World.Ufos);
            Assert.Empty(after.World.Missions);
            Assert.Empty(after.World.MissionSites);
            Assert.Equal(hunting ? 0 : 1, after.World.AlienBases[0].GenMissionCount);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void GeneratedSiteAreasReserveOnePossibleMissionContactChain(bool cache, int count)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        foreach (var areas in new[] { 2, 256 })
        {
            var content = GeneratedSiteObjectiveContent(cache, hunting: false, areaCount: areas, ufoCount: count);
            var initial = GeneratedSiteObjectiveSnapshot(content, hunting: false, seed: 41)
                .WithFacility("RADAR_TEST").WithNextId("STR_UFO", int.MaxValue - count);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(41));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(count, after.World.Ufos.Count);
            Assert.All(after.World.Ufos, ufo => Assert.True(ufo.Detected));
            Assert.Equal(Enumerable.Range(int.MaxValue - count, count), after.World.Ufos.Select(ufo => ufo.Id));
            Assert.Equal(int.MaxValue, after.NextIds["STR_UFO"]);
            Reload(content, campaign, 41, "site-last-contact-chain.sav");
            AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO", int.MaxValue - count + 1),
                content, new SplitMix64RandomSource(41)),
                "UFO contact marker IDs are exhausted or collide with a saved UFO.");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedSiteHunterProofCannotLeakBetweenSelectedAreaOrigins(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        foreach (var hunting in new[] { false, true })
        {
            var content = GeneratedSiteObjectiveContent(cache, hunting, selectedOrigin: true);
            var initial = GeneratedSiteObjectiveSnapshot(content, hunting, 41);
            var owner = initial.Bases[0];
            initial = initial with
            {
                Bases = [owner with
                {
                    Crafts = [new("GENERATED_SITE_CRAFT", 1)
                    {
                        Logistics = CraftLogistics.LoadStarting(content.RuntimeRules.Crafts[
                            content.RuntimeRules.Crafts.GetRequired("GENERATED_SITE_CRAFT")].Value, null) with
                        {
                            Status = "STR_OUT", Longitude = Degrees(12), Latitude = Degrees(-2),
                            Fuel = 1000, Weapons = [],
                        },
                    }],
                    Soldiers = [],
                }],
            };
            // The first area is outside radar; the second starts on the craft.
            // Both share the unallocated mission ID, rule, race and source.
            AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)),
                "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedSiteAreaChoicesDoNotDuplicateAreaNeutralBirthAndTakeoffAwards(bool cache, bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        foreach (var ground in new[] { false, true })
            foreach (var areas in new[] { 2, 256 })
            {
                var content = GeneratedSiteObjectiveContent(cache, hunting, areaCount: areas, groundBirth: ground);
                var score = (hunting ? 0 : ground ? 6 : 3) + (ground ? 7 : 0);
                var initial = GeneratedSiteObjectiveSnapshot(content, hunting, 41);
                initial = initial with
                { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - score] }, .. initial.Regions.Skip(1)] };
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(41));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                Assert.Single(after.World.Ufos);
                Assert.Equal(int.MaxValue, after.Regions[0].ActivityAlien[^1]);
                Reload(content, campaign, 41, "site-area-neutral-awards.sav");
                if (score == 0) continue;
                var limited = initial with
                { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - score + 1] }, .. initial.Regions.Skip(1)] };
                AssertTimeBlocked(CampaignState.Restore(limited, content, new SplitMix64RandomSource(41)),
                    "UFO alien activity exceeds the supported range.");
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedSiteObjectiveValidatesSpawnZoneBeforeProjectingAreas(bool cache)
    {
        foreach (var hunting in new[] { false, true })
            foreach (var zone in new[] { -1, 7 })
            {
                var content = GeneratedSiteObjectiveContent(cache, hunting, spawnZone: zone);
                var initial = GeneratedSiteObjectiveSnapshot(content, hunting, 41);
                AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)), hunting
                    ? "Alien base hunting site mission has an invalid spawn zone."
                    : "Alien base site mission has an invalid spawn zone.");
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedSiteActivityReservationsKeepDistinctSourcesAndBirthCounters(bool cache)
    {
        var content = GeneratedSiteObjectiveContent(cache, hunting: false, ufoCount: 2);
        var initial = GeneratedSiteObjectiveSnapshot(content, hunting: false, seed: 41);
        initial = initial with
        {
            World = initial.World with
            {
                AlienBases = [initial.World.AlienBases[0], new(3, "GENERATED_SITE_BASE", "RACE_A", 0.2, 0.1)],
            },
            Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 12] }, .. initial.Regions.Skip(1)],
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(41));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(4, campaign.Capture().World.Ufos.Count);
        Assert.Equal(int.MaxValue, campaign.Capture().Regions[0].ActivityAlien[^1]);
        Reload(content, campaign, 41, "site-distinct-activity.sav");
        var limited = initial with
        { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 11] }, .. initial.Regions.Skip(1)] };
        AssertTimeBlocked(CampaignState.Restore(limited, content, new SplitMix64RandomSource(41)),
            "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedActivityProjectionWorkIncludesCandidateCopiesAndMerges(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        foreach (var count in new[] { 500, 600 })
        {
            var content = GeneratedSiteObjectiveContent(cache, hunting: false, areaCount: 256, extraRegions: count);
            var initial = GeneratedSiteObjectiveSnapshot(content, hunting: false, seed: 41);
            initial = initial with
            { Regions = [.. initial.Regions, .. Enumerable.Range(0, count).Select(index => new RegionSnapshot($"GENERATED_ACTIVITY_REGION_{index}", [0], [0]))] };
            Assert.Equal(count + 2, initial.Regions.Count);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(41));
            if (count == 600)
                AssertTimeBlocked(campaign, "Alien mission activity forecasts exceed the supported work limit.");
            else
            {
                AdvanceUnblocked(campaign, 1);
                Assert.Single(campaign.Capture().World.Ufos);
                Reload(content, campaign, 41, "generated-activity-bounded-regions.sav");
            }
        }
    }

    private static RuntimeContent GeneratedSiteObjectiveContent(bool cache, bool hunting, bool landing = false,
        int? areaCount = null, int ufoCount = 1, bool selectedOrigin = false, bool groundBirth = false, int spawnZone = 1,
        int extraRegions = 0) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-selected-site-waypoints.rul"], cache, $$$"""
            {{{(areaCount is null ? "" : GeneratedSiteAreas(areaCount.Value) + string.Concat(Enumerable.Range(0, extraRegions)
                .Select(index => $"\n  - type: GENERATED_ACTIVITY_REGION_{index}\n    cost: 0\n")))}}}
            crafts:
              - {type: GENERATED_SITE_CRAFT, speedMax: 2000, damageMax: 100, fuelMax: 1000, soldiers: 0, allowLanding: false, patrolWithoutFuel: true}
            ufoTrajectories:
              - id: GENERATED_SITE_ORIGIN_PATH
                waypoints: [[1, 3, 100], [4, 2, 80], [1, 0, 50], [6, 4, 100]]
              - id: GENERATED_SITE_GROUND_PATH
                groundTimer: 1
                waypoints: [[4, 0, 100], [1, 2, 80], [6, 0, 50], [6, 4, 100]]
            alienDeployments:
              - type: GENERATED_SITE_BASE
                markerName: STR_ALIEN_BASE
                genMission: {GENERATED_SITE_MISSION: 1}
                genMissionFreq: {{{(hunting ? 0 : 100)}}}
                genMissionLimit: 1
                baseDetectionRange: {{{(hunting ? 500 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: 5400
                huntMissionWeights: {0: {GENERATED_SITE_MISSION: 1}}
              - type: GENERATED_SITE_DEPLOY
                markerName: GENERATED_SITE_MARKER
                duration: [2, 2]
                points: 2
                race: RACE_A
            alienMissions:
              - type: GENERATED_SITE_MISSION
                objective: 3
                points: {{{(groundBirth ? 7 : 0)}}}
                spawnZone: {{{spawnZone}}}
                siteType: GENERATED_SITE_DEPLOY
                {{{(areaCount is null ? "" : "regionWeights: {0: {GENERATED_SITE_REGION: 1}}")}}}
                waves: [{ufo: UFO_SCOUT, count: {{{ufoCount}}}, trajectory: {{{(selectedOrigin ? "GENERATED_SITE_ORIGIN_PATH" : groundBirth ? "GENERATED_SITE_GROUND_PATH" : "SELECTED_SITE")}}}, timer: 0, objective: true, hunterKillerPercentage: {{{(selectedOrigin ? 100 : 0)}}}, objectiveOnTheLandingSite: {{{landing.ToString().ToLowerInvariant()}}}}]
            """);

    private static string GeneratedSiteAreas(int count) => $$$"""
        regions:
          - type: GENERATED_SITE_REGION
            areas: [[0, 360, -90, 90]]
            missionZones:
              - [[2, 28, -18, 18, 10]]
              - [{{{string.Join(", ", Enumerable.Repeat("[8, 8, 2, 2, 10, CITY_ALPHA]", count))}}}]
              - [[22, 28, -8, 8, 11]]
              - [[362, 362, 2, 2, 10]]
              - [[10, 10, 8, 8, 10]]
              - [[8, 8, 2, 2, 10]]
              - [[8.01, 8.01, 2, 2, 10]]
        """;

    private static CampaignSnapshot GeneratedSiteObjectiveSnapshot(RuntimeContent content, bool hunting, ulong seed)
    {
        var initial = HuntingGeneratedWaveSnapshot(content, hunting ? new(1, 1, 1, 1999, 0, 29, 55)
            : new(1, 1, 1, 1999, 23, 59, 55));
        return initial with
        {
            RandomState = seed,
            World = new()
            {
                AlienBases = [new(2, "GENERATED_SITE_BASE", "RACE_A", 0.2, 0.1)
                { MinutesSinceLastHuntMissionGeneration = hunting ? 5390 : 0 }],
            },
        };
    }
}
