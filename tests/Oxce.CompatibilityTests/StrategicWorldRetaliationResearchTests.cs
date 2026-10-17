using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldRetaliationResearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedRetaliationReceivesOnlyGrantsAfterItsOwnCreation(bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-retaliation-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        for (var targetIndex = 0; targetIndex <= 1; targetIndex++)
        {
            for (var replaceIndex = 0; replaceIndex <= 1; replaceIndex++)
            {
                var targeted = targetIndex == 1;
                var replace = replaceIndex == 1;
                var content = StrategicGeneratedRetaliationTests.Content(hunting, replace,
                    noOriginalUfo: targeted && replace, immediateChain: targeted && replace, interrupt: "CORE");
                var initial = WithSite(StrategicGeneratedRetaliationTests.Initial(content, hunting, targeted));
                var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(17));
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
                AdvanceUnblocked(campaign, 1);
                AdvanceUnblocked(control, 1);
                var after = campaign.Capture();
                var mission = Assert.Single(after.World.Missions);
                Assert.Equal(!hunting, mission.Interrupted);
                Assert.Equal(targeted && replace ? 2 : 1, mission.LiveUfos);
                Assert.Equal(mission.LiveUfos, after.World.Ufos.Count);
                Assert.All(after.World.Ufos, ufo =>
                {
                    Assert.Equal(mission.Id, ufo.MissionId);
                    Assert.Equal(targeted && replace ? "GENERATED_RETAL_OVERRIDE" : "GENERATED_RETAL_UFO", ufo.RuleId);
                    Assert.Equal(targeted ? RuntimeUfoTrajectoryRule.RetaliationAssaultRun : "TRAJ_SPAWN_FIXED", ufo.TrajectoryId);
                });
                Assert.Empty(after.World.MissionSites);
                Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
                Assert.Equal(control.Capture().RandomState, after.RandomState);
                Assert.Equivalent(control.Capture().World.Ufos, after.World.Ufos, strict: true);
                for (var pass = 0; pass < 3; pass++)
                {
                    campaign = Reload(content, campaign, 17, "generated-retaliation-research.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstantRetaliationWithoutATargetCleansUpBeforeLaterResearch(bool hunting)
    {
        var content = StrategicGeneratedRetaliationTests.Content(hunting, false, instant: true, immediateChain: true, interrupt: "MISSING_RESEARCH");
        var initial = WithSite(StrategicGeneratedRetaliationTests.Initial(content, hunting, false));
        initial = initial with { Bases = initial.Bases.Select(owner => owner with { Longitude = 2 }).ToArray() };
        // Moving the player base outside the source region must not recall the
        // nearby patrol before the hunting caller; keep its return fuel ample.
        if (hunting) initial = initial.WithCraft("SHIP", craft => craft with
        { Logistics = craft.Logistics! with { Fuel = 1000 } });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.Ufos);
        Assert.Empty(after.World.MissionSites);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        if (!hunting) Assert.Empty(after.World.Missions);
        else Assert.True(Assert.Single(after.World.Missions).Interrupted);
        Assert.Equal(hunting ? 0 : 1, hunting ? Assert.Single(after.World.AlienBases).MinutesSinceLastHuntMissionGeneration :
            Assert.Single(after.World.AlienBases).GenMissionCount);
        Assert.Equivalent(after, Reload(content, campaign, 17, "instant-no-target-research.sav").Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiteResearchInterruptsAGeneratedInstantRetaliationOnlyBeforeItsAssault(bool hunting)
    {
        var content = StrategicGeneratedRetaliationTests.Content(hunting, false, instant: true, interrupt: "CORE");
        var campaign = CampaignState.Restore(WithSite(StrategicGeneratedRetaliationTests.Initial(content, hunting, true)), content,
            new SplitMix64RandomSource(17));
        var events = campaign.Execute(new AdvanceCampaignTime(1)).Events;
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Equal(UfoStatus.Destroyed, ufo.Status);
        // The daily research interrupts the mission born the same day before its ship arrives; the
        // half-hour hunting birth attacks first.
        Assert.Equal(hunting ? 1 : 0, events.OfType<DeploymentRequested>().Count());
        Assert.Equal(hunting ? 1 : 0, ufo.TrajectoryPoint);
        if (hunting) return;
        content = StrategicGeneratedRetaliationTests.Content(false, false, interrupt: "MISSING_RESEARCH");
        AssertTimeBlocked(CampaignState.Restore(WithSite(StrategicGeneratedRetaliationTests.Initial(content, false, false)), content, new SplitMix64RandomSource(17)),
            "Alien mission interruption references undefined research.");
    }

    private static CampaignSnapshot WithSite(CampaignSnapshot initial) => initial with
    {
        CompletedResearch = ["UNLOCK"],
        World = initial.World with
        {
            MissionSites = [new(1, "MISSION_SITE", "DEPLOY_DIARY", "RACE_A", 0.2, 0.1, 0) { Detected = true }],
        },
    };
}
