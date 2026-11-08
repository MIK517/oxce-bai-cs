using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;
using static Oxce.CompatibilityTests.BattleMovementGroundworkTests;
using static Oxce.CompatibilityTests.BattleStartTests;
using static Oxce.CompatibilityTests.BattleTestSupport;
using static Oxce.CompatibilityTests.BattleTestTerrain;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S2: pathfinding (<c>src/Battlescape/Pathfinding.cpp</c>, <c>PathfindingNode.cpp</c>, <c>PathfindingOpenSet.cpp</c>) on synthetic
/// maps. The battle is the suited-soldier one past its first turn; the test clears its 30x30x2 map and builds the tiles it needs from the
/// records below (appended to TESTSET after its three, so MAP IDs are the record indexes). The soldier walks (movement type 0) unless a test
/// says it flies.
/// </summary>
public sealed class BattlePathfindingTests
{
    // TESTSET's records after the three the battle generator uses.
    private const int Floor = 3; // walk, slide and fly cost 4
    private const int Hole = 4; // a floor part with no floor, cost 0
    private const int Impassable = 5; // cost 255
    private const int Rubble = 6; // cost 12
    private const int Ladder = 7; // an object that is a grav lift, cost 6
    private const int StairsLow = 8; // an object of terrain level -8, cost 0
    private const int StairsHigh = 9; // an object of terrain level -16, cost 0
    private const int LiftFloor = 10; // a grav lift floor, cost 4
    private const int Wall10 = 11; // cost 10
    private const int Door = 12; // a door, cost 4
    private const int BigWallNesw = 13;
    private const int FlyCostly = 14; // walk 4, slide 5, fly 9
    private const int TfStairsHigh = 15; // terrain level -18
    private const int TfStairsLow = 16; // terrain level -12
    private const int HighObject = 17; // an object of terrain level -24, cost 4
    private const int Floor120 = 18; // cost 120
    private const int FloorFree = 19; // cost 0
    private const int BigWallNwse = 20;
    private const int LadderFree = 21; // a grav lift object, cost 0

    private static byte[] Cost(int walk, int slide, int fly, params (int Offset, byte Value)[] extra) =>
        Record([(39, (byte)walk), (40, (byte)slide), (41, (byte)fly), .. extra]);

    private static void Terrain(Dictionary<string, byte[]> files) => files["TERRAIN/TESTSET.MCD"] = Mcd(Record((42, 2)), Record((42, 3)), Record((42, 4), (59, 2)),
        Cost(4, 4, 4), Cost(0, 0, 0, (32, 1)), Cost(255, 255, 255), Cost(12, 12, 12), Cost(6, 6, 6, (34, 1)), Cost(0, 0, 0, (48, 248)),
        Cost(0, 0, 0, (48, 240)), Cost(4, 4, 4, (34, 1)), Cost(10, 10, 10), Cost(4, 4, 4, (35, 1)), Cost(0, 0, 0, (33, 2)), Cost(4, 5, 9),
        Cost(0, 0, 0, (48, 238)), Cost(0, 0, 0, (48, 244)), Cost(4, 4, 4, (48, 232)), Cost(120, 120, 120), Cost(0, 0, 0), Cost(0, 0, 0, (33, 3)), Cost(0, 0, 0, (34, 1)));

    private sealed record Arena(BattleState Battle, BattleUnit Soldier, RuntimeRuleCatalog Rules)
    {
        public BattleMap Map => Battle.Map;

        public BattlePathfinder Finder(BattlePathingOptions? options = null) => new(Battle, Rules, options);

        public void Put(int x, int y, int z, BattleTilePart part, int id) => Place(Battle, new BattlePosition(x, y, z), part, id);

        public void Put(BattlePosition position, BattleTilePart part, int id) => Place(Battle, position, part, id);

        public void Floors(int z, int x0, int y0, int x1, int y1)
        {
            for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++) Put(x, y, z, BattleTilePart.Floor, Floor);
        }

        public BattleUnit Alien => Battle.Units.First(static unit => unit.Faction == BattleFaction.Hostile);

