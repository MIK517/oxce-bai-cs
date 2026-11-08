using Oxce.Engine;
using Oxce.Engine.Battlescape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Xunit;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleStartTests;
using static Oxce.CompatibilityTests.BattleWalkTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S10: the battlescape client's movement. The queries (the previewed path with its time unit and energy markers and the
/// reachable tiles, <c>Pathfinding::refreshPath</c> and <c>findReachable</c>) and the commands the screen sends (the tile click, the right click,
/// the kneel, up and down keys and the visible-unit buttons), driven by scripted input on the walking arena of <see cref="BattleWalkTests"/>.
/// </summary>
public sealed class BattleClientMovementTests
{
    private const uint Tab = 9;

    internal static (BattleUiContext Context, BattlescapeClient Screen, BattleViewModel Model) Client(Arena arena)
    {
        var campaign = arena.Campaign;
        var context = new BattleUiContext(campaign, campaign.GetQuery<IBattleQuery>()!, campaign.GetQuery<IBattleViewQuery>()!,
            campaign.GetQuery<IBattleInventoryQuery>()!, campaign.GetQuery<IBattleBriefingQuery>()!, BattleViewer.LocalPlayer, new ScreenNavigator());
        Assert.True(context.Open());
        var screen = Assert.IsType<BattlescapeClient>(context.Screens.Top);
        Assert.True(screen.Model.Select(arena.Soldier.Id));
        screen.Model.Camera.SetViewLevel(0);
        screen.Model.Refresh();
        return (context, screen, screen.Model);
    }

    /// <summary>A change the rules announce by their revision (a selection of the unit already selected), so the client reads again.</summary>
    private static void Changed(Arena arena) => Assert.DoesNotContain(arena.Campaign.Execute(new SelectUnit(arena.Soldier.Id)).Events, static @event => @event is CampaignActionBlocked);

    private static IBattleViewQuery Queries(Arena arena) => arena.Campaign.GetQuery<IBattleViewQuery>()!;

