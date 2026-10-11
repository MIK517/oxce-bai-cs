using Oxce.Engine.Geoscape;
using Oxce.Gameplay.Campaigns.World;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class GlobeViewTests
{
    // The 4d frame doubles the reference 256x200 globe surface.
    private static GlobeView View() => new(512, 400, pixelScale: 2);

    [Fact]
    public void ProjectionFollowsPolarToCartWithSouthDown()
    {
        var view = View();
        Assert.Equal((256, 200), (view.CenterX, view.CenterY));
        Assert.Equal(180, view.Radius, 12);
        Assert.Equal((256, 200), view.Project(0, 0));
        Assert.Equal((436, 200), view.Project(Math.PI / 2, 0));
        Assert.Equal((76, 200), view.Project(-Math.PI / 2, 0));
        // 180 * sin(45°) = 127.28: floored, and positive latitude (south) is down.
        Assert.Equal((256, 327), view.Project(0, Math.PI / 4));
        Assert.Equal((256, 72), view.Project(0, -Math.PI / 4));
        // floor rounds towards negative infinity, not towards zero.
        Assert.Equal(256 + (int)Math.Floor(-180 * Math.Sin(0.1)), view.Project(-0.1, 0).X);
    }

    [Fact]
    public void UnprojectionInvertsTheProjectionAndRejectsPointsOffTheGlobe()
    {
        var view = View();
        Assert.True(view.TryUnproject(256, 200, out var longitude, out var latitude));
        Assert.Equal((0.0, 0.0), (longitude, latitude));
        Assert.True(view.TryUnproject(436, 200, out longitude, out latitude));
        Assert.Equal(Math.PI / 2, longitude, 12);
        Assert.Equal(0, latitude, 12);
        // West of the centre is normalised into [0, 2π).
        Assert.True(view.TryUnproject(76, 200, out longitude, out _));
        Assert.Equal(3 * Math.PI / 2, longitude, 12);
        Assert.False(view.TryUnproject(437, 200, out _, out _));
        Assert.False(view.TryUnproject(0, 0, out _, out _));

        view.Center(1.0, -0.5);
        view.SetZoom(2);
        foreach (var (x, y) in new[] { (300, 250), (100, 120), (256, 50) })
        {
            Assert.True(view.TryUnproject(x, y, out longitude, out latitude));
            var (px, py) = view.ProjectPrecise(longitude, latitude);
            Assert.Equal(x, px, 9);
            Assert.Equal(y, py, 9);
        }
    }

    [Fact]
    public void BackHemisphereTargetsAreNeitherVisibleNorNear()
    {
        var view = View();
        Assert.True(view.IsBack(Math.PI, 0));
        Assert.False(view.IsBack(Math.PI / 2, 0));
        Assert.False(view.IsBack(0, Math.PI / 2));
        Assert.False(view.IsNear(Math.PI, 0, 256, 200));
    }

    [Theory]
    [InlineData(1, 5, true)]
    [InlineData(1, 6, false)]
    [InlineData(2, 10, true)]
    [InlineData(2, 11, false)]
    public void NearRadiusIsTwentyFiveSquaredReferencePixels(int scale, int distance, bool near)
    {
        var view = new GlobeView(256 * scale, 200 * scale, scale);
        var (x, y) = view.Project(0, 0);
        Assert.Equal(near, view.IsNear(0, 0, x + distance, y));
        Assert.Equal(near, view.IsNear(0, 0, x, y - distance));
    }

    [Fact]
    public void ZoomRadiiTexturesAndRotationFollowTheReference()
    {
        var view = View();
        double[] radii = [180, 240, 360, 560, 900, 1440];
        int[] textureOffsets = [26, 26, 13, 13, 0, 0];
        for (var zoom = 0; zoom < GlobeView.ZoomLevels; zoom++)
        {
            view.SetZoom(zoom);
            Assert.Equal(radii[zoom], view.Radius, 9);
            Assert.Equal(textureOffsets[zoom], view.TextureOffset(39));
        }
        Assert.False(view.ZoomIn());
        view.SetZoom(99);
        Assert.Equal(5, view.Zoom);
        view.SetZoom(-3);
        Assert.Equal(0, view.Zoom);
        Assert.False(view.ZoomOut());

        var revision = view.Revision;
        view.Rotate(1, -1);
        Assert.Equal(0.09, view.CenterLongitude, 12);
        Assert.Equal(-0.054, view.CenterLatitude, 12);
        Assert.True(view.Revision > revision);
        view.SetZoom(1);
        view.Rotate(-1, 0, scrollSpeed: 60);
        Assert.Equal(0.09 - 0.1 * 0.5 / 2, view.CenterLongitude, 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => view.Center(double.NaN, 0));
    }

    [Fact]
    public void TargetsFollowGetTargetsOrderAndSkipGroundedCraft()
    {
        var waypoint = new CampaignWorldTarget(WorldTargetKind.Waypoint, 4, WorldTargetReference.WaypointType, "WP", 0.02, 0) { Detected = true };
        var site = new CampaignWorldTarget(WorldTargetKind.MissionSite, 2, "STR_TERROR_SITE", "Site", 0.03, 0) { Detected = true };
        var alienBase = new CampaignWorldTarget(WorldTargetKind.AlienBase, 1, "STR_ALIEN_BASE", "Base", 0.04, 0) { Detected = true };
        var ufo = new CampaignWorldTarget(WorldTargetKind.Ufo, 7, "STR_SMALL_SCOUT", "UFO-7", 0.01, 0)
        {
            Detected = true,
            Reference = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 7, 0.01, 0) { UniqueId = 70 },
        };
        var world = new CampaignWorldOverview([alienBase, site, waypoint, ufo], 1, 0)
        {
            Bases = [new(1, "Alpha", 0, 0), new(2, "Beta", 3, 0)],
            Crafts =
            [
                new(1, "SHIP", 1, "SHIP-1", "STR_READY", 0, 0, 10, null, null, null),
                new(2, "SHIP", 2, "SHIP-2", "STR_OUT", 0.005, 0, 10, WorldTargetReference.ForWaypoint(4, new(0.02, 0)), null, null),
                new(1, "SHIP", 3, "SHIP-3", "STR_OUT", 0.006, 0, 10, null, null, null),
            ],
        };

        var targets = GlobeTargets.Build(world);

        Assert.Equal(["Alpha", "SHIP-3", "Beta", "SHIP-2", "UFO-7", "WP", "Site", "Base"],
            targets.All.Select(target => target.Label));
        var view = View();
        var near = targets.Near(view, 256, 200);
        Assert.Equal(["Alpha", "SHIP-3", "SHIP-2", "UFO-7", "WP", "Site", "Base"], near.Select(target => target.Label));
        var excluded = targets.Near(view, 256, 200, near[1].Reference);
        Assert.DoesNotContain(excluded, target => target.Label == "SHIP-3");
        Assert.DoesNotContain(targets.Visible(view), target => target.Label == "Beta");
        // UFOs are found again by unique id even after their marker id changes.
        Assert.Same(targets.All[4], targets.Find(new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 99, 0, 0) { UniqueId = 70 }));
        Assert.Null(targets.Find(new(WorldTargetKind.Craft, "SHIP", 1, 0, 0)));
    }
}
