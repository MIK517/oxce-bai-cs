using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Content;
using Xunit;
using static Oxce.CompatibilityTests.BattleTestTerrain;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 1 S2: terrain files load as <c>MapDataSet::loadData</c> (MCD fields, <c>setBlockValue</c>),
/// <c>MCDPatch::modifyData</c> and the size checks of <c>BattlescapeGenerator::loadMAP</c>. Malformed content is a
/// reason, never an exception (ADR 0038).
/// </summary>
public sealed class BattleTerrainLoaderTests
{
    private const string Rules = """
        MCDPatches:
          - type: PATCHED
            data:
              - {MCDIndex: 1, TUWalk: 10, stopLOS: false, armor: 40}
              - {MCDIndex: 1, TUWalk: 12, HEBlock: 30, objectType: 1, specialType: 4, LOFTS: [21, 22, 23]}
          - type: OUT_OF_RANGE
            data: [{MCDIndex: 2, armor: 1}]
          - type: REPAIRED
            data: [{MCDIndex: 0, objectType: 2}]
          - type: TOO_MANY_LOFTS
            data: [{MCDIndex: 0, LOFTS: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13]}]
        terrains:
          - name: TEST_TERRAIN
            mapDataSets: [PLAIN, PATCHED]
            mapBlocks: [{name: BLOCK, width: 10, length: 20}, {name: WIDE, width: 130, length: 10}]
          - name: SECOND_TERRAIN
            mapDataSets: [PLAIN]
        """;

