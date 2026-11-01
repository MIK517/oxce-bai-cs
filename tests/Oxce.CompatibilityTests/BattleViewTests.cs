using Oxce.Engine.Battlescape;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Sessions;
using Xunit;
using static Oxce.CompatibilityTests.BattleStartTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 3 S5: the battlescape queries and view model. <c>SavedBattleGame::selectPlayerUnit</c>,
/// <c>selectNextPlayerUnitByDistance</c>, <c>selectUnit</c>, <c>BattleUnit::isSelectable</c>, <c>BattlescapeState</c>'s soldier buttons
/// (<c>btnNextSoldierClick</c>, <c>btnNextStopLClick</c>, <c>btnNextStopMClick</c>, <c>btnNextStopRClick</c>, <c>btnMapUpClick</c>,
/// <c>btnCenterClick</c>, <c>updateSoldierInfo</c>), <c>Camera</c> and <c>Map::drawTerrain</c> (<c>src/Savegame/SavedBattleGame.cpp</c>,
/// <c>src/Battlescape/BattlescapeState.cpp</c>, <c>Camera.cpp</c>, <c>Map.cpp</c> at 4df3a5e). The craft carries two vehicles and a soldier,
/// the units numbered vehicles first; one alien stands on the map.
/// </summary>
public sealed class BattleViewTests
{
    private const int FirstVehicle = BattleState.MaximumSoldierId;
    private const int SecondVehicle = BattleState.MaximumSoldierId + 1;

