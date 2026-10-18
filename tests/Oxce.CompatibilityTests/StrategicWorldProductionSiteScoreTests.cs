using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldProductionSiteScoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SiteScoreIncludesPrecedingDailyResearchAndHourlyProductionBeforeAnyMutation(int mode)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldDailyResearchProductionScoreTests.Content(mode, false, withSite: true);
        var initial = StrategicWorldDailyResearchProductionScoreTests.Snapshot(content, 17, mode);
        initial = initial with { World = initial.World with { MissionSites = StrategicWorldSiteResearchTests.Snapshot(content).World.MissionSites } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), "Mission research score exceeds the supported range.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SafeCombinedDailyProductionAndSiteScoreSurvivesRewrites(int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = StrategicWorldDailyResearchProductionScoreTests.Content(mode, false, withSite: true);
        HashSet<int> seen = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = StrategicWorldDailyResearchProductionScoreTests.Snapshot(content, seed, mode);
            initial = initial with
            {
                ResearchScores = [mode == 0 ? int.MaxValue - 4 : int.MinValue + 4],
                World = initial.World with { MissionSites = StrategicWorldSiteResearchTests.Snapshot(content).World.MissionSites },
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            seen.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchDailyProductionSiteScoreRows").Single(row => row[0].GetInt32() == mode && row[1].GetInt32() == choice && row[2].GetInt32() == 4 && row[3].GetInt32() == 0);
            Assert.Equal(expected[6].GetInt64(), (long)after.ResearchScores[^1] - initial.ResearchScores[^1]);
            Assert.Contains("SITE", after.CompletedResearch);
            Assert.Empty(after.World.MissionSites);
            campaign = ReloadRepeatedly(content, campaign, seed, "production-site-score.sav", 3, after);
        }
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void ProductionScoreIntervalsIncludeStoppedProjectsAndShareTheWorkBudget()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldDailyResearchProductionScoreTests.Content(0, false, uncertainOffset: true);
        var initial = StrategicWorldDailyResearchProductionScoreTests.Snapshot(content, 17, 0);
        initial = initial with
        {
            Bases = [initial.Bases[0] with
            { Productions = [new("UNCERTAIN_OFFSET", 4, 0, 4, false, false, false, new Dictionary<string, int>()), new("SCORE_OUTPUT", 6, 0, 6, false, false, false, new Dictionary<string, int>())] }],
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        long work = 0;
        Assert.Null(campaign.ProductionPreflight.ScoreBounds(CampaignTimeTrigger.OneDay, ref work, out var minimum, out var maximum));
        Assert.Equal(-4, minimum);
        Assert.Equal(6, maximum);
        Assert.InRange(work, 1, 1_000_000);
        Assert.Equivalent(initial, campaign.Capture(), strict: true);
        work = 999_999;
        Assert.Equal("Production staffing exceeds the supported work limit.",
            campaign.ProductionPreflight.ScoreBounds(CampaignTimeTrigger.OneDay, ref work, out _, out _));
        Assert.Equivalent(initial, campaign.Capture(), strict: true);
    }

    [Fact]
    public void HourlyProductionBeforeSiteScoresUsesTheSameAtomicBoundary()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldDailyResearchProductionScoreTests.Content(0, false, withSite: true);
        var initial = StrategicWorldDailyResearchProductionScoreTests.Snapshot(content, 17, 0);
        var owner = initial.Bases[0];
        initial = initial with
        {
            Time = new(2, 2, 1, 1999, 12, 59, 55),
            World = initial.World with { MissionSites = StrategicWorldSiteResearchTests.Snapshot(content).World.MissionSites },
            Bases = [owner with { Research = [], Items = new Dictionary<string, int> { ["SUPPLY"] = 2 }, Productions = [owner.Productions[0] with { Assigned = 3, Amount = 3 }] }],
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), "Mission research score exceeds the supported range.");
        initial = initial with { Bases = [initial.Bases[0] with { Productions = [owner.Productions[0] with { Assigned = 2, Amount = 2 }] }] };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(int.MaxValue, campaign.Capture().ResearchScores[^1]);
        Reload(content, campaign, 17, "hour-production-site-score.sav");
    }
}