    // The content build requires each data set's MCD, PCK and TAB to exist; the loader reads the test's own bytes.
    private static readonly Lazy<RuntimeContent> Content = new(() =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], Rules, files: RequiredFiles("PLAIN", "PATCHED")));

    private static BattleTerrainLoader Loader(Files files) => new(Content.Value.RuntimeRules.Battle, files);

    [Fact]
    public void AnMcdRecordLoadsAsMapDataAndItsPatchAppliesPropertyByProperty()
    {
        var lofts = Enumerable.Range(0, 12).Select(static layer => (8 + layer, (byte)(100 + layer))).ToArray();
        var record = Record([(0, 7), (7, 9), .. lofts, (20, 0x34), (21, 0x12), (30, 1), (31, 1), (33, 2), (35, 1),
            (37, 1), (39, 4), (40, 5), (41, 6), (42, 20), (43, 7), (44, 2), (45, 9), (46, 1), (48, 0xFC), (49, 3),
            (51, 5), (52, 2), (53, 3), (54, 1), (55, 50), (56, 77), (57, 8), (58, 3), (59, 11), (60, 1)]);
        var files = new Files(new() { ["TERRAIN/PATCHED.MCD"] = Mcd(Record(), record, Record((37, 2))) });
        var loader = Loader(files);
        Assert.Null(loader.TryLoadDataSet("PATCHED", out var set));
        var data = set!.Objects[1];

        // MapDataSet::loadData's field mapping; setBlockValue keeps light, high-explosive and fire blocks as read.
        Assert.Equal((7, 9, 3, true, 6, 5), (data.Sprite(0), data.Sprite(7), data.YOffset, data.IsUfoDoor,
            data.TimeUnitsFly, data.TimeUnitsSlide));
        Assert.Equal((2, true, true, -4, 2, 2, 1), (data.BigWall, data.IsDoor, data.IsBaseModule, data.TerrainLevel,
            data.FootstepSound, data.DieMcd, data.AlternateMcd));
        Assert.Equal((3, 9, 8, 1, 50, (ushort)0x1234), (data.LightSource, data.Flammable, data.Fuel, data.ExplosiveType,
            data.Explosive, data.MiniMapIndex));
        // Block_Smoke 1 blocks smoke (256); any other value does not.
        Assert.Equal([5, 0, 30, 256, 9, 7], Enumerable.Range(0, 6).Select(data.Block));
        Assert.Equal(0, set.Objects[2].Block(3));
        Assert.True(set.Objects[2].BlocksSmoke);

        // The patch: entries in order within each property, so the second TUWalk wins; stopLOS also clears the
        // vision block; LOFTS replace the leading layers only.
        Assert.Equal((12, 40, false, BattleTilePart.WestWall, 4), (data.TimeUnitsWalk, data.Armor, data.StopsLineOfSight,
            data.ObjectType, data.SpecialType));
        Assert.Equal([21, 22, 23, 103, 104, 105, 106, 107, 108, 109, 110, 111], Enumerable.Range(0, 12).Select(data.LoftId));

        // A data set loads once and is kept for later battles.
        Assert.Null(loader.TryLoadDataSet("PATCHED", out var again));
        Assert.Same(set, again);
        Assert.Equal(1, files.Reads);
    }

    [Fact]
    public void ATerrainLoadsItsDataSetsInOrderAndTrailingPartialRecordsAreIgnored()
    {
        // MapDataSet::loadData stops at the first incomplete record without complaint.
        var files = new Files(new()
        {
            ["TERRAIN/PLAIN.MCD"] = [.. Mcd(Record((42, 1)), Record((42, 2))), 1, 2, 3],
            ["TERRAIN/PATCHED.MCD"] = Mcd(Record(), Record()),
        });
        var terrain = Content.Value.RuntimeRules.Battle.Terrains["TEST_TERRAIN"];
        Assert.Null(Loader(files).TryLoadTerrain(terrain, out var sets));
        Assert.Equal(["PLAIN", "PATCHED"], sets!.Select(static set => set.Name));
        Assert.Equal([1, 2], sets![0].Objects.Select(static data => data.Armor));
        Assert.Equal(["TEST_TERRAIN", "SECOND_TERRAIN"],
            Content.Value.RuntimeRules.Battle.TerrainOrder.Where(static id => id.EndsWith("_TERRAIN", StringComparison.Ordinal)));
    }

    [Fact]
    public void MalformedTerrainDataIsAReason()
    {
        var loader = Loader(new Files(new()
        {
            ["TERRAIN/OUT_OF_RANGE.MCD"] = Mcd(Record(), Record()),
            ["TERRAIN/TOO_MANY_LOFTS.MCD"] = Mcd(Record()),
            // Object 0 dies into object 1, whose tile type is no tile part.
            ["TERRAIN/BAD_TYPE.MCD"] = Mcd(Record((44, 1)), Record((53, 4))),
            // A door (offset 35) opens into its alternative, object 0 even when the alternative is 0.
            ["TERRAIN/BAD_DOOR.MCD"] = Mcd(Record((35, 1), (53, 5))),
            // Nothing turns into the object, so its type is never read (vanilla BARN object 7 has type 10).
            ["TERRAIN/UNUSED_TYPE.MCD"] = Mcd(Record((53, 10)), Record((44, 0), (46, 0))),
            ["TERRAIN/REPAIRED.MCD"] = Mcd(Record((53, 4))),
        }));
        Assert.Equal("Terrain file TERRAIN/MISSING.MCD is missing.", loader.TryLoadDataSet("MISSING", out _));
        // MapDataSet::getObject throws for a patch index past the data set.
        Assert.Equal("MCD OUT_OF_RANGE has no object 2", loader.TryLoadDataSet("OUT_OF_RANGE", out _));
        Assert.Equal("MCD patch TOO_MANY_LOFTS object 0 has more than 12 LOFTS.", loader.TryLoadDataSet("TOO_MANY_LOFTS", out _));
        // The reference reads a replacement's type (Tile::openDoor, Tile::destroy) and would index past the tile's parts.
        Assert.Equal("MCD BAD_TYPE object 1 has an invalid tile type 4.", loader.TryLoadDataSet("BAD_TYPE", out var set));
        Assert.Null(set);
        Assert.Equal("MCD BAD_DOOR object 0 has an invalid tile type 5.", loader.TryLoadDataSet("BAD_DOOR", out _));
        Assert.Null(loader.TryLoadDataSet("UNUSED_TYPE", out var unused));
        Assert.Equal(10, (int)unused!.Objects[0].ObjectType);
        // The patch runs before the check, so its objectType can repair a record's tile type.
        Assert.Null(loader.TryLoadDataSet("REPAIRED", out var repaired));
        Assert.Equal(BattleTilePart.NorthWall, repaired!.Objects[0].ObjectType);
        var terrain = Content.Value.RuntimeRules.Battle.Terrains["SECOND_TERRAIN"];
        Assert.Equal("Terrain file TERRAIN/PLAIN.MCD is missing.", loader.TryLoadTerrain(terrain, out var sets));
        Assert.Null(sets);
    }

    [Fact]
    public void AMapBlockLoadsItsMapAndRoutesWithLoadMapsSizeChecks()
    {
        var blocks = Content.Value.RuntimeRules.Battle.Terrains["TEST_TERRAIN"].MapBlocks;
        var routes = new byte[24];
        var loader = Loader(new Files(new()
        {
            // The MAP header is length, width, height; the height comes from the file, not the rule.
            ["MAPS/BLOCK.MAP"] = Map(20, 10, 0, 0),
            ["ROUTES/BLOCK.RMP"] = routes,
            ["MAPS/WIDE.MAP"] = Map(10, 130, 0),
            ["ROUTES/WIDE.RMP"] = [],
        }));
        Assert.Null(loader.TryLoadBlock(blocks[0], out var block));
        Assert.Equal(("BLOCK", 10, 20, 2, 24), (block!.Name, block.Map.Width, block.Map.Length, block.Map.Levels, block.Routes.Length));
        // loadMAP reads the header as signed chars, so a 130-wide block never matches its rule.
        Assert.Equal("Map block is not of the size specified MAPS/WIDE.MAP is -126x10 , expected: 130x10",
            loader.TryLoadBlock(blocks[1], out _));

        var wrong = Loader(new Files(new() { ["MAPS/BLOCK.MAP"] = Map(10, 10, 0, 0, 0, 0), ["ROUTES/BLOCK.RMP"] = [] }));
        Assert.Equal("Map block is not of the size specified MAPS/BLOCK.MAP is 10x10 , expected: 10x20", wrong.TryLoadBlock(blocks[0], out _));
        var noRoutes = Loader(new Files(new() { ["MAPS/BLOCK.MAP"] = Map(20, 10, 0, 0, 0, 0) }));
        Assert.Equal("Terrain file ROUTES/BLOCK.RMP is missing.", noRoutes.TryLoadBlock(blocks[0], out _));
        var truncated = Loader(new Files(new() { ["MAPS/BLOCK.MAP"] = Map(20, 10, 0, 0, 0, 0)[..^4], ["ROUTES/BLOCK.RMP"] = [] }));
        Assert.StartsWith("Invalid MAP file: MAPS/BLOCK.MAP: ", truncated.TryLoadBlock(blocks[0], out _), StringComparison.Ordinal);
    }
}
