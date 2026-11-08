using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Xunit;
using static Oxce.CompatibilityTests.BattleSightTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S8: walking and flying (<c>src/Battlescape/UnitWalkBState.cpp</c>, <c>BattlescapeGame::primaryAction</c> and
/// <c>moveUpDown</c>). The arena is the sight tests' one made four levels high: the soldier at (10, 10) facing east and the alien five tiles
/// east facing west on the floor of the ground level. A step on the floor costs 4 time units (6 across a diagonal) and half that in energy.
/// </summary>
public sealed class BattleWalkTests
{
    private const string GunText = "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1";

    private static string Replaced(string rules, string from, string to)
    {
        var result = rules.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(rules, result);
        return result;
    }

    /// <summary>The soldier walks (unless <paramref name="flying"/>), the map is four levels high, the alien reacts fast and the gun has costs.</summary>
    internal static Arena Make(bool flying = false, string suitExtra = "", int distance = 5, string rulesExtra = "", Func<string, string>? edit = null) =>
        Flat(distance: distance, edit: rules =>
        {
            rules = Replaced(rules, "    shade: 0\n    width: 30\n    length: 30\n    height: 2", "    shade: 0\n    width: 30\n    length: 30\n    height: 4");
            if (!flying) rules = Replaced(rules, "  - type: SUIT\n    movementType: 1\n", "  - type: SUIT\n    movementType: 0\n");
            if (suitExtra.Length != 0) rules = Replaced(rules, "  - type: SUIT\n", "  - type: SUIT\n" + suitExtra);
            rules = Replaced(rules, "stats: {tu: 50, firing: 61}", "stats: {tu: 50, firing: 61, reactions: 100}");
            rules = Replaced(rules, GunText, GunText + "\n    tuSnap: 20\n    tuAimed: 50");
            if (rulesExtra.Length != 0) rules += "\n" + rulesExtra + "\n";
            return edit is null ? rules : edit(rules);
        });

    internal static BattlePosition At(int x, int y, int z = 0) => new(x, y, z);

    private static string Refusal(CampaignCommandResult result) => Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events)).Code;

    private static IReadOnlyList<ICampaignEvent> Walk(Arena arena, BattlePosition target, BattleMoveType move = BattleMoveType.Normal, bool ignore = false) =>
        Events(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, target, move, ignore)));

    private static IReadOnlyList<ICampaignEvent> Events(CampaignCommandResult result)
    {
        Assert.DoesNotContain(result.Events, static @event => @event is CampaignActionBlocked);
        return result.Events;
    }

    private static UnitWalkEnded End(IReadOnlyList<ICampaignEvent> events) => Assert.IsType<UnitWalkEnded>(events[^1]);

    internal static BattleItem GunFor(Arena arena, BattleUnit unit)
    {
        var gun = new BattleItem(arena.Battle.TakeItemId(), "GUN") { OwnerUnitId = unit.Id, Slot = "STR_RIGHT_HAND", IsWeaponWithAmmo = true };
        arena.Battle.AddItem(gun);
        var clip = new BattleItem(arena.Battle.TakeItemId(), "CLIP") { AmmoQuantity = 5, IsAmmo = true };
        arena.Battle.AddItem(clip);
        gun.AmmoSlots[0] = clip.Id;
        return gun;
    }

    /// <summary>The state a refused walk must leave as it found it.</summary>
    private static string State(Arena arena) => string.Join('|', arena.Battle.Units.Select(unit =>
        $"{unit.Id}:{unit.Position}:{unit.LastPosition}:{unit.Direction}:{unit.Status}:{unit.Floating}:{unit.TimeUnits}:{unit.Energy}:{unit.MotionPoints}:{unit.Kneeled}:" +
        string.Join(',', unit.UnitsSpottedThisTurn))) + $"|{arena.Battle.Revision}|{string.Join(',', arena.Battle.FallingUnits.Select(static unit => unit.Id))}";

    // ---- walking ----

    [Fact]
    public void AStraightWalkStepsTileByTileAndPaysTheTimeUnitsAndEnergyOfEachStep()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var (units, energy, motion) = (soldier.TimeUnits, soldier.Energy, soldier.MotionPoints);
        var events = Walk(arena, At(13, 10));
        Assert.Equal(
        [
            new UnitStepped(soldier.Id, At(10, 10), At(11, 10), 2, 4, 2),
            new UnitStepped(soldier.Id, At(11, 10), At(12, 10), 2, 4, 2),
            new UnitStepped(soldier.Id, At(12, 10), At(13, 10), 2, 4, 2),
        ], events.Take(3));
        Assert.Equal(new UnitWalkEnded(soldier.Id, BattleWalkStop.Arrived, 3, 12, 6, At(13, 10)), End(events));
        Assert.Equal(4, events.Count);
        Assert.Equal((At(13, 10), At(12, 10), 2, BattleUnitStatus.Standing, units - 12, energy - 6), (soldier.Position!.Value, soldier.LastPosition!.Value,
            soldier.Direction, soldier.Status, soldier.TimeUnits, soldier.Energy));
        Assert.Equal(arena.Alien.Id, arena.Map.UnitAt(arena.Tile(15, 10)));
        Assert.Equal(soldier.Id, arena.Map.UnitAt(arena.Tile(13, 10)));
        Assert.Null(arena.Map.UnitAt(arena.Tile(10, 10)));
        // Each step of a tall unit is 4 motion points.
        Assert.Equal(motion + 12, soldier.MotionPoints);
    }

    [Fact]
    public void ADiagonalStepCostsOneAndAHalfTimesAndTheUnitTurnsFirstForNothing()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var units = soldier.TimeUnits;
        var events = Walk(arena, At(11, 11));
        // The turn from east to south east is free: the armor does not turn before its first step.
        Assert.Equal(new UnitWalkTurned(soldier.Id, 3, 0), events[0]);
        Assert.Equal(new UnitStepped(soldier.Id, At(10, 10), At(11, 11), 3, 6, 3), events[1]);
        Assert.Equal(new UnitWalkEnded(soldier.Id, BattleWalkStop.Arrived, 1, 6, 3, At(11, 11)), End(events));
        Assert.Equal((3, units - 6), (soldier.Direction, soldier.TimeUnits));
    }

    [Fact]
    public void ATurnBeforeTheFirstStepCostsTheTurnCostWhenTheArmorTurnsBeforeItsFirstStep()
    {
        var arena = Make(suitExtra: "    turnCost: 2\n    turnBeforeFirstStep: true\n");
        var soldier = arena.Soldier;
        var units = soldier.TimeUnits;
        // Two eighths from east to south, each paid before the step; the turn costs nothing once the unit walks.
        var events = Walk(arena, At(10, 12));
        Assert.Equal([new UnitWalkTurned(soldier.Id, 3, 2), new UnitWalkTurned(soldier.Id, 4, 2)], events.Take(2));
        Assert.Equal(new UnitStepped(soldier.Id, At(10, 10), At(10, 11), 4, 4, 2), events[2]);
        Assert.Equal(units - 4 - 8, soldier.TimeUnits);
        Assert.Equal(12, End(events).TimeUnitsSpent);
        // A turn between steps is free whatever the armor says: a path that bends turns the unit for nothing.
        var bent = Make(suitExtra: "    turnCost: 2\n    turnBeforeFirstStep: true\n");
        // The door costs enough to make the path go round it: diagonally up, then diagonally down.
        bent.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorCost);
        var after = Walk(bent, At(12, 10));
        var turns = after.SkipWhile(static @event => @event is not UnitStepped).OfType<UnitWalkTurned>().ToArray();
        Assert.NotEmpty(turns);
        Assert.All(turns, static turned => Assert.Equal(0, turned.TimeUnitsSpent));
    }

    [Fact]
    public void AWalkWithoutTheTurnBeforeTheFirstStepFlagIsOnlyChargedForTheTurnWhenItStopsOnASighting()
    {
        // The alien stands north of the soldier, who walks south east then turns north: the turn before the first step shows the alien.
        var arena = Make(suitExtra: "    turnCost: 2\n");
        arena.Stand(arena.Alien, 10, 5, 6);
        arena.See();
        Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
        var soldier = arena.Soldier;
        var units = soldier.TimeUnits;
        var events = Walk(arena, At(10, 14));
        Assert.Equal(new UnitWalkEnded(soldier.Id, BattleWalkStop.Arrived, 4, 16, 8, At(10, 14)), End(events));
        // Walking south, the turn goes from east to south (two eighths) and the alien north stays unseen.
        Assert.Equal(units - 16, soldier.TimeUnits);
        // Going north instead: the first eighth (to the north east) brings the alien into view, the walk stops with the turn paid.
        var arena2 = Make(suitExtra: "    turnCost: 2\n");
        arena2.Stand(arena2.Alien, 10, 5, 6);
        arena2.See();
        var before = arena2.Soldier.TimeUnits;
        var north = Walk(arena2, At(10, 8));
        Assert.Equal(new UnitWalkEnded(arena2.Soldier.Id, BattleWalkStop.SpottedUnits, 0, 2, 0, At(10, 10)), End(north));
        Assert.Equal([arena2.Alien.Id], Assert.IsType<UnitsSpotted>(north[^2]).Spotted);
        Assert.Equal(before - 2, arena2.Soldier.TimeUnits);
        Assert.Equal((1, BattleUnitStatus.Standing), (arena2.Soldier.Direction, arena2.Soldier.Status));
    }

    [Fact]
    public void AWalkStopsAfterTheStepThatShowsANewUnitAndAShiftWalkDoesNot()
    {
        // The alien stands behind a wall block north east of the soldier; walking east brings it into view once the line past the block opens.
        foreach (var ignore in new[] { false, true })
        {
            var arena = Make();
            arena.Stand(arena.Alien, 14, 8, 6);
            arena.Put(12, 9, 0, BattleTilePart.ObjectPart, Wall);
            arena.See();
            Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
            var events = Walk(arena, At(16, 10), BattleMoveType.Normal, ignore);
            var end = End(events);
            if (ignore)
            {
                Assert.Equal((BattleWalkStop.Arrived, 6, At(16, 10)), (end.Stop, end.Steps, end.Position));
                Assert.DoesNotContain(events, static @event => @event is UnitsSpotted);
            }
            else
            {
                Assert.Equal(BattleWalkStop.SpottedUnits, end.Stop);
                Assert.True(end.Steps is > 0 and < 6);
                Assert.Equal(end.Steps * 4, end.TimeUnitsSpent);
                Assert.Equal([arena.Alien.Id], Assert.IsType<UnitsSpotted>(events[^2]).Spotted);
                Assert.Equal(At(10 + end.Steps, 10), end.Position);
                // The walk goes on once the unit is known.
                var rest = Walk(arena, At(16, 10));
                Assert.Equal(BattleWalkStop.Arrived, End(rest).Stop);
            }
        }
    }

    [Fact]
    public void ARunningWalkPaysItsOwnCostsAndTheArmorMustAllowRunning()
    {
        var arena = Make(suitExtra: "    allowsRunning: true\n    moveCost: {runPercent: [50, 300]}\n");
        var soldier = arena.Soldier;
        var before = (soldier.TimeUnits, soldier.Energy);
        var events = Walk(arena, At(12, 10), BattleMoveType.Run);
        Assert.Equal(new UnitStepped(soldier.Id, At(10, 10), At(11, 10), 2, 2, 12), events[0]);
        Assert.Equal((before.TimeUnits - 4, before.Energy - 24), (soldier.TimeUnits, soldier.Energy));
        // The default run costs three quarters of the time units and one and a half times the energy.
        var plain = Make(suitExtra: "    allowsRunning: true\n");
        Assert.Equal(new UnitStepped(plain.Soldier.Id, At(10, 10), At(11, 10), 2, 3, 3), Walk(plain, At(12, 10), BattleMoveType.Run)[0]);
        var refused = Make(suitExtra: "    allowsRunning: false\n");
        var state = State(refused);
        Assert.Equal("battle.walk-move-not-allowed", Refusal(refused.Campaign.Execute(new WalkUnit(refused.Soldier.Id, At(12, 10), BattleMoveType.Run))));
        Assert.Equal(state, State(refused));
    }

    [Fact]
    public void ARunWithTheExtendedRunningCostDoesNotStopOnANewlySpottedUnit()
    {
        foreach (var extended in new[] { false, true })
        {
            var arena = Make(suitExtra: "    allowsRunning: true\n", rulesExtra: extended ? "extendedRunningCost: true" : "");
            arena.Stand(arena.Alien, 14, 8, 6);
            arena.Put(12, 9, 0, BattleTilePart.ObjectPart, Wall);
            arena.See();
            Assert.False(Arena.Sees(arena.Soldier, arena.Alien));
            var end = End(Walk(arena, At(16, 10), BattleMoveType.Run));
            Assert.Equal(extended ? BattleWalkStop.Arrived : BattleWalkStop.SpottedUnits, end.Stop);
        }
    }

    [Fact]
    public void AStrafingStepKeepsTheFacingAndAnUnsupportedWayOfMovingIsRefused()
    {
        var arena = Make(suitExtra: "    allowsStrafing: true\n");
        var soldier = arena.Soldier;
        var events = Walk(arena, At(10, 11), BattleMoveType.Strafe);
        // One step south to the side of a unit facing east: no turn, and the unit still faces east.
        Assert.Equal(BattleWalkStop.Arrived, End(events).Stop);
        Assert.DoesNotContain(events, static @event => @event is UnitWalkTurned);
        Assert.Equal((At(10, 11), 2), (soldier.Position!.Value, soldier.Direction));
        // The plain armor does not allow sneaking (a non-soldier default) and the move is refused before any change.
        var arena2 = Make(suitExtra: "    allowsSneaking: false\n");
        var state = State(arena2);
        Assert.Equal("battle.walk-move-not-allowed", Refusal(arena2.Campaign.Execute(new WalkUnit(arena2.Soldier.Id, At(12, 10), BattleMoveType.Sneak))));
        Assert.Equal(state, State(arena2));
    }

    [Fact]
    public void AWalkThatCannotPayItsFirstStepIsRefusedAndOneThatRunsOutStopsWhereItIs()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        soldier.TimeUnits = 9;
        var events = Walk(arena, At(14, 10));
        Assert.Equal(new UnitWalkEnded(soldier.Id, BattleWalkStop.NotEnoughTimeUnits, 2, 8, 4, At(12, 10)), End(events));
        Assert.Equal(1, soldier.TimeUnits);
        var state = State(arena);
        Assert.Equal("battle.walk-not-enough-time-units", Refusal(arena.Campaign.Execute(new WalkUnit(soldier.Id, At(14, 10)))));
        Assert.Equal(state, State(arena));
    }

    [Fact]
    public void AWalkWithoutEnoughEnergyStopsOrIsRefused()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        soldier.Energy = 3;
        var events = Walk(arena, At(14, 10));
        Assert.Equal(new UnitWalkEnded(soldier.Id, BattleWalkStop.NotEnoughEnergy, 1, 4, 2, At(11, 10)), End(events));
        soldier.Energy = 1;
        var state = State(arena);
        Assert.Equal("battle.walk-not-enough-energy", Refusal(arena.Campaign.Execute(new WalkUnit(soldier.Id, At(14, 10)))));
        Assert.Equal(state, State(arena));
    }

    [Fact]
    public void AWalkStopsBeforeTheTimeUnitsReservedForAShotAndRefusesWhenTheFirstStepWouldUseThem()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var gun = arena.Battle.InventoryOf(soldier).Single(static item => item.ItemType == "GUN" && item.Slot == "STR_RIGHT_HAND");
        arena.Battle.TimeUnitsReserved = (int)BattleActionType.AimedShot;
        var reserve = BattleActionCost.For(arena.Rules, BattleActionType.AimedShot, soldier, gun).Time;
        soldier.TimeUnits = reserve + 9;
        var events = Walk(arena, At(14, 10));
        Assert.Equal(new UnitWalkEnded(soldier.Id, BattleWalkStop.ReservedTimeUnits, 2, 8, 4, At(12, 10)), End(events));
        Assert.Equal(reserve + 1, soldier.TimeUnits);
        var state = State(arena);
        Assert.Equal("battle.walk-time-units-reserved", Refusal(arena.Campaign.Execute(new WalkUnit(soldier.Id, At(14, 10)))));
        Assert.Equal(state, State(arena));
    }

    [Fact]
    public void AKneelingUnitStandsUpFirstAndPaysForIt()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        soldier.Kneeled = true;
        var units = soldier.TimeUnits;
        var events = Walk(arena, At(12, 10));
        Assert.Equal(new UnitKneeled(soldier.Id, false, 8), events[0]);
        Assert.Equal(new UnitWalkEnded(soldier.Id, BattleWalkStop.Arrived, 2, 16, 4, At(12, 10)), End(events));
        Assert.Equal(units - 16, soldier.TimeUnits);
        Assert.False(soldier.Kneeled);
        // Not enough time units to stand up: refused with the kneel's reason before anything changes.
        arena.Soldier.Kneeled = true;
        arena.Soldier.TimeUnits = 5;
        var state = State(arena);
        Assert.Equal("battle.kneel-not-enough-time-units", Refusal(arena.Campaign.Execute(new WalkUnit(soldier.Id, At(14, 10)))));
        Assert.Equal(state, State(arena));
    }

    [Fact]
    public void AnArmorThatDoesNotAllowMovingNoPathAndATargetOutsideAreRefused()
    {
        var arena = Make(flying: true, suitExtra: "    allowsMoving: false\n");
        var state = State(arena);
        Assert.Equal("battle.walk-not-allowed", Refusal(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(12, 10)))));
        Assert.Equal("battle.walk-not-allowed", Refusal(arena.Campaign.Execute(new MoveUnitUpDown(arena.Soldier.Id, true))));
        Assert.Equal(state, State(arena));

        var walled = Make();
        walled.Stand(walled.Alien, 13, 10, 6);
        walled.See();
        state = State(walled);
        // A unit the soldier sees stands on the end tile: the end is blocked.
        Assert.Equal("battle.walk-no-path", Refusal(walled.Campaign.Execute(new WalkUnit(walled.Soldier.Id, At(13, 10)))));
        Assert.Equal("battle.walk-no-path", Refusal(walled.Campaign.Execute(new WalkUnit(walled.Soldier.Id, At(10, 10)))));
        Assert.Equal("battle.walk-target-outside-map", Refusal(walled.Campaign.Execute(new WalkUnit(walled.Soldier.Id, At(40, 10)))));
        Assert.Equal("battle.walk-target-outside-map", Refusal(walled.Campaign.Execute(new WalkUnit(walled.Soldier.Id, At(10, 10, 6)))));
        Assert.Equal("battle.selection-unit-unknown", Refusal(walled.Campaign.Execute(new WalkUnit(walled.Alien.Id, At(10, 10)))));
        Assert.Equal(state, State(walled));
    }

    /// <summary>
    /// Phase 7 branch 4 S11: a save can hold a unit mid-panic (status 8); the port's walk steps standing units only (the panic and berserk
    /// walks are branch 6's), so the command is refused before it starts instead of looping, and nothing changes.
    /// </summary>
    [Fact]
    public void AUnitThatIsNotStandingIsRefusedTheWalkAndTheUpAndDownMove()
    {
        foreach (var status in new[] { BattleUnitStatus.Panicking, BattleUnitStatus.Berserk, BattleUnitStatus.Walking, BattleUnitStatus.Turning })
        {
            var arena = Make();
            arena.Soldier.Status = status;
            var state = State(arena);
            Assert.Equal("battle.walk-unit-busy-unsupported", Refusal(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(12, 10)))));
            Assert.Equal("battle.walk-unit-busy-unsupported", Refusal(arena.Campaign.Execute(new MoveUnitUpDown(arena.Soldier.Id, true))));
            Assert.Equal(state, State(arena));
        }
    }

    [Fact]
    public void AUnitInTheWayThatTheWalkerDoesNotKnowOfRefusesTheWalkAndAKnownOneIsWalkedAround()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Battle.SetTile(arena.Alien, At(11, 10));
        // The soldier does not know of the alien (the player's path ignores what it has not seen), so the path goes through its tile.
        arena.Battle.SetSpotted(arena.Alien, BattleFaction.Player, false);
        var state = State(arena);
        Assert.Equal("battle.walk-unit-in-the-way", Refusal(arena.Campaign.Execute(new WalkUnit(soldier.Id, At(13, 10)))));
        Assert.Equal(state, State(arena));
        // Once the alien is known the path leaves its tile out.
        arena.Battle.SetSpotted(arena.Alien, BattleFaction.Player, true);
        var events = Walk(arena, At(13, 10));
        Assert.Equal(BattleWalkStop.Arrived, End(events).Stop);
        Assert.DoesNotContain(events.OfType<UnitStepped>(), static step => step.To == At(11, 10));
    }

    // ---- doors, stairs, ladders ----

    [Fact]
    public void AWalkThroughASwingingDoorOpensItAndPaysTheStepThroughIt()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorCost);
        var units = soldier.TimeUnits;
        var events = Walk(arena, At(11, 10));
        var door = Assert.IsType<UnitWalkOpenedDoor>(events[0]);
        Assert.Equal((BattleDoorResult.Opened, 1, At(11, 10)), (door.Door, door.DoorsOpened, door.DoorCentre!.Value));
        Assert.Equal(BattleWalkStop.Arrived, End(events).Stop);
        Assert.Equal(At(11, 10), soldier.Position);
        Assert.Equal(OpenDoorPart, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.NorthWall).Id);
        Assert.True(soldier.TimeUnits < units - 8);
    }

    [Fact]
    public void AWalkThroughAUfoDoorOpensItAtOnceAndThenThinksAgain()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, UfoDoorCost);
        var units = soldier.TimeUnits;
        var events = Walk(arena, At(11, 10));
        var door = Assert.IsType<UnitWalkOpenedDoor>(events[0]);
        Assert.Equal(BattleDoorResult.UfoDoorOpened, door.Door);
        Assert.Equal(6, door.TimeUnitsSpent);
        Assert.IsType<UnitStepped>(events[1]);
        Assert.Equal(At(11, 10), soldier.Position);
        Assert.True(units - soldier.TimeUnits > 6);
        // The door is only opened when the unit can pay for it and the step through it: with 9 time units it neither opens nor walks.
        var poor = Make();
        poor.Put(11, 10, 0, BattleTilePart.WestWall, UfoDoorCost);
        poor.Soldier.TimeUnits = 9;
        var state = State(poor);
        Assert.Equal("battle.walk-not-enough-time-units", Refusal(poor.Campaign.Execute(new WalkUnit(poor.Soldier.Id, At(11, 10)))));
        Assert.Equal(state, State(poor));
        Assert.Equal(UfoDoorCost, poor.Map.Part(poor.Tile(11, 10), BattleTilePart.WestWall).Id);
    }

    [Fact]
    public void AStairGoesUpALevelAndALadderIsClimbedWithTheJumpOntoTheLedge()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        // A ramp of two steps (terrain levels -8 and -16) leads up to a floor on the next level beyond it.
        arena.Put(11, 10, 0, BattleTilePart.Floor, RampLow);
        arena.Put(12, 10, 0, BattleTilePart.Floor, RampHigh);
        arena.Put(13, 10, 1, BattleTilePart.Floor, Floor);
        arena.See();
        var events = Walk(arena, At(13, 10, 1));
        Assert.Equal(BattleWalkStop.Arrived, End(events).Stop);
        Assert.Equal(At(13, 10, 1), soldier.Position);
        var last = events.OfType<UnitStepped>().Last();
        Assert.Equal((At(12, 10, 0), At(13, 10, 1)), (last.From, last.To));

        // A ladder: the unit climbs up the ladder tile, and the ledge it reaches has a floor only one tile further on, so
        // the unit reaches the top of the ladder with no footing and "jumps" across without falling.
        var ladder = Make();
        ladder.Put(11, 10, 0, BattleTilePart.ObjectPart, GravLift);
        ladder.Put(12, 10, 1, BattleTilePart.Floor, Floor);
        ladder.See();
        var climbed = Walk(ladder, At(12, 10, 1));
        Assert.Equal(BattleWalkStop.Arrived, End(climbed).Stop);
        Assert.Equal(At(12, 10, 1), ladder.Soldier.Position);
        Assert.Contains(climbed.OfType<UnitStepped>(), static step => step.Direction == BattleUnit.DirectionUp);
        Assert.Empty(ladder.Battle.FallingUnits);
    }

    // ---- flying ----

    [Fact]
    public void AFlyingUnitFliesUpAndDownByTheUpAndDownButtons()
    {
        var arena = Make(flying: true);
        var soldier = arena.Soldier;
        var units = soldier.TimeUnits;
        var up = Events(arena.Campaign.Execute(new MoveUnitUpDown(soldier.Id, true)));
        var step = Assert.IsType<UnitStepped>(up[0]);
        Assert.Equal((At(10, 10, 0), At(10, 10, 1), BattleUnit.DirectionUp), (step.From, step.To, step.Direction));
        Assert.Equal((At(10, 10, 1), true, 2, BattleWalkStop.Arrived), (soldier.Position!.Value, soldier.Floating, soldier.Direction, End(up).Stop));
        Assert.True(soldier.TimeUnits < units);
        Assert.Equal(soldier.Id, arena.Map.UnitAt(arena.Tile(10, 10, 1)));
        // Over a floor-less tile the unit hovers; down brings it back.
        var down = Events(arena.Campaign.Execute(new MoveUnitUpDown(soldier.Id, false)));
        Assert.Equal(At(10, 10, 0), Assert.IsType<UnitStepped>(down[0]).To);
        Assert.False(soldier.Floating);
        // The ground level's floor lies below the lowest level: nothing further down, and the top level has no tile above.
        Assert.Equal("battle.up-down-impossible", Refusal(arena.Campaign.Execute(new MoveUnitUpDown(soldier.Id, false))));
    }

    [Fact]
    public void AFlyingUnitFliesOverAHoleWithoutFallingAndAWalkingOneCannotGoUpWithoutALift()
    {
        var arena = Make(flying: true);
        var soldier = arena.Soldier;
        arena.Clear(12, 10, 0, BattleTilePart.Floor);
        var events = Walk(arena, At(13, 10));
        Assert.Equal(BattleWalkStop.Arrived, End(events).Stop);
        Assert.Equal(At(13, 10), soldier.Position);
        Assert.Empty(arena.Battle.FallingUnits);
        var walker = Make();
        var state = State(walker);
        Assert.Equal("battle.up-down-impossible", Refusal(walker.Campaign.Execute(new MoveUnitUpDown(walker.Soldier.Id, true))));
        Assert.Equal(state, State(walker));
    }

    // ---- falling ----

    [Fact]
    public void AWalkOffALedgeFallsAndTheWalkEndsOnTheFloorBelow()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        arena.Put(11, 10, 1, BattleTilePart.Floor, Floor);
        arena.Battle.SetTile(soldier, At(10, 10, 1));
        arena.See();
        var events = Walk(arena, At(12, 10, 0));
        Assert.Equal(BattleWalkStop.Arrived, End(events).Stop);
        Assert.Equal(0, soldier.Position!.Value.Z);
        Assert.False(soldier.HaveNoFloorBelow);
        Assert.Empty(arena.Battle.FallingUnits);
        Assert.Contains(events.OfType<UnitStepped>(), static step => step.Direction == BattleUnit.DirectionDown);
    }

    [Fact]
    public void AFallOntoAUnitThatCanMoveAsideWorksAndOneThatCannotIsRefusedBeforeAnyChange()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        arena.Battle.SetTile(soldier, At(10, 10, 1));
        arena.Stand(arena.Alien, 11, 10, 6);
        arena.See();
        // The soldier does not know of the alien below the ledge, so the walk steps off the ledge onto it.
        arena.Battle.SetSpotted(arena.Alien, BattleFaction.Player, false);
        var events = Walk(arena, At(12, 10, 0));
        Assert.Equal(BattleWalkStop.Arrived, End(events).Stop);
        Assert.Equal(At(12, 10, 0), soldier.Position);
        // The alien stepped aside when the soldier fell onto its tile.
        var moved = Assert.Single(events.OfType<UnitFell>(), fell => fell.UnitId == arena.Alien.Id);
        Assert.Equal((arena.Alien.Id, At(11, 10, 0)), (moved.UnitId, moved.From));
        Assert.NotEqual(At(11, 10, 0), arena.Alien.Position);
        Assert.Empty(arena.Battle.FallingUnits);

        // Boxed in by tiles without a floor, the unit below cannot move out of the way: the walk is refused.
        var boxed = Make();
        boxed.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        boxed.Battle.SetTile(boxed.Soldier, At(10, 10, 1));
        boxed.Stand(boxed.Alien, 11, 10, 6);
        for (var x = 10; x <= 12; x++)
            for (var y = 9; y <= 11; y++)
                if ((x, y) != (11, 10)) boxed.Clear(x, y, 0, BattleTilePart.Floor);
        boxed.See();
        boxed.Battle.SetSpotted(boxed.Alien, BattleFaction.Player, false);
        // (The trial copy sees what the soldier would see, so the walk is run on the battle itself here.)
        var late = boxed.Battle.Walk(boxed.Rules, boxed.Soldier.Id, At(12, 10, 0), BattleMoveType.Normal, false, []);
        Assert.Equal("battle.fall-knockout-unsupported", late?.Code);
    }

    // ---- what is refused by name ----

    [Fact]
    public void AStepNextToAnArmedOpposingUnitThatCouldReactIsRefusedBeforeAnyChange()
    {
        var arena = Make();
        GunFor(arena, arena.Alien);
        var state = State(arena);
        Assert.Equal("battle.reaction-fire-unsupported", Refusal(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(12, 10)))));
        Assert.Equal(state, State(arena));
        // A weaponless alien only spots: the mover is seen by the hostile faction's unit that faces it.
        var unarmed = Make();
        unarmed.Forget();
        Events(unarmed.Campaign.Execute(new WalkUnit(unarmed.Soldier.Id, At(12, 10))));
        Assert.True(unarmed.Soldier.IsSpottedBy(BattleFaction.Hostile));
        Assert.Contains(unarmed.Soldier.Id, unarmed.Alien.VisibleUnits);
    }

    [Fact]
    public void AUnitThatBurnsTheFloorItStandsOnWalksOnWhenThereIsNothingForTheHitToReach()
    {
        var arena = Make(suitExtra: "    specab: 2\n");
        // (The fire tests walk over floors with voxels: the hits and their refusals are BattleFireTests's.)
        Events(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(12, 10))));
        Assert.Equal(At(12, 10), arena.Soldier.Position);
    }

    [Fact]
    public void AStepNextToAnItemThatReactsToNearbyUnitsIsRefusedBeforeAnyChange()
    {
        var arena = Make(edit: rules => Replaced(rules, "- type: GRENADE\n    battleType: 4", "- type: GRENADE\n    battleType: 5"));
        var grenade = arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "GRENADE") { FuseEnabled = true, FuseTimer = 2 }, At(13, 11));
        Assert.NotNull(grenade);
        var state = State(arena);
        Assert.Equal("battle.proximity-unsupported", Refusal(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(12, 10)))));
        Assert.Equal(state, State(arena));
    }

    // ---- light and sight, the selection ----

    [Fact]
    public void ThePersonalLightAndTheUnitsSightMoveWithTheWalker()
    {
        var arena = Flat(personalLight: true, edit: rules => Replaced(rules, "    shade: 0\n    width: 30", "    shade: 15\n    width: 30"));
        var soldier = arena.Soldier;
        arena.Stand(arena.Alien, 24, 10, 6);
        arena.Relight();
        arena.See();
        var (here, there) = (arena.Battle.TileShade(At(10, 10)), arena.Battle.TileShade(At(14, 10)));
        // The personal light makes the soldier's own tile brighter than the dark tile four tiles on.
        Assert.True(here < there, $"{here} {there}");
        Assert.False(Arena.Sees(soldier, arena.Alien));
        var events = Events(arena.Campaign.Execute(new WalkUnit(soldier.Id, At(14, 10))));
        Assert.Equal(BattleWalkStop.Arrived, End(events).Stop);
        // The light is at the new place and gone from the old one: the two tiles trade shades on the flat map.
        Assert.Equal((there, here), (arena.Battle.TileShade(At(10, 10)), arena.Battle.TileShade(At(14, 10))));
    }

    [Fact]
    public void ADarkWalkersSightReachesFartherAsItMovesAndTheWalkStopsWhereItSeesAUnit()
    {
        var arena = Flat(personalLight: true, edit: rules => Replaced(rules, "    shade: 0\n    width: 30", "    shade: 15\n    width: 30"));
        var soldier = arena.Soldier;
        arena.Stand(arena.Alien, 26, 10, 6);
        arena.See();
        Assert.False(Arena.Sees(soldier, arena.Alien));
        var events = Events(arena.Campaign.Execute(new WalkUnit(soldier.Id, At(22, 10))));
        var end = End(events);
        Assert.Equal(BattleWalkStop.SpottedUnits, end.Stop);
        Assert.True(end.Steps is > 0 and < 12);
        Assert.True(Arena.Sees(soldier, arena.Alien));
        Assert.Equal([arena.Alien.Id], Assert.IsType<UnitsSpotted>(events[^2]).Spotted);
    }

    [Fact]
    public void AUnitOfTheHostileSideCannotWalkYetSoTheHostileReserveRefusalIsNotReachable()
    {
        // The selection rules refuse every command while the hostile side is on turn (turn processing is branch 6's), so
        // CheckReservedTimeUnits' hostile answer (battle.hostile-reserve-unsupported) cannot reach the walk through the command or the method.
        var arena = Make();
        arena.Battle.Side = BattleFaction.Hostile;
        Assert.Equal("battle.selection-unavailable", arena.Battle.Walk(arena.Rules, arena.Alien.Id, At(17, 10), BattleMoveType.Normal, false, [])?.Code);
    }

    [Fact]
    public void AKneelingUnitThatAReactorWouldShootAtIsRefusedByTheKneelBeforeAnyChange()
    {
        var arena = Make();
        GunFor(arena, arena.Alien);
        arena.Soldier.Kneeled = true;
        var state = State(arena);
        // Called on the battle itself: a refusal after a step would leave the walk done, a refusal of the kneel leaves nothing.
        Assert.Equal("battle.reaction-fire-unsupported", arena.Battle.Walk(arena.Rules, arena.Soldier.Id, At(12, 10), BattleMoveType.Normal, false, [])?.Code);
        Assert.Equal(state, State(arena));
        Assert.Equal("battle.reaction-fire-unsupported", Refusal(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(12, 10)))));
    }

    [Theory]
    [InlineData("proximityTrigger")]
    [InlineData("deathTrap")]
    public void AStepNextToAProximityTriggerItemOrOntoADeathTrapIsRefusedBeforeAnyChange(string kind)
    {
        var arena = Make(edit: rules =>
        {
            if (kind == "proximityTrigger")
                return Replaced(rules, "- type: GRENADE\n    battleType: 4\n    weight: 1", "- type: GRENADE\n    battleType: 4\n    weight: 1\n    fuseTriggerEvents: {proximityTrigger: true}");
            return Replaced(rules, "- type: GRENADE\n    battleType: 4\n    weight: 1", "- type: GRENADE\n    battleType: 4\n    weight: 1\n  - type: STR_DEATH_TRAP_200\n    battleType: 0\n    weight: 1");
        });
        if (kind == "proximityTrigger") arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "GRENADE"), At(13, 11));
        else arena.Put(12, 10, 0, BattleTilePart.Floor, DeathTrapFloor);
        var state = State(arena);
        Assert.Equal("battle.proximity-unsupported", Refusal(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(12, 10)))));
        Assert.Equal(state, State(arena));
    }

    [Fact]
    public void AUnitPokingUpFromBelowAHoleStopsTheWalkAfterTheStepsBeforeIt()
    {
        // A tall alien stands below a floorless tile on the path, out of the soldier's knowledge: the second step would be "in the way".
        var arena = Make(edit: rules => rules.Replace("standHeight: 22", "standHeight: 30", StringComparison.Ordinal));
        arena.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        arena.Put(11, 10, 1, BattleTilePart.Floor, Floor);
        arena.Battle.SetTile(arena.Soldier, At(10, 10, 1));
        arena.Stand(arena.Alien, 12, 10, 6);
        arena.See();
        arena.Battle.SetSpotted(arena.Alien, BattleFaction.Player, false);
        var events = Walk(arena, At(12, 10, 1));
        Assert.Equal(new UnitWalkEnded(arena.Soldier.Id, BattleWalkStop.UnitInTheWay, 1, 4, 2, At(11, 10, 1)), End(events));
    }

    [Fact]
    public void AFirstStepOntoATileWhoseHolePokesAUnitFromBelowIsRefused()
    {
        // A tall alien below a floorless tile pokes into it: stepping there is "in the way" although the tile itself is empty.
        var arena = Make(edit: rules => rules.Replace("standHeight: 22", "standHeight: 30", StringComparison.Ordinal));
        arena.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        arena.Battle.SetTile(arena.Soldier, At(10, 10, 1));
        arena.Stand(arena.Alien, 11, 10, 6);
        arena.See();
        arena.Battle.SetSpotted(arena.Alien, BattleFaction.Player, false);
        var late = arena.Battle.Walk(arena.Rules, arena.Soldier.Id, At(11, 10, 1), BattleMoveType.Normal, false, new List<ICampaignEvent>());
        Assert.Equal("battle.walk-unit-in-the-way", late?.Code);
    }

    [Fact]
    public void AStepClearsTheOtherFactionsSpottingOfTheMoverAndAnUndefinedMoveTypeIsRefused()
    {
        var arena = Make();
        arena.Stand(arena.Alien, 5, 29, 0);
        arena.See();
        arena.Battle.SetSpotted(arena.Soldier, BattleFaction.Hostile, true);
        arena.Battle.SetSpotted(arena.Soldier, BattleFaction.Neutral, true);
        Walk(arena, At(12, 10));
        Assert.False(arena.Soldier.IsSpottedBy(BattleFaction.Hostile));
        Assert.False(arena.Soldier.IsSpottedBy(BattleFaction.Neutral));
        var state = State(arena);
        Assert.Equal("battle.walk-move-not-allowed", Refusal(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(14, 10), (BattleMoveType)4))));
        Assert.Equal(state, State(arena));
    }

    [Fact]
    public void AKneelingUnitStandsUpForTheUpAndDownButtonsEvenWhenItsArmorThenDoesNotMove()
    {
        var arena = Make(flying: true);
        arena.Soldier.Kneeled = true;
        var events = Events(arena.Campaign.Execute(new MoveUnitUpDown(arena.Soldier.Id, true)));
        Assert.Equal(new UnitKneeled(arena.Soldier.Id, false, 8), events[0]);
        Assert.Equal(At(10, 10, 1), arena.Soldier.Position);

        var still = Make(flying: true, suitExtra: "    allowsMoving: false\n");
        still.Soldier.Kneeled = true;
        var units = still.Soldier.TimeUnits;
        var stood = Events(still.Campaign.Execute(new MoveUnitUpDown(still.Soldier.Id, true)));
        Assert.Equal(new UnitKneeled(still.Soldier.Id, false, 8), stood[0]);
        Assert.Equal(new UnitWalkEnded(still.Soldier.Id, BattleWalkStop.Arrived, 0, 8, 0, At(10, 10)), End(stood));
        Assert.Equal((units - 8, At(10, 10)), (still.Soldier.TimeUnits, still.Soldier.Position!.Value));
    }

    [Fact]
    public void AnUpButtonWithAUnitAboveIsRefusedAndTheNoPathEndOfMoveUpDownIsNotReachable()
    {
        // validateUpDown already refuses a tile above that holds a unit, and no arena that passes it was found where the path is missing, so the
        // path-less end of moveUpDown (postPathProcedures alone) cannot be reached through the command; the walk's own dir == -1 branch is
        // the same code that ends every walk and is run by each test.
        var arena = Make(flying: true);
        var friend = arena.Alien;
        arena.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        arena.Battle.SetTile(friend, At(10, 10, 1));
        arena.See();
        var state = State(arena);
        Assert.Equal("battle.up-down-impossible", Refusal(arena.Campaign.Execute(new MoveUnitUpDown(arena.Soldier.Id, true))));
        Assert.Equal(state, State(arena));
    }

    [Fact]
    public void AWalkKeepsTheSelection()
    {
        var arena = Make();
        arena.Battle.SelectedUnitId = arena.Soldier.Id;
        Events(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(12, 10))));
        Assert.Equal(arena.Soldier.Id, arena.Battle.SelectedUnitId);
    }
}
