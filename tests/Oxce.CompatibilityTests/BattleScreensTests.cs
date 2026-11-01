using Oxce.Engine;
using Oxce.Engine.Battlescape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Xunit;
using static Oxce.CompatibilityTests.BattleStartTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 3 S6: the battlescape screens on <see cref="ScreenNavigator"/>, driven by scripted input over a generated battle
/// (no pixels are compared). The reference screens are <c>BriefingState</c>, <c>InventoryState</c> and <c>BattlescapeState</c>
/// (<c>src/Battlescape/*.cpp</c> at 4df3a5e); every decision shown comes from the rules' queries and commands.
/// </summary>
public sealed class BattleScreensTests
{
    private const uint Tab = 9, Enter = 13, Escape = 27, Right = 0x4000004f, Down = 0x40000051, PageUp = 0x4000004b;

    private static (CampaignState Campaign, BattleState Battle, BattleUiContext Context) Open(BattlePhase phase, string? rules = null)
    {
        var (campaign, _) = Requested(rules ?? Rules, "UFO_CRAFTED", [new("VEHICLE", 0), new("VEHICLE_B", 0)]);
        Started(campaign);
        var battle = Battle(campaign);
        if (phase >= BattlePhase.Inventory) campaign.Execute(new ConfirmBriefing());
        if (phase == BattlePhase.InProgress) campaign.Execute(new StartFirstTurn());
        var context = new BattleUiContext(campaign, campaign.GetQuery<IBattleQuery>()!, campaign.GetQuery<IBattleViewQuery>()!,
            campaign.GetQuery<IBattleInventoryQuery>()!, campaign.GetQuery<IBattleBriefingQuery>()!, BattleViewer.LocalPlayer, new ScreenNavigator());
        return (campaign, battle, context);
    }

