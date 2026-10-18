using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchScorePrefixTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SignedDailyScorePrefixesMatchNativeOrderedCapacityAndReloads(int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(mode);
        var projection = CampaignState.Restore(StrategicWorldDailyResearchQuantityTests.Snapshot(content, 1, false), content, new SplitMix64RandomSource(1));
        long work = 0;
        Assert.True(projection.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var forecast));
        var rows = TestFixtures.Rows(oracle.RootElement, "researchDailyScorePrefixRows").Where(row => row[0].GetInt32() == mode).ToArray();
        var actualPrefixes = forecast.DiscoveryAlternatives.Select(branch => new long[]
        {
            mode, branch.CompletedResearch!.Contains("EQUIV_A") ? 0 : 1,
            branch.MinimumScorePrefix!.Value, branch.MaximumScorePrefix!.Value, branch.ScoreChange!.Value,
        }).OrderBy(row => row[1]);
        TestFixtures.AssertJsonEqual(rows.Select(row => new long[]
        {
            row[0].GetInt64(), row[1].GetInt64(), row[2].GetInt64(), row[3].GetInt64(), row[4].GetInt64(),
        }), JsonSerializer.SerializeToUtf8Bytes(actualPrefixes));
        Assert.Equal(rows.Min(row => row[2].GetInt64()), forecast.MinimumScorePrefix);
        Assert.Equal(rows.Max(row => row[3].GetInt64()), forecast.MaximumScorePrefix);
        Assert.InRange(work, 1, 1_000_000);
        HashSet<int> seen = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, seed, false) with
            { ResearchScores = [mode == 0 ? int.MinValue + 2 : int.MaxValue - 2] };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            seen.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchDailyScorePrefixRows").Single(row => row[0].GetInt32() == mode && row[1].GetInt32() == choice);
            Assert.Equal(expected[4].GetInt64(), (long)after.ResearchScores[^1] - initial.ResearchScores[^1]);
            Assert.Equal(expected[5].GetInt32() - 1, after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
            Assert.Equal(expected[7].GetInt32(), after.NextIds["EQUIV_COUNTER"]);
            Assert.DoesNotContain("UNREACHED_AUTO", after.CompletedResearch);
            Assert.Equal("STR_OUT", after.Bases[0].Crafts[0].Logistics!.Status);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            campaign = ReloadRepeatedly(content, campaign, seed, "daily-score-prefix.sav", 3, after);
            AssertContinuesLike(control, campaign, 1);
        }
        Assert.Equal(2, seen.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnsafeFinalOrIntermediateDailyScoresRemainAtomic(int mode)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(mode);
        foreach (var score in new[] { mode == 0 ? int.MinValue + 1 : int.MaxValue - 1, mode == 0 ? int.MaxValue - 1 : int.MinValue + 1 })
        {
            var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, 17, false) with { ResearchScores = [score] };
            AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
                "Research and production scores exceed the supported range.");
        }
    }

    [Fact]
    public void FutureSiteAutomaticScoreDoesNotInflateTheDailyReservation()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldDailyResearchQuantityContextTests.Content(0);
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, seed, true) with { ResearchScores = [int.MaxValue - 5] };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(int.MaxValue, after.ResearchScores[^1]);
            Assert.Contains("SITE", after.CompletedResearch);
            Assert.DoesNotContain("SITE_AUTO", after.CompletedResearch);
            Assert.Empty(after.World.MissionSites);
            Assert.Equivalent(after, Reload(content, campaign, seed, "daily-before-site-score.sav").Capture(), strict: true);
        }
    }

    internal static RuntimeContent Content(int mode, string extra = "") =>
        StrategicWorldDailyResearchQuantityTests.Content(false, extra: extra,
            extraResearch: $"  - {{name: CORE, points: {(mode == 0 ? -4 : 4)}}}\n  - {{name: EQUIV_A, points: {(mode == 0 ? 3 : -3)}}}\n  - {{name: EQUIV_B, points: {(mode == 0 ? 2 : -2)}}}\n  - {{name: FUTURE_GATE, cost: 1}}\n  - {{name: UNREACHED_AUTO, cost: 0, points: 99, dependencies: [FUTURE_GATE]}}");
}
