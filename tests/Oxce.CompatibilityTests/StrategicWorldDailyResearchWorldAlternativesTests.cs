using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchWorldAlternativesTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void WorldResearchProjectionRetainsNativeExactStatesWithoutChoosingRewards(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        using var diary = TestFixtures.ReadVerifiedExpected("strategic-research-diary-execution");
        var content = StrategicWorldResearchStockAlternativesTests.Content(cache, false, 2, otherPoints: mode == 0 ? 2 : 1, rewardLookup: mode == 1, extraResearch: "  - {name: UNLOCK, cost: 1}");
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var campaign = CampaignState.Restore(StrategicWorldResearchStockAlternativesTests.Snapshot(content, seed), content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var forecast));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            Assert.Equal(2, forecast.DiscoveryAlternatives.Count);
            Assert.Contains("CORE", forecast.CompletedResearch!);
            Assert.DoesNotContain("EQUIV_A", forecast.CompletedResearch!);
            Assert.DoesNotContain("EQUIV_B", forecast.CompletedResearch!);
            Assert.Null(forecast.ResearchStatuses);
            Assert.Null(forecast.ActiveResearch);
            Assert.Null(forecast.ScoreChange);
            Assert.Null(forecast.DiaryEntries);
            Assert.All(forecast.DiscoveryAlternatives, branch => Assert.Empty(branch.DiscoveryAlternatives));
            long strictWork = 0;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref strictWork, out _, includeDiscoveries: true));
            long exhausted = 999_999;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref exhausted, out _));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            choices.Add(choice);
            var branch = Assert.Single(forecast.DiscoveryAlternatives, candidate => candidate.CompletedResearch!.Contains(choice == 0 ? "EQUIV_A" : "EQUIV_B"));
            Assert.Equal(after.CompletedResearch.Order(StringComparer.Ordinal), branch.CompletedResearch!.Order(StringComparer.Ordinal));
            Assert.Equal(after.ResearchScores[^1] - before.ResearchScores[^1], branch.ScoreChange);
            Assert.Equal(after.ResearchDiary.Count - before.ResearchDiary.Count, branch.DiaryEntries);
            Assert.Empty(branch.ActiveResearch![before.Bases[0].Id]);
            var row = TestFixtures.Rows(oracle.RootElement, "researchScopedRewardRows").Single(row => row[0].GetInt32() == mode && row[1].GetInt32() == choice);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { mode, choice, after.CompletedResearch.Contains("EQUIV_Z"),
                after.Bases[0].Items.GetValueOrDefault("SUPPLY") + 1, forecast.Arrivals[before.Bases[0].Id].Count, after.NextIds["EQUIV_COUNTER"], branch.ScoreChange!.Value });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
            var expectedDiary = TestFixtures.Rows(diary.RootElement, "stockScopeRows").Single(row => row[0].GetInt32() == mode && row[1].GetInt32() == choice);
            Assert.Equal(expectedDiary[2].EnumerateArray().Select(entry => entry.GetString()), after.ResearchDiary.Select(entry => entry.RuleId));
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "daily-world-alternatives.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorldResearchAlternativesRequireCommonArrivalShapeButRetainCounterTraces(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        foreach (var content in new[]
        {
            StrategicWorldResearchStockAlternativesTests.Content(cache, false, 2, extraResearch: "  - {name: EQUIV_B, spawnedItemList: [SUPPLY]}"),
            StrategicWorldResearchStockAlternativesTests.Content(cache, false, 2, otherCounter: "OTHER_COUNTER"),
        })
        {
            var campaign = CampaignState.Restore(StrategicWorldResearchStockAlternativesTests.Snapshot(content, 17), content, new SplitMix64RandomSource(17));
            var before = campaign.Capture();
            long work = 0;
            var varyingCounters = content.RuntimeRules.Research[content.RuntimeRules.Research.GetRequired("EQUIV_B")].Value.IncreaseCounters.Contains("OTHER_COUNTER");
            Assert.Equal(varyingCounters, campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var forecast));
            if (varyingCounters)
            {
                Assert.False(forecast.CounterChangesAreCommon);
                Assert.Empty(forecast.CounterChanges);
                Assert.Equal(2, forecast.DiscoveryAlternatives.Count);
            }
            Assert.Equivalent(before, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void DailyExactAlternativesComposeWithLaterSiteResearch(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = SiteContent(cache, mode);
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var campaign = CampaignState.Restore(SiteSnapshot(content, seed), content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            choices.Add(choice);
            var row = TestFixtures.Rows(oracle.RootElement, "researchDailySiteAlternativeRows").Single(row => row[0].GetInt32() == mode && row[1].GetInt32() == choice);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { mode, choice, after.Bases[0].Items.GetValueOrDefault("SUPPLY"), after.NextIds["EQUIV_COUNTER"],
                after.ResearchScores[^1] - before.ResearchScores[^1], after.CompletedResearch.Contains("SITE_AUTO") ? 1 : 0, after.CompletedResearch.Contains("EQUIV_A") ? 1 : 0,
                after.CompletedResearch.Contains("EQUIV_B") ? 1 : 0, after.CompletedResearch.Contains("EQUIV_Z") ? 1 : 0 });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
            Assert.Contains("SITE", after.CompletedResearch);
            Assert.Empty(after.World.MissionSites);
            var expectedDiary = new List<string> { "CORE", choice == 0 ? "EQUIV_A" : "EQUIV_B" };
            if (mode == 1 && choice == 0) expectedDiary.Add("EQUIV_Z");
            expectedDiary.Add("SITE");
            if (choice == 0) expectedDiary.Add("SITE_AUTO");
            Assert.Equal(expectedDiary, after.ResearchDiary.Select(entry => entry.RuleId));
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "daily-alternative-site.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, choices.Count);
    }

    internal static Oxce.Mods.Rulesets.Content.RuntimeContent SiteContent(bool cache, int mode, string extra = "", string research = "") =>
        StrategicWorldResearchStockAlternativesTests.Content(cache, false, 2, otherPoints: mode == 0 ? 2 : 1, rewardLookup: mode == 1,
            extra: StrategicWorldSiteResearchTests.Deployment.Replace("unlockedResearchOnDespawn: CORE", "unlockedResearchOnDespawn: SITE", StringComparison.Ordinal) + "\n" + extra,
            extraResearch: "  - {name: UNLOCK, cost: 1}\n  - {name: SITE, cost: 1, points: 3}\n  - {name: SITE_AUTO, cost: 0, points: 0, dependencies: [SITE, EQUIV_A]}\n" + research);

    internal static CampaignSnapshot SiteSnapshot(Oxce.Mods.Rulesets.Content.RuntimeContent content, ulong seed)
    {
        var initial = StrategicWorldResearchStockAlternativesTests.Snapshot(content, seed);
        return initial with { World = initial.World with { MissionSites = StrategicWorldSiteResearchTests.Snapshot(content).World.MissionSites } };
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void WorldResearchDistinctOutcomePopulationIsBounded(int count, bool supported)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var labels = string.Join(", ", Enumerable.Range(0, count).Select(index => "WORLD_REWARD_" + index));
        var definitions = string.Join("\n", Enumerable.Range(0, count).Select(index => $"  - {{name: WORLD_REWARD_{index}, cost: 1, points: 1, spawnedItem: SUPPLY, spawnedItemCount: 2, increaseCounter: [EQUIV_COUNTER]}}"));
        var content = StrategicWorldResearchStockAlternativesTests.Content(false, false, 2, labels: labels, extraResearch: definitions);
        var campaign = CampaignState.Restore(StrategicWorldResearchStockAlternativesTests.Snapshot(content, 17), content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        long work = 0;
        Assert.Equal(supported, campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var forecast));
        Assert.InRange(work, 1, 1_000_000);
        if (supported) Assert.Equal(count, forecast.DiscoveryAlternatives.Count);
        else Assert.Equal("Daily research exceeds the supported world outcome limit.", forecast.UnavailableReason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }
}
