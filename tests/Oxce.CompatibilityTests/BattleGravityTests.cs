using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S7: gravity and falling (<c>TileEngine::applyGravity</c>, <c>UnitFallBState</c>, <c>SavedBattleGame::addFallingUnit</c>).
/// The arena is the sight tests' one made four levels high: the soldier at (10, 10) and the alien five tiles east on the ground floor,
/// the soldier a flier unless a test says otherwise, the alien a walker.
/// </summary>
public sealed class BattleGravityTests
{
    private const string GunText = "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1";

    private static Arena Tall(bool walkingSoldier = false, string suitExtra = "", bool reactive = false, bool bigAlien = false, string grenadeEvents = "", bool proximityGrenade = false,
        Func<string, string>? more = null) =>
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
            return more is null ? rules : more(rules);
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

    [Fact]
    public void AUnitWithoutAFloorFallsLevelByLevelToTheFloorBelow()
    {
        var arena = Tall();
        var alien = arena.Alien;
        Raise(arena, alien, 12, 12, 3);
        var motion = alien.MotionPoints;
        Assert.Equal(At(12, 12, 0), arena.Battle.ApplyGravity(alien.Position));
        Assert.Same(alien, Assert.Single(arena.Battle.FallingUnits));
        Assert.Null(arena.Battle.FallsRefusal());
        // Asking changed nothing: the unit still hangs where it was.
        Assert.Equal((At(12, 12, 3), true), (alien.Position, alien.HaveNoFloorBelow));
        Assert.Null(arena.Battle.ResolveFalls());
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
        Assert.Null(arena.Battle.ResolveFalls());
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
        Assert.Null(arena.Battle.FallsRefusal());
        Assert.Null(arena.Battle.ResolveFalls());
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
        Assert.Null(arena.Battle.ResolveFalls());
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
        Assert.Null(arena.Battle.ResolveFalls());
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
    public void AFallWithNoEscapeForTheUnitBelowKnocksItOut()
    {
        var arena = Tall(walkingSoldier: true);
        arena.Stand(arena.Soldier, 12, 12, 2);
        Raise(arena, arena.Alien, 12, 12, 2);
        foreach (var (dx, dy) in new[] { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) })
            arena.Clear(12 + dx, 12 + dy, 0, BattleTilePart.Floor);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        var before = State(arena);
        // The knock-out refuses nothing: the trial finds no reason to stop and changes nothing.
        Assert.Null(arena.Battle.FallsRefusal());
        Assert.Equal(before, State(arena));
        // Give one side its floor back, and the unit below escapes that way (east is the first direction with a floor).
        var escape = Tall(walkingSoldier: true);
        escape.Stand(escape.Soldier, 12, 12, 2);
        Raise(escape, escape.Alien, 12, 12, 2);
        foreach (var (dx, dy) in new[] { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) })
            escape.Clear(12 + dx, 12 + dy, 0, BattleTilePart.Floor);
        escape.Put(13, 12, 0, BattleTilePart.Floor, Floor);
        escape.Battle.ApplyGravity(escape.Alien.Position);
        Assert.Null(escape.Battle.ResolveFalls());
        Assert.Equal((At(13, 12, 0), At(12, 12, 0)), (escape.Soldier.Position, escape.Alien.Position));
        Assert.Equal(BattleUnitStatus.Standing, escape.Soldier.Status);