    internal static GameInputEvent Key(uint key, InputKeyModifiers modifiers = InputKeyModifiers.None) =>
        GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, key, modifiers);

    internal static GameInputEvent ClickTile(BattlescapeClient screen, int x, int y, byte button = 1, InputKeyModifiers modifiers = InputKeyModifiers.None)
    {
        var size = screen.TileSize;
        return GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x * size + size / 2, y * size + size / 2, button, 1) with { Modifiers = modifiers };
    }

    internal static GameInputEvent Over(BattlescapeClient screen, int x, int y)
    {
        var size = screen.TileSize;
        return GameInputEvent.PointerMotion(0, 0, x * size + size / 2, y * size + size / 2, 0, 0, 0);
    }

    /// <summary>Ticks until the replay is over, as the loop would, so the next input is not dropped as the busy game drops it.</summary>
    internal static void Play(BattlescapeClient screen)
    {
        while (screen.Replaying) Tick(screen);
    }

    internal static void Tick(BattlescapeClient screen) => screen.Tick(TimeSpan.FromMilliseconds(16));

    internal static byte Pixel(BattlescapeClient screen, int x, int y, int dx = 0)
    {
        var size = screen.TileSize;
        return screen.Frame.GetPixel(x * size + size / 2 + dx, y * size + size / 2);
    }

    // ---- the path and reachable queries ----

    [Fact]
    public void ThePathQueryMarksEachStepWithTheTimeUnitsAndEnergyTheUnitWouldHaveLeft()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var view = Queries(arena).QueryPath(BattleViewer.LocalPlayer, soldier.Id, At(13, 10))!;
        Assert.Equal((3, 12, 6, ""), (view.Steps.Count, view.TimeUnitsCost, view.EnergyCost, view.NoPathReason));
        Assert.Equal([At(11, 10), At(12, 10), At(13, 10)], view.Steps.Select(static step => step.Position));
        Assert.Equal([soldier.TimeUnits - 4, soldier.TimeUnits - 8, soldier.TimeUnits - 12], view.Steps.Select(static step => step.TimeUnitsLeft));
        Assert.Equal([soldier.Energy - 2, soldier.Energy - 4, soldier.Energy - 6], view.Steps.Select(static step => step.EnergyLeft));
        Assert.All(view.Steps, static step => Assert.Equal(BattlePathMarker.Affordable, step.Marker));
        Assert.All(view.Steps, static step => Assert.Equal(2, step.Direction));
        // The query changes nothing: the walk it previewed takes exactly that path.
        var walked = Events(arena.Campaign.Execute(new WalkUnit(soldier.Id, At(13, 10))));
        Assert.Equal(view.TimeUnitsCost, End(walked).TimeUnitsSpent);
    }

    [Fact]
    public void AStepTheUnitCannotPayIsRedAndTheTimeUnitsLeftStopAtZero()
    {
        var arena = Make();
        arena.Soldier.TimeUnits = 6;
        var view = Queries(arena).QueryPath(BattleViewer.LocalPlayer, arena.Soldier.Id, At(13, 10))!;
        // The first step can be paid, but kneeling is reserved for (4 time units) and the step leaves only 2: yellow, as the reference's justChecking reserve.
        Assert.Equal([BattlePathMarker.ReservedTimeUnits, BattlePathMarker.Unaffordable, BattlePathMarker.Unaffordable], view.Steps.Select(static step => step.Marker));
        Assert.Equal([2, 0, 0], view.Steps.Select(static step => step.TimeUnitsLeft));
        Assert.Equal(12, view.TimeUnitsCost);
        arena.Soldier.TimeUnits = 100;
        arena.Soldier.Energy = 3;
        var tired = Queries(arena).QueryPath(BattleViewer.LocalPlayer, arena.Soldier.Id, At(13, 10))!;
        Assert.Equal([BattlePathMarker.Affordable, BattlePathMarker.Unaffordable, BattlePathMarker.Unaffordable], tired.Steps.Select(static step => step.Marker));
        Assert.Equal([1, 0, 0], tired.Steps.Select(static step => step.EnergyLeft));
    }

    [Fact]
    public void AStepThatKeepsTheUnitFromItsReservedTimeUnitsIsYellowAndWithoutAReserveTheAutoShotIsChecked()
    {
        foreach (var reserved in new[] { BattleActionType.None, BattleActionType.SnapShot })
        {
            var arena = Make();
            arena.Stand(arena.Alien, 12, 4, 6);
            arena.See();
            arena.Battle.KneelReserved = false;
            arena.Battle.TimeUnitsReserved = (int)reserved;
            // The soldier's gun has no auto shot, so the reserve falls back to its snap shot (20 percent of 60: 12). With 40 time units, eight steps of 4 leave 8, below it.
            arena.Soldier.TimeUnits = 40;
            var view = Queries(arena).QueryPath(BattleViewer.LocalPlayer, arena.Soldier.Id, At(18, 10))!;
            Assert.Equal(8, view.Steps.Count);
            Assert.Equal(BattlePathMarker.Affordable, view.Steps[6].Marker);
            Assert.Equal(BattlePathMarker.ReservedTimeUnits, view.Steps[7].Marker);
            Assert.Equal(40 - 32, view.Steps[7].TimeUnitsLeft);
        }
        // Nothing to reserve for: no weapon, so no step is yellow.
        var unarmed = Make();
        unarmed.Stand(unarmed.Alien, 12, 4, 6);
        unarmed.See();
        unarmed.Battle.KneelReserved = false;
        Assert.All(Queries(unarmed).QueryPath(BattleViewer.LocalPlayer, unarmed.Soldier.Id, At(18, 10))!.Steps, static step => Assert.Equal(BattlePathMarker.Affordable, step.Marker));
    }

    [Fact]
    public void TheKneelingUpAndTheTurnBeforeTheFirstStepArePartOfThePreviewedCost()
    {
        var kneeling = Make();
        kneeling.Soldier.Kneel(true);
        var stands = Queries(kneeling).QueryPath(BattleViewer.LocalPlayer, kneeling.Soldier.Id, At(11, 10))!;
        Assert.Equal(BattleUnit.KneelUpCost + 4, stands.TimeUnitsCost);
        Assert.Equal(kneeling.Soldier.TimeUnits - BattleUnit.KneelUpCost - 4, stands.Steps[0].TimeUnitsLeft);
        var turning = Make(suitExtra: "    turnCost: 2\n    turnBeforeFirstStep: true\n");
        var south = Queries(turning).QueryPath(BattleViewer.LocalPlayer, turning.Soldier.Id, At(12, 12))!;
        // One eighth from east to south east at 2, then two diagonal steps of 6.
        Assert.Equal(2 + 12, south.TimeUnitsCost);
        Assert.Equal(turning.Soldier.TimeUnits - 2 - 6, south.Steps[0].TimeUnitsLeft);
        var free = Make(suitExtra: "    turnCost: 2\n");
        Assert.Equal(12, Queries(free).QueryPath(BattleViewer.LocalPlayer, free.Soldier.Id, At(12, 12))!.TimeUnitsCost);
    }

    [Fact]
    public void ThePathQueryIsScopedToTheViewersUnitOnTurnAndToTilesTheViewerHasDiscovered()
    {
        var arena = Make();
        var query = Queries(arena);
        var soldier = arena.Soldier;
        // The viewer's side, its own unit, a tile on the map that it has discovered.
        Assert.Null(query.QueryPath(new BattleViewer(BattleFaction.Hostile), soldier.Id, At(13, 10)));
        Assert.Null(query.QueryPath(BattleViewer.LocalPlayer, arena.Alien.Id, At(13, 10)));
        Assert.Null(query.QueryPath(BattleViewer.LocalPlayer, 9999, At(13, 10)));
        Assert.Null(query.QueryPath(BattleViewer.LocalPlayer, soldier.Id, At(-1, 10)));
        Assert.Null(query.QueryPath(BattleViewer.LocalPlayer, soldier.Id, At(13, 10, 9)));
        var hidden = arena.Tile(2, 2);
        arena.Map.SetDiscovered(hidden, BattleTilePart.Floor, false, BattleFaction.Player);
        Assert.Null(query.QueryPath(BattleViewer.LocalPlayer, soldier.Id, At(2, 2)));
        arena.Map.SetDiscovered(hidden, BattleTilePart.Floor, true, BattleFaction.Player);
        // A route over a tile the viewer has not discovered shows how passable it is: no preview.
        var onTheWay = arena.Tile(12, 10);
        arena.Map.SetDiscovered(onTheWay, BattleTilePart.Floor, false, BattleFaction.Player);
        Assert.Null(query.QueryPath(BattleViewer.LocalPlayer, soldier.Id, At(13, 10)));
        arena.Map.SetDiscovered(onTheWay, BattleTilePart.Floor, true, BattleFaction.Player);
        Assert.NotNull(query.QueryPath(BattleViewer.LocalPlayer, soldier.Id, At(13, 10)));
        // Not X-COM's turn: nothing to preview.
        arena.Battle.Side = BattleFaction.Hostile;
        Assert.Null(query.QueryPath(BattleViewer.LocalPlayer, soldier.Id, At(13, 10)));
    }

    [Fact]
    public void ANoPathViewNamesItsReasonAndAnArmorThatCannotMoveOrRunHasNone()
    {
        var arena = Make();
        var query = Queries(arena);
        var soldier = arena.Soldier;
        // The soldier's own tile is no walk.
        Assert.Equal(("battle.walk-no-path", 0), (query.QueryPath(BattleViewer.LocalPlayer, soldier.Id, At(10, 10))!.NoPathReason, query.QueryPath(BattleViewer.LocalPlayer, soldier.Id, At(10, 10))!.Steps.Count));
        var refused = Make(suitExtra: "    allowsRunning: false\n");
        Assert.Equal("battle.walk-move-not-allowed", Queries(refused).QueryPath(BattleViewer.LocalPlayer, refused.Soldier.Id, At(13, 10), BattleMoveType.Run)!.NoPathReason);
        var runner = Make(suitExtra: "    allowsRunning: true\n");
        var running = Queries(runner).QueryPath(BattleViewer.LocalPlayer, runner.Soldier.Id, At(13, 10), BattleMoveType.Run)!;
        // A run costs three quarters of the time units: 3 per step.
        Assert.Equal((3, 9), (running.Steps.Count, running.TimeUnitsCost));
        var still = Make(suitExtra: "    allowsMoving: false\n");
        Assert.Equal("battle.walk-not-allowed", Queries(still).QueryPath(BattleViewer.LocalPlayer, still.Soldier.Id, At(13, 10))!.NoPathReason);
        Assert.Null(Queries(still).QueryReachable(BattleViewer.LocalPlayer, still.Soldier.Id));
    }

    [Fact]
    public void AStrafeStepIsPreviewedWithTheExtraTimeUnitItsWalkPays()
    {
        // refreshPath prices the steps on the pathfinder that calculated the path, so the one-step strafe's extra time unit shows: the
        // soldier faces east and strafes south-east, a diagonal step of 6 and 1 for strafing.
        var arena = Make(suitExtra: "    allowsStrafing: true\n");
        var soldier = arena.Soldier;
        var view = Queries(arena).QueryPath(BattleViewer.LocalPlayer, soldier.Id, At(11, 11), BattleMoveType.Strafe)!;
        Assert.Equal((1, 7), (view.Steps.Count, view.TimeUnitsCost));
        var walked = Events(arena.Campaign.Execute(new WalkUnit(soldier.Id, At(11, 11), BattleMoveType.Strafe)));
        Assert.Equal(view.TimeUnitsCost, End(walked).TimeUnitsSpent);
    }

    [Fact]
    public void TheReachableTilesAreThoseWithinTheTimeUnitsAndEnergyAndTheViewerHasDiscovered()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        soldier.TimeUnits = 8;
        var reachable = Queries(arena).QueryReachable(BattleViewer.LocalPlayer, soldier.Id)!;
        Assert.Equal(At(10, 10), reachable.Tiles[0]);
        Assert.Contains(At(12, 10), reachable.Tiles);
        Assert.DoesNotContain(At(13, 10), reachable.Tiles);
        // A diagonal step costs 6: one diagonal and no more.
        Assert.Contains(At(11, 11), reachable.Tiles);
        Assert.DoesNotContain(At(12, 12), reachable.Tiles);
        Assert.All(reachable.Tiles, tile => Assert.True(arena.Map.IsDiscovered(arena.Tile(tile.X, tile.Y, tile.Z), BattleTilePart.Floor, BattleFaction.Player)));
        // A tile the viewer has not discovered is left out, and so is everything for another side's viewer.
        arena.Map.SetDiscovered(arena.Tile(11, 10), BattleTilePart.Floor, false, BattleFaction.Player);
        Assert.DoesNotContain(At(11, 10), Queries(arena).QueryReachable(BattleViewer.LocalPlayer, soldier.Id)!.Tiles);
        Assert.Null(Queries(arena).QueryReachable(new BattleViewer(BattleFaction.Hostile), soldier.Id));
        Assert.Null(Queries(arena).QueryReachable(BattleViewer.LocalPlayer, arena.Alien.Id));
    }

    [Fact]
    public void TheLegalActionsListTurnKneelWalkAndUpAndDownWhereTheRulesWouldStartThem()
    {
        var arena = Make();
        var actions = Queries(arena).QueryLegalActions(BattleViewer.LocalPlayer, arena.Soldier.Id)!.Actions;
        Assert.Contains(BattleActionKind.Turn, actions);
        Assert.Contains(BattleActionKind.Kneel, actions);
        Assert.Contains(BattleActionKind.Walk, actions);
        // A walker with no ladder cannot go up, and there is no level below the ground.
        Assert.DoesNotContain(BattleActionKind.MoveUp, actions);
        Assert.DoesNotContain(BattleActionKind.MoveDown, actions);

        var flier = Make(flying: true);
        var flying = Queries(flier).QueryLegalActions(BattleViewer.LocalPlayer, flier.Soldier.Id)!.Actions;
        Assert.Contains(BattleActionKind.MoveUp, flying);
        Assert.DoesNotContain(BattleActionKind.MoveDown, flying);
        Events(flier.Campaign.Execute(new MoveUnitUpDown(flier.Soldier.Id, true)));
        var hovering = Queries(flier).QueryLegalActions(BattleViewer.LocalPlayer, flier.Soldier.Id)!.Actions;
        Assert.Contains(BattleActionKind.MoveDown, hovering);
        Assert.DoesNotContain(BattleActionKind.Kneel, hovering);

        var forbidden = Make(suitExtra: "    allowsKneeling: false\n    allowsMoving: false\n");
        var none = Queries(forbidden).QueryLegalActions(BattleViewer.LocalPlayer, forbidden.Soldier.Id)!.Actions;
        Assert.DoesNotContain(BattleActionKind.Kneel, none);
        Assert.DoesNotContain(BattleActionKind.Walk, none);
        Assert.Contains(BattleActionKind.Turn, none);
        // Another side's turn lists none of them.
        arena.Battle.Side = BattleFaction.Hostile;
        Assert.DoesNotContain(BattleActionKind.Walk, Queries(arena).QueryLegalActions(BattleViewer.LocalPlayer, arena.Soldier.Id)!.Actions);
    }

    [Fact]
    public void TheTilesTheViewerSeesCarryTheirShadeAndOnlyDiscoveredPartsAreListed()
    {
        var arena = Make();
        var level = Queries(arena).QueryLevel(BattleViewer.LocalPlayer, 0)!;
        Assert.NotEmpty(level.Tiles);
        foreach (var tile in level.Tiles)
        {
            Assert.Equal(arena.Battle.TileShade(tile.Position), tile.Shade);
            var index = arena.Tile(tile.Position.X, tile.Position.Y);
            Assert.All(tile.Parts, part => Assert.True(arena.Map.IsDiscovered(index, part.Part, BattleFaction.Player)));
        }
        var hidden = arena.Tile(28, 28);
        arena.Map.SetDiscovered(hidden, BattleTilePart.Floor, false, BattleFaction.Player);
        Assert.DoesNotContain(Queries(arena).QueryLevel(BattleViewer.LocalPlayer, 0)!.Tiles.Where(static tile => tile.Position == At(28, 28)), static tile => tile.Parts.Any(static part => part.Part == BattleTilePart.Floor));
    }

    [Fact]
    public void TheSpotterWithTheMostTimeUnitsLeftIsSelectedAndNoSpotterIsRefused()
    {
        // A battle with several X-COM units: the craft carries two vehicles beside the soldiers.
        var (campaign, _) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 0), new("VEHICLE_B", 0)]);
        Started(campaign);
        campaign.Execute(new ConfirmBriefing());
        campaign.Execute(new StartFirstTurn());
        var battle = Battle(campaign);
        var selectable = campaign.GetQuery<IBattleViewQuery>()!.QuerySelection(BattleViewer.LocalPlayer)!.SelectableUnitIds;
        var withTimeUnits = selectable.Select(id => battle.UnitById(id)!).Where(static unit => unit.Stats.TimeUnits > 0).ToArray();
        Assert.True(withTimeUnits.Length >= 2, string.Join(',', selectable.Select(id => battle.UnitById(id)!.Stats.TimeUnits)));
        var (first, second) = (withTimeUnits[0], withTimeUnits[1]);
        var alien = battle.Units.First(static unit => unit.Faction == BattleFaction.Hostile);
        battle.SetSpotted(alien, BattleFaction.Player, true);
        foreach (var unit in battle.Units) unit.ClearVisibleUnits();
        // Only the first unit sees the alien: it is the spotter, whoever asks.
        first.AddToVisibleUnits(alien.Id);
        var chosen = campaign.Execute(new SelectSpotter(second.Id, alien.Id));
        Assert.Equal(new UnitSelected(first.Id, true), Assert.Single(chosen.Events));
        Assert.Equal(first.Id, battle.SelectedUnitId);
        // The second sees it too, with all its time units against a half: it is selected.
        second.AddToVisibleUnits(alien.Id);
        second.TimeUnits = second.Stats.TimeUnits;
        first.TimeUnits = first.Stats.TimeUnits / 2;
        Assert.Equal(new UnitSelected(second.Id, true), Assert.Single(campaign.Execute(new SelectSpotter(first.Id, alien.Id)).Events));
        // A tie keeps the order of the units.
        first.TimeUnits = first.Stats.TimeUnits;
        second.TimeUnits = second.Stats.TimeUnits;
        Assert.Equal(new UnitSelected(first.Id, true), Assert.Single(campaign.Execute(new SelectSpotter(second.Id, alien.Id)).Events));
        // An unknown unit, and a unit nobody sees.
        Assert.Equal("battle.selection-unit-unknown", ((CampaignActionBlocked)campaign.Execute(new SelectSpotter(first.Id, 9999)).Events[0]).Code);
        first.ClearVisibleUnits();
        second.ClearVisibleUnits();
        Assert.Equal("battle.selection-no-spotter", ((CampaignActionBlocked)campaign.Execute(new SelectSpotter(first.Id, alien.Id)).Events[0]).Code);
    }

    // ---- the screen ----

    [Fact]
    public void ATileClickWalksTheSelectedUnitAndTheEventsAreReplayedOnePerTick()
    {
        var arena = Make();
        var (context, screen, model) = Client(arena);
        Assert.True(context.Screens.HandleInput(ClickTile(screen, 13, 10)));
        Assert.Equal("", screen.Message);
        Assert.Equal(At(13, 10), arena.Soldier.Position);
        // The camera follows the unit, and the events are what is replayed: three steps and the end.
        Assert.Equal(new BattlePosition(13, 10, 0), model.Camera.Center);
        Assert.Equal(4, screen.Replay.Count);
        Assert.Equal([At(11, 10), At(12, 10), At(13, 10)], screen.Replay.Where(static frame => frame.Position is not null).Select(static frame => frame.Position!.Value));
        Assert.True(screen.Replaying);
        var revision = screen.PresentationRevision;
        for (var played = 1; played <= 4; played++)
        {
            Tick(screen);
            Assert.Equal(played, screen.ReplayPlayed);
            Assert.Equal(screen.Replay[played - 1].Text, screen.Message);
            Assert.NotEqual(revision, screen.PresentationRevision);
            revision = screen.PresentationRevision;
        }
        Assert.False(screen.Replaying);
        Assert.Equal("Walk ends: Arrived after 3 steps (12 TU, 6 En)", screen.Message);
        Assert.Equal(At(13, 10), screen.ReplayFrame is { Position: null } ? arena.Soldier.Position : null);
        // The trail stays until the next input, and an idle tick draws nothing.
        Assert.Equal(BattleUiContext.Color.Selected, Pixel(screen, 11, 10));
        Tick(screen);
        Assert.Equal(revision, screen.PresentationRevision);
        // The next input ends it.
        context.Screens.HandleInput(Over(screen, 20, 20));
        Assert.True(context.Screens.HandleInput(Key('c')));
        Assert.Empty(screen.Replay);
    }

    [Fact]
    public void AReleaseDoesNotEndTheReplayAndAPressDuringItPlaysItOutAndIsDropped()
    {
        var arena = Make();
        var (context, screen, _) = Client(arena);
        context.Screens.HandleInput(ClickTile(screen, 13, 10));
        // The release of the click, the key release and a hover do not end it.
        context.Screens.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerReleased, 0, 0, 5, 5, 1, 1));
        context.Screens.HandleInput(Over(screen, 20, 20));
        Tick(screen);
        Assert.Equal(1, screen.ReplayPlayed);
        Assert.True(screen.Replaying);
        // A press is dropped as the busy game drops it (no second walk); it plays the replay out, and a right click names the cancel it cannot do.
        context.Screens.HandleInput(ClickTile(screen, 13, 12));
        Assert.Equal(At(13, 10), arena.Soldier.Position);
        Assert.False(screen.Replaying);
        Assert.Equal("Walk ends: Arrived after 3 steps (12 TU, 6 En)", screen.Message);
        context.Screens.HandleInput(ClickTile(screen, 13, 12));
        Assert.Equal(At(13, 12), arena.Soldier.Position);
        Assert.True(screen.Replaying);
        context.Screens.HandleInput(ClickTile(screen, 14, 13, 3));
        Assert.Equal(BattlescapeClient.CancelWalkCode, screen.Message);
        Assert.False(screen.Replaying);
    }

    [Fact]
    public void EveryKindOfEventHasItsReplayFrame()
    {
        var events = new ICampaignEvent[]
        {
            new UnitKneeled(1, true, 4), new UnitTurned(1, 3, 0, 0, BattleTurnStop.NoTurn), new UnitWalkTurned(1, 3, 0),
            new UnitWalkOpenedDoor(1, BattleDoorResult.Opened, 1, At(2, 2), 3), new UnitStepped(1, At(1, 1), At(2, 1), 2, 4, 2),
            new UnitFell(2, At(2, 1), At(2, 1, 0)), new UnitsSpotted(1, [5, 6]), new UnitWalkEnded(1, BattleWalkStop.Arrived, 1, 4, 2, At(2, 1)),
            new UnitSelected(1, true),
        };
        var frames = BattleReplay.From(events);
        Assert.Equal(
        [
            ("Kneels (4 TU)", null), ("Faces 3", null), ("Turns to 3", null), ("Door Opened (1 opened, 3 TU)", null), ("Step 2 (4 TU, 2 En)", At(2, 1)),
            ("Falls", At(2, 1)), ("Spots 2 unit(s)", null), ("Walk ends: Arrived after 1 steps (4 TU, 2 En)", null),
        ], frames.Select(static frame => (frame.Text, frame.Position)).Select(static pair => (pair.Text, (BattlePosition?)pair.Position)));
        Assert.Equal(2, frames[5].UnitId);
    }

    [Fact]
    public void APartWhoseFloorIsNotDiscoveredIsDrawnBlack()
    {
        var arena = Make();
        var (_, screen, model) = Client(arena);
        arena.Put(12, 12, 0, BattleTilePart.ObjectPart, Wall);
        arena.Map.SetDiscovered(arena.Tile(12, 12), BattleTilePart.Floor, false, BattleFaction.Player);
        arena.Map.SetDiscovered(arena.Tile(12, 12), BattleTilePart.ObjectPart, true, BattleFaction.Player);
        Changed(arena);
        screen.Tick(TimeSpan.Zero);
        var tile = model.Level!.Tiles.Single(static shown => shown.Position == At(12, 12));
        Assert.DoesNotContain(tile.Parts, static part => part.Part == BattleTilePart.Floor);
        Assert.Contains(tile.Parts, static part => part.Part == BattleTilePart.ObjectPart);
        Assert.Equal(0, Pixel(screen, 12, 12));
    }

    [Fact]
    public void ShiftWalksPastANewlySeenUnitWhereAPlainClickStopsAtIt()
    {
        foreach (var shift in new[] { false, true })
        {
            var arena = Make();
            arena.Stand(arena.Alien, 14, 8, 6);
            arena.Put(12, 9, 0, BattleTilePart.ObjectPart, Wall);
            arena.See();
            var (context, screen, model) = Client(arena);
            context.Screens.HandleInput(ClickTile(screen, 16, 10, modifiers: shift ? InputKeyModifiers.LeftShift : InputKeyModifiers.None));
            var end = Assert.IsType<UnitWalkEnded>(model.LastEvents[^1]);
            Assert.Equal(shift ? BattleWalkStop.Arrived : BattleWalkStop.SpottedUnits, end.Stop);
            Assert.Equal(shift, arena.Soldier.Position == At(16, 10));
        }
    }

    [Fact]
    public void AWalkTheRulesRefuseShowsTheirReasonAndChangesNothing()
    {
        var arena = Make();
        var (context, screen, model) = Client(arena);
        arena.Soldier.TimeUnits = 2;
        var revision = arena.Battle.Revision;
        context.Screens.HandleInput(ClickTile(screen, 13, 10));
        Assert.Equal("battle.walk-not-enough-time-units", screen.Message);
        Assert.Empty(model.LastEvents);
        Assert.Empty(screen.Replay);
        Assert.Equal((At(10, 10), revision), (arena.Soldier.Position!.Value, arena.Battle.Revision));
        // The unit's own tile is no walk either.
        context.Screens.HandleInput(ClickTile(screen, 10, 10));
        Assert.Equal("battle.walk-no-path", screen.Message);
    }

    [Fact]
    public void AClickOnAnotherUnitOfTheSideSelectsItAndOneOnAHostileUnitDoesNothing()
    {
        var arena = Make();
        var (context, screen, model) = Client(arena);
        var revision = arena.Battle.Revision;
        // The alien is seen at (15, 10): a click on it does not walk to it and shows no refusal.
        context.Screens.HandleInput(ClickTile(screen, 15, 10));
        Assert.Equal(("", revision, arena.Soldier.Id), (screen.Message, arena.Battle.Revision, model.SelectedUnitId!.Value));
        Assert.Empty(model.LastEvents);
        // The middle button over a unit is the alien inventory of Phase 8, and over a bare tile nothing.
        context.Screens.HandleInput(ClickTile(screen, 15, 10, 2));
        Assert.Equal(BattlescapeClient.AlienInventoryCode, screen.Message);
        context.Screens.HandleInput(ClickTile(screen, 12, 12, 2));
        Assert.Equal("", screen.Message);
        Assert.Equal(revision, arena.Battle.Revision);
    }

    [Fact]
    public void TheRightClickTurnsTheUnitAndOpensTheDoorItFaces()
    {
        var arena = Make();
        var (context, screen, model) = Client(arena);
        context.Screens.HandleInput(ClickTile(screen, 10, 12, 3));
        Assert.Equal(4, arena.Soldier.Direction);
        var turned = Assert.IsType<UnitTurned>(Assert.Single(model.LastEvents));
        Assert.Equal((4, BattleTurnStop.Arrived), (turned.Direction, turned.Stop));
        Assert.Single(screen.Replay);
        Assert.StartsWith("Turns to 4", screen.Replay[0].Text);
        // Facing east at a swinging door: the right click towards it opens it instead of turning.
        var doored = Make();
        doored.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorCost);
        doored.See();
        var (doorContext, doorScreen, doorModel) = Client(doored);
        doorContext.Screens.HandleInput(ClickTile(doorScreen, 12, 10, 3));
        var opened = Assert.IsType<UnitTurned>(Assert.Single(doorModel.LastEvents));
        Assert.Equal((BattleTurnStop.NoTurn, BattleDoorResult.Opened), (opened.Stop, opened.Door));
        Assert.Contains("door Opened", doorScreen.Replay[0].Text);
        Assert.Equal(OpenDoorPart, doored.Map.Part(doored.Tile(11, 10), BattleTilePart.NorthWall).Id);
        // A refusal of the rules is the screen's message.
        arena.Soldier.TimeUnits = 0;
        Play(screen);
        context.Screens.HandleInput(ClickTile(screen, 10, 10 + 2, 3));
        Play(screen);
        Assert.Equal("Faces 4", screen.Message);
        context.Screens.HandleInput(ClickTile(screen, 5, 5, 3));
        Assert.Equal("battle.turn-not-enough-time-units", screen.Message);
    }

    [Fact]
    public void TheKneelKeyKneelsAndStandsUpAndShowsTheRefusalOfAnArmorThatCannot()
    {
        var arena = Make();
        var (context, screen, model) = Client(arena);
        var units = arena.Soldier.TimeUnits;
        Assert.True(context.Screens.HandleInput(Key(BattlescapeClient.KneelKey)));
        Assert.True(arena.Soldier.Kneeled);
        Assert.Equal(units - 4, arena.Soldier.TimeUnits);
        Assert.True(model.SelectedUnit!.Kneeled);
        Tick(screen);
        Assert.Equal("Kneels (4 TU)", screen.Message);
        context.Screens.HandleInput(Key(BattlescapeClient.KneelKey));
        Assert.False(arena.Soldier.Kneeled);
        Assert.Equal(units - 12, arena.Soldier.TimeUnits);
        Tick(screen);
        Assert.Equal("Stands up (8 TU)", screen.Message);

        var forbidden = Make(suitExtra: "    allowsKneeling: false\n");
        var (forbiddenContext, forbiddenScreen, _) = Client(forbidden);
        forbiddenContext.Screens.HandleInput(Key(BattlescapeClient.KneelKey));
        Assert.Equal("battle.kneel-not-allowed", forbiddenScreen.Message);
        Assert.False(forbidden.Soldier.Kneeled);
    }

    [Fact]
    public void TheUnitUpAndDownKeysFlyTheUnitAndTheCameraFollowsItsLevel()
    {
        var arena = Make(flying: true);
        var (context, screen, model) = Client(arena);
        context.Screens.HandleInput(Key(BattlescapeClient.UnitUpKey));
        Assert.Equal(At(10, 10, 1), arena.Soldier.Position);
        Assert.Equal(1, model.Camera.ViewLevel);
        Assert.Equal(1, Assert.IsType<UnitStepped>(model.LastEvents[0]).To.Z);
        Assert.Equal(1, model.Level!.Level);
        Play(screen);
        context.Screens.HandleInput(Key(BattlescapeClient.UnitDownKey));
        Assert.Equal(At(10, 10, 0), arena.Soldier.Position);
        Assert.Equal(0, model.Camera.ViewLevel);
        Play(screen);
        context.Screens.HandleInput(Key(BattlescapeClient.UnitDownKey));
        Assert.Equal("battle.up-down-impossible", screen.Message);
        Assert.Equal(0, model.Camera.ViewLevel);
    }

    [Fact]
    public void TheVisibleUnitButtonsCenterOnTheUnitAndTheRightClickSelectsTheBestSpotter()
    {
        var arena = Make();
        var (context, screen, model) = Client(arena);
        Assert.Equal([arena.Alien.Id], model.SelectedUnit!.VisibleUnitIds);
        // The key 1 is the first button: the camera centers on the unit it shows.
        context.Screens.HandleInput(Key('1'));
        Assert.Equal(new BattlePosition(15, 10, 0), model.Camera.Center);
        model.Camera.CenterOn(At(1, 1));
        // The button on the panel does the same with a left click.
        var (x, y) = (BattlescapeClient.MapWidth + 8 + 2, 316 + 2);
        context.Screens.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x, y, 1, 1));
        Assert.Equal(new BattlePosition(15, 10, 0), model.Camera.Center);
        // A button the unit has no unit for, and the digits past the list, do nothing.
        model.Camera.CenterOn(At(1, 1));
        context.Screens.HandleInput(Key('2'));
        context.Screens.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x + 22, y, 1, 1));
        Assert.Equal(new BattlePosition(1, 1, 0), model.Camera.Center);
        // The right click (Shift and the key): the unit that sees it best, here the soldier, stays selected and the camera goes to it.
        context.Screens.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x, y, 3, 1));
        Assert.Equal((arena.Soldier.Id, new BattlePosition(10, 10, 0)), (model.SelectedUnitId!.Value, model.Camera.Center));
        model.Camera.CenterOn(At(1, 1));
        // A key never is a right click: Shift and the digit still center on the unit.
        context.Screens.HandleInput(Key('1', InputKeyModifiers.LeftShift));
        Assert.Equal(new BattlePosition(15, 10, 0), model.Camera.Center);
        Assert.Equal("", screen.Message);
        // The buttons are drawn: a block in the hostile colour with its number.
        Assert.Equal(BattleUiContext.Color.Hostile, screen.Frame.GetPixel(x - 1 + 1, y - 1 + 1));
    }

    [Fact]
    public void ThePointerOverATilePreviewsThePathAndTheReachableKeyShowsTheTilesWithinReach()
    {
        var arena = Make();
        var (context, screen, model) = Client(arena);
        context.Screens.HandleInput(Over(screen, 13, 10));
        Assert.Equal(3, model.Path!.Steps.Count);
        // Each step tile is marked in its colour; one the unit cannot pay is red.
        Assert.Equal(BattleUiContext.Color.PathAffordable, Pixel(screen, 12, 10));
        arena.Soldier.TimeUnits = 6;
        Changed(arena);
        Tick(screen);
        // The first step is yellow (kneeling is reserved for), the next ones red.
        Assert.Equal(BattleUiContext.Color.PathReserved, Pixel(screen, 11, 10));
        Assert.Equal(BattleUiContext.Color.PathUnaffordable, Pixel(screen, 12, 10));
        // Off the map area the preview is gone, and a tile the viewer has not discovered has none.
        context.Screens.HandleInput(GameInputEvent.PointerMotion(0, 0, BattlescapeClient.MapWidth + 10, 10, 0, 0, 0));
        Assert.Null(model.Path);
        arena.Map.SetDiscovered(arena.Tile(2, 2), BattleTilePart.Floor, false, BattleFaction.Player);
        context.Screens.HandleInput(Over(screen, 2, 2));
        Assert.Null(model.Path);
        // The reachable overlay.
        Assert.Null(model.Reachable);
        context.Screens.HandleInput(Key(BattlescapeClient.ReachableKey));
        Assert.NotNull(model.Reachable);
        Assert.Contains(At(11, 10), model.Reachable!.Tiles);
        Assert.Equal(BattleUiContext.Color.ReachableTile, Pixel(screen, 11, 10));
        context.Screens.HandleInput(Key(BattlescapeClient.ReachableKey));
        Assert.Null(model.Reachable);
    }

    [Fact]
    public void TheFloorIsDrawnInTheShadeOfItsTileAndOnlyWhereTheViewerHasDiscoveredIt()
    {
        var arena = Make();
        var (_, screen, model) = Client(arena);
        var tile = model.Level!.Tiles.First(static shown => shown.Parts.Any(static part => part.Part == BattleTilePart.Floor) && shown.UnitId is null);
        Assert.Equal((byte)(BattleUiContext.Color.FloorShade + tile.Shade), Pixel(screen, tile.Position.X, tile.Position.Y));
        // A tile the viewer has not discovered is the frame's colour.
        var hidden = arena.Tile(28, 28);
        foreach (var part in Enum.GetValues<BattleTilePart>()) arena.Map.SetDiscovered(hidden, part, false, BattleFaction.Player);
        Changed(arena);
        model.Refresh();
        screen.Tick(TimeSpan.Zero);
        Assert.Equal(BattleUiContext.Color.Frame, Pixel(screen, 28, 28));
        // A darker light darkens the drawing: the shade index follows the tile's.
        var lit = arena.Tile(tile.Position.X, tile.Position.Y);
        arena.Map.ResetLightMulti(lit, BattleLightLayer.Ambient);
        arena.Map.AddLight(lit, 6, BattleLightLayer.Ambient);
        Changed(arena);
        screen.Tick(TimeSpan.Zero);
        Assert.Equal((byte)(BattleUiContext.Color.FloorShade + 9), Pixel(screen, tile.Position.X, tile.Position.Y));
    }

    [Fact]
    public void TheTabKeyStillSelectsAndNothingElseOfTheLaterButtonsMovesTheUnit()
    {
        var arena = Make();
        var (context, screen, _) = Client(arena);
        var position = arena.Soldier.Position;
        foreach (var (key, code, owner) in BattlescapeClient.LaterButtons)
        {
            context.Screens.HandleInput(Key(key));
            Assert.Equal($"{code} ({owner})", screen.Message);
        }
        Assert.DoesNotContain(BattlescapeClient.LaterButtons, static later => later.Key is BattlescapeClient.KneelKey or BattlescapeClient.UnitUpKey or BattlescapeClient.UnitDownKey or 'w');
        Assert.Equal(position, arena.Soldier.Position);
        context.Screens.HandleInput(Key(Tab));
    }
}
