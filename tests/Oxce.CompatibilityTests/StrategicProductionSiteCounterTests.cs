using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicProductionSiteCounterTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ProductionPrecedesDirectSitesAndUfoConversionsSharingCraftMarkers(bool cache, bool conversion)
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-production");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, """
            facilities:
              - {type: SHARED_SITE_WORKSHOP, mapName: LOGISTICS, size: 1, workshops: 2}
            manufacture:
              - name: SHARED_SITE_CRAFT
                category: STR_CRAFT
                time: 1
                producedItems: {SHIP: 1}
            alienDeployments:
              - {type: SHARED_SITE, markerName: SHIP, duration: [2, 2]}
            ufoTrajectories:
              - id: SHARED_SITE_ARRIVAL
                groundTimer: 60
                waypoints: [[1, 3, 0], [1, 0, 0], [1, 3, 0]]
            alienMissions:
              - type: SHARED_SITE_DIRECT
                objective: 3
                spawnZone: 1
                siteType: SHARED_SITE
                waves: [{ufo: SHARED_SITE, count: 1, trajectory: TRAJ_PATROL, timer: 0}]
              - type: SHARED_SITE_CONVERT
                objective: 3
                spawnZone: 1
                siteType: SHARED_SITE
                waves: [{ufo: UFO_SCOUT, count: 1, trajectory: SHARED_SITE_ARRIVAL, timer: 3000, objective: true}]
            """);
        var initial = CreateSnapshot(content, "Shared craft site markers") with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var owner = initial.Bases[0];
        var next = owner.Crafts.Where(craft => craft.RuleId == "SHIP").Max(craft => craft.Id) + 1;
        var mission = conversion ? "SHARED_SITE_CONVERT" : "SHARED_SITE_DIRECT";
        initial = initial.WithNextId("SHIP", next) with
        {
            Bases = [owner with
            {
                Facilities = [.. owner.Facilities, new("SHARED_SITE_WORKSHOP", 5, 5, 0, 0, false, false, false)],
                Productions = [new("SHARED_SITE_CRAFT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
            }],
            World = initial.World with
            {
                Missions = [new(4, mission, "REGION", "RACE_A", conversion ? 1 : 0, 0, 0, conversion ? 1 : 0, 0)],
                Ufos = conversion ?
                [
                    new(9, "UFO_SCOUT", 4, "SHARED_SITE_ARRIVAL", 0, Degrees(8), Degrees(2), UfoStatus.Flying, WorldAltitudes.High)
                    {
                        Shield = 0,
                        MissionWaveNumber = 0,
                        Destination = WorldTargetReference.ForWaypoint(0, new(Degrees(8), Degrees(2))),
                    },
                ] : [],
                MissionSites = [new(next + 1, mission, "SHARED_SITE", "RACE_A", 0.2, 0.1, 7200)],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
        initial = initial with { World = initial.World with { MissionSites = [] } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Contains(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP" && craft.Id == next);
        Assert.Equal(next + 1, Assert.Single(after.World.MissionSites).Id);
        Assert.Equal(next + 2, after.NextIds["SHIP"]);
        campaign = Reload(content, campaign, 17, "produced-craft-site.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.Single(campaign.Capture().World.MissionSites);
    }
}
