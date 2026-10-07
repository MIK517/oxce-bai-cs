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
    [InlineData(false)]
    [InlineData(true)]
    public void DailyGeneratedSiteObjectivePersistsSelectedAreaAndConvertsAfterTravel(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        foreach (var landing in new[] { false, true })
        {
            var content = GeneratedSiteObjectiveContent(cache, hunting: false, landing: landing);
            HashSet<int> selected = [];
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var initial = GeneratedSiteObjectiveSnapshot(content, hunting: false, seed: seed);
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
                Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 3, after.Regions[0].ActivityAlien[^1]);
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
        var content = GeneratedSiteObjectiveContent(cache, hunting: false, areaCount: areas);
        var initial = GeneratedSiteObjectiveSnapshot(content, hunting: false, seed: 41);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)), areas == 0
            ? "Mission zone 1 has no areas."
            : "Immediate generated site areas exceed the supported limit.");
    }

    private static RuntimeContent GeneratedSiteObjectiveContent(bool cache, bool hunting, bool landing = false,
        int? areaCount = null) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-selected-site-waypoints.rul"], cache, $$$"""
            {{{(areaCount is null ? "" : GeneratedSiteAreas(areaCount.Value))}}}
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
                spawnZone: 1
                siteType: GENERATED_SITE_DEPLOY
                {{{(areaCount is null ? "" : "regionWeights: {0: {GENERATED_SITE_REGION: 1}}")}}}
                waves: [{ufo: UFO_SCOUT, count: 1, trajectory: SELECTED_SITE, timer: 0, objective: true, objectiveOnTheLandingSite: {{{landing.ToString().ToLowerInvariant()}}}}]
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
