using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Scripted geoscape sessions: the player's inputs drive the client from interception to recall,
/// time runs between them, and quick save and load continue the campaign.
/// </summary>
public sealed class StrategicWorldGeoscapeScenarioTests
{
    [Fact]
    public void ACrewlessCraftWaitsOverALandingUntilTakeoffStopsTimeThenIsRecalledAndTheGameReloads()
    {
        // The fixture ship burns its 100 fuel in minutes; a long-range tank keeps low fuel out of this run.
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], "crafts: [{type: SHIP, fuelMax: 100000}]\n",
            extraRulesAfterOverlays: true);
        var source = CreateLifecycleSnapshot(content);
        var fuel = content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value.FuelMaximum;
        var snapshot = source.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Status = "STR_READY", Fuel = fuel, Weapons = [], Items = new Dictionary<string, int>() },
        }) with
        {
            // A detected landing that lifts off in two hours.
            World = source.World with
            {
                Ufos = [source.World.Ufos[0] with { Detected = true, LandId = 1, SecondsRemaining = 2 * 3600 }],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(23));
        string? saved = null;
        GeoscapeClient? client = null;
        client = Client(content, CampaignUiSession.ForCampaign(campaign, campaign, campaign), save: () =>
        {
            var view = client!.CurrentView;
            saved = OxceSaveAdapter.EmitNewCampaign(campaign.Capture(),
                new OxceSaveWriteOptions { GlobeView = new(view.Longitude, view.Latitude, view.Zoom) });
        }, load: () =>
        {
            var loaded = TestFixtures.LoadLogisticsSave(saved!, content, seed: 23, name: "scenario.sav");
            campaign = loaded.Campaign;
            var view = loaded.Source.GlobeView;
            return CampaignUiSession.ForCampaign(campaign, campaign, campaign) with
            {
                GlobeView = new GlobeViewState(view.Longitude, view.Latitude, view.Zoom),
            };
        });

        // Click the UFO, Intercept, pick the ship and confirm.
        var ufo = Assert.Single(client.World.Targets, target => target.Kind == WorldTargetKind.Ufo);
        client.View.Center(ufo.Longitude, ufo.Latitude);
        Click(client, 256, 200);
        Assert.Equal(ufo.Label, Assert.Single(client.Popups).Title);
        client.Choose(0);
        client.Choose(Row(client, "SHIP"));
        Key(client, 13);
        Assert.Empty(client.Popups);
        Assert.Equal(WorldTargetKind.Ufo, Ship(client).Destination!.Kind);

        // The ship carries no crew, so it waits over the landing (no ConfirmLandingState). When the UFO
        // lifts off the ship meets a flying UFO: interception needs strategic combat (branch 5), so the
        // clock stops with the reason in a popup and stays halted.
        Key(client, '2');
        var seen = Drive(client, () => campaign, 4 * 60, () => client.Time.HaltReason is not null);
        Assert.Equal(["Time cannot advance: " + WorldCommandReasons.InterceptionBoundary], seen);
        Assert.Contains(WorldCommandReasons.InterceptionBoundary, client.Time.HaltReason, StringComparison.Ordinal);
        Assert.False(client.Time.Running);
        var stopped = campaign.QueryOverview().Time;
        client.Tick(GeoscapeTimeController.GeoClock);
        Assert.Equal(stopped, campaign.QueryOverview().Time);

        // Recall from the craft screen; the clock restarts on a speed choice.
        var ship = Ship(client);
        Assert.Equal(("STR_OUT", WorldTargetKind.Ufo), (ship.Status, ship.Destination!.Kind));
        OpenCraft(client, ship);
        client.Choose(0);
        Assert.Equal(WorldTargetKind.Base, Ship(client).Destination!.Kind);
        Key(client, '2');
        Assert.True(client.TimeCanRun);
        Drive(client, () => campaign, 3 * 24 * 60, () => Ship(client).Status != "STR_OUT");
        Assert.NotEqual("STR_OUT", Ship(client).Status);

        // Quick save, change the view, quick load: the campaign and view come back.
        client.View.Center(1.1, -0.2);
        Key(client, 0x4000003E);
        Assert.Equal("Campaign saved.", client.Status);
        var before = campaign.Capture();
        var view = client.CurrentView;
        client.View.Center(3.0, 0.5);
        Key(client, 0x40000042);
        Key(client, 13);
        Assert.Equal("Campaign loaded.", client.Status);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Equal(view, client.CurrentView);
        Assert.True(client.TimeCanRun);
        Drive(client, () => campaign, 60, () => false);
    }

    /// <summary>
    /// Runs the clock through the client for a number of game minutes, answering every popup with a
    /// button that only closes it, and checks that time never runs under a popup or a choice.
    /// </summary>
    internal static List<string> Drive(GeoscapeClient client, Func<CampaignState> campaign, int minutes, Func<bool> stop)
    {
        var seen = new List<string>();
        var start = Minutes(campaign().QueryOverview().Time);
        for (var frame = 0; frame < 200_000 && Minutes(campaign().QueryOverview().Time) - start < minutes; frame++)
        {
            var choosing = client.SelectingWing is not null || client.PlacingBase is not null || client.BaseName is not null ||
                client.BasesOpen;
            if (client.Popups.Count != 0 || choosing)
            {
                // Time waits under a popup, a destination or base-site choice, a name and the base screens.
                Assert.False(client.TimeCanRun);
                var before = campaign().QueryOverview().Time;
                client.Tick(GeoscapeTimeController.GeoClock);
                Assert.Equal(before, campaign().QueryOverview().Time);
            }
            Assert.False(choosing, "The drive does not answer choices other than popups.");
            if (client.Popups.Count != 0)
            {
                var popup = client.Popups[0];
                seen.Add(popup.Lines.Count == 0 ? popup.Title : popup.Title + ": " + popup.Lines[0]);
                // Answer with a button that only closes the popup, never one that runs a command or opens a screen.
                var choice = IndexOf(popup, action => action.CancelKey && action.Intent is null && action.Command is null &&
                    action.Disabled is null);
                if (choice < 0) choice = IndexOf(popup, action => action.Intent is null && action.Command is null && action.Disabled is null);
                // The lost-UFO craft screen's Cancel is "Go to last known UFO position".
                if (choice < 0) choice = IndexOf(popup, action => action.CancelKey && action.Intent is null && action.Disabled is null);
                Assert.True(choice >= 0, $"No button closes \"{popup.Title}\".");
                var count = client.Popups.Count;
                client.Choose(choice);
                Assert.True(client.Popups.Count < count || !ReferenceEquals(client.Popups[0], popup),
                    $"\"{popup.Title}\" did not close.");
                continue;
            }
            if (stop() || !client.Time.Running) break;
            client.Tick(GeoscapeTimeController.GeoClock);
        }
        return seen;
    }

    private static long Minutes(CampaignTime time) =>
        ((((long)time.Year * 12 + time.Month) * 31 + time.Day) * 24 + time.Hour) * 60 + time.Minute;

    private static int IndexOf(GeoscapePopup popup, Func<GeoscapePopupAction, bool> predicate)
    {
        for (var index = 0; index < popup.Actions.Count; index++)
            if (predicate(popup.Actions[index])) return index;
        return -1;
    }

    private static CampaignWorldCraft Ship(GeoscapeClient client) => client.World.Crafts.Single(craft => craft.TypeId == "SHIP");

    private static int Row(GeoscapeClient client, string type)
    {
        var actions = client.Popups[0].Actions;
        for (var index = 0; index < actions.Count; index++)
            if (actions[index].Intent is InterceptRowIntent row && row.Craft.CraftTypeId == type) return index;
        throw new InvalidOperationException($"No intercept row for {type}.");
    }

    private static void OpenCraft(GeoscapeClient client, CampaignWorldCraft craft)
    {
        client.View.Center(craft.Longitude, craft.Latitude);
        Click(client, 256, 200);
        if (client.Popups is [{ Title: "STR_TARGETS" } targets])
            client.Choose(targets.Actions.ToList().FindIndex(action => action.Label == GeoscapeScreens.CraftName(craft, static text => text)));
    }

    private static GeoscapeClient Client(RuntimeContent content, CampaignUiSession session, Action save, Func<CampaignUiSession> load)
    {
        var texture = new IndexedSurface(32, 32);
        texture.Clear(0x41);
        var assets = new GeoscapeAssets(IndexedPalette.CreateGrayscale(),
            new Dictionary<int, IndexedSurface> { [0] = texture, [1] = texture, [2] = texture },
            new Dictionary<int, IndexedSurface>(), RuntimeGlobeColors.Default);
        return new GeoscapeClient(new GeoscapeClientSetup(session, content.RuntimeRules, assets,
            IndexedInterfaceFont.Create(), static text => text)
        {
            Stopwatch = static () => TimeSpan.Zero,
            Save = save,
            Load = load,
        });
    }

    private static void Key(GeoscapeClient client, uint key) =>
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, InputKeyModifiers.None));

    private static void Click(GeoscapeClient client, int x, int y) =>
        client.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x, y, 1, 1));
}
