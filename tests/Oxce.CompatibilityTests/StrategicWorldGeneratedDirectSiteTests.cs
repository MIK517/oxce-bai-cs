using System.Globalization;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
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
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 5)]
    [InlineData(true, false, 5)]
    [InlineData(false, true, 5)]
    [InlineData(true, true, 5)]
    [InlineData(false, false, 3)]
    [InlineData(true, false, 3)]
    [InlineData(false, true, 3)]
    [InlineData(true, true, 3)]
    public void GeneratedDeploymentWavesCreateAndExpireSitesWithoutUfosAtBothCallers(bool cache, bool hunting, int objective)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        var native = TestFixtures.Rows(oracle.RootElement, "cases").Single(row => row.GetProperty("name").GetString() == "selected")
            .GetProperty("site");
        var content = GeneratedDirectSiteContent(cache, hunting, objective: objective);
        HashSet<string> cities = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = GeneratedDirectSiteSnapshot(content, seed, hunting).WithNextId("DIRECT_MARKER", 7);
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)),
                seed, "generated-direct-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var site = Assert.Single(after.World.MissionSites);
            cities.Add(site.City);
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(new object[]
                { native[0].GetString()!, native[5].GetString()!, native[6].GetInt32() })),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(new object[] { site.DeploymentId, site.Race, site.Texture })));
            Assert.Equal(7, site.Id);
            Assert.False(site.Detected);
            var originalTimer = site.SecondsRemaining + (hunting ? 0 : 1800);
            Assert.InRange(originalTimer, 7200, 10800);
            Assert.Equal(0, originalTimer % 3600);
            if (site.City == "CITY_SELECTED")
            {
                Assert.Equal(native[1].GetString(), site.Longitude.ToString("F9", CultureInfo.InvariantCulture));
                Assert.Equal(native[2].GetString(), site.Latitude.ToString("F9", CultureInfo.InvariantCulture));
            }
            else
            {
                Assert.Equal("CITY", site.City);
                Assert.InRange(site.Longitude, 0.1, 0.3);
                Assert.InRange(site.Latitude, -0.2, 0.2);
            }
            Assert.Empty(after.World.Ufos);
            if (hunting) Assert.Equal((1, 0), (Assert.Single(after.World.Missions).NextWave, after.World.Missions[0].LiveUfos));
            else Assert.Empty(after.World.Missions);
            Assert.Equal(hunting ? 0 : 1, after.World.AlienBases[0].GenMissionCount);
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + (hunting ? 0 : 4), after.Regions[0].ActivityAlien[^1]);
            campaign = Reload(content, campaign, seed, "generated-direct-born.sav");
            AdvanceUnblocked(campaign, 3000);
            Reload(content, campaign, seed, "generated-direct-expired.sav");
            Assert.Empty(campaign.Capture().World.MissionSites);
        }
        Assert.Equal(2, cities.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyGeneratedDirectSiteReservesMarkerAndActivityBeforeMutation(bool cache)
    {
        var content = GeneratedDirectSiteContent(cache);
        var initial = GeneratedDirectSiteSnapshot(content, 41);
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("DIRECT_MARKER", int.MaxValue), content,
            new SplitMix64RandomSource(41)), "Mission site marker IDs are exhausted or collide with a saved target.");
        var limited = initial with
        { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 3] }, .. initial.Regions.Skip(1)] };
        AssertTimeBlocked(CampaignState.Restore(limited, content, new SplitMix64RandomSource(41)),
            "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalGeneratedNegativeSiteCannotSupplyActivityHeadroom(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        var content = GeneratedDirectSiteContent(cache);
        var initial = GeneratedDirectSiteSnapshot(content, 41) with
        {
            World = new()
            {
                AlienBases = [new(2, "GENERATED_DIRECT_NEGATIVE_BASE", "RACE_A", 0.2, 0.1),
                    new(3, "GENERATED_DIRECT_BASE", "RACE_A", 0.6, 0.1)],
            },
        };
        initial = initial with
        { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue] }, .. initial.Regions.Skip(1)] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)),
            "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HuntingGeneratedZeroTimerSiteStopsOnlyWhenItsPayloadWouldExecute(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        var content = GeneratedDirectSiteContent(cache, hunting: true, deployment: "GENERATED_DIRECT_ZERO_EVENT");
        var initial = GeneratedDirectSiteSnapshot(content, 41, hunting: true) with
        { Time = new(1, 1, 1, 1999, 0, 9, 55) };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(41));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(0, Assert.Single(campaign.Capture().World.MissionSites).SecondsRemaining);
        campaign = Reload(content, campaign, 41, "late-zero-site-born.sav");
        AdvanceUnblocked(campaign, 239);
        campaign = Reload(content, campaign, 41, "late-zero-site-before-payload.sav");
        AssertTimeBlocked(campaign, "Mission site despawn requires research or event payloads.");

        content = GeneratedDirectSiteContent(cache, deployment: "GENERATED_DIRECT_ZERO_EVENT");
        AssertTimeBlocked(CampaignState.Restore(GeneratedDirectSiteSnapshot(content, 41), content,
            new SplitMix64RandomSource(41)), "Mission site despawn requires research or event payloads.");
    }

    [Theory]
    [InlineData(false, false, 5)]
    [InlineData(true, false, 5)]
    [InlineData(false, true, 5)]
    [InlineData(true, true, 5)]
    [InlineData(false, false, 3)]
    [InlineData(true, false, 3)]
    [InlineData(false, true, 3)]
    [InlineData(true, true, 3)]
    public void GeneratedDeploymentAndUfoChainsReserveTogetherAndCompose(bool cache, bool hunting, int objective)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var content = GeneratedDirectSiteContent(cache, hunting, objective: objective, chain: true);
        var initial = GeneratedDirectSiteSnapshot(content, 41, hunting, chain: true);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)),
            41, "generated-supply-chain-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(2, after.World.MissionSites.Count);
        Assert.Single(after.World.Ufos);
        Assert.Equal((3, 1), (Assert.Single(after.World.Missions).NextWave, after.World.Missions[0].LiveUfos));
        Assert.Equal(initial.Regions[0].ActivityAlien[^1] + (hunting ? 0 : 11), after.Regions[0].ActivityAlien[^1]);
        campaign = Reload(content, campaign, 41, "generated-supply-chain-born.sav");
        for (var day = 0; day < 3; day++)
        {
            AdvanceUnblocked(campaign, 17_280);
            campaign = Reload(content, campaign, 41, $"generated-supply-chain-day-{day}.sav");
        }
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Empty(campaign.Capture().World.MissionSites);
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("DIRECT_MARKER", int.MaxValue - 1), content,
            new SplitMix64RandomSource(41)), "Mission site marker IDs are exhausted or collide with a saved target.");
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO_UNIQUE", int.MaxValue), content,
            new SplitMix64RandomSource(41)), "UFO unique IDs are exhausted or collide with a saved UFO.");
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void GeneratedDeploymentRaceChoicesShareTheLastMarkerId(bool cache, bool hunting, bool chain)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        var content = GeneratedDirectSiteContent(cache, hunting, chain: chain, alternatives: true);
        var count = chain ? 2 : 1;
        var initial = GeneratedDirectSiteSnapshot(content, 41, hunting, chain).WithNextId("DIRECT_MARKER", int.MaxValue - count);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(41));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(Enumerable.Range(int.MaxValue - count, count), campaign.Capture().World.MissionSites.Select(site => site.Id));
        Reload(content, campaign, 41, "generated-direct-alternative-last-id.sav");
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("DIRECT_MARKER", int.MaxValue - count + 1), content,
            new SplitMix64RandomSource(41)), "Mission site marker IDs are exhausted or collide with a saved target.");
        var twoSources = initial with
        {
            World = initial.World with
            {
                AlienBases = [initial.World.AlienBases[0], new(3, "GENERATED_DIRECT_BASE", "RACE_A", chain ? 0.2 : 0.6, 0.1)
                { MinutesSinceLastHuntMissionGeneration = hunting ? 5390 : 0 }],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(twoSources, content, new SplitMix64RandomSource(41)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedSelectedDirectChainsKeepOneAreaAcrossWavesAndReload(bool cache, bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = GeneratedDirectSiteContent(cache, hunting, objective: 3, chain: true);
        HashSet<int> areas = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = GeneratedDirectSiteSnapshot(content, seed, hunting, chain: true)
                .WithNextId("DIRECT_MARKER", int.MaxValue - 2);
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)),
                seed, "selected-direct-chain-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var mission = Assert.Single(after.World.Missions);
            areas.Add(mission.MissionSiteZoneArea);
            Assert.Equal(2, after.World.MissionSites.Count);
            Assert.All(after.World.MissionSites, site =>
            {
                Assert.Equal(mission.MissionSiteZoneArea == 0 ? "CITY_ALPHA" : "CITY_BETA", site.City);
                Assert.Equal(Degrees(mission.MissionSiteZoneArea == 0 ? 8 : 12), site.Longitude);
                Assert.Equal(Degrees(mission.MissionSiteZoneArea == 0 ? 2 : -2), site.Latitude);
            });
            campaign = Reload(content, campaign, seed, "selected-direct-chain-born.sav");
            // The second hidden site receives hourly postponement before discovery.
            AdvanceUnblocked(campaign, 4000);
            Reload(content, campaign, seed, "selected-direct-chain-expired.sav");
            Assert.Empty(campaign.Capture().World.MissionSites);
            Assert.Empty(campaign.Capture().World.Missions);
        }
        Assert.Equal(Enumerable.Range(0, 2), areas.Order());
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 3, false)]
    [InlineData(true, 3, false)]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, true)]
    [InlineData(false, 3, true)]
    [InlineData(true, 3, true)]
    public void GeneratedDirectActivityReservesExclusiveChoicesAndSeparateSources(bool cache, int objective, bool repeatSite)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        var content = GeneratedDirectSiteContent(cache, objective: objective, alternatives: true, repeatSite: repeatSite);
        var count = repeatSite ? 2 : 1;
        var points = count * 4;
        var initial = GeneratedDirectSiteSnapshot(content, 41);
        initial = initial with { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - points] }, .. initial.Regions.Skip(1)] };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(41));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(int.MaxValue, campaign.Capture().Regions[0].ActivityAlien[^1]);
        Reload(content, campaign, 41, "generated-direct-exclusive-activity.sav");
        AssertTimeBlocked(CampaignState.Restore(initial with
        { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - points + 1] }, .. initial.Regions.Skip(1)] },
            content, new SplitMix64RandomSource(41)), "UFO alien activity exceeds the supported range.");
        var twoSources = initial with
        {
            Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 2 * points] }, .. initial.Regions.Skip(1)],
            World = initial.World with { AlienBases = [initial.World.AlienBases[0], new(3, "GENERATED_DIRECT_BASE", "RACE_A", 0.6, 0.1)] },
        };
        campaign = CampaignState.Restore(twoSources, content, new SplitMix64RandomSource(41));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(2 * count, campaign.Capture().World.MissionSites.Count);
        Assert.Equal(int.MaxValue, campaign.Capture().Regions[0].ActivityAlien[^1]);
        AssertTimeBlocked(CampaignState.Restore(twoSources with
        { Regions = [twoSources.Regions[0] with { ActivityAlien = [int.MaxValue - 2 * points + 1] }, .. twoSources.Regions.Skip(1)] },
            content, new SplitMix64RandomSource(41)), "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedSelectedDirectSitesRejectUnsafeDurationsBeforeMutation(bool cache, bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        foreach (var deployment in new[] { "DIRECT_NEGATIVE_DURATION", "DIRECT_OVERFLOW_DURATION" })
        {
            var content = GeneratedDirectSiteContent(cache, hunting, deployment, objective: 3);
            AssertTimeBlocked(CampaignState.Restore(GeneratedDirectSiteSnapshot(content, 41, hunting), content,
                new SplitMix64RandomSource(41)), "Direct mission site duration is outside the supported range.");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedMixedRaceChoicesShareActivityAcrossUfosAndSites(bool cache, bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        foreach (var objective in new[] { 0, 3 })
        {
            var content = GeneratedDirectSiteContent(cache, hunting, objective: objective, chain: true, alternatives: true);
            var initial = GeneratedDirectSiteSnapshot(content, 41, hunting, chain: true);
            var points = hunting ? 0 : 11;
            var limited = initial with
            { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - points] }, .. initial.Regions.Skip(1)] };
            var campaign = CampaignState.Restore(limited, content, new SplitMix64RandomSource(41));
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(int.MaxValue, campaign.Capture().Regions[0].ActivityAlien[^1]);
            Assert.Equal(2, campaign.Capture().World.MissionSites.Count);
            Assert.Single(campaign.Capture().World.Ufos);
            campaign = Reload(content, campaign, 41, "mixed-race-activity-at-capacity.sav");
            AdvanceUnblocked(campaign, 1);
            if (!hunting)
                AssertTimeBlocked(CampaignState.Restore(limited with
                { Regions = [limited.Regions[0] with { ActivityAlien = [int.MaxValue - points + 1] }, .. limited.Regions.Skip(1)] },
                    content, new SplitMix64RandomSource(41)), "UFO alien activity exceeds the supported range.");
            campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(41));
            AdvanceUnblocked(campaign, 1);
            for (var day = 0; day < 3; day++)
            {
                AdvanceUnblocked(campaign, 17_280);
                campaign = Reload(content, campaign, 41, $"mixed-race-activity-day-{day}.sav");
            }
            Assert.Empty(campaign.Capture().World.MissionSites);
            Assert.Empty(campaign.Capture().World.Ufos);
            Assert.Empty(campaign.Capture().World.Missions);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void GeneratedExpiryCountersReserveCompleteChainsAcrossRaceAlternatives(bool cache, int count)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = GeneratedDirectSiteContent(cache, deployment: "DIRECT_ZERO_COUNTERS", alternatives: true, directCount: count);
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = GeneratedDirectSiteSnapshot(content, seed)
                .WithNextId("shared", int.MaxValue - 2 * count)
                .WithNextId("DIRECT_MARKER", int.MaxValue - 2 * count);
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)),
                seed, "generated-expiry-capacity-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.Missions);
            Assert.Equal(int.MaxValue, after.NextIds["shared"]);
            Assert.Equal(int.MaxValue, after.NextIds["DIRECT_MARKER"]);
            Reload(content, campaign, seed, "generated-expiry-capacity-after.sav");
            AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("shared", int.MaxValue - 2 * count + 1), content,
                new SplitMix64RandomSource(seed)), "Mission site custom counter exceeds the supported range.");
            var twoSources = initial with
            {
                World = initial.World with
                {
                    AlienBases = [initial.World.AlienBases[0], new(3, "GENERATED_DIRECT_BASE", "RACE_A", 0.6, 0.1)],
                },
            };
            AssertTimeBlocked(CampaignState.Restore(twoSources.WithNextId("DIRECT_MARKER", 1), content,
                new SplitMix64RandomSource(seed)), "Mission site custom counter exceeds the supported range.");
        }
    }

    private static RuntimeContent GeneratedDirectSiteContent(bool cache, bool hunting = false, string deployment = "DIRECT_DEPLOY",
        int objective = 0, bool chain = false, bool alternatives = false, bool repeatSite = false, int directCount = 1) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-spawn.rul"], cache, $$$"""
            crafts:
              - {type: GENERATED_DIRECT_CRAFT, speedMax: 2000, damageMax: 100, fuelMax: 1000, soldiers: 0, allowLanding: false, patrolWithoutFuel: true}
            alienDeployments:
              - type: GENERATED_DIRECT_BASE
                markerName: STR_ALIEN_BASE
                genMission: {GENERATED_DIRECT_MISSION: 1}
                genMissionFreq: {{{(hunting ? 0 : 100)}}}
                genMissionLimit: 1
                genMissionRaceFromAlienBase: {{{!alternatives}}}
                huntMissionRaceFromAlienBase: {{{!alternatives}}}
                baseDetectionRange: {{{(hunting ? 500 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: 5400
                huntMissionWeights: {0: {GENERATED_DIRECT_MISSION: 1}}
              - type: GENERATED_DIRECT_ZERO_EVENT
                markerName: GENERATED_ZERO_MARKER
                duration: [0, 0]
                despawnEvents: {EVENT_DESPAWN: 1}
              - type: GENERATED_DIRECT_NEGATIVE_BASE
                markerName: STR_ALIEN_BASE
                genMission: {GENERATED_DIRECT_NEGATIVE_MISSION: 1}
                genMissionFreq: 50
                genMissionLimit: 1
            alienMissions:
              - type: GENERATED_DIRECT_MISSION
                objective: {{{objective}}}
                {{{(alternatives ? "raceWeights: {0: {RACE_A: 1, RACE_B: 1}}" : "")}}}
                spawnZone: {{{(objective == 3 && chain ? 1 : 0)}}}
                regionWeights: {{{(chain ? "{0: {REGION: 1}}" : "{0: {SITE_SPAWN_REGION: 1}}")}}}
                waves:
                  - {ufo: {{{deployment}}}, count: {{{directCount}}}, trajectory: DIRECT_SITE, timer: 0, objective: {{{objective == 3}}}}
                  {{{(chain ? "- {ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0}\n      - {ufo: DIRECT_DEPLOY, count: 1, trajectory: DIRECT_SITE, timer: 0}" : "")}}}
                  {{{(repeatSite ? "- {ufo: DIRECT_DEPLOY, count: 1, trajectory: DIRECT_SITE, timer: 0}" : "")}}}
              - type: GENERATED_DIRECT_NEGATIVE_MISSION
                objective: 0
                spawnZone: 1
                regionWeights: {0: {REGION: 1}}
                waves: [{ufo: DIRECT_NEGATIVE, count: 1, trajectory: DIRECT_SITE, timer: 0}]
            """);

    private static CampaignSnapshot GeneratedDirectSiteSnapshot(RuntimeContent content, ulong seed, bool hunting = false, bool chain = false)
    {
        var initial = CreateSnapshot(content, "Generated deployment sites") with
        {
            RandomState = seed,
            Time = hunting ? new(1, 1, 1, 1999, 0, 29, 55) : new(1, 1, 1, 1999, 23, 59, 55),
            World = new()
            {
                AlienBases = [new(2, "GENERATED_DIRECT_BASE", "RACE_A", chain ? 0.2 : 0.6, 0.1)
                { MinutesSinceLastHuntMissionGeneration = hunting ? 5390 : 0 }]
            },
        };
        if (!hunting) return initial;
        var owner = initial.Bases[0];
        return initial with
        {
            Bases = [owner with
            {
                Crafts = [new("GENERATED_DIRECT_CRAFT", 1)
                {
                    Logistics = CraftLogistics.LoadStarting(content.RuntimeRules.Crafts[
                        content.RuntimeRules.Crafts.GetRequired("GENERATED_DIRECT_CRAFT")].Value, null) with
                    {
                        Status = "STR_OUT", Longitude = chain ? 0.2 : 0.6, Latitude = 0.1, Fuel = 1000, Weapons = [],
                    },
                }],
                Soldiers = [],
            }],
        };
    }
}
