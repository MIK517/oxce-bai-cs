using Oxce.Core.Graphics;
using Oxce.Engine;
using Oxce.Engine.Battlescape;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Gameplay.Sessions;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;
using Xunit;
using static Oxce.CompatibilityTests.BattleStartTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 3 S6b: the host loop opens a generated battle's screens above the geoscape, the screens cover the host clock, and
/// the turn-1 save of <c>BattlescapeState::init</c> is requested when the first turn starts.
/// </summary>
public sealed class GeoscapeBattleHostTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(80);

    internal static GeoscapeClient Client(CampaignState campaign, List<CampaignSaveKind> saves, out CampaignSession host, out SessionHandle local,
        Func<string, string>? localize = null)
    {
        host = new CampaignSession(campaign, campaign, static () => TimeSpan.Zero);
        local = host.Attach(PlayerId.Local);
        host.Controllers = new BattleUnitControllers(campaign.GetQuery<IBattleQuery>()!);
        var texture = new IndexedSurface(32, 32);
        texture.Clear(0x41);
        var assets = new GeoscapeAssets(IndexedPalette.CreateGrayscale(),
            new Dictionary<int, IndexedSurface> { [0] = texture, [1] = texture, [2] = texture },
            new Dictionary<int, IndexedSurface>(), RuntimeGlobeColors.Default);
        return new GeoscapeClient(new GeoscapeClientSetup(CampaignUiSession.ForCampaign(campaign, local, local),
            campaign.Content.RuntimeRules, assets, IndexedInterfaceFont.Create(), localize ?? (static text => text))
        {
            SaveRequested = saves.Add,
        });
    }

    private static CampaignState Briefing()
    {
        var (campaign, _) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 0), new("VEHICLE_B", 0)]);
        Started(campaign);
        return campaign;
    }

    internal static void Key(GeoscapeClient client, uint key) =>
        client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, InputKeyModifiers.None));

    [Fact]
    public void TheHostLoopOpensTheBriefingAboveTheGeoscapeAndTheScreensCoverTheClock()
    {
        var saves = new List<CampaignSaveKind>();
        var campaign = Briefing();
        var client = Client(campaign, saves, out var host, out var local);
        using var session = local;
        Assert.Equal(0, client.Screens.Count);
        client.Tick(Step);
        Assert.IsType<BattleBriefingScreen>(client.Screens.Top);
        Assert.True(host.Clock.Covered);
        var time = campaign.QueryOverview().Time;
        for (var tick = 0; tick < 3; tick++) client.Tick(Step);
        Assert.Equal(time, campaign.QueryOverview().Time);
        // The same screen is not pushed again while it stays open.
        Assert.Equal(1, client.Screens.Count);
        Assert.Empty(saves);
    }

    [Fact]
    public void TheFirstTurnRequestsTheTurnOneSaveOnceAndTheBattlescapeFollows()
    {
        var saves = new List<CampaignSaveKind>();
        var campaign = Briefing();
        var client = Client(campaign, saves, out _, out var local);
        using var session = local;
        client.Tick(Step);
        Key(client, 13);
        client.Tick(Step);
        Assert.IsType<BattleInventoryScreen>(client.Screens.Top);
        Assert.Empty(saves);
        Key(client, 27);
        Assert.IsType<BattlescapeClient>(client.Screens.Top);
        client.Tick(Step);
        client.Tick(Step);
        Assert.Equal([CampaignSaveKind.BattleTurnOne], saves);
    }

    [Fact]
    public void TheCampaignsBattlePreflightHaltsTheSessionClockAndAnAdvanceIsRefused()
    {
        // No advance during a battle, through the real clock path: the campaign's own `battle.in-progress` preflight (not a client's cover)
        // refuses the advance, which halts the clock until a speed is selected.
        var campaign = Briefing();
        var host = new CampaignSession(campaign, campaign, static () => TimeSpan.Zero);
        using var local = host.Attach(PlayerId.Local);
        var time = campaign.QueryOverview().Time;
        Assert.False(host.Clock.Covered);
        var step = host.Clock.Tick(SessionClock.GeoClock);
        Assert.Equal(0, step.Ticks);
        Assert.Contains("battle is in progress", step.BlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(time, campaign.QueryOverview().Time);
        Assert.False(host.Clock.Running);
        // A manual advance of a halted clock is the session's refusal; after a speed is selected the rules refuse it again.
        Assert.Equal(SessionReasons.ClockHalted, Assert.IsType<CampaignActionBlocked>(Assert.Single(host.Clock.Advance(1).Events)).Code);
        host.Clock.Select(CampaignTimeSpeed.FiveSeconds);
        Assert.Contains("battle is in progress", host.Clock.Advance(1).Events.OfType<CampaignActionBlocked>().First().Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(time, campaign.QueryOverview().Time);
    }

    [Fact]
    public void ABattleUnitCommandFromAPlayerWhoDoesNotControlTheUnitIsRefusedBySessionAndNothingChanges()
    {
        var campaign = Briefing();
        campaign.Execute(new ConfirmBriefing());
        campaign.Execute(new StartFirstTurn());
        var host = new CampaignSession(campaign, campaign, static () => TimeSpan.Zero);
        host.Controllers = new BattleUnitControllers(campaign.GetQuery<IBattleQuery>()!);
        using var local = host.Attach(PlayerId.Local);
        using var guest = host.Attach(new PlayerId(7));
        var battle = campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer)!;
        var unit = battle.Units.First(candidate => candidate.Faction == BattleFaction.Player && candidate.ControllerSlot == 0).Id;
        var before = campaign.GetQuery<IBattleViewQuery>()!.QuerySelection(BattleViewer.LocalPlayer)!.SelectedUnitId;
        Assert.Equal(SessionReasons.UnitNotControlled, Assert.IsType<CampaignActionBlocked>(Assert.Single(guest.Execute(new SelectUnit(unit)).Events)).Code);
        Assert.Equal(before, campaign.GetQuery<IBattleViewQuery>()!.QuerySelection(BattleViewer.LocalPlayer)!.SelectedUnitId);
        Assert.IsType<UnitSelected>(Assert.Single(local.Execute(new SelectUnit(unit)).Events));
    }

    [Fact]
    public void TheTurnOneSaveDoesNotDependOnATickSeeingThePhaseBetween()
    {
        // NextTurnState::close sets the autosave: confirming the briefing and closing the inventory between two ticks still saves once.
        var saves = new List<CampaignSaveKind>();
        var campaign = Briefing();
        var client = Client(campaign, saves, out _, out var local);
        using var session = local;
        client.Tick(Step);
        Key(client, 13);
        Key(client, 27);
        Assert.Equal([CampaignSaveKind.BattleTurnOne], saves);
        client.Tick(Step);
        client.Tick(Step);
        Assert.Equal([CampaignSaveKind.BattleTurnOne], saves);
    }

    [Fact]
    public void TheTurnOneSaveReloadsIntoTheSameBattlescapeWithoutAnotherSave()
    {
        // The two halves together: the host requests the save on the first turn, and a client over the reloaded save opens the battle.
        var saves = new List<CampaignSaveKind>();
        var campaign = Briefing();
        var client = Client(campaign, saves, out _, out var local);
        using var session = local;
        client.Tick(Step);
        Key(client, 13);
        Key(client, 27);
        Assert.Equal([CampaignSaveKind.BattleTurnOne], saves);
        var selected = campaign.GetQuery<IBattleViewQuery>()!.QuerySelection(BattleViewer.LocalPlayer)!.SelectedUnitId;
        var restored = CampaignState.Restore(campaign.Capture(), campaign.Content, new Oxce.Core.Random.SplitMix64RandomSource(3), TerrainFiles());
        var reloadedSaves = new List<CampaignSaveKind>();
        var reloaded = Client(restored, reloadedSaves, out _, out var again);
        using var secondSession = again;
        reloaded.Tick(Step);
        Assert.IsType<BattlescapeClient>(reloaded.Screens.Top);
        Assert.Equal(selected, reloaded.BattleContext!.Model.SelectedUnitId);
        reloaded.Tick(Step);
        Assert.Empty(reloadedSaves);
    }

    [Fact]
    public void ABriefingSavedAndReloadedKeepsItsLinesAndAClientsEmptyOnesDoNotReplaceThem()
    {
        // The save holds the lines the first open named; the client of the loaded save has no request to compose them from.
        var saves = new List<CampaignSaveKind>();
        var campaign = Requesting();
        static string Localize(string text) => text switch { "STR_CRAFT_" => "Craft: {0}", "STR_BASE_UC_" => "Base: {0}", _ => text };
        var client = Client(campaign, saves, out _, out var local, Localize);
        using var session = local;
        Key(client, 13);
        client.Tick(Step);
        Assert.IsType<BattleBriefingScreen>(client.Screens.Top);
        var named = campaign.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!;
        // BriefingState: tr("STR_CRAFT_").arg(craft name).
        Assert.StartsWith("Craft: ", named.CraftOrBaseName, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", named.CraftOrBaseName, StringComparison.Ordinal);
        var restored = CampaignState.Restore(campaign.Capture(), campaign.Content, new Oxce.Core.Random.SplitMix64RandomSource(3), TerrainFiles());
        var reloaded = Client(restored, [], out _, out var again, Localize);
        using var secondSession = again;
        reloaded.Tick(Step);
        Assert.IsType<BattleBriefingScreen>(reloaded.Screens.Top);
        var kept = restored.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!;
        Assert.Equal((named.TargetName, named.CraftOrBaseName), (kept.TargetName, kept.CraftOrBaseName));
    }

    [Fact]
    public void ABattleLoadedRunningOpensTheBattlescapeWithoutTheTurnOneSave()
    {
        var saves = new List<CampaignSaveKind>();
        var campaign = Briefing();
        campaign.Execute(new ConfirmBriefing());
        campaign.Execute(new StartFirstTurn());
        var client = Client(campaign, saves, out _, out var local);
        using var session = local;
        client.Tick(Step);
        Assert.IsType<BattlescapeClient>(client.Screens.Top);
        Assert.Empty(saves);
    }

    [Fact]
    public void ADeploymentRequestAsksToStartTheBattleAndOkStartsIt()
    {
        var (campaign, request) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 0), new("VEHICLE_B", 0)]);
        var popup = GeoscapeNotifications.DescribeAll(new DeploymentRequested(request), campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld(),
            static text => text, campaign.Content.RuntimeRules, 0).Single();
        var ok = Assert.Single(popup.Actions);
        Assert.IsType<StartBattle>(ok.Command);
        Assert.True(ok.OkKey);
    }

    private static CampaignState Requesting()
    {
        var (campaign, _) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 0), new("VEHICLE_B", 0)]);
        return campaign;
    }

    [Fact]
    public void OkOnTheStartPopupStartsTheBattleAndTheBriefingCarriesTheRequestsLines()
    {
        var saves = new List<CampaignSaveKind>();
        var campaign = Requesting();
        var client = Client(campaign, saves, out _, out var local);
        using var session = local;
        Assert.Equal("Start the battle", Assert.Single(client.Popups).Title);
        Key(client, 13);
        Assert.Empty(client.Popups);
        Assert.NotNull(campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer));
        client.Tick(Step);
        Assert.IsType<BattleBriefingScreen>(client.Screens.Top);
        var briefing = campaign.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!;
        // BriefingState names the craft that landed, or the base; the request carried one of them.
        Assert.NotEqual("", briefing.CraftOrBaseName);
    }

    [Fact]
    public void ARefusedStartShowsTheReasonAndOffersTheRequestAgain()
    {
        var saves = new List<CampaignSaveKind>();
        var campaign = Requesting();
        campaign.BattleTerrainFiles = null;
        var client = Client(campaign, saves, out _, out var local);
        using var session = local;
        Key(client, 13);
        // The refusal comes first; once read, the start popup returns because the request still holds the clock.
        Assert.NotEqual("Start the battle", client.Popups[0].Title);
        Key(client, 13);
        Assert.Equal("Start the battle", Assert.Single(client.Popups).Title);
        Assert.Null(campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer));
    }

    [Theory]
    [InlineData(CampaignSaveKind.Ironman, false, false, "save.sav")]
    [InlineData(CampaignSaveKind.Autosave, true, false, "_autogeo_.asav")]
    [InlineData(CampaignSaveKind.BattleTurnOne, true, true, "save.sav")]
    [InlineData(CampaignSaveKind.BattleTurnOne, false, true, "_autobattle_.asav")]
    [InlineData(CampaignSaveKind.BattleTurnOne, false, false, null)]
    public void TheSavePolicyMapsEachRequestToItsFile(CampaignSaveKind kind, bool ironman, bool autosave, string? file)
    {
        var path = Path.Combine(Path.GetTempPath(), "policy", "save.sav");
        var expected = file is null ? null : Path.Combine(Path.GetDirectoryName(path)!, file);
        Assert.Equal(expected, BattleSavePolicy.PathFor(kind, ironman, autosave, path));
    }
}
