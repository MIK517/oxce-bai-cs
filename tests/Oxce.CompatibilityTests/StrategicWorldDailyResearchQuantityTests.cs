using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchQuantityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VariableDailyQuantitiesComposeWithFuelAndLaterSiteResearch(bool withSite)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(withSite);
        HashSet<int> seen = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = Snapshot(content, seed, withSite);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            long strictWork = 0;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref strictWork, out _));
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var forecast));
            Assert.Equal(work, forecast.Work);
            Assert.Equal(2, forecast.DiscoveryAlternatives.Count);
            Assert.Equal(4, forecast.Arrivals[initial.Bases[0].Id].Sum(item => item.Value));
            Assert.Equal(1, forecast.ArrivalUncertainty[initial.Bases[0].Id]["SUPPLY"]);
            Assert.Equivalent(initial, campaign.Capture(), strict: true);
            long exhausted = 999_999;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref exhausted, out _));
            Assert.Equivalent(initial, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            seen.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchDailyQuantityRows").Single(row => row[0].GetInt32() == choice && row[1].GetInt32() == (withSite ? 1 : 0));
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { choice, withSite ? 1 : 0, after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                forecast.Arrivals[initial.Bases[0].Id].Count, after.NextIds["EQUIV_COUNTER"], after.ResearchScores[^1] - initial.ResearchScores[^1], after.CompletedResearch.Contains("SITE_AUTO") ? 1 : 0 });
            TestFixtures.AssertJsonEqual(expected, actual);
            var exact = Assert.Single(forecast.DiscoveryAlternatives, branch => branch.CompletedResearch!.Contains(choice == 0 ? "EQUIV_A" : "EQUIV_B"));
            Assert.Equal(3 + choice, exact.Arrivals[initial.Bases[0].Id].Sum(item => item.Value));
            Assert.Empty(exact.ArrivalUncertainty);
            Assert.Equal("STR_OUT", after.Bases[0].Crafts[0].Logistics!.Status);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            Assert.Empty(after.World.MissionSites);
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            campaign = ReloadRepeatedly(content, campaign, seed, "daily-variable-quantities.sav", 3, after);
            AssertContinuesLike(control, campaign, 1);
        }
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void VariableArrivalsStillRequireCommonOrderAndTransferPopulation()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(false, extraResearch: "  - {name: EQUIV_B, spawnedItemList: [SUPPLY]}");
        var campaign = CampaignState.Restore(Snapshot(content, 17, false), content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        long work = 0;
        Assert.False(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out _));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        AssertTimeBlocked(campaign, "UFO retargeting requires daily research fuel-stock forecasts.");
    }

    internal static RuntimeContent Content(bool withSite, int minimum = 2, int maximum = 3, string extra = "", string extraResearch = "") =>
        StrategicWorldResearchStockAlternativesTests.Content(false, minimum, otherCount: maximum,
            extra: (withSite ? StrategicWorldSiteResearchTests.Deployment.Replace("unlockedResearchOnDespawn: CORE", "unlockedResearchOnDespawn: SITE", StringComparison.Ordinal) : "") + "\n" + extra,
            extraResearch: "  - {name: UNLOCK, cost: 1}\n  - {name: SITE, cost: 1, points: 3}\n  - {name: SITE_AUTO, cost: 0, points: 0, dependencies: [SITE], needItem: true, neededItem: SUPPLY}\n" + extraResearch);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool withSite)
    {
        var initial = StrategicWorldResearchStockAlternativesTests.Snapshot(content, seed);
        return withSite ? initial with { World = initial.World with { MissionSites = StrategicWorldSiteResearchTests.Snapshot(content).World.MissionSites } } : initial;
    }
}
