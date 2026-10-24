using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Generation;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;
using static Oxce.CompatibilityTests.BattleTestTerrain;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 1 S6: a base defense map is laid out from the base's facilities before the map script runs
/// (<c>BattlescapeGenerator::generateMap</c> and <c>generateBaseMap</c>, <c>SavedBattleGame::calculateModuleMap</c>), with
/// <c>baseDefenseMapFromLocation</c> drawing it from a stream seeded by the base's position.
/// </summary>
public sealed class BaseDefenseMapTests
{
    // BLANKS has two objects (MAP IDs 0 and 1); XSET's follow as MAP IDs 2 (a floor), 3 (a base module) and 4.
    private const string Rules = """
        facilities:
          - type: XB_STORES
            mapName: XB_03
            storage: 50
          - type: XB_HANGAR
            mapName: XB_08
            sizeX: 2
            sizeY: 2
          - type: XB_TOWER
            mapName: XB_00
            verticalLevels:
              - {type: ground, blocks: [8]}
              - {type: ceiling, blocks: [9]}
          - type: XB_SHELF
            mapName: XB_00
            storage: 10
            storageTiles: [[2, 3, 0], [1, 1, 5], [4, 4, 0]]
          - type: XB_ROOM
            mapName: XB_00
          - type: XB_MISSING
            mapName: XB_40
          - type: XB_SHORT
            mapName: X
          - type: XB_NO_SHELF
            mapName: XB_00
            storage: 10
            storageTiles: [[1, 1, 5]]
          - type: XB_EMPTY_LEVELS
            mapName: XB_00
            verticalLevels:
              - {type: ground, blocks: [99]}
          - type: XB_CRAFT_LEVELS
            mapName: XB_00
            verticalLevels:
              - {type: craft}
          - type: XB_BAD_LEVELS
            mapName: XB_00
            verticalLevels:
              - {type: ground, size: wide}
          - type: XB_NONE_SHELF
            mapName: XB_00
            storage: 10
            storageTiles: [[-1, -1, -1]]
          - type: XB_LOW_STORES
            mapName: XB_04
            storage: 10
          - type: XB_HOLLOW
            mapName: XB_05
            storage: 10
          - type: XB_ALT_TOWER
            mapName: XB_00
            verticalLevels:
              - {type: ground, blocks: [0], terrain: baseTerrain}
        terrains:
          - name: XBASE_T
            mapDataSets: [BLANKS, XSET]
            mapBlocks:
              - {name: XB_00, width: 10, length: 10}
              - {name: XB_03, width: 10, length: 10}
              - {name: XB_04, width: 10, length: 10}
              - {name: XB_05, width: 10, length: 10}
              - {name: XB_08, width: 10, length: 10}
              - {name: XB_09, width: 10, length: 10}
              - {name: XB_10, width: 10, length: 10}
              - {name: XB_11, width: 10, length: 10}
              - {name: TOWER_G, width: 10, length: 10}
              - {name: TOWER_C, width: 10, length: 10, height: 1}
              - {name: DIRT_A, width: 10, length: 10, groups: [5]}
              - {name: DIRT_B, width: 10, length: 10, groups: [5]}
            script: XBASE_S
          - name: XBASE_ALT
            mapDataSets: [BLANKS, XSET]
            mapBlocks:
              - {name: ALT_G, width: 10, length: 10}
            script: XBASE_S
        mapScripts:
          - type: XBASE_S
            commands:
              - type: digTunnel
                direction: both
                tunnelData:
                  level: 0
                  MCDReplacements:
                    - {type: floor, set: 1, entry: 2}
              - {type: fillArea, groups: [5]}
        alienDeployments:
          - type: STR_BASE_DEFENSE
            width: 40
            length: 30
            height: 2
            terrains: [XBASE_T]
            data: [{alienRank: 0, lowQty: 1, highQty: 1, itemSets: [[]]}]

        """;

    private const string FromLocation = """
        baseDefenseMapFromLocation: 1
        globe:
          textures:
            - {id: 10, baseTerrain: [{name: XBASE_ALT, weight: 1}]}

        """;

    /// <summary>A MAP with the given (top level first) tile parts set: (level from the top, tile, part offset, MAP ID).</summary>
    private static byte[] WithParts(byte[] map, params (int Level, int Tile, int Part, int Id)[] parts)
    {
        foreach (var (level, tile, part, id) in parts) map[3 + (level * 100 + tile) * 4 + part] = (byte)id;
        return map;
    }

