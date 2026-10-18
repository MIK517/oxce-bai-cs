using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchDiaryCapacityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundedDailyDiaryCountsMatchNativeOrderAtTheIdentityLimit(bool lookup)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-research-diary-execution");
        var content = Content(lookup);
        HashSet<int> seen = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, seed, false) with
            { ResearchDiarySequence = ulong.MaxValue - (lookup ? 3UL : 2UL) };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var forecast));
            Assert.Equal(lookup ? 3 : 2, forecast.DiscoveryAlternatives.Max(branch => branch.DiaryEntries!.Value));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            seen.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "stockScopeRows").Single(row => row[0].GetInt32() == (lookup ? 1 : 0) && row[1].GetInt32() == choice);
            TestFixtures.AssertJsonEqual(expected[2], JsonSerializer.SerializeToUtf8Bytes(after.ResearchDiary.Select(entry => entry.RuleId)));
            Assert.Equal((byte)ResearchDiscoverySource.Base, after.ResearchDiary[0].SourceType);
            Assert.All(after.ResearchDiary.Skip(1), entry =>
            {
                Assert.Equal((byte)ResearchDiscoverySource.FreeFrom, entry.SourceType);
                Assert.Equal("CORE", entry.SourceName);
            });
            Assert.Equal(initial.ResearchDiarySequence + (ulong)after.ResearchDiary.Count, after.ResearchDiarySequence);
            Assert.DoesNotContain("UNREACHED_AUTO", after.CompletedResearch);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            campaign = ReloadRepeatedly(content, campaign, seed, "daily-diary-capacity.sav", 3, after);
            AssertContinuesLike(control, campaign, 1);
        }
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void ExactDiaryPopulationHeadroomAllowsTheBoundedDailyTransition()
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(false);
        var diary = Enumerable.Range(0, CampaignState.MaximumResearchDiaryEntries - 2).Select(index =>
            new ResearchDiaryEntrySnapshot("CORE", 1998, 1, 1, 0, "Old base") { PreservationKey = "old:" + index }).ToArray();
        var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, 17, false) with { ResearchDiary = diary };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(CampaignState.MaximumResearchDiaryEntries, after.ResearchDiary.Count);
        Assert.Equal(diary, after.ResearchDiary.Take(diary.Length));
        Assert.Equivalent(after, CampaignState.Restore(after, content, new SplitMix64RandomSource(17)).Capture(), strict: true);
        // The gameplay population ceiling is independent of the YAML document
        // node ceiling. Keep the latter intact for this deliberately huge save.
        var error = Assert.Throws<YamlFormatException>(() => YamlCompatibilityReader.Parse(OxceSaveAdapter.EmitNewCampaign(after), "daily-diary-population.sav"));
        Assert.Contains("1000000-node limit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnyRewardExhaustingTheDiaryStillBlocksAtomically(bool lookup)
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(lookup);
        var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, 17, false) with
        { ResearchDiarySequence = ulong.MaxValue - (lookup ? 2UL : 1UL) };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Research diary exceeds its supported population or identity range.");
    }

    [Fact]
    public void DailyDiaryHeadroomIncludesTheLaterSiteDiscovery()
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldDailyResearchQuantityContextTests.Content(0);
        var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, 17, true) with { ResearchDiarySequence = ulong.MaxValue - 3 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(3, after.ResearchDiary.Count);
        Assert.Equal("SITE", after.ResearchDiary[^1].RuleId);
        Assert.Equal((byte)ResearchDiscoverySource.Mission, after.ResearchDiary[^1].SourceType);
        Assert.Equal(ulong.MaxValue, after.ResearchDiarySequence);
        Assert.Equivalent(after, Reload(content, campaign, 17, "daily-site-diary-capacity.sav").Capture(), strict: true);
        AssertTimeBlocked(CampaignState.Restore(initial with { ResearchDiarySequence = ulong.MaxValue - 2 }, content, new SplitMix64RandomSource(17)),
            "Mission research diary exceeds its supported population or identity range.");
    }

    internal static RuntimeContent Content(bool lookup) =>
        StrategicWorldDailyResearchQuantityTests.Content(false, extraResearch:
            "  - {name: FUTURE_GATE, cost: 1}\n  - {name: UNREACHED_AUTO, cost: 0, dependencies: [FUTURE_GATE]}\n" +
            (lookup ? "  - {name: EQUIV_A, lookup: EQUIV_Z}\n  - {name: EQUIV_Z, cost: 1}" : ""));
}
