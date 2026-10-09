using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldConversionResearchTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void SelectedConversionPreservesSiteFollowerAndDormantUfoOutcomes(bool cache, bool respawn, bool units)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(cache, respawn);
        var interruptedContent = Content(cache, respawn, "CORE");
        var continuedContent = Content(cache, respawn, "");
        var interruptedSeen = false;
        var continuedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Snapshot(content, seed, units);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var interrupted = after.CompletedResearch.Contains("BONUS");
            var passEnded = interrupted || !respawn;
            Assert.Equal(interrupted, Assert.Single(after.World.Missions).Interrupted);
            Assert.Equal(interrupted || !respawn ? UfoStatus.Destroyed : UfoStatus.IgnoreMe, after.World.Ufos[0].Status);
            Assert.Equal(!interrupted, result.Events.OfType<MissionSiteDetected>().Any());
            Assert.Equal(interrupted, result.Events.OfType<UfoContactLost>().Any());
            Assert.Equal(passEnded, after.World.Ufos[1].Position == initial.World.Ufos[1].Position);
            var follower = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
            Assert.Equal(passEnded, follower.Longitude == initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Longitude);
            Assert.Equal(interrupted || !respawn && !units ? WorldTargetKind.Ufo : units ? WorldTargetKind.MissionSite : WorldTargetKind.Base,
                follower.Destination!.Kind);
            if (interrupted) Assert.Empty(after.World.MissionSites);
            else
            {
                var site = Assert.Single(after.World.MissionSites);
                Assert.Equal((70, "DEPLOY_ARRIVAL_SITE", 3600), (site.Id, site.DeploymentId, site.SecondsRemaining));
                Assert.Equal(respawn ? initial.World.Ufos[0].UniqueId : 0, site.UfoUniqueId);
                Assert.True(site.Detected);
            }
            var control = CampaignState.Restore(initial, interrupted ? interruptedContent : continuedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++)
                campaign = Reload(content, campaign, seed, "conversion-research-arrival.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(passEnded ? 1 : 2, campaign.Capture().World.Ufos.Count);
            Assert.Equal(passEnded ? 1 : 2, Assert.Single(campaign.Capture().World.Missions).LiveUfos);
            Reload(content, campaign, seed, "conversion-research-followup.sav");
            interruptedSeen |= interrupted;
            continuedSeen |= !interrupted;
        }
        Assert.True(interruptedSeen);
        Assert.True(continuedSeen);
    }

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, bool units = false, bool baseLanding = false)
    {
        var initial = StrategicWorldSiteResearchUfoTests.Arrival(content).WithFacility("RADAR_TEST")
            .WithNextId("ARRIVAL_SITE", 70).WithNextId("ARRIVAL_BASE", 70) with
        { RandomState = seed };
        var first = initial.World.Ufos[0] with { Detected = true };
        var later = first with
        {
            UniqueId = first.UniqueId + 1,
            Id = first.Id + 1,
            TrajectoryId = "TRAJ_AIRBORNE",
            TrajectoryPoint = 0,
            Longitude = 0,
            Latitude = 0,
            Destination = WorldTargetReference.ForWaypoint(0, new(0.6, 0.2)),
        };
        var owner = initial.Bases[0];
        var craftId = owner.Crafts.Single(craft => craft.RuleId == "SHIP").Id;
        initial = initial with
        {
            Bases = [owner with { Soldiers = units ?
                [owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with { CraftType = "SHIP", CraftId = craftId } }] : [] }],
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with
                { RuleId = "MISSION_ARRIVAL_RESEARCH", NextWave = 1, LiveUfos = 2, MissionSiteZoneArea = baseLanding ? -1 : 0 }],
                Ufos = [first, later],
            },
        };
        return initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 6000,
                SpeedRadian = WorldGeometry.RadianSpeed(6000),
                Longitude = owner.Longitude + 0.1,
                Latitude = owner.Latitude + 0.1,
                Vehicles = [],
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, first.Id, first.Longitude, first.Latitude)
                { UniqueId = first.UniqueId },
            },
        });
    }

    internal static RuntimeContent Content(bool cache, bool respawn = false, string interrupt = "BONUS", bool baseLanding = false,
        bool showBase = false, int operation = 0, string sourceCode = "BONUS") => StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
          - {name: BONUS, cost: 1}
          - {name: OTHER, cost: 1}
        alienMissions:
          - type: MISSION_ARRIVAL_RESEARCH
            objective: {{{(baseLanding ? 2 : 3)}}}
            points: 5
            siteType: {{{(baseLanding ? "DEPLOY_ARRIVAL_BASE" : "DEPLOY_ARRIVAL_SITE")}}}
            spawnZone: 5
            interruptResearch: {{{interrupt}}}
            respawnUfoAfterSiteDespawn: {{{(respawn ? "true" : "false")}}}
            showAlienBase: {{{(showBase ? "true" : "false")}}}
            operationType: {{{operation}}}
            operationBaseType: {{{(operation == 0 ? "" : "OPTIONAL_OPERATION_BASE")}}}
            waves: [{ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_LANDING, timer: 9000, objective: true, objectiveOnTheLandingSite: true}]
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + "\n" + $$$"""
          - {type: DEPLOY_ARRIVAL_SITE, markerName: ARRIVAL_SITE, duration: [1, 1]}
          - {type: DEPLOY_ARRIVAL_BASE, markerName: ARRIVAL_BASE, duration: [1, 1]}
        {{{(operation == 0 ? "" : "  - {type: OPTIONAL_OPERATION_BASE, markerName: OPTIONAL_SOURCE, points: 7, baseSelfDestructCode: " + sourceCode + "}")}}}
        """);
}
