using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;
using static Oxce.CompatibilityTests.StrategicWorldDailyResearchWorldAlternativesTests;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchSiteAlternativeAcceptanceTests
{
    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 1, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, true)]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    public void SiteEventsRequireInterruptionOnEveryDailyResearchPath(bool cache, int mode, bool guaranteed)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var rows = TestFixtures.Rows(oracle.RootElement, "researchDailySiteAlternativeRows").Where(row => row[0].GetInt32() == mode).ToArray();
        Assert.Equal(2, rows.Select(row => row[5].GetInt32()).Distinct().Count());
        var content = SiteContent(cache, mode, "    despawnEvents: {SITE_EVENT: 1}\nevents:\n  - {name: SITE_EVENT, interruptResearch: " + (guaranteed ? "SITE" : "SITE_AUTO") + "}");
        if (!guaranteed)
        {
            var campaign = Reload(content, CampaignState.Restore(SiteSnapshot(content, 17), content, new SplitMix64RandomSource(17)), 17, "daily-site-event-guard.sav");
            AssertTimeBlocked(campaign, "Mission site despawn requires research or event payloads.");
            return;
        }
        HashSet<bool> outcomes = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var campaign = CampaignState.Restore(SiteSnapshot(content, seed), content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            outcomes.Add(after.CompletedResearch.Contains("SITE_AUTO"));
            Assert.Contains("SITE", after.CompletedResearch);
            Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.Events);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "daily-site-suppressed-event.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.Equal(2, outcomes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiteGrantKeepsEachDailyPathsActiveProjects(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = SiteContent(cache, 0, research: "  - {name: EQUIV_A, disables: [ACTIVE]}\n  - {name: SITE, reenables: [ACTIVE]}\n  - {name: ACTIVE, cost: 0, points: 2, dependencies: [SITE]}");
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = SiteSnapshot(content, seed);
            initial = initial with { Bases = [initial.Bases[0] with { Research = [.. initial.Bases[0].Research, new("ACTIVE", 0, 0, 5)] }] };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var daily));
            var forecast = campaign.MissionResearch.CreateForecast(CampaignTimeTrigger.OneDay, daily);
            Assert.Null(forecast.Advance("SITE"));
            Assert.Contains("ACTIVE", forecast.PossibleResearch);
            Assert.DoesNotContain("ACTIVE", forecast.GuaranteedResearch);
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            choices.Add(choice);
            var row = TestFixtures.Rows(oracle.RootElement, "researchDailySiteActiveRows").Single(row => row[0].GetInt32() == choice);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { choice, after.Bases[0].Research.Count, after.CompletedResearch.Contains("ACTIVE") ? 1 : 0,
                after.ResearchRuleStatus.GetValueOrDefault("ACTIVE"), after.ResearchScores[^1] - before.ResearchScores[^1], after.NextIds["EQUIV_COUNTER"] });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
            Assert.Equal(choice == 0, after.ResearchDiary.Any(entry => entry.RuleId == "ACTIVE"));
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "daily-site-active-project.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailySiteResearchScoreCapacityChecksEveryPathBeforeMutation(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = SiteContent(cache, 0);
        var initial = SiteSnapshot(content, 17) with { ResearchScores = [int.MaxValue - 5] };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Mission research score exceeds the supported range.");
        initial = initial with { ResearchScores = [int.MaxValue - 6] };
        campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.InRange(campaign.Capture().ResearchScores[^1], int.MaxValue - 1, int.MaxValue);
        Reload(content, campaign, 17, "daily-site-score-capacity.sav");
    }

    [Fact]
    public void DailySiteResearchAlternativesRetainDiaryAndSharedWorkGuards()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = SiteContent(false, 1);
        var initial = SiteSnapshot(content, 17) with { ResearchDiarySequence = ulong.MaxValue - 4 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        long work = 0;
        Assert.True(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var daily));
        Assert.Equal("Mission research diary exceeds its supported population or identity range.", campaign.MissionResearch.CreateForecast(CampaignTimeTrigger.OneDay, daily).Advance("SITE"));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        AssertTimeBlocked(campaign, "Mission research diary exceeds its supported population or identity range.");
        initial = initial with { ResearchDiarySequence = 0 };
        campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        before = campaign.Capture();
        var bounded = campaign.MissionResearch.CreateForecast(CampaignTimeTrigger.OneDay, daily, precedingWork: 999_999);
        Assert.Equal("Mission research exceeds the supported discovery work limit.", bounded.Advance("SITE"));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }
}
