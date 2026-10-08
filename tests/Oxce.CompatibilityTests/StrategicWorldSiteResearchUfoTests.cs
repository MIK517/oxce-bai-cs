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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExplicitGrantInterruptsArrivalBeforeLandingEvenWhenCoreIsDisabled(bool cache, bool disabled)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(cache, 0, Deployment);
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
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-research-ufo-arrived.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Equal(0, campaign.Capture().World.Missions[0].LiveUfos);
        Reload(content, campaign, 17, "site-research-ufo-deleted.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticResearchDoesNotEnterImmediateExplicitMissionInterruption(bool cache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, """
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
    public void UncertainExplicitInterruptionStopsBeforeChoosingResearchRewardOrMovingUfo()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
              - {name: BONUS, cost: 1}
              - {name: OTHER, cost: 1}
            alienMissions:
              - {type: MISSION_SCOUT, interruptResearch: BONUS}
            """ + "\n" + Deployment);
        AssertTimeBlocked(CampaignState.Restore(Arrival(content), content, new SplitMix64RandomSource(17)),
            "UFO mission interruption depends on uncertain site research outcomes.");
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
