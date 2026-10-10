using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldResearchStockAlternativesTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 2)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 2)]
    public void EquivalentFreeRewardsFeedFuelAndHuntingWithoutChoosingResearch(bool cache, bool duplicate, int count)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(cache, duplicate, count);
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            Assert.Null(forecast.CompletedResearch);
            Assert.Null(forecast.ResearchStatuses);
            Assert.Null(forecast.ActiveResearch);
            Assert.Equal(2, forecast.Arrivals[before.Bases[0].Id].Count);
            Assert.Equal(1 + Math.Max(1, count), forecast.Arrivals[before.Bases[0].Id].Sum(item => item.Value));
            Assert.All(forecast.CounterChanges, change => Assert.Equal(new CustomCounterChange("EQUIV_COUNTER", true), change));
            Assert.Equal(2, forecast.CounterChanges.Count);
            long identityWork = 0;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref identityWork, out _, includeDiscoveries: true));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            choices.Add(choice);
            Assert.NotEqual(after.CompletedResearch.Contains("EQUIV_A"), after.CompletedResearch.Contains("EQUIV_B"));
            var expected = TestFixtures.Rows(oracle.RootElement, "researchEquivalentRewardRows").Single(row =>
                row[0].GetInt32() == choice && row[1].GetBoolean() == duplicate && row[2].GetInt32() == count);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                choice, duplicate, count, choice + 1, after.Bases[0].Items.GetValueOrDefault("SUPPLY") + 1,
                forecast.Arrivals[before.Bases[0].Id].Count, after.NextIds["EQUIV_COUNTER"], after.ResearchScores[^1] - before.ResearchScores[^1],
            });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
            Assert.Equal("STR_OUT", after.Bases[0].Crafts[0].Logistics!.Status);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "equivalent-research-stock.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentConsequencesRemainAtomicAndProjectionWorkIsBounded(bool cache)
    {
        foreach (var content in new[]
        {
            Content(cache, false, 2, extraResearch: "  - {name: EQUIV_B, spawnedItemList: [SUPPLY]}"),
            Content(cache, false, 2, otherCounter: "OTHER_COUNTER"),
        })
        {
            var campaign = CampaignState.Restore(Snapshot(content, 17), content, new SplitMix64RandomSource(17));
            var before = campaign.Capture();
            long work = 0;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out _));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AssertTimeBlocked(campaign, "UFO retargeting requires daily research fuel-stock forecasts.");
        }
        var supported = Content(cache, false, 2);
        var limited = CampaignState.Restore(Snapshot(supported, 17), supported, new SplitMix64RandomSource(17));
        var initial = limited.Capture();
        long exhausted = 999_999;
        Assert.False(limited.ResearchPreflight.TryForecastDailyStocks(ref exhausted, out _));
        Assert.Equivalent(initial, limited.Capture(), strict: true);
        var identities = Enumerable.Range(0, 4097).Select(index => "LIMIT_REWARD_" + index).ToArray();
        var labels = string.Join(", ", identities);
        var extraResearch = string.Join("\n", identities.Select(id => "  - {name: " + id + ", cost: 1}"));
        var oversized = Content(cache, false, 2, labels: labels, extraResearch: extraResearch);
        limited = CampaignState.Restore(Snapshot(oversized, 17), oversized, new SplitMix64RandomSource(17));
        initial = limited.Capture();
        long branching = 0;
        Assert.False(limited.ResearchPreflight.TryForecastDailyStocks(ref branching, out _));
        Assert.InRange(branching, 1, 1_000_000);
        Assert.Equivalent(initial, limited.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void AutomaticClosureMakesDistinctRewardsAnExactFinalResearchProjection(bool cache, bool duplicate, bool withSite)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(cache, duplicate, 2, withSite ? StrategicWorldSiteResearchTests.Deployment : "", convergent: true);
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed);
            if (withSite) initial = initial with { World = initial.World with { MissionSites = StrategicWorldSiteResearchTests.Snapshot(content).World.MissionSites } };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast, includeDiscoveries: true));
            Assert.Equal(work, forecast.Work);
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Empty(after.World.MissionSites);
            var choice = after.ResearchDiary[1].RuleId == "EQUIV_A" ? 0 : 1;
            choices.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchConvergentRewardRows").Single(row =>
                row[0].GetInt32() == choice && row[1].GetBoolean() == duplicate);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                choice, duplicate, after.CompletedResearch.Contains("CORE"), after.CompletedResearch.Contains("EQUIV_A"),
                after.CompletedResearch.Contains("EQUIV_B"), after.Bases[0].Items.GetValueOrDefault("SUPPLY") + 1,
                forecast.Arrivals[before.Bases[0].Id].Count, after.NextIds["EQUIV_COUNTER"], after.ResearchScores[^1] - before.ResearchScores[^1],
            });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
            Assert.Equal(after.CompletedResearch.Order(StringComparer.Ordinal), forecast.CompletedResearch!.Order(StringComparer.Ordinal));
            Assert.Equivalent(after.ResearchRuleStatus, forecast.ResearchStatuses, strict: true);
            Assert.Equal(after.Bases[0].Research.Select(project => project.RuleId).Order(StringComparer.Ordinal), forecast.ActiveResearch![after.Bases[0].Id].Order(StringComparer.Ordinal));
            Assert.Equal(after.ResearchDiary.Count - before.ResearchDiary.Count, forecast.DiaryEntries);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "convergent-research-stock.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingCompletedTopicsDoNotHideDifferentResearchStatuses(bool cache)
    {
        var content = Content(cache, false, 2, convergent: true, unequalStatus: true);
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var campaign = CampaignState.Restore(Snapshot(content, seed), content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long stockWork = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref stockWork, out _));
            long exactWork = 0;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref exactWork, out _, includeDiscoveries: true));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.ResearchDiary[1].RuleId == "EQUIV_A" ? 0 : 1;
            choices.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchConvergentStatusRows").Single(row => row[0].GetInt32() == choice);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { choice, after.CompletedResearch.Contains("EQUIV_A"),
                after.CompletedResearch.Contains("EQUIV_B"), after.ResearchRuleStatus.GetValueOrDefault("EQUIV_Z"), after.ResearchScores[^1] - before.ResearchScores[^1] });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4096)]
    [InlineData(true, 4096)]
    public void WeightedDuplicateLabelsShareProofWithoutChangingActualSelection(bool cache, int repeats)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var labels = string.Join(", ", Enumerable.Repeat("EQUIV_A", repeats).Append("EQUIV_B"));
        var content = Content(cache, false, 2, labels: labels);
        foreach (var seed in new ulong[] { 1, repeats == 3 ? 7UL : 221UL })
        {
            var predictedChoice = new SplitMix64RandomSource(seed).NextExclusive(repeats + 1) == repeats ? 1 : 0;
            var campaign = CampaignState.Restore(Snapshot(content, seed), content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast));
            Assert.InRange(work, 1, 100_000);
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            Assert.Equal(predictedChoice, choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchWeightedEquivalentRows").Single(row =>
                row[0].GetInt32() == repeats && row[1].GetInt32() == choice);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { repeats, choice, choice + 1,
                after.Bases[0].Items.GetValueOrDefault("SUPPLY") + 1, forecast.Arrivals[before.Bases[0].Id].Count,
                after.ResearchScores[^1] - before.ResearchScores[^1] });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "weighted-research-stock.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void StockCallersIgnoreUnusedRewardScoresAndDiaryMetadata(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        using var diary = TestFixtures.ReadVerifiedExpected("strategic-research-diary-execution");
        var content = Content(cache, false, 2, otherPoints: mode == 0 ? 2 : 1, rewardLookup: mode == 1, extraResearch: "  - {name: UNLOCK, cost: 1}");
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast));
            Assert.Null(forecast.ScoreChange);
            Assert.Null(forecast.DiaryEntries);
            Assert.Null(forecast.CompletedResearch);
            Assert.Equivalent(initial, campaign.Capture(), strict: true);
            long exactWork = 0;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref exactWork, out _, includeDiscoveries: true));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            choices.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchScopedRewardRows").Single(row => row[0].GetInt32() == mode && row[1].GetInt32() == choice);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { mode, choice, after.CompletedResearch.Contains("EQUIV_Z"),
                after.Bases[0].Items.GetValueOrDefault("SUPPLY") + 1, forecast.Arrivals[initial.Bases[0].Id].Count,
                after.NextIds["EQUIV_COUNTER"], after.ResearchScores[^1] - initial.ResearchScores[^1] });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
            var expectedDiary = TestFixtures.Rows(diary.RootElement, "stockScopeRows").Single(row => row[0].GetInt32() == mode && row[1].GetInt32() == choice);
            Assert.Equal(expectedDiary[2].EnumerateArray().Select(entry => entry.GetString()), after.ResearchDiary.Select(entry => entry.RuleId));
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "scoped-research-stock.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.Equal(2, choices.Count);
        var unsafeInitial = Snapshot(content, 17) with { ResearchScores = [int.MaxValue] };
        AssertTimeBlocked(CampaignState.Restore(unsafeInitial, content, new SplitMix64RandomSource(17)), "Research and production scores exceed the supported range.");
        unsafeInitial = Snapshot(content, 17) with { ResearchDiarySequence = ulong.MaxValue };
        AssertTimeBlocked(CampaignState.Restore(unsafeInitial, content, new SplitMix64RandomSource(17)), "Research diary exceeds its supported population or identity range.");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void EmptyCounterNamesAreUnconsumedInEquivalentRewardEffects(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(cache, false, 2, rewardCounter: mode == 1 ? "EQUIV_COUNTER" : "",
            otherCounter: mode == 1 ? "EQUIV_COUNTER" : "", emptyDecrease: mode != 0);
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed);
            initial = initial with { NextIds = new Dictionary<string, int>(initial.NextIds) { ["UNKNOWN_COUNTER"] = 11 } };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast));
            Assert.Equal(mode == 1 ? 2 : 1, forecast.CounterChanges.Count);
            Assert.All(forecast.CounterChanges, change => Assert.Equal(new CustomCounterChange("EQUIV_COUNTER", true), change));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            choices.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchEmptyCounterRewardRows").Single(row => row[0].GetInt32() == mode && row[1].GetInt32() == choice);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { mode, choice, after.Bases[0].Items.GetValueOrDefault("SUPPLY") + 1,
                forecast.Arrivals[before.Bases[0].Id].Count, after.NextIds["EQUIV_COUNTER"], after.NextIds.ContainsKey(""), after.NextIds["UNKNOWN_COUNTER"], after.ResearchScores[^1] - before.ResearchScores[^1] });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "empty-research-counter.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.Equal(2, choices.Count);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 10000)]
    [InlineData(true, 10000)]
    public void EmptyOnlyEffectsUseTheBoundedFastStockProof(bool cache, int labels)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var counters = string.Join(", ", Enumerable.Repeat("''", labels));
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$"""
            crafts: [{type: SHIP, fuelMax: 1000, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]
            facilities: [{type: EQUIV_LAB, mapName: LOGISTICS, labs: 2}]
            ufos: [{type: UFO_HUNTER, radarRange: 20000}]
            research:
              - {name: CORE, cost: 1, points: 1, getOneFree: [EQUIV_A, EQUIV_B], increaseCounter: [{{counters}}]}
              - {name: EQUIV_A, cost: 1, points: 1}
              - {name: EQUIV_B, cost: 1, points: 2}
              - {name: UNLOCK, cost: 1}
            """, extraRulesAfterOverlays: true);
        HashSet<int> choices = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed);
            initial = initial with
            {
                NextIds = new Dictionary<string, int>(initial.NextIds) { ["UNKNOWN_COUNTER"] = 11 },
                Bases = [initial.Bases[0] with { Items = new Dictionary<string, int> { ["SUPPLY"] = 1 } }],
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 985_000;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast));
            Assert.InRange(work, 985_000 + labels, 1_000_000);
            Assert.Empty(forecast.StockChanges);
            Assert.Empty(forecast.Arrivals);
            Assert.Empty(forecast.CounterChanges);
            Assert.Null(forecast.NextTransferId);
            Assert.Null(forecast.ScoreChange);
            Assert.Null(forecast.DiaryEntries);
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            long exhausted = 999_999;
            Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref exhausted, out _));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            choices.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchEmptyOnlyRewardRows").Single(row => row[0].GetInt32() == labels && row[1].GetInt32() == choice);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { labels, choice, after.Bases[0].Items.GetValueOrDefault("SUPPLY") + 1,
                after.Bases[0].Transfers.Count, after.NextIds["UNKNOWN_COUNTER"], after.ResearchScores[^1] - before.ResearchScores[^1] });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
            Assert.False(after.NextIds.ContainsKey(""));
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "empty-only-research-effects.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.Equal(2, choices.Count);
    }

    internal static RuntimeContent Content(bool cache, bool duplicate, int count, string extra = "", int? otherCount = null, string otherCounter = "EQUIV_COUNTER", int otherPoints = 1, string? labels = null, string extraResearch = "", bool convergent = false, bool unequalStatus = false, bool rewardLookup = false, string rewardCounter = "EQUIV_COUNTER", bool emptyDecrease = false) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$"""
        crafts: [{type: SHIP, fuelMax: 1000, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]
        facilities: [{type: EQUIV_LAB, mapName: LOGISTICS, labs: 2}]
        ufos: [{type: UFO_HUNTER, radarRange: 20000}]
        research:
          - {name: CORE, cost: 1, points: 1, getOneFree: [{{labels ?? "EQUIV_A, " + (duplicate ? "EQUIV_A, " : "") + "EQUIV_B"}}], spawnedItem: SUPPLY, spawnedItemCount: 1, increaseCounter: [EQUIV_COUNTER]}
          - {name: EQUIV_A, cost: {{(convergent ? 0 : 1)}}, points: 1{{(rewardLookup ? ", lookup: EQUIV_Z" : "")}}{{(convergent ? ", dependencies: [CORE]" : "")}}{{(unequalStatus ? ", disables: [EQUIV_Z]" : "")}}, spawnedItem: SUPPLY, spawnedItemCount: {{count}}, increaseCounter: ['{{rewardCounter}}']{{(emptyDecrease ? ", decreaseCounter: ['']" : "")}}}
          - {name: EQUIV_B, cost: {{(convergent ? 0 : 1)}}{{(convergent ? ", dependencies: [CORE]" : "")}}, points: {{otherPoints}}{{(unequalStatus ? ", reenables: [EQUIV_Z]" : "")}}, spawnedItem: SUPPLY, spawnedItemCount: {{otherCount ?? count}}, increaseCounter: [{{otherCounter}}]}
          {{(unequalStatus || rewardLookup ? "- {name: EQUIV_Z, cost: 1, points: 0}" : "")}}
        {{extraResearch}}
        {{extra}}
        """, extraRulesAfterOverlays: true);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed)
    {
        var initial = CreateUfoTransitCampaign(content, new(2, 2, 1, 1999, 23, 59, 55)).Capture();
        var owner = initial.Bases[0];
        var ship = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
        return initial with
        {
            RandomState = seed,
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["EQUIV_COUNTER"] = 1 },
            Bases = [owner with
            {
                Items = new Dictionary<string, int>(StringComparer.Ordinal),
                Facilities = [.. owner.Facilities, new("EQUIV_LAB", 1, 1, 0, 0, false, false, false)],
                Research = [new("CORE", 1, 0, 1)],
                Transfers = [],
                Productions = [],
                Soldiers = [],
                Crafts = [ship with
                {
                    Logistics = ship.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 960,
                        MissionComplete = false,
                        LowFuel = false,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 1,
                        AutoPatrolLatitude = 0.6,
                        Destination = null,
                    },
                }],
            }],
            World = initial.World with { Ufos = [initial.World.Ufos[0] with { HunterKiller = true }] },
        };
    }
}
