using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionRaceFallbackTests
{
    [Fact]
    public void MissionRaceFallbackMatchesReferenceAndPersistsRunHistory()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        var row = TestFixtures.Rows(oracle.RootElement, "rows").Single(row => row[0].GetString() == "weighted");
        var content = Load();
        var snapshot = CreateSnapshot(content, "Mission race fallback") with { MonthsPassed = 10, RandomState = 17 };
        var expected = new SplitMix64RandomSource(17);
        expected.NextInclusive(0, 99); // execution
        expected.NextInclusive(0, 99); // target base
        expected.NextInclusive(1, 1); // region
        expected.NextInclusive(1, 1); // mission
        expected.NextInclusive(1, 1); // mission rule race
        var countdown = WorldTrajectory.SpawnCountdown(9000, expected);
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var after = campaign.Capture();
        var mission = Assert.Single(after.World.Missions);
        Assert.Equal(row[3][2].GetString(), mission.Race);
        Assert.Equal(countdown, mission.SpawnCountdown);
        Assert.Equal(expected.State, after.RandomState);
        campaign = Reload(content, campaign, 17, "mission-race-fallback.sav");
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Single(campaign.Capture().World.Missions);
        Assert.Equal(after.RandomState, campaign.Capture().RandomState);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void MissingFutureAndExcessiveRaceWeightsFailBeforeMutation(int month)
    {
        var content = Load();
        var snapshot = CreateSnapshot(content, "Invalid mission races") with { MonthsPassed = month, RandomState = 17 };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        if (month == 13)
            Assert.Throws<InvalidDataException>(() => campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)));
        else
            Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void ExplicitRaceWeightsOverrideUnavailableMissionRace()
    {
        var content = Load();
        var snapshot = CreateSnapshot(content, "Explicit mission races") with { MonthsPassed = 14, RandomState = 17 };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        Assert.Equal("RACE_A", Assert.Single(campaign.Capture().World.Missions).Race);
        Reload(content, campaign, 17, "explicit-mission-race.sav");
    }

    private static Oxce.Mods.Rulesets.Content.RuntimeContent Load()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-race-fallback.rul"]);
    }
}
