using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicResearchDiaryExecutionTests;
using static Oxce.CompatibilityTests.StrategicWorldSiteResearchTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchUfoTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitGrantInterruptsArrivalBeforeLandingEvenWhenCoreIsDisabled(bool disabled)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(0, Deployment);
        var snapshot = Arrival(content) with { ResearchRuleStatus = disabled ? new Dictionary<string, int> { ["CORE"] = 2 } : new Dictionary<string, int>() };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        campaign = Reload(content, campaign, 17, "site-research-ufo-before.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Empty(result.Events.OfType<UfoLanded>());
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        Assert.Equal(UfoStatus.Destroyed, ufo.Status);
        Assert.Equal(0, ufo.LandId);
        Assert.Equal(1, Assert.Single(after.World.Missions).LiveUfos);
        Assert.True(after.World.Missions[0].Interrupted);
        Assert.Empty(after.World.MissionSites);
        Assert.Equal(disabled ? 3 : 4, after.ResearchDiary.Count);
        campaign = ReloadRepeatedly(content, campaign, 17, "site-research-ufo-arrived.sav", 3, after);
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Equal(0, campaign.Capture().World.Missions[0].LiveUfos);
        Reload(content, campaign, 17, "site-research-ufo-deleted.sav");
    }

    [Fact]
    public void AutomaticResearchDoesNotEnterImmediateExplicitMissionInterruption()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1}
              - {name: AUTO, cost: 0, dependencies: [CORE]}
            alienMissions:
              - {type: MISSION_SCOUT, interruptResearch: AUTO}
            """ + "\n" + Deployment);
        var snapshot = Arrival(content);
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Contains("AUTO", after.CompletedResearch);
        Assert.False(after.World.Missions[0].Interrupted);
        Assert.Equal(UfoStatus.Landed, Assert.Single(after.World.Ufos).Status);
        Assert.Equal(["CORE", "AUTO"], after.ResearchDiary.Select(entry => entry.RuleId));
        Reload(content, campaign, 17, "automatic-research-ufo-landing.sav");
    }

    [Fact]
    public void PossibleUninterruptedLandingStillRejectsAnInvalidGroundTimer()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
              - {name: BONUS, cost: 1}
              - {name: OTHER, cost: 1}
            alienMissions:
              - {type: MISSION_SCOUT, interruptResearch: BONUS}
            """ + "\n" + Deployment);
        var initial = Arrival(content);
        initial = initial with
        {
            World = initial.World with
            { Ufos = [initial.World.Ufos[0] with { TrajectoryId = "TRAJ_LANDING_ZERO", TrajectoryPoint = 0 }] }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO waypoint arrival requires mission simulation.");
    }

    [Fact]
    public void EarlierDailyInterruptionSurvivesUncertainSiteDisablingAndReenabling()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, disables: [BONUS], reenables: [BONUS], getOneFree: [BONUS, OTHER]}
              - {name: BONUS, cost: 1}
              - {name: OTHER, cost: 1}
            alienMissions:
              - {type: MISSION_SCOUT, interruptResearch: BONUS}
            """ + "\n" + Deployment);
        var snapshot = Arrival(content) with { Time = new(1, 1, 1, 1999, 23, 59, 55), CompletedResearch = ["BONUS"] };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.True(Assert.Single(after.World.Missions).Interrupted);
        Assert.Equal(UfoStatus.Destroyed, Assert.Single(after.World.Ufos).Status);
        Assert.Equal(2, after.ResearchDiary.Count);
        Assert.Empty(after.World.MissionSites);
        Reload(content, campaign, 17, "daily-interruption-before-site-reward.sav");
    }

    internal static CampaignSnapshot Arrival(Oxce.Mods.Rulesets.Content.RuntimeContent content)
    {
        var snapshot = CreateUfoTransitCampaign(content, new(2, 2, 1, 1999, 0, 29, 55), "UFO_SCOUT", 2200,
            trajectoryId: "TRAJ_LANDING", missionWaveNumber: 0).Capture();
        var ufo = snapshot.World.Ufos[0] with
        {
            TrajectoryPoint = 1,
            Longitude = Degrees(8),
            Latitude = Degrees(2),
            Destination = WorldTargetReference.ForWaypoint(0, new(Degrees(8), Degrees(2))),
        };
        return snapshot with { World = snapshot.World with { Ufos = [ufo], MissionSites = Snapshot(content).World.MissionSites } };
    }
}
