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
    [InlineData(false)]
    [InlineData(true)]
    public void DailyGeneratedDeploymentWavesCreateAndExpireSitesWithoutUfos(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var native = TestFixtures.Rows(oracle.RootElement, "cases").Single(row => row.GetProperty("name").GetString() == "selected")
            .GetProperty("site");
        var content = GeneratedDirectSiteContent(cache);
        HashSet<string> cities = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = GeneratedDirectSiteSnapshot(content, seed).WithNextId("DIRECT_MARKER", 7);
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
            Assert.InRange(site.SecondsRemaining + 1800, 7200, 10800);
            Assert.Equal(0, (site.SecondsRemaining + 1800) % 3600);
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
            Assert.Empty(after.World.Missions);
            Assert.Equal(1, after.World.AlienBases[0].GenMissionCount);
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 4, after.Regions[0].ActivityAlien[^1]);
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

    private static RuntimeContent GeneratedDirectSiteContent(bool cache) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-site-spawn.rul"], cache, """
            alienDeployments:
              - type: GENERATED_DIRECT_BASE
                markerName: STR_ALIEN_BASE
                genMission: {GENERATED_DIRECT_MISSION: 1}
                genMissionFreq: 100
                genMissionLimit: 1
              - type: GENERATED_DIRECT_NEGATIVE_BASE
                markerName: STR_ALIEN_BASE
                genMission: {GENERATED_DIRECT_NEGATIVE_MISSION: 1}
                genMissionFreq: 50
                genMissionLimit: 1
            alienMissions:
              - type: GENERATED_DIRECT_MISSION
                objective: 0
                spawnZone: 0
                regionWeights: {0: {SITE_SPAWN_REGION: 1}}
                waves: [{ufo: DIRECT_DEPLOY, count: 1, trajectory: DIRECT_SITE, timer: 0}]
              - type: GENERATED_DIRECT_NEGATIVE_MISSION
                objective: 0
                spawnZone: 1
                regionWeights: {0: {REGION: 1}}
                waves: [{ufo: DIRECT_NEGATIVE, count: 1, trajectory: DIRECT_SITE, timer: 0}]
            """);

    private static CampaignSnapshot GeneratedDirectSiteSnapshot(RuntimeContent content, ulong seed) =>
        CreateSnapshot(content, "Generated deployment sites") with
        {
            RandomState = seed,
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            World = new() { AlienBases = [new(2, "GENERATED_DIRECT_BASE", "RACE_A", 0.6, 0.1)] },
        };
}
