using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicResearchDiaryExecutionTests;
using static Oxce.CompatibilityTests.StrategicWorldSiteResearchTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchBaseTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void QuietBasePermitsSiteResearchWithNativeActivityAndTimerOrder(int mode)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-activity");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-timer");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(0, Deployment + "\n" + $$$"""
              - type: QUIET_BASE
                markerName: QUIET
                points: 11
                genMission: {MISSION_SUPPLY: 1}
                genMissionFreq: {{{(mode == 1 ? 100 : 0)}}}
                genMissionLimit: 1
                baseDetectionRange: {{{(mode == 2 ? 100 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: 120
                huntMissionWeights: {0: {MISSION_SUPPLY: 1}}
            """);
        var snapshot = Snapshot(content);
        snapshot = snapshot with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            Bases = [snapshot.Bases[0] with { Research = [] }],
            World = snapshot.World with
            {
                AlienBases = [new(2, "QUIET_BASE", "RACE_A", 0.2, 0.1)
            {
                Discovered = true, GenMissionCount = mode == 1 ? 1 : 0,
            }]
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.MissionSites);
        Assert.Equal(4, after.ResearchDiary.Count);
        Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 18, after.Regions[0].ActivityAlien[^1]);
        var alienBase = Assert.Single(after.World.AlienBases);
        Assert.Equal(mode == 2 ? 10 : 0, alienBase.MinutesSinceLastHuntMissionGeneration);
        Assert.Equal(mode == 1 ? 1 : 0, alienBase.GenMissionCount);
        Assert.Equal(3, after.World.Missions.Count);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "quiet-base-research.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    [Fact]
    public void SiteResearchSelfDestructsBaseOnlyAtTheFollowingDailyPass()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(0, Deployment + "\n  - {type: QUIET_BASE, markerName: QUIET, points: 11, baseSelfDestructCode: CORE}");
        var snapshot = Snapshot(content);
        snapshot = snapshot with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            Bases = [snapshot.Bases[0] with { Research = [] }],
            World = snapshot.World with { AlienBases = [new(2, "QUIET_BASE", "RACE_A", 0.2, 0.1) { Discovered = true }] },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Single(campaign.Capture().World.AlienBases);
        Assert.Contains("CORE", campaign.Capture().CompletedResearch);
        campaign = Reload(content, campaign, 17, "base-before-site-self-destruct.sav");
        for (var hour = 0; hour < 24; hour++) AdvanceUnblocked(campaign, 720);
        var after = campaign.Capture();
        Assert.Empty(after.World.AlienBases);
        Assert.Equal(4, after.ResearchDiary.Count);
        Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 18, after.Regions[0].ActivityAlien[^1]);
        Reload(content, campaign, 17, "base-after-site-self-destruct.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateEscortGenerationComposesWithSiteResearch(bool hunting)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], extraRules:
            "research:\n  - {name: UNLOCK, cost: 1}\n  - {name: CORE, cost: 1}\n" + Deployment + "\n" + (hunting ? """
              - type: GENERATING_BASE
                baseDetectionRange: 100
                baseDetectionChance: 100
                huntMissionMaxFrequency: 0
                huntMissionWeights: {0: {GENERATING_ACTOR: 1}}
            """ : """
              - type: GENERATING_BASE
                genMission: {GENERATING_ACTOR: 1}
                genMissionFreq: 100
                genMissionLimit: 1
            """) + "\nalienMissions:\n  - type: GENERATING_ACTOR\n    waves: [{ufo: UFO_HUNTER, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0, escort: true}]\n");
        var snapshot = Snapshot(content);
        snapshot = snapshot with
        {
            Time = hunting ? snapshot.Time : new(1, 1, 1, 1999, 23, 59, 55),
            Bases = [snapshot.Bases[0] with { Research = [] }],
            World = snapshot.World with { AlienBases = [new(2, "GENERATING_BASE", "RACE_A", 0.2, 0.1)] },
        };
        snapshot = snapshot.WithNextId("ALIEN_MISSIONS", 100);
        if (hunting) snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 0,
                Longitude = 0.2,
                Latitude = 0.1,
                Destination = WorldTargetReference.ForWaypoint(1, new(1, 0.6))
            },
        }) with
        { World = snapshot.World with { Waypoints = [new(1, 1, 0.6)] } };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.True(Assert.Single(after.World.Ufos).Escort);
        Assert.Contains("CORE", after.CompletedResearch);
        Assert.Empty(after.World.MissionSites);
        Assert.Equivalent(after, Reload(content, campaign, 17, "generated-escort-site.sav").Capture(), strict: true);
    }
}
