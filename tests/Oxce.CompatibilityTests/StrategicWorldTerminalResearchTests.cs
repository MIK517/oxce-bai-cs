using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldTerminalResearchTests
{
    [Fact]
    public void TerminalArrivalHasTheSameCleanupForBothExplicitResearchOutcomes()
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content();
        var controlContent = Content(interrupt: "");
        var interruptedSeen = false;
        var uninterruptedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = StrategicWorldSiteResearchUfoTests.Arrival(content) with { RandomState = seed };
            var trajectory = content.RuntimeRules.UfoTrajectories[content.RuntimeRules.UfoTrajectories.GetRequired(initial.World.Ufos[0].TrajectoryId)].Value;
            initial = initial with
            {
                World = initial.World with
                { Ufos = [initial.World.Ufos[0] with { TrajectoryPoint = trajectory.Waypoints.Count - 1 }] }
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var control = CampaignState.Restore(initial, controlContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            var after = campaign.Capture();
            var mission = Assert.Single(after.World.Missions);
            Assert.Equal(after.CompletedResearch.Contains("BONUS"), mission.Interrupted);
            Assert.Equal(UfoStatus.Destroyed, Assert.Single(after.World.Ufos).Status);
            Assert.Equal(1, mission.LiveUfos);
            Assert.Empty(after.World.MissionSites);
            Assert.Equal(control.Capture().RandomState, after.RandomState);
            Assert.Equivalent(control.Capture().World.Ufos, after.World.Ufos, strict: true);
            interruptedSeen |= mission.Interrupted;
            uninterruptedSeen |= !mission.Interrupted;
            campaign = ReloadRepeatedly(content, campaign, seed, "terminal-research-arrival.sav", 3, after);
            AdvanceUnblocked(campaign, 1);
            Assert.Empty(campaign.Capture().World.Ufos);
            Assert.All(campaign.Capture().World.Missions, retained => Assert.Equal(0, retained.LiveUfos));
            Reload(content, campaign, seed, "terminal-research-cleanup.sav");
        }
        Assert.True(interruptedSeen);
        Assert.True(uninterruptedSeen);
    }

    [Fact]
    public void PossibleConversionStillRequiresMarkerCapacityBeforeResearchSelection()
    {
        var content = Content();
        var rule = content.RuntimeRules.AlienMissions[content.RuntimeRules.AlienMissions.GetRequired("MISSION_CONVERSION")].Value;
        Assert.Equal(5, rule.SpawnZone);
        Assert.True(Assert.Single(rule.Waves).Objective);
        var initial = StrategicWorldSiteResearchUfoTests.Arrival(content).WithNextId("STR_TERROR_SITE", int.MaxValue);
        initial = initial with
        {
            World = initial.World with
            { Missions = [initial.World.Missions[0] with { RuleId = "MISSION_CONVERSION", MissionSiteZoneArea = 0 }] }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    private static RuntimeContent Content(string interrupt = "BONUS") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
          - {name: BONUS, cost: 1}
          - {name: OTHER, cost: 1}
        alienMissions:
          - type: MISSION_SCOUT
            interruptResearch: {{{interrupt}}}
          - type: MISSION_CONVERSION
            interruptResearch: {{{interrupt}}}
            spawnZone: 5
            waves:
              - {ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_LANDING, timer: 9000, objective: true}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment);
}
