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
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void QuietBasePermitsSiteResearchWithNativeActivityAndTimerOrder(bool cache, int mode)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-activity");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-timer");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(cache, 0, Deployment + "\n" + $$$"""
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiteResearchSelfDestructsBaseOnlyAtTheFollowingDailyPass(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(cache, 0, Deployment + "\n  - {type: QUIET_BASE, markerName: QUIET, points: 11, baseSelfDestructCode: CORE}");
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
    public void PossibleDailyOrHuntingGenerationRemainsAtomic(bool hunting)
    {
        var content = Content(false, 0, Deployment + "\n" + (hunting ? """
              - type: GENERATING_BASE
                baseDetectionRange: 100
                baseDetectionChance: 100
                huntMissionMaxFrequency: 0
                huntMissionWeights: {0: {MISSION_SUPPLY: 1}}
            """ : """
              - type: GENERATING_BASE
                genMission: {MISSION_SUPPLY: 1}
                genMissionFreq: 100
                genMissionLimit: 1
            """));
        var snapshot = Snapshot(content);
        snapshot = snapshot with
        {
            Time = hunting ? snapshot.Time : new(1, 1, 1, 1999, 23, 59, 55),
            Bases = [snapshot.Bases[0] with { Research = [] }],
            World = snapshot.World with { AlienBases = [new(2, "GENERATING_BASE", "RACE_A", 0.2, 0.1)] },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)),
            "Mission site research requires alien-base generation phase forecasts.");
    }
}