    private static readonly string[] Blocks =
        ["XB_00", "XB_03", "XB_04", "XB_05", "XB_08", "XB_09", "XB_10", "XB_11", "TOWER_G", "TOWER_C", "DIRT_A", "DIRT_B", "ALT_G"];

    internal static Files TerrainFiles(Action<Dictionary<string, byte[]>>? adjust = null)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["TERRAIN/BLANKS.MCD"] = Mcd(Record(), Record((42, 1))),
            // XSET's second object is a base module (MCD byte 60).
            ["TERRAIN/XSET.MCD"] = Mcd(Record((42, 2)), Record((42, 3), (60, 1)), Record((42, 4))),
        };
        foreach (var block in Blocks)
        {
            files[$"MAPS/{block}.MAP"] = Map(10, 10, 2);
            files[$"ROUTES/{block}.RMP"] = [];
        }
        // The stores have a floor on both levels; on the upper one an object on (0, 0) and a west wall on (3, 0).
        files["MAPS/XB_03.MAP"] = WithParts(Map(10, 10, 2, 2), (0, 0, 3, 4), (0, 3, 1, 4));
        // Two base module tiles in the tower's ground block; its ceiling has XSET's second object as the floor.
        files["MAPS/TOWER_G.MAP"] = WithParts(Map(10, 10, 2), (0, 11, 3, 3), (0, 12, 3, 3));
        files["MAPS/TOWER_C.MAP"] = Map(10, 10, 3);
        files["MAPS/ALT_G.MAP"] = Map(10, 10, 4);
        // Low stores: one level, with a north wall on (2, 1); hollow stores: no floor on any level.
        files["MAPS/XB_04.MAP"] = WithParts(Map(10, 10, 2), (0, 12, 2, 4));
        files["MAPS/XB_05.MAP"] = Map(10, 10, 0, 0);
        adjust?.Invoke(files);
        return new(files);
    }

    /// <summary>
    /// An undefended base with the given facilities under a retaliation UFO about to arrive (StrategicBaseAttackTests'
    /// set-up): the next second requests the base defense.
    /// </summary>
    internal static (CampaignState Campaign, CampaignDeploymentRequest Request) Requested(IReadOnlyList<FacilitySnapshot> facilities,
        string extraRules = "", ulong seed = 11, Func<string, string>? rules = null, Action<Dictionary<string, byte[]>>? files = null,
        Func<BaseSnapshot, BaseSnapshot>? adjustBase = null, Func<CampaignSnapshot, CampaignSnapshot>? adjust = null)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], (rules?.Invoke(Rules) ?? Rules) + extraRules,
            files: RequiredFiles("BLANKS", "XSET"));
        var snapshot = CreateSnapshot(content, "Base defense map");
        var owner = snapshot.Bases[0];
        var ufo = new UfoSnapshot(9, "UFO_HUNTER", 4, "__RETALIATION_ASSAULT_RUN", 0, owner.Longitude - 0.0001, owner.Latitude,
            UfoStatus.Flying, WorldAltitudes.High)
        {
            Id = 3,
            Speed = 1000,
            MissionWaveNumber = 0,
            Shield = 0,
            Destination = WorldTargetReference.ForWaypoint(0, new WorldPosition(owner.Longitude, owner.Latitude)),
        };
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 0, 5),
            Bases = [(adjustBase ?? (static owner => owner))(owner with { Facilities = facilities }), .. snapshot.Bases.Skip(1)],
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, "MISSION_RETALIATION", "REGION", "RACE_A", 1, 0, 1500, 1, -1)],
                Ufos = [ufo],
            },
        };
        if (adjust is not null) snapshot = adjust(snapshot);
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(seed));
        campaign.BattleTerrainFiles = TerrainFiles(files);
        var request = Assert.IsType<DeploymentRequested>(Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events
            .OfType<DeploymentRequested>())).Request;
        Assert.Equal("STR_BASE_DEFENSE", request.MissionType);
        return (campaign, request);
    }

    internal static FacilitySnapshot Facility(string type, int x, int y, int buildTime = 0, bool hadPrevious = false) =>
        new(type, x, y, buildTime, 0, false, false, hadPrevious);

    // On the 4x3 block grid: the stores, a 2x2 hangar and the tower in the top row, the shelf below the stores, a room
    // under construction that had a previous facility below it, and an unbuilt room in the far corner.
    internal static readonly FacilitySnapshot[] Layout =
    [
        Facility("XB_STORES", 0, 0),
        Facility("XB_HANGAR", 1, 0),
        Facility("XB_TOWER", 3, 0),
        Facility("XB_SHELF", 0, 1),
        Facility("XB_ROOM", 0, 2, buildTime: 5, hadPrevious: true),
        Facility("XB_ROOM", 3, 2, buildTime: 5),
    ];

    private static BattleState MapOf(CampaignState campaign, CampaignDeploymentRequest request) =>
        BattleGenerator.GenerateMap(campaign, request, new BattleTerrainLoader(campaign.Content.RuntimeRules.Battle, TerrainFiles())).State;

    internal static string Refusal(CampaignState campaign) =>
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new StartBattle()).Events)).Reason;

    [Fact]
    public void TheBaseLayoutPlacesTheFacilitiesBlocksBeforeTheScriptFillsTheRest()
    {
        var (campaign, request) = Requested(Layout);
        var battle = MapOf(campaign, request);
        var map = battle.Map;
        string Block(int x, int y) => map.BlockName(x, y);
        // The hangar's map name counts up over its cells in row order; the tower is its ground level's block; the room
        // with a previous facility is built, the unbuilt one is left to the script's dirt.
        Assert.Equal(["XB_03", "XB_08", "XB_09", "TOWER_G"], Enumerable.Range(0, 4).Select(x => Block(x, 0)));
        Assert.Equal(["XB_00", "XB_10", "XB_11"], Enumerable.Range(0, 3).Select(x => Block(x, 1)));
        Assert.Equal("XB_00", Block(0, 2));
        foreach (var (x, y) in new[] { (3, 1), (1, 2), (2, 2), (3, 2) }) Assert.StartsWith("DIRT_", Block(x, y), StringComparison.Ordinal);
        Assert.All(Enumerable.Range(0, 4), x => Assert.Equal("XBASE_T", map.TerrainName(x, 0)));
        // The tower's levels: its ground block on level 0 and its ceiling (one level high by its rule, with XSET's second
        // object as the floor) on level 1.
        Assert.Equal((0, 1), map.Part(map.TileIndex(new(35, 5, 0)), BattleTilePart.Floor));
        Assert.Equal((1, 1), map.Part(map.TileIndex(new(35, 5, 1)), BattleTilePart.Floor));
        // Base defense tiles start discovered.
        Assert.True(map.IsDiscovered(map.TileIndex(new(5, 5, 1)), BattleTilePart.Floor));

        // Drilling: the stores' east edge (marked at the facility's far corner) opens into the hangar, laying XSET's third
        // object on the tunnel floor; inside the hangar the cells lost their marks, so its middle edge stays closed.
        Assert.Equal((2, 1), map.Part(map.TileIndex(new(9, 4, 0)), BattleTilePart.Floor));
        Assert.Equal((0, 1), map.Part(map.TileIndex(new(19, 4, 0)), BattleTilePart.Floor));
        // The stores also open south into the shelf; the tunnel runs before the dirt, so nothing opens into it.
        Assert.Equal((2, 1), map.Part(map.TileIndex(new(4, 9, 0)), BattleTilePart.Floor));
        Assert.Equal((0, 1), map.Part(map.TileIndex(new(14, 19, 0)), BattleTilePart.Floor));

        // The stores' checkerboard on their ground level (the top one), less the tile with an object and the one whose east
        // neighbour has a west wall; their inventory tile is the cell's centre one level lower. The shelf adds its own
        // tiles inside the map (the one above the map is skipped) and keeps that inventory tile.
        var checkerboard = new List<BattlePosition>();
        for (var x = 0; x < 10; x++)
            for (var y = 0; y < 10; y++)
                if ((x + y) % 2 == 0 && (x, y) is not (0, 0) and not (2, 0)) checkerboard.Add(new(x, y, 1));
        Assert.Equal([.. checkerboard, new(2, 13, 0), new(4, 14, 0)], battle.StorageSpace);
        Assert.Equal(new BattlePosition(5, 5, 0), battle.CraftInventoryTile);

        // The module map: two base module tiles in the tower's block count 2, the other blocks have none.
        Assert.Equal(new BattleBaseModule(2, 2), battle.BaseModules[3, 0]);
        Assert.Equal(new BattleBaseModule(-1, -1), battle.BaseModules[0, 0]);
        Assert.Equal((4, 3), (battle.BaseModules.GetLength(0), battle.BaseModules.GetLength(1)));
    }

    [Fact]
    public void FromTheBaseLocationTheMapComesFromTheBasesSeedAndTheTextureBaseTerrain()
    {
        // The tower's ground level names the base terrain, which the globe texture under the base picks.
        var layout = Layout.Select(static facility => facility.RuleId == "XB_TOWER" ? facility with { RuleId = "XB_ALT_TOWER" } : facility).ToArray();
        var (first, firstRequest) = Requested(layout, FromLocation, seed: 11);
        var (second, secondRequest) = Requested(layout, FromLocation, seed: 99);
        var firstMap = MapOf(first, firstRequest).Map;
        Assert.Equal(("ALT_G", "XBASE_ALT"), (firstMap.BlockName(3, 0), firstMap.TerrainName(3, 0)));
        // The script's dirt draws from the base's stream, whatever the campaign's stream holds.
        var secondMap = MapOf(second, secondRequest).Map;
        string Names(BattleMap map) => string.Join(",", from y in Enumerable.Range(0, 3) from x in Enumerable.Range(0, 4) select map.BlockName(x, y));
        Assert.Equal(Names(firstMap), Names(secondMap));
    }

    [Fact]
    public void CheckerboardStoresSkipNorthWallsAndTheMapEdgesAndAHollowOrOneLevelStoreHasNoInventoryTile()
    {
        // The hollow stores have no floor on any level, so nothing is stored; the one-level stores in the far corner have
        // their ground on level 0, so the inventory tile would be below the map.
        var (campaign, request) = Requested([Facility("XB_HOLLOW", 0, 0), Facility("XB_LOW_STORES", 3, 2)]);
        var battle = MapOf(campaign, request);
        var expected = new List<BattlePosition>();
        for (var x = 30; x < 40; x++)
            for (var y = 20; y < 30; y++)
                // No east neighbour on the last column, no south one on the last row; (32, 20)'s south neighbour has a
                // north wall.
                if ((x + y) % 2 == 0 && x != 39 && y != 29 && (x, y) != (32, 20)) expected.Add(new(x, y, 0));
        Assert.Equal(expected, battle.StorageSpace);
        Assert.Null(battle.CraftInventoryTile);
    }

    [Fact]
    public void ALoneInvalidStorageTileMeansNoStorageOnPurpose()
    {
        // (-1, -1, -1) ends the list as placed: no tile and no inventory tile, and no refusal.
        var (campaign, request) = Requested([Facility("XB_NONE_SHELF", 0, 0)]);
        var battle = MapOf(campaign, request);
        Assert.Empty(battle.StorageSpace);
        Assert.Null(battle.CraftInventoryTile);
    }

    [Fact]
    public void ALevelBlockPastTheMapsSideRefusesBeforeTheModuleMap()
    {
        // A 45 tiles wide map and a tower whose ceiling block is 20 wide, with a base module on its column 12: loaded at
        // x 30, the block runs past the map's side, where the reference's getTile returns null and loadMAP crashes. Only
        // such a block reaches the tiles past the last whole block, so the module map's own check is not reached.
        var (campaign, _) = Requested([Facility("XB_TOWER", 3, 0)],
            rules: static rules => rules.Replace("width: 40", "width: 45", StringComparison.Ordinal)
                .Replace("{name: TOWER_C, width: 10", "{name: TOWER_C, width: 20", StringComparison.Ordinal),
            files: static files =>
            {
                var wide = Map(10, 20, 3);
                wide[3 + 12 * 4 + 3] = 3;
                files["MAPS/TOWER_C.MAP"] = wide;
            });
        Assert.Equal("Map block MAPS/TOWER_C.MAP (20x10) placed at (30, 0) runs past the 45x30 map.", Refusal(campaign));
    }

    [Fact]
    public void FromTheBaseLocationTheMapLeavesTheCampaignsStreamAlone()
    {
        // Two layouts whose maps draw differently often: with the setting the campaign's stream ends the same, without
        // it the map's draws come from the campaign's stream.
        IReadOnlyList<FacilitySnapshot> fewer = [Facility("XB_STORES", 0, 0)];
        ulong After(IReadOnlyList<FacilitySnapshot> layout, string extra)
        {
            var (campaign, request) = Requested(layout, extra);
            MapOf(campaign, request);
            return campaign.Random.State;
        }
        Assert.Equal(After(Layout, FromLocation), After(fewer, FromLocation));
        Assert.NotEqual(After(Layout, ""), After(fewer, ""));
    }

    [Theory]
    [InlineData(0, 0, 1_000_000UL)]
    [InlineData(0.5, 0, 500_000UL)]
    [InlineData(0.25, 0.5, 125_000UL)]
    [InlineData(0.5, -0.2, ulong.MaxValue - 100_000 + 1)]
    public void TheBaseSeedIsTheProductOfTheCoordinatesWithZeroCountingAsOne(double longitude, double latitude, ulong seed) =>
        Assert.Equal(seed, BattleGenerator.BaseSeed(longitude, latitude));

    [Theory]
    [InlineData("XB_MISSING", "Map generator encountered an error: map block XB_40 is not defined in terrain XBASE_T.")]
    [InlineData("XB_SHORT", "Facility XB_SHORT map name 'X' is too short to number its blocks.")]
    [InlineData("XB_NO_SHELF", "Could not place items on given tiles in storage facility XB_NO_SHELF")]
    [InlineData("XB_CRAFT_LEVELS", "Facility XB_CRAFT_LEVELS verticalLevels leave no level to place.")]
    [InlineData("XB_EMPTY_LEVELS", "Map generator encountered an error: facility XB_EMPTY_LEVELS has no block on first verticalLevel.")]
    public void AFacilityThatCannotBePlacedRefusesTheBattle(string type, string reason)
    {
        var (campaign, _) = Requested([Facility("XB_STORES", 0, 0), Facility(type, 1, 1)]);
        var before = campaign.Capture();
        Assert.Equal(reason, Refusal(campaign));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void MalformedFacilityVerticalLevelsRefuseTheBattle()
    {
        var (campaign, _) = Requested([Facility("XB_BAD_LEVELS", 1, 1)]);
        Assert.StartsWith("Facility XB_BAD_LEVELS verticalLevels are invalid: ", Refusal(campaign), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("XB_08", 3, "XB_11")]
    [InlineData("XB_05", 5, "XB_10")]
    [InlineData("BIG_99", 1, "BIG_100")]
    // atoi: leading white space and a sign are read, anything else ends the number.
    [InlineData("XB 7", 1, "XB08")]
    [InlineData("X-3", 1, "X0-2")]
    [InlineData("XBA5", 2, "XB02")]
    public void AFacilityCellsBlockNameCountsUpFromTheMapNamesLastTwoCharacters(string mapName, int number, string name) =>
        Assert.Equal(name, BattleMapGenerator.FacilityBlockName(mapName, number));

    [Fact]
    public void AMissingGlobeTextureRefusesTheMapFromTheBaseLocation()
    {
        var (campaign, request) = Requested(Layout, FromLocation);
        var failure = Assert.Throws<MapGenerationException>(() => MapOf(campaign, request with { GlobeTexture = 77 }));
        Assert.Equal("The base's globe texture 77 does not exist, so it has no base terrain.", failure.Message);
    }

    [Fact]
    public void ADeploymentWithoutTerrainsUnderATextureWithTerrainsRefusesTheBaseDefense()
    {
        // run picks the texture's terrain at the UFO or the mission site; a base defense has neither.
        var (campaign, _) = Requested(Layout, "globe:\n  textures:\n    - {id: 10, terrain: [{name: XBASE_T}]}\n",
            rules: static rules => rules.Replace("    terrains: [XBASE_T]\n", "", StringComparison.Ordinal));
        Assert.Equal(BattleGenerator.TextureTerrainWithoutTarget, Refusal(campaign));
    }

    [Fact]
    public void AFacilityOutsideTheMapsBlockGridRefusesTheBattle()
    {
        var (campaign, _) = Requested([Facility("XB_STORES", 0, 0), Facility("XB_HANGAR", 3, 1)]);
        Assert.Equal("Facility XB_HANGAR at 3, 1 does not fit the 40x30 base defense map.", Refusal(campaign));
    }

    [Fact]
    public void ATextureWithoutBaseTerrainsRefusesTheMapFromTheBaseLocation()
    {
        var (campaign, request) = Requested(Layout, "baseDefenseMapFromLocation: 1\n");
        // The base's texture has no base terrain criteria, so the pick names no terrain.
        Assert.Equal($"Map generator encountered an error: base terrain '' of globe texture {request.GlobeTexture} does not exist.", Refusal(campaign));
    }

    [Fact]
    public void FacilityVerticalLevelsAndTheMapFromLocationSettingAreLoaded()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], Rules + FromLocation, files: RequiredFiles("BLANKS", "XSET"));
        var rules = content.RuntimeRules;
        Assert.Equal(1, rules.Campaign.BaseDefenseMapFromLocation);
        var tower = rules.Facilities.GetRequiredValue("XB_TOWER");
        Assert.Equal([(VerticalLevelType.Ground, 8), (VerticalLevelType.Ceiling, 9)],
            tower.VerticalLevels.Select(static level => (level.Type, level.Blocks.Single())));
        Assert.Null(tower.VerticalLevelsInvalid);
        Assert.Equal(3, rules.Facilities.GetRequiredValue("XB_SHELF").StorageTiles.Count);
        Assert.Empty(rules.Facilities.GetRequiredValue("XB_STORES").VerticalLevels);
    }
}