        public void Stand(BattleUnit unit, int x, int y, int z) => Battle.SetTile(unit, new BattlePosition(x, y, z));
    }

    /// <summary>A flat arena: a floor over the whole ground level, nothing above, the soldier at (5, 5, 0) facing east with plenty of time units.</summary>
    private static Arena Flat(string? extraRules = null, string ufo = "UFO_SUITED", string? rules = null)
    {
        var (_, battle, soldier, set) = Open((rules ?? Rules) + extraRules, Terrain, ufo);
        var map = battle.Map;
        for (var tile = 0; tile < map.TileCount; tile++)
            foreach (var part in Enum.GetValues<BattleTilePart>()) map.SetPart(tile, part, -1, -1);
        var arena = new Arena(battle, soldier, set);
        arena.Floors(0, 0, 0, 29, 29);
        // The other units go to the far corner, out of the way.
        var corner = 29;
        foreach (var unit in battle.Units.Where(unit => unit != soldier)) arena.Stand(unit, corner--, 29, 0);
        arena.Stand(soldier, 5, 5, 0);
        soldier.SetDirection(2);
        soldier.MovementType = 0;
        soldier.TimeUnits = 100;
        soldier.Energy = 100;
        return arena;
    }

    /// <summary>Walks a path step by step with the step costs, checking each is valid; returns where it ends and what it costs.</summary>
    private static (List<BattlePosition> Positions, int Time, int Energy) Follow(BattlePathfinder finder, BattleUnit unit, BattlePosition start,
        IEnumerable<int> path, BattleActionMove bam = BattleActionMove.Normal)
    {
        var positions = new List<BattlePosition> { start };
        int time = 0, energy = 0;
        foreach (var direction in path)
        {
            var step = finder.GetTuCost(positions[^1], direction, unit, bam);
            Assert.True(step.IsValid, $"step {direction} from {positions[^1]}");
            positions.Add(step.Position);
            time += step.Cost.Time;
            energy += step.Cost.Energy;
        }
        return (positions, time, energy);
    }

    private static BattlePosition At(int x, int y, int z = 0) => new(x, y, z);

    private static (int, int) TimeAndEnergy(PathfindingStep step) => (step.Cost.Time, step.Cost.Energy);

    // ---- the cost of a step ----

    [Fact]
    public void AStepCostsTheFloorsCostAndADiagonalOneAndAHalfTimesThatWithHalfAsMuchEnergy()
    {
        var arena = Flat();
        var finder = arena.Finder();
        var east = finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal);
        Assert.Equal((4, 2), TimeAndEnergy(east));
        Assert.Equal(At(6, 5), east.Position);
        var diagonal = finder.GetTuCost(At(5, 5), 3, arena.Soldier, BattleActionMove.Normal);
        Assert.Equal((6, 3), TimeAndEnergy(diagonal));
        Assert.Equal(At(6, 6), diagonal.Position);
        // Outside the map, and with a direction that does not exist.
        Assert.False(finder.GetTuCost(At(0, 0), 0, arena.Soldier, BattleActionMove.Normal).IsValid);
        Assert.False(finder.GetTuCost(At(0, 0), 7, arena.Soldier, BattleActionMove.Normal).IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() => finder.GetTuCost(At(5, 5), 10, arena.Soldier, BattleActionMove.Normal));
    }

    [Fact]
    public void TheMoveTypeAndTheUnitsAndArmorsPercentagesScaleTheCostAndFloorTheTimeAtOne()
    {
        var arena = Flat();
        var finder = arena.Finder();
        // Running is 75% of the time and energy; a walk of 4 is 3 and 3.
        Assert.Equal((3, 3), TimeAndEnergy(finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Run)));
        // The unit's base percentages multiply in: 25% of a step of 4 is 1, 10% would be 0 and is raised to 1 (energy is not).
        arena.Soldier.MoveCosts = RuntimeArmorMoveCosts.Default with { Base = (10, 0) };
        Assert.Equal((1, 0), TimeAndEnergy(finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal)));
        // A cost over 100 is capped there before the percentages (a floor of 120 costs 100).
        arena.Soldier.MoveCosts = RuntimeArmorMoveCosts.Default;
        arena.Put(6, 5, 0, BattleTilePart.Floor, Floor120);
        Assert.Equal((100, 50), TimeAndEnergy(finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal)));
        // A tile that costs nothing is a broken one and costs the default 4.
        arena.Put(6, 5, 0, BattleTilePart.Floor, FloorFree);
        Assert.Equal((4, 2), TimeAndEnergy(finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal)));
        // The floor and the object add.
        arena.Put(6, 5, 0, BattleTilePart.Floor, Floor);
        arena.Put(6, 5, 0, BattleTilePart.ObjectPart, Wall10);
        Assert.Equal((14, 7), TimeAndEnergy(finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal)));
        // Sneaking walks even for a flying unit, which pays the flying cost otherwise.
        arena.Put(6, 5, 0, BattleTilePart.ObjectPart, -1);
        arena.Put(6, 5, 0, BattleTilePart.Floor, FlyCostly);
        arena.Soldier.MovementType = 1;
        Assert.Equal(9, finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal).Cost.Time);
        Assert.Equal(4, finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Sneak).Cost.Time);
        // A slide cost (movement type 2) is read for a unit of that type.
        arena.Soldier.MovementType = 2;
        Assert.Equal(5, finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal).Cost.Time);
    }

    [Theory]
    [InlineData(0, 7, 3)]
    [InlineData(1, 8, 4)]
    [InlineData(2, 7, 4)]
    public void TheMovementCostRoundingModesRoundTheTimeAndEnergyPercentages(int mode, int time, int energy)
    {
        var arena = Flat($"\nextendedMovementCostRounding: {mode}\n");
        // A diagonal step is 6; at 125% the time is exactly 7.5 and the energy exactly 3.75.
        arena.Soldier.MoveCosts = RuntimeArmorMoveCosts.Default with { Base = (125, 125) };
        Assert.Equal((time, energy), TimeAndEnergy(arena.Finder().GetTuCost(At(5, 5), 3, arena.Soldier, BattleActionMove.Normal)));
    }

    [Fact]
    public void WallsCostTheAverageOfTheWallsInTheWayAndAnImpassableWallBlocks()
    {
        var arena = Flat();
        var finder = arena.Finder();
        // North of (6,5): a wall of 12; the north-east step takes it and the west wall of (7,4), which costs 10: their average, 11.
        arena.Put(6, 5, 0, BattleTilePart.NorthWall, Rubble);
        arena.Put(7, 4, 0, BattleTilePart.WestWall, Wall10);
        Assert.Equal(16, finder.GetTuCost(At(6, 5), 0, arena.Soldier, BattleActionMove.Normal).Cost.Time);
        Assert.Equal(6 + 11, finder.GetTuCost(At(6, 5), 1, arena.Soldier, BattleActionMove.Normal).Cost.Time);
        // An impassable wall blocks the step and, as a corner, the diagonal past it.
        arena.Put(7, 5, 0, BattleTilePart.WestWall, Impassable);
        Assert.False(finder.GetTuCost(At(6, 5), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        Assert.False(finder.GetTuCost(At(6, 5), 1, arena.Soldier, BattleActionMove.Normal).IsValid);
        Assert.False(finder.GetTuCost(At(6, 6), 1, arena.Soldier, BattleActionMove.Normal).IsValid);
        Assert.True(finder.GetTuCost(At(6, 6), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        // An impassable object on the destination blocks it.
        arena.Put(8, 8, 0, BattleTilePart.ObjectPart, Impassable);
        Assert.False(finder.GetTuCost(At(7, 8), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        // A height step of more than 8 voxels is refused, 8 is not.
        arena.Put(10, 10, 0, BattleTilePart.ObjectPart, StairsLow);
        arena.Put(11, 10, 0, BattleTilePart.ObjectPart, StairsHigh);
        Assert.True(finder.GetTuCost(At(9, 10), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        Assert.True(finder.GetTuCost(At(10, 10), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        Assert.False(finder.GetTuCost(At(11, 9), 4, arena.Soldier, BattleActionMove.Normal).IsValid);
    }

    [Fact]
    public void StrictBlockedCheckingAlsoLooksAtTheBigWallOfTheNextTileInTheDirection()
    {
        var arena = Flat();
        arena.Put(7, 4, 0, BattleTilePart.ObjectPart, BigWallNesw);
        var plain = arena.Finder();
        var strict = arena.Finder(new BattlePathingOptions { StrictBlockedChecking = true });
        Assert.False(plain.IsBlockedDirection(arena.Soldier, At(7, 5), 0));
        Assert.True(strict.IsBlockedDirection(arena.Soldier, At(7, 5), 0));
        // A diagonal past a big wall of the other diagonal is blocked either way.
        arena.Put(7, 5, 0, BattleTilePart.ObjectPart, BigWallNwse);
        Assert.True(plain.IsBlockedDirection(arena.Soldier, At(6, 5), 1));
        Assert.False(plain.IsBlockedDirection(arena.Soldier, At(6, 5), 0));
    }

    [Fact]
    public void ADepthAddsTwoToATileOnFireOrFullOfSmokeAndAHostileUnitAvoidsFireWithAPenaltyOnly()
    {
        var arena = Flat(ufo: "UFO_FLOAT_DEEP");
        Assert.True(arena.Battle.Depth > 0);
        var finder = arena.Finder();
        arena.Map.SetFire(arena.Map.TileIndex(At(6, 5)), 3);
        arena.Map.SetSmoke(arena.Map.TileIndex(At(5, 6)), 1);
        Assert.Equal(6, finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal).Cost.Time);
        Assert.Equal(6, finder.GetTuCost(At(5, 5), 4, arena.Soldier, BattleActionMove.Normal).Cost.Time);
        Assert.Equal(0, finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal).Penalty.Time);
        // The alien avoids fire: the step stays valid and carries a penalty of 32 the search adds (a player's unit has none).
        arena.Stand(arena.Alien, 5, 5, 0);
        arena.Alien.MovementType = 0;
        var step = finder.GetTuCost(At(5, 5), 2, arena.Alien, BattleActionMove.Normal);
        Assert.Equal((6, 32), (step.Cost.Time, step.Penalty.Time));
    }

    [Fact]
    public void AStrafingStepCostsOneMoreUnlessItIsInTheFacingDirectionAndBackwardsStrafingIsOff()
    {
        var arena = Flat();
        var finder = arena.Finder();
        // Facing east (2): a step south is within two eighths of the facing.
        Assert.Equal([4], finder.Calculate(arena.Soldier, At(5, 6), BattleActionMove.Strafe));
        Assert.True(finder.StrafeMove);
        Assert.Equal(5, finder.GetTuCost(At(5, 5), 4, arena.Soldier, BattleActionMove.Strafe).Cost.Time);
        Assert.Equal(4, finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Strafe).Cost.Time);
        // Backwards is not supported: the strafe move is off, the step is a plain one, still found.
        Assert.Equal([6], finder.Calculate(arena.Soldier, At(4, 5), BattleActionMove.Strafe));
        Assert.False(finder.StrafeMove);
        Assert.Equal(4, finder.GetTuCost(At(5, 5), 6, arena.Soldier, BattleActionMove.Strafe).Cost.Time);
        // Not adjacent: no strafe move.
        finder.Calculate(arena.Soldier, At(5, 8), BattleActionMove.Strafe);
        Assert.False(finder.StrafeMove);
    }

    // ---- levels ----

    [Fact]
    public void StairsAreWalkedUpOnTheLowPartThenTheHighPartThenOntoTheLevelAboveAndDownAgain()
    {
        var arena = Flat();
        // Going north: a low part (-8) at y 8, a high part (-16) at y 7, the upper floor at (10, 6) on level 1.
        arena.Put(10, 8, 0, BattleTilePart.ObjectPart, StairsLow);
        arena.Put(10, 7, 0, BattleTilePart.ObjectPart, StairsHigh);
        arena.Put(10, 6, 1, BattleTilePart.Floor, Floor);
        var finder = arena.Finder();
        Assert.True(finder.IsOnStairs(At(10, 9), At(10, 6)));
        Assert.True(finder.IsOnStairs(At(10, 8), At(10, 6)));
        Assert.True(finder.IsOnStairs(At(10, 7), At(10, 6)));
        Assert.False(finder.IsOnStairs(At(10, 10), At(10, 6)));
        Assert.False(finder.IsOnStairs(At(9, 9), At(10, 6)));

        arena.Stand(arena.Soldier, 10, 9, 0);
        // A click behind the stairs, on the ground, goes up them.
        var up = finder.Calculate(arena.Soldier, At(10, 6), BattleActionMove.Normal);
        Assert.Equal([0, 0, 0], up);
        var walk = Follow(finder, arena.Soldier, At(10, 9), up);
        Assert.Equal([At(10, 9), At(10, 8), At(10, 7), At(10, 6, 1)], walk.Positions);
        // 4 for each part of the stairs on the floor, 5 for the step up a level (one extra).
        Assert.Equal(4 + 4 + 5, walk.Time);
        // A player's unit going somewhere else on the level does not take the stairs.
        Assert.Equal([2, 2], finder.Calculate(arena.Soldier, At(12, 9), BattleActionMove.Normal));

        // And down: from the upper floor the step onto the high part has no floor above it and goes down onto it.
        arena.Stand(arena.Soldier, 10, 6, 1);
        var down = finder.Calculate(arena.Soldier, At(10, 9), BattleActionMove.Normal);
        Assert.Equal([4, 4, 4], down);
        var back = Follow(finder, arena.Soldier, At(10, 6, 1), down);
        Assert.Equal([At(10, 6, 1), At(10, 7), At(10, 8), At(10, 9)], back.Positions);
        Assert.Equal(12, back.Time);

        // A flying unit does not walk down: it stays on the level above.
        arena.Soldier.MovementType = 1;
        Assert.Equal(At(10, 7, 1), finder.GetTuCost(At(10, 6, 1), 4, arena.Soldier, BattleActionMove.Normal).Position);
    }

    [Fact]
    public void TheEastWestAndTheTftdStairsAreStairsToo()
    {
        var arena = Flat();
        var finder = arena.Finder();
        // East-west: the high part (-16) one tile east of the end, the low part (-8) two east.
        arena.Put(13, 12, 0, BattleTilePart.ObjectPart, StairsHigh);
        arena.Put(14, 12, 0, BattleTilePart.ObjectPart, StairsLow);
        Assert.True(finder.IsOnStairs(At(15, 12), At(12, 12)));
        Assert.True(finder.IsOnStairs(At(14, 12), At(12, 12)));
        Assert.False(finder.IsOnStairs(At(16, 12), At(12, 12)));
        // A different second part is not stairs.
        arena.Put(14, 12, 0, BattleTilePart.ObjectPart, -1);
        Assert.False(finder.IsOnStairs(At(15, 12), At(12, 12)));
        // TFTD's: -18 then -12.
        arena.Put(20, 21, 0, BattleTilePart.ObjectPart, TfStairsHigh);
        arena.Put(20, 22, 0, BattleTilePart.ObjectPart, TfStairsLow);
        Assert.True(finder.IsOnStairs(At(20, 23), At(20, 20)));
        arena.Put(20, 22, 0, BattleTilePart.ObjectPart, StairsLow);
        Assert.False(finder.IsOnStairs(At(20, 23), At(20, 20)));
        arena.Put(20, 21, 0, BattleTilePart.ObjectPart, -1);
        arena.Put(20, 22, 0, BattleTilePart.ObjectPart, -1);
        arena.Put(21, 20, 0, BattleTilePart.ObjectPart, TfStairsHigh);
        arena.Put(22, 20, 0, BattleTilePart.ObjectPart, TfStairsLow);
        Assert.True(finder.IsOnStairs(At(23, 20), At(20, 20)));
        // The tiles beyond the map are not stairs.
        Assert.False(finder.IsOnStairs(At(29, 29), At(29, 29)));
    }

    [Fact]
    public void ADestinationAboveAHoleIsLoweredAndOneInsideAFullHeightObjectIsRaised()
    {
        var arena = Flat();
        var finder = arena.Finder();
        arena.Soldier.MovementType = 1; // flying, so it can leave the ground
        arena.Floors(1, 4, 4, 12, 12);
        arena.Put(8, 5, 1, BattleTilePart.Floor, Hole);
        arena.Stand(arena.Soldier, 6, 5, 1);
        // A hole on level 1 above the floor of level 0: a flying unit goes there.
        var over = finder.Calculate(arena.Soldier, At(8, 5, 1), BattleActionMove.Normal);
        Assert.Equal(At(8, 5, 1), Follow(finder, arena.Soldier, At(6, 5, 1), over).Positions[^1]);

        // A walking unit's destination is lowered: it ends where it would fall to.
        arena.Soldier.MovementType = 0;
        var lowered = finder.Calculate(arena.Soldier, At(8, 5, 1), BattleActionMove.Normal);
        Assert.Equal([2, 2, 9], lowered);
        Assert.Equal(At(8, 5), Follow(finder, arena.Soldier, At(6, 5, 1), lowered).Positions[^1]);

        // An object of terrain level -24 is the floor of the level above: the destination goes up (a flying unit reaches it).
        arena.Soldier.MovementType = 1;
        arena.Put(10, 10, 0, BattleTilePart.ObjectPart, HighObject);
        arena.Stand(arena.Soldier, 6, 6, 0);
        var raised = finder.Calculate(arena.Soldier, At(10, 10), BattleActionMove.Normal);
        Assert.Equal(At(10, 10, 1), Follow(finder, arena.Soldier, At(6, 6), raised).Positions[^1]);
        // On the top level there is no level above: no path, and a destination outside the map is none.
        arena.Put(14, 14, 1, BattleTilePart.ObjectPart, HighObject);
        arena.Put(14, 14, 1, BattleTilePart.Floor, Floor);
        Assert.Empty(finder.Calculate(arena.Soldier, At(14, 14, 1), BattleActionMove.Normal));
        Assert.Empty(finder.Calculate(arena.Soldier, At(30, 5), BattleActionMove.Normal));
        Assert.Empty(finder.Calculate(arena.Soldier, At(-1, 5), BattleActionMove.Normal));
        Assert.Empty(finder.Calculate(arena.Soldier, At(5, 5, 2), BattleActionMove.Normal));
    }

    [Fact]
    public void StepsOntoAHoleCostTheDefaultAndTheFallFromItIsFree()
    {
        var arena = Flat();
        var finder = arena.Finder();
        arena.Floors(1, 4, 4, 8, 8);
        arena.Put(6, 5, 1, BattleTilePart.Floor, Hole);
        // A step onto a hole: the default cost of 4 where there is no floor to say; the unit ends in the hole.
        var onto = finder.GetTuCost(At(5, 5, 1), 2, arena.Soldier, BattleActionMove.Normal);
        Assert.Equal((4, 2), TimeAndEnergy(onto));
        Assert.Equal(At(6, 5, 1), onto.Position);
        // The fall from the hole is a step down that costs nothing (the penalty of a fire aside) and ends below.
        var fall = finder.GetTuCost(At(6, 5, 1), BattleUnit.DirectionDown, arena.Soldier, BattleActionMove.Normal);
        Assert.Equal(((short)0, (short)0, At(6, 5)), (fall.Cost.Time, fall.Cost.Energy, fall.Position));
        Assert.True(fall.IsValid);
        // A walking unit cannot step off an edge into the air from a tile with no floor.
        Assert.False(finder.GetTuCost(At(6, 5, 1), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        // Nor can it go down without something to take it: on the ground, down is not a step.
        Assert.False(finder.GetTuCost(At(5, 5), BattleUnit.DirectionDown, arena.Soldier, BattleActionMove.Normal).IsValid);
        // On the ground level a no-floor floor does not fall.
        arena.Put(7, 7, 0, BattleTilePart.Floor, Hole);
        Assert.Equal(At(7, 7), finder.GetTuCost(At(6, 7), 2, arena.Soldier, BattleActionMove.Normal).Position);
    }

    [Fact]
    public void ALadderIsClimbedForItsCostAndAGravLiftAndAFlyingUnitGoUpForTheDefaultEight()
    {
        var arena = Flat();
        var finder = arena.Finder();
        // A ladder on the object part at both levels, the upper tile with no floor: climbing costs the ladder's 6, with the climb percentages.
        arena.Put(12, 12, 0, BattleTilePart.ObjectPart, Ladder);
        arena.Put(12, 12, 1, BattleTilePart.ObjectPart, Ladder);
        var climb = finder.GetTuCost(At(12, 12), BattleUnit.DirectionUp, arena.Soldier, BattleActionMove.Normal);
        Assert.Equal((6, 3), TimeAndEnergy(climb));
        Assert.Equal(At(12, 12, 1), climb.Position);
        // Down the ladder from the upper tile is the same step back (its destination is the tile below, which has floor).
        var down = finder.GetTuCost(At(12, 12, 1), BattleUnit.DirectionDown, arena.Soldier, BattleActionMove.Normal);
        Assert.True(down.IsValid);
        Assert.Equal(At(12, 12), down.Position);
        // Up where there is no ladder is refused for a walking unit.
        Assert.False(finder.GetTuCost(At(14, 12), BattleUnit.DirectionUp, arena.Soldier, BattleActionMove.Normal).IsValid);

        // A grav lift floor on both levels: 8, with the grav lift percentages (no energy).
        arena.Put(16, 12, 0, BattleTilePart.Floor, LiftFloor);
        arena.Put(16, 12, 1, BattleTilePart.Floor, LiftFloor);
        var lift = finder.GetTuCost(At(16, 12), BattleUnit.DirectionUp, arena.Soldier, BattleActionMove.Normal);
        Assert.Equal((8, 0), TimeAndEnergy(lift));
        Assert.Equal(At(16, 12, 1), lift.Position);

        // A flying unit goes up where the tile above has no floor, for 8 and no energy, and pays the flying percentages sideways over a hole.
        arena.Soldier.MovementType = 1;
        var fly = finder.GetTuCost(At(20, 12), BattleUnit.DirectionUp, arena.Soldier, BattleActionMove.Normal);
        Assert.Equal(((8, 0), At(20, 12, 1)), (TimeAndEnergy(fly), fly.Position));
        arena.Soldier.MoveCosts = RuntimeArmorMoveCosts.Default with { Fly = (200, 200) };
        Assert.Equal((8, 4), TimeAndEnergy(finder.GetTuCost(At(20, 12, 1), 2, arena.Soldier, BattleActionMove.Normal)));
        // Under a roof it cannot.
        arena.Put(22, 12, 1, BattleTilePart.Floor, Floor);
        Assert.False(finder.GetTuCost(At(22, 12), BattleUnit.DirectionUp, arena.Soldier, BattleActionMove.Normal).IsValid);
        Assert.False(finder.ValidateUpDown(arena.Soldier, At(22, 12), BattleUnit.DirectionUp));
        Assert.True(finder.ValidateUpDown(arena.Soldier, At(20, 12), BattleUnit.DirectionUp));
        Assert.True(finder.ValidateUpDown(arena.Soldier, At(20, 12, 1), BattleUnit.DirectionDown));
        Assert.False(finder.ValidateUpDown(arena.Soldier, At(5, 5, 1), BattleUnit.DirectionUp));
    }

    // ---- units ----

    [Fact]
    public void AUnitInTheWayBlocksWhatTheMoverKnowsOf()
    {
        var arena = Flat();
        var finder = arena.Finder();
        var alien = arena.Alien;
        arena.Stand(alien, 6, 5, 0);
        // The player does not know of an unseen alien and may step onto it (the move then stops); it knows a visible one.
        Assert.False(alien.IsSpottedBy(BattleFaction.Player));
        Assert.True(finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        arena.Battle.SetSpotted(alien, BattleFaction.Player, true);
        Assert.False(finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        // Ignoring friends lets it through; an unconscious unit does not block.
        finder.IgnoreFriends = true;
        Assert.True(finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        finder.IgnoreFriends = false;
        alien.Status = BattleUnitStatus.Unconscious;
        Assert.True(finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        alien.Status = BattleUnitStatus.Standing;

        // The alien moving towards the soldier: blocked only once it has spotted it this turn. A unit never blocks itself.
        alien.MovementType = 0;
        arena.Stand(alien, 5, 6, 0);
        arena.Stand(alien, 4, 5, 0);
        Assert.True(finder.GetTuCost(At(4, 5), 2, alien, BattleActionMove.Normal).IsValid);
        alien.UnitsSpottedThisTurn.Add(arena.Soldier.Id);
        Assert.False(finder.GetTuCost(At(4, 5), 2, alien, BattleActionMove.Normal).IsValid);
        Assert.True(finder.GetTuCost(At(4, 5), 0, alien, BattleActionMove.Normal).IsValid);
    }

    [Fact]
    public void AFlyingUnitCannotShareATileWithAUnitPokingIntoItFromBelow()
    {
        // Units 30 voxels tall poke 4 voxels into the tile above them: more than one, so a flying unit there is blocked.
        var arena = Flat(rules: Replaced(Rules, SuitText, SuitText + "\n    standHeight: 30"));
        var finder = arena.Finder();
        arena.Floors(1, 4, 4, 12, 12);
        arena.Put(8, 6, 1, BattleTilePart.Floor, Hole);
        arena.Put(8, 6, 0, BattleTilePart.Floor, Floor);
        arena.Soldier.MovementType = 1;
        arena.Stand(arena.Soldier, 7, 6, 1);
        var other = new BattleUnit(950, BattleFaction.Player, BattleController.Human(0))
        {
            ArmorId = "SUIT",
            UnitType = "VEHICLE",
            Stats = arena.Soldier.Stats,
            Position = At(8, 6),
        };
        arena.Battle.AddUnit(other);
        Assert.False(finder.GetTuCost(At(7, 6, 1), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        // The same unit standing on the floor beside, not below, does not matter; nor does a unit it does not know of that is not visible.
        arena.Stand(other, 8, 8, 0);
        Assert.True(finder.GetTuCost(At(7, 6, 1), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
    }

    private const string SuitText = "- type: SUIT\n    movementType: 1";

    [Fact]
    public void ABigUnitMovesAsTheAverageOfItsFourTilesNeverThroughADoorAndNeverOntoOrOffAUnit()
    {
        var arena = Flat();
        var finder = arena.Finder();
        var vehicle = new BattleUnit(901, BattleFaction.Player, BattleController.Human(0))
        {
            ArmorId = "VEHICLE_ARMOR",
            ArmorSize = 2,
            UnitType = "VEHICLE",
            Position = At(10, 16),
            Stats = arena.Soldier.Stats,
        };
        arena.Battle.AddUnit(vehicle);
        // Four tiles of 4 average to 4; one of them dearer raises the average (over a total divided by four).
        Assert.Equal((4, 2), TimeAndEnergy(finder.GetTuCost(At(10, 16), 2, vehicle, BattleActionMove.Normal)));
        arena.Put(12, 17, 0, BattleTilePart.Floor, Wall10);
        Assert.Equal((5, 2), TimeAndEnergy(finder.GetTuCost(At(10, 16), 2, vehicle, BattleActionMove.Normal)));
        arena.Put(12, 17, 0, BattleTilePart.Floor, Floor);
        // A door wall on the last tile stops it.
        arena.Put(12, 17, 0, BattleTilePart.NorthWall, Door);
        Assert.False(finder.GetTuCost(At(10, 16), 2, vehicle, BattleActionMove.Normal).IsValid);
        arena.Put(12, 17, 0, BattleTilePart.NorthWall, -1);
        arena.Put(12, 17, 0, BattleTilePart.WestWall, Door);
        Assert.False(finder.GetTuCost(At(10, 16), 2, vehicle, BattleActionMove.Normal).IsValid);
        arena.Put(12, 17, 0, BattleTilePart.WestWall, -1);
        // A wall across the middle of the four tiles at the end position (the X check) stops it, though no single tile's step is blocked.
        arena.Put(12, 17, 0, BattleTilePart.NorthWall, Impassable);
        Assert.False(finder.GetTuCost(At(10, 16), 2, vehicle, BattleActionMove.Normal).IsValid);
        arena.Put(12, 17, 0, BattleTilePart.NorthWall, -1);
        Assert.True(finder.GetTuCost(At(10, 16), 2, vehicle, BattleActionMove.Normal).IsValid);
        // It cannot be sent where its footprint leaves the map; a destination at the edge is the largest corner.
        Assert.Empty(finder.Calculate(vehicle, At(29, 29), BattleActionMove.Normal));
        Assert.NotEmpty(finder.Calculate(vehicle, At(28, 28), BattleActionMove.Normal));

        // It cannot step onto a hole above a unit, however small; the same hole above nothing is fine.
        arena.Floors(1, 18, 18, 24, 22);
        vehicle.Position = null;
        arena.Battle.SetTile(vehicle, At(19, 20, 1));
        arena.Put(21, 20, 1, BattleTilePart.Floor, Hole);
        arena.Stand(arena.Alien, 21, 20, 0);
        Assert.False(finder.GetTuCost(At(19, 20, 1), 2, vehicle, BattleActionMove.Normal).IsValid);
        arena.Stand(arena.Alien, 21, 25, 0);
        Assert.True(finder.GetTuCost(At(19, 20, 1), 2, vehicle, BattleActionMove.Normal).IsValid);
        // Nor can a small unit step onto the hole above a big one.
        arena.Stand(vehicle, 21, 24, 0);
        arena.Stand(arena.Soldier, 20, 23, 1);
        arena.Put(21, 23, 1, BattleTilePart.Floor, Hole);
        Assert.False(finder.GetTuCost(At(20, 23, 1), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        Assert.Throws<InvalidOperationException>(() => finder.GetTuCost(At(5, 5), 2, new BattleUnit(960, BattleFaction.Player, BattleController.Human(0))
        {
            ArmorSize = 3,
            ArmorId = "SUIT",
            UnitType = "VEHICLE",
        }, BattleActionMove.Normal));
    }

    // ---- the path ----

    [Fact]
    public void AClearStraightRunIsTheBresenhamLineWithItsCostAndMixedDiagonalsKeepTheLinesSteps()
    {
        var arena = Flat();
        var finder = arena.Finder();
        Assert.Equal([2, 2, 2, 2], finder.Calculate(arena.Soldier, At(9, 5), BattleActionMove.Normal));
        Assert.Equal(16, finder.TotalTimeUnitCost);
        Assert.Equal(2, finder.StartDirection);
        Assert.Equal([3, 3, 3], finder.Calculate(arena.Soldier, At(8, 8), BattleActionMove.Normal));
        Assert.Equal(18, finder.TotalTimeUnitCost);
        // Two sideways for every diagonal: costs 4 and 6 alternate, which the line accepts (6 is 4 and a half).
        Assert.Equal([2, 3, 2, 3], finder.Calculate(arena.Soldier, At(9, 7), BattleActionMove.Normal));
        Assert.Equal(20, finder.TotalTimeUnitCost);
        // Northwest, west and the other compass points.
        Assert.Equal([7, 7], finder.Calculate(arena.Soldier, At(3, 3), BattleActionMove.Normal));
        Assert.Equal([0, 0], finder.Calculate(arena.Soldier, At(5, 3), BattleActionMove.Normal));
        // To where it stands: nothing to walk.
        Assert.Empty(finder.Calculate(arena.Soldier, At(5, 5), BattleActionMove.Normal));
        // The path is dequeued first step first and then ends.
        finder.Calculate(arena.Soldier, At(5, 8), BattleActionMove.Normal);
        Assert.Equal([4, 4, 4], finder.Path);
        Assert.Equal(4, finder.DequeuePath());
        Assert.Equal([4, 4], finder.Path);
        finder.AbortPath();
        Assert.Equal((-1, -1, 0), (finder.StartDirection, finder.DequeuePath(), finder.TotalTimeUnitCost));
        // The start is given: from another position.
        Assert.Equal([2, 2], finder.Calculate(arena.Soldier, At(1, 1), At(3, 1), BattleActionMove.Normal));
    }

    [Fact]
    public void ABlockedLineFallsBackToAStarWhichGoesAroundAndTheTimeUnitLimitOnlyBindsAStar()
    {
        var arena = Flat();
        var finder = arena.Finder();
        // A west wall on (7,5) stops the line; the shortest way round costs 20 either way round (6 + 4 + 4 + 6).
        arena.Put(7, 5, 0, BattleTilePart.WestWall, Impassable);
        var around = finder.Calculate(arena.Soldier, At(9, 5), BattleActionMove.Normal);
        var walk = Follow(finder, arena.Soldier, At(5, 5), around);
        Assert.Equal(At(9, 5), walk.Positions[^1]);
        Assert.Equal((4, 20), (around.Count, walk.Time));
        Assert.DoesNotContain(At(7, 5), walk.Positions);
        Assert.Equal([3, 2, 2, 1], around);
        // The limit binds A*: 19 is not enough, 20 is. A straight line is not held to it (the reference's Bresenham ignores it).
        Assert.Empty(finder.Calculate(arena.Soldier, At(9, 5), BattleActionMove.Normal, 19));
        Assert.Equal(4, finder.Calculate(arena.Soldier, At(9, 5), BattleActionMove.Normal, 20).Count);
        Assert.Equal([4, 4, 4], finder.Calculate(arena.Soldier, At(5, 8), BattleActionMove.Normal, 1));
        // An end that cannot be reached: walled in on every side; an end that is blocked itself.
        foreach (var (x, y) in new[] { (20, 20), (21, 20), (22, 20), (20, 21), (22, 21), (20, 22), (21, 22), (22, 22) })
            arena.Put(x, y, 0, BattleTilePart.ObjectPart, Impassable);
        Assert.Empty(finder.Calculate(arena.Soldier, At(21, 21), BattleActionMove.Normal));
        Assert.Empty(finder.Calculate(arena.Soldier, At(20, 20), BattleActionMove.Normal));
        Assert.Equal(0, finder.TotalTimeUnitCost);
    }

    [Fact]
    public void ABrutalUnitSkipsTheLineAndAStarFindsTheCheapestPathByItsTieOrder()
    {
        var arena = Flat();
        var alien = arena.Alien;
        alien.MovementType = 0;
        arena.Stand(alien, 5, 5, 0);
        alien.SetDirection(2);
        var brutal = arena.Finder();
        Assert.True(alien.UsesBrutalAi(arena.Rules, BattleOptions.Pathing));
        // The same journey as the line's [2, 3, 2, 3] for the soldier: A* finds a path of the same cost, which is its own.
        var aStar = brutal.Calculate(alien, At(9, 7), BattleActionMove.Normal);
        Assert.Equal(4, aStar.Count);
        Assert.Equal(20, Follow(brutal, alien, At(5, 5), aStar).Time);
        Assert.NotEqual([2, 3, 2, 3], aStar);
        Assert.Equal([2, 2, 3, 3], aStar);
        // Without brutal AI the alien takes the line.
        var gentle = arena.Finder(new BattlePathingOptions { BrutalAi = false });
        Assert.False(alien.UsesBrutalAi(arena.Rules, new BattlePathingOptions { BrutalAi = false }));
        Assert.Equal([2, 3, 2, 3], gentle.Calculate(alien, At(9, 7), BattleActionMove.Normal));
        // Brutal civilians, a player's unit under AI control, and the unit rule's own flags.
        Assert.False(arena.Soldier.UsesBrutalAi(arena.Rules, BattleOptions.Pathing));
        Assert.True(arena.Soldier.UsesBrutalAi(arena.Rules, new BattlePathingOptions { AutoCombat = true }));
        arena.Soldier.AllowAutoCombat = false;
        Assert.False(arena.Soldier.UsesBrutalAi(arena.Rules, new BattlePathingOptions { AutoCombat = true }));
        Assert.True(arena.Soldier.UsesBrutalAi(arena.Rules, new BattlePathingOptions { AutoCombat = true, AutoCombatControlPerUnit = false }));
    }

    [Fact]
    public void TheUnitRulesBrutalFlagsAndFireAvoidanceAreRead()
    {
        const string AlienRule = "\n  - type: ALIEN\n";
        Assert.Contains(AlienRule, Rules, StringComparison.Ordinal);
        var arena = Flat();
        Assert.False(arena.Rules.Battle.Equipment.Units["ALIEN"].IsBrutal);
        Assert.False(arena.Rules.Battle.Equipment.Units["ALIEN"].IsNotBrutal);
        Assert.True(arena.Alien.AvoidsFire(arena.Rules));
        Assert.True(arena.Soldier.AvoidsFire(arena.Rules));
        var brutalRules = Rules.Replace(AlienRule, AlienRule + "    isBrutal: true\n    specab: 2\n", StringComparison.Ordinal);
        var other = Flat(rules: brutalRules);
        Assert.True(other.Rules.Battle.Equipment.Units["ALIEN"].IsBrutal);
        Assert.False(other.Alien.AvoidsFire(other.Rules));
        Assert.True(other.Alien.UsesBrutalAi(other.Rules, new BattlePathingOptions { BrutalAi = false }));
        var notBrutal = Rules.Replace(AlienRule, AlienRule + "    isNotBrutal: true\n    avoidsFire: false\n", StringComparison.Ordinal);
        var third = Flat(rules: notBrutal);
        Assert.False(third.Alien.UsesBrutalAi(third.Rules, BattleOptions.Pathing));
        Assert.False(third.Alien.AvoidsFire(third.Rules));
    }

    [Fact]
    public void ASneakingHostileUnitAvoidsTilesThePlayerSeesAndTheLineGivesWayToAStar()
    {
        var arena = Flat();
        var alien = arena.Alien;
        alien.MovementType = 0;
        arena.Stand(alien, 5, 5, 0);
        var options = new BattlePathingOptions { SneakyAi = true, BrutalAi = false };
        var finder = arena.Finder(options);
        Assert.Equal([2, 2, 2, 2], finder.Calculate(alien, At(9, 5), BattleActionMove.Normal));
        arena.Map.SetVisibility(arena.Map.TileIndex(At(7, 5)), 2);
        var around = finder.Calculate(alien, At(9, 5), BattleActionMove.Normal);
        var walk = Follow(finder, alien, At(5, 5), around);
        Assert.DoesNotContain(At(7, 5), walk.Positions);
        Assert.Equal(At(9, 5), walk.Positions[^1]);
        // Not sneaking (the option off, its default): the line goes through.
        Assert.Equal([2, 2, 2, 2], arena.Finder(new BattlePathingOptions { BrutalAi = false }).Calculate(alien, At(9, 5), BattleActionMove.Normal));
    }

    // ---- reachable tiles ----

    [Fact]
    public void TheReachableTilesAreThoseWithinTheTimeUnitsAndEnergyCheapestFirstAndTheStartIsFirst()
    {
        var arena = Flat();
        var finder = arena.Finder();
        var soldier = arena.Soldier;
        soldier.TimeUnits = 8;
        // 8 time units: the start, 4 tiles at 4, 4 diagonals at 6 and 4 tiles two away at 8.
        var all = finder.FindReachableNodes(soldier, new BattleActionCost(soldier));
        Assert.Equal(13, all.Nodes.Count);
        Assert.True(all.RanOutOfTimeUnits);
        Assert.Equal((At(5, 5), (short)0), (all.Nodes[0].Position, all.Nodes[0].Cost.Time));
        Assert.Equal(all.Nodes.Select(static node => (int)node.Cost.Time).Order(), all.Nodes.Select(static node => (int)node.Cost.Time));
        Assert.Equal([0, 4, 4, 4, 4, 6, 6, 6, 6, 8, 8, 8, 8], all.Nodes.Select(static node => (int)node.Cost.Time));
        Assert.Equal(13, finder.FindReachable(soldier, new BattleActionCost(soldier), out var ranOut).Count);
        Assert.True(ranOut);
        // The cost of the action they are kept for comes off first.
        var kept = new BattleActionCost(soldier) { Time = 4 };
        Assert.Equal(5, finder.FindReachableNodes(soldier, kept).Nodes.Count);
        // Energy limits too: 2 energy pays for a straight step (2), not a diagonal (3).
        soldier.TimeUnits = 100;
        soldier.Energy = 2;
        Assert.Equal(5, finder.FindReachableNodes(soldier, new BattleActionCost(soldier)).Nodes.Count);
        // The whole map ignores the unit's limits; the full statistics are the other limit.
        soldier.TimeUnits = 8;
        var everything = finder.FindReachableNodes(soldier, new BattleActionCost(soldier), entireMap: true);
        Assert.Equal(30 * 30, everything.Nodes.Count);
        Assert.False(everything.RanOutOfTimeUnits);
        soldier.TimeUnits = 0;
        soldier.Energy = 0;
        Assert.Single(finder.FindReachableNodes(soldier, new BattleActionCost(soldier)).Nodes);
        Assert.True(finder.FindReachableNodes(soldier, new BattleActionCost(soldier), useMaxTimeUnits: true).Nodes.Count > 1);
        // Just checking if any movement is possible stops at the second tile; another start is its own.
        soldier.TimeUnits = 100;
        soldier.Energy = 100;
        Assert.Equal(2, finder.FindReachableNodes(soldier, new BattleActionCost(soldier), justCheckIfAnyMovementIsPossible: true).Nodes.Count);
        var elsewhere = finder.FindReachableNodes(soldier, new BattleActionCost(soldier), alternateStart: At(20, 20));
        Assert.Equal(At(20, 20), elsewhere.Nodes[0].Position);
        Assert.Equal(At(20, 20), elsewhere.Nodes[1].PreviousPosition);
        // The cheapest way to a node leads back to its predecessor.
        var far = all.Nodes[^1];
        Assert.NotNull(far.PreviousPosition);
    }

    private static string WithMoveCosts(string costs) => Replaced(Rules, SuitText, SuitText + "\n    moveCost: {" + costs + "}");

    [Fact]
    public void ClimbingDownFlyingDownRunningAndStrafingInTheAirUseTheirOwnPercentages()
    {
        var arena = Flat(rules: WithMoveCosts("climbDownPercent: [200, 100], flyDownPercent: [400, 100], flyRunPercent: [600, 100], flyStrafePercent: [700, 100]"));
        var finder = arena.Finder();
        arena.Put(12, 12, 0, BattleTilePart.ObjectPart, Ladder);
        arena.Put(12, 12, 1, BattleTilePart.ObjectPart, Ladder);
        // The ladder's 6 at 200%.
        Assert.Equal((12, 6), TimeAndEnergy(finder.GetTuCost(At(12, 12, 1), BattleUnit.DirectionDown, arena.Soldier, BattleActionMove.Normal)));
        Assert.Equal(6, finder.GetTuCost(At(12, 12), BattleUnit.DirectionUp, arena.Soldier, BattleActionMove.Normal).Cost.Time);
        arena.Soldier.MovementType = 1;
        // Flying down from a tile with no floor: 8 at 400%; sideways in the air the default 4 at the run and strafe percentages.
        Assert.Equal(32, finder.GetTuCost(At(20, 12, 1), BattleUnit.DirectionDown, arena.Soldier, BattleActionMove.Normal).Cost.Time);
        Assert.Equal(24, finder.GetTuCost(At(20, 12, 1), 2, arena.Soldier, BattleActionMove.Run).Cost.Time);
        Assert.Equal(28, finder.GetTuCost(At(20, 12, 1), 2, arena.Soldier, BattleActionMove.Strafe).Cost.Time);
    }

    [Fact]
    public void ALadderOnAWallIsClimbedForItsCostOrEightIfFreeAndItsWallIsNotChargedForWalking()
    {
        var arena = Flat();
        var finder = arena.Finder();
        arena.Put(14, 14, 0, BattleTilePart.NorthWall, Ladder);
        arena.Put(14, 14, 1, BattleTilePart.NorthWall, Ladder);
        Assert.Equal(6, finder.GetTuCost(At(14, 14), BattleUnit.DirectionUp, arena.Soldier, BattleActionMove.Normal).Cost.Time);
        // The ladder's wall adds nothing to a step across it (the north wall of the tile entered or left).
        Assert.Equal(4, finder.GetTuCost(At(14, 13), 4, arena.Soldier, BattleActionMove.Normal).Cost.Time);
        Assert.Equal(4, finder.GetTuCost(At(14, 14), 0, arena.Soldier, BattleActionMove.Normal).Cost.Time);
        // A ladder that costs nothing costs the default 8; on the west wall too.
        arena.Put(16, 14, 0, BattleTilePart.WestWall, LadderFree);
        arena.Put(16, 14, 1, BattleTilePart.WestWall, LadderFree);
        Assert.Equal(8, finder.GetTuCost(At(16, 14), BattleUnit.DirectionUp, arena.Soldier, BattleActionMove.Normal).Cost.Time);
        Assert.Equal(4, finder.GetTuCost(At(15, 14), 2, arena.Soldier, BattleActionMove.Normal).Cost.Time);
    }

    [Fact]
    public void AUnitOnATileWithNoFloorIsHeldUpByALadderBelowWhenItStepsOntoFloor()
    {
        var arena = Flat();
        var finder = arena.Finder();
        arena.Put(14, 12, 1, BattleTilePart.Floor, Floor);
        // From a tile with no floor to a tile with floor: it would fall unless a ladder is on the tile below.
        Assert.False(finder.GetTuCost(At(13, 12, 1), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        arena.Put(13, 12, 0, BattleTilePart.ObjectPart, Ladder);
        Assert.True(finder.GetTuCost(At(13, 12, 1), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        // The ladder on its own tile holds it too, even stepping onto no floor.
        arena.Put(13, 12, 0, BattleTilePart.ObjectPart, -1);
        arena.Put(13, 12, 1, BattleTilePart.ObjectPart, Ladder);
        Assert.True(finder.GetTuCost(At(13, 12, 1), 6, arena.Soldier, BattleActionMove.Normal).IsValid);
    }

    [Fact]
    public void ABigUnitOnStairsGoesUpWhenEveryPartHasFloorAboveItsDestinationOrNoneAboveItsStart()
    {
        var arena = Flat();
        var finder = arena.Finder();
        var vehicle = new BattleUnit(901, BattleFaction.Player, BattleController.Human(0))
        {
            ArmorId = "VEHICLE_ARMOR",
            ArmorSize = 2,
            UnitType = "VEHICLE",
            Position = At(10, 10),
            Stats = arena.Soldier.Stats,
        };
        arena.Battle.AddUnit(vehicle);
        foreach (var (x, y) in new[] { (10, 10), (11, 10), (10, 11), (11, 11) }) arena.Put(x, y, 0, BattleTilePart.ObjectPart, StairsHigh);
        // Floor above only one destination tile; the other parts have a hole above their start tiles: all four count.
        arena.Put(12, 10, 1, BattleTilePart.Floor, Floor);
        var up = finder.GetTuCost(At(10, 10), 2, vehicle, BattleActionMove.Normal);
        Assert.Equal(1, up.Position.Z);
        // A floor above a start tile that has none above its destination takes the part out of both masks.
        arena.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        var flat = finder.GetTuCost(At(10, 10), 2, vehicle, BattleActionMove.Normal);
        Assert.Equal(0, flat.Position.Z);
    }

    [Fact]
    public void AnAlwaysVisibleArmorIsKnownToThePlayerWhetherSpottedOrNot()
    {
        var rules = Replaced(Rules, "  - type: SMALL_VEHICLE_ARMOR\n    size: 1\n", "  - type: SMALL_VEHICLE_ARMOR\n    size: 1\n    alwaysVisible: true\n");
        var arena = Flat(rules: rules);
        Assert.True(arena.Rules.Battle.Equipment.Armors["SMALL_VEHICLE_ARMOR"].AlwaysVisible);
        var finder = arena.Finder();
        var alien = arena.Alien;
        arena.Stand(alien, 6, 5, 0);
        Assert.False(alien.IsSpottedBy(BattleFaction.Player));
        Assert.True(alien.IsVisibleToPlayer(arena.Rules));
        Assert.False(finder.GetTuCost(At(5, 5), 2, arena.Soldier, BattleActionMove.Normal).IsValid);
        // A player's unit is always visible to the player.
        Assert.True(arena.Soldier.IsVisibleToPlayer(arena.Rules));
        Assert.False(Flat().Alien.IsVisibleToPlayer(Flat().Rules));
    }

    [Fact]
    public void AiPerformanceOptimizationCapsTheEntireMapSearchWhenManyUnitsOfTheSideAreAlive()
    {
        var arena = Flat();
        var soldier = arena.Soldier;
        soldier.TimeUnits = 8;
        var cost = new BattleActionCost(soldier);
        var capped = new BattlePathingOptions { AiPerformanceOptimization = true };
        // Few units: the scale factor is above one and nothing is capped.
        Assert.Equal(900, arena.Finder(capped).FindReachableNodes(soldier, cost, entireMap: true).Nodes.Count);
        for (var id = 1000; id < 3000; id++)
            arena.Battle.AddUnit(new BattleUnit(id, BattleFaction.Player, BattleController.Human(0)) { ArmorId = "SUIT", UnitType = "VEHICLE", Stats = soldier.Stats });
        // 2001 units: 432000 / (1800 * 2001) leaves 215 tiles before the limits of the unit apply again.
        var limited = arena.Finder(capped).FindReachableNodes(soldier, cost, entireMap: true);
        Assert.InRange(limited.Nodes.Count, 216, 899);
        Assert.Equal(900, arena.Finder().FindReachableNodes(soldier, cost, entireMap: true).Nodes.Count);
    }
}
