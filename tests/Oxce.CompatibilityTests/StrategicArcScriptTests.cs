using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>GeoscapeState::determineAlienMissions arc phase and SavedGame::addFinishedResearch.</summary>
public sealed class StrategicArcScriptTests
{
    [Fact]
    public void SequentialThenPrefilteredRandomAndLaterScriptPersistResearch()
    {
        var content = StrategicReadinessTestContent.Load("strategic-arc-scripts.rul");
        var snapshot = CreateSnapshot(content, "Arc scheduling") with { RandomState = 17 };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));

        var events = campaign.Execute(new ScheduleMonthlyWorldArcs(0, 0)).Events;
        Assert.Equal(["ARC_A", "ARC_A", "ARC_C"], events.Cast<CampaignArcUnlocked>().Select(x => x.RuleId));
        Assert.Equal(["ARC_LOOKUP", "ARC_LOOKUP", "ARC_C"],
            events.Cast<CampaignArcUnlocked>().Select(x => x.ArticleId));
        var first = campaign.Capture();
        Assert.Contains("ARC_A", first.CompletedResearch);
        Assert.Contains("ARC_LOOKUP", first.CompletedResearch);
        Assert.Contains("ARC_ZERO", first.CompletedResearch);
        Assert.Contains("ARC_C", first.CompletedResearch);
        Assert.DoesNotContain("ARC_B", first.CompletedResearch);
        Assert.Equal(snapshot.ResearchScores[^1] + 42, first.ResearchScores[^1]);
        Assert.Equal(0, first.MonthsPassed);

        campaign = Reload(content, campaign, 17, "arc-scripts.sav");
        campaign = CampaignState.Restore(campaign.Capture() with { MonthsPassed = 1 }, content,
            new SplitMix64RandomSource(campaign.Capture().RandomState));
        var next = Assert.Single(campaign.Execute(new ScheduleMonthlyWorldArcs(0, 0)).Events);
        Assert.Equal("ARC_B", Assert.IsType<CampaignArcUnlocked>(next).RuleId);
        Assert.Equal(first.ResearchScores[^1] + 11, campaign.Capture().ResearchScores[^1]);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void UnreachableTailDoesNotBlockButReachableMissingResearchDoes(int month)
    {
        var content = StrategicReadinessTestContent.Load("strategic-arc-scripts.rul");
        var snapshot = CreateSnapshot(content, "Arc preflight") with { MonthsPassed = month, RandomState = 17 };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var events = campaign.Execute(new ScheduleMonthlyWorldArcs(0, 0)).Events;
        if (month == 2)
        {
            Assert.Equal("ARC_B", Assert.IsType<CampaignArcUnlocked>(Assert.Single(events)).RuleId);
            Assert.Contains("ARC_B", campaign.Capture().CompletedResearch);
        }
        else if (month is 3 or 5 or 6 or 7)
        {
            Assert.Contains(month == 7 ? "score" : "MISSING_ARC",
                Assert.IsType<CampaignActionBlocked>(Assert.Single(events)).Reason);
            Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
        }
        else
        {
            Assert.Empty(events);
            var expected = new SplitMix64RandomSource(17);
            expected.NextInclusive(0, 99); // executionOdds: 0 still consumes the reference roll.
            Assert.Equivalent(snapshot with { RandomState = expected.State }, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void UnreachableWeightedChoicesAndSafeScoresProceed(int month)
    {
        var content = StrategicReadinessTestContent.Load("strategic-arc-scripts.rul");
        var snapshot = CreateSnapshot(content, "Arc reachable slots") with { MonthsPassed = month, RandomState = 17 };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var events = campaign.Execute(new ScheduleMonthlyWorldArcs(0, 0)).Events.Cast<CampaignArcUnlocked>().ToArray();
        var after = campaign.Capture();
        Assert.Equal(snapshot.MonthsPassed, after.MonthsPassed);
        if (month == 8)
        {
            Assert.Equal("ARC_B", Assert.Single(events).RuleId);
            Assert.Equal(snapshot.ResearchScores[^1] + 11, after.ResearchScores[^1]);
        }
        else if (month == 9)
        {
            Assert.Equal("SAFE_BIG", Assert.Single(events).RuleId);
            Assert.Equal(snapshot.ResearchScores[^1] + 1_200_000_000, after.ResearchScores[^1]);
        }
        else if (month == 10)
        {
            Assert.Equal(["ARC_A", "ARC_B"], events.Select(item => item.RuleId));
            Assert.Equal(snapshot.ResearchScores[^1] + 40, after.ResearchScores[^1]);
        }
        else
        {
            var expected = new SplitMix64RandomSource(17);
            expected.NextInclusive(0, 99); // execution odds are collected before selection.
            var rule = expected.NextInclusive(1, 2) == 1 ? "WEIGHTED_BIG_A" : "WEIGHTED_BIG_B";
            Assert.Equal(rule, Assert.Single(events).RuleId);
            Assert.Equal(snapshot.ResearchScores[^1] + (rule == "WEIGHTED_BIG_A" ? 1_100_000_000 : 1_150_000_000),
                after.ResearchScores[^1]);
            Assert.Equal(expected.State, after.RandomState);
        }
    }

    [Fact]
    public void PossibleEarlierDisableDoesNotMakeSequentialGrantCertain()
    {
        var content = StrategicReadinessTestContent.Load("strategic-arc-scripts.rul");
        var snapshot = CreateSnapshot(content, "Arc uncertain disable") with
        {
            MonthsPassed = 12,
            RandomState = 17,
            CompletedResearch = ["ARC_A"],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new ScheduleMonthlyWorldArcs(0, 0));
        Assert.Contains("MISSING_ARC", Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events)).Reason);
        Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
    }
}
