using System.Text;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicAdhocScriptTests
{
    private static Oxce.Mods.Rulesets.Content.RuntimeContent Load(bool fromCache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-adhoc-scripts");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-adhoc-scripts.rul"], fromCache);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TaggedSelectionAndOrderMatchExtractedReference(bool fromCache)
    {
        var content = Load(fromCache);
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-adhoc-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var eventName = row.GetProperty("event").GetString()!;
            var initial = CreateSnapshot(content, "Adhoc reference") with { RandomState = 17 };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            var result = campaign.Execute(new ScheduleAdhocWorldMissions($"EVENT_{eventName}", 5, 10));
            if (eventName == "BAD")
            {
                Assert.Equal("BAD_SITE", Assert.Single(row.GetProperty("scripts").EnumerateArray()).GetString());
                Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events));
                Assert.Equivalent(initial, campaign.Capture(), strict: true);
                continue;
            }
            Assert.Empty(result.Events);
            var actual = campaign.Capture().World.Missions.Select(mission => mission.SpawnCountdown switch
            {
                60 => "SECOND_FIRST",
                90 => "FOLLOW_SECOND",
                120 => "OTHER_FIRST",
                150 => "THRESHOLD",
                _ => throw new Xunit.Sdk.XunitException("Unexpected adhoc mission delay."),
            }).ToArray();
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetProperty("scripts").GetRawText())),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(actual)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EventTagsFilterOnFirstScriptTagAndPersistRunHistory(bool fromCache)
    {
        var content = Load(fromCache);
        var initial = CreateSnapshot(content, "Adhoc scripts") with { RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));

        Assert.Empty(campaign.Execute(new ScheduleAdhocWorldMissions("EVENT_SECOND", 0, 0)).Events);
        var first = campaign.Capture();
        Assert.Equal(2, first.World.Missions.Count);
        Assert.Equal(1, first.World.Strategy.MissionRuns.Single(pair => pair.Key == "ADHOC_RUN").Value);
        Assert.All(first.World.Missions, mission => Assert.Equal("MISSION_SCOUT", mission.RuleId));

        campaign = Reload(content, campaign, 17, "adhoc-scripts.sav");
        Assert.Empty(campaign.Execute(new ScheduleAdhocWorldMissions("EVENT_SECOND", 0, 0)).Events);
        Assert.Equal(3, campaign.Capture().World.Missions.Count);
        Assert.Equal(1, campaign.Capture().World.Strategy.MissionRuns.Single(pair => pair.Key == "ADHOC_RUN").Value);
        Assert.Empty(campaign.Execute(new ScheduleAdhocWorldMissions("EVENT_OTHER", 0, 0)).Events);
        Assert.Equal(4, campaign.Capture().World.Missions.Count);
        Assert.Equal(4, campaign.Capture().World.Missions.Select(mission => mission.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyTagsAndMissingEventDoNotAdvanceWorld(bool fromCache)
    {
        var content = Load(fromCache);
        var initial = CreateSnapshot(content, "Adhoc empty") with { RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));

        Assert.Empty(campaign.Execute(new ScheduleAdhocWorldMissions("EVENT_EMPTY", 0, 0)).Events);
        Assert.Equivalent(initial, campaign.Capture(), strict: true);
        Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new ScheduleAdhocWorldMissions("MISSING_EVENT", 0, 0)).Events));
        Assert.Equivalent(initial, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedTaggedMissionBlocksBeforeRandomnessOrMutation(bool fromCache)
    {
        var content = Load(fromCache);
        var initial = CreateSnapshot(content, "Adhoc site guard") with { RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new ScheduleAdhocWorldMissions("EVENT_BAD", 0, 0)).Events));
        Assert.Equivalent(initial, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScoreAndFundsCriteriaUseSuppliedCurrentContext(bool fromCache)
    {
        var content = Load(fromCache);
        var initial = CreateSnapshot(content, "Adhoc eligibility") with { MonthsPassed = 1, RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));

        Assert.Empty(campaign.Execute(new ScheduleAdhocWorldMissions("EVENT_THRESHOLD", 4, 10)).Events);
        Assert.Empty(campaign.Execute(new ScheduleAdhocWorldMissions("EVENT_THRESHOLD", 5, 9)).Events);
        Assert.Equivalent(initial, campaign.Capture(), strict: true);
        Assert.Empty(campaign.Execute(new ScheduleAdhocWorldMissions("EVENT_THRESHOLD", 5, 10)).Events);
        Assert.Equal("MISSION_SCOUT", Assert.Single(campaign.Capture().World.Missions).RuleId);
    }
}
