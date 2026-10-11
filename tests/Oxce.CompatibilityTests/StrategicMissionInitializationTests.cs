using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionInitializationTests
{
    [Fact]
    public void SpaceObjectivesInitializeWithoutExecutingTheirFutureEffects()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-initialization.rul"]);
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

    [Fact]
    public void ExistingBaseSelectionAndAbortMatchReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-operation");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-initialization.rul"]);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var op = row[0].GetInt32();
            var empty = row[1].GetInt32() != 0;
            var last = row[2].GetInt32() != 0;
            var guarded = row[3].GetInt32() != 0;
            var snapshot = CreateSnapshot(content, "Existing operation base") with { MonthsPassed = 60 + op };
            snapshot = snapshot with
            {
                World = snapshot.World with
                {
                    AlienBases = empty ? [] :
                [new AlienBaseSnapshot(1, "ALIEN_BASE_DEPLOY", "RACE_A", 0.2, 0),
                 new AlienBaseSnapshot(2, "ALIEN_BASE_DEPLOY", "RACE_A", 2, 0),
                 new AlienBaseSnapshot(3, "ALIEN_BASE_DEPLOY", "RACE_A", 0.4, 0)]
                }
            };
            var campaign = CampaignState.Restore(snapshot, content, new MinimumRandom(last));
            var before = campaign.Capture();
            var result = campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0));
            if (guarded)
            {
                Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events));
                Assert.Equivalent(before, campaign.Capture(), strict: true);
                continue;
            }
            Assert.Empty(result.Events);
            var mission = Assert.Single(campaign.Capture().World.Missions);
            Assert.Equal(row[4].GetInt32(), mission.SpawnCountdown);
            Assert.Equal(row[5].GetInt32(), mission.AlienBase?.Id ?? 0);
            Assert.Equal(row[6].GetInt32() != 0, mission.Interrupted);
            Reload(content, campaign, 17, "existing-operation.sav");
        }
    }

    private sealed class MinimumRandom(bool last = false) : IStatefulRandomSource
    {
        public ulong State { get; private set; }
        public void Restore(ulong state) => State = state;
        public int NextExclusive(int maximum) { State++; return 0; }
        public int NextInclusive(int minimum, int maximum) { State++; return last ? maximum : minimum; }
        public double NextUnit() { State++; return 0; }
    }
}
