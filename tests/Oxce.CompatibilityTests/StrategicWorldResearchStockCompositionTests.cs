using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldResearchStockCompositionTests
{
    private static readonly int[] ExpectedEventCountdowns = [60, 90];
    [Theory]
    [InlineData(false, 2, false, false)]
    [InlineData(true, 2, false, false)]
    [InlineData(false, 2, true, false)]
    [InlineData(true, 2, true, false)]
    [InlineData(false, 2, false, true)]
    [InlineData(true, 2, false, true)]
    [InlineData(false, 2, true, true)]
    [InlineData(true, 2, true, true)]
    [InlineData(false, 3, false, false)]
    [InlineData(true, 3, false, false)]
    [InlineData(false, 3, true, false)]
    [InlineData(true, 3, true, false)]
    [InlineData(false, 3, false, true)]
    [InlineData(true, 3, false, true)]
    [InlineData(false, 3, true, true)]
    [InlineData(true, 3, true, true)]
    public void NestedRewardsRetainSavedBaseOrderAndComposeWithIncomingStocks(bool cache, int count, bool reverse, bool incoming)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(cache);
        HashSet<(int, int)> paths = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, count, reverse, incoming);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var stocks));
            Assert.Null(stocks.ScoreChange);
            Assert.Null(stocks.DiaryEntries);
            long exactWork = 0;
            var exact = campaign.ResearchPreflight.TryForecastDailyStocks(ref exactWork, out var research, includeDiscoveries: true);
            Assert.Equal(count == 3, exact);
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var selected = after.ResearchDiary.Where(entry => entry.RuleId.StartsWith("EQUIV_", StringComparison.Ordinal))
                .Select(entry => entry.RuleId[^1] - 'A').ToArray();
            Assert.Equal(count, selected.Length);
            Assert.Equal(count, selected.Distinct().Count());
            var remaining = Enumerable.Range(0, 3).Where(index => index != selected[0]).ToArray();
            var second = Array.IndexOf(remaining, selected[1]);
            paths.Add((selected[0], second));
            var expected = TestFixtures.Rows(oracle.RootElement, "researchMultiBaseRewardRows").Single(row =>
                row[0].GetInt32() == count && row[1].GetBoolean() == incoming && row[2].GetInt32() == selected[0] && row[3].GetInt32() == second);
            var mask = selected.Sum(index => 1 << index);
            var quantities = after.Bases.Select(owner => owner.Items.GetValueOrDefault("SUPPLY") + (owner.Crafts.Count == 0 ? 0 : 1)).ToArray();
            var arrivals = stocks.Arrivals.Values.Sum(items => items.Count) + (incoming ? count : 0);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { count, incoming, selected[0], second, mask,
                quantities, arrivals, after.NextIds["EQUIV_COUNTER"], after.ResearchScores[^1] - before.ResearchScores[^1] });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
            Assert.Equal(before.Bases.Select(owner => owner.Id), after.Bases.Select(owner => owner.Id));
            Assert.All(after.Bases, owner => { Assert.Empty(owner.Research); Assert.Empty(owner.Transfers); });
            Assert.Equal(stocks.NextTransferId, after.NextIds["oxcePortTransfer"]);
            Assert.Equal("STR_OUT", Assert.Single(after.Bases.SelectMany(owner => owner.Crafts)).Logistics!.Status);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            if (exact)
            {
                Assert.Equal(after.CompletedResearch.Order(StringComparer.Ordinal), research.CompletedResearch!.Order(StringComparer.Ordinal));
                Assert.Equivalent(after.ResearchRuleStatus, research.ResearchStatuses, strict: true);
                Assert.Equal(after.ResearchScores[^1] - before.ResearchScores[^1], research.ScoreChange);
                Assert.Equal(after.ResearchDiary.Count - before.ResearchDiary.Count, research.DiaryEntries);
                Assert.All(research.ActiveResearch!.Values, Assert.Empty);
            }
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            for (var rewrite = 0; rewrite < 3; rewrite++)
            {
                campaign = Reload(content, campaign, seed, "composed-research-stocks.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 24);
            for (var tick = 0; tick < 24; tick++) AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
        }
        Assert.Equal(6, paths.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposedClosureCountsFutureEventsButKeepsTheirPayloadBoundary(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-event");
        var content = Content(cache, "events: [{name: EQUIV_EVENT, timer: 90}]");
        var initial = Snapshot(content, 17, 3, true, true);
        initial = initial with
        {
            World = initial.World with
            {
                GeneratedEvents = new Dictionary<string, int>(initial.World.GeneratedEvents) { ["EQUIV_EVENT"] = 2 },
                Events = [new("EQUIV_EVENT", 90) { PreservationKey = "event:generated:EQUIV_EVENT:1" },
                    new("EQUIV_EVENT", 120) { PreservationKey = "event:generated:EQUIV_EVENT:2" }],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(ExpectedEventCountdowns, after.World.Events.Select(scheduled => scheduled.SpawnCountdown));
        Assert.Equal(2, after.World.GeneratedEvents["EQUIV_EVENT"]);
        Assert.Contains("EQUIV_C", after.CompletedResearch);
        for (var rewrite = 0; rewrite < 3; rewrite++)
        {
            campaign = Reload(content, campaign, 17, "composed-research-future-events.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
        var due = after with
        {
            Time = new(3, 3, 1, 1999, 0, 29, 55),
            World = after.World with { Events = [after.World.Events[0] with { SpawnCountdown = 30 }, after.World.Events[1]] },
        };
        AssertTimeBlocked(CampaignState.Restore(due, content, new SplitMix64RandomSource(17)),
            "Strategic event consequences require campaign event execution (Phase 6 branch 6).");
    }

    private static RuntimeContent Content(bool cache, string extra = "") => StrategicWorldResearchStockAlternativesTests.Content(cache, false, 2,
        labels: "EQUIV_A, EQUIV_B, EQUIV_C", extraResearch: """
          - {name: UNLOCK, cost: 1}
          - {name: EQUIV_C, cost: 1, points: 1, spawnedItem: SUPPLY, spawnedItemCount: 2, increaseCounter: [EQUIV_COUNTER]}
        """, extra: extra);

    private static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int count, bool reverse, bool incoming)
    {
        var initial = StrategicWorldResearchStockAlternativesTests.Snapshot(content, seed);
        var owner = initial.Bases[0];
        var bases = Enumerable.Range(0, count).Select(index => owner with
        {
            Id = owner.Id + index,
            Name = "Research base " + index,
            Crafts = index == 0 ? owner.Crafts : [],
            Transfers = incoming ? [new(1001 + index, 1, CampaignTransferKind.Item, "SUPPLY", 1)] : [],
        }).ToArray();
        if (reverse) Array.Reverse(bases);
        return initial with { Bases = bases };
    }
}
