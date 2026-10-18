using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchQuantityContextTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DailyRewardStockContextRemainsCoupledThroughLaterSiteResearch(int dependency)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(dependency);
        HashSet<int> seen = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, seed, true);
            if (dependency == 0) initial = initial with { ResearchScores = [int.MaxValue - 6] };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var daily));
            var item = content.RuntimeRules.Items.GetRequired("SUPPLY");
            Dictionary<RuleHandle<ItemRuleFamily>, int> stocks = new() { [item] = 1 };
            Dictionary<RuleHandle<ItemRuleFamily>, int> uncertainty = new() { [item] = 1 };
            var forecast = campaign.MissionResearch.CreateForecast(CampaignTimeTrigger.OneDay, daily, stocks,
                uncertainFirstBaseItems: [item], firstBaseItemUncertainty: uncertainty);
            Assert.Null(forecast.Advance("SITE"));
            Assert.Equal(dependency == 1, forecast.PossibleResearch.Contains("SITE_AUTO"));
            Assert.DoesNotContain("SITE_AUTO", forecast.GuaranteedResearch);
            Assert.Equal("Mission research exceeds the supported discovery work limit.",
                campaign.MissionResearch.CreateForecast(CampaignTimeTrigger.OneDay, daily, stocks, precedingWork: 999_999,
                    uncertainFirstBaseItems: [item], firstBaseItemUncertainty: uncertainty).Advance("SITE"));
            Assert.Equivalent(initial, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            if (dependency == 0) Assert.Equal(int.MaxValue - 1, after.ResearchScores[^1]);
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            seen.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchDailyQuantityContextRows").Single(row => row[0].GetInt32() == dependency && row[1].GetInt32() == choice);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { dependency, choice, after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                after.ResearchScores[^1] - initial.ResearchScores[^1], after.CompletedResearch.Contains("SITE_AUTO") ? 1 : 0, after.NextIds.GetValueOrDefault("oxcePortTransfer", 1) - initial.NextIds.GetValueOrDefault("oxcePortTransfer", 1), after.NextIds["EQUIV_COUNTER"] });
            TestFixtures.AssertJsonEqual(expected, actual);
            Assert.Equal(dependency == 1 && choice == 1, after.CompletedResearch.Contains("SITE_AUTO"));
            Assert.Equal("STR_OUT", after.Bases[0].Crafts[0].Logistics!.Status);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            Assert.Empty(after.World.MissionSites);
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            campaign = ReloadRepeatedly(content, campaign, seed, "daily-quantity-context.sav", 3, after);
            AssertContinuesLike(control, campaign, 1);
        }
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void BranchStockContextRetainsOtherUncertaintyAndRejectsInvalidRanges()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(0);
        var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, 17, true) with { ResearchScores = [int.MaxValue - 6] };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        long work = 0;
        Assert.True(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var daily));
        var item = content.RuntimeRules.Items.GetRequired("SUPPLY");
        Dictionary<RuleHandle<ItemRuleFamily>, int> stocks = new() { [item] = 2 };
        Dictionary<RuleHandle<ItemRuleFamily>, int> uncertainty = new() { [item] = 2 };
        var forecast = campaign.MissionResearch.CreateForecast(CampaignTimeTrigger.OneDay, daily, stocks,
            uncertainFirstBaseItems: [item], firstBaseItemUncertainty: uncertainty);
        Assert.Equal("Mission research score exceeds the supported range.", forecast.Advance("SITE"));
        Assert.Equivalent(initial, campaign.Capture(), strict: true);
        stocks[item] = 1;
        uncertainty[item] = 0;
        forecast = campaign.MissionResearch.CreateForecast(CampaignTimeTrigger.OneDay, daily, stocks,
            uncertainFirstBaseItems: [item], firstBaseItemUncertainty: uncertainty);
        Assert.Equal("Mission site research item stocks exceed the supported range.", forecast.Advance("SITE"));
        uncertainty[item] = 1;
        stocks[item] = 0;
        forecast = campaign.MissionResearch.CreateForecast(CampaignTimeTrigger.OneDay, daily, stocks,
            uncertainFirstBaseItems: [item], firstBaseItemUncertainty: uncertainty);
        Assert.Equal("Mission site research item stocks exceed the supported range.", forecast.Advance("SITE"));
        Assert.Equivalent(initial, campaign.Capture(), strict: true);
    }

    internal static RuntimeContent Content(int dependency, string extra = "") =>
        StrategicWorldDailyResearchQuantityTests.Content(true, minimum: 1, maximum: 2, extra: extra,
            extraResearch: "  - {name: CORE, spawnedItem: ''}\n  - {name: SITE_AUTO, points: 2, dependencies: [SITE, " + (dependency == 0 ? "EQUIV_A" : "EQUIV_B") + "]}");
}
