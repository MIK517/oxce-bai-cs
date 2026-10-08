using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldSiteResearchTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchDetectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SafeTransitCommitsRandomInterruptionAndDetectionReadsPreSiteResearch(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-scripts");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, """
            facilities:
              - {type: STORES, radarRange: 5000, radarChance: 100}
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
              - {name: BONUS, cost: 1}
              - {name: OTHER, cost: 1}
            alienMissions:
              - {type: MISSION_SCOUT, interruptResearch: BONUS}
            ufos:
              - type: UFO_RESEARCH
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: |
                    var ptr RuleResearch topic;
                    var int completed;
                    rules.getRuleResearch topic "CORE";
                    geoscape_game.isResearched completed topic;
                    if eq completed 1; return DETECTION_RADAR 100; end;
                    return DETECTION_NONE 0;
            """ + "\n" + Deployment);
        HashSet<bool> outcomes = [];
        for (var seed = 1; seed <= 8; seed++)
        {
            var snapshot = CreateUfoTransitCampaign(content, new(2, 2, 1, 1999, 0, 29, 55), "UFO_RESEARCH", 2200,
                trajectoryId: "TRAJ_AIRBORNE", missionWaveNumber: 0).Capture();
            snapshot = snapshot with
            {
                RandomState = (ulong)seed,
                World = snapshot.World with
                {
                    Ufos = [snapshot.World.Ufos[0] with { Detected = false }],
                    MissionSites = Snapshot(content).World.MissionSites,
                }
            };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource((ulong)seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Contains("CORE", after.CompletedResearch);
            var interrupted = after.CompletedResearch.Contains("BONUS");
            outcomes.Add(interrupted);
            Assert.Equal(interrupted, Assert.Single(after.World.Missions).Interrupted);
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal(UfoStatus.Flying, ufo.Status);
            Assert.False(ufo.Detected);
            Assert.NotEqual(snapshot.World.Ufos[0].Longitude, ufo.Longitude);
            Assert.Equal(2, after.ResearchDiary.Count);
            campaign = Reload(content, campaign, (ulong)seed, "site-random-transit.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 360);
            Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
            Assert.Equal(2, campaign.Capture().ResearchDiary.Count);
            Reload(content, campaign, (ulong)seed, "site-next-detection.sav");
        }
        Assert.Equal(2, outcomes.Count);
    }
}
