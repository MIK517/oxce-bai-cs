using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicResearchDiaryExecutionTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchTests
{
    private const string Deployment = """
        alienDeployments:
          - type: DEPLOY_DIARY
            markerName: DIARY_SITE
            unlockedResearchOnDespawn: CORE
            counterDespawn: SITE_COUNT
            counterAll: SITE_COUNT
            despawnPenalty: 7
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiteExpiryExecutesNativeMissionResearchOrderingAndPersistsOnce(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-research-diary-execution");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "executionRows").Where(row => row.GetProperty("mission").GetBoolean()))
        {
            var scenario = row.GetProperty("scenario").GetInt32();
            var content = Content(cache, scenario, Deployment);
            var snapshot = Snapshot(content) with
            {
                DebugMode = row.GetProperty("debug").GetBoolean(),
                ResearchRuleStatus = scenario == 3 ? new Dictionary<string, int> { ["CORE"] = 2 } : new Dictionary<string, int>(),
            };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            campaign = Reload(content, campaign, 17, "site-research-before.sav");
            AdvanceUnblocked(campaign, 1);
            AssertDiary(row, campaign);
            var after = campaign.Capture();
            Assert.Empty(after.World.MissionSites);
            Assert.Equal(3, after.NextIds["SITE_COUNT"]);
            Assert.Equal(row.GetProperty("interrupted").EnumerateArray().Select(value => value.GetBoolean()),
                after.World.Missions.Select(mission => mission.Interrupted));
            Assert.Single(after.Bases[0].Research);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 17, "site-research-after.sav");
                AssertDiary(row, campaign);
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(after.ResearchDiary.Count, campaign.Capture().ResearchDiary.Count);
            Assert.Equal(3, campaign.Capture().NextIds["SITE_COUNT"]);
        }
    }

    [Fact]
    public void AllExpiringSitesShareDiaryCapacityBeforeWholeTickMutation()
    {
        var content = Content(false, 2, Deployment);
        var snapshot = Snapshot(content);
        var first = snapshot.World.MissionSites[0];
        snapshot = snapshot with
        {
            ResearchDiarySequence = ulong.MaxValue - 5,
            World = snapshot.World with { MissionSites = [first, first with { Id = 2 }] },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Mission research diary exceeds its supported population or identity range.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SiteEventUsesItsOwnPostGrantStateAndBlocksUnavailablePayloadAtomically(bool interrupted)
    {
        var deployment = Deployment.Replace("despawnPenalty: 7", "despawnPenalty: 7\n    despawnEvents: {EVENT_DESPAWN: 1}", StringComparison.Ordinal);
        var content = Content(false, 0, deployment + "\n" + $$$"""
            events:
              - {name: EVENT_DESPAWN, interruptResearch: {{{(interrupted ? "CORE" : "AUTO")}}}}
            """);
        var campaign = CampaignState.Restore(Snapshot(content), content, new SplitMix64RandomSource(17));
        if (!interrupted) AssertTimeBlocked(campaign, "Mission site despawn requires research or event payloads.");
        else
        {
            AdvanceUnblocked(campaign, 1);
            Assert.Empty(campaign.Capture().World.MissionSites);
            Assert.Contains("CORE", campaign.Capture().CompletedResearch);
            Assert.Empty(campaign.Capture().World.Events);
            Reload(content, campaign, 17, "site-suppressed-event.sav");
        }
    }

    [Fact]
    public void LaterSiteResearchCannotSuppressAnEarlierSiteEvent()
    {
        var content = Content(false, 0, Deployment + "\n  - {type: EARLY_SITE, markerName: EARLY_MARKER, despawnEvents: {EVENT_DESPAWN: 1}}\n" +
            "events:\n  - {name: EVENT_DESPAWN, interruptResearch: CORE}\n");
        var snapshot = Snapshot(content);
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                MissionSites = [snapshot.World.MissionSites[0] with { DeploymentId = "EARLY_SITE" }, snapshot.World.MissionSites[0]],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)),
            "Mission site despawn requires research or event payloads.");
    }

    [Fact]
    public void UnprojectedUfoPhaseStopsBeforeSiteResearchAndCounterChanges()
    {
        var content = Content(false, 0, Deployment);
        var existing = CreateUfoTransitCampaign(content, new(2, 2, 1, 1999, 0, 29, 55)).Capture();
        var snapshot = existing with { World = existing.World with { MissionSites = Snapshot(content).World.MissionSites } };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)),
            "Mission site research requires UFO phase forecasts.");
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content)
    {
        var initial = DailySnapshot(content);
        return initial with
        {
            Time = new(2, 2, 1, 1999, 0, 29, 55),
            World = initial.World with
            {
                Missions = [new(1, "MISSION_SCOUT", "REGION", "RACE_A", 0, 0, 9000, 0, -1),
                    new(2, "MISSION_LANDING", "REGION", "RACE_A", 0, 0, 9000, 0, -1),
                    new(3, "MISSION_SITE", "REGION", "RACE_A", 0, 0, 9000, 0, -1)],
                MissionSites = [new(1, "MISSION_SITE", "DEPLOY_DIARY", "RACE_A", 0.2, 0.1, 0) { Detected = true }],
            },
        };
    }
}
