using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;
using static Oxce.CompatibilityTests.BattleMovementGroundworkTests;
using static Oxce.CompatibilityTests.BattleStartTests;
using static Oxce.CompatibilityTests.BattleTestTerrain;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S3: lighting and blockage (<c>src/Battlescape/TileEngine.cpp</c>: <c>calculateLighting</c>, <c>calculateSunShading</c>,
/// <c>calculateTerrainBackground</c>, <c>calculateTerrainItems</c>, <c>calculateUnitLighting</c>, <c>addLight</c>, <c>blockage</c>,
/// <c>horizontalBlockage</c>, <c>verticalBlockage</c>, <c>togglePersonalLighting</c>; <c>Tile.cpp</c>'s light layers and <c>getShade</c>) on
/// synthetic maps. The battle is the suited-soldier one past its first turn; the test clears its 30x30x2 map and builds the tiles it needs
/// from the records below (appended to TESTSET after its three, so MAP IDs are the record indexes). The soldier stands at (25, 25, 0) with
/// personal light off unless a test says otherwise; the global shade is chosen per test.
/// </summary>
public sealed class BattleLightingTests
{
    // TESTSET's records after the three the battle generator uses.
    private const int Floor = 3; // a solid floor
    private const int Hole = 4; // a floor part with no floor
    private const int Lamp = 5; // light source 10
    private const int Bonfire = 6; // light source 20
    private const int WallLos = 7; // stops line of sight (vision block 255)
    private const int Pane = 8; // does not stop sight; high explosive block 50
    private const int UfoDoor = 9; // a UFO door that stops sight
    private const int BigWallWest = 10; // a west big wall that stops sight
    private const int BigWallNesw = 11; // a diagonal big wall that stops sight
    private const int LowObject = 12; // terrain level -8
    private const int HighBlocker = 13; // terrain level -24, impassable
    private const int ArmoredFloor = 14; // armor 20
    private const int Brazier = 15; // light source 3, in a floor

    private static void Terrain(Dictionary<string, byte[]> files) => files["TERRAIN/TESTSET.MCD"] = Mcd(Record((42, 2)), Record((42, 3)),
        Record((42, 4), (59, 2)), Record((39, 4), (40, 4), (41, 4)), Record((32, 1)), Record((58, 10)), Record((58, 20)), Record((31, 1)),
        Record((43, 50)), Record((30, 1), (31, 1)), Record((33, 4), (31, 1)), Record((33, 2), (31, 1)), Record((48, 248)),
        Record((48, 232), (39, 255)), Record((42, 20)), Record((58, 3)));

    private sealed record Arena(CampaignState Campaign, BattleState Battle, BattleUnit Soldier, RuntimeRuleCatalog Rules)
    {
        public BattleMap Map => Battle.Map;

        public void Put(int x, int y, int z, BattleTilePart part, int id) => Place(Battle, new BattlePosition(x, y, z), part, id);

        /// <summary>Recomputes everything, as generation does.</summary>
        public void Relight() => Battle.CalculateLighting(Rules, BattleLightLayer.Ambient, null, 0, true);

        public int Shade(int x, int y, int z = 0) => Battle.TileShade(new BattlePosition(x, y, z));

        public int Tile(int x, int y, int z = 0) => Map.TileIndex(new BattlePosition(x, y, z));

        public BattleUnit Alien => Battle.Units.First(static unit => unit.Faction == BattleFaction.Hostile);

        public BattleItem Drop(string type, int x, int y, int z = 0) =>
            Battle.AddItemOnTile(BattleState.ConstructItem(Rules, Battle.TakeItemId(), type), new BattlePosition(x, y, z));

        public BattleItem Hold(string type, BattleUnit unit, string slot)
        {
            var item = BattleState.ConstructItem(Rules, Battle.TakeItemId(), type);
            item.OwnerUnitId = unit.Id;
            item.Slot = slot;
            Battle.AddItem(item);
            return item;
        }
    }

    private static string Replaced(string rules, string from, string to)
    {
        var result = rules.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(rules, result);
        return result;
    }

    private static string WithShade(string rules, int shade) =>
        Replaced(rules, "  - type: UFO_SUITED\n    width: 30", $"  - type: UFO_SUITED\n    shade: {shade}\n    width: 30");

    private const string Flares = "- type: AMP\n    battleType: 9";

    private static string WithFlares(string rules) => Replaced(rules, Flares, Flares +
        "\n  - type: FLARE\n    battleType: 10\n    power: 8\n  - type: FLARE_FUSED\n    battleType: 10\n    power: 6\n    fuseType: 0");

    /// <summary>A flat arena: a floor over the whole ground level, nothing above, no light but the sun's.</summary>
    private static Arena Flat(int shade = 15, string? extraRules = null, bool flares = true, bool personalLight = false, Func<string, string>? edit = null)
    {
        var rules = WithShade(Rules, shade) + extraRules;
        if (edit is not null) rules = edit(rules);
        if (flares) rules = WithFlares(rules);
        var (campaign, battle, soldier, set) = Open(rules, Terrain);
        var map = battle.Map;
        for (var tile = 0; tile < map.TileCount; tile++)
            foreach (var part in Enum.GetValues<BattleTilePart>()) map.SetPart(tile, part, -1, -1);
        var arena = new Arena(campaign, battle, soldier, set);
        for (var x = 0; x < 30; x++)
            for (var y = 0; y < 30; y++) arena.Put(x, y, 0, BattleTilePart.Floor, Floor);
        var corner = 29;
        foreach (var unit in battle.Units.Where(unit => unit != soldier)) battle.SetTile(unit, new BattlePosition(corner--, 29, 0));
        battle.SetTile(soldier, new BattlePosition(25, 25, 0));
        battle.TogglePersonalLight = personalLight;
        arena.Relight();
        return arena;
    }

    private static int[] Shades(BattleState battle) => [.. Enumerable.Range(0, battle.Map.TileCount).Select(battle.Map.Shade)];

    // ---- blockage ----

    [Fact]
    public void AWallBlocksSightWhereItsVisionBlockIsSetAndOtherPowersByTheirOwnBlock()
    {
        var arena = Flat();
        var west = arena.Tile(6, 5);
        arena.Put(6, 5, 0, BattleTilePart.WestWall, WallLos);
        // A west wall is crossed going east into its tile and west out of it, by sight (the vision block, 255).
        Assert.Equal(255, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(5, 5), west, BattleBlockType.None));
        Assert.Equal(255, BattleBlockage.HorizontalBlockage(arena.Map, west, arena.Tile(5, 5), BattleBlockType.None));
        Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(5, 5), arena.Tile(5, 4), BattleBlockType.None));
        // That wall has no high explosive block and smoke is stopped by any wall part.
        Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(5, 5), west, BattleBlockType.Damage));
        Assert.Equal(256, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(5, 5), west, BattleBlockType.Smoke));
        // A pane stops damage by the high explosive block (50) and not sight.
        arena.Put(9, 5, 0, BattleTilePart.WestWall, Pane);
        Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(8, 5), arena.Tile(9, 5), BattleBlockType.None));
        Assert.Equal(50, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(8, 5), arena.Tile(9, 5), BattleBlockType.Damage));
        // Outside the map is a solid stop; a tile that is not a neighbour on the level above has none; the same tile blocks nothing.
        Assert.Equal(255, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(0, 0), -1, BattleBlockType.None));
        Assert.Equal(255, BattleBlockage.Blockage(arena.Map, -1, BattleTilePart.Floor, BattleBlockType.None));
        Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(5, 5), arena.Tile(5, 5, 1), BattleBlockType.None));
        Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(5, 5), arena.Tile(5, 5), BattleBlockType.None));
    }

    [Fact]
    public void AnOpenUfoDoorBlocksNothingAndAClosedOneBlocksSightAndSmoke()
    {
        var arena = Flat();
        arena.Put(6, 5, 0, BattleTilePart.NorthWall, UfoDoor);
        var tile = arena.Tile(6, 5);
        Assert.Equal(255, BattleBlockage.Blockage(arena.Map, tile, BattleTilePart.NorthWall, BattleBlockType.None));
        Assert.Equal(256, BattleBlockage.Blockage(arena.Map, tile, BattleTilePart.NorthWall, BattleBlockType.Smoke));
        Assert.Equal(255, BattleBlockage.HorizontalBlockage(arena.Map, tile, arena.Tile(6, 4), BattleBlockType.None));
        arena.Map.SetDoorFrame(tile, BattleTilePart.NorthWall, true);
        Assert.Equal(0, BattleBlockage.Blockage(arena.Map, tile, BattleTilePart.NorthWall, BattleBlockType.None));
        Assert.Equal(0, BattleBlockage.Blockage(arena.Map, tile, BattleTilePart.NorthWall, BattleBlockType.Smoke));
        Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, tile, arena.Tile(6, 4), BattleBlockType.None));
    }

    [Fact]
    public void AFloorBlocksSightUnlessItHasNoFloorAndDamageByItsArmorAndSmokeByItsArmorToo()
    {
        var arena = Flat();
        var tile = arena.Tile(4, 4);
        // A solid floor that does not stop sight still stops it for the blockage (256); a hole does not.
        Assert.Equal(256, BattleBlockage.Blockage(arena.Map, tile, BattleTilePart.Floor, BattleBlockType.None));
        arena.Put(4, 4, 0, BattleTilePart.Floor, Hole);
        Assert.Equal(0, BattleBlockage.Blockage(arena.Map, tile, BattleTilePart.Floor, BattleBlockType.None));
        // Damage adds the floor's armor when its own block is 0; smoke adds it too but any part with map data then stops smoke (256).
        arena.Put(4, 4, 0, BattleTilePart.Floor, ArmoredFloor);
        Assert.Equal(20, BattleBlockage.Blockage(arena.Map, tile, BattleTilePart.Floor, BattleBlockType.Damage));
        Assert.Equal(256, BattleBlockage.Blockage(arena.Map, tile, BattleTilePart.Floor, BattleBlockType.Smoke));
        // An empty part blocks nothing.
        Assert.Equal(0, BattleBlockage.Blockage(arena.Map, tile, BattleTilePart.WestWall, BattleBlockType.None));
    }

    [Fact]
    public void ABigWallBlocksOnlyFromTheDirectionsItFaces()
    {
        var arena = Flat();
        var tile = arena.Tile(7, 7);
        arena.Put(7, 7, 0, BattleTilePart.ObjectPart, BigWallWest);
        // The reference's table for a west big wall (4): it is skipped going north, north east, east, south east and south, and hit going
        // south west, west and north west.
        Assert.Equal([0, 0, 0, 0, 0, 255, 255, 255],
            Enumerable.Range(0, 8).Select(direction => BattleBlockage.Blockage(arena.Map, tile, BattleTilePart.ObjectPart, BattleBlockType.None, direction)));
        // Vertically a big wall that is not the full block (1) lets power through.
        Assert.Equal(0, BattleBlockage.Blockage(arena.Map, tile, BattleTilePart.ObjectPart, BattleBlockType.None, BattleBlockage.DirectionUp));
        // A diagonal big wall stops everything unless checked from its own tile (the origin form); smoke is stopped either way.
        arena.Put(8, 8, 0, BattleTilePart.ObjectPart, BigWallNesw);
        var diagonal = arena.Tile(8, 8);
        Assert.Equal(255, BattleBlockage.Blockage(arena.Map, diagonal, BattleTilePart.ObjectPart, BattleBlockType.None, 2));
        Assert.Equal(0, BattleBlockage.Blockage(arena.Map, diagonal, BattleTilePart.ObjectPart, BattleBlockType.None, 2, checkingFromOrigin: true));
        Assert.Equal(256, BattleBlockage.Blockage(arena.Map, diagonal, BattleTilePart.ObjectPart, BattleBlockType.Smoke, 2, checkingFromOrigin: true));
        // Stepping into a big wall's tile reveals it (-1).
        Assert.Equal(-1, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(6, 7), tile, BattleBlockType.None));
    }

    [Fact]
    public void ADiagonalStepIsOpenWhenEitherWayAroundItsCornerIsAndBlockedByTheSecondWayAlone()
    {
        var arena = Flat();
        var start = arena.Tile(10, 10);
        var end = arena.Tile(11, 9); // north east
        Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, start, end, BattleBlockType.None));
        // The north wall of the start tile closes the way "up, then right"; "right, then up" is open.
        arena.Put(10, 10, 0, BattleTilePart.NorthWall, WallLos);
        Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, start, end, BattleBlockType.None));
        // A wall on the north side of the east tile closes the second way too, and the result is the second way's.
        arena.Put(11, 10, 0, BattleTilePart.NorthWall, WallLos);
        Assert.Equal(255, BattleBlockage.HorizontalBlockage(arena.Map, start, end, BattleBlockType.None));
        // Damage averages the walls of the two ways instead: a pane (50) on the end tile's west side gives (0 + 50) / 2.
        arena.Put(11, 9, 0, BattleTilePart.WestWall, Pane);
        Assert.Equal(25, BattleBlockage.HorizontalBlockage(arena.Map, start, end, BattleBlockType.Damage));
        // The other three diagonals are open on an empty map.
        foreach (var (x, y, direction) in new[] { (12, 12, 3), (14, 12, 5), (16, 14, 7) })
        {
            var from = arena.Tile(x, y);
            var (dx, dy) = BattleBlockage.DirectionVector(direction);
            var to = arena.Tile(x + dx, y + dy);
            Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, from, to, BattleBlockType.None));
            Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, from, to, BattleBlockType.Damage));
        }
    }

    [Fact]
    public void AStepBetweenLevelsIsBlockedByTheFloorOfTheUpperTile()
    {
        var arena = Flat();
        var below = arena.Tile(5, 5);
        var above = arena.Tile(5, 5, 1);
        // Nothing above: sight passes both ways.
        Assert.Equal(0, BattleBlockage.VerticalBlockage(arena.Map, below, above, BattleBlockType.None));
        Assert.Equal(0, BattleBlockage.VerticalBlockage(arena.Map, above, below, BattleBlockType.None));
        arena.Put(5, 5, 1, BattleTilePart.Floor, Floor);
        Assert.Equal(256, BattleBlockage.VerticalBlockage(arena.Map, below, above, BattleBlockType.None));
        Assert.Equal(256, BattleBlockage.VerticalBlockage(arena.Map, above, below, BattleBlockType.None));
        // A diagonal step up adds the horizontal blockage and the floor of the tile beside, one level up.
        Assert.Equal(256, BattleBlockage.VerticalBlockage(arena.Map, arena.Tile(4, 5), above, BattleBlockType.None));
        arena.Put(4, 5, 1, BattleTilePart.Floor, Hole);
        Assert.Equal(256, BattleBlockage.VerticalBlockage(arena.Map, arena.Tile(4, 5), above, BattleBlockType.None));
        arena.Put(5, 5, 1, BattleTilePart.Floor, Hole);
        Assert.Equal(0, BattleBlockage.VerticalBlockage(arena.Map, arena.Tile(4, 5), above, BattleBlockType.None));
        // The object part is skipped on request: a high blocker above counts unless skipObject.
        arena.Put(5, 5, 1, BattleTilePart.ObjectPart, WallLos);
        Assert.Equal(255, BattleBlockage.VerticalBlockage(arena.Map, below, above, BattleBlockType.None));
        Assert.Equal(0, BattleBlockage.VerticalBlockage(arena.Map, below, above, BattleBlockType.None, skipObject: true));
        // Outside the map, and the same level.
        Assert.Equal(255, BattleBlockage.VerticalBlockage(arena.Map, above, -1, BattleBlockType.None));
        Assert.Equal(0, BattleBlockage.VerticalBlockage(arena.Map, below, arena.Tile(6, 5), BattleBlockType.None));
    }

    // ---- the blockage cache ----

    [Fact]
    public void TheCacheRecordsBlockedDirectionsBigWallsSmokeFireAndHeights()
    {
        var arena = Flat();
        arena.Put(12, 10, 0, BattleTilePart.WestWall, WallLos);
        arena.Put(15, 15, 0, BattleTilePart.ObjectPart, BigWallWest);
        arena.Put(18, 18, 0, BattleTilePart.ObjectPart, HighBlocker);
        arena.Put(19, 19, 0, BattleTilePart.ObjectPart, LowObject);
        arena.Put(20, 5, 1, BattleTilePart.Floor, Floor);
        arena.Map.SetSmoke(arena.Tile(3, 3), 2);
        arena.Map.SetFire(arena.Tile(4, 3), 1);
        arena.Relight();
        // The wall closes the step east out of (11, 10) and west out of (12, 10), on the level only.
        Assert.True(arena.Map.LightCache(arena.Tile(11, 10)).Blocks(2, 0));
        Assert.True(arena.Map.LightCache(arena.Tile(12, 10)).Blocks(6, 0));
        Assert.False(arena.Map.LightCache(arena.Tile(11, 10)).Blocks(0, 0));
        Assert.False(arena.Map.LightCache(arena.Tile(12, 10)).Blocks(2, 0));
        // Smoke and fire bits; a heights of an impassable object (24) and of a lower one (8), and 0 on a plain floor.
        Assert.True(arena.Map.LightCache(arena.Tile(3, 3)).HasSmoke);
        Assert.False(arena.Map.LightCache(arena.Tile(3, 3)).HasFire);
        Assert.True(arena.Map.LightCache(arena.Tile(4, 3)).HasFire);
        Assert.Equal((24, 8, 0), (arena.Map.LightCache(arena.Tile(18, 18)).Height, arena.Map.LightCache(arena.Tile(19, 19)).Height,
            arena.Map.LightCache(arena.Tile(2, 2)).Height));
        // The big wall's tile is revealed from the west: the step into it has a big wall bit.
        Assert.True(arena.Map.LightCache(arena.Tile(14, 15)).IsBigWall(2));
        Assert.False(arena.Map.LightCache(arena.Tile(14, 14)).IsBigWall(2));
        // A roof closes the way up from below and down from above; the top level never lets sight up.
        Assert.True(arena.Map.LightCache(arena.Tile(20, 5)).BlocksUp);
        Assert.True(arena.Map.LightCache(arena.Tile(20, 5, 1)).BlocksDown);
        Assert.False(arena.Map.LightCache(arena.Tile(21, 5)).BlocksUp);
        Assert.True(arena.Map.LightCache(arena.Tile(21, 5, 1)).BlocksUp);
        // The ground level has nothing below.
        Assert.True(arena.Map.LightCache(arena.Tile(21, 5)).BlocksDown);
    }

    [Fact]
    public void ARefreshAroundAChangeEqualsARefreshOfTheWholeMap()
    {
        var arena = Flat();
        arena.Put(12, 10, 0, BattleTilePart.WestWall, WallLos);
        arena.Battle.CalculateLighting(arena.Rules, BattleLightLayer.Fire, new BattlePosition(12, 10, 0), 1, terrainChanged: true);
        var partial = Enumerable.Range(0, arena.Map.TileCount).Select(arena.Map.LightCache).ToArray();
        arena.Relight();
        Assert.Equal(Enumerable.Range(0, arena.Map.TileCount).Select(arena.Map.LightCache), partial);
    }

    // ---- sun shading ----

    [Fact]
    public void TheSunLightsEveryTileBy15LessTheGlobalShade()
    {
        foreach (var shade in new[] { 0, 7, 12 })
        {
            var arena = Flat(shade);
            Assert.Equal(shade, arena.Battle.GlobalShade);
            Assert.All(Enumerable.Range(0, arena.Map.TileCount), tile => Assert.Equal(shade, arena.Map.Shade(tile)));
            Assert.Equal(shade, arena.Shade(3, 3, 1));
        }
    }

    [Fact]
    public void ARoofTakesTwoLevelsOffTheSunBelowItOnlyInBrightLight()
    {
        // Global shade 0 to 4 shades the tiles under a roof; 5 and up does not.
        foreach (var (shade, expected) in new[] { (0, 2), (4, 6), (5, 5), (9, 9) })
        {
            var arena = Flat(shade);
            arena.Put(10, 10, 1, BattleTilePart.Floor, Floor);
            arena.Relight();
            Assert.Equal(expected, arena.Shade(10, 10));
            Assert.Equal(shade, arena.Shade(9, 10));
            Assert.Equal(shade, arena.Shade(10, 10, 1));
        }
        // A roof object blocks the sun too: the object part pointing down (a block of sight), and a hole does not.
        var objects = Flat(0);
        objects.Put(11, 11, 1, BattleTilePart.ObjectPart, WallLos);
        objects.Put(12, 12, 1, BattleTilePart.Floor, Hole);
        objects.Relight();
        Assert.Equal((2, 0), (objects.Shade(11, 11), objects.Shade(12, 12)));
    }

    // ---- terrain light ----

    [Fact]
    public void ALightSourceInTheTerrainLightsACircleLosingALevelPerTile()
    {
        var arena = Flat();
        arena.Put(10, 10, 0, BattleTilePart.ObjectPart, Lamp);
        arena.Relight();
        Assert.Equal(5, arena.Shade(10, 10));
        Assert.Equal(8, arena.Shade(13, 10));
        Assert.Equal(8, arena.Shade(10, 7));
        // A diagonal tile is 4.24 tiles away, which rounds to 4.
        Assert.Equal(9, arena.Shade(13, 13));
        Assert.Equal(14, arena.Shade(19, 10));
        Assert.Equal(15, arena.Shade(20, 10));
        // The level above is 24 voxels (1.5 tiles) away, which rounds to 2.
        Assert.Equal(7, arena.Shade(10, 10, 1));
        // Walls do not stop the classic light.
        arena.Put(12, 10, 0, BattleTilePart.WestWall, WallLos);
        arena.Relight();
        Assert.Equal(9, arena.Shade(14, 10));
    }

    [Fact]
    public void ALightSourceIsCappedBelowTheMaximumStaticDistanceAndAFireLightsFully()
    {
        var arena = Flat();
        arena.Put(3, 3, 0, BattleTilePart.ObjectPart, Bonfire);
        arena.Put(25, 5, 0, BattleTilePart.Floor, Brazier);
        arena.Map.SetFire(arena.Tile(10, 20), 3);
        arena.Relight();
        Assert.Equal((0, 3), (arena.Shade(3, 3), arena.Shade(6, 3)));
        Assert.Equal(12, arena.Shade(25, 5));
        Assert.Equal((0, 2), (arena.Shade(10, 20), arena.Shade(10, 22)));
        // maxStatic 8 caps a source at 7.
        var small = Flat(extraRules: "\nlighting: {maxStatic: 8}\n");
        small.Put(10, 10, 0, BattleTilePart.ObjectPart, Lamp);
        small.Relight();
        Assert.Equal((8, 9, 15), (small.Shade(10, 10), small.Shade(11, 10), small.Shade(17, 10)));
    }

    // ---- items and units ----

    [Fact]
    public void AFlareOnTheGroundGlowsByItsPowerAndAnUnprimedFusedOneDoesNot()
    {
        var arena = Flat();
        var flare = arena.Drop("FLARE", 5, 5);
        arena.Relight();
        Assert.Equal(7, arena.Shade(5, 5));
        Assert.Equal(10, arena.Shade(8, 5));
        Assert.Equal(15, arena.Shade(13, 5));
        // A flare with a fuse type glows once its fuse runs (a timer of 0 or more, enabled).
        var fused = arena.Drop("FLARE_FUSED", 15, 15);
        arena.Relight();
        Assert.Equal(15, arena.Shade(15, 15));
        fused.SetFuseTimer(2, arena.Rules.Battle.Equipment.Items["FLARE_FUSED"]);
        arena.Battle.CalculateLighting(arena.Rules, BattleLightLayer.Items, fused.Tile);
        Assert.Equal(9, arena.Shade(15, 15));
        // Anything else lying there does not glow.
        arena.Drop("GRENADE", 20, 20);
        arena.Relight();
        Assert.Equal(15, arena.Shade(20, 20));
        Assert.NotNull(flare.Tile);
    }

    [Fact]
    public void AFlareInAHandLightsTheUnitsTileAndABurningUnitLightsFullyUnlessItIsOut()
    {
        var arena = Flat();
        var soldier = arena.Soldier;
        arena.Hold("FLARE", soldier, "STR_LEFT_HAND");
        arena.Relight();
        Assert.Equal((7, 10), (arena.Shade(25, 25), arena.Shade(25, 22)));
        // A unit on fire lights fully, and the brightest wins.
        soldier.Fire = 4;
        arena.Relight();
        Assert.Equal((0, 3), (arena.Shade(25, 25), arena.Shade(25, 28)));
        // A unit that is out lights nothing.
        soldier.Status = BattleUnitStatus.Unconscious;
        arena.Relight();
        Assert.Equal(15, arena.Shade(25, 25));
    }

    [Fact]
    public void ABurningUnitLyingOnTheGroundGlowsAtTenAndAGlowOnTheTileCapsAtTheMaximumDynamicDistance()
    {
        var arena = Flat();
        var body = arena.Drop("GRENADE", 15, 15);
        body.CorpseUnitId = arena.Alien.Id;
        arena.Alien.Fire = 1;
        arena.Relight();
        Assert.Equal(5, arena.Shade(15, 15));
        // An item's glow is capped at one below the maximum dynamic distance.
        var capped = Flat(extraRules: "\nlighting: {maxDynamic: 6}\n");
        capped.Drop("FLARE", 5, 5);
        capped.Relight();
        Assert.Equal((10, 15), (capped.Shade(5, 5), capped.Shade(10, 5)));
    }

    [Fact]
    public void ThePersonalLightOfEachSideIsTheArmorsAndTheSoldiersCanBeToggled()
    {
        var arena = Flat(personalLight: true);
        Assert.Equal(15, arena.Shade(3, 3));
        // The soldier's own light is the armor's personal light (15), lost by a level per tile.
        Assert.Equal((0, 5, 15), (arena.Shade(25, 25), arena.Shade(25, 20), arena.Shade(10, 25)));
        var revision = arena.Battle.Revision;
        arena.Battle.TogglePersonalLighting(arena.Rules);
        Assert.False(arena.Battle.TogglePersonalLight);
        Assert.True(arena.Battle.Revision > revision);
        Assert.Equal((15, 15), (arena.Shade(25, 25), arena.Shade(25, 20)));
        arena.Battle.TogglePersonalLighting(arena.Rules);
        Assert.True(arena.Battle.TogglePersonalLight);
        Assert.Equal((0, 5), (arena.Shade(25, 25), arena.Shade(25, 20)));
        // The toggle only affects X-COM's units: a hostile unit's light is its armor's personal light for the hostile side and a neutral
        // unit's the neutral one, whatever the toggle says.
        var others = Flat(personalLight: false, edit: static rules => Replaced(rules, "- type: SMALL_VEHICLE_ARMOR\n    size: 1",
            "- type: SMALL_VEHICLE_ARMOR\n    size: 1\n    personalLightHostile: 6\n    personalLightNeutral: 4"));
        var alien = others.Alien;
        var at = alien.Position!.Value;
        Assert.Equal(9, others.Battle.TileShade(at));
        alien.Faction = BattleFaction.Neutral;
        others.Relight();
        Assert.Equal(11, others.Battle.TileShade(at));
    }

    // ---- enhanced lighting ----

    [Fact]
    public void EnhancedLightingStopsAtWallsForTheLayersItIsSetFor()
    {
        // Bit 1 is the terrain (fire) layer, 2 the items, 4 the units.
        var arena = Flat(extraRules: "\nlighting: {enhanced: 1}\n");
        arena.Put(10, 10, 0, BattleTilePart.ObjectPart, Lamp);
        arena.Put(12, 10, 0, BattleTilePart.WestWall, WallLos);
        arena.Drop("FLARE", 10, 20);
        arena.Put(12, 20, 0, BattleTilePart.WestWall, WallLos);
        arena.Relight();
        // The lamp reaches the wall's near side and not past it; the open side and the other directions are as in the classic mode.
        Assert.Equal((5, 6, 15, 15), (arena.Shade(10, 10), arena.Shade(11, 10), arena.Shade(12, 10), arena.Shade(14, 10)));
        Assert.Equal((9, 10), (arena.Shade(6, 10), arena.Shade(10, 5)));
        // The flare's layer is not enhanced: the wall does not stop it.
        Assert.Equal((7, 9, 10), (arena.Shade(10, 20), arena.Shade(12, 20), arena.Shade(13, 20)));

        var items = Flat(extraRules: "\nlighting: {enhanced: 2}\n");
        items.Drop("FLARE", 10, 20);
        items.Put(12, 20, 0, BattleTilePart.WestWall, WallLos);
        items.Put(10, 10, 0, BattleTilePart.ObjectPart, Lamp);
        items.Put(12, 10, 0, BattleTilePart.WestWall, WallLos);
        items.Relight();
        Assert.Equal((8, 15, 9), (items.Shade(11, 20), items.Shade(12, 20), items.Shade(14, 10)));

        var units = Flat(extraRules: "\nlighting: {enhanced: 4}\n");
        units.Soldier.Fire = 1;
        units.Put(27, 25, 0, BattleTilePart.WestWall, WallLos);
        units.Relight();
        Assert.Equal((0, 1, 15), (units.Shade(25, 25), units.Shade(26, 25), units.Shade(28, 25)));
    }

    [Fact]
    public void EnhancedLightingPassesWindowsAndIsDimmedBySmokeAndTallObjects()
    {
        var plain = Flat(extraRules: "\nlighting: {enhanced: 1}\n");
        plain.Put(10, 10, 0, BattleTilePart.ObjectPart, Lamp);
        plain.Relight();
        Assert.Equal((7, 8, 9), (plain.Shade(12, 10), plain.Shade(13, 10), plain.Shade(14, 10)));
        // A pane does not stop sight.
        plain.Put(12, 10, 0, BattleTilePart.WestWall, Pane);
        plain.Relight();
        Assert.Equal((7, 8, 9), (plain.Shade(12, 10), plain.Shade(13, 10), plain.Shade(14, 10)));
        // Smoke on the way takes a level off what passes it (from the third tile on); the smoky tile itself is lit as before.
        var smoky = Flat(extraRules: "\nlighting: {enhanced: 1}\n");
        smoky.Put(10, 10, 0, BattleTilePart.ObjectPart, Lamp);
        smoky.Map.SetSmoke(smoky.Tile(12, 10), 2);
        smoky.Relight();
        Assert.Equal((7, 9, 10), (smoky.Shade(12, 10), smoky.Shade(13, 10), smoky.Shade(14, 10)));
        // An impassable tall object (height 24) takes two levels off what passes it.
        var tall = Flat(extraRules: "\nlighting: {enhanced: 1}\n");
        tall.Put(10, 10, 0, BattleTilePart.ObjectPart, Lamp);
        tall.Put(12, 10, 0, BattleTilePart.ObjectPart, HighBlocker);
        tall.Relight();
        Assert.Equal((7, 10, 11), (tall.Shade(12, 10), tall.Shade(13, 10), tall.Shade(14, 10)));
    }

    [Fact]
    public void EveryDiagonalIsClosedByTheSecondWayAndAveragesItsWallsForDamage()
    {
        var arena = Flat();
        // South east, south west and north west: the first way round the corner is closed, the second open (0); then both closed (255).
        foreach (var (x, y, direction, first, second) in new[]
        {
            (15, 15, 3, (15, 16, BattleTilePart.NorthWall), (16, 15, BattleTilePart.WestWall)),
            (20, 15, 5, (20, 16, BattleTilePart.NorthWall), (20, 15, BattleTilePart.WestWall)),
            (15, 20, 7, (15, 20, BattleTilePart.NorthWall), (14, 20, BattleTilePart.NorthWall)),
        })
        {
            var (dx, dy) = BattleBlockage.DirectionVector(direction);
            var from = arena.Tile(x, y);
            var to = arena.Tile(x + dx, y + dy);
            arena.Put(first.Item1, first.Item2, 0, first.Item3, WallLos);
            Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, from, to, BattleBlockType.None));
            arena.Put(second.Item1, second.Item2, 0, second.Item3, WallLos);
            Assert.Equal(255, BattleBlockage.HorizontalBlockage(arena.Map, from, to, BattleBlockType.None));
        }
        // The averaged form (damage): a pane (50) on one wall of the corner gives half.
        foreach (var (x, y, direction, pane) in new[]
        {
            (22, 22, 3, (23, 23, BattleTilePart.WestWall)),
            (22, 10, 5, (21, 11, BattleTilePart.NorthWall)),
            (22, 5, 7, (22, 4, BattleTilePart.WestWall)),
        })
        {
            var (dx, dy) = BattleBlockage.DirectionVector(direction);
            arena.Put(pane.Item1, pane.Item2, 0, pane.Item3, Pane);
            Assert.Equal(25, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(x, y), arena.Tile(x + dx, y + dy), BattleBlockType.Damage));
        }
        // A big wall at the start counts its object even when objects are skipped (sight only).
        arena.Put(26, 5, 0, BattleTilePart.ObjectPart, BigWallNesw);
        Assert.Equal(255, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(26, 5), arena.Tile(27, 5), BattleBlockType.None, skipObject: true));
        Assert.Equal(0, BattleBlockage.HorizontalBlockage(arena.Map, arena.Tile(26, 8), arena.Tile(27, 8), BattleBlockType.None, skipObject: true));
    }

    [Fact]
    public void AStepDownToATileBesideAddsBothFloorsAndTheCacheRecordsIt()
    {
        var arena = Flat();
        arena.Put(6, 6, 1, BattleTilePart.Floor, Floor);
        Assert.Equal(256, BattleBlockage.VerticalBlockage(arena.Map, arena.Tile(6, 6, 1), arena.Tile(5, 6), BattleBlockType.None));
        arena.Put(5, 6, 1, BattleTilePart.Floor, Floor);
        Assert.Equal(512, BattleBlockage.VerticalBlockage(arena.Map, arena.Tile(6, 6, 1), arena.Tile(5, 6), BattleBlockType.None));
        arena.Relight();
        Assert.True(arena.Map.LightCache(arena.Tile(6, 6, 1)).Blocks(6, -1));
        Assert.False(arena.Map.LightCache(arena.Tile(8, 8, 1)).Blocks(6, -1));
        Assert.True(arena.Map.LightCache(arena.Tile(6, 6)).Blocks(6, 1));
    }

    [Fact]
    public void EnhancedLightExemptsABigWallTargetIsStoppedByRoofsAndFiresAndIgnoresLowObjects()
    {
        var enhanced = "\nlighting: {enhanced: 1}\n";
        // A big wall target is lit although the way into it is closed; what lies past it is dark.
        var wall = Flat(extraRules: enhanced);
        wall.Put(10, 10, 0, BattleTilePart.ObjectPart, Lamp);
        wall.Put(12, 10, 0, BattleTilePart.ObjectPart, BigWallWest);
        wall.Relight();
        Assert.Equal((7, 15), (wall.Shade(12, 10), wall.Shade(13, 10)));
        // An object of height 8 on the way takes nothing off (the line runs at the middle of the tile, height 8).
        var low = Flat(extraRules: enhanced);
        low.Put(10, 10, 0, BattleTilePart.ObjectPart, Lamp);
        low.Put(12, 10, 0, BattleTilePart.ObjectPart, LowObject);
        low.Relight();
        Assert.Equal((7, 8, 9), (low.Shade(12, 10), low.Shade(13, 10), low.Shade(14, 10)));
        // A roof stops the light going up: the tile above the lamp is dark, as it is not in the classic mode.
        var roof = Flat(extraRules: enhanced);
        roof.Put(10, 10, 0, BattleTilePart.ObjectPart, Lamp);
        roof.Put(10, 10, 1, BattleTilePart.Floor, Floor);
        roof.Relight();
        Assert.Equal((5, 15), (roof.Shade(10, 10), roof.Shade(10, 10, 1)));
        // With a long maxStatic a source of 20 reaches a fire two tiles on with 14 or less, which the fire stops; the fire lights 8 at x 9.
        var fire = Flat(extraRules: "\nlighting: {maxStatic: 24, enhanced: 1}\n");
        fire.Put(0, 10, 0, BattleTilePart.ObjectPart, Bonfire);
        fire.Map.SetFire(fire.Tile(2, 10), 1);
        fire.Relight();
        Assert.Equal(7, fire.Shade(9, 10));
    }

    [Fact]
    public void AFlareWithADamageBonusIsRefusedAtTheFirstTurn()
    {
        var rules = Replaced(WithFlares(Rules), "power: 8", "power: 8" + "\n    damageBonus:\n      flatHundred: 0.5");
        var (campaign, _) = Requested(rules, "UFO_SUITED");
        campaign.BattleTerrainFiles = TerrainFiles(Terrain);
        Started(campaign);
        campaign.Execute(new BeginBriefing("T", "C"));
        campaign.Execute(new ConfirmBriefing());
        var battle = Battle(campaign);
        battle.AddItemOnTile(BattleState.ConstructItem(campaign.Content.RuntimeRules, battle.TakeItemId(), "FLARE"), new BattlePosition(0, 0, 0));
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new StartFirstTurn()).Events));
        Assert.Equal(BattleFlowReasons.FlareBonusUnsupported.Text, blocked.Reason);
    }

    // ---- partial recomputation ----

    [Fact]
    public void RecomputingAroundAnEventGivesTheLightARecomputeOfTheWholeMapWould()
    {
        // Short light distances make the recomputed area smaller than the map.
        var arena = Flat(extraRules: "\nlighting: {maxStatic: 6, maxDynamic: 6}\n");
        arena.Put(10, 10, 0, BattleTilePart.ObjectPart, Lamp);
        arena.Relight();
        // A flare lands, a lamp is built and the soldier catches fire: each is recomputed around its tile only.
        arena.Drop("FLARE", 20, 20);
        arena.Battle.CalculateLighting(arena.Rules, BattleLightLayer.Items, new BattlePosition(20, 20, 0));
        arena.Put(5, 20, 0, BattleTilePart.ObjectPart, Lamp);
        arena.Battle.CalculateLighting(arena.Rules, BattleLightLayer.Fire, new BattlePosition(5, 20, 0), 1, terrainChanged: true);
        arena.Soldier.Fire = 2;
        arena.Battle.CalculateLighting(arena.Rules, BattleLightLayer.Units, arena.Soldier.Position);
        var partial = Shades(arena.Battle);
        // Both are capped at 5, one below the distance.
        Assert.Equal((10, 10, 10, 10), (arena.Shade(20, 20), arena.Shade(25, 25), arena.Shade(5, 20), arena.Shade(10, 10)));
        arena.Relight();
        Assert.Equal(Shades(arena.Battle), partial);
        // A recompute from the units layer leaves the other layers alone.
        arena.Battle.CalculateLighting(arena.Rules, BattleLightLayer.Units);
        Assert.Equal(partial, Shades(arena.Battle));
    }

    // ---- when the battle lights itself ----

    [Fact]
    public void AGeneratedBattleIsLitAndARestoredOneIsLitAsIt()
    {
        var rules = WithShade(Rules, 12);
        var (campaign, battle, soldier, _) = Open(rules, Terrain);
        // Generation lit the map with the sun (15 - 12 = 3, shade 12) and the soldier's personal light.
        Assert.True(battle.TogglePersonalLight);
        Assert.Equal(0, battle.TileShade(soldier.Position!.Value));
        Assert.All(Shades(battle), shade => Assert.InRange(shade, 0, 12));
        Assert.Contains(12, Shades(battle));
        var restored = CampaignState.Restore(campaign.Capture(), campaign.Content, new SplitMix64RandomSource(3), TerrainFiles(Terrain));
        var again = Battle(restored);
        Assert.NotSame(battle, again);
        Assert.Equal(Shades(battle), Shades(again));
        for (var tile = 0; tile < battle.Map.TileCount; tile++)
            Assert.Equal(battle.Map.LightCache(tile), again.Map.LightCache(tile));
    }

    [Fact]
    public void TheFirstTurnLightsTheItemsOnTheGroundWhenTheInventoryCloses()
    {
        var rules = WithFlares(WithShade(Rules, 12));
        var (campaign, _) = Requested(rules, "UFO_SUITED");
        campaign.BattleTerrainFiles = TerrainFiles(Terrain);
        Started(campaign);
        Assert.IsType<BriefingOpened>(Assert.Single(campaign.Execute(new BeginBriefing("T", "C")).Events));
        Assert.IsType<BriefingConfirmed>(Assert.Single(campaign.Execute(new ConfirmBriefing()).Events));
        var battle = Battle(campaign);
        var darkest = Enumerable.Range(0, battle.Map.TileCount).First(tile => battle.Map.Shade(tile) == 12 && battle.Map.PositionOf(tile).Z == 0);
        var position = battle.Map.PositionOf(darkest);
        battle.AddItemOnTile(BattleState.ConstructItem(campaign.Content.RuntimeRules, battle.TakeItemId(), "FLARE"), position);
        Assert.Equal(12, battle.TileShade(position));
        Assert.IsType<BattleFirstTurnStarted>(Assert.Single(campaign.Execute(new StartFirstTurn()).Events));
        Assert.Equal(7, battle.TileShade(position));
    }
}
