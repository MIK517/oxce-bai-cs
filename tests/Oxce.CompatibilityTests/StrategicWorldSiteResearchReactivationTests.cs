using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicResearchDiaryExecutionTests;
using static Oxce.CompatibilityTests.StrategicWorldSiteResearchTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchReactivationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiteGrantPrecedesReactivationAndLaterInterruptedArrival(bool arrival)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(0, Deployment);
        var snapshot = Linked(content, arrival);
        var original = snapshot.World.Ufos[0];
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        campaign = Reload(content, campaign, 17, "research-dormant-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.MissionSites);
        Assert.True(Assert.Single(after.World.Missions).Interrupted);
        Assert.Equal(4, after.ResearchDiary.Count);
        var ufo = Assert.Single(after.World.Ufos);
        Assert.Equal(arrival ? UfoStatus.Destroyed : UfoStatus.Flying, ufo.Status);
        Assert.Equal(77, ufo.SecondsRemaining);
        Assert.Equal(40, ufo.LandId);
        Assert.Equal(arrival ? -1 : 15, ufo.Shield);
        if (!arrival)
        {
            Assert.Equal(1100, ufo.Speed);
            Assert.Equal(WorldAltitudes.VeryLow, ufo.Altitude);
            Assert.True(ufo.Detected);
            Assert.Equal(WorldGeometry.Move(original.Position, original.Destination!.Position, WorldGeometry.RadianSpeed(1100)), ufo.Position);
        }
        var points = content.RuntimeRules.AlienMissions[content.RuntimeRules.AlienMissions.GetRequired("MISSION_SCOUT")].Value.Points;
        Assert.Equal(snapshot.Regions[0].ActivityAlien[^1] + 7 + Math.Max(0, points), after.Regions[0].ActivityAlien[^1]);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "research-dormant-after.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
        if (arrival)
        {
            AdvanceUnblocked(campaign, 1);
            Assert.Empty(campaign.Capture().World.Ufos);
            Assert.Equal(0, campaign.Capture().World.Missions[0].LiveUfos);
        }
    }

    [Fact]
    public void CombinedSiteAndLiftScoreOverflowStopsBeforeResearchAndReactivation()
    {
        var content = Content(0, Deployment);
        var snapshot = Linked(content, false);
        snapshot = snapshot with { Regions = [snapshot.Regions[0] with { ActivityAlien = [int.MaxValue - 8] }] };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)),
            "UFO alien activity exceeds the supported range.");
    }

    [Fact]
    public void UnconditionalPayloadStillBlocksDormantSiteBeforeItsResearchOrLift()
    {
        var content = Content(0, Deployment.Replace("despawnPenalty: 7", "despawnPenalty: 7\n    despawnEvents: {PAYLOAD: 1}", StringComparison.Ordinal) +
            "\nevents:\n  - {name: PAYLOAD}\n");
        AssertEventTickRuns(CampaignState.Restore(Linked(content, false), content, new SplitMix64RandomSource(17)), popped: ["PAYLOAD"]);
    }

    internal static CampaignSnapshot Linked(RuntimeContent content, bool arrival)
    {
        var snapshot = StrategicWorldSiteResearchUfoTests.Arrival(content);
        var original = snapshot.World.Ufos[0];
        var ufo = original with
        {
            RuleId = "UFO_SHIELD_TEST",
            Status = UfoStatus.IgnoreMe,
            Altitude = WorldAltitudes.Ground,
            Shield = -1,
            SecondsRemaining = 77,
            LandId = 40,
            Detected = true,
            TrajectoryPoint = 2,
            Speed = 0,
            Destination = WorldTargetReference.ForWaypoint(0, arrival ? original.Position : new(Degrees(8.1), Degrees(2))),
        };
        return snapshot with
        {
            World = snapshot.World with
            {
                Ufos = [ufo],
                Missions = [snapshot.World.Missions[0] with { Race = "RACE_B" }],
                MissionSites = [snapshot.World.MissionSites[0] with { UfoUniqueId = ufo.UniqueId, Longitude = ufo.Longitude, Latitude = ufo.Latitude }],
            }
        };
    }
}
