using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;
using System.Text;
using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionSiteScriptTests
{
    [Fact]
    public void OrderedSiteCandidatesMatchPinnedPointRepeatAndRectangleRules()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-site-candidates");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var direct = row[0].GetBoolean();
            var site = row[1].GetBoolean();
            var occupied = row[2].GetInt32();
            var repeat = row[3].GetInt32();
            var zone = row[4].GetInt32();
            var strategy = new AlienStrategyState();
            if ((repeat & 1) != 0) strategy.AddMissionLocation("RECENT", "REGION", 0, 10);
            if ((repeat & 2) != 0) strategy.AddMissionLocation("RECENT", "REGION_SECOND", 0, 10);
            MissionZone[] first = [new([Area(true, 0), Area(false, 0), Area(false, -1), Area(true, 0)]), new([])];
            MissionZone[] second = [new([Area(true, 0), Area(false, 0)])];
            WorldSiteScriptRegion[] regions =
                [new("REGION", first, (occupied & 1) != 0), new("REGION_SECOND", second, (occupied & 2) != 0)];
            IReadOnlyList<WorldSiteScriptCandidate> candidates = [];
            if (row[5].GetBoolean())
                Assert.Throws<InvalidDataException>(() =>
                    WorldMissionSiteSelection.Candidates(regions, zone, direct, site, strategy, "RECENT"));
            else candidates = WorldMissionSiteSelection.Candidates(regions, zone, direct, site, strategy, "RECENT");
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                direct, site, occupied, repeat, zone, row[5].GetBoolean(),
                candidates.Select(candidate => new object[] { candidate.Region, candidate.Area }).ToArray(),
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }

        static MissionArea Area(bool point, int texture) => new(0.1, point ? 0.1 : 0.2, 0.1, 0.1, texture, "");
    }
    [Fact]
    public void SiteMissionRetryAndAreaDrawsMatchPinnedCommandBlock()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-site-candidates");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "selectionRows"))
        {
            var name = row[0].GetString()!;
            var random = new TestFixtures.RecordingMinimumRandomSource();
            random.NextInclusive(1, 3); // captured initial mission distribution
            random.NextInclusive(0, 99); // captured target-base odds
            var strategy = new AlienStrategyState();
            if (name == "repeatPoints") strategy.AddMissionLocation("RECENT", "REGION", 0, 10);
            MissionArea point = new(0.1, 0.1, 0.1, 0.1, 0, "");
            MissionArea rectangle = point with { LongitudeMaximum = 0.2 };
            IReadOnlyList<WorldSiteScriptCandidate> Candidates(string mission)
            {
                MissionZone[] first = [new(name == "rectangle" ? [rectangle] : [point, point])];
                var occupied = name == "allOccupied" || name == "firstOccupied" && mission == "MISSION_A" || name == "noBase";
                var regions = new List<WorldSiteScriptRegion> { new("REGION", first, occupied) };
                if (name is "twoRegions" or "uniform" or "secondRegion") regions.Add(new("REGION_SECOND", [new([point])], false));
                return WorldMissionSiteSelection.Candidates(regions, 0, name == "rectangle" && mission == "MISSION_A",
                    false, strategy, "RECENT");
            }
            string[] types = ["MISSION_A", "MISSION_B"];
            var candidates = WorldMissionSiteSelection.SelectMission(types[0], types, Candidates, out var mission);
            var region = "";
            var area = -1;
            if (candidates is not null)
            {
                if (name == "uniform") random.NextInclusive(0, 1);
                else random.NextInclusive(1, 1); // captured weighted region distribution
                region = name == "secondRegion" ? "REGION_SECOND" : "REGION";
                area = WorldMissionSiteSelection.SelectArea(candidates, region, random);
            }
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                name, candidates is not null, mission, region, area,
                random.Calls.Select(call => new[] { call.Minimum, call.Maximum }).ToArray(),
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiteScriptSchedulesPointAndRunsOwnedSiteLifecycleAfterReload(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-mission-site-candidates");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-site-scripts.rul"], cache);
        var initial = CreateSnapshot(content, "Site script scheduling") with
        {
            RandomState = 17,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        };
        var expected = new SplitMix64RandomSource(17);
        expected.NextInclusive(0, 99); // execution odds
        var type = expected.NextInclusive(1, 4) <= 3 ? "SITE_SCRIPT_A" : "SITE_SCRIPT_B";
        expected.NextInclusive(0, 99); // target-base odds, after mission choice
        expected.NextInclusive(1, 1); // region
        var area = expected.NextInclusive(0, 1);
        expected.NextInclusive(1, 1); // race
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var after = campaign.Capture();
        var mission = Assert.Single(after.World.Missions);
        Assert.Equal(type, mission.RuleId);
        Assert.Equal("REGION", mission.RegionId);
        Assert.Equal("RACE_A", mission.Race);
        Assert.Equal(area, mission.MissionSiteZoneArea);
        Assert.Equal(30, mission.SpawnCountdown);
        Assert.Equal(expected.State, after.RandomState);
        Assert.Equal(1, after.World.Strategy.MissionRuns.Single(pair => pair.Key == "CITY_HISTORY").Value);
        var location = Assert.Single(after.World.Strategy.MissionLocations.Single(pair => pair.Key == "CITY_HISTORY").Value);
        Assert.Equal(new AlienStrategyState.MissionLocation("REGION", area), location);
        campaign = Reload(content, campaign, 17, "scheduled-site-script.sav");
        AdvanceUnblocked(campaign, 1);
        var site = Assert.Single(campaign.Capture().World.MissionSites);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value;
        var point = region.MissionZones[1].Areas[area];
        Assert.Equal(WorldPosition.Create(point.LongitudeMinimum, point.LatitudeMinimum), site.Position);
        Assert.Equal("SITE_DEPLOY_B", site.DeploymentId);
        Reload(content, campaign, 17, "site-script-after-site-birth.sav");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MultiRegionSiteSelectionPreservesWeightedOrUniformRegionThenAreaOrder(bool cache, bool uniform)
    {
        var weights = uniform ? "{}" : "{0: {REGION: 4, SITE_SECOND: 1}}";
        var content = LoadSite(cache, $$$"""
            regions:
              - type: SITE_SECOND
                areas: [[90, 180, -20, 20]]
                missionZones:
                  - []
                  - [[100, 100, 2, 2, 10]]
            missionScripts:
              - type: SITE_SCRIPT_START
                executionOdds: 0
              - type: SITE_SCRIPT_MULTI
                varName: MULTI_HISTORY
                useTable: false
                startDelay: 30
                missionWeights: {0: {SITE_SCRIPT_A: 3, SITE_SCRIPT_B: 1}}
                regionWeights: {{{weights}}}
                raceWeights: {0: {RACE_A: 1}}
            """);
        var multiScript = content.RuntimeRules.MissionScripts.Rules.Single(entry => entry.Id == "SITE_SCRIPT_MULTI").Value;
        Assert.Equal(uniform ? 0 : 1, multiScript.Regions.Count);
        if (!uniform) Assert.Equal(2, WorldScriptSelection.ForMonth(multiScript.Regions, 0, false).Count);
        Assert.Equal(2, content.RuntimeRules.Regions.Count);
        var initial = CreateSnapshot(content, "Multi-region site script") with { RandomState = 17 };
        var expected = new SplitMix64RandomSource(17);
        expected.NextInclusive(0, 99); // disabled original script still draws its odds
        expected.NextInclusive(0, 99); // multi-region script odds
        var type = expected.NextInclusive(1, 4) <= 3 ? "SITE_SCRIPT_A" : "SITE_SCRIPT_B";
        expected.NextInclusive(0, 99);
        var region = uniform ? content.RuntimeRules.Regions.Rules[expected.NextInclusive(0, 1)].Id :
            expected.NextInclusive(1, 5) <= 4 ? "REGION" : "SITE_SECOND";
        var area = region == "REGION" ? expected.NextInclusive(0, 1) : expected.NextInclusive(2, 2) - 2;
        expected.NextInclusive(1, 1);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var after = campaign.Capture();
        var mission = Assert.Single(after.World.Missions);
        Assert.Equal(type, mission.RuleId);
        Assert.Equal(region, mission.RegionId);
        Assert.Equal(area, mission.MissionSiteZoneArea);
        Assert.Equal(expected.State, after.RandomState);
        Reload(content, campaign, 17, "multi-region-site-script.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointRepeatHistorySurvivesSiteBirthAndSuppressesAnExhaustedLocationSet(bool cache)
    {
        var content = LoadSite(cache);
        var initial = CreateSnapshot(content, "Site repeat history") with
        {
            RandomState = 17,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var firstArea = Assert.Single(campaign.Capture().World.Missions).MissionSiteZoneArea;
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Missions);
        campaign = Reload(content, campaign, 17, "site-history-first.sav");
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.NotEqual(firstArea, Assert.Single(campaign.Capture().World.Missions).MissionSiteZoneArea);
        AdvanceUnblocked(campaign, 360);
        Assert.Empty(campaign.Capture().World.Missions);
        campaign = Reload(content, campaign, 17, "site-history-second.sav");
        var before = campaign.Capture();
        var expected = new SplitMix64RandomSource(before.RandomState);
        expected.NextInclusive(0, 99);
        expected.NextInclusive(1, 4);
        expected.NextInclusive(0, 99);
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var after = campaign.Capture();
        Assert.Empty(after.World.Missions);
        Assert.Equal(2, after.World.Strategy.MissionRuns.Single(pair => pair.Key == "CITY_HISTORY").Value);
        Assert.Equal(2, after.World.Strategy.MissionLocations.Single(pair => pair.Key == "CITY_HISTORY").Value.Count);
        Assert.Equal(expected.State, after.RandomState);
        Reload(content, campaign, 17, "site-history-exhausted.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposedSiteCommandsObserveLiveOccupancyAndFalseLabels(bool cache)
    {
        var content = LoadSite(cache, """
            missionScripts:
              - type: SITE_SCRIPT_START
                executionOdds: 0
              - type: SITE_SCRIPT_COMPOSED_FIRST
                label: 7
                varName: CITY_HISTORY
                avoidRepeats: 2
                useTable: false
                startDelay: 30
                missionWeights: {0: {SITE_SCRIPT_A: 1}}
                regionWeights: {0: {REGION: 1}}
                raceWeights: {0: {RACE_A: 1}}
              - type: SITE_SCRIPT_OCCUPIED
                label: 8
                conditionals: [7]
                varName: OCCUPIED_RUN
                useTable: false
                missionWeights: {0: {SITE_SCRIPT_A: 1}}
                regionWeights: {0: {REGION: 1}}
                raceWeights: {0: {RACE_A: 1}}
              - type: AFTER_OCCUPIED_SITE
                conditionals: [-8]
                startDelay: 120
                missionWeights: {0: {MISSION_SCOUT: 1}}
                regionWeights: {0: {REGION: 1}}
                raceWeights: {0: {RACE_A: 1}}
            """);
        var initial = CreateSnapshot(content, "Composed site commands") with { RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var after = campaign.Capture();
        Assert.Equal(["SITE_SCRIPT_A", "MISSION_SCOUT"], after.World.Missions.Select(mission => mission.RuleId));
        Assert.DoesNotContain(after.World.Strategy.MissionRuns, pair => pair.Key == "OCCUPIED_RUN");
        Assert.Single(after.World.Strategy.MissionLocations.Single(pair => pair.Key == "CITY_HISTORY").Value);
        Reload(content, campaign, 17, "composed-site-command-labels.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiteRegionRetryWithoutCandidateProofStopsBeforeAnyCommandMutates(bool targetBase)
    {
        var content = LoadSite(overlay: $$$"""
            regions:
              - type: SITE_SECOND
                areas: [[90, 180, -20, 20]]
                missionZones:
                  - []
                  - {{{(targetBase ? "[[100, 100, 2, 2, 10]]" : "[]")}}}
            missionScripts:
              - type: SITE_SCRIPT_START
                executionOdds: 0
              - type: SITE_SCRIPT_RETRY
                useTable: false
                missionWeights: {0: {SITE_SCRIPT_A: 1}}
                raceWeights: {0: {RACE_A: 1}}
                targetBaseOdds: {{{(targetBase ? 100 : 0)}}}
                regionWeights: {0: {REGION: 1, SITE_SECOND: 1}}
            """);
        var loadedScript = content.RuntimeRules.MissionScripts.Rules.Single(entry => entry.Id == "SITE_SCRIPT_RETRY").Value;
        Assert.True(loadedScript.SiteType);
        Assert.Equal(2, WorldScriptSelection.ForMonth(loadedScript.Regions, 0, false).Count);
        Assert.Equal(targetBase ? 100 : 0, loadedScript.TargetBaseOdds);
        var initial = CreateSnapshot(content, "Site retry guard") with { RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events));
        Assert.Equal("Site mission scripts require bounded region retry forecasts.", blocked.Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void SiteLocationHistoryCapacityIsReservedBeforeRngOrMutation()
    {
        var content = LoadSite();
        var initial = CreateSnapshot(content, "Site history limit") with { RandomState = 17 };
        initial = initial with
        {
            World = initial.World with
            {
                Strategy = initial.World.Strategy with
                {
                    MissionLocations = [new("CITY_HISTORY",
                        Enumerable.Repeat(new AlienStrategyState.MissionLocation("REGION", 0), 100_000).ToArray())],
                },
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events));
        Assert.Equal("Site mission location history exceeds the supported limit.", blocked.Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void EmptyHistoryKeysAlsoConsumeSiteHistoryCapacityBeforeMutation()
    {
        var content = LoadSite();
        var initial = CreateSnapshot(content, "Site empty history key limit") with { RandomState = 17 };
        initial = initial with
        {
            World = initial.World with
            {
                Strategy = initial.World.Strategy with
                {
                    MissionLocations = Enumerable.Range(0, 100_000).Select(index =>
                        new KeyValuePair<string, IReadOnlyList<AlienStrategyState.MissionLocation>>(
                            $"LEGACY_{index:000000}", [])).ToArray(),
                },
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events));
        Assert.Equal("Site mission location history exceeds the supported limit.", blocked.Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static Oxce.Mods.Rulesets.Content.RuntimeContent LoadSite(bool cache = false, string? overlay = null)
    {
        TestFixtures.LoadVerifiedManifest("strategic-mission-site-candidates");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-site-scripts.rul"], cache, overlay);
    }

}
