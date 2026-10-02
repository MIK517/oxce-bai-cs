using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionInitializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpaceObjectivesInitializeWithoutExecutingTheirFutureEffects(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-initialization.rul"], cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows").Where(row =>
                     row[0].GetString()!.StartsWith("objective", StringComparison.Ordinal) && row[0].GetString() != "objective3"))
        {
            var objective = int.Parse(row[0].GetString()![9..], System.Globalization.CultureInfo.InvariantCulture);
            var snapshot = CreateSnapshot(content, "Space objective initialization") with { MonthsPassed = 50 + objective };
            var campaign = CampaignState.Restore(snapshot, content, new MinimumRandom());
            Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
            var mission = Assert.Single(campaign.Capture().World.Missions);
            Assert.Equal($"START_OBJECTIVE_{objective}", mission.RuleId);
            Assert.Equal(row[3][4].GetInt32(), mission.SpawnCountdown);
            Assert.Equal((0, 0, 0, -1, false), (mission.NextWave, mission.NextUfoCounter, mission.LiveUfos,
                mission.MissionSiteZoneArea, mission.Interrupted));
            Assert.Null(mission.AlienBase);
            Assert.Empty(campaign.Capture().World.Ufos);
            Assert.Empty(campaign.Capture().World.AlienBases);
            Assert.Empty(campaign.Capture().World.MissionSites);
            Reload(content, campaign, 17, "space-objective.sav");
        }
    }

    private sealed class MinimumRandom : IStatefulRandomSource
    {
        public ulong State { get; private set; }
        public void Restore(ulong state) => State = state;
        public int NextExclusive(int maximum) { State++; return 0; }
        public int NextInclusive(int minimum, int maximum) { State++; return minimum; }
        public double NextUnit() { State++; return 0; }
    }
}
