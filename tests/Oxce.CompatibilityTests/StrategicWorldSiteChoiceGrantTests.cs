using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteChoiceGrantTests
{
    private static readonly string[] ChoiceTopics = ["BONUS", "CORE"];
    private static readonly string[] MissingChoiceTopics = ["BONUS", "MISSING"];

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SelectedResearchAndOwnEventsFollowTheirDeploymentAndPersist(bool cache, bool generated)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = StrategicWorldNewbornSiteChoiceResearchTests.Content(cache, generated, differentGrant: true, ownEvents: true);
        var seenA = false;
        var seenOnlyB = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldNewbornSiteChoiceResearchTests.Snapshot(content, generated) with { RandomState = seed };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)), seed, "different-site-grants-before.sav");
            var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var a = after.NextIds.GetValueOrDefault("CHOICE_A_MARKER", 1) - 1;
            var b = after.NextIds.GetValueOrDefault("CHOICE_B_MARKER", 1) - 1;
            Assert.Equal(2, a + b);
            Assert.Equal(a != 0, after.CompletedResearch.Contains("BONUS"));
            Assert.Equal(a != 0 ? 2 : 1, after.ResearchDiary.Count);
            Assert.Equal("CORE", after.ResearchDiary[0].RuleId);
            if (a != 0) Assert.Equal("BONUS", after.ResearchDiary[1].RuleId);
            Assert.Equal(control.Capture().RandomState, after.RandomState);
            Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.Missions);
            Assert.Empty(after.World.Events);
            seenA |= a != 0;
            seenOnlyB |= a == 0;
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "different-site-grants-after.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.True(seenA);
        Assert.True(seenOnlyB);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SuppressedDeploymentChoiceDoesNotInventAResearchGrant(bool cache, bool generated)
    {
        var content = StrategicWorldNewbornSiteChoiceResearchTests.Content(cache, generated, ownEvents: true, nullChoice: true);
        var seenBirth = false;
        var seenNone = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldNewbornSiteChoiceResearchTests.Snapshot(content, generated) with { RandomState = seed };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var births = after.NextIds.GetValueOrDefault("CHOICE_A_MARKER", 1) - 1;
            Assert.InRange(births, 0, 2);
            Assert.Equal(births != 0, after.CompletedResearch.Contains("BONUS"));
            Assert.Equal(births != 0 ? 2 : 1, after.ResearchDiary.Count);
            Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.Missions);
            Assert.Empty(after.World.Events);
            Assert.Equivalent(after, Reload(content, campaign, seed, "suppressed-site-choice.sav").Capture(), strict: true);
            seenBirth |= births != 0;
            seenNone |= births == 0;
        }
        Assert.True(seenBirth);
        Assert.True(seenNone);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UnchosenResearchCannotSuppressDueEventsAndKernelRestoresNoOpBranches(bool cache, bool generated)
    {
        var content = StrategicWorldNewbornSiteChoiceResearchTests.Content(cache, generated, differentGrant: true, ownEvents: true);
        var initial = StrategicWorldNewbornSiteChoiceResearchTests.Snapshot(content, generated);
        AssertTimeBlocked(CampaignState.Restore(initial with { World = initial.World with { Events = [new GeoscapeEventSnapshot("OWN_A", 30)] } }, content, new SplitMix64RandomSource(17)),
            "Strategic event consequences require campaign event execution (Phase 6 branch 6).");
        var campaign = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var forecast = campaign.MissionResearch.CreateForecast();
        Assert.Null(forecast.AdvanceChoices(ChoiceTopics, false, out var conditional));
        Assert.Contains("BONUS", conditional["BONUS"]);
        Assert.DoesNotContain("CORE", conditional["BONUS"]);
        Assert.Contains("CORE", conditional["CORE"]);
        Assert.DoesNotContain("BONUS", conditional["CORE"]);
        Assert.DoesNotContain("CORE", forecast.GuaranteedResearch);
        Assert.DoesNotContain("BONUS", forecast.GuaranteedResearch);
        forecast = campaign.MissionResearch.CreateForecast();
        Assert.Null(forecast.AdvanceChoices(MissingChoiceTopics, false, out conditional));
        Assert.DoesNotContain("BONUS", conditional["MISSING"]);
        Assert.DoesNotContain("BONUS", forecast.GuaranteedResearch);
        Assert.Contains("BONUS", forecast.PossibleResearch);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }
}
