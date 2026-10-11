using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldResearchCounterLabelTests
{
    [Fact]
    public void WhitespaceCounterLabelsKeepNativeIdentityThroughDailyWorldTicksAndRewrites()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        string[] labels = ["", " ", "\t", "\u00a0"];
        for (var index = 0; index < labels.Length; index++)
        {
            var label = labels[index];
            var quoted = JsonSerializer.Serialize(label);
            var content = StrategicWorldDailyResearchQuantityTests.Content(false, extraResearch:
                $"  - {{name: EQUIV_A, increaseCounter: [{quoted}, {quoted}]}}\n  - {{name: EQUIV_B, decreaseCounter: [{quoted}]}}");
            HashSet<int> choices = [];
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, seed, false);
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var choice = after.CompletedResearch.Contains("EQUIV_A") ? 0 : 1;
                choices.Add(choice);
                var expected = TestFixtures.Rows(oracle.RootElement, "researchCounterLabelRows").Single(row => row[0].GetInt32() == index && row[1].GetInt32() == choice);
                var actual = JsonSerializer.SerializeToUtf8Bytes(new[] { index, choice, after.NextIds.GetValueOrDefault(label), after.NextIds.ContainsKey(label) ? 1 : 0 });
                Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)), CanonicalJson.Normalize(actual));
                Assert.True(Assert.Single(after.World.Ufos).Hunting);
                Assert.Equal("STR_OUT", after.Bases[0].Crafts[0].Logistics!.Status);
                for (var rewrite = 0; rewrite < 3; rewrite++)
                {
                    campaign = Reload(content, campaign, seed, "research-counter-label.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
            }
            Assert.Equal(2, choices.Count);
        }
    }

    [Fact]
    public void LoadedWhitespaceCounterNamesRemainDistinct()
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldDailyResearchQuantityTests.Content(false);
        var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, 17, false);
        initial = initial with { NextIds = new Dictionary<string, int>(initial.NextIds) { [" "] = 2, ["\t"] = 3, ["\u00a0"] = 4 } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        for (var rewrite = 0; rewrite < 3; rewrite++)
        {
            campaign = Reload(content, campaign, 17, "loaded-counter-label.sav");
            Assert.Equivalent(initial, campaign.Capture(), strict: true);
        }
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(2, campaign.Capture().NextIds[" "]);
        Assert.Equal(3, campaign.Capture().NextIds["\t"]);
        Assert.Equal(4, campaign.Capture().NextIds["\u00a0"]);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData(" ", 0)]
    [InlineData("\t", -1)]
    [InlineData("ordinary", 0)]
    [InlineData("ordinary", -1)]
    public void EmptyCounterNamesAndNonpositiveIdsRemainInvalid(string label, int value)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = StrategicWorldDailyResearchQuantityTests.Content(false);
        var initial = StrategicWorldDailyResearchQuantityTests.Snapshot(content, 17, false);
        initial = initial with { NextIds = new Dictionary<string, int>(initial.NextIds) { [label] = value } };
        if (label.Length == 0)
            Assert.Throws<ArgumentException>(() => CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)));
        else
            Assert.Throws<ArgumentOutOfRangeException>(() => CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)));
    }
}
