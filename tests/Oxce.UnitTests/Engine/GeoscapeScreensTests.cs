using Oxce.Engine.Geoscape;
using Oxce.Gameplay.Campaigns.World;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class GeoscapeScreensTests
{
    private static readonly Dictionary<string, string> Strings = new(StringComparer.Ordinal)
    {
        ["STR_ETA"] = "ETA {0}",
        ["STR_DAY_SHORT"] = "{0}d",
        ["STR_HOUR_SHORT"] = "{0}h",
        ["STR_TARGET"] = "Target: {0}",
    };

    private static string Tr(string key) => Strings.GetValueOrDefault(key, key);

    [Fact]
    public void TheEtaFollowsTheOptionAndTheReferenceRounding()
    {
        var leader = Craft("SHIP", 1, speedMaximum: 100);
        var escort = Craft("ESCORT", 2, speedMaximum: 100) with { Speed = 50 };
        var world = World([leader, escort]);
        // One radian is 3437.7 nautical miles: (3437 + 50) / 100 = 34 hours.
        var waypoint = new GeoscapeDestination(null, new WorldPosition(1.0, 0), "STR_WAY_POINT");
        var key = GeoscapeScreens.Key(leader);

        Assert.DoesNotContain("ETA 1d/10h", GeoscapeScreens.Confirm(world, [key], waypoint, true, Tr).Lines);
        Assert.Contains("ETA 1d/10h", GeoscapeScreens.Confirm(world, [key], waypoint, true, Tr, etaMode: 1).Lines);
        var moving = new GeoscapeDestination(new WorldTargetReference(WorldTargetKind.Craft, "ESCORT", 2, 1.0, 0), null, "E");
        Assert.Empty(GeoscapeScreens.Confirm(world, [key], moving, true, Tr, etaMode: 1).Lines);
        Assert.Equal(["ETA 1d/10h"], GeoscapeScreens.Confirm(world, [key], moving, true, Tr, etaMode: 2).Lines);
    }

    [Fact]
    public void TheWingFollowsItsLeaderUnlessItChasesAFlyingUfo()
    {
        var flying = Ufo(1, "STR_HIGH_UP");
        var landed = Ufo(2, "STR_GROUND");
        var world = World([]) with { Targets = [flying, landed] };
        Assert.False(GeoscapeScreens.DefaultFollowLeader(world, flying.Reference));
        Assert.True(GeoscapeScreens.DefaultFollowLeader(world, landed.Reference));
        Assert.True(GeoscapeScreens.DefaultFollowLeader(world, null));
    }

    [Fact]
    public void InterceptListsBaseCraftWithTheirStatusAndOffersTheBase()
    {
        var home = Craft("SHIP", 1, speedMaximum: 100) with
        {
            Status = "STR_OUT",
            Destination = new WorldTargetReference(WorldTargetKind.Base, WorldTargetReference.BaseType, 7, 0, 0),
        };
        var away = Craft("SHIP", 2, speedMaximum: 100) with
        {
            Status = "STR_OUT",
            Destination = new WorldTargetReference(WorldTargetKind.Base, WorldTargetReference.BaseType, 8, 0, 0),
        };
        var other = Craft("SHIP", 3, speedMaximum: 100) with { BaseId = 8 };
        var world = World([home, away, other]);

        var list = GeoscapeScreens.Intercept(world, [], null, 0, Tr, baseId: 7);
        var rows = list.Actions.Where(action => action.Intent is InterceptRowIntent).Select(action => action.Label).ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Contains("STR_RETURNING", rows[0], StringComparison.Ordinal);
        // InterceptState shows another base as a destination by the craft status.
        Assert.Contains("STR_OUT", rows[1], StringComparison.Ordinal);
        Assert.Equal(7, Assert.IsType<BaseIntent>(Assert.Single(list.Actions, action => action.Intent is BaseIntent).Intent).BaseId);
        Assert.DoesNotContain(GeoscapeScreens.Intercept(world, [], null, 0, Tr).Actions, action => action.Intent is BaseIntent);
    }

    [Fact]
    public void UnnamedCraftUseTheCraftNamePattern()
    {
        Assert.Equal("SHIP-4", GeoscapeScreens.DefaultCraftName("SHIP", 4, Tr));
        Assert.Equal("SHIP #4", GeoscapeScreens.DefaultCraftName("SHIP", 4,
            key => key == "STR_CRAFTNAME" ? "{0} #{1}" : key));
        Assert.Throws<InvalidOperationException>(() => new GeoscapeDestination(null, null, "").Position);
    }

    private static CampaignWorldCraft Craft(string type, int id, int speedMaximum) =>
        new(7, type, id, "", "STR_READY", 0, 0, 50, null, null, null) { SpeedMaximum = speedMaximum, FuelMaximum = 100 };

    private static CampaignWorldTarget Ufo(int uniqueId, string altitude) =>
        new(WorldTargetKind.Ufo, uniqueId, "STR_UFO", "UFO-" + uniqueId, 0.5, 0.5)
        {
            Altitude = altitude,
            Reference = new WorldTargetReference(WorldTargetKind.Ufo, "STR_UFO", uniqueId, 0.5, 0.5) { UniqueId = uniqueId },
        };

    private static CampaignWorldOverview World(IReadOnlyList<CampaignWorldCraft> crafts) =>
        new([], 0, 0) { Crafts = crafts, Bases = [new(7, "Alpha", 0, 0), new(8, "Beta", 1, 1)] };
}
