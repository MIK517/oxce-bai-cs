using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Xunit;
using static Oxce.CompatibilityTests.BattleSightTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S7: gravity and falling (<c>TileEngine::applyGravity</c>, <c>UnitFallBState</c>, <c>SavedBattleGame::addFallingUnit</c>).
/// The arena is the sight tests' one made four levels high: the soldier at (10, 10) and the alien five tiles east on the ground floor,
/// the soldier a flier unless a test says otherwise, the alien a walker.
/// </summary>
public sealed class BattleGravityTests
{
    private const string GunText = "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1";

    private static string Replaced(string rules, string from, string to)
    {
        var result = rules.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(rules, result);
        return result;
    }

    private static Arena Tall(bool walkingSoldier = false, string suitExtra = "", bool reactive = false, bool bigAlien = false, string grenadeEvents = "", bool proximityGrenade = false) =>
        Flat(bigAlien: bigAlien, edit: rules =>
        {
            rules = Replaced(rules, "    shade: 0\n    width: 30\n    length: 30\n    height: 2", "    shade: 0\n    width: 30\n    length: 30\n    height: 4");
            if (walkingSoldier) rules = Replaced(rules, "  - type: SUIT\n    movementType: 1\n", "  - type: SUIT\n    movementType: 0\n");
            if (suitExtra.Length != 0) rules = Replaced(rules, "  - type: SUIT\n", "  - type: SUIT\n" + suitExtra);
            if (reactive)
            {
                rules = Replaced(rules, "stats: {tu: 50, firing: 61}", "stats: {tu: 50, firing: 61, reactions: 100}");
                rules = Replaced(rules, GunText, GunText + "\n    tuSnap: 20\n    tuAimed: 50");
            }
            if (grenadeEvents.Length != 0)
                rules = Replaced(rules, "- type: GRENADE\n    battleType: 4\n    weight: 1", "- type: GRENADE\n    battleType: 4\n    weight: 1\n    fuseTriggerEvents: " + grenadeEvents);
            if (proximityGrenade) rules = Replaced(rules, "- type: GRENADE\n    battleType: 4", "- type: GRENADE\n    battleType: 5");
            return rules;
        });

    private static BattlePosition At(int x, int y, int z) => new(x, y, z);

    private static void Raise(Arena arena, BattleUnit unit, int x, int y, int z) => arena.Battle.SetTile(unit, At(x, y, z));

    /// <summary>The state a refused trial run must leave as it found it.</summary>
    private static string State(Arena arena) => string.Join('|', arena.Battle.Units.Select(unit =>
        $"{unit.Id}:{unit.Position}:{unit.LastPosition}:{unit.Status}:{unit.Floating}:{unit.HaveNoFloorBelow}:{unit.MotionPoints}:{unit.Kneeled}")) +
        $"|{arena.Battle.Revision}|{string.Join(',', arena.Battle.FallingUnits.Select(static unit => unit.Id))}|" +
        string.Join(',', Enumerable.Range(0, arena.Map.TileCount).Where(tile => arena.Map.UnitAt(tile) is not null).Select(tile => $"{tile}={arena.Map.UnitAt(tile)}"));

