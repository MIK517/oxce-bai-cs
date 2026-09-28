using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionScriptTests
{
    private static Oxce.Mods.Rulesets.Content.RuntimeContent Load(bool fromCache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-script-rules");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-scripts.rul"], fromCache);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderedMissionsPersistRunLimitAndStrategyTable(bool fromCache)
    {
        var content = Load(fromCache);
        var initial = CreateSnapshot(content, "Mission scripts") with { RandomState = 17 };
        var expected = new SplitMix64RandomSource(17);
        expected.NextInclusive(0, 99); // START execution odds
        expected.NextInclusive(0, 99); // START target-base odds
        expected.NextInclusive(1, 1); // region
        expected.NextInclusive(1, 1); // mission
        expected.NextInclusive(1, 1); // race
        var firstDelay = 60 + expected.NextInclusive(0, 30);
        expected.NextInclusive(0, 99); // FOLLOW execution odds
        expected.NextInclusive(0, 99); // FOLLOW target-base odds
        expected.NextInclusive(1, 1);
        expected.NextInclusive(1, 1);
        expected.NextInclusive(1, 1);
        var secondDelay = WorldTrajectory.SpawnCountdown(9000, expected);

        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var first = campaign.Capture();
        Assert.Equal([firstDelay, secondDelay], first.World.Missions.Select(mission => mission.SpawnCountdown));
        Assert.Equal(["MISSION_SCOUT", "MISSION_SCOUT"], first.World.Missions.Select(mission => mission.RuleId));
        Assert.Equal(["REGION", "REGION"], first.World.Missions.Select(mission => mission.RegionId));
        Assert.Equal(["RACE_A", "RACE_A"], first.World.Missions.Select(mission => mission.Race));
        Assert.Equal(1, first.World.Strategy.MissionRuns.Single(pair => pair.Key == "RUNS").Value);
        Assert.DoesNotContain(first.World.Strategy.RegionMissions.Single(pair => pair.Key == "REGION").Value,
            pair => pair.Key == "MISSION_SCOUT");
        Assert.Equal(expected.State, first.RandomState);

        campaign = Reload(content, campaign, 17, "mission-scripts.sav");
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var second = campaign.Capture();
        Assert.Equal(3, second.World.Missions.Count);
        Assert.Equal(1, second.World.Strategy.MissionRuns.Single(pair => pair.Key == "RUNS").Value);
        Assert.Equal(3, second.World.Missions.Select(mission => mission.Id).Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOddsSetFalseLabelForLaterNegativeConditional(bool fromCache)
    {
        var content = Load(fromCache);
        var initial = CreateSnapshot(content, "Mission negative condition") with { MonthsPassed = 1, RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Equal("MISSION_SCOUT", Assert.Single(campaign.Capture().World.Missions).RuleId);
        Assert.Equal(initial.MonthsPassed, campaign.Capture().MonthsPassed);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(6, true)]
    public void InvalidLabelsAndUnsupportedObjectivesBlockAtomically(int month, bool fromCache)
    {
        var content = Load(fromCache);
        var initial = CreateSnapshot(content, "Mission script guard") with { MonthsPassed = month, RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(7, 1, false)]
    [InlineData(7, 1, true)]
    [InlineData(8, 2, false)]
    [InlineData(8, 2, true)]
    public void MissingConditionsPassAndAvailableListIsCapturedBeforeRunUpdates(int month, int count, bool fromCache)
    {
        var content = Load(fromCache);
        var initial = CreateSnapshot(content, "Mission availability") with { MonthsPassed = month, RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var snapshot = campaign.Capture();
        Assert.Equal(count, snapshot.World.Missions.Count);
        if (month == 8) Assert.Equal(2, snapshot.World.Strategy.MissionRuns.Single(pair => pair.Key == "SHARED").Value);
    }

    [Fact]
    public void ExhaustedMissionIdsBlockBeforeMutation()
    {
        var content = Load(false);
        var initial = CreateSnapshot(content, "Mission ID bound") with { RandomState = 17 };
        initial = initial.WithNextId("ALIEN_MISSIONS", int.MaxValue);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void NoEligibleScriptsDoNotConsumeMissionCapacityOrRandomness()
    {
        var content = Load(false);
        var initial = CreateSnapshot(content, "No mission scripts") with { MonthsPassed = 9, RandomState = 17 };
        initial = initial.WithNextId("ALIEN_MISSIONS", int.MaxValue);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Equivalent(initial, campaign.Capture(), strict: true);
    }
}