    private static GameInputEvent Key(uint key, InputKeyModifiers modifiers = InputKeyModifiers.None) =>
        GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, modifiers);

    private static GameInputEvent Click(float x, float y, byte button = 1) =>
        GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x, y, button, 1);

    private static void Press(ScreenNavigator screens, uint key, InputKeyModifiers modifiers = InputKeyModifiers.None) =>
        Assert.True(screens.HandleInput(Key(key, modifiers)));

    [Fact]
    public void TheBriefingLeadsThroughThePreBattleInventoryIntoTheBattlescape()
    {
        var (_, battle, context) = Open(BattlePhase.Briefing);
        Assert.True(context.Open());
        var briefing = Assert.IsType<BattleBriefingScreen>(context.Screens.Top);
        Assert.Equal(BattlePhase.Briefing, briefing.Briefing!.Phase);
        var revision = briefing.PresentationRevision;
        Press(context.Screens, Enter);
        // Confirming the briefing replaced it with the inventory that is closed by the first turn.
        Assert.Equal(1, context.Screens.Count);
        var inventory = Assert.IsType<BattleInventoryScreen>(context.Screens.Top);
        Assert.NotEqual(revision, context.Screens.PresentationRevision);
        Assert.Equal(BattlePhase.Inventory, battle.Phase);
        Assert.NotNull(inventory.Inventory);
        Press(context.Screens, Escape);
        Assert.IsType<BattlescapeClient>(context.Screens.Top);
        Assert.Equal(BattlePhase.InProgress, battle.Phase);
        Assert.Equal(1, context.Screens.Count);
    }

    [Fact]
    public void TheBattlescapeShowsTheUnitStartFirstTurnSelectedNotTheLastOneTheInventoryWalkStoppedOn()
    {
        // startFirstTurn selects the unit standing on the inventory tile once the units are put back on their tiles; with two
        // units on one tile the later one in the list stands there. The battlescape shows getSelectedUnit, not the client's walk.
        var (_, battle, context) = Open(BattlePhase.Inventory);
        // The soldier and a vehicle share a tile; the one later in the unit list is the one the reset leaves standing there.
        var pair = battle.Units.Where(static unit => unit.Faction == BattleFaction.Player).Take(2).ToArray();
        var (first, second) = (pair[0], pair[1]);
        second.Position = first.Position;
        battle.SelectedUnitId = first.Id;
        context.Open();
        Assert.IsType<BattleInventoryScreen>(context.Screens.Top);
        Assert.Equal(first.Id, context.Model.SelectedUnitId);
        Press(context.Screens, Escape);
        var screen = Assert.IsType<BattlescapeClient>(context.Screens.Top);
        Assert.Equal(second.Id, battle.SelectedUnitId);
        Assert.Equal(second.Id, context.Model.SelectedUnitId);
        Assert.NotNull(screen);
    }

    [Fact]
    public void ABattleOfOnlyVehiclesClosesThePreBattleInventoryAtOnce()
    {
        // InventoryState::init: no unit with an inventory ("starting a mission with just vehicles") closes it with btnOkClick.
        var vehicles = Rules.Replace("- type: SUIT\n    movementType: 1",
            "- type: ARMOR\n    allowInv: false\n    movementType: 1\n    corpseBattle: [CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [0]\n  - type: SUIT\n    allowInv: false\n    movementType: 1",
            StringComparison.Ordinal);
        Assert.NotEqual(Rules, vehicles);
        var (_, battle, context) = Open(BattlePhase.Briefing, vehicles);
        Assert.All(battle.Units.Where(static unit => unit.Faction == BattleFaction.Player), unit => Assert.False(battle.Rules!.Battle.Equipment.Armors[unit.ArmorId].HasInventory, unit.ArmorId));
        Assert.True(context.Open());
        Press(context.Screens, Enter);
        Assert.Equal(BattlePhase.InProgress, battle.Phase);
        Assert.Equal(1, battle.Turn);
        Assert.IsType<BattlescapeClient>(context.Screens.Top);
        Assert.Equal(1, context.Screens.Count);
    }

    [Fact]
    public void ABattleThatIsAlreadyInProgressOpensTheBattlescapeDirectlyAndAnEmptyCampaignOpensNothing()
    {
        var (_, _, context) = Open(BattlePhase.InProgress);
        Assert.True(context.Open());
        Assert.IsType<BattlescapeClient>(context.Screens.Top);
        var none = new BattleUiContext(new NoCommands(), new NoBattle(), context.View, context.Inventory, context.Briefing, BattleViewer.LocalPlayer, new ScreenNavigator());
        Assert.False(none.Open());
        Assert.Null(none.CreateScreen());
    }

    [Fact]
    public void TheBriefingRefusalStaysOnTheScreen()
    {
        var (campaign, _, context) = Open(BattlePhase.Briefing);
        context.Open();
        var screen = Assert.IsType<BattleBriefingScreen>(context.Screens.Top);
        // Someone else already confirmed it: the rules refuse a second confirmation and the screen stays with the code.
        campaign.Execute(new ConfirmBriefing());
        Press(context.Screens, Enter);
        Assert.Same(screen, context.Screens.Top);
        Assert.NotEqual("", screen.Message);
    }

    [Fact]
    public void ThePreBattleInventoryDropsItemsWalksSoldiersAndRefusesFeaturesByName()
    {
        var (_, _, context) = Open(BattlePhase.Inventory);
        context.Open();
        var screen = Assert.IsType<BattleInventoryScreen>(context.Screens.Top);
        var view = screen.Inventory!;
        var carried = view.Carried.First(static item => !item.Fixed);
        // Walk the cursor to the carried item and the ground section, then drop the item there.
        for (var i = 0; i < 20 && screen.CurrentItem?.ItemId != carried.ItemId; i++) Press(context.Screens, Down);
        Assert.Equal(carried.ItemId, screen.CurrentItem!.ItemId);
        for (var i = 0; i < 20 && screen.CurrentSlot?.Type != 2; i++) Press(context.Screens, Right);
        Assert.Equal(2, screen.CurrentSlot!.Type);
        Press(context.Screens, Enter);
        Assert.Equal("", screen.Message);
        Assert.Contains(screen.Inventory!.Ground, item => item.ItemId == carried.ItemId);
        // Ctrl click on the ground item.
        for (var i = 0; i < 40 && screen.CurrentItem?.ItemId != carried.ItemId; i++) Press(context.Screens, Down);
        Press(context.Screens, 'g');
        // The test rules define no cost for this move: the rules' refusal is shown and nothing moves.
        Assert.Equal("battle.inventory-cost-unknown", screen.Message);
        Assert.Contains(screen.Inventory!.Ground, item => item.ItemId == carried.ItemId);
        // A feature of a later part is refused by the rules' name, and nothing changes.
        Press(context.Screens, 'l');
        Assert.Equal("battle.inventory-ammo-unsupported", screen.Message);
        Press(context.Screens, 'r');
        Assert.Equal("battle.inventory-rename-unsupported", screen.Message);
        Press(context.Screens, Tab, InputKeyModifiers.LeftShift);
        Assert.NotNull(screen.Inventory);
    }

    [Fact]
    public void TheBattlescapeSelectsSkipsChangesLevelAndShowsTheUnit()
    {
        var (_, battle, context) = Open(BattlePhase.InProgress);
        context.Open();
        var screen = Assert.IsType<BattlescapeClient>(context.Screens.Top);
        var model = screen.Model;
        var first = model.SelectedUnitId;
        Assert.NotNull(first);
        var revision = screen.PresentationRevision;
        Press(context.Screens, Tab);
        Assert.NotEqual(first, model.SelectedUnitId);
        Assert.True(screen.PresentationRevision > revision);
        var second = model.SelectedUnitId;
        Press(context.Screens, 'k');
        Assert.Equal(second, model.Selection!.UndoUnitId);
        Press(context.Screens, 'r');
        Assert.Equal(second, model.SelectedUnitId);
        Press(context.Screens, 'k', InputKeyModifiers.LeftShift);
        Assert.Equal(second, model.Selection!.UndoUnitId);
        // The level keys stop at the map's levels; the panel reads the unit's bars.
        var level = model.Camera.ViewLevel;
        Press(context.Screens, PageUp);
        Assert.Equal(Math.Min(level + 1, battle.Height - 1), model.Camera.ViewLevel);
        Press(context.Screens, 'c');
        Assert.NotNull(model.SelectedUnit);
        Assert.Contains(BattleActionKind.SkipUnit, model.Actions!.Actions);
        Assert.Contains(screen.Frame.Pixels.ToArray(), pixel => pixel == BattleUiContext.Color.Floor);
        // Every distinct terrain object without a sprite is listed once.
        Assert.NotEmpty(context.Diagnostics);
        Assert.All(context.Diagnostics, static note => Assert.StartsWith("missing sprite ", note));
    }

    [Fact]
    public void AClickOnAUnitSelectsItAndAClickOnATileIsRefusedByTheOwnerOfWalking()
    {
        var (_, _, context) = Open(BattlePhase.InProgress);
        context.Open();
        var screen = Assert.IsType<BattlescapeClient>(context.Screens.Top);
        var model = screen.Model;
        model.Camera.SetViewLevel(0);
        model.Refresh();
        var target = model.Level!.Tiles.First(tile => tile.UnitId is { } id && id != model.SelectedUnitId && model.Overview!.Units.Single(u => u.Id == id).Faction == BattleFaction.Player);
        var size = screen.TileSize;
        Assert.True(context.Screens.HandleInput(Click(target.Position.X * size + 1, target.Position.Y * size + 1)));
        Assert.Equal(target.UnitId, model.SelectedUnitId);
        var empty = model.Level!.Tiles.First(tile => tile.UnitId is null);
        context.Screens.HandleInput(Click(empty.Position.X * size + 1, empty.Position.Y * size + 1));
        Assert.StartsWith("battle.walking-unsupported", screen.Message);
        // A click outside the map area does nothing.
        var revision = screen.PresentationRevision;
        context.Screens.HandleInput(Click(MapEdge, 10));
        Assert.Equal(revision, screen.PresentationRevision);
    }

    private const int MapEdge = BattlescapeClient.MapWidth + 4;

    [Fact]
    public void TheButtonsOfLaterBranchesAreRefusedByTheirOwnersName()
    {
        var (_, battle, context) = Open(BattlePhase.InProgress);
        context.Open();
        var screen = Assert.IsType<BattlescapeClient>(context.Screens.Top);
        var revision = battle.Revision;
        foreach (var (key, code, owner) in BattlescapeClient.LaterButtons)
        {
            Press(context.Screens, key);
            Assert.Equal($"{code} ({owner})", screen.Message);
        }
        Assert.Equal(revision, battle.Revision);
        // A refusal of the rules shows its code: it is the enemy's turn.
        battle.Side = BattleFaction.Hostile;
        Press(context.Screens, Tab);
        Assert.Equal("battle.selection-unavailable", screen.Message);
        Press(context.Screens, 'i');
        Assert.Equal("battle.inventory-unavailable", screen.Message);
    }

    [Fact]
    public void TheInventoryOpensOverTheBattlescapeAndPoppingRevealsItAgain()
    {
        var (campaign, _, context) = Open(BattlePhase.InProgress);
        context.Open();
        var map = Assert.IsType<BattlescapeClient>(context.Screens.Top);
        Press(context.Screens, 'i');
        var inventory = Assert.IsType<BattleInventoryScreen>(context.Screens.Top);
        Assert.True(context.Screens.Covers(map));
        // Another player's command changes the battle while the inventory covers the map: the map reads it when revealed.
        var before = map.PresentationRevision;
        campaign.Execute(new SelectNextUnit(map.Model.SelectedUnitId!.Value));
        Press(context.Screens, Escape);
        Assert.Same(map, context.Screens.Top);
        Assert.False(context.Screens.Covers(map));
        Assert.True(map.PresentationRevision > before);
        Assert.True(inventory.ExitRequested);
    }

    [Fact]
    public void AChangeMadeElsewhereIsReadOnTheNextTick()
    {
        var (campaign, _, context) = Open(BattlePhase.InProgress);
        context.Open();
        var map = Assert.IsType<BattlescapeClient>(context.Screens.Top);
        var before = map.PresentationRevision;
        Assert.True(context.Screens.Tick(TimeSpan.FromMilliseconds(16)));
        Assert.Equal(before, map.PresentationRevision);
        campaign.Execute(new SkipUnit(map.Model.SelectedUnitId!.Value));
        context.Screens.Tick(TimeSpan.FromMilliseconds(16));
        Assert.True(map.PresentationRevision > before);
    }

    [Fact]
    public void TheBriefingKeepsTheNamesTheClientPassesAndEscapeConfirmsIt()
    {
        var (_, battle, context) = Open(BattlePhase.Briefing);
        Assert.True(context.Open("Landing site 7", "Craft SHIP-1"));
        var screen = Assert.IsType<BattleBriefingScreen>(context.Screens.Top);
        Assert.Equal(("Landing site 7", "Craft SHIP-1"), (screen.Briefing!.TargetName, screen.Briefing.CraftOrBaseName));
        Assert.Equal("Craft SHIP-1", battle.CraftOrBaseName);
        Press(context.Screens, Escape);
        Assert.IsType<BattleInventoryScreen>(context.Screens.Top);
    }

    [Fact]
    public void TheBattlescapeOpensOnTheSelectedUnitAndTheCameraFollowsTheCenterKeys()
    {
        var (_, battle, context) = Open(BattlePhase.InProgress);
        context.Open();
        var screen = Assert.IsType<BattlescapeClient>(context.Screens.Top);
        var model = screen.Model;
        var position = battle.Units.Single(unit => unit.Id == model.SelectedUnitId).Position!.Value;
        Assert.Equal((position.X, position.Y, position.Z), (model.Camera.Center.X, model.Camera.Center.Y, model.Camera.ViewLevel));
        // The arrow keys scroll the camera one tile (up and left toward lower coordinates), the level stays.
        model.Camera.CenterOn(new BattlePosition(5, 5, 0));
        Press(context.Screens, 0x40000052);
        Assert.Equal((5, 4, 0), (model.Camera.Center.X, model.Camera.Center.Y, model.Camera.ViewLevel));
        Press(context.Screens, 0x40000050);
        Assert.Equal((4, 4), (model.Camera.Center.X, model.Camera.Center.Y));
        Press(context.Screens, 0x40000051);
        Press(context.Screens, 0x4000004f);
        Assert.Equal((5, 5, 0), (model.Camera.Center.X, model.Camera.Center.Y, model.Camera.ViewLevel));
        // Home and C center again after the camera moved; Ctrl-C toggles all layers.
        model.Camera.CenterOn(new BattlePosition(0, 0, 0));
        Press(context.Screens, 'c');
        Assert.Equal(position.X, model.Camera.Center.X);
        model.Camera.CenterOn(new BattlePosition(0, 0, 0));
        Press(context.Screens, 0x4000004a);
        Assert.Equal(position.Y, model.Camera.Center.Y);
        Press(context.Screens, 'c', InputKeyModifiers.LeftControl);
        Assert.True(model.Camera.ShowAllLayers);
        // The level buttons and the center button are silent no-ops when they cannot act.
        model.Camera.SetViewLevel(0);
        model.Refresh();
        Press(context.Screens, 'e');
        Assert.StartsWith("battle.end-turn", screen.Message);
        Press(context.Screens, '[');
        Assert.Equal("", screen.Message);
        Press(context.Screens, ']');
        Assert.Equal(1, model.Camera.ViewLevel);
        Press(context.Screens, ']');
        Assert.Equal("", screen.Message);
        Press(context.Screens, 0x4000004e);
        Assert.Equal(0, model.Camera.ViewLevel);
    }

    [Fact]
    public void TheOtherInputsOfTheReferenceAreRefusedByTheirOwnersName()
    {
        var (_, battle, context) = Open(BattlePhase.InProgress);
        context.Open();
        var screen = Assert.IsType<BattlescapeClient>(context.Screens.Top);
        var revision = battle.Revision;
        Press(context.Screens, Escape);
        Assert.Equal(BattlescapeClient.DeselectCode, screen.Message);
        Press(context.Screens, '3');
        Assert.Equal(BattlescapeClient.VisibleUnitCode, screen.Message);
        Press(context.Screens, 'b', InputKeyModifiers.LeftControl);
        Assert.Equal(BattlescapeClient.BriefingAgainCode, screen.Message);
        context.Screens.HandleInput(Click(10, 10, 3));
        Assert.Equal(BattlescapeClient.SecondaryActionCode, screen.Message);
        context.Screens.HandleInput(Click(10, 10, 2));
        Assert.Equal(BattlescapeClient.MiddleButtonCode, screen.Message);
        Assert.Equal(revision, battle.Revision);
    }

    [Fact]
    public void AnIdleInventoryDoesNotRedrawAndAnUnacceptedDropOrFirstTurnShowsTheRulesCode()
    {
        var (campaign, _, context) = Open(BattlePhase.Inventory);
        context.Open();
        var screen = Assert.IsType<BattleInventoryScreen>(context.Screens.Top);
        var revision = screen.PresentationRevision;
        for (var i = 0; i < 3; i++) context.Screens.Tick(TimeSpan.FromMilliseconds(16));
        Assert.Equal(revision, screen.PresentationRevision);
        // Walking soldiers and the cursor keys keep a valid unit and cursor.
        var unit = screen.Inventory!.UnitId;
        Press(context.Screens, Tab);
        Press(context.Screens, Tab, InputKeyModifiers.LeftShift);
        Press(context.Screens, 0x40000052);
        Press(context.Screens, 0x40000050);
        Assert.Equal(unit, screen.Inventory!.UnitId);
        // Every section, tried in turn with the first carried item: whatever the rules refuse shows its code and moves nothing.
        var item = screen.Inventory.Carried.First(static carried => !carried.Fixed);
        for (var i = 0; i < 20 && screen.CurrentItem?.ItemId != item.ItemId; i++) Press(context.Screens, Down);
        var shown = new List<string>();
        for (var slot = 0; slot < screen.Inventory.Slots.Count; slot++)
        {
            Press(context.Screens, Enter);
            shown.Add(screen.Message);
            Press(context.Screens, Right);
        }
        Assert.All(shown, static message => Assert.True(message.Length == 0 || message.StartsWith("battle.inventory-", StringComparison.Ordinal)));
        // The first turn is refused by the rules (it already began): the inventory stays open with the code.
        campaign.Execute(new StartFirstTurn());
        Press(context.Screens, Escape);
        Assert.Same(screen, context.Screens.Top);
        Assert.NotEqual("", screen.Message);
    }

    [Fact]
    public void ASpriteProviderThatKnowsSomeObjectsLeavesDiagnosticsOnlyForTheOthers()
    {
        var (campaign, _, plain) = Open(BattlePhase.InProgress);
        plain.Open();
        var all = plain.Diagnostics.ToArray();
        Assert.NotEmpty(all);
        var known = all[0]["missing sprite ".Length..];
        var sprites = new Oxce.Rendering.IndexedSurface(2, 2);
        var context = new BattleUiContext(campaign, plain.Battle, plain.View, plain.Inventory, plain.Briefing, BattleViewer.LocalPlayer,
            new ScreenNavigator(), sprites: (set, index) => $"{set}#{index}" == known ? sprites : null);
        context.Open();
        Assert.DoesNotContain(all[0], context.Diagnostics);
        Assert.Equal(all.Length - 1, context.Diagnostics.Count);
    }

    [Fact]
    public void ClosingThePreBattleInventoryWithNoLiveSoldiersShowsTheEndOfBattleRefusal()
    {
        var (_, battle, context) = Open(BattlePhase.Inventory);
        context.Open();
        var screen = Assert.IsType<BattleInventoryScreen>(context.Screens.Top);
        foreach (var unit in battle.Units.Where(static unit => unit.Faction == BattleFaction.Player)) unit.Status = BattleUnitStatus.Dead;
        Press(context.Screens, Escape);
        // NextTurnState::close would finish the battle (branch 7); the rules refuse it by name and the inventory stays.
        Assert.Equal("battle.no-live-soldiers", screen.Message);
        Assert.Same(screen, context.Screens.Top);
    }

    private sealed class NoBattle : IBattleQuery
    {
        public BattleOverview? QueryBattle(BattleViewer viewer) => null;
    }

    private sealed class NoCommands : ICampaignCommandTarget
    {
        public CampaignCommandResult Execute(ICampaignCommand command) => new([]);
    }
}
