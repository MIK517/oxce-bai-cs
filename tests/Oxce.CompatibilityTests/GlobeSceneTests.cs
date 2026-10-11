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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RadarsFlightsAndLabelsFollowTheGlobeDrawPasses(int enemyRadars)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], extraRules: $"""
            drawEnemyRadarCircles: {enemyRadars}
            extraGlobeLabels:
              - type: STR_OCEAN_LABEL
                labelLon: 20
                labelLat: 5
                zoomLevel: 4
                labelColor: 7
            """);
        var rules = content.RuntimeRules;
        var world = new CampaignWorldOverview(
        [
            new(WorldTargetKind.Ufo, 1, "UFO_SCOUT", "HK", 0.3, 0)
            {
                Detected = true, HunterKiller = true, HyperDetected = false, RadarRange = 300,
                Hunting = true, Destination = new(0.31, 0.01),
            },
            new(WorldTargetKind.Ufo, 2, "UFO_SCOUT", "Scout", 0.4, 0) { Detected = true, RadarRange = 300 },
            new(WorldTargetKind.AlienBase, 3, "ALIEN_BASE_DEPLOY", "Base", 0.5, 0) { Detected = true },
        ], 0, 0)
        {
            Bases = [new(1, "Alpha", 0.1, 0.1) { RadarRange = 1500 }, new(2, "Quiet", 0.2, 0.2)],
            // Globe::drawDetail visits the campaign's countries and regions, not every rule.
            CountryIds = [.. rules.Countries.Rules.Select(rule => rule.Id).Take(1), "MISSING_COUNTRY"],
            RegionIds = ["REGION"],
            Crafts =
            [
                new(1, "SHIELD_CRAFT", 1, "Out", "STR_OUT", 0.12, 0.1, 1,
                    WorldTargetReference.ForWaypoint(9, new(0.15, 0.15)), null, null) { RadarRange = 600 },
                new(1, "SHIELD_CRAFT", 2, "Home", "STR_READY", 0.1, 0.1, 1, null, null, null) { RadarRange = 600 },
            ],
        };

        var scene = GlobeScene.Build(world, rules, text => "[" + text + "]");

        static double Nautical(int miles) => miles / 60.0 * Math.PI / 180;
        GlobeCircle[] expected = enemyRadars == 1
            ? [new(0.1, 0.1, Nautical(1500), 48), new(0.12, 0.1, Nautical(600), 24), new(0.5, 0, Nautical(600), 24)]
            : [new(0.1, 0.1, Nautical(1500), 48), new(0.12, 0.1, Nautical(600), 24), new(0.3, 0, Nautical(300), 24),
                new(0.5, 0, Nautical(600), 24)];
        Assert.Equal(expected, scene.Radars);
        Assert.Equal([new GlobePath(0.12, 0.1, 0.15, 0.15), new GlobePath(0.3, 0, 0.31, 0.01)], scene.Flights);

        var labels = scene.Labels;
        Assert.Equal(Math.Min(1, rules.Countries.Count), labels.Count(label => label.MinimumZoom == 2 && label.BoxWidth == 150));
        var extra = Assert.Single(labels, label => label.Text == "[STR_OCEAN_LABEL]");
        Assert.Equal(((byte)7, 4, 120), (extra.Color, extra.MinimumZoom, extra.BoxWidth));
        var cities = labels.Where(label => label.City).Select(label => label.Text).ToArray();
        Assert.Equal(["[CITY_ALPHA]", "[CITY_BETA]", "[CITY_WRAPPED]", "[CITY_SPAWN]", "[LANDING_POINT]", "[DEPARTURE_POINT]"], cities);
        Assert.All(labels.Where(label => label.City), label =>
            Assert.Equal((rules.Campaign.Globe.Colors.CityLabel, 3, 100, 2), (label.Color, label.MinimumZoom, label.BoxWidth, label.OffsetY)));
        var bases = labels.Where(label => !label.City && label.MinimumZoom == 3).ToArray();
        Assert.Equal(["Alpha", "Quiet"], bases.Select(label => label.Text));
        Assert.All(bases, label => Assert.Equal(rules.Campaign.Globe.Colors.BaseLabel, label.Color));
    }
}