    private static BattleItem Drop(Arena arena, string type, BattlePosition tile) =>
        arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), type), tile);

    private static BattleItem GunFor(Arena arena, BattleUnit unit)
    {
        var gun = new BattleItem(arena.Battle.TakeItemId(), "GUN") { OwnerUnitId = unit.Id, Slot = "STR_RIGHT_HAND", IsWeaponWithAmmo = true };
        arena.Battle.AddItem(gun);
        var clip = new BattleItem(arena.Battle.TakeItemId(), "CLIP") { AmmoQuantity = 5, IsAmmo = true };
        arena.Battle.AddItem(clip);
        gun.AmmoSlots[0] = clip.Id;
        return gun;
    }

    [Fact]
    public void AUnitWithoutAFloorFallsLevelByLevelToTheFloorBelow()
    {
        var arena = Tall();
        var alien = arena.Alien;
        Raise(arena, alien, 12, 12, 3);
        var motion = alien.MotionPoints;
        Assert.Equal(At(12, 12, 0), arena.Battle.ApplyGravity(alien.Position));
        Assert.Same(alien, Assert.Single(arena.Battle.FallingUnits));
        Assert.Null(arena.Battle.FallsRefusal(arena.Rules));
        // Asking changed nothing: the unit still hangs where it was.
        Assert.Equal((At(12, 12, 3), true), (alien.Position, alien.HaveNoFloorBelow));
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        Assert.Equal((At(12, 12, 0), At(12, 12, 1), false, false, BattleUnitStatus.Standing),
            (alien.Position, alien.LastPosition, alien.HaveNoFloorBelow, alien.Floating, alien.Status));
        Assert.Empty(arena.Battle.FallingUnits);
        Assert.Equal(alien.Id, arena.Map.UnitAt(arena.Tile(12, 12, 0)));
        Assert.Null(arena.Map.UnitAt(arena.Tile(12, 12, 3)));
        // Each level of the fall is a step of a unit taller than 16: 4 motion points.
        Assert.Equal(motion + 12, alien.MotionPoints);
    }

    [Fact]
    public void AUnitThatStandsOnFloorStaysAndANothingTileChangesNothing()
    {
        var arena = Tall();
        Raise(arena, arena.Alien, 12, 12, 2);
        arena.Put(12, 12, 2, BattleTilePart.Floor, Floor);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        Assert.Empty(arena.Battle.FallingUnits);
        Assert.Equal((At(12, 12, 2), false), (arena.Alien.Position, arena.Alien.HaveNoFloorBelow));
        Assert.Null(arena.Battle.ApplyGravity(null));
        Assert.Equal(At(20, 20, 3), arena.Battle.ApplyGravity(At(20, 20, 3)));
    }

    [Fact]
    public void AFlyingUnitOverAHoleHoversWhereItIsAndStopsKneeling()
    {
        var arena = Tall();
        var soldier = arena.Soldier;
        Assert.Equal(1, soldier.MovementType);
        Raise(arena, soldier, 12, 12, 2);
        soldier.Kneeled = true;
        soldier.Status = BattleUnitStatus.Turning;
        arena.Battle.ApplyGravity(soldier.Position);
        Assert.Empty(arena.Battle.FallingUnits);
        Assert.Equal((At(12, 12, 2), true, false, BattleUnitStatus.Standing, At(12, 12, 2)),
            (soldier.Position, soldier.Floating, soldier.Kneeled, soldier.Status, soldier.LastPosition));
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        Assert.Equal(At(12, 12, 2), soldier.Position);
    }

    [Fact]
    public void AnUnconsciousUnitIsNotQueuedButItsBodyDropsWithTheTilesItems()
    {
        var arena = Tall();
        var alien = arena.Alien;
        Raise(arena, alien, 14, 14, 3);
        alien.StunLevel = alien.Health;
        alien.Status = BattleUnitStatus.Unconscious;
        var clip = Drop(arena, "CLIP", At(14, 14, 3));
        var body = arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = alien.Id }, At(14, 14, 3));
        Assert.Equal(At(14, 14, 0), arena.Battle.ApplyGravity(alien.Position));
        Assert.Empty(arena.Battle.FallingUnits);
        Assert.Equal((At(14, 14, 0), At(14, 14, 0), At(14, 14, 0)), (clip.Tile!.Value, body.Tile!.Value, alien.Position!.Value));
        Assert.Empty(arena.Battle.ItemsOnTile(At(14, 14, 3)));
        Assert.Equal(2, arena.Battle.ItemsOnTile(At(14, 14, 0)).Count());
    }

    [Fact]
    public void ItemsOverAHoleDropToTheFloorBelowAndStayWhereThereIsAFloorOrNoLevelBelow()
    {
        var arena = Tall();
        var clip = Drop(arena, "CLIP", At(16, 16, 2));
        arena.Put(16, 16, 1, BattleTilePart.Floor, Floor);
        Assert.Equal(At(16, 16, 1), arena.Battle.ApplyGravity(At(16, 16, 2)));
        Assert.Equal(At(16, 16, 1), clip.Tile);
        var low = Drop(arena, "CLIP", At(18, 18, 0));
        arena.Clear(18, 18, 0, BattleTilePart.Floor);
        Assert.Equal(At(18, 18, 0), arena.Battle.ApplyGravity(At(18, 18, 0)));
        Assert.Equal(At(18, 18, 0), low.Tile);
        var kept = Drop(arena, "CLIP", At(16, 16, 1));
        arena.Battle.ApplyGravity(At(16, 16, 1));
        Assert.Equal(At(16, 16, 1), kept.Tile);
    }

    [Fact]
    public void AUnitLandingOnAnotherMovesItAsideAndTakesItsTile()
    {
        var arena = Tall();
        arena.Stand(arena.Soldier, 12, 12, 2);
        Raise(arena, arena.Alien, 12, 12, 2);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        Assert.Null(arena.Battle.FallsRefusal(arena.Rules));
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        // The soldier flies, so the first direction (north) is open to it; the alien takes the tile it left.
        Assert.Equal(At(12, 11, 0), arena.Soldier.Position);
        Assert.Equal(At(12, 12, 0), arena.Alien.Position);
        Assert.Equal(arena.Soldier.Id, arena.Map.UnitAt(arena.Tile(12, 11, 0)));
        Assert.Equal(arena.Alien.Id, arena.Map.UnitAt(arena.Tile(12, 12, 0)));
        Assert.Null(arena.Map.UnitAt(arena.Tile(12, 12, 1)));
        Assert.Empty(arena.Battle.FallingUnits);
        // startWalking turned it to the direction it moved.
        Assert.Equal(0, arena.Soldier.Direction);
    }

    [Fact]
    public void AWalkerBelowTakesTheFirstDirectionWithAFloor()
    {
        var arena = Tall(walkingSoldier: true);
        arena.Stand(arena.Soldier, 12, 12, 2);
        arena.Clear(12, 11, 0, BattleTilePart.Floor);
        Raise(arena, arena.Alien, 12, 12, 1);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        // North has no floor, so the soldier steps north east.
        Assert.Equal(At(13, 11, 0), arena.Soldier.Position);
        Assert.Equal(At(12, 12, 0), arena.Alien.Position);
    }

    [Fact]
    public void ABigUnitFallsOntoFourTilesAndTheEscapeAvoidsThoseTiles()
    {
        var arena = Tall(walkingSoldier: true, bigAlien: true);
        arena.Stand(arena.Soldier, 13, 13, 2);
        Raise(arena, arena.Alien, 12, 12, 1);
        Assert.True(arena.Alien.IsBigUnit);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        Assert.Same(arena.Alien, Assert.Single(arena.Battle.FallingUnits));
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        // North (13, 12) is one of the four tiles about to be taken, so the soldier takes north east.
        Assert.Equal(At(14, 12, 0), arena.Soldier.Position);
        Assert.Equal(At(12, 12, 0), arena.Alien.Position);
        foreach (var tile in new[] { At(12, 12, 0), At(13, 12, 0), At(12, 13, 0), At(13, 13, 0) })
            Assert.Equal(arena.Alien.Id, arena.Map.UnitAt(arena.Map.TileIndex(tile)));
    }

    [Fact]
    public void ABigUnitWithAFloorUnderAnyOfItsTilesDoesNotFall()
    {
        var arena = Tall(bigAlien: true);
        Raise(arena, arena.Alien, 12, 12, 2);
        arena.Put(13, 13, 2, BattleTilePart.Floor, Floor);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        Assert.Empty(arena.Battle.FallingUnits);
        Assert.False(arena.Alien.HaveNoFloorBelow);
    }

    [Fact]
    public void AFallWithNoEscapeForTheUnitBelowIsRefusedBeforeAnythingChanges()
    {
        var arena = Tall(walkingSoldier: true);
        arena.Stand(arena.Soldier, 12, 12, 2);
        Raise(arena, arena.Alien, 12, 12, 2);
        foreach (var (dx, dy) in new[] { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) })
            arena.Clear(12 + dx, 12 + dy, 0, BattleTilePart.Floor);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        var before = State(arena);
        Assert.Equal("battle.fall-knockout-unsupported", arena.Battle.FallsRefusal(arena.Rules)!.Code);
        Assert.Equal(before, State(arena));
        // Give one side its floor back, and the unit below escapes that way (east is the first direction with a floor).
        arena.Put(13, 12, 0, BattleTilePart.Floor, Floor);
        Assert.Null(arena.Battle.FallsRefusal(arena.Rules));
        Assert.Equal(before, State(arena));
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        Assert.Equal((At(13, 12, 0), At(12, 12, 0)), (arena.Soldier.Position, arena.Alien.Position));
    }

    [Fact]
    public void ALandingThatAnArmedReactorCouldAnswerIsRefusedAndAnUnarmedOneSpots()
    {
        var arena = Tall(walkingSoldier: true, reactive: true);
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var gun = GunFor(arena, arena.Alien);
        var before = State(arena);
        Assert.Equal("battle.reaction-fire-unsupported", arena.Battle.FallsRefusal(arena.Rules)!.Code);
        Assert.Equal(before, State(arena));
        // The trial copies of the first turn run the fall for real and refuse the same way.
        Assert.Equal("battle.reaction-fire-unsupported", arena.Battle.ResolveFalls(arena.Rules)!.Code);
        arena.Battle.RemoveItem(gun);
        arena = Tall(walkingSoldier: true, reactive: true);
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        gun = GunFor(arena, arena.Alien);
        // Without its gun, the alien sees the soldier land and has it among its visible units.
        arena.Battle.RemoveItem(gun);
        Assert.Null(arena.Battle.FallsRefusal(arena.Rules));
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        Assert.Equal(At(10, 10, 0), arena.Soldier.Position);
        Assert.True(Arena.Sees(arena.Alien, arena.Soldier));
    }

    [Fact]
    public void ALandingOfAUnitOfTheSideNotOnTurnIsNoReactionTrigger()
    {
        // checkReactionFire only reacts to the side on turn: the alien lands on the player's turn beside an armed soldier.
        var arena = Tall(walkingSoldier: true, reactive: true);
        GunFor(arena, arena.Soldier);
        Raise(arena, arena.Alien, 15, 10, 2);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        Assert.Null(arena.Battle.FallsRefusal(arena.Rules));
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        Assert.Equal(At(15, 10, 0), arena.Alien.Position);
    }

    [Fact]
    public void AUnitThatBurnsTheFloorRefusesToLand()
    {
        var arena = Tall(walkingSoldier: true, suitExtra: "    specab: 2\n");
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var before = State(arena);
        Assert.Equal("battle.burn-floor-unsupported", arena.Battle.FallsRefusal(arena.Rules)!.Code);
        Assert.Equal(before, State(arena));
    }

    [Fact]
    public void ALandingBesideAProximityFuseIsRefusedAndOtherItemsNearItAreNot()
    {
        var arena = Tall(walkingSoldier: true, grenadeEvents: "{proximityExplode: true}");
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var grenade = Drop(arena, "GRENADE", At(11, 11, 0));
        // Unlit: nothing happens (proximityExplode needs a lit fuse).
        Assert.Null(arena.Battle.FallsRefusal(arena.Rules));
        grenade.FuseTimer = 0;
        grenade.FuseEnabled = true;
        var before = State(arena);
        Assert.Equal("battle.proximity-unsupported", arena.Battle.FallsRefusal(arena.Rules)!.Code);
        Assert.Equal(before, State(arena));
        // Two tiles away it is out of reach.
        grenade.Tile = At(12, 10, 0);
        Assert.Null(arena.Battle.FallsRefusal(arena.Rules));
    }

    [Fact]
    public void ALitPlainGrenadeNextToTheLandingIsNoProximityEvent()
    {
        // fuseProximityEvent: a lit fuse with the default behavior only counts for a proximity grenade.
        var arena = Tall(walkingSoldier: true);
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var grenade = Drop(arena, "GRENADE", At(11, 10, 0));
        grenade.FuseTimer = 0;
        grenade.FuseEnabled = true;
        Assert.Null(arena.Battle.FallsRefusal(arena.Rules));
    }

    [Fact]
    public void AnItemWhoseFuseNearbyUnitsTriggerIsRefusedWhetherLitOrNot()
    {
        var arena = Tall(walkingSoldier: true, grenadeEvents: "{proximityTrigger: true}");
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        Drop(arena, "GRENADE", At(9, 11, 0));
        Assert.Equal("battle.proximity-unsupported", arena.Battle.FallsRefusal(arena.Rules)!.Code);
    }

    [Fact]
    public void ARequeueFindsTheUnitsWithoutAFloorInUnitOrderWithTheLastFirst()
    {
        var arena = Tall(walkingSoldier: true);
        Raise(arena, arena.Alien, 12, 12, 2);
        Raise(arena, arena.Soldier, 14, 12, 2);
        arena.Battle.RequeueFallingUnits();
        Assert.Equal(arena.Battle.Units.Where(static unit => unit.HaveNoFloorBelow).Select(static unit => unit.Id).OrderByDescending(static id => id),
            arena.Battle.FallingUnits.Select(static unit => unit.Id));
        Assert.Equal(2, arena.Battle.FallingUnits.Count);
        arena.Battle.RequeueFallingUnits();
        Assert.Equal(2, arena.Battle.FallingUnits.Count);
        Assert.False(arena.Battle.AddFallingUnit(arena.Alien));
        // A flier is never queued.
        var fliers = Tall();
        Raise(fliers, fliers.Soldier, 14, 12, 2);
        fliers.Battle.RequeueFallingUnits();
        Assert.Empty(fliers.Battle.FallingUnits);
    }

    [Fact]
    public void AFallingUnitThatWasTurningOrPanickingStopsAndStillFalls()
    {
        foreach (var status in new[] { BattleUnitStatus.Turning, BattleUnitStatus.Panicking, BattleUnitStatus.Berserk })
        {
            var arena = Tall(walkingSoldier: true);
            Raise(arena, arena.Soldier, 10, 10, 2);
            arena.Battle.ApplyGravity(arena.Soldier.Position);
            arena.Soldier.Status = status;
            Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
            Assert.Equal((At(10, 10, 0), BattleUnitStatus.Standing), (arena.Soldier.Position, arena.Soldier.Status));
        }
    }

    [Fact]
    public void AQueuedUnitThatPassedTheOutThresholdLeavesTheListWithoutFalling()
    {
        var arena = Tall();
        Raise(arena, arena.Alien, 12, 12, 3);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        arena.Alien.StunLevel = arena.Alien.Health;
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        Assert.Empty(arena.Battle.FallingUnits);
        Assert.Equal(At(12, 12, 3), arena.Alien.Position);
    }

    [Fact]
    public void AFlierBelowEscapesOntoATileWithoutAFloor()
    {
        var arena = Tall();
        arena.Stand(arena.Soldier, 12, 12, 2);
        foreach (var (dx, dy) in new[] { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) })
            arena.Clear(12 + dx, 12 + dy, 0, BattleTilePart.Floor);
        Raise(arena, arena.Alien, 12, 12, 1);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        Assert.Null(arena.Battle.FallsRefusal(arena.Rules));
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        Assert.Equal((At(12, 11, 0), At(12, 12, 0)), (arena.Soldier.Position, arena.Alien.Position));
    }

    [Fact]
    public void ABigUnitBelowMovesAllItsSectionsAsideForAFallingUnit()
    {
        var arena = Tall(walkingSoldier: true, bigAlien: true);
        Raise(arena, arena.Alien, 12, 12, 0);
        Raise(arena, arena.Soldier, 13, 13, 1);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        // North moves the big unit to (12, 11): none of the four tiles it takes is the soldier's or occupied.
        Assert.Equal((At(12, 11, 0), At(13, 13, 0)), (arena.Alien.Position, arena.Soldier.Position));
        Assert.Equal(arena.Soldier.Id, arena.Map.UnitAt(arena.Tile(13, 13, 0)));
        Assert.Equal(arena.Alien.Id, arena.Map.UnitAt(arena.Tile(13, 12, 0)));
        Assert.Equal(arena.Alien.Id, arena.Map.UnitAt(arena.Tile(12, 11, 0)));
    }

    [Fact]
    public void ALitProximityGrenadeWithTheDefaultBehaviorIsAProximityEvent()
    {
        var arena = Tall(walkingSoldier: true, proximityGrenade: true);
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var grenade = Drop(arena, "GRENADE", At(10, 11, 0));
        grenade.FuseTimer = 0;
        grenade.FuseEnabled = true;
        Assert.Equal("battle.proximity-unsupported", arena.Battle.FallsRefusal(arena.Rules)!.Code);
    }

    [Fact]
    public void ANonSoldierTakesTheUnitRulesFloorBurningAbility()
    {
        // Armor::getSpecialAbility is none for the alien's armor; its unit rule has specab 2 (the silacoid's).
        var arena = Flat(edit: rules => Replaced(Replaced(rules, "    shade: 0\n    width: 30\n    length: 30\n    height: 2", "    shade: 0\n    width: 30\n    length: 30\n    height: 4"),
            "  - type: ALIEN\n", "  - type: ALIEN\n    specab: 2\n"));
        Raise(arena, arena.Alien, 12, 12, 2);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        Assert.Equal("battle.burn-floor-unsupported", arena.Battle.FallsRefusal(arena.Rules)!.Code);
    }
}
