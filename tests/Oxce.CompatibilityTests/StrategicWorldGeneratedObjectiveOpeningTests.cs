using Oxce.Core.Random;
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
    [InlineData(false, 1, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 2, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    [InlineData(false, 2, true)]
    [InlineData(true, 2, true)]
    public void GeneratedObjectiveOpeningPreservesPendingCompletion(bool cache, int objective, bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        var content = GeneratedObjectiveOpeningContent(cache, objective, hunting);
        var initial = GeneratedObjectiveOpeningSnapshot(content, hunting);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)),
            41, "generated-objective-prefix-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var mission = Assert.Single(after.World.Missions);
        var ufo = Assert.Single(after.World.Ufos);
        Assert.Equal((1, 1), (mission.NextWave, mission.LiveUfos));
        Assert.Equal(mission.Id, ufo.MissionId);
        Assert.Equal(0, ufo.MissionWaveNumber);
        Assert.Equal(hunting ? 0 : 3, after.Regions[0].ActivityAlien[^1] - initial.Regions[0].ActivityAlien[^1]);
        Assert.Single(after.World.AlienBases);
        Assert.All(after.Countries, country => Assert.False(country.NewPact));
        campaign = Reload(content, campaign, 41, "generated-objective-prefix-born.sav");
        for (var day = 0; day < 3; day++)
        {
            AdvanceUnblocked(campaign, 17_280);
            campaign = Reload(content, campaign, 41, $"generated-objective-prefix-day-{day}.sav");
        }
        after = campaign.Capture();
        Assert.Empty(after.World.Ufos);
        Assert.Equal((1, 0), (Assert.Single(after.World.Missions).NextWave, after.World.Missions[0].LiveUfos));
        Assert.Single(after.World.AlienBases);
        Assert.All(after.Countries, country => Assert.False(country.NewPact));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void DailyGeneratedFinalBaseWaveCreatesBaseBeforeUfoLifecycle(bool cache, int groundTimer)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = GeneratedObjectiveOpeningContent(cache, 2, groundTimer: groundTimer, prefix: false);
        var initial = GeneratedObjectiveOpeningSnapshot(content).WithNextId("STR_ALIEN_BASE", 7);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)),
            41, "generated-final-base-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var born = Assert.Single(after.World.AlienBases, alienBase => alienBase.Id == 7);
        Assert.Equal("OBJECTIVE_OPENING_DESTINATION", born.DeploymentId);
        Assert.Equal("RACE_A", born.Race);
        Assert.Equal(8, after.NextIds["STR_ALIEN_BASE"]);
        Assert.Equal(UfoStatus.Flying, Assert.Single(after.World.Ufos).Status);
        Assert.Equal((1, 1), (Assert.Single(after.World.Missions).NextWave, after.World.Missions[0].LiveUfos));
        var points = groundTimer == 0 ? 10 : 13; // Base completion plus creation-stage UFO activity; no base-mission takeoff award.
        Assert.Equal(points, after.Regions[0].ActivityAlien[^1] - initial.Regions[0].ActivityAlien[^1]);
        campaign = Reload(content, campaign, 41, "generated-final-base-born.sav");
        for (var day = 0; day < 3; day++)
        {
            AdvanceUnblocked(campaign, 17_280);
            campaign = Reload(content, campaign, 41, $"generated-final-base-day-{day}.sav");
        }
        Assert.Equal(2, campaign.Capture().World.AlienBases.Count);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Empty(campaign.Capture().World.Missions);
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_ALIEN_BASE", int.MaxValue), content,
            new SplitMix64RandomSource(41)), "Base objective IDs are exhausted or collide.");
        var atCapacity = initial with
        { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - points] }, .. initial.Regions.Skip(1)] };
        campaign = CampaignState.Restore(atCapacity, content, new SplitMix64RandomSource(41));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(int.MaxValue, campaign.Capture().Regions[0].ActivityAlien[^1]);
        Reload(content, campaign, 41, "generated-final-base-score-capacity.sav");
        AssertTimeBlocked(CampaignState.Restore(atCapacity with
        { Regions = [atCapacity.Regions[0] with { ActivityAlien = [int.MaxValue - points + 1] }, .. atCapacity.Regions.Skip(1)] }, content,
            new SplitMix64RandomSource(41)), "UFO alien activity exceeds the supported range.");
    }

    private static RuntimeContent GeneratedObjectiveOpeningContent(bool cache, int objective, bool hunting = false,
        int groundTimer = 0, bool prefix = true) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
            alienDeployments:
              - type: OBJECTIVE_OPENING_SOURCE
                alienBase: true
                markerName: STR_ALIEN_BASE
                genMission: {OBJECTIVE_OPENING_MISSION: 1}
                genMissionFreq: {{{(hunting ? 0 : 100)}}}
                genMissionLimit: 1
                baseDetectionRange: {{{(hunting ? 100 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: 5400
                huntMissionWeights: {0: {OBJECTIVE_OPENING_MISSION: 1}}
              - type: OBJECTIVE_OPENING_DESTINATION
                alienBase: true
                markerName: STR_ALIEN_BASE
            alienMissions:
              - type: OBJECTIVE_OPENING_MISSION
                objective: {{{objective}}}
                points: 7
                spawnZone: 1
                siteType: OBJECTIVE_OPENING_DESTINATION
                regionWeights: {0: {REGION: 1}}
                waves:
                  - {ufo: UFO_SCOUT, count: 1, trajectory: {{{(groundTimer == 0 ? "TRAJ_SPAWN_FIXED" : "OBJECTIVE_OPENING_GROUND")}}}, timer: 0}
                  {{{(prefix ? "- {ufo: UNKNOWN_OBJECTIVE_FINAL, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 30000}" : "")}}}
            ufoTrajectories:
              - id: OBJECTIVE_OPENING_GROUND
                groundTimer: {{{groundTimer}}}
                waypoints: [[4, 0, 100], [5, 2, 80], [0, 3, 50]]
            """);

    private static CampaignSnapshot GeneratedObjectiveOpeningSnapshot(RuntimeContent content, bool hunting = false) =>
        (hunting ? HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 0, 29, 55)) : GeneratedWaveSnapshot(content, 41)) with
        {
            World = new()
            {
                AlienBases = [new(2, "OBJECTIVE_OPENING_SOURCE", "RACE_A", 0.2, 0.1)
                { MinutesSinceLastHuntMissionGeneration = 5390 }],
            },
        };
}
