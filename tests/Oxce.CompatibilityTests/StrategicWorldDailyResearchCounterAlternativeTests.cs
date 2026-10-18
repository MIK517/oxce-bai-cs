using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchCounterAlternativeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentCounterNamesRetainTheirWorldBranchesAndNativeEffects(bool withSite) =>
        AssertNativeCounterAlternatives(0, withSite);

    internal static void AssertNativeCounterAlternatives(int mode, bool withSite)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(mode, withSite);
        foreach (var start in new[] { 1, 20, int.MaxValue - 1 })
        {
            HashSet<int> seen = [];
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var initial = Snapshot(content, seed, start, withSite);
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                long work = 0;
                Assert.False(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out _));
                work = 0;
                Assert.True(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var forecast));
                Assert.Equal(2, forecast.DiscoveryAlternatives.Count);
                Assert.False(forecast.CounterChangesAreCommon);
                Assert.Empty(forecast.CounterChanges);
                Assert.All(forecast.DiscoveryAlternatives, branch => Assert.True(branch.CounterChangesAreCommon));
                Assert.Equivalent(initial, campaign.Capture(), strict: true);
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
                seen.Add(choice);
                var expected = TestFixtures.Rows(oracle.RootElement, "researchDailyCounterAlternativeRows").Single(row => row[0].GetInt32() == mode && row[1].GetInt32() == start && row[2].GetInt32() == choice);
                var actual = JsonSerializer.SerializeToUtf8Bytes(new long[] { mode, start, choice, after.NextIds["EQUIV_COUNTER"], after.NextIds["LEFT_COUNTER"], after.NextIds["RIGHT_COUNTER"], after.NextIds["TRACE_COUNTER"], after.Bases[0].Items.GetValueOrDefault("SUPPLY"), after.ResearchScores[^1] - initial.ResearchScores[^1] - (withSite ? 3 : 0), after.NextIds["oxcePortTransfer"] - initial.NextIds.GetValueOrDefault("oxcePortTransfer", 1) });
                TestFixtures.AssertJsonEqual(expected, actual);
                Assert.True(Assert.Single(after.World.Ufos).Hunting);
                Assert.Equal("STR_OUT", after.Bases[0].Crafts[0].Logistics!.Status);
                Assert.Empty(after.World.MissionSites);
                campaign = ReloadRepeatedly(content, campaign, seed, "daily-counter-alternatives.sav", 3, after);
            }
            Assert.Equal(2, seen.Count);
        }
    }

    [Fact]
    public void UnsafeCounterAlternativeRefusesWithoutSelectingAReward()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(0);
        var initial = Snapshot(content, 17, int.MaxValue, false);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), "Research custom counters exceed the supported range.");
    }

    internal static RuntimeContent Content(int mode, bool withSite = false) =>
        StrategicWorldDailyResearchQuantityTests.Content(withSite, extraResearch: mode == 0 ?
            "  - {name: EQUIV_A, increaseCounter: [LEFT_COUNTER]}\n  - {name: EQUIV_B, increaseCounter: [RIGHT_COUNTER]}" :
            "  - {name: CORE, decreaseCounter: [TRACE_COUNTER, TRACE_COUNTER]}\n  - {name: EQUIV_A, increaseCounter: [TRACE_COUNTER]}\n  - {name: EQUIV_B, increaseCounter: [TRACE_COUNTER, TRACE_COUNTER], decreaseCounter: [TRACE_COUNTER]}");

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int start, bool withSite = false)
    {
        var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, seed, withSite);
        return initial with
        { NextIds = new Dictionary<string, int>(initial.NextIds) { ["LEFT_COUNTER"] = start, ["RIGHT_COUNTER"] = start, ["TRACE_COUNTER"] = start } };
    }
}
