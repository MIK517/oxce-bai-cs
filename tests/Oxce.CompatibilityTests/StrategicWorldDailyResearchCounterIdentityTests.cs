using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyResearchCounterIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentOrderedTracesWithTheSameFinalIdentityComposeWithBirths(bool person)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = StrategicWorldDailyResearchQuantityIdentityTests.Content(person, floorRewardTrace: true);
        var counter = person ? "STR_SOLDIER" : "PRODUCED_CRAFT";
        foreach (var start in new[] { 1, 20, int.MaxValue - 1 })
        {
            HashSet<int> seen = [];
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var initial = StrategicWorldDailyResearchQuantityIdentityTests.Snapshot(content, seed, counter, start);
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
                seen.Add(choice);
                var expected = TestFixtures.Rows(oracle.RootElement, "researchCounterAlternativeBirthRows").Single(row => row[0].GetInt32() == (person ? 1 : 0) && row[1].GetInt32() == start && row[2].GetInt32() == choice);
                var identity = person ? Assert.Single(after.Bases[0].Transfers).Soldier!.Id : Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "PRODUCED_CRAFT").Id;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new long[] { person ? 1 : 0, start, choice, identity, identity, after.NextIds[counter], after.Bases[0].Items.GetValueOrDefault("SUPPLY"), after.Bases[0].Transfers.Count, after.ResearchScores[^1] - initial.ResearchScores[^1] });
                TestFixtures.AssertJsonEqual(expected, actual);
                Assert.Equal(2, after.NextIds["EQUIV_COUNTER"]);
                Assert.Equal("STR_OUT", after.Bases[0].Crafts[0].Logistics!.Status);
                Assert.True(Assert.Single(after.World.Ufos).Hunting);
                var control = CampaignState.Restore(after, content, new SplitMix64RandomSource(seed));
                campaign = ReloadRepeatedly(content, campaign, seed, "counter-alternative-birth.sav", 3, after);
                AssertContinuesLike(control, campaign, 1);
            }
            Assert.Equal(2, seen.Count);
        }
    }

    [Fact]
    public void DifferentFinalCraftIdentitiesRemainGuardedAndCancellationClearsTheProof()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldDailyResearchQuantityIdentityTests.Content(false, floorRewardTrace: true, differentFinalCounters: true);
        var initial = StrategicWorldDailyResearchQuantityIdentityTests.Snapshot(content, 17, "PRODUCED_CRAFT", 20);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Craft production requires daily research identity forecasts.");
        AssertTimeBlocked(campaign, "Craft production requires daily research identity forecasts.");
        var cancelled = campaign.Execute(new ConfigureResearchProject(initial.Bases[0].Id, "CORE", 0, Cancel: true));
        Assert.Empty(cancelled.Events.OfType<CampaignActionBlocked>());
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(20, Assert.Single(campaign.Capture().Bases[0].Crafts, craft => craft.RuleId == "PRODUCED_CRAFT").Id);
        Reload(content, campaign, 17, "different-counter-after-cancel.sav");
    }

    [Fact]
    public void UnsafeConsumedCounterIntermediateStillBlocksBeforeBirth()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldDailyResearchQuantityIdentityTests.Content(false);
        var initial = StrategicWorldDailyResearchQuantityIdentityTests.Snapshot(content, 17, "PRODUCED_CRAFT", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), "Craft production exhausts the craft identity range.");
    }
}
