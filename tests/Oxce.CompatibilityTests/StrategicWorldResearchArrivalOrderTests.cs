using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldResearchArrivalOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonReusableArrivalPermutationsPreserveTransferMultiplicityAndActualOrder(bool duplicate)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(duplicate);
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
            TestFixtures.AssertJsonEqual(expected, actual);
            Assert.Equal(forecast.NextTransferId, after.NextIds["oxcePortTransfer"]);
            Assert.Empty(after.Bases[0].Transfers);
            Assert.Equal("STR_OUT", after.Bases[0].Crafts[0].Logistics!.Status);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            campaign = ReloadRepeatedly(content, campaign, seed, "research-arrival-order.sav", 3, after);
            AssertContinuesLike(control, campaign, 1);
        }
        Assert.Equal(2, choices.Count);
    }

    [Fact]
    public void ReusableAmmunitionPermutationsRemainOrderedAndRefusedAtomically()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(false, reusable: true);
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldResearchStockAlternativesTests.Snapshot(content, seed);
            initial = initial with
            {
                Bases = [initial.Bases[0] with { Crafts = [initial.Bases[0].Crafts[0] with
                {
                    Logistics = initial.Bases[0].Crafts[0].Logistics! with
                    { Weapons = [new("REUSE_CLIP", 0), new("REUSE_SUPPLY", 0)] },
                }] }],
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 0;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out _));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AssertTimeBlocked(campaign, "UFO retargeting requires daily research fuel-stock forecasts.");
            // The native side effects themselves remain executable when no hunter
            // needs an unselected craft forecast in this tick.
            initial = initial with { World = initial.World with { Ufos = [initial.World.Ufos[0] with { HunterKiller = false }] } };
            campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            choices.Add(choice);
            var selected = CampaignState.Restore(initial with { CompletedResearch = [choice == 0 ? "EQUIV_B" : "EQUIV_A"] }, content, new SplitMix64RandomSource(seed));
            long selectedWork = 0;
            Assert.True(selected.ResearchPreflight.TryForecastDailyStocks(ref selectedWork, out var ordered));
            var expected = TestFixtures.Rows(oracle.RootElement, "researchReusableArrivalRows").Single(row => row[0].GetInt32() == choice);
            var ship = after.Bases[0].Crafts[0].Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { choice, ordered.Arrivals[before.Bases[0].Id].Select(item => item.Key).ToArray(),
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"), after.Bases[0].Items.GetValueOrDefault("CLIP"),
                ship.Weapons[0]!.Rearming, ship.Weapons[1]!.Rearming, ship.Status });
            TestFixtures.AssertJsonEqual(expected, actual);
            campaign = ReloadRepeatedly(content, campaign, seed, "reusable-research-order.sav", 3, after);
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(10000)]
    public void LargeOrderedReusableListsDoNotPayForAnUnusedSort(int count)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var labels = string.Join(", ", Enumerable.Repeat("SUPPLY", count));
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], $$"""
        crafts: [{type: SHIP, fuelMax: 1000, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true, weapons: 2}]
        facilities: [{type: EQUIV_LAB, mapName: LOGISTICS, labs: 2}]
        ufos: [{type: UFO_HUNTER, radarRange: 20000}]
        research:
          - {name: CORE, cost: 1, points: 1, getOneFree: [EQUIV_A, EQUIV_B], spawnedItem: SUPPLY, spawnedItemCount: 1}
          - {name: EQUIV_A, cost: 1, points: 1, spawnedItemList: [{{labels}}]}
          - {name: EQUIV_B, cost: 1, points: 1, spawnedItemList: [{{labels}}]}
          - {name: UNLOCK, cost: 1}
        """, extraRulesAfterOverlays: true);
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var campaign = CampaignState.Restore(StrategicWorldResearchStockAlternativesTests.Snapshot(content, seed), content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 940_000;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast));
            Assert.InRange(work, 940_000 + count * 4, 1_000_000);
            Assert.Equal(count + 1, forecast.Arrivals[before.Bases[0].Id].Count);
            Assert.All(forecast.Arrivals[before.Bases[0].Id], item => Assert.Equal(new KeyValuePair<string, int>("SUPPLY", 1), item));
            long exhausted = 999_999;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref exhausted, out _));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            choices.Add(after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchLargeReusableRows").Single(row => row[0].GetInt32() == count);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { count, after.Bases[0].Items["SUPPLY"] + 1,
                forecast.Arrivals[before.Bases[0].Id].Count, after.ResearchScores[^1] - before.ResearchScores[^1] });
            TestFixtures.AssertJsonEqual(expected, actual);
            Assert.Equal(forecast.NextTransferId, after.NextIds["oxcePortTransfer"]);
            Assert.Empty(after.Bases[0].Transfers);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            campaign = ReloadRepeatedly(content, campaign, seed, "large-reusable-research.sav", 3, after);
            if (choices.Count == 2) break;
        }
        Assert.Equal(2, choices.Count);
    }

    internal static RuntimeContent Content(bool duplicate, bool reusable = false) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], $$"""
        items: [{type: CLIP, battleType: {{(reusable ? 0 : 2)}}}]
        crafts: [{type: SHIP, fuelMax: 1000, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true, weapons: 2}]
        craftWeapons: [{type: REUSE_CLIP, launcher: SUPPLY, clip: CLIP, ammoMax: 10, rearmRate: 1}, {type: REUSE_SUPPLY, launcher: SUPPLY, clip: SUPPLY, ammoMax: 10, rearmRate: 1}]
        facilities: [{type: EQUIV_LAB, mapName: LOGISTICS, labs: 2}]
        ufos: [{type: UFO_HUNTER, radarRange: 20000}]
        research:
          - {name: CORE, cost: 1, points: 1, getOneFree: [EQUIV_A, EQUIV_B], spawnedItem: '{{(reusable ? "" : "SUPPLY")}}', spawnedItemCount: 1}
          - {name: EQUIV_A, cost: 1, points: 1, spawnedItemList: [CLIP, SUPPLY{{(duplicate ? ", CLIP" : "")}}]}
          - {name: EQUIV_B, cost: 1, points: 1, spawnedItemList: [{{(duplicate ? "CLIP, " : "")}}SUPPLY, CLIP]}
          - {name: UNLOCK, cost: 1}
        """, extraRulesAfterOverlays: true);
}
