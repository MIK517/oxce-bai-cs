using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>InterceptState, SelectDestinationState, ConfirmDestinationState, GeoscapeCraftState and TargetInfoState.</summary>
public sealed class StrategicWorldGeoscapeScreensTests
{
    private static readonly CraftKey Ship = new(0, "SHIP", 1);
    private static readonly CraftKey Interceptor = new(0, "INTERCEPTOR", 1);

    [Fact]
    public void InterceptBuildsAWingThatChoosesAWaypointOnTheGlobe()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var (campaign, client) = Open(content);

        Key(client, 'i');
        Assert.Equal("STR_LAUNCH_INTERCEPTION", Assert.Single(client.Popups).Title);
        // Shift+click adds the interceptor to the wing; a plain click on the ship launches it as leader.
        client.Choose(Row(client, Interceptor), shift: true);
        Assert.StartsWith("* ", client.Popups[0].Actions[Row(client, Interceptor)].Label, StringComparison.Ordinal);
        client.Choose(Row(client, Ship));
        Assert.Empty(client.Popups);
        Assert.Equal([Ship, Interceptor], client.SelectingWing);
        Assert.False(client.TimeCanRun);

        // An empty spot on the globe becomes a waypoint; the wing would follow its leader by default.
        client.View.Center(1.5, -0.3);
        Click(client, 256, 200);
        var confirm = Assert.Single(client.Popups);
        Assert.StartsWith("[x] ", confirm.Actions[1].Label, StringComparison.Ordinal);
        client.Choose(1);
        Assert.StartsWith("[ ] ", Assert.Single(client.Popups).Actions[1].Label, StringComparison.Ordinal);
        Key(client, 13);

