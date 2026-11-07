using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Xunit;
using static Oxce.CompatibilityTests.BattleSightTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S6: doors (<c>TileEngine::unitOpensDoor</c>, <c>checkAdjacentDoors</c>, <c>closeUfoDoors</c>, <c>Tile::openDoor</c>). The
/// arena is the sight tests' one: the soldier at (10, 10) facing east, the alien five tiles east facing west, in daylight.
/// </summary>
public sealed class BattleDoorTests
{
    private const string GunText = "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1";

    /// <summary>The sight arena with a gun that takes 50 percent of the time units for an aimed shot.</summary>
    private static Arena Make(int distance = 5, bool bigAlien = false) => Flat(distance: distance, bigAlien: bigAlien,
        edit: rules => rules.Replace(GunText, GunText + "\n    tuSnap: 20\n    tuAimed: 50", StringComparison.Ordinal));

    private static BattlePosition At(int x, int y) => new(x, y, 0);

    private static UnitTurned TurnEast(Arena arena) =>
        Assert.IsType<UnitTurned>(Assert.Single(arena.Campaign.Execute(new TurnUnit(arena.Soldier.Id, At(15, 10))).Events));

    private static string Refusal(CampaignCommandResult result) => Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events)).Code;

    private static int UfoCost(BattleUnit unit) => unit.MovementType switch { 0 => 6, 1 => 10, _ => 8 };

    // ---- the swinging door ----

    [Fact]
    public void ASwingingDoorInFrontOfAUnitThatDoesNotTurnOpensIntoItsAlternativeAndPaysThatPartsCost()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorCost);
        var (units, revision) = (soldier.TimeUnits, arena.Battle.Revision);
        var turned = TurnEast(arena);
        // The door moved to the north wall part as the open door; right clicking pays for that part (3), not the door's own cost (5).
        Assert.Equal(new UnitTurned(soldier.Id, 2, 0, 3, BattleTurnStop.NoTurn, BattleDoorResult.Opened, 1, At(11, 10)), turned);
        var tile = arena.Tile(11, 10);
        Assert.Null(arena.Map.MapData(tile, BattleTilePart.WestWall));
        Assert.Equal(OpenDoorPart, arena.Map.Part(tile, BattleTilePart.NorthWall).Id);
        Assert.Equal((units - 3, revision + 1), (soldier.TimeUnits, arena.Battle.Revision));
        // Once open there is nothing to open: the next try does nothing.
        Assert.Equal(new UnitTurned(soldier.Id, 2, 0, 0, BattleTurnStop.NoTurn), TurnEast(arena));
        Assert.Equal(units - 3, soldier.TimeUnits);
    }

    [Fact]
    public void ADoorOnlyOpensFromTheTilesTheDirectionLooksAtAndATurnThatTurnsNeverTriesIt()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorCost);
        // Facing south the unit looks at the north wall of the tile south of it, so the east door is left alone and a real turn skips doors.
        var turned = Assert.IsType<UnitTurned>(Assert.Single(arena.Campaign.Execute(new TurnUnit(soldier.Id, At(10, 15))).Events));
        Assert.Equal(BattleTurnStop.Arrived, turned.Stop);
        Assert.Equal(BattleDoorResult.None, turned.Door);
        Assert.Equal(SwingDoorCost, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.WestWall).Id);
        // Facing south already (no turn), the unit tries the door of the tile south of it.
        soldier.SetDirection(4);
        arena.Put(10, 11, 0, BattleTilePart.NorthWall, UfoDoorCost);
        var south = Assert.IsType<UnitTurned>(Assert.Single(arena.Campaign.Execute(new TurnUnit(soldier.Id, At(10, 15))).Events));
        Assert.Equal((BattleDoorResult.UfoDoorOpened, At(10, 11)), (south.Door, south.DoorCentre!.Value));
    }

    [Fact]
    public void ADoorThatMovesToItsOwnPartIsLeftEmptyAsTheReferencesTwoCallsDo()
    {
        var arena = Make();
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorSameWall);
        var turned = TurnEast(arena);
        // The alternative is a west wall: it is put in the west wall part, and then that part is emptied. The cost is the north wall's (none).
        Assert.Equal((BattleDoorResult.Opened, 0), (turned.Door, turned.TimeUnitsSpent));
        Assert.Equal(-1, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.WestWall).Id);
        Assert.Equal(-1, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.NorthWall).Id);
    }

    [Fact]
    public void ADoorWhoseAlternativeIsPastTheDataSetIsNotOpened()
    {
        var arena = Make();
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorNowhere);
        var revision = arena.Battle.Revision;
        var turned = TurnEast(arena);
        Assert.Equal(BattleDoorResult.None, turned.Door);
        Assert.Equal((SwingDoorNowhere, revision), (arena.Map.Part(arena.Tile(11, 10), BattleTilePart.WestWall).Id, arena.Battle.Revision));
    }

    [Fact]
    public void OnlyTheOtherTilesOfABigUnitKeepADoorShutNotAUnitOnTheDoorsOwnTile()
    {
        var arena = Make(bigAlien: true);
        var (soldier, tank) = (arena.Soldier, arena.Alien);
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorCost);
        // Tile::openDoor refuses when the unit it holds stands elsewhere than the tile's position: a big unit covering the door's tile from
        // (11, 9) stops it, and the door stays.
        arena.Stand(tank, 11, 9, 6);
        Assert.Equal((tank.Id, tank.Position), (arena.Map.UnitAt(arena.Tile(11, 10)), (BattlePosition?)At(11, 9)));
        var revision = arena.Battle.Revision;
        Assert.Equal(BattleDoorResult.None, TurnEast(arena).Door);
        Assert.Equal((SwingDoorCost, revision), (arena.Map.Part(arena.Tile(11, 10), BattleTilePart.WestWall).Id, arena.Battle.Revision));
        // A unit whose position is the door's tile does not stop it (the reference's test), here the tank itself.
        arena.Stand(tank, 11, 10, 6);
        Assert.Equal(BattleDoorResult.Opened, TurnEast(arena).Door);
        Assert.Equal(soldier.Id, arena.Map.UnitAt(arena.Tile(10, 10)));
    }

    [Fact]
    public void ABigUnitCannotOpenASwingingDoorButOpensAUfoDoor()
    {
        var arena = Make(distance: 15, bigAlien: true);
        var tank = arena.Alien;
        var at = tank.Position!.Value;
        tank.SetDirection(2);
        arena.Put(at.X + 2, at.Y, 0, BattleTilePart.WestWall, SwingDoorCost);
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, tank, true, -1, out var swing));
        Assert.Equal(BattleDoorAttempt.None, swing);
        arena.Put(at.X + 2, at.Y, 0, BattleTilePart.WestWall, UfoDoorCost);
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, tank, true, -1, out var ufo));
        Assert.Equal((BattleDoorResult.UfoDoorOpened, 1, UfoCost(tank)), (ufo.Result, ufo.DoorsOpened, ufo.TimeUnitsSpent));
        // Direction 0 checks the west wall of the tile north of the second column only.
        arena.Map.SetDoorFrame(arena.Tile(at.X + 2, at.Y), BattleTilePart.WestWall, false);
        arena.Clear(at.X + 2, at.Y, 0, BattleTilePart.WestWall);
        tank.TimeUnits = 100;
        tank.SetDirection(0);
        arena.Put(at.X, at.Y - 1, 0, BattleTilePart.WestWall, UfoDoorCost);
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, tank, true, -1, out var none));
        Assert.Equal(BattleDoorResult.None, none.Result);
        arena.Put(at.X + 1, at.Y - 1, 0, BattleTilePart.WestWall, UfoDoorCost);
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, tank, true, -1, out var found));
        Assert.Equal(BattleDoorResult.UfoDoorOpened, found.Result);
    }

    [Fact]
    public void AUnitOnStairsLooksOneLevelUp()
    {
        var arena = Make(distance: 15);
        var soldier = arena.Soldier;
        arena.Put(10, 10, 0, BattleTilePart.Floor, StairFloor);
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorCost);
        Assert.Equal(BattleDoorResult.None, TurnEast(arena).Door);
        arena.Put(11, 10, 1, BattleTilePart.WestWall, SwingDoorCost);
        var turned = TurnEast(arena);
        Assert.Equal((BattleDoorResult.Opened, new BattlePosition(11, 10, 1)), (turned.Door, turned.DoorCentre!.Value));
        Assert.Equal(OpenDoorPart, arena.Map.Part(arena.Tile(11, 10, 1), BattleTilePart.NorthWall).Id);
        Assert.Equal(SwingDoorCost, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.WestWall).Id);
        Assert.Equal(soldier.Id, arena.Map.UnitAt(arena.Tile(10, 10)));
    }

    // ---- the UFO door ----

    [Fact]
    public void AUfoDoorOpensAtOnceWithTheDoorsBesideItAndSightAndKnowledgeFollow()
    {
        var arena = Make();
        var (soldier, alien) = (arena.Soldier, arena.Alien);
        // A row of UFO doors in the wall east of the soldier, from y = 9 to y = 13, and the soldier at one end of it.
        for (var y = 9; y <= 13; y++) arena.Put(11, y, 0, BattleTilePart.WestWall, UfoDoorCost);
        arena.See();
        Assert.False(Arena.Sees(soldier, alien));
        var units = soldier.TimeUnits;
        Array.Fill(soldier.TileLastSpottedBy, -1);
        Array.Fill(soldier.TurnsSinceSeenBy, 255);
        var turned = TurnEast(arena);
        // Starting at y = 10: three doors to the south, one to the north, so the net offset is 2 and the centre one tile south.
        Assert.Equal(new UnitTurned(soldier.Id, 2, 0, UfoCost(soldier), BattleTurnStop.NoTurn, BattleDoorResult.UfoDoorOpened, 5, At(11, 11)), turned);
        for (var y = 9; y <= 13; y++) Assert.True(arena.Map.IsUfoDoorOpen(arena.Tile(11, y), BattleTilePart.WestWall), $"y {y}");
        Assert.Equal(units - UfoCost(soldier), soldier.TimeUnits);
        // The field of view through the doorway: the soldier now sees the alien.
        Assert.True(Arena.Sees(soldier, alien));
        // updateEnemyKnowledge with the door flag: the other factions learn the tile, the player's side does not.
        var tile = arena.Tile(10, 10);
        Assert.Equal((tile, tile, 0), (soldier.TileLastSpottedBy[(int)BattleFaction.Hostile], soldier.TileLastSpottedForBlindShotBy[(int)BattleFaction.Hostile],
            soldier.TurnsSinceSeenBy[(int)BattleFaction.Hostile]));
        Assert.Equal((tile, 0), (soldier.TileLastSpottedBy[(int)BattleFaction.Neutral], soldier.TurnsSinceSeenBy[(int)BattleFaction.Neutral]));
        Assert.Equal((-1, 255), (soldier.TileLastSpottedBy[(int)BattleFaction.Player], soldier.TurnsSinceSeenBy[(int)BattleFaction.Player]));
    }

    [Fact]
    public void AnAlreadyOpenDoorInTheRowDoesNotStopTheRunAndIsNotCounted()
    {
        var arena = Make();
        for (var y = 9; y <= 13; y++) arena.Put(11, y, 0, BattleTilePart.WestWall, UfoDoorCost);
        arena.Map.SetDoorFrame(arena.Tile(11, 12), BattleTilePart.WestWall, true);
        var turned = TurnEast(arena);
        // South: 11 opens (+1), 12 is open (skipped), 13 opens (+1); north: 9 opens (-1); the net offset 1 halves to 0.
        Assert.Equal((BattleDoorResult.UfoDoorOpened, 4, At(11, 10)), (turned.Door, turned.DoorsOpened, turned.DoorCentre!.Value));
        for (var y = 9; y <= 13; y++) Assert.True(arena.Map.IsUfoDoorOpen(arena.Tile(11, y), BattleTilePart.WestWall));
    }

    [Fact]
    public void ANorthWallUfoDoorRunsAlongTheRowAndItsLightIsRefreshed()
    {
        var arena = Make();
        arena.Stand(arena.Soldier, 10, 10, 4);
        for (var x = 8; x <= 12; x++) arena.Put(x, 11, 0, BattleTilePart.NorthWall, UfoDoorCost);
        arena.Relight();
        Assert.True(arena.Map.LightCache(arena.Tile(10, 10)).Blocks(4, 0));
        var turned = Assert.IsType<UnitTurned>(Assert.Single(arena.Campaign.Execute(new TurnUnit(arena.Soldier.Id, At(10, 15))).Events));
        // Two doors west and two east of x = 10: the centre stays at x = 10.
        Assert.Equal((BattleDoorResult.UfoDoorOpened, 5, At(10, 11)), (turned.Door, turned.DoorsOpened, turned.DoorCentre!.Value));
        // The blockage cache was refreshed through the doorway: an open door lets light and sight pass.
        Assert.False(arena.Map.LightCache(arena.Tile(10, 10)).Blocks(4, 0));
    }

    [Fact]
    public void ADoorThatIsNotSoughtIsLeftAloneWhateverItsPart()
    {
        // Every direction looks at its own tiles and no others: a UFO door put on each neighbouring part opens only where the table says so.
        foreach (var rightClick in new[] { true, false })
        {
            var arena = Make();
            var soldier = arena.Soldier;
            for (var direction = 0; direction < 8; direction++)
            {
                soldier.SetDirection(direction);
                foreach (var part in new[] { BattleTilePart.WestWall, BattleTilePart.NorthWall })
                    for (var dx = -1; dx <= 1; dx++)
                        for (var dy = -1; dy <= 1; dy++)
                        {
                            arena.Put(10 + dx, 10 + dy, 0, part, UfoDoorCost);
                            soldier.TimeUnits = 60;
                            Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, soldier, rightClick, -1, out var attempt));
                            var expected = (rightClick ? RightClickDoors : WalkingDoors)[direction].Contains((dx, dy, part));
                            Assert.True(expected == (attempt.Result == BattleDoorResult.UfoDoorOpened), $"{(rightClick ? "right click" : "walking")} direction {direction}, ({dx}, {dy}) {part}");
                            arena.Map.SetDoorFrame(arena.Tile(10 + dx, 10 + dy), part, false);
                            arena.Clear(10 + dx, 10 + dy, 0, part);
                        }
            }
        }
    }

    private static readonly (int Dx, int Dy, BattleTilePart Part)[][] WalkingDoors =
    [
        [(0, 0, BattleTilePart.NorthWall)],
        [(0, 0, BattleTilePart.NorthWall), (1, -1, BattleTilePart.WestWall)],
        [(1, 0, BattleTilePart.WestWall)],
        [(1, 1, BattleTilePart.WestWall), (1, 1, BattleTilePart.NorthWall)],
        [(0, 1, BattleTilePart.NorthWall)],
        [(0, 0, BattleTilePart.WestWall), (-1, 1, BattleTilePart.NorthWall)],
        [(0, 0, BattleTilePart.WestWall)],
        [(0, 0, BattleTilePart.WestWall), (0, 0, BattleTilePart.NorthWall)],
    ];

    private static readonly (int Dx, int Dy, BattleTilePart Part)[][] RightClickDoors =
    [
        [(0, 0, BattleTilePart.NorthWall)],
        [(0, 0, BattleTilePart.NorthWall), (1, -1, BattleTilePart.WestWall), (1, 0, BattleTilePart.WestWall), (1, 0, BattleTilePart.NorthWall)],
        [(1, 0, BattleTilePart.WestWall)],
        [(1, 1, BattleTilePart.WestWall), (1, 1, BattleTilePart.NorthWall), (1, 0, BattleTilePart.WestWall), (0, 1, BattleTilePart.NorthWall)],
        [(0, 1, BattleTilePart.NorthWall)],
        [(0, 0, BattleTilePart.WestWall), (-1, 1, BattleTilePart.NorthWall), (0, 1, BattleTilePart.WestWall), (0, 1, BattleTilePart.NorthWall)],
        [(0, 0, BattleTilePart.WestWall)],
        [(0, 0, BattleTilePart.WestWall), (0, 0, BattleTilePart.NorthWall), (0, -1, BattleTilePart.WestWall), (-1, 0, BattleTilePart.NorthWall)],
    ];

    [Fact]
    public void ADirectionOtherThanTheUnitsOwnCanBeAsked()
    {
        var arena = Make();
        arena.Put(10, 11, 0, BattleTilePart.NorthWall, UfoDoorCost);
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, arena.Soldier, true, -1, out var east));
        Assert.Equal(BattleDoorResult.None, east.Result);
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, arena.Soldier, true, 4, out var south));
        Assert.Equal(BattleDoorResult.UfoDoorOpened, south.Result);
    }

    // ---- paying: result 4 and result 5, the walking form, the reserve ----

    [Fact]
    public void NotEnoughTimeUnitsForTheDoorLeavesEverythingAsItWas()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorCost);
        soldier.TimeUnits = 4;
        var revision = arena.Battle.Revision;
        var turned = TurnEast(arena);
        Assert.Equal(new UnitTurned(soldier.Id, 2, 0, 0, BattleTurnStop.NoTurn, BattleDoorResult.NotEnoughTimeUnits), turned);
        Assert.Equal((4, revision, SwingDoorCost), (soldier.TimeUnits, arena.Battle.Revision, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.WestWall).Id));
        // Five is enough for the check (the door's own cost), whatever the open door costs.
        soldier.TimeUnits = 5;
        Assert.Equal((BattleDoorResult.Opened, 2), (TurnEast(arena).Door, soldier.TimeUnits));
    }

    [Fact]
    public void TheReservedTimeUnitsCountTowardsTheDoorsCheck()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var gun = arena.Battle.InventoryOf(soldier).Single(static item => item.ItemType == "GUN" && item.Slot == "STR_RIGHT_HAND");
        arena.Battle.TimeUnitsReserved = (int)BattleActionType.AimedShot;
        var reserve = BattleActionCost.For(arena.Rules, BattleActionType.AimedShot, soldier, gun).Time;
        Assert.True(reserve > 0);
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorCost);
        // The door needs its 5 on top of the reserve (the check in Tile::openDoor): one short is result 4.
        soldier.TimeUnits = reserve + 4;
        Assert.Equal(BattleDoorResult.NotEnoughTimeUnits, TurnEast(arena).Door);
        Assert.Equal(SwingDoorCost, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.WestWall).Id);
        soldier.TimeUnits = reserve + 5;
        Assert.Equal((BattleDoorResult.Opened, reserve + 2), (TurnEast(arena).Door, soldier.TimeUnits));
    }

    [Fact]
    public void ADoorThatCostsNothingButOpensIntoACostlyPartOpensWithoutPayingWhenTheReserveForbidsIt()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var gun = arena.Battle.InventoryOf(soldier).Single(static item => item.ItemType == "GUN" && item.Slot == "STR_RIGHT_HAND");
        arena.Battle.TimeUnitsReserved = (int)BattleActionType.AimedShot;
        var reserve = BattleActionCost.For(arena.Rules, BattleActionType.AimedShot, soldier, gun).Time;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorFree);
        // The door itself is free, so Tile::openDoor passes with just the reserve; the part it opens into costs 8, which would eat the reserve.
        soldier.TimeUnits = reserve + 7;
        var revision = arena.Battle.Revision;
        var turned = TurnEast(arena);
        Assert.Equal(new UnitTurned(soldier.Id, 2, 0, 0, BattleTurnStop.NoTurn, BattleDoorResult.ReservedTimeUnits, 1, At(11, 10)), turned);
        // Result 5 leaves the door open, with nothing paid.
        Assert.Equal((reserve + 7, revision + 1), (soldier.TimeUnits, arena.Battle.Revision));
        Assert.Equal(CostlyOpenDoor, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.NorthWall).Id);
        Assert.Equal(-1, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.WestWall).Id);
    }

    [Fact]
    public void ADoorOpenedBeforeTheUnitCannotPayStaysOpenAndSaysNotEnough()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorFree);
        soldier.TimeUnits = 5;
        var revision = arena.Battle.Revision;
        var turned = TurnEast(arena);
        // No reserve, so Tile::openDoor had nothing to check; spending the open door's 8 then fails, after the door moved.
        Assert.Equal(new UnitTurned(soldier.Id, 2, 0, 0, BattleTurnStop.NoTurn, BattleDoorResult.NotEnoughTimeUnits), turned with { DoorsOpened = 0, DoorCentre = null });
        // The port still reports the door that opened: one part, at the door's tile.
        Assert.Equal((1, (BattlePosition?)At(11, 10)), (turned.DoorsOpened, turned.DoorCentre));
        Assert.Equal((5, revision + 1), (soldier.TimeUnits, arena.Battle.Revision));
        Assert.Equal(CostlyOpenDoor, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.NorthWall).Id);
    }

    [Fact]
    public void TheKneelReserveAddsToTheDoorsCheckForAStandingUnit()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Battle.KneelReserved = true;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, UfoDoorCost);
        var cost = UfoCost(soldier);
        // The check wants the door's cost and the kneel (4); the reserve check then wants the same, and the door costs only its own.
        soldier.TimeUnits = cost + 3;
        Assert.Equal(BattleDoorResult.NotEnoughTimeUnits, TurnEast(arena).Door);
        Assert.False(arena.Map.IsUfoDoorOpen(arena.Tile(11, 10), BattleTilePart.WestWall));
        soldier.TimeUnits = cost + 4;
        Assert.Equal((BattleDoorResult.UfoDoorOpened, 4), (TurnEast(arena).Door, soldier.TimeUnits));
    }

    [Fact]
    public void AKneelingUnitOrOneWhoseArmorForbidsKneelingAddsNoKneelReserve()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Battle.KneelReserved = true;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, UfoDoorCost);
        var cost = UfoCost(soldier);
        soldier.Kneel(true);
        soldier.TimeUnits = cost;
        Assert.Equal((BattleDoorResult.UfoDoorOpened, 0), (TurnEast(arena).Door, soldier.TimeUnits));
    }

    [Fact]
    public void AUfoDoorNeedsTheAimedShotReservePlusItsCost()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        var gun = arena.Battle.InventoryOf(soldier).Single(static item => item.ItemType == "GUN" && item.Slot == "STR_RIGHT_HAND");
        arena.Battle.TimeUnitsReserved = (int)BattleActionType.AimedShot;
        var reserve = BattleActionCost.For(arena.Rules, BattleActionType.AimedShot, soldier, gun).Time;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, UfoDoorCost);
        // Result 5 needs the door's cost to pass Tile::openDoor and fail checkReservedTU; the kneel reserve makes openDoor stricter, so
        // with the reserve on aimed shots alone both ask for the same: the UFO door is paid or refused as result 4, never 5.
        soldier.TimeUnits = reserve + UfoCost(soldier) - 1;
        Assert.Equal(BattleDoorResult.NotEnoughTimeUnits, TurnEast(arena).Door);
        Assert.False(arena.Map.IsUfoDoorOpen(arena.Tile(11, 10), BattleTilePart.WestWall));
    }

    [Fact]
    public void AHostileUnitThatCannotAffordADoorGetsResultFourNotTheReserveRefusal()
    {
        var arena = Make();
        var alien = arena.Alien;
        arena.Battle.Side = BattleFaction.Hostile;
        arena.Put(15, 10, 0, BattleTilePart.WestWall, UfoDoorCost);
        alien.TimeUnits = 1;
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, alien, true, -1, out var attempt));
        Assert.Equal(BattleDoorResult.NotEnoughTimeUnits, attempt.Result);
        Assert.False(arena.Map.IsUfoDoorOpen(arena.Tile(15, 10), BattleTilePart.WestWall));
    }

    [Fact]
    public void ADoorRunStopsAtTheMapEdge()
    {
        var arena = Make();
        arena.Put(11, 1, 0, BattleTilePart.WestWall, UfoDoorCost);
        arena.Put(11, 0, 0, BattleTilePart.WestWall, UfoDoorCost);
        arena.Stand(arena.Soldier, 10, 1, 2);
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, arena.Soldier, true, -1, out var attempt));
        Assert.Equal((BattleDoorResult.UfoDoorOpened, 2), (attempt.Result, attempt.DoorsOpened));
    }

    [Fact]
    public void TheWalkingFormPaysEnergyAndNoTimeUnitsForASwingingDoor()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, SwingDoorCost);
        // A walk's door costs 5 time units and 2 energy to be allowed, but opening pays nothing here (the step pays).
        soldier.Energy = 1;
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, soldier, false, -1, out var poor));
        Assert.Equal(BattleDoorResult.NotEnoughTimeUnits, poor.Result);
        Assert.Equal(SwingDoorCost, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.WestWall).Id);
        soldier.Energy = 2;
        var units = soldier.TimeUnits;
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, soldier, false, -1, out var open));
        Assert.Equal((BattleDoorResult.Opened, 0, units), (open.Result, open.TimeUnitsSpent, soldier.TimeUnits));
        Assert.Equal(OpenDoorPart, arena.Map.Part(arena.Tile(11, 10), BattleTilePart.NorthWall).Id);
    }

    [Fact]
    public void TheWalkingFormPaysAUfoDoorsCostAtOnce()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        arena.Put(11, 10, 0, BattleTilePart.WestWall, UfoDoorCost);
        var units = soldier.TimeUnits;
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, soldier, false, 2, out var open));
        Assert.Equal((BattleDoorResult.UfoDoorOpened, UfoCost(soldier)), (open.Result, open.TimeUnitsSpent));
        Assert.Equal(units - UfoCost(soldier), soldier.TimeUnits);
    }

    [Fact]
    public void TheHostileSidesReserveIsRefusedBeforeTheDoorOpensAndNothingIsRefusedWithoutADoor()
    {
        var arena = Make();
        var alien = arena.Alien;
        arena.Battle.Side = BattleFaction.Hostile;
        Assert.Null(arena.Battle.UnitOpensDoor(arena.Rules, alien, true, -1, out var none));
        Assert.Equal(BattleDoorResult.None, none.Result);
        arena.Put(15, 10, 0, BattleTilePart.WestWall, UfoDoorCost);
        var reason = arena.Battle.UnitOpensDoor(arena.Rules, alien, true, -1, out var refused);
        Assert.Equal("battle.hostile-reserve-unsupported", reason!.Code);
        Assert.Equal(BattleDoorAttempt.None, refused);
        Assert.False(arena.Map.IsUfoDoorOpen(arena.Tile(15, 10), BattleTilePart.WestWall));
    }

    // ---- closing UFO doors ----

    [Fact]
    public void CloseUfoDoorsClosesEachTilesDoorsOnceExceptWhereABigUnitStandsInTheDoorway()
    {
        var arena = Make(bigAlien: true);
        var tank = arena.Alien;
        arena.Stand(tank, 11, 10, 6); // covers (11, 10), (12, 10), (11, 11) and (12, 11)
        void Open(int x, int y, BattleTilePart part)
        {
            arena.Put(x, y, 0, part, UfoDoorCost);
            arena.Map.SetDoorFrame(arena.Tile(x, y), part, true);
        }
        Open(14, 10, BattleTilePart.WestWall);
        Open(14, 10, BattleTilePart.NorthWall); // the same tile's second door: still one
        Open(11, 11, BattleTilePart.NorthWall); // north of it is the tank's own tile: kept
        Open(12, 10, BattleTilePart.WestWall); // west of it is the tank's own tile: kept
        Open(12, 11, BattleTilePart.WestWall); // west is the tank's tile (11, 11): kept
        Open(11, 10, BattleTilePart.NorthWall); // north of it is empty: closed
        arena.Put(16, 10, 0, BattleTilePart.WestWall, SwingDoorCost);
        Assert.Equal(2, arena.Battle.CloseUfoDoors());
        Assert.False(arena.Map.IsUfoDoorOpen(arena.Tile(14, 10), BattleTilePart.WestWall));
        Assert.False(arena.Map.IsUfoDoorOpen(arena.Tile(14, 10), BattleTilePart.NorthWall));
        Assert.False(arena.Map.IsUfoDoorOpen(arena.Tile(11, 10), BattleTilePart.NorthWall));
        Assert.True(arena.Map.IsUfoDoorOpen(arena.Tile(11, 11), BattleTilePart.NorthWall));
        Assert.True(arena.Map.IsUfoDoorOpen(arena.Tile(12, 10), BattleTilePart.WestWall));
        Assert.True(arena.Map.IsUfoDoorOpen(arena.Tile(12, 11), BattleTilePart.WestWall));
        Assert.Equal(SwingDoorCost, arena.Map.Part(arena.Tile(16, 10), BattleTilePart.WestWall).Id);
        // Nothing left to close once the tank leaves.
        arena.Stand(tank, 20, 20, 6);
        Assert.Equal(3, arena.Battle.CloseUfoDoors());
        Assert.Equal(0, arena.Battle.CloseUfoDoors());
    }

    [Fact]
    public void ASmallUnitInTheDoorwayDoesNotKeepADoorOpen()
    {
        var arena = Make();
        arena.Put(11, 10, 0, BattleTilePart.NorthWall, UfoDoorCost);
        arena.Map.SetDoorFrame(arena.Tile(11, 10), BattleTilePart.NorthWall, true);
        arena.Stand(arena.Alien, 11, 9, 6);
        Assert.Equal(1, arena.Battle.CloseUfoDoors());
    }
}
