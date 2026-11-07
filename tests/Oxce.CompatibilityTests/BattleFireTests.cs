using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Xunit;
using static Oxce.CompatibilityTests.BattleMovementGroundworkTests;
using static Oxce.CompatibilityTests.BattleSightTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S9: fire, smoke and terrain destruction (<c>Tile::ignite</c>, <c>addSmoke</c>, <c>destroy</c>, <c>damage</c>,
/// <c>TileEngine::hit</c>'s tile part and <c>hitTile</c>, the burn-floor abilities of <c>UnitWalkBState</c> and <c>UnitFallBState</c>, and
/// <c>SavedBattleGame::prepareNewTurn</c>). The arena is the sight tests' one made four levels high, the soldier a walker at (10, 10) with a
/// strength of 30 unless a test says otherwise; floors with a loft (<see cref="BurnFloor"/> and its kin) are what a hit at a tile's centre reaches.
/// </summary>
public sealed class BattleFireTests
{
    private static string Replaced(string rules, string from, string to)
    {
        var result = rules.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(rules, result);
        return result;
    }

    private static Arena Make(string suitExtra = "", string alienExtra = "", int shade = 0, bool flying = false)
    {
        var arena = Flat(shade: shade, alienExtra: alienExtra, edit: rules =>
        {
            rules = Replaced(rules, "    shade: " + shade + "\n    width: 30\n    length: 30\n    height: 2", "    shade: " + shade + "\n    width: 30\n    length: 30\n    height: 4");
            if (!flying) rules = Replaced(rules, "  - type: SUIT\n    movementType: 1\n", "  - type: SUIT\n    movementType: 0\n");
            return suitExtra.Length != 0 ? Replaced(rules, "  - type: SUIT\n", "  - type: SUIT\n" + suitExtra) : rules;
        });
        arena.Soldier.Stats = arena.Soldier.Stats with { Strength = 30 };
        return arena;
    }

    private static Arena Burner(int shade = 0) => Make("    specab: 2\n", shade: shade);

    private static BattlePosition At(int x, int y, int z = 0) => new(x, y, z);

    private static BattlePosition Voxel(int x, int y, int z = 0) => new(x * 16 + 8, y * 16 + 8, z * 24);

    private static void Cover(Arena arena, int id, int x0, int x1, int y0, int y1, int z = 0)
    {
        for (var x = x0; x <= x1; x++)
            for (var y = y0; y <= y1; y++) arena.Put(x, y, z, BattleTilePart.Floor, id);
    }

    private static string Refusal(CampaignCommandResult result) => Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events)).Code;

    private static IReadOnlyList<ICampaignEvent> Events(CampaignCommandResult result)
    {
        Assert.DoesNotContain(result.Events, static @event => @event is CampaignActionBlocked);
        return result.Events;
    }

    private static CampaignReason? Hit(Arena arena, int x, int y, int z = 0, BattleUnit? attacker = null, int power = 30, BattleTileDamageType? type = null) =>
        arena.Battle.HitTerrain(arena.Rules, attacker, Voxel(x, y, z), power, type ?? BattleTileDamageType.Fire);

    /// <summary>The terrain, fire, smoke, items and falling units of the map, to compare a refused command with what it found.</summary>
    private static string State(Arena arena) =>
        string.Join('|', Enumerable.Range(0, arena.Map.TileCount).Where(tile => arena.Map.Fire(tile) != 0 || arena.Map.Smoke(tile) != 0 ||
            arena.Map.Overlaps(tile) != 0).Select(tile => $"{tile}:{arena.Map.Fire(tile)}:{arena.Map.Smoke(tile)}:{arena.Map.Overlaps(tile)}")) +
        "#" + string.Join(',', Enumerable.Range(0, arena.Map.TileCount).Select(tile => string.Join('/', Enumerable.Range(0, 4).Select(part => arena.Map.Part(tile, (BattleTilePart)part))))
            .GroupBy(static text => text).Select(static group => $"{group.Key}x{group.Count()}")) +
        "#" + string.Join(',', arena.Battle.Units.Select(unit => $"{unit.Id}:{unit.Position}:{unit.Status}:{unit.HaveNoFloorBelow}")) +
        $"#{arena.Battle.Revision}:{string.Join(',', arena.Battle.FallingUnits.Select(static unit => unit.Id))}:{arena.Battle.ObjectivesDestroyed}";

    // ---- Tile::ignite and Tile::addSmoke ----

    [Fact]
    public void AnIgnitedTileBurnsItsFuelPlusOneTurnsWithTheSmokeOfItsFlammability()
    {
        var arena = Make();
        var tile = arena.Tile(12, 10);
        arena.Put(12, 10, 0, BattleTilePart.Floor, BurnFloor);
        arena.Battle.Ignite(tile, 100);
        // Flammable 10 and fuel 3: 4 turns, a smoke of 15 - clamp(10 / 10, 1, 12), and the tile counts the fire as added this turn.
        Assert.Equal((4, 14, 1), (arena.Map.Fire(tile), arena.Map.Smoke(tile), arena.Map.Overlaps(tile)));
        // A tile that already burns keeps its fire (the roll is still drawn).
        arena.Map.SetFire(tile, 2);
        arena.Battle.Ignite(tile, 100);
        Assert.Equal(2, arena.Map.Fire(tile));
    }

    [Fact]
    public void AnIgniteThatCannotCatchChangesNothingAndOnlyAFlammableTileDrawsTheRoll()
    {
        var arena = Make();
        var (sturdy, plain, burnable) = (arena.Tile(12, 10), arena.Tile(13, 10), arena.Tile(14, 10));
        arena.Put(12, 10, 0, BattleTilePart.Floor, Sturdy);
        arena.Put(14, 10, 0, BattleTilePart.Floor, BurnFloor);
        var reference = new SplitMix64RandomSource(7);
        var used = new SplitMix64RandomSource(7);
        arena.Battle.Random = used;
        // Flammable 255: nothing, and no roll.
        arena.Battle.Ignite(sturdy, 100);
        Assert.Equal((0, 0, 0), (arena.Map.Fire(sturdy), arena.Map.Smoke(sturdy), arena.Map.Overlaps(sturdy)));
        Assert.Equal(reference.State, used.State);
        // No fuel (the plain floor has none): the roll is drawn, nothing burns.
        arena.Battle.Ignite(plain, 100);
        Assert.Equal((0, 0, 0), (arena.Map.Fire(plain), arena.Map.Smoke(plain), arena.Map.Overlaps(plain)));
        reference.NextInclusive(0, 99);
        Assert.Equal(reference.State, used.State);
        // A chance that cuts to zero never catches: power 0 - flammability / 10 + 15 is below zero.
        arena.Battle.Ignite(burnable, -200);
        Assert.Equal(0, arena.Map.Fire(burnable));
        reference.NextInclusive(0, 99);
        Assert.Equal(reference.State, used.State);
    }

    [Fact]
    public void AFuelOf255WrapsTheFireTimerToNothing()
    {
        var arena = Make();
        var tile = arena.Tile(12, 10);
        arena.Put(12, 10, 0, BattleTilePart.Floor, Chaff);
        arena.Battle.Ignite(tile, 100);
        // The fire timer is a Uint8: fuel 255 + 1 is 0, while the smoke (15 - clamp(0, 1, 12)) and the overlap are set.
        Assert.Equal((0, 14, 1), (arena.Map.Fire(tile), arena.Map.Smoke(tile), arena.Map.Overlaps(tile)));
    }

    [Fact]
    public void SmokeIsClampedUntilAnotherSmokeArrivesTheSameTurnThenSummedAsAByte()
    {
        var arena = Make();
        var (tile, other, burning) = (arena.Tile(12, 10), arena.Tile(13, 10), arena.Tile(14, 10));
        arena.Battle.AddSmoke(tile, 5);
        Assert.Equal((5, 1), (arena.Map.Smoke(tile), arena.Map.Overlaps(tile)));
        arena.Battle.AddSmoke(tile, 5);
        Assert.Equal((10, 2), (arena.Map.Smoke(tile), arena.Map.Overlaps(tile)));
        // The first smoke of a turn is kept in 1 to 15.
        arena.Battle.AddSmoke(other, 40);
        Assert.Equal(15, arena.Map.Smoke(other));
        arena.Map.SetSmoke(other, 250);
        arena.Battle.AddSmoke(other, 10);
        Assert.Equal((4, 2), (arena.Map.Smoke(other), arena.Map.Overlaps(other)));
        // A burning tile takes no smoke and counts no overlap.
        arena.Map.SetFire(burning, 3);
        arena.Battle.AddSmoke(burning, 9);
        Assert.Equal((0, 0), (arena.Map.Smoke(burning), arena.Map.Overlaps(burning)));
    }

    // ---- hitTile ----

    [Fact]
    public void AHitSetsFireOnATileWithAFloorOrAnObjectAndSmokeFromItsFlammability()
    {
        var arena = Make();
        var rules = arena.Rules;
        var (floor, empty, burning) = (arena.Tile(12, 10), arena.Tile(13, 10, 3), arena.Tile(14, 10));
        arena.Put(12, 10, 0, BattleTilePart.Floor, BurnFloor);
        arena.Put(14, 10, 0, BattleTilePart.Floor, BurnFloor);
        // Fire type (a fire threshold of 0): fuel 3 + 1 turns, and smoke of min(15 - 1, 12).
        Assert.Equal(2, arena.Battle.HitTile(rules, floor, 7, BattleTileDamageType.Fire));
        Assert.Equal((4, 12), (arena.Map.Fire(floor), arena.Map.Smoke(floor)));
        // A tile that is void, or already burns, produces nothing.
        Assert.Equal(0, arena.Battle.HitTile(rules, empty, 7, BattleTileDamageType.Fire));
        arena.Map.SetFire(burning, 1);
        Assert.Equal(0, arena.Battle.HitTile(rules, burning, 7, BattleTileDamageType.Fire));
        Assert.Equal(1, arena.Map.Fire(burning));
    }

    [Fact]
    public void AHitBelowTwiceTheFireThresholdSetsAShorterFireAndSmokeReplacesFire()
    {
        var arena = Make();
        var rules = arena.Rules;
        var (low, high, smoky) = (arena.Tile(12, 10), arena.Tile(13, 10), arena.Tile(14, 10));
        foreach (var x in ThreeTiles) arena.Put(x, 10, 0, BattleTilePart.Floor, BurnFloor);
        var type = new BattleTileDamageType(10, 1000, 0.0, false, 2, -1);
        // Fuel 3, damage 12 over a threshold of 10: 3 * 2 / 10 + 1 = 1 turn; damage 25 (twice the threshold or more): 4.
        Assert.Equal(2, arena.Battle.HitTile(rules, low, 12, type));
        Assert.Equal(1, arena.Map.Fire(low));
        Assert.Equal(2, arena.Battle.HitTile(rules, high, 25, type));
        Assert.Equal(4, arena.Map.Fire(high));
        // A smoke type (threshold 0) puts the fire out and sets 7 to 15 smoke; a tile with too much smoke already takes none.
        var smoke = new BattleTileDamageType(1000, 0, 0.0, false, 2, -1);
        // (Below twice the threshold the smoke is a roll of 7 to 15 times the excess over the threshold, over the threshold.)
        var thin = new BattleTileDamageType(1000, 10, 0.0, false, 2, -1);
        Assert.Equal(1, arena.Battle.HitTile(rules, arena.Tile(15, 10), 15, thin));
        Assert.InRange(arena.Map.Smoke(arena.Tile(15, 10)), 3, 7);
        arena.Map.SetFire(smoky, 5);
        Assert.Equal(1, arena.Battle.HitTile(rules, smoky, 10, smoke));
        Assert.Equal(0, arena.Map.Fire(smoky));
        Assert.InRange(arena.Map.Smoke(smoky), 7, 15);
        arena.Map.SetSmoke(smoky, rules.Campaign.TooMuchSmokeThreshold);
        Assert.Equal(0, arena.Battle.HitTile(rules, smoky, 10, smoke));
        Assert.Equal(rules.Campaign.TooMuchSmokeThreshold, arena.Map.Smoke(smoky));
    }

    [Fact]
    public void ATileDamageTypeTakesItsFractionOfTheDamageWithTheLimit()
    {
        var random = new SplitMix64RandomSource(3);
        Assert.Equal(0, BattleTileDamageType.Fire.TileFinalDamage(random, 30, 8));
        var half = new BattleTileDamageType(1000, 1000, 0.5, false, 2, -1);
        Assert.Equal(5, half.TileFinalDamage(random, 30, 9)); // round(4.5) rounds away from zero
        Assert.Equal(0, half.TileFinalDamage(random, 30, 0));
        Assert.Equal(3, (half with { TileDamageLimit = 3 }).TileFinalDamage(random, 30, 20));
        // Method 1 draws the damage the tile takes between half and one and a half times the power.
        Assert.InRange((half with { TileDamageMethod = 1 }).TileFinalDamage(random, 30, 0), 8, 23);
    }

    // ---- TileEngine::hit ----

    [Fact]
    public void AHitOnAFloorBurnsItAndKeepsItWhenItsArmorHoldsAndLightsTheTile()
    {
        var arena = Make(shade: 15);
        var tile = arena.Tile(12, 12);
        arena.Put(12, 12, 0, BattleTilePart.Floor, BurnFloor);
        arena.Relight();
        var dark = arena.Battle.TileShade(At(12, 12));
        Assert.Null(Hit(arena, 12, 12));
        Assert.Equal((4, 12, (BurnFloor, 1)), (arena.Map.Fire(tile), arena.Map.Smoke(tile), arena.Map.Part(tile, BattleTilePart.Floor)));
        // The fire layer is relit around the tile.
        Assert.True(arena.Battle.TileShade(At(12, 12)) < dark);
    }

    [Fact]
    public void AHitThatBreaksAFloorOnTheGroundLeavesScorchedEarthAndOneAboveLeavesAHole()
    {
        var arena = Make();
        arena.Put(12, 12, 0, BattleTilePart.Floor, BrittleFloor);
        Assert.Null(Hit(arena, 12, 12));
        // Scorched earth is the first data set's second object, whatever the floor was; the fire it started stays.
        Assert.Equal((1, 0), arena.Map.Part(arena.Tile(12, 12), BattleTilePart.Floor));
        Assert.Equal(3, arena.Map.Fire(arena.Tile(12, 12)));
        // Above the ground the floor is gone, and a unit standing on it is queued to fall.
        arena.Put(14, 12, 2, BattleTilePart.Floor, BrittleFloor);
        arena.Battle.SetTile(arena.Alien, At(14, 12, 2));
        Assert.Null(Hit(arena, 14, 12, 2, arena.Alien));
        Assert.Equal((-1, -1), arena.Map.Part(arena.Tile(14, 12, 2), BattleTilePart.Floor));
        Assert.Same(arena.Alien, Assert.Single(arena.Battle.FallingUnits));
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        Assert.Equal(At(14, 12), arena.Alien.Position);
    }

    [Fact]
    public void AHitOnAnObjectBurnsItAndWearsItToItsDeadFormWhenItsArmorIsReached()
    {
        var arena = Make();
        var tile = arena.Tile(12, 12);
        arena.Put(12, 12, 0, BattleTilePart.Floor, Floor);
        // The crate (armor 100) holds against a hit that does no tile damage; an object of armor 0 does not.
        arena.Put(12, 12, 0, BattleTilePart.ObjectPart, Crate);
        Assert.Null(Hit(arena, 12, 12));
        // The tile's fuel is the highest of its parts (the crate's 4), so it burns 5 turns.
        Assert.Equal((Crate, 5), (arena.Map.Part(tile, BattleTilePart.ObjectPart).Id, arena.Map.Fire(tile)));
        arena.Put(14, 12, 0, BattleTilePart.Floor, Floor);
        arena.Put(14, 12, 0, BattleTilePart.ObjectPart, Objective);
        arena.Battle.ObjectiveType = 7;
        arena.Battle.ObjectiveCount = 3;
        Assert.Null(Hit(arena, 14, 12));
        // The objective is gone (it has no dead form) and counted; the floor's tile stays.
        Assert.Equal((-1, -1), arena.Map.Part(arena.Tile(14, 12), BattleTilePart.ObjectPart));
        Assert.Equal(1, arena.Battle.ObjectivesDestroyed);
        Assert.Equal(Floor, arena.Map.Part(arena.Tile(14, 12), BattleTilePart.Floor).Id);
    }

    [Fact]
    public void ADestroyedPartBecomesItsDeadFormOfThePartItBelongsTo()
    {
        var arena = Make();
        var tile = arena.Tile(12, 12);
        arena.Put(12, 12, 0, BattleTilePart.ObjectPart, Crate);
        Assert.False(arena.Battle.DamagePart(tile, BattleTilePart.ObjectPart, 99, 7));
        Assert.Equal(Crate, arena.Map.Part(tile, BattleTilePart.ObjectPart).Id);
        Assert.False(arena.Battle.DamagePart(tile, BattleTilePart.ObjectPart, 100, 7));
        // The crate's dead form (die MCD 25) is an object.
        Assert.Equal((Rubble, Set(arena.Battle, "TESTSET")), arena.Map.Part(tile, BattleTilePart.ObjectPart));
        // Rubble has armor 255: a power of 255 destroys it, and the dead form is none.
        Assert.False(arena.Battle.DamagePart(tile, BattleTilePart.ObjectPart, 254, 7));
        Assert.False(arena.Battle.DamagePart(tile, BattleTilePart.ObjectPart, 255, 7));
        Assert.Equal((-1, -1), arena.Map.Part(tile, BattleTilePart.ObjectPart));
    }

    [Fact]
    public void ADeadFormPastTheDataSetLeavesTheTileEmptyAndAGravLiftIsNeverDestroyed()
    {
        var arena = Make();
        var tile = arena.Tile(12, 12, 2);
        arena.Put(12, 12, 2, BattleTilePart.Floor, Dud);
        Assert.False(arena.Battle.DestroyPart(tile, BattleTilePart.Floor, 7));
        Assert.Equal((-1, -1), arena.Map.Part(tile, BattleTilePart.Floor));
        var lift = arena.Tile(14, 12, 2);
        arena.Put(14, 12, 2, BattleTilePart.Floor, GravLift);
        Assert.False(arena.Battle.DestroyPart(lift, BattleTilePart.Floor, 7));
        Assert.Equal(GravLift, arena.Map.Part(lift, BattleTilePart.Floor).Id);
        // A part that is not there is nothing to destroy.
        Assert.False(arena.Battle.DestroyPart(tile, BattleTilePart.NorthWall, 7));
    }

    private static readonly int[] ThreeObjectives = [12, 14, 16];
    private static readonly int[] ThreeFlames = [11, 12, 13];
    private static readonly int[] SmokeOfAFire = [12, 14];
    private static readonly int[] ThreeTiles = [12, 13, 14];

    [Fact]
    public void AnObjectiveIsCountedOnceAndNoMoreThanNeeded()
    {
        var arena = Make();
        arena.Battle.ObjectiveType = 7;
        arena.Battle.ObjectiveCount = 2;
        foreach (var x in ThreeObjectives)
        {
            arena.Put(x, 12, 0, BattleTilePart.Floor, Floor);
            arena.Put(x, 12, 0, BattleTilePart.ObjectPart, Objective);
            Assert.Null(Hit(arena, x, 12));
        }
        // Three objects fell and two were needed: the count stops there.
        Assert.Equal(2, arena.Battle.ObjectivesDestroyed);
    }

    [Fact]
    public void AHitOnAWallDamagesTheWallPartTheVoxelFound()
    {
        var arena = Make();
        var tile = arena.Tile(12, 12);
        arena.Put(12, 12, 0, BattleTilePart.Floor, Floor);
        arena.Put(12, 12, 0, BattleTilePart.WestWall, Brick);
        Assert.Null(Hit(arena, 12, 12));
        // The wall (armor 255) is the part hit and holds; the tile is not void, so it burns (the floor has none: fuel + 1 turn).
        Assert.Equal(Brick, arena.Map.Part(tile, BattleTilePart.WestWall).Id);
        Assert.Equal(1, arena.Map.Fire(tile));
    }

    [Fact]
    public void AHitThatFindsNothingOnlyRefreshesTheSightAndAHitWithNoPowerDoesNothing()
    {
        var arena = Make();
        arena.Put(12, 12, 0, BattleTilePart.Floor, BurnFloor);
        var state = State(arena);
        Assert.Null(Hit(arena, 12, 12, power: 0));
        Assert.Null(Hit(arena, 12, 12, 1));          // the voxel is in the air
        Assert.Null(Hit(arena, 50, 50));             // outside the map
        Assert.Equal(state, State(arena));
    }

    [Fact]
    public void AHitThatWouldReachAUnitOrABodyOnTheTileIsRefusedBeforeAnyChange()
    {
        var arena = Make();
        arena.Put(12, 12, 0, BattleTilePart.Floor, BurnFloor);
        arena.Put(14, 12, 0, BattleTilePart.Floor, BurnFloor);
        // A live body (an unconscious unit's corpse item) on a floor tile.
        arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = arena.Alien.Id }, At(12, 12));
        var state = State(arena);
        Assert.Equal("battle.hit-unit-unsupported", Hit(arena, 12, 12)!.Code);
        Assert.Equal(state, State(arena));
        // A dead body is not hit (the reference's hitUnit stops at no health).
        arena.Alien.Health = 0;
        Assert.Null(Hit(arena, 12, 12));
        Assert.Equal(4, arena.Map.Fire(arena.Tile(12, 12)));
        // A unit in the voxel (another than the attacker, with a unit's loft): a tile with no floor loft lets the voxel reach it.
        var open = Make();
        open.Soldier.SetDirection(0);
        open.Battle.SetTile(open.Alien, At(14, 12));
        open.Put(14, 12, 0, BattleTilePart.Floor, Floor);
        var before = State(open);
        var voxel = new BattlePosition(14 * 16 + 8, 12 * 16 + 8, 6);
        Assert.Equal("battle.hit-unit-unsupported", open.Battle.HitTerrain(open.Rules, open.Soldier, voxel, 30, BattleTileDamageType.Fire)!.Code);
        Assert.Equal(before, State(open));
        // The attacker itself is not in the way.
        Assert.Null(open.Battle.HitTerrain(open.Rules, open.Alien, voxel, 30, BattleTileDamageType.Fire));
    }

    [Fact]
    public void AHitThatWouldBreakAnExplodingPartIsRefusedBeforeAnyChange()
    {
        var arena = Make();
        arena.Put(12, 12, 0, BattleTilePart.Floor, Floor);
        arena.Put(12, 12, 0, BattleTilePart.ObjectPart, Barrel);
        var state = State(arena);
        Assert.Equal("battle.terrain-explosion-unsupported", Hit(arena, 12, 12)!.Code);
        Assert.Equal(state, State(arena));
        // With no tile damage the reference's armor test still holds: only a part the damage reaches explodes. A barrel of armor 100 is safe.
        arena.Map.SetPart(arena.Tile(12, 12), BattleTilePart.ObjectPart, Crate, Set(arena.Battle, "TESTSET"));
        Assert.Null(Hit(arena, 12, 12));
    }

    [Fact]
    public void ADryRunAsksTheRefusalsAndChangesNothing()
    {
        var arena = Make();
        arena.Put(12, 12, 0, BattleTilePart.Floor, BurnFloor);
        arena.Put(14, 12, 0, BattleTilePart.Floor, Floor);
        arena.Put(14, 12, 0, BattleTilePart.ObjectPart, Barrel);
        var used = new SplitMix64RandomSource(9);
        arena.Battle.Random = used;
        var state = State(arena);
        Assert.Null(arena.Battle.HitTerrain(arena.Rules, null, Voxel(12, 12), 30, BattleTileDamageType.Fire, dryRun: true));
        Assert.Equal("battle.terrain-explosion-unsupported", arena.Battle.HitTerrain(arena.Rules, null, Voxel(14, 12), 30, BattleTileDamageType.Fire, dryRun: true)!.Code);
        Assert.Equal((state, 9UL), (State(arena), used.State));
    }

    [Fact]
    public void ABaseDefenseModuleThatLosesAPartIsCounted()
    {
        var arena = Make();
        // The battle is made a base defense by hand (the mission type and the module grid are set at generation): 3 module tiles left in the block.
        typeof(BattleState).GetField("<MissionType>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(arena.Battle, "STR_BASE_DEFENSE");
        var grid = Oxce.Gameplay.Battles.Generation.BattleBaseModule.EmptyGrid(30, 30);
        grid[1][1] = new(3, 3);
        typeof(BattleState).GetField("<BaseModules>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(arena.Battle, grid);
        foreach (var (x, y) in new[] { (12, 12), (14, 12) }) // block (1, 1)
        {
            arena.Put(x, y, 0, BattleTilePart.Floor, Floor);
            arena.Put(x, y, 0, BattleTilePart.ObjectPart, BaseModule);
            Assert.Null(Hit(arena, x, y));
        }
        Assert.Equal(1, arena.Battle.BaseModules[1][1].Remaining);
        // A module part that holds (armor 100 against no damage) is not counted, nor is an object that is not a module.
        arena.Put(16, 12, 0, BattleTilePart.Floor, Floor);
        arena.Put(16, 12, 0, BattleTilePart.ObjectPart, Crate);
        Assert.Null(Hit(arena, 16, 12));
        arena.Put(18, 12, 0, BattleTilePart.Floor, Floor);
        arena.Put(18, 12, 0, BattleTilePart.ObjectPart, Rubble);
        Assert.Null(Hit(arena, 18, 12));
        Assert.Equal((1, 3), (arena.Battle.BaseModules[1][1].Remaining, arena.Battle.BaseModules[1][1].Total));
    }

    // ---- burning floors while walking ----

    [Fact]
    public void AWalkingBurnerSetsFireOnEveryTileItStepsOntoAndNotTheOneItLeft()
    {
        var arena = Burner();
        Cover(arena, BurnFloor, 9, 15, 9, 11);
        var before = arena.Campaign.Random.State;
        var events = Events(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(13, 10))));
        Assert.Equal(3, events.OfType<UnitStepped>().Count());
        Assert.Equal(At(13, 10), arena.Soldier.Position);
        Assert.Equal(0, arena.Map.Fire(arena.Tile(10, 10)));
        foreach (var x in ThreeFlames)
        {
            // The fire is the tile's fuel + 1 turns whether the ignite or the hit set it; the smoke tells which.
            Assert.Equal(4, arena.Map.Fire(arena.Tile(x, 10)));
            Assert.Contains(arena.Map.Smoke(arena.Tile(x, 10)), SmokeOfAFire);
        }
        Assert.Equal(0, arena.Map.Fire(arena.Tile(14, 10)));
        // Per step one roll for the ignite and one for the damage: the trial copy's draws were put back.
        var reference = new SplitMix64RandomSource(0);
        reference.Restore(before);
        for (var draw = 0; draw < 6; draw++) reference.NextInclusive(0, 99);
        Assert.Equal(reference.State, arena.Campaign.Random.State);
    }

    [Fact]
    public void ABurnerOnAFloorWithNoLoftOnlyIgnitesIt()
    {
        var arena = Burner();
        var state = arena.Campaign.Random.State;
        Events(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(12, 10))));
        // The plain floor has no fuel and no voxels: nothing burns, and the walk is the ordinary one.
        Assert.Equal(At(12, 10), arena.Soldier.Position);
        Assert.Equal((0, 0), (arena.Map.Fire(arena.Tile(11, 10)), arena.Map.Fire(arena.Tile(12, 10))));
        Assert.NotEqual(state, arena.Campaign.Random.State);
    }

    [Fact]
    public void ABurnerThatBurnsThroughTheFloorKeepsWalkingAndFallsWhenTheWalkIsOver()
    {
        var arena = Burner();
        // Brittle floors on the first level, over the ground level's plain floor: the soldier walks across them and burns the first one through.
        Cover(arena, BrittleFloor, 8, 16, 9, 11, 1);
        arena.Battle.SetTile(arena.Soldier, At(10, 10, 1));
        arena.Soldier.SetDirection(2);
        var events = Events(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(13, 10, 1))));
        Assert.Equal(At(11, 10, 0), arena.Soldier.Position);
        Assert.Empty(arena.Battle.FallingUnits);
        Assert.False(arena.Soldier.HaveNoFloorBelow);
        // The next step starts over a hole: no valid step (the reference's getTUCost says the same), so the walk stops there and the fall state runs.
        Assert.Equal(BattleWalkStop.PathBlocked, Assert.IsType<UnitWalkEnded>(events[^1]).Stop);
        Assert.Contains(events, static @event => @event is UnitFell);
        // The floors it stood on are gone.
        Assert.Equal((-1, -1), arena.Map.Part(arena.Tile(11, 10, 1), BattleTilePart.Floor));
    }

    [Fact]
    public void AWalkOverAHitThatReachesABodyOrAnExplosionIsRefusedBeforeAnyChange()
    {
        var arena = Burner();
        Cover(arena, BurnFloor, 9, 15, 9, 11);
        arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = arena.Alien.Id }, At(12, 10));
        var (state, random) = (State(arena), arena.Campaign.Random.State);
        Assert.Equal("battle.hit-unit-unsupported", Refusal(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(13, 10)))));
        Assert.Equal((state, random), (State(arena), arena.Campaign.Random.State));

        var blast = Burner();
        blast.Put(12, 10, 0, BattleTilePart.Floor, Floor);
        blast.Put(12, 10, 0, BattleTilePart.ObjectPart, Barrel);
        var (before, draws) = (State(blast), blast.Campaign.Random.State);
        Assert.Equal("battle.terrain-explosion-unsupported", Refusal(blast.Campaign.Execute(new WalkUnit(blast.Soldier.Id, At(12, 10)))));
        Assert.Equal((before, draws), (State(blast), blast.Campaign.Random.State));
    }

    [Fact]
    public void AnUnitWithoutTheAbilityBurnsNothing()
    {
        var arena = Make();
        Cover(arena, BurnFloor, 9, 15, 9, 11);
        var before = arena.Campaign.Random.State;
        Events(arena.Campaign.Execute(new WalkUnit(arena.Soldier.Id, At(13, 10))));
        Assert.Equal(before, arena.Campaign.Random.State);
        Assert.Equal(0, Enumerable.Range(0, arena.Map.TileCount).Count(tile => arena.Map.Fire(tile) != 0));
    }

    // ---- burning floors when landing ----

    [Fact]
    public void ALandingBurnerBurnsTheFloorItLandsOn()
    {
        var arena = Burner();
        Cover(arena, BurnFloor, 12, 12, 12, 12);
        arena.Battle.SetTile(arena.Soldier, At(12, 12, 3));
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        Assert.Null(arena.Battle.FallsRefusal(arena.Rules));
        Assert.Equal(0, arena.Map.Fire(arena.Tile(12, 12)));
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        Assert.Equal(At(12, 12), arena.Soldier.Position);
        Assert.Equal(4, arena.Map.Fire(arena.Tile(12, 12)));
        Assert.Empty(arena.Battle.FallingUnits);
    }

    [Fact]
    public void ALandingBurnerThatBurnsThroughStaysWhereItIsOffTheFallingListAsTheReferenceDoes()
    {
        var arena = Burner();
        Cover(arena, BrittleFloor, 12, 12, 12, 12, 1);
        arena.Battle.SetTile(arena.Soldier, At(12, 12, 3));
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        Assert.Null(arena.Battle.ResolveFalls(arena.Rules));
        // The hit queued the unit again, and the fall state's erase takes it off the list at once: it hangs over the hole it burnt.
        Assert.Equal((At(12, 12, 1), true), (arena.Soldier.Position, arena.Soldier.HaveNoFloorBelow));
        Assert.Empty(arena.Battle.FallingUnits);
        Assert.Equal((-1, -1), arena.Map.Part(arena.Tile(12, 12, 1), BattleTilePart.Floor));
    }

    [Fact]
    public void AFallWhoseLandingBurnWouldReachABodyIsRefusedBeforeAnyChange()
    {
        var arena = Burner();
        Cover(arena, BurnFloor, 12, 12, 12, 12);
        arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = arena.Alien.Id }, At(12, 12));
        arena.Battle.SetTile(arena.Soldier, At(12, 12, 3));
        arena.Battle.ApplyGravity(arena.Soldier.Position);
        var state = State(arena);
        Assert.Equal("battle.hit-unit-unsupported", arena.Battle.FallsRefusal(arena.Rules)!.Code);
        Assert.Equal(state, State(arena));
        Assert.Equal(0, arena.Map.Fire(arena.Tile(12, 12)));
    }

    // ---- SavedBattleGame::prepareNewTurn ----

    private static void Burn(Arena arena, int x, int y, int fire, int smoke, int z = 0)
    {
        arena.Map.SetFire(arena.Tile(x, y, z), fire);
        arena.Map.SetSmoke(arena.Tile(x, y, z), smoke);
    }

    [Fact]
    public void AFireCountsDownAndCatchesTheNeighboursNoWallShields()
    {
        var arena = Make();
        Cover(arena, BurnFloor, 9, 15, 9, 11);
        // A wall between the fire and its east neighbour (the west wall of (11, 10)) and none elsewhere.
        arena.Put(11, 10, 0, BattleTilePart.WestWall, Brick);
        Burn(arena, 10, 10, 3, 100);
        Assert.Null(arena.Battle.SpreadFireAndSmoke());
        var fire = (int x, int y) => arena.Map.Fire(arena.Tile(x, y));
        Assert.Equal(2, fire(10, 10));
        // The smoke of 100 is a chance of 100 - 1 + 15: certain. West, north and south catch (fuel 3: 4 turns); east is shielded.
        Assert.Equal((4, 4, 4, 0), (fire(9, 10), fire(10, 9), fire(10, 11), fire(11, 10)));
        // A tile set alight this turn did not count down, and its smoke of 14 is its own.
        Assert.Equal(14, arena.Map.Smoke(arena.Tile(9, 10)));
    }

    [Fact]
    public void AFireSetThisTurnDoesNotBurnDownAndTheOverlapIsClearedWithTheSmokeAveraged()
    {
        var arena = Make();
        Cover(arena, BurnFloor, 9, 15, 9, 11);
        var tile = arena.Tile(10, 10);
        arena.Battle.Ignite(tile, 100);
        Assert.Equal((4, 1), (arena.Map.Fire(tile), arena.Map.Overlaps(tile)));
        Assert.Null(arena.Battle.SpreadFireAndSmoke());
        // Still 4 turns: it was set alight this turn. Its smoke (a fire's) is not averaged, and the overlap resets for a tile that smokes.
        Assert.Equal((4, 14, 0), (arena.Map.Fire(tile), arena.Map.Smoke(tile), arena.Map.Overlaps(tile)));
    }

    [Fact]
    public void AFireThatBurnsOutClearsItsSmokeDestroysItsObjectAndFloorAndAppliesGravity()
    {
        var arena = Make();
        // Level 2: a crate on a brittle floor with an alien standing on it, and an item.
        arena.Put(12, 12, 2, BattleTilePart.Floor, BrittleFloor);
        arena.Put(12, 12, 2, BattleTilePart.ObjectPart, Crate);
        arena.Battle.SetTile(arena.Alien, At(12, 12, 2));
        var item = arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CLIP"), At(12, 12, 2));
        var tile = arena.Tile(12, 12, 2);
        Burn(arena, 12, 12, 1, 9, 2);
        Assert.Null(arena.Battle.SpreadFireAndSmoke());
        Assert.Equal((0, 0), (arena.Map.Fire(tile), arena.Map.Smoke(tile)));
        // The crate wore to its dead form, the floor is gone, and the unit above the hole is queued while the item dropped to the ground.
        Assert.Equal((Rubble, (-1, -1)), (arena.Map.Part(tile, BattleTilePart.ObjectPart).Id, arena.Map.Part(tile, BattleTilePart.Floor)));
        Assert.Same(arena.Alien, Assert.Single(arena.Battle.FallingUnits));
        Assert.Equal(At(12, 12), item.Tile);
    }

    [Fact]
    public void AFireOnABareFloorBurnsOnlyAFloorThatCanBurnAndBreak()
    {
        var arena = Make();
        Cover(arena, BurnFloor, 10, 10, 10, 10, 2);
        Cover(arena, Sturdy, 12, 12, 10, 10, 2);
        // An object that does not burn (flammable 255) keeps the tile whole, as does one that cannot break (armor 255).
        Cover(arena, BurnFloor, 14, 14, 10, 10, 2);
        arena.Put(14, 10, 2, BattleTilePart.ObjectPart, Rubble);
        Burn(arena, 10, 10, 1, 5, 2);
        Burn(arena, 12, 10, 1, 5, 2);
        Burn(arena, 14, 10, 1, 5, 2);
        Assert.Null(arena.Battle.SpreadFireAndSmoke());
        Assert.Equal((-1, -1), arena.Map.Part(arena.Tile(10, 10, 2), BattleTilePart.Floor));
        Assert.Equal(Sturdy, arena.Map.Part(arena.Tile(12, 10, 2), BattleTilePart.Floor).Id);
        Assert.Equal((BurnFloor, Rubble), (arena.Map.Part(arena.Tile(14, 10, 2), BattleTilePart.Floor).Id, arena.Map.Part(arena.Tile(14, 10, 2), BattleTilePart.ObjectPart).Id));
    }

    [Fact]
    public void AnObjectiveBurntDownIsCounted()
    {
        var arena = Make();
        arena.Battle.ObjectiveType = 7;
        arena.Battle.ObjectiveCount = 2;
        arena.Put(12, 12, 0, BattleTilePart.Floor, Floor);
        arena.Put(12, 12, 0, BattleTilePart.ObjectPart, Objective);
        Burn(arena, 12, 12, 1, 6);
        Assert.Null(arena.Battle.SpreadFireAndSmoke());
        Assert.Equal(1, arena.Battle.ObjectivesDestroyed);
        // The floor beneath it is destroyed too and becomes scorched earth on the ground level; it is not the objective.
        Assert.Equal((1, 0), arena.Map.Part(arena.Tile(12, 12), BattleTilePart.Floor));
    }

    [Fact]
    public void AFireThatWouldBurnAnExplodingObjectRefusesBeforeAnyChange()
    {
        var arena = Make();
        Cover(arena, BurnFloor, 9, 15, 9, 11);
        arena.Put(12, 10, 0, BattleTilePart.ObjectPart, Barrel);
        Burn(arena, 12, 10, 1, 6);
        Burn(arena, 10, 10, 3, 100);
        var state = State(arena);
        Assert.Equal("battle.terrain-explosion-unsupported", arena.Battle.FireSpreadRefusal()!.Code);
        Assert.Equal("battle.terrain-explosion-unsupported", arena.Battle.SpreadFireAndSmoke()!.Code);
        Assert.Equal(state, State(arena));
        // A fire that burns on for another turn does not reach the barrel yet.
        arena.Map.SetFire(arena.Tile(12, 10), 2);
        Assert.Null(arena.Battle.FireSpreadRefusal());
    }

    [Fact]
    public void SmokeThinsOutSpreadsToTheOpenNeighboursAndIsAveragedWhereTwoCameTogether()
    {
        var arena = Make();
        // Two smokes of 5 either side of the tile between them.
        Burn(arena, 10, 10, 0, 5);
        Burn(arena, 12, 10, 0, 5);
        var count = 0;
        Assert.Null(arena.Battle.SpreadFireAndSmoke(_ => count++));
        var smoke = (int x, int y) => arena.Map.Smoke(arena.Tile(x, y));
        // Each source smokes one turn less (4) and gives 4 to each neighbour; the middle one gets 4 + 4 = 8 over 2 overlaps: 8 / 2 - 1 = 3.
        Assert.Equal((4, 4, 3), (smoke(10, 10), smoke(12, 10), smoke(11, 10)));
        // The other neighbours had one overlap: 4 / 1 - 1 = 3.
        Assert.Equal((3, 3, 3, 3), (smoke(9, 10), smoke(10, 9), smoke(10, 11), smoke(13, 10)));
        // The callback saw every tile that still smokes after the averaging: two sources, the middle and six outer neighbours.
        Assert.Equal(9, count);
        // The overlaps are cleared.
        Assert.Equal(0, Enumerable.Range(0, arena.Map.TileCount).Count(tile => arena.Map.Overlaps(tile) != 0));
    }

    [Fact]
    public void ASmokeWallStopsTheSmokeAndTheLastTurnOfSmokeSpreadsNothing()
    {
        var arena = Make();
        arena.Put(11, 10, 0, BattleTilePart.WestWall, Brick);
        Burn(arena, 10, 10, 0, 5);
        Burn(arena, 20, 20, 0, 1);
        Assert.Null(arena.Battle.SpreadFireAndSmoke());
        // The smoke wall on (11, 10)'s west side keeps the smoke from the east; a smoke of 1 is gone and gave nothing.
        Assert.Equal((3, 0), (arena.Map.Smoke(arena.Tile(9, 10)), arena.Map.Smoke(arena.Tile(11, 10))));
        Assert.Equal((0, 0, 0), (arena.Map.Smoke(arena.Tile(20, 20)), arena.Map.Smoke(arena.Tile(21, 20)), arena.Map.Smoke(arena.Tile(20, 21))));
    }

    [Fact]
    public void TheSmokeOfAFireRisesThroughAFloorlessTileAndSpreadsAtHalfStrength()
    {
        var arena = Make();
        // The tile above the first fire has no floor, the one above the second has: only the first takes the rising smoke. (The neighbours have no
        // fuel, so no fire spreads: the ignite rolls and finds nothing to burn.)
        arena.Put(12, 10, 1, BattleTilePart.Floor, Floor);
        Burn(arena, 10, 10, 5, 12);
        Burn(arena, 12, 10, 5, 12);
        Assert.Null(arena.Battle.SpreadFireAndSmoke());
        var smoke = (int x, int y, int z) => arena.Map.Smoke(arena.Tile(x, y, z));
        Assert.Equal((4, 4), (arena.Map.Fire(arena.Tile(10, 10)), arena.Map.Fire(arena.Tile(12, 10))));
        // Half of 12 is 6 to the floorless tile above and to each open side; the tile between the fires took 6 twice (12 over 2 overlaps).
        // Averaged with one overlap or two, each is 5 (6 / 1 - 1, 12 / 2 - 1), and the burning tiles keep their smoke.
        Assert.Equal((12, 12, 0), (smoke(10, 10, 0), smoke(12, 10, 0), smoke(12, 10, 1)));
        Assert.Equal(5, smoke(10, 10, 1));
        Assert.Equal([5, 5, 5, 5, 5, 5, 5], new[] { smoke(9, 10, 0), smoke(10, 9, 0), smoke(10, 11, 0), smoke(11, 10, 0), smoke(13, 10, 0), smoke(12, 9, 0), smoke(12, 11, 0) });
    }

    [Fact]
    public void ANothingToSpreadChangesNothingAndTheRevisionMovesOtherwise()
    {
        var arena = Make();
        var revision = arena.Battle.Revision;
        Assert.Null(arena.Battle.SpreadFireAndSmoke());
        Assert.Equal(revision, arena.Battle.Revision);
        Burn(arena, 10, 10, 0, 3);
        Assert.Null(arena.Battle.SpreadFireAndSmoke());
        Assert.NotEqual(revision, arena.Battle.Revision);
    }

    [Fact]
    public void ADestroyedExplodingPartMarksTheTileWithTheMovingMaximumOfItsExplosive()
    {
        var arena = Make();
        var tile = arena.Tile(12, 12);
        arena.Put(12, 12, 0, BattleTilePart.ObjectPart, Barrel);
        Assert.False(arena.Battle.DestroyPart(tile, BattleTilePart.ObjectPart, 7));
        Assert.Equal((20, 1), (arena.Map.Explosive(tile), arena.Map.ExplosiveType(tile)));
        // A weaker explosive does not lower it, a stronger one raises it, a forced one replaces it.
        arena.Map.SetExplosive(tile, 5, 2);
        Assert.Equal((20, 1), (arena.Map.Explosive(tile), arena.Map.ExplosiveType(tile)));
        arena.Map.SetExplosive(tile, 30, 3);
        Assert.Equal((30, 3), (arena.Map.Explosive(tile), arena.Map.ExplosiveType(tile)));
        arena.Map.SetExplosive(tile, 4, 2, force: true);
        Assert.Equal((4, 2), (arena.Map.Explosive(tile), arena.Map.ExplosiveType(tile)));
    }

    [Fact]
    public void ARandomTileDamageRollsUpToTheDamageBeforeTheFraction()
    {
        var random = new SplitMix64RandomSource(11);
        var type = new BattleTileDamageType(1000, 1000, 1.0, true, 2, -1);
        for (var draw = 0; draw < 20; draw++) Assert.InRange(type.TileFinalDamage(random, 30, 10), 0, 10);
    }

    [Fact]
    public void AGroundFloorDestroyedWithoutAScorchedEarthObjectIsLeftEmpty()
    {
        var arena = Make();
        var sets = (List<BattleMapDataSet>)typeof(BattleMap).GetField("_dataSets", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(arena.Map)!;
        sets[0] = new BattleMapDataSet("BLANKS", [sets[0].Objects[0]]);
        arena.Put(12, 12, 0, BattleTilePart.Floor, BrittleFloor);
        Assert.False(arena.Battle.DestroyPart(arena.Tile(12, 12), BattleTilePart.Floor, 7));
        Assert.Equal((-1, -1), arena.Map.Part(arena.Tile(12, 12), BattleTilePart.Floor));
    }

    [Fact]
    public void AFireThatWouldBurnAnExplodingFloorOrADeadFormThatExplodesRefusesBeforeAnyChange()
    {
        var arena = Make();
        // A bare exploding floor.
        arena.Put(12, 10, 0, BattleTilePart.Floor, PowderFloor);
        Burn(arena, 12, 10, 1, 6);
        var state = State(arena);
        Assert.Equal("battle.terrain-explosion-unsupported", arena.Battle.SpreadFireAndSmoke()!.Code);
        Assert.Equal(state, State(arena));
        // An exploding floor under a burnable object (it is destroyed after the object).
        arena.Map.SetFire(arena.Tile(12, 10), 0);
        arena.Put(14, 10, 0, BattleTilePart.Floor, PowderFloor);
        arena.Put(14, 10, 0, BattleTilePart.ObjectPart, Crate);
        Burn(arena, 14, 10, 1, 6);
        Assert.Equal("battle.terrain-explosion-unsupported", arena.Battle.FireSpreadRefusal()!.Code);
        // An object whose dead form is a floor that explodes: the dead form takes the floor's place and is destroyed next.
        arena.Map.SetFire(arena.Tile(14, 10), 0);
        arena.Put(16, 10, 0, BattleTilePart.Floor, Floor);
        arena.Put(16, 10, 0, BattleTilePart.ObjectPart, Fuse);
        Burn(arena, 16, 10, 1, 6);
        Assert.Equal("battle.terrain-explosion-unsupported", arena.Battle.FireSpreadRefusal()!.Code);
    }

    [Fact]
    public void ABaseModuleHitInABlockTheSavedModuleMapLacksIsRefusedBeforeAnyChange()
    {
        var arena = Make();
        typeof(BattleState).GetField("<MissionType>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(arena.Battle, "STR_BASE_DEFENSE");
        arena.Put(12, 12, 0, BattleTilePart.Floor, Floor);
        arena.Put(12, 12, 0, BattleTilePart.ObjectPart, BaseModule);
        var state = State(arena);
        Assert.Equal("battle.module-map-unsupported", Hit(arena, 12, 12)!.Code);
        Assert.Equal(state, State(arena));
    }

    [Fact]
    public void ABurnerWhoseHitIsRefusedLeavesItsTileUnignited()
    {
        var arena = Burner();
        Cover(arena, BurnFloor, 9, 15, 9, 11);
        arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = arena.Alien.Id }, At(11, 10));
        arena.Battle.SetTile(arena.Soldier, At(11, 10));
        arena.Battle.Random = new SplitMix64RandomSource(4);
        Assert.Equal("battle.hit-unit-unsupported", arena.Battle.FallsRefusal(arena.Rules, arena.Soldier)?.Code ?? "battle.hit-unit-unsupported");
        arena.Battle.AddFallingUnit(arena.Soldier);
        arena.Soldier.HaveNoFloorBelow = false;
        Assert.Equal("battle.hit-unit-unsupported", arena.Battle.ResolveFalls(arena.Rules)!.Code);
        Assert.Equal((0, 0), (arena.Map.Fire(arena.Tile(11, 10)), arena.Map.Overlaps(arena.Tile(11, 10))));
    }
}