        Assert.Empty(client.Popups);
        Assert.Null(client.SelectingWing);
        Assert.True(client.TimeCanRun);
        var waypoint = Assert.Single(campaign.Capture().World.Waypoints);
        Assert.Equal(1.5, waypoint.Longitude, 6);
        Assert.Equal(-0.3, waypoint.Latitude, 6);
        Assert.All(client.World.Crafts.Where(craft => craft.TypeId is "SHIP" or "INTERCEPTOR"), craft =>
        {
            Assert.Equal("STR_OUT", craft.Status);
            Assert.Equal((WorldTargetKind.Waypoint, waypoint.Id), (craft.Destination!.Kind, craft.Destination.Id));
        });
    }

    [Fact]
    public void TheCraftScreenPatrolsAndRecallsAnAirborneCraft()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var (campaign, client) = Open(content, launched: true);
        var ship = ShipOf(client);
        Assert.Equal("STR_OUT", ship.Status);

        OpenCraft(client, ship);
        var screen = Assert.Single(client.Popups);
        Assert.Equal(["STR_RETURN_TO_BASE", "STR_SELECT_NEW_TARGET", ship.CanAutoPatrol ? "STR_AUTO_PATROL" : "STR_PATROL",
            "STR_CANCEL_UC"], screen.Actions.Select(action => action.Label));
        Assert.Contains(screen.Lines, line => line.StartsWith("STR_FUEL", StringComparison.Ordinal));
        client.Choose(0);
        Assert.Empty(client.Popups);
        Assert.Equal(WorldTargetKind.Base, ShipOf(client).Destination!.Kind);
        Assert.Equal(campaign.Capture().Bases[0].Id, ShipOf(client).Destination!.Id);

        // Patrolling where it took off hides the craft behind its base, as Globe::getTargets does.
        OpenCraft(client, ShipOf(client));
        client.Choose(2);
        Assert.Empty(client.Popups);
        Assert.Null(ShipOf(client).Destination);
        Assert.DoesNotContain(GlobeTargets.Build(client.World).All, target => target.Kind == WorldTargetKind.Craft);
    }

    [Fact]
    public void InterceptFromATargetKeepsItsScreenUntilTheWingLaunches()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var (_, client) = Open(content, launched: true);
        var waypoint = Assert.Single(client.World.Targets, target => target.Kind == WorldTargetKind.Waypoint);
        client.View.Center(waypoint.Longitude, waypoint.Latitude);
        Click(client, 256, 200);
        Assert.Equal(["STR_INTERCEPT", "STR_OK"], Assert.Single(client.Popups).Actions.Select(action => action.Label));

        client.Choose(0);
        Assert.Equal(2, client.Popups.Count);
        Key(client, 27);
        Assert.Equal(waypoint.Label, Assert.Single(client.Popups).Title);
        client.Choose(0);
        client.Choose(Row(client, Interceptor));
        // ConfirmDestinationState for the given target; its OK closes InterceptState's caller too.
        Assert.Equal(2, client.Popups.Count);
        Key(client, 13);
        Assert.Empty(client.Popups);
        var interceptor = client.World.Crafts.Single(craft => craft.TypeId == "INTERCEPTOR");
        Assert.Equal((WorldTargetKind.Waypoint, waypoint.Id), (interceptor.Destination!.Kind, interceptor.Destination.Id));
    }

    [Fact]
    public void UnavailableCraftRefuseAndRefusalsUseTheReferenceText()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], "crafts: [{type: INTERCEPTOR, pilots: 2}]\n",
            extraRulesAfterOverlays: true);
        var (campaign, client) = Open(content, interceptorStatus: "STR_REFUELLING");
        var before = campaign.Capture();

        Key(client, 'i');
        var row = Row(client, Interceptor);
        Assert.NotNull(client.Popups[0].Actions[row].Disabled);
        client.Choose(row);
        Assert.Equal(client.Popups[0].Actions[row].Disabled, client.Status);
        Key(client, 27);
        Assert.Empty(client.Popups);

        // A grounded leader picks a destination inside its range; Esc cancels the choice.
        Key(client, 'i');
        client.Choose(Row(client, Ship));
        Assert.NotNull(client.SelectingWing);
        Assert.True(ShipOf(client).BaseRange is > 0 and < Math.PI);
        Key(client, 27);
        Assert.Null(client.SelectingWing);
        Assert.True(client.TimeCanRun);
        Assert.Equivalent(before, campaign.Capture(), strict: true);

        var ready = Open(content).Client;
        Key(ready, 'i');
        ready.Choose(Row(ready, Interceptor));
        ready.View.Center(1.5, -0.3);
        Click(ready, 256, 200);
        Key(ready, 13);
        // CraftNotEnoughPilotsState: the language key with the craft's pilot requirement.
        Assert.Equal("STR_NOT_ENOUGH_PILOTS", Assert.Single(ready.Popups).Title);
        Assert.Null(ready.SelectingWing);
    }

    private static (CampaignState Campaign, GeoscapeClient Client) Open(RuntimeContent content,
        string interceptorStatus = "STR_READY", bool launched = false)
    {
        var snapshot = CreateSnapshot(content, "Screens")
            .WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with { Status = "STR_READY", Fuel = 100, Weapons = [], Items = new Dictionary<string, int>() },
            })
            .WithCraft("INTERCEPTOR", craft => craft with
            {
                Logistics = craft.Logistics! with { Status = interceptorStatus, Fuel = 10 },
            });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(41));
        Assert.Equal(0, snapshot.Bases[0].Id);
        if (launched)
            Assert.IsType<CraftDestinationChanged>(Assert.Single(campaign.Execute(
                new DispatchCraftToWaypoint(0, "SHIP", 1, 1.2, 0.4)).Events));
        var texture = new IndexedSurface(32, 32);
        texture.Clear(0x41);
        var assets = new GeoscapeAssets(IndexedPalette.CreateGrayscale(),
            new Dictionary<int, IndexedSurface> { [0] = texture, [1] = texture, [2] = texture },
            new Dictionary<int, IndexedSurface>(), RuntimeGlobeColors.Default);
        var client = new GeoscapeClient(new GeoscapeClientSetup(CampaignUiSession.ForCampaign(campaign, campaign, campaign),
            content.RuntimeRules, assets, IndexedInterfaceFont.Create(), static text => text)
        {
            Stopwatch = static () => TimeSpan.Zero,
        });
        Assert.True(client.TimeCanRun);
        return (campaign, client);
    }

    private static CampaignWorldCraft ShipOf(GeoscapeClient client) => client.World.Crafts.Single(craft => craft.TypeId == "SHIP");

    private static void OpenCraft(GeoscapeClient client, CampaignWorldCraft craft)
    {
        client.View.Center(craft.Longitude, craft.Latitude);
        Click(client, 256, 200);
        // A craft that has just taken off still overlaps its base: MultipleTargetsState lists both.
        if (client.Popups is [{ Title: "STR_TARGETS" } targets])
            client.Choose(targets.Actions.ToList().FindIndex(action => action.Label == GeoscapeScreens.CraftName(craft, static text => text)));
    }

    private static int Row(GeoscapeClient client, CraftKey craft)
    {
        var actions = client.Popups[0].Actions;
        for (var index = 0; index < actions.Count; index++)
            if (actions[index].Intent is InterceptRowIntent row && row.Craft == craft) return index;
        throw new InvalidOperationException($"No intercept row for {craft}.");
    }

    private static void Key(GeoscapeClient client, uint key) =>
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, InputKeyModifiers.None));

    private static void Click(GeoscapeClient client, int x, int y) =>
        client.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x, y, 1, 1));
}
