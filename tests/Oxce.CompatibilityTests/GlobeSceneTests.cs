using Oxce.Engine.Geoscape;
using Oxce.Gameplay.Campaigns.World;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class GlobeSceneTests
{
    [Fact]
    public void MarkerFramesFollowTargetGetMarkerInDrawMarkersOrder()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        CampaignWorldTarget Target(WorldTargetKind kind, int id, string type, string status = "") =>
            new(kind, id, type, type, 0.1 * id, 0) { Detected = true, Status = status };
        var world = new CampaignWorldOverview(
        [
            Target(WorldTargetKind.Ufo, 1, "UFO_SCAN_TEST", nameof(UfoStatus.Landed)),
            Target(WorldTargetKind.AlienBase, 2, "ALIEN_BASE_DEPLOY"),
            Target(WorldTargetKind.MissionSite, 3, "SITE_DEPLOY"),
            Target(WorldTargetKind.MissionSite, 4, "SITE_DEPLOY_B"),
            Target(WorldTargetKind.Ufo, 5, "UFO_SCOUT", nameof(UfoStatus.Flying)),
            Target(WorldTargetKind.Ufo, 6, "UFO_SCAN_TEST", nameof(UfoStatus.Crashed)),
            Target(WorldTargetKind.Waypoint, 7, WorldTargetReference.WaypointType),
        ], 0, 0)
        {
            Bases = [new(1, "Alpha", 0.5, 0.5), new(2, "Unplaced", 0, 0)],
            Crafts =
            [
                new(1, "SHIELD_CRAFT", 1, "Out", "STR_OUT", 0.2, 0.2, 1, null, null, null),
                new(1, "SHIELD_CRAFT", 2, "Home", "STR_READY", 0.5, 0.5, 1, null, null, null),
            ],
        };

        var scene = GlobeScene.Build(world, content.RuntimeRules);

        // Bases, waypoints, sites, alien bases (none without a marker icon), UFOs, then airborne craft.
        Assert.Equal([0, 6, 8, 5, 3, 2, 4, 1], scene.Markers.Select(marker => marker.Frame));
        Assert.Equal((0.5, 0.5), (scene.Markers[0].Longitude, scene.Markers[0].Latitude));
    }
}
