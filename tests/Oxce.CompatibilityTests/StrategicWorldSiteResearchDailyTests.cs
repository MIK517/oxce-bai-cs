using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchDailyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void DailyPrefixForecastMatchesExecutionWithoutMutation(int mode)
    {
        var content = Content(mode);
        var initial = Snapshot(content, mode);
        initial = initial with { World = initial.World with { MissionSites = [] } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        long work = 0;
        Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var prefix, includeDiscoveries: true));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Equal(work, prefix.Work);
        long exhausted = 1_000_000;
        Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref exhausted, out _, includeDiscoveries: true));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(after.CompletedResearch.Order(StringComparer.Ordinal), prefix.CompletedResearch!.Order(StringComparer.Ordinal));
        Assert.Equivalent(after.ResearchRuleStatus, prefix.ResearchStatuses, strict: true);
        Assert.Equal(after.ResearchScores[^1] - before.ResearchScores[^1], prefix.ScoreChange);
        Assert.Equal(after.ResearchDiary.Count - before.ResearchDiary.Count, prefix.DiaryEntries);
        Assert.Equal(after.Bases[0].Research.Select(project => project.RuleId).Order(StringComparer.Ordinal),
            prefix.ActiveResearch![after.Bases[0].Id].Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void DailyProjectsPrecedeSiteResearchAndPersistTheirCombinedState(int mode)
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(mode);
        var initial = Snapshot(content, mode);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Empty(after.World.MissionSites);
        Assert.Equal(mode == 0 ? 1 : 0, after.Bases[0].Research.Count);
        Assert.Equal(mode == 2 ? 2 : 0, after.ResearchRuleStatus.GetValueOrDefault("CORE"));
        Assert.Equal(mode != 2, after.CompletedResearch.Contains("CORE"));
        Assert.Equal(mode == 0 ? 5 : mode == 2 ? 3 : 8, after.ResearchScores[^1] - initial.ResearchScores[^1]);
        Assert.True(after.World.Missions[0].Interrupted);
        string[] expected = mode == 0 ? ["CORE", "AUTO"] : mode == 2 ? ["DAY"] : ["DAY", "CORE", "AUTO"];
        Assert.Equal(expected, after.ResearchDiary.Select(entry => entry.RuleId));
        if (mode != 0) Assert.Equal((byte)ResearchDiscoverySource.Base, after.ResearchDiary[0].SourceType);
        if (mode != 2)
        {
            var core = after.ResearchDiary.Single(entry => entry.RuleId == "CORE");
            Assert.Equal((byte)ResearchDiscoverySource.Mission, core.SourceType);
            Assert.Equal("DEPLOY_DIARY", core.SourceName);
        }
        campaign = ReloadRepeatedly(content, campaign, 17, "daily-site-research.sav", 3, after);
    }

    [Fact]
    public void CombinedDailyAndSiteDiaryCapacityStopsBeforeMutation()
    {
        var content = Content(1);
        var initial = Snapshot(content, 1) with { ResearchDiarySequence = ulong.MaxValue - 2 };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Mission research diary exceeds its supported population or identity range.");
    }

    [Fact]
    public void CombinedDailyAndSiteScoreStopsBeforeMutation()
    {
        var content = Content(1);
        var initial = Snapshot(content, 1) with { ResearchScores = [int.MaxValue - 7] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Mission research score exceeds the supported range.");
    }

    [Fact]
    public void AmbiguousDailyRewardCannotSuppressUnavailableSitePayload()
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(4, siteEvent: true);
        AssertEventTickRuns(CampaignState.Restore(Snapshot(content, 4), content, new SplitMix64RandomSource(17)), popped: []);
    }

    internal static RuntimeContent Content(int mode, bool siteEvent = false) => StrategicReadinessTestContent.Load(["strategic-world.rul"], $$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: DAY, cost: 1, points: 3{{(mode == 2 ? ", disables: [CORE]" : mode == 3 ? ", reenables: [CORE]" : mode == 4 ? ", getOneFree: [BONUS, OTHER]" : "")}}}
          - {name: CORE, cost: 1, points: 5}
          - {name: AUTO, cost: 0, dependencies: [CORE]}
          - {name: BONUS, cost: 1}
          - {name: OTHER, cost: 1}
        alienMissions:
          - {type: MISSION_SCOUT, interruptResearch: CORE}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + (siteEvent ? "\n    despawnEvents: {DAILY_EVENT: 1}\nevents:\n  - {name: DAILY_EVENT, interruptResearch: BONUS}\n" : ""));

    internal static CampaignSnapshot Snapshot(RuntimeContent content, int mode)
    {
        var initial = StrategicWorldSiteResearchTests.Snapshot(content);
        return initial with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            ResearchRuleStatus = mode == 3 ? new Dictionary<string, int> { ["CORE"] = 2 } : initial.ResearchRuleStatus,
            Bases = [initial.Bases[0] with { Research = [new("DAY", 0, 1, mode == 0 ? 10 : 1)] }],
        };
    }
}
