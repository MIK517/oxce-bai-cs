using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldResearchArrivalOrderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NonReusableArrivalPermutationsPreserveTransferMultiplicityAndActualOrder(bool cache, bool duplicate)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(cache, duplicate);
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldResearchStockAlternativesTests.Snapshot(content, seed);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast));
            var arrivals = forecast.Arrivals[before.Bases[0].Id];
            Assert.Equal(duplicate ? 4 : 3, arrivals.Count);
            Assert.Equal(2, arrivals.Count(item => item.Key == "SUPPLY"));
            Assert.Equal(duplicate ? 2 : 1, arrivals.Count(item => item.Key == "CLIP"));
            Assert.All(arrivals, item => Assert.Equal(1, item.Value));
            long exactWork = 0;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref exactWork, out _, includeDiscoveries: true));
            long exhausted = 999_999;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref exhausted, out _));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            choices.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchOrderedArrivalRows").Single(row => row[0].GetBoolean() == duplicate && row[1].GetInt32() == choice);
            // A deterministic exact projection exposes the native order, whereas
            // the common stock-only projection exposes only the reusable subsequence.
            var selectedInitial = initial with { CompletedResearch = [choice == 0 ? "EQUIV_B" : "EQUIV_A"] };
            var selected = CampaignState.Restore(selectedInitial, content, new SplitMix64RandomSource(seed));
            long selectedWork = 0;
            Assert.True(selected.ResearchPreflight.TryForecastDailyStocks(ref selectedWork, out var ordered, includeDiscoveries: true));
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { duplicate, choice,
                ordered.Arrivals[before.Bases[0].Id].Select(item => item.Key).ToArray(),
                after.Bases[0].Items.GetValueOrDefault("SUPPLY") + 1, after.Bases[0].Items.GetValueOrDefault("CLIP"),
                arrivals.Count, after.ResearchScores[^1] - before.ResearchScores[^1] });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
            Assert.Equal(forecast.NextTransferId, after.NextIds["oxcePortTransfer"]);
            Assert.Empty(after.Bases[0].Transfers);
            Assert.Equal("STR_OUT", after.Bases[0].Crafts[0].Logistics!.Status);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "research-arrival-order.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, choices.Count);
    }

    internal static RuntimeContent Content(bool cache, bool duplicate) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$"""
        items: [{type: CLIP, battleType: 2}]
        crafts: [{type: SHIP, fuelMax: 1000, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]
        facilities: [{type: EQUIV_LAB, mapName: LOGISTICS, labs: 2}]
        ufos: [{type: UFO_HUNTER, radarRange: 20000}]
        research:
          - {name: CORE, cost: 1, points: 1, getOneFree: [EQUIV_A, EQUIV_B], spawnedItem: SUPPLY, spawnedItemCount: 1}
          - {name: EQUIV_A, cost: 1, points: 1, spawnedItemList: [CLIP, SUPPLY{{(duplicate ? ", CLIP" : "")}}]}
          - {name: EQUIV_B, cost: 1, points: 1, spawnedItemList: [{{(duplicate ? "CLIP, " : "")}}SUPPLY, CLIP]}
          - {name: UNLOCK, cost: 1}
        """, extraRulesAfterOverlays: true);
}