    private static (CampaignState Campaign, BattleState Battle, int Soldier) Playing(BattlePhase phase = BattlePhase.InProgress)
    {
        var (campaign, request) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 0), new("VEHICLE_B", 0)]);
        Started(campaign);
        var battle = Battle(campaign);
        if (phase >= BattlePhase.Inventory)
        {
            Assert.IsType<BriefingConfirmed>(Assert.Single(campaign.Execute(new ConfirmBriefing()).Events));
        }
        if (phase == BattlePhase.InProgress)
        {
            Assert.IsType<BattleFirstTurnStarted>(Assert.Single(campaign.Execute(new StartFirstTurn()).Events));
            // The tests walk the list from its front.
            battle.SelectedUnitId = FirstVehicle;
        }
        return (campaign, battle, Assert.Single(request.Soldiers));
    }

    private static string Refusal(CampaignState campaign, ICampaignCommand command) =>
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(command).Events)).Code;

    private static T Event<T>(CampaignState campaign, ICampaignCommand command) where T : ICampaignEvent
    {
        var events = campaign.Execute(command).Events;
        Assert.Equal("", events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Reason ?? "");
        return Assert.IsType<T>(Assert.Single(events));
    }

    private static IBattleViewQuery View(CampaignState campaign) => campaign.GetQuery<IBattleViewQuery>()!;

    private static BattleUnit Unit(BattleState battle, int id) => battle.Units.Single(unit => unit.Id == id);

    private static BattleUnit Alien(BattleState battle) => battle.Units.First(static unit => unit.Faction == BattleFaction.Hostile);

    [Fact]
    public void ASelectionNeedsTheBriefingToBeOverAndXcomsTurn()
    {
        var (campaign, battle, soldier) = Playing(BattlePhase.Briefing);
        Assert.Equal("battle.selection-unavailable", Refusal(campaign, new SelectUnit(soldier)));
        Assert.Equal("battle.selection-unavailable", Refusal(campaign, new SelectNextUnit(soldier)));
        Assert.Equal("battle.selection-unavailable", Refusal(campaign, new SkipUnit(soldier)));
        Assert.Equal("battle.selection-unavailable", Refusal(campaign, new ReselectSkippedUnit(soldier)));
        Assert.Equal(new BattleSelectionView(null, null, [], -1, battle.Revision), View(campaign).QuerySelection(BattleViewer.LocalPlayer));
        // The pre-battle inventory only walks units that have an inventory (InventoryState::btnNextClick); init moved the selection off the vehicle.
        (campaign, battle, soldier) = Playing(BattlePhase.Inventory);
        Assert.Equal(soldier, battle.SelectedUnitId);
        Assert.Equal("battle.selection-unavailable", Refusal(campaign, new SelectUnit(FirstVehicle)));
        Assert.Equal("battle.selection-unavailable", Refusal(campaign, new SelectNextUnit(soldier)));
        Assert.Equal("battle.selection-unavailable", Refusal(campaign, new SkipUnit(soldier)));
        Assert.Equal("battle.selection-unavailable", Refusal(campaign, new ReselectSkippedUnit(soldier)));
        Assert.Equal(soldier, Event<UnitSelected>(campaign, new SelectNextUnit(soldier, InventoryOnly: true)).UnitId);
        Assert.Null(battle.UndoUnitId);
        Assert.Equal([BattleActionKind.SelectNextInventoryUnit, BattleActionKind.SelectPreviousInventoryUnit], View(campaign).QueryLegalActions(BattleViewer.LocalPlayer, soldier)!.Actions);
        Assert.IsType<BattleFirstTurnStarted>(Assert.Single(campaign.Execute(new StartFirstTurn()).Events));
        // startFirstTurn selects the unit on the craft inventory tile: the large vehicle, placed first.
        Assert.Equal((battle.CraftInventoryTile, FirstVehicle), (Unit(battle, FirstVehicle).Position, battle.SelectedUnitId));
        // The enemy's turn closes it again.
        (campaign, battle, soldier) = Playing();
        battle.Side = BattleFaction.Hostile;
        Assert.Equal("battle.selection-unavailable", Refusal(campaign, new SelectUnit(soldier)));
        Assert.Equal("battle.selection-unavailable", Refusal(campaign, new SkipUnit(soldier)));
        Assert.Empty(View(campaign).QueryLegalActions(BattleViewer.LocalPlayer, soldier)!.Actions);
    }

    [Fact]
    public void ClickingAUnitSelectsItOnlyWhenItIsLiveOnTheMapAndOnTheSideOnTurn()
    {
        var (campaign, battle, soldier) = Playing();
        Assert.Equal(FirstVehicle, battle.SelectedUnitId);
        var revision = battle.Revision;
        Assert.Equal(new UnitSelected(soldier, Saved: true), Event<UnitSelected>(campaign, new SelectUnit(soldier)));
        Assert.Equal(soldier, battle.SelectedUnitId);
        Assert.True(battle.Revision > revision);
        revision = battle.Revision;
        var alien = Alien(battle).Id;
        Unit(battle, SecondVehicle).Status = BattleUnitStatus.Dead;
        Unit(battle, SecondVehicle).Position = null;
        Assert.Equal("battle.selection-unit-unknown", Refusal(campaign, new SelectUnit(alien)));
        Assert.Equal("battle.selection-unit-unknown", Refusal(campaign, new SelectUnit(SecondVehicle)));
        Assert.Equal("battle.selection-unit-unknown", Refusal(campaign, new SelectUnit(-5)));
        Assert.Equal(revision, battle.Revision);
        Assert.Equal(soldier, campaign.Capture().Battle!.SelectedUnitId);
        // A unit that stands on no tile cannot be clicked either.
        Unit(battle, FirstVehicle).Position = null;
        Assert.Equal("battle.selection-unit-unknown", Refusal(campaign, new SelectUnit(FirstVehicle)));
        Assert.True(new SelectUnit(1) is IUnitCommand { UnitId: 1 } && new SelectNextUnit(2) is IUnitCommand && new SkipUnit(3) is IUnitCommand && new ReselectSkippedUnit(4) is IUnitCommand);
    }

    [Fact]
    public void NextAndPreviousWalkTheUnitListWrapAroundAndSkipUnitsThatAreOutOrFlagged()
    {
        var (campaign, battle, soldier) = Playing();
        var order = battle.Units.Where(static unit => unit.Faction == BattleFaction.Player).Select(static unit => unit.Id).ToArray();
        Assert.Equal([FirstVehicle, SecondVehicle, soldier], order);
        // From the first vehicle forward, the next live unit; at the end of the list it wraps to the front.
        Assert.Equal(new UnitSelected(SecondVehicle, true), Event<UnitSelected>(campaign, new SelectNextUnit(FirstVehicle)));
        Assert.Equal(SecondVehicle, battle.SelectedUnitId);
        Assert.Equal(soldier, Event<UnitSelected>(campaign, new SelectNextUnit(SecondVehicle)).UnitId);
        Assert.Equal(FirstVehicle, Event<UnitSelected>(campaign, new SelectNextUnit(soldier)).UnitId);
        Assert.Equal(soldier, Event<UnitSelected>(campaign, new SelectNextUnit(FirstVehicle, Backwards: true)).UnitId);
        Assert.Equal(SecondVehicle, Event<UnitSelected>(campaign, new SelectNextUnit(soldier, Backwards: true)).UnitId);
        // A unit that is out is skipped; so is one flagged not to be reselected (the walk checks it), unless the inventory walks.
        Unit(battle, SecondVehicle).Status = BattleUnitStatus.Unconscious;
        Assert.Equal(soldier, Event<UnitSelected>(campaign, new SelectNextUnit(FirstVehicle)).UnitId);
        Unit(battle, SecondVehicle).Status = BattleUnitStatus.Standing;
        Unit(battle, SecondVehicle).DontReselect = true;
        Assert.Equal(soldier, Event<UnitSelected>(campaign, new SelectNextUnit(FirstVehicle)).UnitId);
        // The inventory walk wants an inventory, which the vehicles' armor has none of, and ignores the reselect flag.
        Unit(battle, soldier).DontReselect = true;
        Assert.Equal(soldier, Event<UnitSelected>(campaign, new SelectNextUnit(FirstVehicle, InventoryOnly: true)).UnitId);
        Assert.Equal(soldier, Event<UnitSelected>(campaign, new SelectNextUnit(SecondVehicle, InventoryOnly: true)).UnitId);
        Unit(battle, soldier).DontReselect = false;
        // Walking back to the unit itself (the others are out) keeps it; flagged, it is dropped from the selection.
        Unit(battle, FirstVehicle).Status = BattleUnitStatus.Dead;
        Unit(battle, SecondVehicle).Status = BattleUnitStatus.Dead;
        Assert.Equal(soldier, Event<UnitSelected>(campaign, new SelectNextUnit(soldier)).UnitId);
        Unit(battle, soldier).DontReselect = true;
        Assert.Equal(new UnitSelected(-1, true), Event<UnitSelected>(campaign, new SelectNextUnit(soldier)));
        Assert.Null(battle.SelectedUnitId);
        Assert.Null(View(campaign).QuerySelection(BattleViewer.LocalPlayer)!.SelectedUnitId);
        // The unit the walk starts from must be a live unit of the side on turn.
        Assert.Equal("battle.selection-unit-unknown", Refusal(campaign, new SelectNextUnit(Alien(battle).Id)));
        Assert.Equal("battle.selection-unit-unknown", Refusal(campaign, new SelectNextUnit(FirstVehicle)));
    }

    [Fact]
    public void SkippingFlagsTheUnitRemembersItAndSelectsTheNextAndReselectingTakesItBack()
    {
        var (campaign, battle, soldier) = Playing();
        Assert.Equal(new UnitSkipped(FirstVehicle, SecondVehicle, true), Event<UnitSkipped>(campaign, new SkipUnit(FirstVehicle)));
        Assert.Equal((SecondVehicle, FirstVehicle, true), (battle.SelectedUnitId, battle.UndoUnitId, Unit(battle, FirstVehicle).DontReselect));
        // The undo unit is the last skipped one; the next skip walks past the flagged first vehicle.
        Assert.Equal(new UnitSkipped(SecondVehicle, soldier, true), Event<UnitSkipped>(campaign, new SkipUnit(SecondVehicle)));
        Assert.Equal((soldier, SecondVehicle), (battle.SelectedUnitId, battle.UndoUnitId));
        Assert.Equal(new UnitSkipped(soldier, -1, true), Event<UnitSkipped>(campaign, new SkipUnit(soldier)));
        Assert.Null(battle.SelectedUnitId);
        Assert.Equal(soldier, battle.UndoUnitId);
        var view = View(campaign).QuerySelection(BattleViewer.LocalPlayer)!;
        Assert.Equal((null, soldier, 0), (view.SelectedUnitId, view.UndoUnitId, view.WritingSlot));
        Assert.Empty(view.SelectableUnitIds);
        // Reselecting takes the undo unit back, allows it again and forgets it.
        Assert.Equal(new UnitSelected(soldier, true), Event<UnitSelected>(campaign, new ReselectSkippedUnit(soldier)));
        Assert.Equal((soldier, (int?)null, false), (battle.SelectedUnitId, battle.UndoUnitId, Unit(battle, soldier).DontReselect));
        Assert.Equal("battle.selection-nothing-to-undo", Refusal(campaign, new ReselectSkippedUnit(soldier)));
        // An undo unit that went out cannot come back.
        Event<UnitSkipped>(campaign, new SkipUnit(soldier));
        Unit(battle, soldier).Status = BattleUnitStatus.Dead;
        Assert.Equal("battle.selection-nothing-to-undo", Refusal(campaign, new ReselectSkippedUnit(soldier)));
    }

    [Fact]
    public void SkippingByDistanceTakesTheNearestSelectableUnitAndTiesGoToTheListOrder()
    {
        var (campaign, battle, soldier) = Playing();
        var first = Unit(battle, FirstVehicle);
        var second = Unit(battle, SecondVehicle);
        var third = Unit(battle, soldier);
        (first.Position, second.Position, third.Position) = (new BattlePosition(1, 1, 0), new BattlePosition(9, 9, 0), new BattlePosition(2, 1, 1));
        // Squared distances from the first vehicle: 2 for the soldier (one tile and one level), 128 for the second vehicle.
        Assert.Equal(new UnitSkipped(FirstVehicle, soldier, true), Event<UnitSkipped>(campaign, new SkipUnit(FirstVehicle, ByDistance: true)));
        Assert.Equal((soldier, FirstVehicle, true), (battle.SelectedUnitId, battle.UndoUnitId, first.DontReselect));
        // The skipped first vehicle is no candidate any more; from the soldier the second vehicle is the only one left.
        Assert.Equal(SecondVehicle, Event<UnitSkipped>(campaign, new SkipUnit(soldier, ByDistance: true)).SelectedUnitId);
        // Ties: both at the same distance, the list order decides (std::sort leaves it open in the reference).
        (first.DontReselect, second.DontReselect, third.DontReselect) = (false, false, false);
        (first.Position, second.Position, third.Position) = (new BattlePosition(5, 5, 0), new BattlePosition(5, 7, 0), new BattlePosition(5, 3, 0));
        Assert.Equal(FirstVehicle, Event<UnitSkipped>(campaign, new SkipUnit(SecondVehicle, ByDistance: true)).SelectedUnitId);
        // Nothing else to select leaves no selection, as the reference drops it first.
        first.DontReselect = third.DontReselect = true;
        Assert.Equal(-1, Event<UnitSkipped>(campaign, new SkipUnit(SecondVehicle, ByDistance: true)).SelectedUnitId);
        Assert.Null(battle.SelectedUnitId);
    }

    [Fact]
    public void OnlyTheSideOwningSlotWritesTheSavedSelectionAndEveryoneKeepsTheirOwn()
    {
        var (campaign, battle, soldier) = Playing();
        Unit(battle, SecondVehicle).Controller = BattleController.Human(1);
        var view = View(campaign);
        Assert.Equal(0, view.QuerySelection(BattleViewer.LocalPlayer)!.WritingSlot);
        // Player 1's unit: the selection is not written, the event says so, and the unit's own flags still change.
        Assert.Equal(new UnitSelected(SecondVehicle, Saved: false), Event<UnitSelected>(campaign, new SelectUnit(SecondVehicle)));
        Assert.Equal(FirstVehicle, battle.SelectedUnitId);
        var skipped = Event<UnitSkipped>(campaign, new SkipUnit(SecondVehicle));
        Assert.Equal((soldier, false), (skipped.SelectedUnitId, skipped.Saved));
        Assert.Equal((FirstVehicle, (int?)null, true), (battle.SelectedUnitId, battle.UndoUnitId, Unit(battle, SecondVehicle).DontReselect));
        Assert.Equal(new UnitSelected(soldier, false), Event<UnitSelected>(campaign, new SelectNextUnit(SecondVehicle)));
        // Slot 0's units write.
        Assert.True(Event<UnitSelected>(campaign, new SelectUnit(soldier)).Saved);
        Assert.Equal(soldier, battle.SelectedUnitId);
        // The session authorizes a unit command by the controller: player 1 cannot select player 0's unit.
        var session = new CampaignSession(campaign, campaign)
        {
            Controllers = new BattleUnitControllers(campaign.GetQuery<IBattleQuery>()!),
        };
        using var other = session.Attach(new PlayerId(1));
        Assert.Equal("session.unit-not-controlled", Assert.IsType<CampaignActionBlocked>(Assert.Single(other.Execute(new SelectUnit(soldier)).Events)).Code);
        Assert.IsType<UnitSelected>(Assert.Single(other.Execute(new SelectUnit(SecondVehicle)).Events));
    }

    [Fact]
    public void AViewerSeesOnlyTheDiscoveredPartsItsOwnUnitsAndTheUnitsItSpotted()
    {
        var (campaign, battle, soldier) = Playing();
        var view = View(campaign);
        var map = battle.Map;
        var alien = Alien(battle);
        var at = alien.Position!.Value;
        var tile = map.TileIndex(at);
        // An undiscovered tile shows nothing; the floor discovered shows the floor and the walls with it.
        foreach (var part in Enum.GetValues<BattleTilePart>()) map.SetDiscovered(tile, part, false);
        var level = view.QueryLevel(BattleViewer.LocalPlayer, at.Z)!;
        Assert.DoesNotContain(level.Tiles, shown => shown.Position == at);
        Assert.Equal((map.Width, map.Length, map.Height, battle.Revision), (level.Width, level.Length, level.LevelCount, level.Revision));
        // Every listed part is a discovered one, and no tile is listed twice.
        foreach (var shown in level.Tiles)
            foreach (var part in shown.Parts)
                Assert.True(map.IsDiscovered(map.TileIndex(shown.Position), part.Part));
        Assert.Equal(level.Tiles.Count, level.Tiles.Select(static shown => shown.Position).Distinct().Count());
        Assert.Contains(level.Tiles, shown => shown.UnitId == soldier);
        var withFloor = level.Tiles.First(shown => shown.Parts.Any(static part => part.Part == BattleTilePart.Floor));
        var index = map.TileIndex(withFloor.Position);
        var floor = map.Part(index, BattleTilePart.Floor);
        Assert.Contains(new BattleTilePartView(BattleTilePart.Floor, map.DataSets[floor.Set].Name, floor.Id), withFloor.Parts);
        // A tile whose floor is hidden hides its smoke, fire and items.
        map.SetSmoke(index, 7);
        map.SetFire(index, 3);
        Assert.Equal((7, 3), (view.QueryLevel(BattleViewer.LocalPlayer, withFloor.Position.Z)!.Tiles.Single(shown => shown.Position == withFloor.Position).Smoke,
            view.QueryLevel(BattleViewer.LocalPlayer, withFloor.Position.Z)!.Tiles.Single(shown => shown.Position == withFloor.Position).Fire));
        map.SetDiscovered(index, BattleTilePart.Floor, false);
        map.RestoreDiscovered(index, BattleTilePart.WestWall, false);
        map.RestoreDiscovered(index, BattleTilePart.NorthWall, false);
        map.RestoreDiscovered(index, BattleTilePart.ObjectPart, false);
        Assert.DoesNotContain(view.QueryLevel(BattleViewer.LocalPlayer, withFloor.Position.Z)!.Tiles, shown => shown.Position == withFloor.Position && (shown.Smoke != 0 || shown.Fire != 0));
        // An alien that no one spotted is neither listed nor on its tile; spotted, it is both.
        alien.SetSpottedBy(BattleFaction.Player, false);
        Assert.DoesNotContain(view.QueryLevel(BattleViewer.LocalPlayer, at.Z)!.Tiles, shown => shown.UnitId == alien.Id);
        Assert.DoesNotContain(campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer)!.Units, unit => unit.Id == alien.Id);
        battle.SetSpotted(alien, BattleFaction.Player, true);
        map.SetDiscovered(tile, BattleTilePart.Floor, true);
        Assert.Contains(view.QueryLevel(BattleViewer.LocalPlayer, at.Z)!.Tiles, shown => shown.UnitId == alien.Id);
        // Levels outside the map have no view.
        Assert.Null(view.QueryLevel(BattleViewer.LocalPlayer, -1));
        Assert.Null(view.QueryLevel(BattleViewer.LocalPlayer, map.Height));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.QueryLevel(new BattleViewer((BattleFaction)9), 0));
    }

    [Fact]
    public void AnotherSideSeesTheTilesItExploredAndItsGroundItemsOnlyThere()
    {
        var (campaign, battle, soldier) = Playing();
        var view = View(campaign);
        var map = battle.Map;
        var hostile = new BattleViewer(BattleFaction.Hostile);
        // Nothing is explored by the aliens yet: no tile, and the X-COM units that nobody spotted stay hidden.
        var level = view.QueryLevel(hostile, 0)!;
        Assert.All(level.Tiles, shown =>
        {
            Assert.Empty(shown.Parts);
            Assert.True(Unit(battle, shown.UnitId!.Value).Faction == BattleFaction.Hostile);
        });
        Assert.DoesNotContain(campaign.GetQuery<IBattleQuery>()!.QueryBattle(hostile)!.Units, unit => unit.Id == soldier);
        var seen = Unit(battle, soldier).Position!.Value;
        map.SetLastExplored(map.TileIndex(seen), BattleFaction.Hostile, 1);
        var explored = view.QueryLevel(hostile, seen.Z)!.Tiles.Single(shown => shown.Position == seen);
        Assert.Equal(seen, explored.Position);
        var seenIndex = map.TileIndex(seen);
        Assert.Equal(Enum.GetValues<BattleTilePart>().Count(part => map.Part(seenIndex, part).Id >= 0), explored.Parts.Count);
        Assert.Null(explored.UnitId);
        battle.SetSpotted(Unit(battle, soldier), BattleFaction.Hostile, true);
        Assert.Equal(soldier, view.QueryLevel(hostile, seen.Z)!.Tiles.Single(shown => shown.Position == seen).UnitId);
        // The player's explored turn does not show terrain to the player (its flags do).
        Assert.Null(view.QuerySelection(hostile)!.SelectedUnitId);
    }

    [Fact]
    public void TheUnitInfoShowsABarsHandsAndSpottedHostilesOfTheViewersOwnLiveUnitsOnly()
    {
        var (campaign, battle, soldier) = Playing();
        var view = View(campaign);
        var unit = Unit(battle, soldier);
        var info = view.QueryUnitInfo(BattleViewer.LocalPlayer, soldier)!;
        Assert.Equal((soldier, (int?)soldier, unit.UnitType, unit.TimeUnits, (int)unit.Stats.TimeUnits, unit.Energy, (int)unit.Stats.Stamina, unit.Health, (int)unit.Stats.Health,
                unit.StunLevel, unit.Morale, unit.Mana, (int)unit.Stats.Mana, unit.Kneeled),
            (info.UnitId, info.SoldierId, info.UnitType, info.TimeUnits, info.MaxTimeUnits, info.Energy, info.MaxEnergy, info.Health, info.MaxHealth,
                info.Stun, info.Morale, info.Mana, info.MaxMana, info.Kneeled));
        var hand = battle.Items.Where(item => item.OwnerUnitId == soldier && item.Slot == "STR_RIGHT_HAND").Select(static item => new BattleTileItemView(item.Id, item.ItemType)).FirstOrDefault();
        Assert.Equal(hand == default ? null : hand, info.RightHand);
        // Hostile units appear once the viewer's side spotted them, and not after they are out.
        var alien = Alien(battle);
        Assert.Equal(alien.IsSpottedBy(BattleFaction.Player) ? [alien.Id] : [], info.VisibleHostileUnitIds);
        battle.SetSpotted(alien, BattleFaction.Player, true);
        Assert.Equal([alien.Id], view.QueryUnitInfo(BattleViewer.LocalPlayer, soldier)!.VisibleHostileUnitIds);
        alien.Status = BattleUnitStatus.Dead;
        Assert.Empty(view.QueryUnitInfo(BattleViewer.LocalPlayer, soldier)!.VisibleHostileUnitIds);
        // Not another side's unit, not an out unit.
        Assert.Null(view.QueryUnitInfo(BattleViewer.LocalPlayer, alien.Id));
        Assert.Null(view.QueryUnitInfo(new BattleViewer(BattleFaction.Hostile), soldier));
        Assert.Null(view.QueryUnitInfo(BattleViewer.LocalPlayer, 424242));
        unit.Status = BattleUnitStatus.Unconscious;
        Assert.Null(view.QueryUnitInfo(BattleViewer.LocalPlayer, soldier));
    }

    [Fact]
    public void OnlyTheActionsTheRulesAcceptAreListedForTheViewersOwnLiveUnit()
    {
        var (campaign, battle, soldier) = Playing();
        var view = View(campaign);
        var all = new[] { BattleActionKind.SelectNextInventoryUnit, BattleActionKind.SelectPreviousInventoryUnit, BattleActionKind.SelectNextUnit, BattleActionKind.SelectPreviousUnit, BattleActionKind.SkipUnit, BattleActionKind.SkipUnitByDistance };
        var actions = view.QueryLegalActions(BattleViewer.LocalPlayer, soldier)!;
        Assert.Equal(all.Append(BattleActionKind.OpenInventory), actions.Actions);
        Assert.Equal([FirstVehicle, SecondVehicle], actions.SelectableUnitIds);
        // The undo unit can be taken back only while another unit is selected.
        Event<UnitSkipped>(campaign, new SkipUnit(FirstVehicle));
        Assert.Contains(BattleActionKind.ReselectSkippedUnit, view.QueryLegalActions(BattleViewer.LocalPlayer, soldier)!.Actions);
        // It is accepted when the undo unit is the selected one too.
        Assert.Contains(BattleActionKind.ReselectSkippedUnit, view.QueryLegalActions(BattleViewer.LocalPlayer, FirstVehicle)!.Actions);
        // An armor without an inventory cannot open one.
        var armorLess = Unit(battle, SecondVehicle);
        Assert.Equal(battle.InventoryAvailable(campaign.Content.RuntimeRules, armorLess.Id, out _) is null,
            view.QueryLegalActions(BattleViewer.LocalPlayer, armorLess.Id)!.Actions.Contains(BattleActionKind.OpenInventory));
        // Not another side's unit, not an out unit, not before the first turn, not on the enemy's turn.
        Assert.Null(view.QueryLegalActions(BattleViewer.LocalPlayer, Alien(battle).Id));
        Assert.Null(view.QueryLegalActions(new BattleViewer(BattleFaction.Hostile), soldier));
        Assert.Empty(view.QueryLegalActions(new BattleViewer(BattleFaction.Hostile), Alien(battle).Id)!.Actions);
        Unit(battle, SecondVehicle).Status = BattleUnitStatus.Dead;
        Assert.Null(view.QueryLegalActions(BattleViewer.LocalPlayer, SecondVehicle));
        var (early, _, sol) = Playing(BattlePhase.Inventory);
        Assert.Equal([BattleActionKind.SelectNextInventoryUnit, BattleActionKind.SelectPreviousInventoryUnit], View(early).QueryLegalActions(BattleViewer.LocalPlayer, sol)!.Actions);
    }

    [Fact]
    public void TheCameraStaysInsideTheMapAndFollowsAPosition()
    {
        var camera = new BattleCamera();
        camera.SetMap(30, 20, 3);
        Assert.True(camera.Up());
        Assert.True(camera.Up());
        Assert.False(camera.Up());
        Assert.Equal(2, camera.ViewLevel);
        Assert.True(camera.Down());
        camera.SetViewLevel(0);
        Assert.False(camera.Down());
        camera.SetViewLevel(99);
        Assert.Equal(2, camera.ViewLevel);
        camera.SetViewLevel(-4);
        Assert.Equal(0, camera.ViewLevel);
        // The center may lie one tile outside the map; the view level follows the position's level.
        camera.CenterOn(new BattlePosition(-9, 50, 1));
        Assert.Equal((new BattlePosition(-1, 20, 1), 1), (camera.Center, camera.ViewLevel));
        camera.CenterOn(new BattlePosition(10, 5, 7));
        Assert.Equal((new BattlePosition(10, 5, 7), 2), (camera.Center, camera.ViewLevel));
        camera.SetMap(8, 8, 1);
        Assert.Equal((new BattlePosition(8, 5, 7), 0), (camera.Center, camera.ViewLevel));
        Assert.True(camera.ToggleShowAllLayers());
        Assert.False(camera.ToggleShowAllLayers());
        Assert.Throws<ArgumentOutOfRangeException>(() => camera.SetMap(0, 8, 1));
    }

    [Fact]
    public void TheViewModelReadsTheQueriesSelectsThroughCommandsAndKeepsTheCameraWithinTheMap()
    {
        var (campaign, battle, soldier) = Playing();
        var model = new BattleViewModel(campaign, campaign.GetQuery<IBattleQuery>()!, View(campaign), BattleViewer.LocalPlayer);
        Assert.True(model.Refresh());
        Assert.False(model.Refresh());
        Assert.Equal((FirstVehicle, battle.Revision), (model.SelectedUnitId, model.Overview!.Revision));
        Assert.Equal((0, battle.Width, battle.Height), (model.Level!.Level, model.Level.Width, model.Level.LevelCount));
        // Next selects through the command, centers the camera on the unit, and the new state is read back.
        Assert.True(model.Next());
        Assert.Equal(SecondVehicle, model.SelectedUnitId);
        Assert.Equal(Unit(battle, SecondVehicle).Position, model.Camera.Center with { Z = Unit(battle, SecondVehicle).Position!.Value.Z });
        Assert.True(model.Next(backwards: true, keepCamera: true));
        Assert.Equal(FirstVehicle, model.SelectedUnitId);
        Assert.True(model.Skip());
        Assert.Equal((SecondVehicle, FirstVehicle), (model.SelectedUnitId, model.Selection!.UndoUnitId));
        Assert.True(model.ReselectSkipped());
        Assert.Equal(FirstVehicle, model.SelectedUnitId);
        Assert.False(model.ReselectSkipped());
        // A click that the rules refuse changes nothing and leaves the code.
        Assert.False(model.Select(Alien(battle).Id));
        Assert.Equal(("battle.selection-unit-unknown", FirstVehicle), (model.Message, model.SelectedUnitId));
        Assert.True(model.Select(soldier));
        Assert.Equal(soldier, model.SelectedUnitId);
        Assert.Equal(soldier, model.SelectedUnit!.UnitId);
        Assert.Contains(BattleActionKind.SkipUnit, model.Actions!.Actions);
        // The level buttons stop at the map's levels; the level read follows the view.
        model.Camera.SetViewLevel(0);
        model.Refresh();
        Assert.True(model.LevelUp());
        Assert.Equal(1, model.Level!.Level);
        Assert.False(model.LevelUp());
        Assert.True(model.LevelDown());
        Assert.False(model.LevelDown());
        Assert.Equal(0, model.Level!.Level);
        // A selection the battle no longer allows follows the saved one; a battle that is gone clears the model.
        Unit(battle, soldier).Status = BattleUnitStatus.Dead;
        Assert.True(model.Select(FirstVehicle));
        Assert.True(model.CenterOnSelection());
        Assert.Equal(FirstVehicle, model.SelectedUnitId);
    }

    [Fact]
    public void TheViewModelStartsFromTheFirstSelectableUnitWithoutASelectionAndRefusesTheButtonsOnTheEnemysTurn()
    {
        var (campaign, battle, soldier) = Playing();
        var model = new BattleViewModel(campaign, campaign.GetQuery<IBattleQuery>()!, View(campaign), BattleViewer.LocalPlayer);
        battle.SelectedUnitId = null;
        Assert.True(model.Refresh());
        Assert.Null(model.SelectedUnitId);
        Assert.True(model.Next());
        Assert.Equal(FirstVehicle, model.SelectedUnitId);
        Assert.Equal(FirstVehicle, battle.SelectedUnitId);
        battle.Side = BattleFaction.Hostile;
        model = new BattleViewModel(campaign, campaign.GetQuery<IBattleQuery>()!, View(campaign), BattleViewer.LocalPlayer);
        model.Refresh();
        Assert.False(model.LevelUp());
        Assert.False(model.Next());
        Assert.Equal("battle.selection-unavailable", model.Message);
        Assert.NotEqual(soldier, model.SelectedUnitId);
        // A model over nothing reads nothing.
        var empty = new BattleViewModel(campaign, new NoBattle(), View(campaign), BattleViewer.LocalPlayer);
        Assert.False(empty.Refresh());
        Assert.False(empty.CenterOnSelection());
    }

    [Fact]
    public void TheViewModelFallsBackToTheSavedSelectionFollowsTheBattleAwayAndStartsFromTheEndGoingBack()
    {
        var (campaign, battle, soldier) = Playing();
        var query = new Switch(campaign.GetQuery<IBattleQuery>()!);
        var model = new BattleViewModel(campaign, query, View(campaign), BattleViewer.LocalPlayer);
        Assert.True(model.Select(soldier));
        // The client's unit goes out; the next refresh follows the saved selection.
        Unit(battle, soldier).Status = BattleUnitStatus.Dead;
        Event<UnitSelected>(campaign, new SelectUnit(SecondVehicle));
        model.Refresh();
        Assert.Equal(SecondVehicle, model.SelectedUnitId);
        // With no selection, going back starts at the end of the list; the front going forward.
        battle.SelectedUnitId = null;
        Unit(battle, soldier).Status = BattleUnitStatus.Standing;
        Event<UnitSelected>(campaign, new SelectNextUnit(FirstVehicle, InventoryOnly: true));
        battle.SelectedUnitId = null;
        model = new BattleViewModel(campaign, query, View(campaign), BattleViewer.LocalPlayer);
        model.Refresh();
        Assert.True(model.Next(backwards: true));
        Assert.Equal(soldier, model.SelectedUnitId);
        // A center button needs X-COM's turn; a battle that is gone clears everything.
        Assert.True(model.CenterOnSelection());
        battle.Side = BattleFaction.Hostile;
        battle.SetSpotted(Alien(battle), BattleFaction.Neutral, true);
        model.Refresh();
        Assert.False(model.CenterOnSelection());
        query.Off = true;
        Assert.True(model.Refresh());
        Assert.Equal((null, null, null), (model.Overview, model.SelectedUnitId, model.Level));
        Assert.False(model.Refresh());
    }

    [Fact]
    public void GroundItemsShowOnlyOnDiscoveredFloorsAndANoHumanSideWritesNoSavedSelection()
    {
        var (campaign, battle, soldier) = Playing();
        var view = View(campaign);
        var map = battle.Map;
        var shown = view.QueryLevel(BattleViewer.LocalPlayer, 0)!.Tiles.First(tile => tile.Parts.Any(static part => part.Part == BattleTilePart.Floor) && tile.UnitId is null);
        var index = map.TileIndex(shown.Position);
        battle.AddItemOnTile(new BattleItem(battle.TakeItemId(), "GRENADE"), shown.Position);
        Assert.Equal(["GRENADE"], view.QueryLevel(BattleViewer.LocalPlayer, 0)!.Tiles.Single(tile => tile.Position == shown.Position).Items.Select(static item => item.ItemType));
        map.RestoreDiscovered(index, BattleTilePart.Floor, false);
        Assert.All(view.QueryLevel(BattleViewer.LocalPlayer, 0)!.Tiles.Where(tile => tile.Position == shown.Position), tile => Assert.Empty(tile.Items));
        // Another side sees them on a tile it explored.
        map.SetLastExplored(index, BattleFaction.Hostile, 1);
        Assert.Single(view.QueryLevel(new BattleViewer(BattleFaction.Hostile), 0)!.Tiles.Single(tile => tile.Position == shown.Position).Items);

        // No human controls the side: nothing is written, the events say so.
        foreach (var unit in battle.Units) unit.Controller = BattleController.Ai;
        Assert.Equal(-1, view.QuerySelection(BattleViewer.LocalPlayer)!.WritingSlot);
        Assert.Equal(new UnitSelected(soldier, Saved: false), Event<UnitSelected>(campaign, new SelectUnit(soldier)));
        Assert.Equal(FirstVehicle, battle.SelectedUnitId);
        // Unknown units, and an undo unit that is not the last skipped one, are refused and change nothing.
        Assert.Equal("battle.selection-unit-unknown", Refusal(campaign, new SkipUnit(Alien(battle).Id)));
        Assert.Equal("battle.selection-unit-unknown", Refusal(campaign, new SkipUnit(-3)));
        Assert.Equal("battle.selection-nothing-to-undo", Refusal(campaign, new ReselectSkippedUnit(soldier)));
        Assert.Null(battle.UndoUnitId);
    }

    private sealed class Switch(IBattleQuery inner) : IBattleQuery
    {
        public bool Off { get; set; }

        public BattleOverview? QueryBattle(BattleViewer viewer) => Off ? null : inner.QueryBattle(viewer);
    }

    private sealed class NoBattle : IBattleQuery
    {
        public BattleOverview? QueryBattle(BattleViewer viewer) => null;
    }
}
