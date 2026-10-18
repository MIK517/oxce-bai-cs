using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchProductionScoreTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void GuaranteedDailyScoreRangeComposesWithActualProductionAndReloads(int mode, bool stopped)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Content(mode, stopped);
        HashSet<int> seen = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = Snapshot(content, seed, mode);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
            seen.Add(choice);
            var expected = TestFixtures.Rows(oracle.RootElement, "researchDailyProductionScoreRows").Single(row => row[0].GetInt32() == mode && row[1].GetInt32() == choice && row[2].GetInt32() == 4 && row[3].GetInt32() == (stopped ? 1 : 0));
            TestFixtures.AssertJsonEqual(new long[] { expected[6].GetInt64(), expected[9].GetInt64() }, JsonSerializer.SerializeToUtf8Bytes(new long[] { (long)after.ResearchScores[^1] - initial.ResearchScores[^1], after.Bases[0].Items.GetValueOrDefault("SUPPLY") }));
            Assert.Equal(expected[4].GetInt32(), stopped ? 1 : 4);
            Assert.Empty(after.Bases[0].Productions);
            Assert.DoesNotContain("UNREACHED_AUTO", after.CompletedResearch);
            Assert.Equal("STR_OUT", after.Bases[0].Crafts[0].Logistics!.Status);
            Assert.True(Assert.Single(after.World.Ufos).Hunting);
            var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
            campaign = ReloadRepeatedly(content, campaign, seed, "daily-production-score.sav", 3, after);
            AssertContinuesLike(control, campaign, 1);
        }
        Assert.Equal(2, seen.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnsafeProductionOrUnguaranteedOffsetStillRefusesTheWholeTick(int mode)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(mode, false);
        var initial = Snapshot(content, 17, mode);
        AssertTimeBlocked(CampaignState.Restore(initial with
        { Bases = [initial.Bases[0] with { Productions = [initial.Bases[0].Productions[0] with { Assigned = 5, Amount = 5 }] }] }, content, new SplitMix64RandomSource(17)),
            "Research and production scores exceed the supported range.");
        content = Content(mode, false, uncertainOffset: true);
        initial = Snapshot(content, 17, mode);
        initial = initial with
        {
            Bases = [initial.Bases[0] with
            { Productions = [new("UNCERTAIN_OFFSET", 4, 0, 4, false, false, false, new Dictionary<string, int>()), new("SCORE_OUTPUT", 6, 0, 6, false, false, false, new Dictionary<string, int>())] }],
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Research and production scores exceed the supported range.");
    }

    internal static RuntimeContent Content(int mode, bool stopped, bool uncertainOffset = false, bool withSite = false) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
            crafts: [{type: SHIP, fuelMax: 1000, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]
            ufos: [{type: UFO_HUNTER, radarRange: 20000}]
            facilities:
              - {type: EQUIV_LAB, mapName: LOGISTICS, labs: 2}
              - {type: SCORE_WORKSHOP, mapName: LOGISTICS, workshops: 10}
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, points: {{{(mode == 0 ? -4 : 4)}}}, getOneFree: [EQUIV_A, EQUIV_B], spawnedItem: SUPPLY, spawnedItemCount: 1, increaseCounter: [EQUIV_COUNTER]}
              - {name: EQUIV_A, cost: 1, points: {{{(mode == 0 ? 3 : -3)}}}, spawnedItem: SUPPLY, spawnedItemCount: 2, increaseCounter: [EQUIV_COUNTER]}
              - {name: EQUIV_B, cost: 1, points: {{{(mode == 0 ? 2 : -2)}}}, spawnedItem: SUPPLY, spawnedItemCount: 3, increaseCounter: [EQUIV_COUNTER]}
              {{{(withSite ? "- {name: SITE, cost: 1, points: " + (mode == 0 ? 1 : -1) + "}" : "")}}}
              - {name: FUTURE_GATE, cost: 1}
              - {name: UNREACHED_AUTO, cost: 0, points: 99, dependencies: [FUTURE_GATE]}
            manufacture:
              - {name: SCORE_OUTPUT, time: 1, cost: {{{(stopped ? 2000 : 0)}}}, points: {{{(mode == 0 ? 1 : -1)}}}, producedItems: {SUPPLY: 0}}
              {{{(uncertainOffset ? "- {name: UNCERTAIN_OFFSET, time: 1, cost: 2000, points: " + (mode == 0 ? -1 : 1) + ", producedItems: {SUPPLY: 0}}" : "")}}}
            {{{(withSite ? StrategicWorldSiteResearchTests.Deployment.Replace("unlockedResearchOnDespawn: CORE", "unlockedResearchOnDespawn: SITE", StringComparison.Ordinal) : "")}}}
            """, extraRulesAfterOverlays: true);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int mode)
    {
        var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, seed, false);
        return initial with
        {
            ResearchScores = [mode == 0 ? int.MaxValue - 3 : int.MinValue + 3],
            Funds = [0],
            Bases = [initial.Bases[0] with
            {
                Facilities = [.. initial.Bases[0].Facilities, new("SCORE_WORKSHOP", 2, 1, 0, 0, false, false, false)],
                Productions = [new("SCORE_OUTPUT", 4, 0, 4, false, false, false, new Dictionary<string, int>())],
            }],
        };
    }
}