        // Without one the unit below is stomped: its stun reaches its health, the casualty check follows, and the unit lies unconscious as a body
        // on the tile it was on while the faller stands there.
        var soldier = arena.Soldier;
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Equal((BattleUnitStatus.Unconscious, null, soldier.Health, At(12, 12, 0)), (soldier.Status, soldier.Position, soldier.StunLevel, arena.Alien.Position));
        Assert.Equal(At(12, 12, 0), soldier.SavedPosition);
        Assert.Equal(arena.Alien.Id, arena.Map.UnitAt(arena.Tile(12, 12, 0)));
        var body = Assert.Single(arena.Battle.Items, item => item.CorpseUnitId == soldier.Id);
        Assert.Equal(At(12, 12, 0), body.Tile);
        Assert.Empty(arena.Battle.FallingUnits);
    }

    [Fact]
    public void TwoUnitsBelowCannotEscapeOntoTheSameTileAndOneWithNoEscapeIsKnockedOut()
    {
        // UnitFallBState's escapeTiles: the tile one unit below escapes onto is taken for the next one, which then looks for another.
        var arena = Tall(walkingSoldier: true);
        var others = new List<BattleUnit>();
        foreach (var id in new[] { 920, 921 })
        {
            var copy = new BattleUnit(id, BattleFaction.Hostile, BattleController.Ai)
            {
                Stats = arena.Alien.Stats,
                ArmorId = arena.Alien.ArmorId,
                UnitType = arena.Alien.UnitType,
                Health = arena.Alien.Health,
                Position = At(25 + others.Count, 25, 0),
            };
            arena.Battle.AddUnit(copy);
            others.Add(copy);
        }
        var (below1, below2, faller1, faller2) = (arena.Soldier, arena.Alien, others[0], others[1]);
        arena.Stand(below1, 12, 12, 2);
        arena.Stand(below2, 12, 10, 2);
        // Both stand where their only neighbour with a floor is (12, 11).
        foreach (var (cx, cy) in new[] { (12, 12), (12, 10) })
            foreach (var (dx, dy) in new[] { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) })
                if ((cx + dx, cy + dy) != (12, 11)) arena.Clear(cx + dx, cy + dy, 0, BattleTilePart.Floor);
        Raise(arena, faller1, 12, 12, 2);
        Raise(arena, faller2, 12, 10, 2);
        arena.Battle.ApplyGravity(faller1.Position);
        arena.Battle.ApplyGravity(faller2.Position);
        Assert.Equal(2, arena.Battle.FallingUnits.Count);
        var before = State(arena);
        Assert.Null(arena.Battle.FallsRefusal());
        Assert.Equal(before, State(arena));
        // The list runs the unit queued last first: the unit at (12, 10) takes (12, 11). Open a second way for the other one (east of it):
        // it takes that instead of the tile already taken.
        arena.Put(13, 12, 0, BattleTilePart.Floor, Floor);
        Assert.Null(arena.Battle.FallsRefusal());
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Equal((At(13, 12, 0), At(12, 11, 0)), (below1.Position, below2.Position));
        Assert.Equal((At(12, 12, 0), At(12, 10, 0)), (faller1.Position, faller2.Position));
    }

    [Fact]
    public void ALandingThatAnArmedReactorCouldAnswerIsRefusedAndAnUnarmedOneSpots()
    {
        var arena = Tall(walkingSoldier: true, reactive: true);
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var gun = GunFor(arena.Battle, arena.Alien);
        var before = State(arena);
        Assert.Equal("battle.reaction-fire-unsupported", arena.Battle.FallsRefusal()!.Code);
        Assert.Equal(before, State(arena));
        // The trial copies of the first turn run the fall for real and refuse the same way.
        Assert.Equal("battle.reaction-fire-unsupported", arena.Battle.ResolveFalls()!.Code);
        arena.Battle.RemoveItem(gun);
        arena = Tall(walkingSoldier: true, reactive: true);
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        gun = GunFor(arena.Battle, arena.Alien);
        // Without its gun, the alien sees the soldier land and has it among its visible units.
        arena.Battle.RemoveItem(gun);
        Assert.Null(arena.Battle.FallsRefusal());
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Equal(At(10, 10, 0), arena.Soldier.Position);
        Assert.True(Arena.Sees(arena.Alien, arena.Soldier));
    }

    [Fact]
    public void ALandingOfAUnitOfTheSideNotOnTurnIsNoReactionTrigger()
    {
        // checkReactionFire only reacts to the side on turn: the alien lands on the player's turn beside an armed soldier.
        var arena = Tall(walkingSoldier: true, reactive: true);
        GunFor(arena.Battle, arena.Soldier);
        Raise(arena, arena.Alien, 15, 10, 2);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        Assert.Null(arena.Battle.FallsRefusal());
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Equal(At(15, 10, 0), arena.Alien.Position);
    }

    [Fact]
    public void AUnitThatBurnsTheFloorLandsLikeAnyOtherWhenTheHitHasNothingToReach()
    {
        var arena = Tall(walkingSoldier: true, suitExtra: "    specab: 2\n");
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var before = State(arena);
        Assert.Null(arena.Battle.FallsRefusal());
        Assert.Equal(before, State(arena));
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Equal(At(10, 10, 0), arena.Soldier.Position);
    }

    [Fact]
    public void ALandingBesideALitProximityExplodeFuseSetsItOffAndOtherItemsNearItAreLeftAlone()
    {
        // Unlit: nothing happens (proximityExplode needs a lit fuse).
        var unlit = Tall(walkingSoldier: true, grenadeEvents: "{proximityExplode: true}");
        Raise(unlit, unlit.Soldier, 10, 10, 2);
        unlit.Battle.ApplyGravity(unlit.Soldier.Position);
        var idle = Drop(unlit, "GRENADE", At(11, 11, 0));
        Assert.Null(unlit.Battle.FallsRefusal());
        Assert.Null(unlit.Battle.ResolveFalls());
        Assert.Contains(idle, unlit.Battle.Items);

        // Lit and next to the landing: the grenade explodes (the fall's states run behind it).
        var arena = Tall(walkingSoldier: true, grenadeEvents: "{proximityExplode: true}");
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var grenade = Drop(arena, "GRENADE", At(11, 11, 0));
        grenade.FuseTimer = 0;
        grenade.FuseEnabled = true;
        // A dry run draws and changes nothing, and refuses a landing that would set the item off (its explosion cannot be tried first on the live battle).
        var before = State(arena);
        Assert.Equal("battle.fall-proximity-explosion-unsupported", arena.Battle.FallsRefusal()?.Code);
        Assert.Equal(before, State(arena));
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.DoesNotContain(grenade, arena.Battle.Items);

        // Two tiles away it is out of reach.
        var far = Tall(walkingSoldier: true, grenadeEvents: "{proximityExplode: true}");
        Raise(far, far.Soldier, 10, 10, 2);
        far.Battle.ApplyGravity(far.Soldier.Position);
        var distant = Drop(far, "GRENADE", At(12, 10, 0));
        distant.FuseTimer = 0;
        distant.FuseEnabled = true;
        Assert.Null(far.Battle.ResolveFalls());
        Assert.Contains(distant, far.Battle.Items);
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
        Assert.Null(arena.Battle.FallsRefusal());
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Contains(grenade, arena.Battle.Items);
    }

    [Fact]
    public void AnItemWhoseFuseNearbyUnitsTriggerStartsItsFuseWhenAUnitLandsNextToIt()
    {
        var arena = Tall(walkingSoldier: true, grenadeEvents: "{proximityTrigger: true}");
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var grenade = Drop(arena, "GRENADE", At(9, 11, 0));
        grenade.FuseTimer = 2;
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Equal((true, 2), (grenade.FuseEnabled, grenade.FuseTimer));
    }

    // ---- the proximity check at a landing (S8) ----

    private const string PlainGrenade = "- type: GRENADE\n    battleType: 4\n    weight: 1";
    private const string TrapMelee = "\n  - type: STR_DEATH_TRAP_200\n    battleType: 3\n    weight: 1\n    power: 40\n    damageType: 1\n    damageAlter: {RandomType: 3}\n    clipSize: -1\n    confMelee: {ammoSlot: -1}";
    private const string TrapProximity = "\n  - type: STR_DEATH_TRAP_200\n    battleType: 5\n    weight: 1\n    power: 40\n    damageType: 3\n    damageAlter: {RandomType: 3}\n    fuseType: 0";
    private const string Dark = "    shade: 15\n    width: 30";

    /// <summary>The grenade is a proximity grenade of power 40 (a high explosive, flat damage) that its blast destroys; <paramref name="extra"/> adds lines to its rule.</summary>
    private static Func<string, string> Armed(string extra = "") => rules =>
        Replaced(rules, PlainGrenade, "- type: GRENADE\n    battleType: 5\n    weight: 1\n    armor: 0\n    power: 40\n    damageType: 3\n    damageAlter: {RandomType: 3}" + extra);

    private static BattleItem Lit(Arena arena, BattlePosition tile)
    {
        var grenade = Drop(arena, "GRENADE", tile);
        grenade.FuseTimer = 0;
        grenade.FuseEnabled = true;
        return grenade;
    }

    [Fact]
    public void AnExplosionAtALaterLandingRunsBeforeAnEarlierRoundsKnockOutDeath()
    {
        // The soldier below cannot move aside and is knocked out when the alien reaches it (round 2); the alien lands the round after, next to a lit grenade. statePushNext puts
        // each push right behind the fall, so the later one, the blast, runs first: what the soldier carries has not dropped to the tile when it goes off, and survives it.
        var arena = Tall(walkingSoldier: true, more: Armed());
        arena.Stand(arena.Soldier, 12, 12, 2);
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 100 };
        arena.Soldier.Health = 100;
        Raise(arena, arena.Alien, 12, 12, 2);
        foreach (var (dx, dy) in new[] { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) })
            arena.Clear(12 + dx, 12 + dy, 0, BattleTilePart.Floor);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        var lit = Lit(arena, At(13, 12, 0));
        var carried = new BattleItem(arena.Battle.TakeItemId(), "GRENADE") { OwnerUnitId = arena.Soldier.Id, Slot = "STR_RIGHT_HAND" };
        arena.Battle.AddItem(carried);
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Contains(arena.Battle.Events.Drain(), static @event => @event is BattleExploded);
        Assert.DoesNotContain(lit, arena.Battle.Items);
        Assert.Equal(BattleUnitStatus.Unconscious, arena.Soldier.Status);
        // Dropped by the death after the blast: still there, on the tile the soldier lay on.
        Assert.Contains(carried, arena.Battle.Items);
        Assert.Equal(At(12, 12, 0), carried.Tile);
    }

    [Fact]
    public void ALitItemThatIsNoGrenadeIsRemovedByItsProximityEventAndNothingExplodes()
    {
        var arena = Tall(walkingSoldier: true, more: rules => Replaced(rules, "- type: CLIP\n    battleType: 2\n    clipSize: 12\n    weight: 1",
            "- type: CLIP\n    battleType: 2\n    clipSize: 12\n    weight: 1\n    fuseTriggerEvents: {proximityExplode: true}"));
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var clip = Drop(arena, "CLIP", At(11, 11, 0));
        clip.FuseTimer = 0;
        clip.FuseEnabled = true;
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.DoesNotContain(clip, arena.Battle.Items);
        Assert.DoesNotContain(arena.Battle.Events.Drain(), static @event => @event is BattleExploded);
    }

    [Fact]
    public void AMissedSpecialChanceLeavesTheLitGrenadeAndItsFuseAsTheyAreAfterOneDraw()
    {
        var arena = Tall(walkingSoldier: true, more: Armed("\n    specialChance: 0"));
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var grenade = Lit(arena, At(11, 11, 0));
        var draws = new ScriptedDraws(99);
        arena.Battle.Random = draws;
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Contains(grenade, arena.Battle.Items);
        Assert.Equal((true, 0), (grenade.FuseEnabled, grenade.FuseTimer));
        Assert.Single(draws.Calls);
        Assert.DoesNotContain(arena.Battle.Events.Drain(), static @event => @event is BattleExploded);
    }

    [Fact]
    public void SeveralExplosionsOfOneCheckRunTheLastTileFirst()
    {
        // The check visits the tiles around the landing in order (x, then y); each explosion goes right behind the fall, so the last one runs first.
        var arena = Tall(walkingSoldier: true, more: Armed());
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        Lit(arena, At(9, 9, 0));
        Lit(arena, At(11, 11, 0));
        Assert.Null(arena.Battle.ResolveFalls());
        var centers = arena.Battle.Events.Drain().OfType<BattleExploded>().Select(static blast => (blast.Center.X / 16, blast.Center.Y / 16)).ToArray();
        Assert.Equal([(11, 11), (9, 9)], centers);
    }

    [Fact]
    public void ADeathTrapItemAlreadyOnTheTileIsReusedAndTheMeleeTrapHitsWithTheItemAsItsWeapon()
    {
        var arena = Tall(walkingSoldier: true, more: rules => Replaced(rules, PlainGrenade, PlainGrenade + TrapMelee));
        arena.Put(12, 12, 0, BattleTilePart.Floor, DeathTrapFloor);
        Raise(arena, arena.Soldier, 12, 12, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var trap = Drop(arena, "STR_DEATH_TRAP_200", At(12, 12, 0));
        Assert.Null(arena.Battle.ResolveFalls());
        // No second trap item was made (the one on the tile may be gone with the blow it struck).
        Assert.DoesNotContain(arena.Battle.Items, item => item.ItemType == "STR_DEATH_TRAP_200" && item != trap);
        // No attacker, and the trap's own item is the weapon of the hit.
        var all = arena.Battle.Events.Drain();
        var hit = Assert.Single(all.OfType<UnitMeleeAttacked>());
        Assert.DoesNotContain(all, static @event => @event is BattleExploded);
        Assert.Equal((-1, trap.Id), (hit.UnitId, hit.ItemId));
    }

    [Fact]
    public void ABigUnitTriggersTheDeathTrapOfAnyOfItsTilesAndTheItemsAroundAllOfThem()
    {
        var trapped = Tall(walkingSoldier: true, bigAlien: true, more: rules => Replaced(rules, PlainGrenade, PlainGrenade + TrapProximity));
        trapped.Put(13, 13, 0, BattleTilePart.Floor, DeathTrapFloor);
        Raise(trapped, trapped.Alien, 12, 12, 1);
        trapped.Battle.ApplyGravity(trapped.Alien.Position);
        Assert.Null(trapped.Battle.ResolveFalls());
        var blast = Assert.Single(trapped.Battle.Events.Drain().OfType<BattleExploded>());
        Assert.Equal((13, 13), (blast.Center.X / 16, blast.Center.Y / 16));

        // The big unit at (12, 12) reaches the tiles from (11, 11) to (14, 14).
        var near = Tall(walkingSoldier: true, bigAlien: true, more: Armed());
        Raise(near, near.Alien, 12, 12, 1);
        near.Battle.ApplyGravity(near.Alien.Position);
        Lit(near, At(14, 14, 0));
        Lit(near, At(15, 15, 0));
        Assert.Null(near.Battle.ResolveFalls());
        // Only the grenade within reach of a tile of the unit was set off (the other one may be gone with its blast, but did not explode itself).
        var set = Assert.Single(near.Battle.Events.Drain().OfType<BattleExploded>());
        Assert.Equal((14, 14), (set.Center.X / 16, set.Center.Y / 16));
    }

    [Fact]
    public void ALitFlareRemovedByAProximityEventTakesItsLightWithItAtOnce()
    {
        var arena = Tall(walkingSoldier: true, more: rules => Replaced(Replaced(rules, "    shade: 0\n    width: 30", Dark), PlainGrenade,
            PlainGrenade + "\n  - type: FLARE_FUSED\n    battleType: 10\n    power: 8\n    fuseType: 0\n    fuseTriggerEvents: {proximityExplode: true}"));
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var flare = Drop(arena, "FLARE_FUSED", At(11, 11, 0));
        flare.FuseTimer = 2;
        flare.FuseEnabled = true;
        arena.Relight();
        var lit = arena.Battle.TileShade(At(11, 11, 0));
        Assert.True(lit < 15, $"{lit}");
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.DoesNotContain(flare, arena.Battle.Items);
        Assert.Equal(15, arena.Battle.TileShade(At(11, 11, 0)));
    }

    [Fact]
    public void AFlareWhoseProximityTriggerSwitchesItOnLightsItsTileAtOnce()
    {
        var arena = Tall(walkingSoldier: true, more: rules => Replaced(Replaced(rules, "    shade: 0\n    width: 30", Dark), PlainGrenade,
            PlainGrenade + "\n  - type: FLARE_TRIGGERED\n    battleType: 10\n    power: 8\n    fuseTriggerEvents: {defaultBehavior: false, proximityTrigger: true}"));
        Raise(arena, arena.Soldier, 10, 10, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var flare = Drop(arena, "FLARE_TRIGGERED", At(11, 11, 0));
        arena.Relight();
        Assert.Equal(15, arena.Battle.TileShade(At(11, 11, 0)));
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Contains(flare, arena.Battle.Items);
        Assert.True(flare.FuseEnabled);
        Assert.True(arena.Battle.TileShade(At(11, 11, 0)) < 15);
    }

    [Fact]
    public void ALandingThatCouldSetOffADeathTrapOrAnItemIsRefusedByTheDryRunAndChangesNothing()
    {
        var arena = Tall(walkingSoldier: true, more: rules => Replaced(rules, PlainGrenade, PlainGrenade + TrapMelee));
        arena.Put(12, 12, 0, BattleTilePart.Floor, DeathTrapFloor);
        Raise(arena, arena.Soldier, 12, 12, 2);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var before = State(arena);
        Assert.Equal("battle.fall-proximity-explosion-unsupported", arena.Battle.FallsRefusal()?.Code);
        Assert.Equal(before, State(arena));

        // The same landing with an item the check leaves alone is no reason to wait.
        var quiet = Tall(walkingSoldier: true, more: Armed());
        Raise(quiet, quiet.Soldier, 10, 10, 2);
        quiet.Battle.ApplyGravity(quiet.Soldier.Position);
        Drop(quiet, "GRENADE", At(11, 11, 0));
        Assert.Null(quiet.Battle.FallsRefusal());
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
            Assert.Null(arena.Battle.ResolveFalls());
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
        Assert.Null(arena.Battle.ResolveFalls());
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
        Assert.Null(arena.Battle.FallsRefusal());
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Equal((At(12, 11, 0), At(12, 12, 0)), (arena.Soldier.Position, arena.Alien.Position));
    }

    [Fact]
    public void ABigUnitBelowMovesAllItsSectionsAsideForAFallingUnit()
    {
        var arena = Tall(walkingSoldier: true, bigAlien: true);
        Raise(arena, arena.Alien, 12, 12, 0);
        Raise(arena, arena.Soldier, 13, 13, 1);
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        Assert.Null(arena.Battle.ResolveFalls());
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
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.DoesNotContain(grenade, arena.Battle.Items);
    }

    [Fact]
    public void ANonSoldierTakesTheUnitRulesFloorBurningAbility()
    {
        // Armor::getSpecialAbility is none for the alien's armor; its unit rule has specab 2 (the silacoid's).
        var arena = Flat(edit: rules => Replaced(Replaced(rules, "    shade: 0\n    width: 30\n    length: 30\n    height: 2", "    shade: 0\n    width: 30\n    length: 30\n    height: 4"),
            "  - type: ALIEN\n", "  - type: ALIEN\n    specab: 2\n"));
        // (The floor it lands on is one a hit reaches, with a strength for the hit to have.)
        arena.Put(12, 12, 0, BattleTilePart.Floor, BurnFloor);
        arena.Alien.Stats = arena.Alien.Stats with { Strength = 30 };
        Raise(arena, arena.Alien, 12, 12, 2);
        arena.Battle.ApplyGravity(arena.Alien.Position);
        Assert.Null(arena.Battle.ResolveFalls());
        Assert.Equal(4, arena.Map.Fire(arena.Tile(12, 12)));
    }
}
