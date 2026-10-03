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
                if (name == "twoRegions") regions.Add(new("REGION_SECOND", [new([point])], false));
                return WorldMissionSiteSelection.Candidates(regions, 0, name == "rectangle" && mission == "MISSION_A",
                    false, strategy, "RECENT");
            }
            string[] types = ["MISSION_A", "MISSION_B"];
            var candidates = WorldMissionSiteSelection.SelectMission(types[0], types, Candidates, out var mission);
            var region = "";
            var area = -1;
            if (candidates is not null)
            {
                random.NextInclusive(1, 1); // captured weighted region distribution
                region = "REGION";
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

}
