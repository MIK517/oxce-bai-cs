using System.Buffers.Binary;
using System.Globalization;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Generation;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;
using static Oxce.CompatibilityTests.BattleSaveRootTests;
using static Oxce.CompatibilityTests.BattleTestTerrain;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 2 S2: the battle save module reads and writes the map of <c>battleGame</c> (<c>SavedBattleGame::load</c>
/// and <c>save</c>, <c>loadMapResources</c>, <c>calculateModuleMap</c>, <c>Tile::load</c>, <c>loadBinary</c> and
/// <c>saveBinary</c>, <c>Node::load</c> and <c>save</c>, <c>unserializeInt</c> and <c>serializeInt</c>), and a restored
/// battle loads its map data sets through the terrain loader.
/// </summary>
public sealed class BattleSaveMapTests
{
    /// <summary>The root a map test needs: the required selected unit and a map size.</summary>
    private static string Root(int width = 2, int length = 2, int height = 1, string mission = "M") =>
        $"selectedUnit: -1\nmissionType: {mission}\nwidth: {width}\nlength: {length}\nheight: {height}\n";

    /// <summary>A tile key size as the reference writes it: one character, quoted and escaped.</summary>
    private static string Char(int size) => $"\"\\x{size:x2}\"";

    /// <summary>
    /// A binary tile section: the key (null leaves a size key out) and the records. The defaults are the reference's
    /// current key.
    /// </summary>
    private static string Binary(int totalTiles, byte[] buffer, int? index = 4, int totalBytes = 29, int? fire = 1, int? smoke = 1,
        int? id = 2, int? set = 2, int? flags = 1, int? player = 2, int? neutral = 2, int? hostile = 2)
    {
        var text = new System.Text.StringBuilder();
        void Size(string key, int? size)
        {
            if (size is { } value) text.Append(CultureInfo.InvariantCulture, $"{key}: {Char(value)}\n");
        }
        Size("tileIndexSize", index);
        text.Append(CultureInfo.InvariantCulture, $"tileTotalBytesPer: {totalBytes}\n");
        Size("tileFireSize", fire);
        Size("tileSmokeSize", smoke);
        Size("tileIDSize", id);
        Size("tileSetIDSize", set);
        Size("tileBoolFieldsSize", flags);
        Size("lastExploredByPlayer", player);
        Size("lastExploredByNeutral", neutral);
        Size("lastExploredByHostile", hostile);
        text.Append(CultureInfo.InvariantCulture, $"totalTiles: {totalTiles}\nbinTiles: {Convert.ToBase64String(buffer)}\n");
        return text.ToString();
    }

    /// <summary>Little-endian fields of the given sizes, as serializeInt lays them out.</summary>
    private static byte[] Fields(params (int Size, int Value)[] fields)
    {
        var bytes = new List<byte>();
        foreach (var (size, value) in fields)
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            bytes.AddRange(buffer.Take(size));
        }
        return [.. bytes];
    }

    /// <summary>One record of the reference's current key (29 bytes).</summary>
    private static byte[] TileRecord(int index, int[] ids, int[] sets, int smoke = 0, int fire = 0, int flags = 0, int hostile = 0,
        int neutral = 0, int player = 0) =>
        Fields([(4, index), .. ids.Select(static value => (2, value)), .. sets.Select(static value => (2, value)), (1, smoke), (1, fire), (1, flags),
            (2, hostile), (2, neutral), (2, player)]);

    private static readonly int[] NoParts = [-1, -1, -1, -1];
    private static readonly int[] FloorOnly = [0, -1, -1, -1];
    private static readonly int[] FirstSet = [0, 0, 0, 0];

    private static BattleSnapshot Read(string yaml, IDiagnosticSink? diagnostics = null) =>
        OxceBattleSave.Read(Parse(yaml), SharedContent.Value, diagnostics);

    private static BattleState Restore(BattleSnapshot snapshot, Action<Dictionary<string, byte[]>>? files = null) =>
        BattleState.Restore(snapshot, SharedContent.Value, new BattleTerrainLoader(SharedContent.Value.RuntimeRules.Battle,
            BattleStartTests.TerrainFiles(files)));

    private static string Failure(Action action) => Assert.Throws<InvalidDataException>(action).Message;

    [Fact]
    public void AGeneratedMapWritesReloadsAndConforms()
    {
        var (_, battle) = Generated();
        var snapshot = battle.Capture();
        var map = battle.Map;
        Assert.Equal(map.DataSets.Select(static set => set.Name), snapshot.MapDataSets);
        // Every tile with a part is written, in index order; the empty ones are not (Tile::isVoid).
        var expected = Enumerable.Range(0, map.TileCount).Where(tile => !map.HasNoPartsOrSmoke(tile)).ToArray();
        Assert.NotEmpty(expected);
        Assert.True(expected.Length < map.TileCount);
        Assert.Equal(expected, snapshot.Tiles.Select(static tile => tile.Index));
        // SMALL.RMP's culled node stays as a dummy, so the IDs line up.
        Assert.Contains(snapshot.Nodes, static node => node.Dummy);
        Assert.Equal(map.Width / 10, snapshot.FlattenedMapBlockNames.Count);

        var written = OxceBattleSave.Write(snapshot);
        BattleSaveConformance.Check(written);
        var text = Emit(written);
        var reread = OxceBattleSave.Read(Parse(text), SharedContent.Value);
        Assert.Equivalent(snapshot, reread, strict: true);
        var restored = BattleState.Restore(reread, SharedContent.Value, Terrain());
        for (var tile = 0; tile < map.TileCount; tile++)
            for (var part = 0; part < BattleMap.PartCount; part++)
            {
                Assert.Equal(map.Part(tile, (BattleTilePart)part), restored.Map.Part(tile, (BattleTilePart)part));
                Assert.Equal(map.IsDiscovered(tile, (BattleTilePart)part), restored.Map.IsDiscovered(tile, (BattleTilePart)part));
            }
        // The restored parts point at the loaded objects.
        var floor = expected.First(tile => map.MapData(tile, BattleTilePart.Floor) is not null);
        Assert.Equal(map.MapData(floor, BattleTilePart.Floor)!.Armor, restored.Map.MapData(floor, BattleTilePart.Floor)!.Armor);
        Assert.Equal(map.Nodes.Select(static node => (node.Id, node.Position, node.IsDummy, string.Join(",", node.Links))),
            restored.Map.Nodes.Select(static node => (node.Id, node.Position, node.IsDummy, string.Join(",", node.Links))));
        Assert.Equivalent(snapshot, restored.Capture(), strict: true);
        Assert.Equal(text, Emit(OxceBattleSave.Write(restored.Capture(), Parse(text))));
    }

    [Fact]
    public void TheMapIsWrittenWithTheReferencesKeyInItsOrder()
    {
        var written = OxceBattleSave.Write(new BattleSnapshot(1, 1, 1, "M"));
        var text = Emit(written);
        // Tile::serializationKey: 4-byte index, 2-byte IDs and set IDs, 1-byte smoke, fire and bool fields, 2-byte explored turns.
        foreach (var (key, size) in new[] { ("tileIndexSize", 4), ("tileFireSize", 1), ("tileSmokeSize", 1), ("tileIDSize", 2), ("tileSetIDSize", 2),
            ("tileBoolFieldsSize", 1), ("lastExploredByPlayer", 2), ("lastExploredByNeutral", 2), ("lastExploredByHostile", 2) })
            Assert.Contains($"{key}: {Char(size)}", text, StringComparison.Ordinal);
        Assert.Contains("tileTotalBytesPer: 29\n", text, StringComparison.Ordinal);
        Assert.Contains("totalTiles: 0\n", text, StringComparison.Ordinal);
        // save: the block grids before globalshade, the map keys after undoUnit and before tuReserved; no module map
        // outside a base defense.
        var keys = written.Entries.Select(static entry => entry.ScalarKey!).ToList();
        Assert.True(keys.IndexOf("reinforcementsMemory") + 1 == keys.IndexOf("reinforcementsBlocks") &&
            keys.IndexOf("flattenedMapBlockNames") + 1 == keys.IndexOf("globalshade"));
        Assert.Equal(["undoUnit", "mapdatasets", "tileIndexSize", "tileTotalBytesPer", "tileFireSize", "tileSmokeSize", "tileIDSize",
            "tileSetIDSize", "tileBoolFieldsSize", "lastExploredByPlayer", "lastExploredByNeutral", "lastExploredByHostile", "totalTiles",
            "binTiles", "nodes", "tuReserved"], keys.Skip(keys.IndexOf("undoUnit")).Take(16));
        Assert.DoesNotContain("moduleMap", keys);
        BattleSaveConformance.Check(written);
        // An empty buffer reads back as no tiles.
        Assert.Empty(OxceBattleSave.Read(Parse(text), SharedContent.Value).Tiles);
    }

    [Fact]
    public void BinaryTilesReadEveryFieldOfTheKey()
    {
        var buffer = TileRecord(3, [0, 1, -1, 2], [1, 1, 0, 1], smoke: 2, fire: 3, flags: 0x1F, hostile: 4, neutral: 5, player: 6);
        var snapshot = Read(Root() + "mapdatasets: [BLANKS, TESTSET]\n" + Binary(1, buffer));
        var tile = Assert.Single(snapshot.Tiles);
        Assert.Equal(new BattleTileSnapshot(3)
        {
            FloorId = 0,
            FloorSet = 1,
            WestWallId = 1,
            WestWallSet = 1,
            NorthWallId = -1,
            NorthWallSet = 0,
            ObjectId = 2,
            ObjectSet = 1,
            Smoke = 2,
            Fire = 3,
            WestWallDiscovered = true,
            NorthWallDiscovered = true,
            FloorDiscovered = true,
            WestDoorOpen = true,
            NorthDoorOpen = true,
            LastExploredByHostile = 4,
            LastExploredByNeutral = 5,
            LastExploredByPlayer = 6,
        }, tile);
        var map = Restore(snapshot).Map;
        // loadMapResources: a part with an ID of -1 is empty whatever its set.
        Assert.Equal((-1, -1), map.Part(3, BattleTilePart.NorthWall));
        Assert.Equal((2, 1), map.Part(3, BattleTilePart.ObjectPart));
        Assert.Equal((2, 3), (map.Smoke(3), map.Fire(3)));
        Assert.Equal((4, 5, 6), (map.LastExplored(3, BattleFaction.Hostile), map.LastExplored(3, BattleFaction.Neutral),
            map.LastExplored(3, BattleFaction.Player)));
        // Each discovered flag is set alone (no setDiscovered spreading from the floor to the walls).
        var alone = Restore(Read(Root() + "mapdatasets: [TESTSET]\n" + Binary(1, TileRecord(0, [0, -1, -1, -1], [0, 0, 0, 0], flags: 4)))).Map;
        Assert.Equal((true, false, false), (alone.IsDiscovered(0, BattleTilePart.Floor), alone.IsDiscovered(0, BattleTilePart.WestWall),
            alone.IsDiscovered(0, BattleTilePart.NorthWall)));
    }

    [Fact]
    public void ARepeatedBinaryTileTakesTheLaterRecord()
    {
        var buffer = (byte[])[.. TileRecord(1, [0, -1, -1, -1], [0, 0, 0, 0], fire: 4), .. TileRecord(1, [1, -1, -1, -1], [0, 0, 0, 0])];
        var tile = Assert.Single(Read(Root() + "mapdatasets: [TESTSET]\n" + Binary(2, buffer)).Tiles);
        Assert.Equal((1, 0, 0), (tile.FloorId, tile.FloorSet, tile.Fire));
    }

    [Fact]
    public void AMissingBoolFieldsSizeIsOneByte()
    {
        // readVal<char>(1): older saves stored the flags in an unmentioned byte.
        var tile = Assert.Single(Read(Root() + Binary(1, TileRecord(2, NoParts, NoParts, smoke: 1, flags: 4), flags: null)).Tiles);
        Assert.Equal((2, true, 1), (tile.Index, tile.FloorDiscovered, tile.Smoke));
    }

    [Fact]
    public void OtherFieldSizesInTheKeyAreRead()
    {
        // A 2-byte index, 4-byte IDs, 1-byte set IDs, 2-byte smoke and fire, no explored turns, and two obsolete bytes
        // after the fields, which the record's total skips.
        var record = Fields((2, 3), (4, 70000), (4, -1), (4, -1), (4, -1), (1, 1), (1, 255), (1, 255), (1, 255), (2, 300), (2, 7), (1, 2),
            (2, 0x7777));
        var tile = Assert.Single(Read(Root() + Binary(1, record, index: 2, totalBytes: record.Length, id: 4, set: 1, smoke: 2, fire: 2,
            player: null, neutral: null, hostile: null)).Tiles);
        // Uint8 sets keep 255 (not -1); _smoke is a Uint8, so 300 wraps to 44.
        Assert.Equal((3, 70000, 1, -1, 255, 44, 7, true, 0), (tile.Index, tile.FloorId, tile.FloorSet, tile.WestWallId, tile.WestWallSet,
            tile.Smoke, tile.Fire, tile.NorthWallDiscovered, tile.LastExploredByPlayer));
        // A 4-byte explored turn and a 1-byte one, the other left out (size 0).
        var explored = Fields([(4, 1), .. NoParts.Select(static value => (2, value)), .. NoParts.Select(static value => (2, value)), (1, 1),
            (1, 0), (1, 0), (4, 100000), (1, 9)]);
        var turns = Assert.Single(Read(Root() + Binary(1, explored, totalBytes: explored.Length, hostile: 4, neutral: null, player: 1)).Tiles);
        Assert.Equal((100000, 0, 9), (turns.LastExploredByHostile, turns.LastExploredByNeutral, turns.LastExploredByPlayer));
    }

    [Fact]
    public void AnUnsupportedFieldSizeReadsZeroAndSkipsItsBytes()
    {
        // unserializeInt's release path: a size other than 1, 2 or 4 logs a warning, gives 0 and skips the size's bytes.
        var record = Fields([(4, 1), .. NoParts.Select(static value => (2, value)), .. NoParts.Select(static value => (2, value)), (1, 6),
            (3, 0x0F0F0F), (1, 4), (2, 0), (2, 0), (2, 0)]);
        var diagnostics = new DiagnosticCollector(10);
        var tile = Assert.Single(Read(Root() + Binary(1, record, totalBytes: record.Length, fire: 3), diagnostics).Tiles);
        Assert.Equal((6, 0, true), (tile.Smoke, tile.Fire, tile.FloorDiscovered));
        Assert.Contains("'tileFireSize'", Assert.Single(diagnostics.Snapshot(), static item => item.Code == OxceBattleSave.SkippedCode).Message,
            StringComparison.Ordinal);
        // A missing index size is 0 (the key starts zeroed): every record is tile 0, read from the record's first byte.
        diagnostics = new DiagnosticCollector(10);
        var noIndex = Fields([.. FloorOnly.Select(static value => (2, value)), .. FirstSet.Select(static value => (2, value)),
            (1, 0), (1, 0), (1, 0), (2, 0), (2, 0), (2, 0)]);
        var first = Assert.Single(Read(Root() + "mapdatasets: [TESTSET]\n" + Binary(1, noIndex, index: null, totalBytes: noIndex.Length), diagnostics).Tiles);
        Assert.Equal((0, 0, 0), (first.Index, first.FloorId, first.FloorSet));
        Assert.Single(diagnostics.Snapshot(), static item => item.Message.Contains("'tileIndexSize'", StringComparison.Ordinal));
    }

    [Theory]
    // The buffer holds fewer records than totalTiles, or a record ends past it (the reference reads past its buffer).
    [InlineData("short", "'binTiles'")]
    [InlineData("truncated", "'binTiles'")]
    // assert(index >= 0 && index < size), and a negative 4-byte index.
    [InlineData("outside", "'binTiles'")]
    [InlineData("negative", "'binTiles'")]
    // A record shorter than its index (the reference's pointer wraps).
    [InlineData("record", "'binTiles'")]
    [InlineData("noTotal", "'totalTiles'")]
    [InlineData("noData", "'binTiles'")]
    [InlineData("notBase64", "'binTiles'")]
    [InlineData("wideSize", "'tileIDSize'")]
    [InlineData("listSize", "'tileSmokeSize'")]
    [InlineData("overflow", "'totalTiles'")]
    public void MalformedBinaryTilesFail(string kind, string key)
    {
        var record = TileRecord(1, NoParts, NoParts);
        var yaml = kind switch
        {
            "short" => Binary(2, record),
            "truncated" => Binary(1, record[..20]),
            "outside" => Binary(1, TileRecord(4, NoParts, NoParts)),
            "negative" => Binary(1, TileRecord(-1, NoParts, NoParts)),
            "record" => Binary(1, record, totalBytes: 2),
            "noTotal" => Binary(1, record).Replace("totalTiles: 1\n", "", StringComparison.Ordinal),
            "noData" => Binary(1, record)[..Binary(1, record).IndexOf("binTiles", StringComparison.Ordinal)],
            "notBase64" => Binary(1, record).Replace("binTiles: ", "binTiles: A-", StringComparison.Ordinal),
            "wideSize" => Binary(1, record).Replace($"tileIDSize: {Char(2)}", "tileIDSize: \"22\"", StringComparison.Ordinal),
            "listSize" => Binary(1, record).Replace($"tileSmokeSize: {Char(1)}", "tileSmokeSize: [1]", StringComparison.Ordinal),
            _ => Binary(1, record).Replace("totalTiles: 1\n", "totalTiles: 18446744073709551615\n", StringComparison.Ordinal),
        };
        Assert.Contains(key, Failure(() => Read(Root() + yaml)), StringComparison.Ordinal);
    }

    [Fact]
    public void OldTextTilesAreReadOverEachOtherAndRewrittenAsBinary()
    {
        const string tiles = """
            mapdatasets: [BLANKS, TESTSET]
            tiles:
              - {position: [1, 0, 0], mapDataID: [0, -1, 1, 2], mapDataSetID: [1, -1, 0, 1], fire: 3, smoke: 2, discovered: [false, false, true], openDoorWest: false}
              - {position: [0, 1, 0], mapDataID: [1], mapDataSetID: [1], discovered: [true, true, false], openDoorNorth: true}
              - {position: [1, 0, 0], fire: 5}
            """;
        var source = Parse(Root() + tiles + "\n");
        var snapshot = OxceBattleSave.Read(source, SharedContent.Value);
        Assert.Equal(2, snapshot.Tiles.Count);
        // Tile::load: the third discovered value is the floor's (the second went there first); openDoorWest sets the frame
        // whatever its value; a repeated position reads its keys over the tile.
        Assert.Equal(new BattleTileSnapshot(1)
        {
            FloorId = 0,
            FloorSet = 1,
            WestWallId = -1,
            WestWallSet = -1,
            NorthWallId = 1,
            NorthWallSet = 0,
            ObjectId = 2,
            ObjectSet = 1,
            Fire = 5,
            Smoke = 2,
            FloorDiscovered = true,
            WestDoorOpen = true,
        }, snapshot.Tiles[0]);
        // A short mapDataID leaves the other parts empty; the first discovered value goes nowhere.
        Assert.Equal(new BattleTileSnapshot(2) { FloorId = 1, FloorSet = 1, NorthDoorOpen = true }, snapshot.Tiles[1]);
        var battle = Restore(snapshot);
        Assert.Equal((1, 1), battle.Map.Part(2, BattleTilePart.Floor));
        // save writes binary tiles and drops the text ones.
        var written = OxceBattleSave.Write(battle.Capture(), source);
        Assert.False(written.TryGet("tiles", out _));
        BattleSaveConformance.Check(written);
        var reread = OxceBattleSave.Read(Parse(Emit(written)), SharedContent.Value);
        // The wall frames are kept only on UFO doors (TESTSET has none), and only the floor's discovered flag was set.
        Assert.Equal([snapshot.Tiles[0] with { WestDoorOpen = false }, snapshot.Tiles[1] with { NorthDoorOpen = false }], reread.Tiles);
    }

    [Theory]
    [InlineData("- {mapDataID: [0]}", "'position'")]
    [InlineData("- 5", "'position'")]
    [InlineData("- {position: [2, 0, 0]}", "outside")]
    [InlineData("- {position: [0, -1, 0]}", "outside")]
    [InlineData("- {position: [0, 0]}", "'position'")]
    [InlineData("- {position: [0, 0, x]}", "'position'")]
    [InlineData("- {position: [0, 0, 0], discovered: [true, true]}", "'discovered'")]
    [InlineData("- {position: [0, 0, 0], discovered: [true, maybe, true]}", "'discovered'")]
    [InlineData("- {position: [0, 0, 0], fire: -1}", "'fire'")]
    [InlineData("- {position: [0, 0, 0], smoke: thick}", "'smoke'")]
    [InlineData("- {position: [0, 0, 0], mapDataID: [x]}", "'mapDataID'")]
    [InlineData("- {position: [0, 0, 0], mapDataSetID: [0, y]}", "'mapDataSetID'")]
    public void MalformedTextTilesFail(string tile, string message) =>
        Assert.Contains(message, Failure(() => Read(Root() + "tiles:\n  " + tile + "\n")), StringComparison.Ordinal);

    [Fact]
    public void AnOpenUfoDoorCostsNoTimeUnitsAndIsTheOnlyFrameWritten()
    {
        // DOORS: a UFO door (MCD byte 30) and a plain wall, both costing 4 to walk through (byte 39).
        void Files(Dictionary<string, byte[]> files) => files["TERRAIN/DOORS.MCD"] = Mcd(Record((30, 1), (39, 4)), Record((39, 4)));
        var snapshot = new BattleSnapshot(2, 1, 1, "M")
        {
            MapDataSets = ["DOORS"],
            Tiles =
            [
                new BattleTileSnapshot(0) { WestWallId = 0, WestWallSet = 0, NorthWallId = 1, NorthWallSet = 0, WestDoorOpen = true, NorthDoorOpen = true },
                new BattleTileSnapshot(1) { WestWallId = 0, WestWallSet = 0 },
            ],
        };
        var battle = Restore(snapshot, Files);
        var map = battle.Map;
        Assert.Equal((0, 4, 4), (map.TimeUnitCost(0, BattleTilePart.WestWall, 0), map.TimeUnitCost(0, BattleTilePart.NorthWall, 0),
            map.TimeUnitCost(1, BattleTilePart.WestWall, 0)));
        Assert.Equal((true, false), (map.IsUfoDoorOpen(0, BattleTilePart.WestWall), map.IsUfoDoorOpen(0, BattleTilePart.NorthWall)));
        // saveBinary: isUfoDoorOpen, so the plain wall's frame is not written.
        var captured = battle.Capture().Tiles[0];
        Assert.Equal((true, false), (captured.WestDoorOpen, captured.NorthDoorOpen));
    }

    [Theory]
    [InlineData(2, 0, "'mapdatasets'")]
    [InlineData(-2, 0, "'mapdatasets'")]
    [InlineData(0, 9, "has no object 9")]
    [InlineData(0, -2, "has no object -2")]
    public void ATilePastItsDataSetsIsACorruptedSave(int set, int id, string message)
    {
        var snapshot = new BattleSnapshot(1, 1, 1, "M") { MapDataSets = ["TESTSET"], Tiles = [new BattleTileSnapshot(0) { ObjectId = id, ObjectSet = set }] };
        Assert.Contains(message, Failure(() => Restore(snapshot)), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyHalfOfAPartLeavesItEmptyAndATileOutsideTheMapFails()
    {
        var snapshot = new BattleSnapshot(1, 1, 1, "M")
        {
            MapDataSets = ["TESTSET"],
            Tiles = [new BattleTileSnapshot(0) { FloorId = -1, FloorSet = 0, ObjectId = 1, ObjectSet = -1 }],
        };
        var map = Restore(snapshot).Map;
        Assert.Equal(((-1, -1), (-1, -1)), (map.Part(0, BattleTilePart.Floor), map.Part(0, BattleTilePart.ObjectPart)));
        Assert.Contains("outside", Failure(() => Restore(snapshot with { Tiles = [new BattleTileSnapshot(1)] })), StringComparison.Ordinal);
        Assert.Contains("0-255", Failure(() => Restore(snapshot with { Tiles = [new BattleTileSnapshot(0) { Smoke = 256 }] })), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownMapDataSetFailsWhenItsFilesAreMissing()
    {
        // Mod::getMapDataSet creates the set for any name; MapDataSet::loadData then needs its MCD file.
        var message = Failure(() => Restore(new BattleSnapshot(1, 1, 1, "M") { MapDataSets = ["NOSUCH"] }));
        Assert.Contains("'mapdatasets'", message, StringComparison.Ordinal);
        Assert.Contains("TERRAIN/NOSUCH.MCD", message, StringComparison.Ordinal);
        // A set no terrain names loads from its file.
        void Files(Dictionary<string, byte[]> files) => files["TERRAIN/LOOSE.MCD"] = Mcd(Record((42, 5)));
        Assert.Equal(5, Restore(new BattleSnapshot(1, 1, 1, "M") { MapDataSets = ["LOOSE"] }, Files).Map.DataSets[0].Objects[0].Armor);
    }

    [Fact]
    public void MapDataSetsLoadWithTheirMcdPatches()
    {
        var content = Generated(BattleStartTests.Rules + "\nMCDPatches:\n  - type: TESTSET\n    data: [{MCDIndex: 0, TUWalk: 9}]\n").Campaign.Content;
        var snapshot = new BattleSnapshot(1, 1, 1, "M") { MapDataSets = ["TESTSET"], Tiles = [new BattleTileSnapshot(0) { FloorId = 0, FloorSet = 0 }] };
        var battle = BattleState.Restore(snapshot, content, new BattleTerrainLoader(content.RuntimeRules.Battle, BattleStartTests.TerrainFiles()));
        Assert.Equal(9, battle.Map.MapData(0, BattleTilePart.Floor)!.TimeUnitsWalk);
    }

    [Fact]
    public void VoidTilesAreNotWritten()
    {
        var snapshot = new BattleSnapshot(5, 1, 1, "M")
        {
            MapDataSets = ["TESTSET"],
            Tiles =
            [
                new BattleTileSnapshot(0) { FloorId = 0, FloorSet = 0 },
                new BattleTileSnapshot(1) { Fire = 4, FloorDiscovered = true, LastExploredByPlayer = 2 },
                new BattleTileSnapshot(2) { Smoke = 1 },
                new BattleTileSnapshot(3),
                new BattleTileSnapshot(4),
            ],
        };
        var battle = Restore(snapshot);
        // Tile::isVoid: no part, no smoke and no item on the ground; fire, discovered flags and explored turns do not count.
        battle.AddItemOnTile(battle.NewItem("GUN"), new BattlePosition(4, 0, 0));
        Assert.Equal([0, 2, 4], battle.Capture().Tiles.Select(static tile => tile.Index));
    }

    [Fact]
    public void NodesKeepTheirSavedFields()
    {
        const string nodes = """
            nodes:
              - {id: 0, position: [1, 0, 0], segment: 7, type: 2, rank: 3, flags: 4, reserved: 5, priority: 6, allocated: true, links: [2, -2, -1]}
              - {id: 0, position: [0, 0, 0], type: 0, rank: 0, flags: 0, reserved: 0, priority: 0, links: [], dummy: true}
              - {id: 2, position: [1, 1, 0]}
            """;
        var source = Parse(Root() + nodes + "\n");
        var snapshot = OxceBattleSave.Read(source, SharedContent.Value);
        Assert.Equal(new BattleNodeSnapshot(0, new(1, 0, 0), 2, 3, 4, 5, 6, true, [2, -2, -1], false) with { Links = snapshot.Nodes[0].Links },
            snapshot.Nodes[0]);
        Assert.Equal([2, -2, -1], snapshot.Nodes[0].Links);
        Assert.True(snapshot.Nodes[1].Dummy);
        // Node(): missing keys keep the constructor's zeros.
        Assert.Equal((2, 0, 0, false, false), (snapshot.Nodes[2].Id, snapshot.Nodes[2].Type, snapshot.Nodes[2].Links.Count, snapshot.Nodes[2].Allocated,
            snapshot.Nodes[2].Dummy));
        var battle = Restore(snapshot);
        var node = battle.Map.Nodes[0];
        Assert.Equal((0, new BattlePosition(1, 0, 0), 0, true), (node.Id, node.Position, node.Segment, node.Allocated));
        Assert.True(battle.Map.Nodes[1].IsDummy);
        // Node::save: allocated and dummy only when set; the segment is neither read nor written.
        var written = (YamlSequenceNode)OxceBattleSave.Write(battle.Capture(), source).Entries.Single(static entry => entry.ScalarKey == "nodes").Value;
        var maps = written.Items.Cast<YamlMappingNode>().ToArray();
        Assert.Equal(["id", "position", "type", "rank", "flags", "reserved", "priority", "allocated", "links"], maps[0].Entries.Select(static entry => entry.ScalarKey));
        Assert.Equal("dummy", maps[1].Entries[^1].ScalarKey);
        Assert.Equal(["id", "position", "type", "rank", "flags", "reserved", "priority", "links"], maps[2].Entries.Select(static entry => entry.ScalarKey));
    }

    [Fact]
    public void ANodeThatIsNoMappingKeepsTheConstructorsValues()
    {
        // getChildNode on a scalar gives NONE, so every tryRead of Node::load fails and Node() stays.
        Assert.Equal(new BattleNodeSnapshot(0, default, 0, 0, 0, 0, 0, false, [], false) with { Links = Read(Root() + "nodes: [5]\n").Nodes[0].Links },
            Read(Root() + "nodes: [5]\n").Nodes[0]);
        Assert.Empty(Read(Root() + "nodes: [5]\n").Nodes[0].Links);
    }

    [Fact]
    public void AMappingWhereASequenceIsExpectedGivesItsValues()
    {
        // YamlNodeReader::children and read(std::vector) iterate a mapping's children like a sequence's.
        var snapshot = Read(Root() + """
            mapdatasets: {a: BLANKS, b: TESTSET}
            reinforcementsBlocks: {x: {y: 4}}
            nodes: {first: {id: 0, position: {x: 1, y: 0, z: 0}, links: {n: -2, e: -3}}}
            tiles: {t: {position: [0, 1, 0], mapDataID: {f: 1}, mapDataSetID: {f: 1}}}
            """ + "\n");
        Assert.Equal(["BLANKS", "TESTSET"], snapshot.MapDataSets);
        Assert.Equal(4, snapshot.ReinforcementsBlocks[0][0]);
        Assert.Equal((new BattlePosition(1, 0, 0), "-2,-3"), (snapshot.Nodes[0].Position, string.Join(",", snapshot.Nodes[0].Links)));
        Assert.Equal(new BattleTileSnapshot(2) { FloorId = 1, FloorSet = 1 }, Assert.Single(snapshot.Tiles));
    }

    [Fact]
    public void TextTilesDoNotReadTheExploredTurns()
    {
        // Tile::save writes lastExploredBy*, but Tile::load never reads them.
        var tile = Assert.Single(Read(Root() + "tiles:\n  - {position: [0, 0, 0], smoke: 1, lastExploredByPlayer: 3, lastExploredByHostile: 4, " +
            "lastExploredByNeutral: 5}\n").Tiles);
        Assert.Equal((0, 0, 0), (tile.LastExploredByPlayer, tile.LastExploredByHostile, tile.LastExploredByNeutral));
    }

    [Theory]
    [InlineData("nodes:\n  - {id: 0, position: [5, 0, 0]}\n", "outside the map")]
    [InlineData("nodes:\n  - {id: 0, position: [0, 0, 0], links: [3]}\n", "links to 3")]
    [InlineData("nodes:\n  - {id: 1, position: [0, 0, 0]}\n", "at index 0")]
    public void ARestoredNodeThatBreaksTheMapIsACorruptedSave(string nodes, string message)
    {
        var failure = Failure(() => Restore(Read(Root() + nodes)));
        Assert.Contains("corrupted", failure, StringComparison.Ordinal);
        Assert.Contains(message, failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nodes:\n  - {position: [0, 0]}\n", "'position'")]
    [InlineData("nodes:\n  - {id: x}\n", "'id'")]
    [InlineData("nodes:\n  - {links: [a]}\n", "'links'")]
    [InlineData("nodes:\n  - {dummy: perhaps}\n", "'dummy'")]
    [InlineData("mapdatasets: [[A]]\n", "'mapdatasets'")]
    [InlineData("reinforcementsBlocks: [[1, x]]\n", "'reinforcementsBlocks'")]
    [InlineData("flattenedMapTerrainNames: [[[A]]]\n", "'flattenedMapTerrainNames'")]
    public void MalformedMapKeysFailNamingTheirKey(string yaml, string key) =>
        Assert.Contains(key, Failure(() => Read(Root() + yaml)), StringComparison.Ordinal);

    [Fact]
    public void BlockGridsKeepTheirSavedShape()
    {
        var source = Parse(Root(20, 10) + "reinforcementsBlocks: [[1], [2, 3]]\nflattenedMapTerrainNames: [[T1], 5]\nflattenedMapBlockNames: 7\n");
        var snapshot = OxceBattleSave.Read(source, SharedContent.Value);
        // tryRead into nested vectors: a scalar has no children, so it is an empty vector; the shape is not checked.
        Assert.Equal(["1", "2,3"], snapshot.ReinforcementsBlocks.Select(static row => string.Join(",", row)));
        Assert.Equal(["T1", ""], snapshot.FlattenedMapTerrainNames.Select(static row => string.Join(",", row)));
        Assert.Empty(snapshot.FlattenedMapBlockNames);
        var battle = Restore(snapshot);
        Assert.Equal((3, "T1"), (battle.Map.ReinforcementBlock(1, 1), battle.Map.TerrainName(0, 0)));
        var captured = battle.Capture();
        Assert.Equivalent(snapshot.ReinforcementsBlocks, captured.ReinforcementsBlocks, strict: true);
        Assert.Empty(captured.FlattenedMapBlockNames);
        BattleSaveConformance.Check(OxceBattleSave.Write(captured, source));
        // A generated map has a whole grid of the map's blocks.
        var generated = Generated().Battle.Capture();
        Assert.All(generated.ReinforcementsBlocks, row => Assert.Equal(generated.Length / 10, row.Count));
    }

    [Fact]
    public void ABaseDefenseWritesItsModuleMapAndReadsItBack()
    {
        var (campaign, request) = BaseDefenseMapTests.Requested(BaseDefenseMapTests.Layout);
        var battle = BaseDefenseMapTests.MapOf(campaign, request);
        var content = campaign.Content;
        var snapshot = battle.Capture();
        Assert.Equal(new BattleModuleSnapshot(2, 2), snapshot.ModuleMap[3][0]);
        // A module destroyed since: the pair is (in all, left).
        snapshot = snapshot with
        {
            ModuleMap = [.. snapshot.ModuleMap.Select((row, x) => (IReadOnlyList<BattleModuleSnapshot>)[.. row.Select((module, y) =>
                (x, y) == (3, 0) ? module with { Remaining = 1 } : module)])],
        };
        var written = OxceBattleSave.Write(snapshot);
        BattleSaveConformance.Check(written);
        var row = (YamlSequenceNode)((YamlSequenceNode)written.Entries.Single(static entry => entry.ScalarKey == "moduleMap").Value).Items[3];
        Assert.Equal(["2", "1"], ((YamlSequenceNode)row.Items[0]).Items.Cast<YamlScalarNode>().Select(static value => value.Value));
        var reread = OxceBattleSave.Read(Parse(Emit(written)), content);
        Assert.Equivalent(snapshot.ModuleMap, reread.ModuleMap, strict: true);
        var restored = BattleState.Restore(reread, content, new BattleTerrainLoader(content.RuntimeRules.Battle, BaseDefenseMapTests.TerrainFiles()));
        Assert.Equal(new BattleBaseModule(1, 2), restored.BaseModules[3][0]);
        Assert.Equivalent(snapshot, restored.Capture(), strict: true);

        // Without moduleMap, calculateModuleMap runs before loadMapResources: no tile part has its object yet, so even the
        // tower's base module tiles count nothing and every block is (-1, -1).
        var withoutModules = new YamlMappingNode(written.Span, [.. written.Entries.Where(static entry => entry.ScalarKey != "moduleMap")], null, null);
        var uncounted = OxceBattleSave.Read(withoutModules, content).ModuleMap;
        Assert.Equal((4, 3), (uncounted.Count, uncounted[0].Count));
        Assert.All(uncounted.SelectMany(static modules => modules), static module => Assert.Equal(new BattleModuleSnapshot(-1, -1), module));
    }

    [Fact]
    public void OnlyABaseDefenseReadsAndWritesAModuleMap()
    {
        const string modules = "moduleMap: [[[1, 1]]]\n";
        // Another mission ignores a saved module map and its rewrite drops it.
        var other = Parse(Root(10, 10) + modules);
        var snapshot = OxceBattleSave.Read(other, SharedContent.Value);
        Assert.Empty(snapshot.ModuleMap);
        Assert.False(OxceBattleSave.Write(snapshot, other).TryGet("moduleMap", out _));
        // A base defense's present module map is read as it is, even when it is no grid (a scalar is an empty vector).
        Assert.Equal(new BattleModuleSnapshot(1, 1), Read(Root(10, 10, mission: BattleSnapshot.BaseDefenseMission) + modules).ModuleMap[0][0]);
        Assert.Empty(Read(Root(10, 10, mission: BattleSnapshot.BaseDefenseMission) + "moduleMap: 0\n").ModuleMap);
        // read(std::pair) needs a sequence of exactly two.
        Assert.Contains("'moduleMap'", Failure(() => Read(Root(10, 10, mission: BattleSnapshot.BaseDefenseMission) + "moduleMap: [[[1, 1, 1]]]\n")),
            StringComparison.Ordinal);
    }
}
