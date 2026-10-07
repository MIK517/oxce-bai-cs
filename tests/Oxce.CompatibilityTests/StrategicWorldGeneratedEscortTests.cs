using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldAlienBaseGenerationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedGroundEscortsInitializeBeforeScoringAndStayInactiveUntilTakeoff(bool cache, bool hunting)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var leader in new[] { false, true })
        {
            var expected = TestFixtures.Rows(oracle.RootElement, "spawnEscortRows").Single(row =>
                row[0].GetBoolean() && row[1].GetBoolean() && row[2].GetInt32() == (leader ? 3 : 0) &&
                row[3].GetInt32() == 0 && row[12].GetInt32() == 1);
            var content = GeneratedEscortContent(cache, hunting, ground: true, leader: leader);
            var initial = GeneratedEscortSnapshot(content, hunting);
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "generated-ground-escort-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var born = Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "GENERATED_ESCORT");
            Assert.Equal(expected[4].GetBoolean(), born.Escort);
            Assert.Equal(expected[5].GetBoolean(), born.Escorting);
            Assert.Equal(expected[12].GetInt32(), (int)born.Status);
            Assert.False(born.Hunting);
            Assert.Equal(5, born.SecondsRemaining);
            Assert.Equal(leader ? expected[9].GetInt32() : 550, born.Speed);
            Assert.Equal(new WorldPosition(Degrees(leader ? 8 : 10), Degrees(leader ? 2 : 8)), born.Position);
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + (hunting ? 0 : leader ? 9 : 6), after.Regions[0].ActivityAlien[^1]);
            Assert.Equal(leader ? 2 : 1, Assert.Single(after.World.Missions).LiveUfos);
            if (leader)
            {
                var first = after.World.Ufos[0];
                Assert.Equal(first.UniqueId, born.Destination!.UniqueId);
                Assert.Equal(WorldPosition.Create(Degrees(362), Degrees(2)), born.OriginalWaypoint);
            }
            else Assert.Null(born.OriginalWaypoint);
            campaign = Reload(content, campaign, 81, "generated-ground-escort-after.sav");
            AdvanceUnblocked(campaign, 1);
            var lifted = Assert.Single(campaign.Capture().World.Ufos, ufo => ufo.RuleId == "GENERATED_ESCORT");
            Assert.Equal(UfoStatus.Flying, lifted.Status);
            Assert.Equal(550, lifted.Speed);
            Assert.Equal(leader, lifted.Escorting);
            campaign = Reload(content, campaign, 81, "generated-ground-escort-lifted.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 81, "generated-ground-escort-following.sav");
        }
    }

    private static RuntimeContent GeneratedEscortContent(bool cache, bool hunting, bool ground, bool leader,
        int radar = 0, int mode = 0, int percentage = 100, int count = 1) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
            ufos:
              - type: GENERATED_ESCORT
                speedMax: 2200
                damageMax: 50
                radarRange: {{{radar}}}
                huntSpeed: 150
                huntMode: {{{mode}}}
                hunterKillerPercentage: {{{percentage}}}
                noAlert: true
                missionScore: 3
            ufoTrajectories:
              - id: GENERATED_ESCORT_LEADER_PATH
                waypoints: [[5, 3, 100], [3, 2, 80], [0, 3, 50]]
              - id: GENERATED_ESCORT_PATH
                groundTimer: 2
                waypoints: [[4, {{{(ground ? 0 : 3)}}}, 25], [3, 2, 80], [0, 3, 50]]
            alienDeployments:
              - type: GENERATED_ESCORT_BASE
                markerName: STR_ALIEN_BASE
                genMission: {GENERATED_ESCORT_MISSION: 1}
                genMissionFreq: {{{(hunting ? 0 : 100)}}}
                genMissionLimit: 1
                baseDetectionRange: {{{(hunting ? 500 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: 60
                huntMissionWeights: {0: {GENERATED_ESCORT_MISSION: 1}}
            alienMissions:
              - type: GENERATED_ESCORT_MISSION
                objective: 0
                waves:
                  {{{(leader ? "- {ufo: UFO_SCOUT, count: 1, trajectory: GENERATED_ESCORT_LEADER_PATH, timer: 0}\n      " : "")}}}- {ufo: GENERATED_ESCORT, count: {{{count}}}, trajectory: GENERATED_ESCORT_PATH, timer: 0, escort: true}
            """);

    private static CampaignSnapshot GeneratedEscortSnapshot(RuntimeContent content, bool hunting)
    {
        var initial = HuntingGeneratedWaveSnapshot(content, hunting ? new(1, 1, 1, 1999, 0, 29, 55) : new(1, 1, 1, 1999, 23, 59, 55));
        return initial with
        {
            World = new()
            {
                AlienBases = [new(2, "GENERATED_ESCORT_BASE", "RACE_A", 0.2, 0.1)
                { MinutesSinceLastHuntMissionGeneration = hunting ? 50 : 0 }]
            },
        };
    }
}
