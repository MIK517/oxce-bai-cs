using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.BattleTestTerrain;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 1 S1 and S3: <c>StartBattle</c> runs the battle generator on the pending deployment request
/// (<c>ConfirmLandingState::btnYesClick</c>, <c>BattlescapeGenerator::run</c> and <c>generateMap</c>). The request is
/// consumed only when the battle exists, and the campaign then holds every strategic command and time (ADR 0038).
/// </summary>
public sealed class BattleStartTests
{
    // BLANKS has two objects (blank, scorched earth); TESTSET's objects follow it as MAP IDs 2 and 3.
    internal const string Rules = """
        alienDeployments:
          - type: UFO_SCOUT
            width: 20
            length: 20
            height: 2
            turnLimit: 15
            chronoTrigger: 2
            cheatTurn: 12
            shade: 9
            terrains: [TEST_TERRAIN]
            race: RACE_TEST
            data:
              - {alienRank: 2, lowQty: 1, highQty: 3, dQty: 1, percentageOutsideUfo: 50, itemSets: [[ITEM_A], [ITEM_B, ITEM_C]]}
              - {customUnitType: VEHICLE, lowQty: 1, itemSets: [[]]}
          - type: UFO_ALIENS
            width: 20
            length: 20
            height: 2
            terrains: [TEST_TERRAIN]
            script: BASIC
            race: RACE_TEST
            data: [{alienRank: 2, lowQty: 2, highQty: 2, itemSets: [[GUN, CLIP], [GRENADE]]}]
          - type: UFO_CIVILIANS
            width: 30
            length: 30
            height: 2
            terrains: [TEST_TERRAIN]
            script: CRAFTED
            race: RACE_TEST
            civilianSpawnNodeRank: 4
            markCiviliansAsVIP: true
            civiliansByType: {CIVILIAN: 2}
            data: [{alienRank: 5, lowQty: 1, highQty: 1, itemSets: [[]]}]
          - type: UFO_POWER
            width: 20
            length: 20
            height: 2
            terrains: [TEST_TERRAIN]
            script: POWER
            objectiveType: 2
            objectivesRequired: 1
            vipSurvivalPercentage: 60
            escapeType: 2
            bughuntMinTurn: 7
            noWeaponPile: true
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_JUNK
            width: 20
            length: 20
            height: 2
            terrains: [TEST_TERRAIN]
            script: JUNK
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_ARMORY
            width: 20
            length: 20
            height: 2
            terrains: [TEST_TERRAIN]
            script: ARMORY
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_RESIZED
            width: 30
            length: 30
            height: 2
            terrains: [TEST_TERRAIN]
            script: RESIZED
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_ROADS
            width: 30
            length: 30
            height: 2
            terrains: [TEST_TERRAIN]
            script: ROADS
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_CRAFTED
            width: 30
            length: 30
            height: 2
            terrains: [TEST_TERRAIN]
            script: CRAFTED
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_TUNNEL
            width: 20
            length: 10
            height: 2
            terrains: [TEST_TERRAIN]
            script: TUNNEL
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_LEVELS
            width: 20
            length: 10
            height: 3
            terrains: [TEST_TERRAIN]
            script: LEVELS
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_SUITED
            width: 30
            length: 30
            height: 2
            terrains: [TEST_TERRAIN]
            script: CRAFTED
            enviroEffects: ENV_SUIT
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_GEAR
            width: 30
            length: 30
            height: 2
            terrains: [TEST_TERRAIN]
            script: CRAFTED
            startingCondition: SC_GEAR
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_REPLACED
            width: 30
            length: 30
            height: 2
            terrains: [TEST_TERRAIN]
            script: CRAFTED
            startingCondition: SC_REPLACE
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_OVERRUN
            width: 20
            length: 20
            height: 6
            terrains: [TEST_TERRAIN]
            script: OVERRUN
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_OVERRUN_SOUTH
            width: 20
            length: 20
            height: 6
            terrains: [TEST_TERRAIN]
            script: OVERRUN_SOUTH
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
          - type: UFO_UNFILLED
            width: 20
            length: 20
            height: 2
            terrains: [TEST_TERRAIN]
            script: UNFILLED
            data: [{customUnitType: ALIEN, lowQty: 1, itemSets: [[]]}]
        terrains:
          - name: TEST_TERRAIN
            mapDataSets: [BLANKS, TESTSET]
            mapBlocks:
              - {name: BIG, width: 20, length: 10, groups: [0]}
              - {name: SMALL, width: 10, length: 10, groups: [0, 1]}
              - {name: ROAD_EW, width: 10, length: 10, groups: [2]}
              - {name: ROAD_NS, width: 10, length: 10, groups: [3]}
              - {name: CROSSING, width: 10, length: 10, groups: [4]}
              - {name: ROOF, width: 10, length: 10, height: 1, groups: [6]}
              - {name: POWER, width: 10, length: 10, groups: [8]}
              - {name: JUNK, width: 10, length: 10, groups: [9], items: {GRENADE: [[1, 1, 0]]}, fuseTimers: {GRENADE: [-2, -2]}}
              - name: ARMORY
                width: 10
                length: 10
                groups: [7]
                items: {GRENADE: [[1, 1, 0], [2, 1, 0]]}
                fuseTimers: {GRENADE: [3, 3]}
                randomizedItems: [{position: [3, 3, 0], amount: 2, itemList: [CLIP]}]
                extendedItems: [{type: GUN, pos: [[4, 4, 0]], ammoDef: [[CLIP, 5]]}]
              - {name: TALL, width: 10, length: 20, groups: [11]}
            script: BASIC
        alienItemLevels: [[0, 0, 0, 0, 0, 0, 0, 0, 0, 0]]
        alienFuel: [CLIP, 50]
        statGrowthMultipliers: {tu: 10}
        statGrowthMultipliersAbs: [{}, {}, {tu: 5}]
        aimMultipliers: [1, 1, 0.5]
        alienRaces:
          - id: RACE_TEST
            members: [ALIEN, ALIEN, ALIEN, ALIEN, ALIEN, ALIEN, ALIEN, ALIEN]
        invs:
          - id: STR_GROUND
            type: 2
          - id: STR_RIGHT_HAND
            type: 1
          - id: STR_LEFT_HAND
            type: 1
          - id: STR_BELT
            slots: [[0, 0], [1, 0]]
            costs: {STR_RIGHT_HAND: 4, STR_LEFT_HAND: 4}
          - id: STR_BACK_PACK
            slots: [[0, 0]]
            costs: {STR_RIGHT_HAND: 8, STR_LEFT_HAND: 2}
        enviroEffects:
          - type: ENV_SUIT
            armorTransformations: {ARMOR: SUIT}
        startingConditions:
          - type: SC_GEAR
            forbiddenVehicles: [VEHICLE_B]
            forbiddenItems: [GRENADE, VEHICLE_AMMO]
          - type: SC_REPLACE
            forbiddenArmors: [ARMOR]
            defaultArmor: {RECRUIT: {SUIT: 2}}
        armors:
          - type: SUIT
            movementType: 1
            builtInWeapons: [GUN, CLIP, GRENADE]
            specialWeapon: AMP
            storeItem: SUPPLY
            corpseBattle: [CORPSE]
            corpseGeo: CORPSE
            loftempsSet: [0]
          - type: VEHICLE_ARMOR
            size: 2
            allowInv: false
            specialWeapon: RAM
            stats: {tu: 10}
            corpseBattle: [CORPSE, CORPSE, CORPSE, CORPSE]
            corpseGeo: CORPSE
            loftempsSet: [0, 0, 0, 0]
          - type: SMALL_VEHICLE_ARMOR
            size: 1
            allowInv: false
            corpseBattle: [CORPSE]
            corpseGeo: CORPSE
            loftempsSet: [0]
        items:
          - type: VEHICLE
            fixedWeapon: true
            battleType: 1
            compatibleAmmo: [VEHICLE_AMMO]
            turretType: 2
          - type: VEHICLE_AMMO
            battleType: 2
            clipSize: 30
          - type: RAM
            battleType: 3
            clipSize: -1
          - type: GUN
            battleType: 1
            compatibleAmmo: [CLIP]
            weight: 1
          - type: CLIP
            battleType: 2
            clipSize: 12
            weight: 1
          - type: GRENADE
            battleType: 4
            weight: 1
          - type: AMP
            battleType: 9
          - type: VEHICLE_B
            fixedWeapon: true
        units:
          - type: CIVILIAN
            armor: SMALL_VEHICLE_ARMOR
          - type: ALIEN
            armor: SMALL_VEHICLE_ARMOR
            stats: {tu: 50, firing: 61}
          - type: VEHICLE
            armor: VEHICLE_ARMOR
            stats: {tu: 60, health: 50}
          - type: VEHICLE_B
            armor: SMALL_VEHICLE_ARMOR
        crafts:
          - type: SHIP
            battlescapeTerrainData:
              name: SHIP_MAP
              mapDataSets: [BLANKS, SHIPSET]
              mapBlocks: [{name: SHIPBLOCK, width: 10, length: 10}]
        ufos:
          - type: UFO_SCOUT
            battlescapeTerrainData:
              name: SCOUT_MAP
              mapDataSets: [BLANKS, UFOSET]
              mapBlocks: [{name: SCOUTBLOCK, width: 10, length: 20, groups: [0, 5]}]
        mapScripts:
          - type: POWER
            commands:
              - {type: addBlock, groups: [8], rects: [[0, 0, 1, 1]]}
              - {type: fillArea}
          - type: JUNK
            commands:
              - {type: addBlock, groups: [9], rects: [[0, 0, 1, 1]]}
              - {type: fillArea}
          - type: ARMORY
            commands:
              - {type: addBlock, groups: [7], rects: [[0, 0, 1, 1]]}
              - {type: fillArea}
          - type: BASIC
            commands:
              - {type: addBlock, size: [2, 1], rects: [[0, 0, 2, 1]]}
              - {type: fillArea}
          - type: RESIZED
            commands:
              - {type: resize, size: [2, 2, 2], label: 1}
              - {type: addBlock, size: [2, 1], rects: [[0, 1, 2, 1]], label: 2}
              - {type: checkBlock, groups: [0], rects: [[0, 0, 2, 1]], label: 3}
              - {type: removeBlock, rects: [[0, 1, 1, 1]], conditionals: [-3], label: 4}
              - {type: fillArea, conditionals: [4]}
          - type: ROADS
            commands:
              - {type: addLine, direction: both}
              - {type: fillArea}
          - type: CRAFTED
            commands:
              - {type: addCraft}
              - {type: addUFO}
              - {type: fillArea}
          - type: TUNNEL
            commands:
              - {type: fillArea}
              - type: digTunnel
                direction: horizontal
                rects: [[3, 3, 3, 4]]
                tunnelData:
                  level: 0
                  MCDReplacements:
                    - {type: floor, set: 1, entry: 1}
                    - {type: northWall, set: 1, entry: 0}
                    - {type: corner, set: 1, entry: 0}
          - type: LEVELS
            commands:
              - type: fillArea
                verticalLevels:
                  - {type: ground, groups: [0]}
                  - {type: middle, groups: [0]}
                  - {type: ceiling, blocks: [5]}
          - type: OVERRUN
            commands:
              - type: addBlock
                size: [1, 1]
                rects: [[1, 0, 1, 1]]
                verticalLevels:
                  - {type: ground, blocks: [1]}
                  - {type: ceiling, blocks: [0]}
              - {type: fillArea}
          - type: OVERRUN_SOUTH
            commands:
              - type: addBlock
                size: [1, 1]
                rects: [[0, 1, 1, 1]]
                verticalLevels:
                  - {type: ground, blocks: [1]}
                  - {type: ceiling, blocks: [9]}
              - {type: fillArea}
          - type: UNFILLED
            commands:
              - {type: addBlock, groups: [1]}
        """;

    /// <summary>A one-level MAP with MAP ID 4 (the power source) as the object part of the given tiles.</summary>
    private static byte[] WithObjects(byte[] map, params int[] tiles)
    {
        foreach (var tile in tiles) map[3 + tile * 4 + 3] = 4;
        return map;
    }

    internal static Files TerrainFiles(Action<Dictionary<string, byte[]>>? adjust = null)
    {
        var files = TerrainFileMap();
        adjust?.Invoke(files);
        return new(files);
    }

    private static Dictionary<string, byte[]> TerrainFileMap() => new()
    {
        ["TERRAIN/BLANKS.MCD"] = Mcd(Record(), Record((42, 1))),
        // TESTSET's third object (MAP ID 4) is a UFO power source (special type 2).
        ["TERRAIN/TESTSET.MCD"] = Mcd(Record((42, 2)), Record((42, 3)), Record((42, 4), (59, 2))),
        // BIG has a floor on its top level only; SMALL has one level of TESTSET's first object.
        ["MAPS/BIG.MAP"] = Map(10, 20, 3, 0),
        ["ROUTES/BIG.RMP"] = [],
        ["MAPS/SMALL.MAP"] = Map(10, 10, 2),
        // A node on the east edge (an alien spawn node, rank 4) linking east (253), west (251) and to the second node, which lies outside the
        // block (x 12) and is culled; then an X-COM spawn node (rank 1, priority 1).
        ["ROUTES/SMALL.RMP"] = [.. RouteNode(9, 5, 0, [253, 251, 1], rank: 4, priority: 1), .. RouteNode(12, 5, 0, []),
            .. RouteNode(2, 2, 0, [], rank: 1, priority: 1)],
        ["MAPS/ROAD_EW.MAP"] = Map(10, 10, 2),
        ["ROUTES/ROAD_EW.RMP"] = [],
        ["MAPS/ROAD_NS.MAP"] = Map(10, 10, 2),
        ["ROUTES/ROAD_NS.RMP"] = [],
        ["MAPS/CROSSING.MAP"] = Map(10, 10, 3),
        ["MAPS/ROOF.MAP"] = Map(10, 10, 3),
        ["ROUTES/ROOF.RMP"] = [],
        // The craft's floor is a start point (special type 1).
        ["TERRAIN/SHIPSET.MCD"] = Mcd(Record((42, 7), (59, 1))),
        // The craft's skin 2: the same start point floor with armor 9.
        ["TERRAIN/SHIPSET_2.MCD"] = Mcd(Record((42, 9), (59, 1))),
        ["TERRAIN/UFOSET.MCD"] = Mcd(Record((42, 8))),
        ["MAPS/SHIPBLOCK.MAP"] = Map(10, 10, 2),
        ["ROUTES/SHIPBLOCK.RMP"] = RouteNode(4, 4, 0, []),
        ["MAPS/SCOUTBLOCK.MAP"] = Map(20, 10, 2),
        ["ROUTES/SCOUTBLOCK.RMP"] = [],
        ["ROUTES/CROSSING.RMP"] = [],
        ["MAPS/ARMORY.MAP"] = Map(10, 10, 2),
        ["MAPS/POWER.MAP"] = WithObjects(Map(10, 10, 2), 55, 56),
        ["ROUTES/POWER.RMP"] = RouteNode(8, 8, 0, [], rank: 4, priority: 1),
        ["MAPS/JUNK.MAP"] = Map(10, 10, 2),
        ["ROUTES/JUNK.RMP"] = RouteNode(8, 8, 0, [], rank: 4, priority: 1),
        ["ROUTES/ARMORY.RMP"] = RouteNode(8, 8, 0, [], rank: 4, priority: 1),
        ["MAPS/TALL.MAP"] = Map(20, 10, 2),
        ["ROUTES/TALL.RMP"] = [],
    };

    private static (CampaignState Campaign, CampaignDeploymentRequest Request) Requested(string? rules, string ufo = "UFO_SCOUT",
        IReadOnlyList<CraftVehicleSnapshot>? vehicles = null, IReadOnlyDictionary<string, int>? items = null,
        IReadOnlyList<SoldierLayoutItem>? layout = null, Func<CampaignSnapshot, CampaignSnapshot>? adjust = null)
    {
        // The landing UFO is UFO_SCOUT: the deployment under test takes its name and the default one steps aside.
        if (rules is not null && ufo != "UFO_SCOUT")
            rules = rules.Replace("type: UFO_SCOUT\n    width", "type: UFO_SCOUT_DEFAULT\n    width", StringComparison.Ordinal)
                .Replace("type: " + ufo + "\n", "type: UFO_SCOUT\n", StringComparison.Ordinal);
        var (content, campaign) = StrategicLandingTests.Create(rules: rules, files: rules is null ? null : RequiredFiles("BLANKS", "TESTSET"),
            vehicles: vehicles, items: items, layout: layout);
        campaign.BattleTerrainFiles = TerrainFiles();
        var requested = AssertLandingRequested(campaign);
        var result = campaign.Execute(new AnswerLanding(requested.Confirmation, LandingAnswer.Yes));
        var request = Assert.IsType<DeploymentRequested>(Assert.Single(result.Events)).Request;
        if (adjust is not null)
        {
            campaign = CampaignState.Restore(adjust(campaign.Capture()), content, new SplitMix64RandomSource(17));
            campaign.BattleTerrainFiles = TerrainFiles();
        }
        return (campaign, request);
    }

    /// <summary>Starts the battle, reporting the refusal reason when there is one.</summary>
    private static BattleStarted Started(CampaignState campaign)
    {
        var events = campaign.Execute(new StartBattle()).Events;
        Assert.Equal("", events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Reason.ToString() ?? "");
        return Assert.IsType<BattleStarted>(Assert.Single(events));
    }

    private static string Refusal(CampaignState campaign, ICampaignCommand command) =>
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(command).Events)).Reason;

    private static BattleState Battle(CampaignState campaign) =>
        campaign.GetQuery<IBattleQuery>() is CampaignBattles battles ? battles.Battle! : throw new InvalidOperationException();

    [Fact]
    public void StartBattleGeneratesTheMapConsumesTheRequestAndHoldsTheCampaign()
    {
        var (campaign, request) = Requested(Rules);
        Assert.Equal(("STR_UFO_GROUND_ASSAULT", "UFO_SCOUT"), (request.MissionType, request.DeploymentId));
        Assert.Null(campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer));

        var started = Started(campaign);
        Assert.Equal(new BattleStarted("STR_UFO_GROUND_ASSAULT", "UFO_SCOUT"), started);
        Assert.Null(campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest());

        // SavedBattleGame before the briefing: turn 0, the player's side, the deployment's size and limits.
        var overview = campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer)!;
        Assert.Equal(("STR_UFO_GROUND_ASSAULT", "UFO_SCOUT", 20, 20, 2, 0, BattleFaction.Player, 15),
            (overview.MissionType, overview.DeploymentId, overview.Width, overview.Length, overview.Height, overview.Turn, overview.Side,
                overview.TurnLimit));

        // The terrain's script: BIG across the top row, then SMALL fills the two remaining blocks.
        var battle = Battle(campaign);
        var map = battle.Map;
        Assert.Equal(("TEST_TERRAIN", "BASIC", 9, 0), (battle.TerrainId, battle.MapScriptId, battle.GlobalShade, battle.Depth));
        Assert.Equal(["BLANKS", "TESTSET"], map.DataSets.Select(static set => set.Name));
        Assert.Equal(["BIG", "BIG", "SMALL", "SMALL"], new[] { (0, 0), (1, 0), (0, 1), (1, 1) }.Select(block => map.BlockName(block.Item1, block.Item2)));
        Assert.Equal("TEST_TERRAIN", map.TerrainName(1, 1));
        // MAP ID 3 is TESTSET's second object; the empty level-0 floors get the scorched earth tile (BLANKS 1).
        Assert.Equal((1, 1), map.Part(map.TileIndex(new(19, 9, 1)), BattleTilePart.Floor));
        Assert.Equal((1, 0), map.Part(map.TileIndex(new(19, 9, 0)), BattleTilePart.Floor));
        Assert.Equal(3, map.MapData(map.TileIndex(new(0, 0, 1)), BattleTilePart.Floor)!.Armor);
        Assert.Equal((0, 1), map.Part(map.TileIndex(new(5, 15, 0)), BattleTilePart.Floor));
        Assert.Equal((-1, -1), map.Part(map.TileIndex(new(5, 15, 1)), BattleTilePart.Floor));
        Assert.Equal((-1, -1), map.Part(map.TileIndex(new(5, 15, 0)), BattleTilePart.WestWall));

        // Routes: one segment per placed block in row order (BIG has none), node IDs across blocks, culled nodes kept as
        // dummies; attachNodeLinks joins the east link of the first SMALL to the west link of the second.
        Assert.Equal([false, true, false, false, true, false], map.Nodes.Select(static node => node.IsDummy));
        var (west, east) = (map.Nodes[0], map.Nodes[3]);
        Assert.Equal((0, new BattlePosition(9, 15, 0), 1, 4), (west.Id, west.Position, west.Segment, west.Rank));
        Assert.Equal((3, new BattlePosition(19, 15, 0), 2), (east.Id, east.Position, east.Segment));
        Assert.Equal([3, -5, -1, -1, -1], west.Links);
        Assert.Equal([-3, 0, -1, -1, -1], east.Links);

        // The geoscape is frozen: time, other commands, a second start and saving are refused.
        AssertTimeBlocked(campaign, CampaignBattles.BattleInProgress.Text);
        var before = campaign.Capture();
        Assert.Equal(CampaignBattles.BattleInProgress.Text, Refusal(campaign, new RecallCraft(request.BaseId, "SHIP", 1)));
        Assert.Equal(CampaignBattles.BattleInProgress.Text, Refusal(campaign, new StartBattle()));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Equal(CampaignBattles.BattleSaveUnsupported.Text, campaign.QuerySaveRefusal());
        Assert.Null(campaign.CaptureForSave(out var refusal));
        Assert.Equal(CampaignBattles.BattleSaveUnsupported.Text, refusal);
    }

    [Fact]
    public void ResizeCheckBlockRemoveBlockAndConditionalsFollowTheReference()
    {
        var (campaign, _) = Requested(Rules, "UFO_RESIZED");
        Started(campaign);
        var map = Battle(campaign).Map;
        // resize to 2x2 blocks; BIG on the second row; checkBlock finds nothing on the first row (label 3 fails), so
        // removeBlock runs and takes BIG away (label 4 succeeds), and fillArea fills all four blocks with SMALL.
        Assert.Equal((20, 20, 2), (map.Width, map.Length, map.Height));
        Assert.Equal(["SMALL", "SMALL", "SMALL", "SMALL"], new[] { (0, 0), (1, 0), (0, 1), (1, 1) }.Select(block => map.BlockName(block.Item1, block.Item2)));
        Assert.Equal((-1, -1), map.Part(map.TileIndex(new(15, 15, 1)), BattleTilePart.Floor));
    }

    [Fact]
    public void AddLineInBothDirectionsCrossesTheRoadsAndKeepsTheReplacedBlocksName()
    {
        var (campaign, _) = Requested(Rules, "UFO_ROADS");
        Started(campaign);
        var map = Battle(campaign).Map;
        var names = new string[3, 3];
        for (var x = 0; x < 3; x++)
            for (var y = 0; y < 3; y++) names[x, y] = map.BlockName(x, y);
        // MapScript's default groups: north-south roads are group 3, east-west roads 2, crossings 4. The vertical road
        // goes first; the horizontal road meets it, turns that cell into a crossing in place and keeps its name.
        var column = Enumerable.Range(0, 3).Single(x => Enumerable.Range(0, 3).All(y => names[x, y] == "ROAD_NS"));
        var row = Enumerable.Range(0, 3).Single(y => Enumerable.Range(0, 3).Count(x => names[x, y] == "ROAD_EW") == 2);
        Assert.All(Enumerable.Range(0, 3).Where(x => x != column), x => Assert.Equal("ROAD_EW", names[x, row]));
        // The crossing's MAP (floor ID 3) replaced the road's tiles.
        Assert.Equal((1, 1), map.Part(map.TileIndex(new(column * 10 + 5, row * 10 + 5, 0)), BattleTilePart.Floor));
        Assert.Equal((0, 1), map.Part(map.TileIndex(new(column * 10 + 5, ((row + 1) % 3) * 10 + 5, 0)), BattleTilePart.Floor));
        Assert.Equal(4, Enumerable.Range(0, 9).Count(cell => names[cell % 3, cell / 3] == "SMALL"));
    }

    [Fact]
    public void TheCraftAndTheUfoLandOnLandingZonesAndTheirMapsAndRoutesLoadAfterTheScript()
    {
        var (campaign, _) = Requested(Rules, "UFO_CRAFTED");
        Started(campaign);
        var map = Battle(campaign).Map;
        var cells = Enumerable.Range(0, 9).Select(cell => (X: cell % 3, Y: cell / 3)).ToArray();
        // The block names under a craft or UFO name the craft or UFO type and its map block.
        var craft = Assert.Single(cells, cell => map.TerrainName(cell.X, cell.Y) == "SHIP");
        Assert.Equal("SHIPBLOCK", map.BlockName(craft.X, craft.Y));
        var ufo = cells.Where(cell => map.TerrainName(cell.X, cell.Y) == "UFO_SCOUT").ToArray();
        Assert.Equal(2, ufo.Length);
        Assert.All(ufo, cell => Assert.Equal("SCOUTBLOCK", map.BlockName(cell.X, cell.Y)));
        Assert.Equal(6, cells.Count(cell => map.BlockName(cell.X, cell.Y) == "SMALL"));
        // The UFO's data sets follow the terrain's, then the craft's; their tiles replace the landing zone's.
        Assert.Equal(["BLANKS", "TESTSET", "BLANKS", "UFOSET", "BLANKS", "SHIPSET"], map.DataSets.Select(static set => set.Name));
        var craftTile = map.TileIndex(new(craft.X * 10 + 5, craft.Y * 10 + 5, 0));
        Assert.Equal((0, 5), map.Part(craftTile, BattleTilePart.Floor));
        Assert.Equal(7, map.MapData(craftTile, BattleTilePart.Floor)!.Armor);
        Assert.True(map.IsDiscovered(craftTile, BattleTilePart.Floor));
        Assert.Equal((0, 3), map.Part(map.TileIndex(new(ufo[0].X * 10 + 5, ufo[0].Y * 10 + 5, 0)), BattleTilePart.Floor));
        // The landing zone blocks keep no routes of their own; the craft's route has the craft segment.
        var real = map.Nodes.Where(static node => !node.IsDummy).ToArray();
        Assert.Equal(13, real.Length);
        var craftNode = Assert.Single(real, static node => node.Segment == 1000);
        Assert.Equal(new BattlePosition(craft.X * 10 + 4, craft.Y * 10 + 4, 0), craftNode.Position);

        // deployXCOM: the craft's soldier stands on the first start tile of the craft in tile order, which is also the
        // craft inventory tile, and is the selected unit.
        var battle = Battle(campaign);
        var soldier = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Player);
        var first = new BattlePosition(craft.X * 10, craft.Y * 10, 0);
        Assert.Equal((BattleFaction.Player, first, first, soldier.Id), (soldier.Faction, soldier.Position, battle.CraftInventoryTile, battle.SelectedUnitId));
        Assert.Equal(soldier.Id, map.UnitAt(map.TileIndex(first)));
        Assert.Equal(soldier.SoldierId, soldier.Id);
        Assert.Equal(BattleController.Human(0), soldier.Controller);
    }

    [Fact]
    public void ASkinnedCraftLoadsItsSkinsDataSetsExceptBlanks()
    {
        // RuleTerrain::refreshMapDataSets with Craft::getSkinIndex: SHIPSET becomes SHIPSET_2, BLANKS stays; the MCD patch
        // of the skinned name applies (getMCDPatch(mds->getName()) after the renaming).
        static Func<CampaignSnapshot, CampaignSnapshot> Skin(int skin) => snapshot => snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { SkinIndex = skin },
        });
        var patched = Rules + "\nMCDPatches:\n  - type: SHIPSET_2\n    data: [{MCDIndex: 0, armor: 11}]\n";
        var (campaign, _) = Requested(patched, "UFO_CRAFTED", adjust: Skin(2));
        Started(campaign);
        var map = Battle(campaign).Map;
        Assert.Equal(["BLANKS", "SHIPSET_2"], map.DataSets.TakeLast(2).Select(static set => set.Name));
        Assert.Equal(11, map.MapData(map.TileIndex(CraftOrigin(map)), BattleTilePart.Floor)!.Armor);

        // A replaced craft rule (addCraft's craftName) is renamed with the landing craft's own skin.
        var replaced = patched.Replace("crafts:\n", "crafts:\n  - type: SHIP_B\n    battlescapeTerrainData:\n      name: SHIP_B_MAP\n" +
                "      mapDataSets: [BLANKS, SHIPSET]\n      mapBlocks: [{name: SHIPBLOCK, width: 10, length: 10}]\n", StringComparison.Ordinal)
            .Replace("- {type: addCraft}", "- {type: addCraft, craftName: SHIP_B}", StringComparison.Ordinal);
        (campaign, _) = Requested(replaced, "UFO_CRAFTED", adjust: Skin(2));
        Started(campaign);
        map = Battle(campaign).Map;
        Assert.Equal(11, map.MapData(map.TileIndex(CraftOrigin(map, "SHIP_B")), BattleTilePart.Floor)!.Armor);

        // A skin without its data set fails when the data is loaded; a negative skin is a name like any other.
        (campaign, _) = Requested(Rules, "UFO_CRAFTED", adjust: Skin(3));
        Assert.Equal("Terrain file TERRAIN/SHIPSET_3.MCD is missing.", Refusal(campaign, new StartBattle()));
        (campaign, _) = Requested(Rules, "UFO_CRAFTED", adjust: Skin(-1));
        Assert.Equal("Terrain file TERRAIN/SHIPSET_-1.MCD is missing.", Refusal(campaign, new StartBattle()));
    }

    [Theory]
    [InlineData(2, 2, 2)]
    [InlineData(2, 3, 0)]
    [InlineData(2, -1, -1)]
    [InlineData(-2, -1, 0)]
    public void TheCraftsSkinIndexIsReadUpToTheRulesMaximumAndWrittenBack(int maximum, int saved, int loaded)
    {
        // Craft::load: a skin above maxSkinIndex, even a negative one, falls back to 0; Craft::save writes a nonzero skin.
        var (content, campaign) = StrategicLandingTests.Create(rules: $"crafts:\n  - type: SHIP\n    maxSkinIndex: {maximum}\n");
        Assert.Equal(maximum, content.RuntimeRules.Crafts.GetRequiredValue("SHIP").MaximumSkinIndex);
        var yaml = System.Text.RegularExpressions.Regex.Replace(OxceSaveAdapter.EmitNewCampaign(campaign.Capture()),
            @"type: SHIP\n(\s+)id: 1\n", match => match.Value + $"{match.Groups[1].Value}skinIndex: {saved}\n");
        var snapshot = TestFixtures.LoadLogisticsSave(yaml, content, 17).Campaign.Capture();
        Assert.Equal(loaded, snapshot.Bases[0].Crafts.Single(static craft => craft.RuleId == "SHIP").Logistics!.SkinIndex);
        var rewritten = OxceSaveAdapter.EmitNewCampaign(snapshot);
        Assert.Equal(loaded != 0, rewritten.Contains("skinIndex", StringComparison.Ordinal));
        Assert.Equivalent(snapshot, TestFixtures.LoadLogisticsSave(rewritten, content, 17).Campaign.Capture(), strict: true);
    }

    [Fact]
    public void VehiclesComeFirstAndALargeVehicleTakesFourStartTiles()
    {
        var (campaign, request) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 0), new("VEHICLE_B", 0)]);
        Assert.Equal(["VEHICLE", "VEHICLE_B"], request.Vehicles);
        Started(campaign);
        var battle = Battle(campaign);
        var map = battle.Map;
        var craft = Enumerable.Range(0, 9).Select(cell => (X: cell % 3, Y: cell / 3)).Single(cell => map.TerrainName(cell.X, cell.Y) == "SHIP");
        var (x, y) = (craft.X * 10, craft.Y * 10);
        // Without extendedHwpLoadOrder every vehicle comes first, in craft order, numbered from MAX_SOLDIER_ID; then the
        // soldier. Each takes the first start tile in tile order that fits it.
        Assert.Equal([(BattleState.MaximumSoldierId, "VEHICLE", 2, new BattlePosition(x, y, 0)),
                (BattleState.MaximumSoldierId + 1, "VEHICLE_B", 1, new BattlePosition(x + 2, y, 0)),
                (Assert.Single(request.Soldiers), battle.Units[2].UnitType, 1, new BattlePosition(x + 3, y, 0))],
            battle.Units.Where(static unit => unit.Faction == BattleFaction.Player)
                .Select(static unit => (unit.Id, unit.UnitType, unit.ArmorSize, unit.Position!.Value)));
        Assert.Equal(BattleState.MaximumSoldierId, battle.SelectedUnitId);
        // The large vehicle stands on all four of its tiles.
        foreach (var (dx, dy) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
            Assert.Equal(BattleState.MaximumSoldierId, map.UnitAt(map.TileIndex(new(x + dx, y + dy, 0))));
        Assert.Null(battle.Units[0].SoldierId);
    }

    [Fact]
    public void AVehicleCarriesItsSpecialItemItsFixedWeaponAndTheWeaponsLoadedAmmo()
    {
        var (campaign, request) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 25), new("VEHICLE_B", 0)]);
        Assert.Equal([25, 0], request.VehicleAmmo);
        Started(campaign);
        var battle = Battle(campaign);
        var (vehicle, small) = (battle.Units[0].Id, battle.Units[1].Id);
        // initUnit's special built-in item (the armor's special weapon, in no section), then addXCOMVehicle's fixed weapon
        // in the right hand and its clip ammunition, loaded into the weapon with the vehicle's ammo; the small vehicle's
        // weapon takes no ammunition. The craft's cargo follows.
        Assert.Equal([(0, "RAM", (int?)vehicle, "", true, false), (1, "VEHICLE", vehicle, "STR_RIGHT_HAND", false, false),
                (2, "VEHICLE_AMMO", null, "", false, true), (3, "VEHICLE_B", small, "STR_RIGHT_HAND", false, false)],
            battle.Items.Take(4).Select(static item => (item.Id, item.ItemType, item.OwnerUnitId, item.Slot, item.IsSpecialWeapon, item.IsAmmo)));
        Assert.Equal([0], battle.Units[0].SpecialWeaponIds);
        // Slots without compatible ammo need none: they hold the weapon itself.
        Assert.Equal([2, 1, 1, 1], battle.Items[1].AmmoSlots);
        Assert.Equal(25, battle.Items[2].AmmoQuantity);
        // addItem marks what it places as X-COM property; createItemForUnitSpecialBuiltin does not.
        Assert.Equal([false, true, true, true], battle.Items.Take(4).Select(static item => item.XcomProperty));
        // The unit rule's statistics plus the armor's; the turret is the weapon's.
        Assert.Equal((70, 50, 0, 100, 2), (battle.Units[0].TimeUnits, battle.Units[0].Health, battle.Units[0].Mana, battle.Units[0].Morale,
            battle.Units[0].TurretType));
        Assert.Equal(-1, battle.Units[1].TurretType);
        // A soldier's statistics are its current statistics plus its soldier bonuses (the fixture's commendation gives
        // PILOT_BONUS, +10 TU) plus its armor's.
        var soldier = battle.Units[2];
        var personal = campaign.Capture().Bases[0].Soldiers.Single(state => state.Id == soldier.SoldierId).Personal!;
        Assert.Equal("PILOT_BONUS", campaign.Content.RuntimeRules.Commendations.GetRequiredValue(Assert.Single(personal.Commendations).RuleId)
            .SoldierBonusTypes[0]);
        var armor = campaign.Content.RuntimeRules.Armors.GetRequiredValue(soldier.ArmorId).Stats;
        int Stat(string key) => personal.CurrentStats[key] + armor.GetValueOrDefault(key);
        Assert.Equal((Stat("tu") + 10, Stat("stamina"), Stat("health"), 100), (soldier.TimeUnits, soldier.Energy, soldier.Health, soldier.Morale));
    }

    [Fact]
    public void ASoldiersArmorBuiltInItemsFillTheHandsTheWeaponAndTheCheapestSection()
    {
        // The environment puts the soldier in the suit, whose built-in items come in list order with ammunition last.
        var (campaign, _) = Requested(Rules, "UFO_SUITED");
        Started(campaign);
        var battle = Battle(campaign);
        var soldier = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Player);
        Assert.Equal(0, soldier.Stats.PsiSkill);
        // The psi-amp special weapon needs psi skill, so the soldier has none. The gun goes to the right hand (C1); the
        // grenade to the storage section cheapest to reach from the empty left hand (the back pack, 2, over the belt, 4);
        // the clip loads into the gun.
        Assert.Empty(soldier.SpecialWeaponIds);
        Assert.Equal([(0, "GUN", (int?)soldier.Id, "STR_RIGHT_HAND", 0), (1, "GRENADE", soldier.Id, "STR_BACK_PACK", 0), (2, "CLIP", null, "", 0)],
            battle.Items.Where(static item => item.ItemType != "SUPPLY")
                .Select(static item => (item.Id, item.ItemType, item.OwnerUnitId, item.Slot, item.SlotX)));
        Assert.Equal(2, battle.Items[0].AmmoSlots[0]);
        Assert.True(battle.Items[2].IsAmmo);
    }

    [Fact]
    public void AliensSpawnOnNodesByRankWithDifficultyStatisticsAndTheirItemSet()
    {
        var (campaign, _) = Requested(Rules, "UFO_ALIENS");
        Assert.Equal(CampaignDifficulty.Veteran, campaign.Difficulty);
        Started(campaign);
        var battle = Battle(campaign);
        var aliens = battle.Units.Where(static unit => unit.Faction == BattleFaction.Hostile).ToArray();
        // Two rank-2 aliens of the deployment's race on the two SMALL blocks' rank-4 nodes (Node::nodeRank's second
        // choice for an engineer), numbered after the X-COM units.
        Assert.Equal(2, aliens.Length);
        Assert.All(aliens, alien =>
        {
            Assert.Equal(("ALIEN", 2, BattleControllerKind.Ai), (alien.UnitType, alien.Rank, alien.Controller.Kind));
            var node = battle.Map.Nodes.Single(node => !node.IsDummy && node.Id == alien.StartNodeId);
            Assert.Equal((4, node.Position), (node.Rank, alien.Position!.Value));
            // Veteran: 10% TU growth times coefficient 2 (50 to 60), firing halved (61 to 30), then the absolute growth (+5 TU).
            Assert.Equal((65, 30), (alien.TimeUnits, (int)alien.Stats.Firing));
            // Item level 0's set: the gun in the right hand, the clip loaded into it.
            var gun = Assert.Single(battle.Items, item => item.OwnerUnitId == alien.Id);
            Assert.Equal(("GUN", "STR_RIGHT_HAND"), (gun.ItemType, gun.Slot));
            Assert.Equal("CLIP", battle.Items.Single(item => item.Id == gun.AmmoSlots[0]).ItemType);
        });
        Assert.Equal(BattleState.MaximumSoldierId, aliens.Min(static alien => alien.Id));
        Assert.Equal(("UFO_SCOUT", "RACE_TEST", 0, 0),
            (battle.ReinforcementsDeployment, battle.ReinforcementsRace, battle.ReinforcementsItemLevel, battle.AlienItemLevel));
    }

    [Fact]
    public void CiviliansOfAPositiveSpawnRankComeBeforeTheAliensAsVips()
    {
        var (campaign, _) = Requested(Rules, "UFO_CIVILIANS");
        Started(campaign);
        var battle = Battle(campaign);
        // civiliansByType rounds half the maximum up: one or two civilians, neutral VIPs on rank-4 nodes, numbered
        // before the alien, which takes another rank-4 node (a soldier's second choice).
        var civilians = battle.Units.Where(static unit => unit.Faction == BattleFaction.Neutral).ToArray();
        Assert.InRange(civilians.Length, 1, 2);
        var alien = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Hostile);
        Assert.All(civilians, civilian =>
        {
            Assert.Equal(("CIVILIAN", true, BattleControllerKind.Ai), (civilian.UnitType, civilian.IsVip, civilian.Controller.Kind));
            Assert.Equal(4, battle.Map.Nodes.Single(node => !node.IsDummy && node.Id == civilian.StartNodeId).Rank);
            Assert.True(civilian.Id < alien.Id);
        });
        Assert.Equal(4, battle.Map.Nodes.Single(node => !node.IsDummy && node.Id == alien.StartNodeId).Rank);
    }

    [Fact]
    public void MapBlockItemsTakeTheFirstItemIdsWithTheirFuseTimersAndLoadedAmmo()
    {
        var (campaign, _) = Requested(Rules, "UFO_ARMORY");
        Started(campaign);
        var battle = Battle(campaign);
        // loadMAP: the block's items (std::map order, fuse timers drawn from their range), the randomized items (one pick,
        // two of it), then the extended gun with its clip loaded at quantity 5; all before the X-COM items.
        Assert.Equal([(0, "GRENADE", (BattlePosition?)new BattlePosition(1, 1, 0), "STR_GROUND", 3), (1, "GRENADE", new BattlePosition(2, 1, 0), "STR_GROUND", 3),
                (2, "CLIP", new BattlePosition(3, 3, 0), "STR_GROUND", -1), (3, "CLIP", new BattlePosition(3, 3, 0), "STR_GROUND", -1),
                (4, "GUN", new BattlePosition(4, 4, 0), "STR_GROUND", -1), (5, "CLIP", null, "", -1)],
            battle.Items.Take(6).Select(static item => (item.Id, item.ItemType, item.Tile, item.Slot, item.FuseTimer)));
        Assert.Equal(5, battle.Items[4].AmmoSlots[0]);
        Assert.Equal((5, true, false), (battle.Items[5].AmmoQuantity, battle.Items[5].IsAmmo, battle.Items[5].XcomProperty));
        Assert.Equal(12, battle.Items[2].AmmoQuantity);
    }

    [Fact]
    public void ObjectivesCountTheTargetPartsPowerSourcesGetFuelAndTheWeaponPileGoesHome()
    {
        var (campaign, _) = Requested(Rules, "UFO_POWER", items: new Dictionary<string, int> { ["SUPPLY"] = 5 });
        Started(campaign);
        var battle = Battle(campaign);
        // setupObjectives: two power sources match objective type 2; one is required. The deployment's bug hunt turn
        // and VIP rules apply.
        Assert.Equal((2, 1, false, 7, 60, 2), (battle.ObjectiveType, battle.ObjectiveCount, battle.BughuntMode, battle.BughuntMinTurn,
            battle.VipSurvivalPercentage, battle.VipEscapeType));
        // fuelPowerSources: the alien fuel on each power source, in tile order.
        Assert.Equal([new BattlePosition(5, 5, 0), new BattlePosition(6, 5, 0)],
            battle.Items.Where(static item => item.ItemType == "CLIP").Select(static item => item.Tile!.Value));
        // noWeaponPile: what the soldier did not take leaves the craft inventory tile for guaranteed recovery.
        Assert.DoesNotContain(battle.Items, item => item.Tile == battle.CraftInventoryTile);
        Assert.NotEmpty(battle.GuaranteedRecoveredItems);
        Assert.All(battle.GuaranteedRecoveredItems, static item => Assert.Equal(("SUPPLY", (BattlePosition?)null, -1), (item.ItemType, item.Tile, item.FuseTimer)));
        Assert.Equal(5, battle.Items.Count(static item => item.ItemType == "SUPPLY") + battle.GuaranteedRecoveredItems.Count);
    }

    [Fact]
    public void MapGrenadesPrimedToExplodeAtTheStartRefuseUntilExplosionsExist()
    {
        var (campaign, _) = Requested(Rules, "UFO_JUNK");
        Assert.Equal(BattleGenerator.PrimedJunk, Refusal(campaign, new StartBattle()));
    }

    [Fact]
    public void TheStartingConditionSendsDisabledVehiclesAndItemsBackToBaseStorage()
    {
        var (campaign, _) = Requested(Rules, "UFO_GEAR", [new("VEHICLE", 25), new("VEHICLE_B", 0)],
            new Dictionary<string, int> { ["GRENADE"] = 2, ["SUPPLY"] = 1 });
        var before = campaign.Capture().Bases[0].Items;
        Started(campaign);
        var battle = Battle(campaign);
        // VEHICLE_B is forbidden; VEHICLE's clip ammo is, so it stays home too, with the clips it loads (its own clip size
        // of 0 makes that the clip's 30). The grenades are forbidden; the supply comes along.
        Assert.DoesNotContain(battle.Units, static unit => unit.UnitType.StartsWith("VEHICLE", StringComparison.Ordinal));
        Assert.Equal(new Dictionary<string, int> { ["VEHICLE"] = 1, ["VEHICLE_AMMO"] = 30, ["VEHICLE_B"] = 1, ["GRENADE"] = 2 },
            battle.ReturnedToBase.ToDictionary(static pair => pair.Key, static pair => pair.Value));
        Assert.DoesNotContain(battle.Items, static item => item.ItemType == "GRENADE");
        Assert.Single(battle.Items, static item => item.ItemType == "SUPPLY");
        var after = campaign.Capture().Bases[0].Items;
        foreach (var (type, quantity) in battle.ReturnedToBase)
            Assert.Equal(before.GetValueOrDefault(type) + quantity, after.GetValueOrDefault(type));
        // SavedBattleGame::getBaseStorageItems: the debriefing's record is the storage before anything went back.
        Assert.Equal(before.Where(static pair => pair.Value > 0).OrderBy(static pair => pair.Key, StringComparer.Ordinal),
            battle.BaseStorageItems.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
        Assert.Empty(battle.TakenFromBase);
    }

    private static Func<CampaignSnapshot, CampaignSnapshot> CustomDeployment(
        CraftDeploymentPoint? soldier, IReadOnlyList<CraftVehicleDeployment>? vehicles,
        IReadOnlyList<IReadOnlyList<int>>? shipOverride = null) => snapshot =>
    {
        // The base's first soldier is the one on board.
        var soldierId = snapshot.Bases[0].Soldiers[0].Id;
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                CustomSoldierDeployment = soldier is null ? craft.Logistics!.CustomSoldierDeployment
                    : new Dictionary<int, CraftDeploymentPoint> { [soldierId] = soldier },
                CustomVehicleDeployment = vehicles ?? craft.Logistics!.CustomVehicleDeployment,
            },
        });
        return shipOverride is null ? snapshot : snapshot with
        {
            CustomRuleCraftDeployments = new Dictionary<string, IReadOnlyList<IReadOnlyList<int>>> { ["SHIP"] = shipOverride },
        };
    };

    /// <summary>The SHIP rule with its own deployment point: the craft's corner, facing 7.</summary>
    private static readonly string RuleDeployment = Rules.Replace("  - type: SHIP\n", "  - type: SHIP\n    deployment: [[0, 0, 0, 7]]\n", StringComparison.Ordinal);

    private static BattlePosition CraftOrigin(BattleMap map, string craftType = "SHIP")
    {
        var craft = Enumerable.Range(0, 9).Select(cell => (X: cell % 3, Y: cell / 3)).Single(cell => map.TerrainName(cell.X, cell.Y) == craftType);
        return new(craft.X * 10, craft.Y * 10, 0);
    }

    [Fact]
    public void TheCraftsOwnCustomDeploymentPlacesSoldiersByIdAndVehiclesByTypeInOrder()
    {
        // Craft::_customSoldierDeployment and _customVehicleDeployment; the save's override for the craft type is ignored
        // while the craft has its own (BattlescapeGenerator::addXCOMUnit tries the craft's first).
        var (campaign, request) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE_B", 0), new("VEHICLE_B", 0)],
            adjust: CustomDeployment(new(3, 4, 0, 5),
                [new("VEHICLE", new(0, 0, 0, 0)), new("VEHICLE_B", new(7, 7, 0, 1)), new("VEHICLE_B", new(5, 1, 0, 2))],
                [[9, 9, 0, 3]]));
        Started(campaign);
        var battle = Battle(campaign);
        var origin = CraftOrigin(battle.Map);
        // Each vehicle takes the first unused point of its type.
        Assert.Equal([(BattleState.MaximumSoldierId, origin with { X = origin.X + 7, Y = origin.Y + 7 }, 1),
                (BattleState.MaximumSoldierId + 1, origin with { X = origin.X + 5, Y = origin.Y + 1 }, 2),
                (Assert.Single(request.Soldiers), origin with { X = origin.X + 3, Y = origin.Y + 4 }, 5)],
            battle.Units.Where(static unit => unit.Faction == BattleFaction.Player).Select(static unit => (unit.Id, unit.Position!.Value, unit.Direction)));
        // The first start tile checked is the craft inventory tile.
        Assert.Equal(origin with { X = origin.X + 7, Y = origin.Y + 7 }, battle.CraftInventoryTile);
    }

    [Fact]
    public void TheSavesCraftTypeDeploymentOverridesTheRuleAndAUnitThatFitsNoPointStaysOut()
    {
        // SavedBattleGame::getCustomDeployment: the save's points for SHIP, in order, ahead of the rule's own point. The
        // large vehicle does not fit at the craft's corner (its other tiles are not start tiles), so it takes the second
        // point; the soldier takes the first.
        var (campaign, request) = Requested(RuleDeployment, "UFO_CRAFTED", [new("VEHICLE", 0)],
            adjust: CustomDeployment(null, null, [[9, 9, 0, 3], [6, 6, 0, 4]]));
        Started(campaign);
        var battle = Battle(campaign);
        var origin = CraftOrigin(battle.Map);
        Assert.Equal([(BattleState.MaximumSoldierId, origin with { X = origin.X + 6, Y = origin.Y + 6 }, 4),
                (Assert.Single(request.Soldiers), origin with { X = origin.X + 9, Y = origin.Y + 9 }, 3)],
            battle.Units.Where(static unit => unit.Faction == BattleFaction.Player).Select(static unit => (unit.Id, unit.Position!.Value, unit.Direction)));

        // An empty override still replaces the start tiles: nobody is placed.
        (campaign, _) = Requested(Rules, "UFO_CRAFTED", adjust: CustomDeployment(null, null, []));
        Assert.Equal("Map generator encountered an error: no xcom units could be placed on the map.", Refusal(campaign, new StartBattle()));

        // BattleUnit::setDirection keeps a saved direction unchecked; the port refuses one outside 0-7.
        (campaign, _) = Requested(Rules, "UFO_CRAFTED", adjust: CustomDeployment(null, null, [[9, 9, 0, 8]]));
        Assert.Equal("A custom craft deployment faces direction 8, outside 0-7.", Refusal(campaign, new StartBattle()));
        (campaign, _) = Requested(Rules, "UFO_CRAFTED", adjust: CustomDeployment(new(3, 4, 0, -1), null));
        Assert.Equal("A custom craft deployment faces direction -1, outside 0-7.", Refusal(campaign, new StartBattle()));
    }

    [Fact]
    public void TheCraftRulesPointsApplyWithoutAnOverrideAndAReplacedCraftRuleIgnoresTheCraftsOwnDeployment()
    {
        // RuleCraft::getDeployment when the save has no override for the craft type.
        var (campaign, request) = Requested(RuleDeployment, "UFO_CRAFTED");
        Started(campaign);
        var battle = Battle(campaign);
        var soldier = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Player);
        Assert.Equal((Assert.Single(request.Soldiers), CraftOrigin(battle.Map), 7), (soldier.Id, soldier.Position!.Value, soldier.Direction));

        // addCraft's craftName replaces the battle's craft rules, so the craft's own deployment (made for SHIP) does not
        // apply: the soldier takes SHIP_B's first start tile.
        var replaced = Rules.Replace("crafts:\n", "crafts:\n  - type: SHIP_B\n    battlescapeTerrainData:\n      name: SHIP_B_MAP\n" +
                "      mapDataSets: [BLANKS, SHIPSET]\n      mapBlocks: [{name: SHIPBLOCK, width: 10, length: 10}]\n", StringComparison.Ordinal)
            .Replace("- {type: addCraft}", "- {type: addCraft, craftName: SHIP_B}", StringComparison.Ordinal);
        (campaign, _) = Requested(replaced, "UFO_CRAFTED", adjust: CustomDeployment(new(3, 4, 0, 5), null));
        Started(campaign);
        battle = Battle(campaign);
        soldier = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Player);
        Assert.Equal(CraftOrigin(battle.Map, "SHIP_B"), soldier.Position);
    }

    [Fact]
    public void CustomDeploymentsAreReadFromTheSaveAndWrittenBackAsTheyWere()
    {
        // Craft::load (customSoldierDeployment: std::map<int, std::pair<Position, int>>; customVehicleDeployment: type,
        // pos, dir with a missing pos at [-1, -1, -1]) and SavedGame::load (customRuleCraftDeployments).
        var (content, campaign) = StrategicLandingTests.Create();
        var soldier = campaign.Capture().Bases[0].Soldiers[0].Id;
        var yaml = System.Text.RegularExpressions.Regex.Replace(OxceSaveAdapter.EmitNewCampaign(campaign.Capture()),
            @"type: SHIP\n(\s+)id: 1\n", match => match.Value +
                $"{match.Groups[1].Value}customSoldierDeployment: {{{soldier}: [[1, 2, 0], 5]}}\n" +
                $"{match.Groups[1].Value}customVehicleDeployment: [{{type: VEHICLE, pos: [3, 4, 1], dir: 2}}, {{type: VEHICLE_B}}]\n");
        Assert.Contains("customVehicleDeployment", yaml, StringComparison.Ordinal);
        yaml += "customRuleCraftDeployments: {SHIP: [[1, 2, 0, 3], [4, 5, 0, 6]]}\n";
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, 17);
        var snapshot = loaded.Campaign.Capture();
        var ship = snapshot.Bases[0].Crafts.Single(static craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal(new CraftDeploymentPoint(1, 2, 0, 5), Assert.Single(ship.CustomSoldierDeployment, pair => pair.Key == soldier).Value);
        Assert.Equal([new("VEHICLE", new(3, 4, 1, 2)), new("VEHICLE_B", new(-1, -1, -1, 0))], ship.CustomVehicleDeployment);
        Assert.Equal([[1, 2, 0, 3], [4, 5, 0, 6]], snapshot.CustomRuleCraftDeployments["SHIP"]);
        // The port writes nothing of its own for them; the loaded nodes stay and read back the same.
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(snapshot, loaded.Source);
        foreach (var key in new[] { "customSoldierDeployment", "customVehicleDeployment", "customRuleCraftDeployments" })
            Assert.Contains(key, rewritten, StringComparison.Ordinal);
        Assert.Equivalent(snapshot, TestFixtures.LoadLogisticsSave(rewritten, content, 17).Campaign.Capture(), strict: true);
    }

    [Fact]
    public void ACustomCraftDeploymentWithoutAPointForAUnitIsRefused()
    {
        // The reference crashes on the missing soldier (std::map::at) and throws for a vehicle without an unused point.
        var (campaign, _) = Requested(Rules, "UFO_CRAFTED", adjust: CustomDeployment(null, [new("VEHICLE", new(0, 0, 0, 0))]));
        Assert.StartsWith("Craft SHIP has no custom deployment point for soldier", Refusal(campaign, new StartBattle()), StringComparison.Ordinal);
        (campaign, _) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE_B", 0)], adjust: CustomDeployment(null, [new("VEHICLE", new(0, 0, 0, 0))]));
        Assert.Equal("Unit generator encountered an error: custom craft deployment failed.", Refusal(campaign, new StartBattle()));
    }

    [Fact]
    public void ASoldierWithALayoutTakesItsLayoutWeaponLoadedWithItsLayoutAmmo()
    {
        var layout = new SoldierLayoutItem("GUN", "STR_RIGHT_HAND", 0, 0, ["CLIP", "", "", ""], -1, false);
        var (campaign, _) = Requested(Rules, "UFO_CRAFTED", items: new Dictionary<string, int> { ["GUN"] = 1, ["CLIP"] = 2 }, layout: [layout]);
        Started(campaign);
        var battle = Battle(campaign);
        var soldier = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Player);
        // placeItemByLayout: the gun goes to the layout's slot once the first ground clip loads into it; the extra clip
        // has no layout slot, and a soldier with a layout takes nothing in autoEquip.
        Assert.Equal([(0, "GUN", (int?)soldier.Id, "STR_RIGHT_HAND", (BattlePosition?)null), (1, "CLIP", null, "", null),
                (2, "CLIP", null, "STR_GROUND", battle.CraftInventoryTile)],
            battle.Items.Select(static item => (item.Id, item.ItemType, item.OwnerUnitId, item.Slot, item.Tile)));
        Assert.Equal(1, battle.Items[0].AmmoSlots[0]);
        Assert.True(battle.Items.All(static item => item.XcomProperty));
    }

    [Fact]
    public void ASoldierWithoutALayoutIsAutoEquippedFromTheLoadedGroundItems()
    {
        var (campaign, _) = Requested(Rules, "UFO_CRAFTED", items: new Dictionary<string, int> { ["GUN"] = 1, ["CLIP"] = 1, ["GRENADE"] = 1 });
        Started(campaign);
        var battle = Battle(campaign);
        var soldier = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Player);
        // loadWeapons loads the clip into the gun on the ground; autoEquip then hands out one item per pass, the heaviest
        // first: the loaded gun to the right hand, then the grenade to the section cheapest from the empty left hand.
        Assert.Equal([(0, "GUN", (int?)soldier.Id, "STR_RIGHT_HAND"), (1, "CLIP", null, ""), (2, "GRENADE", soldier.Id, "STR_BACK_PACK")],
            battle.Items.Select(static item => (item.Id, item.ItemType, item.OwnerUnitId, item.Slot)));
        Assert.Equal(1, battle.Items[0].AmmoSlots[0]);
        Assert.All(battle.Items, static item => Assert.Null(item.Tile));
    }

    [Theory]
    [InlineData("UFO_SUITED")]
    [InlineData("UFO_REPLACED")]
    public void TheEnvironmentOrTheStartingConditionChangesTheSoldiersBattleArmor(string deployment)
    {
        // RuleEnviroEffects::getArmorTransformation, or RuleStartingCondition::getArmorReplacement for a forbidden armor:
        // the battle unit wears the suit (a flyer); the geoscape soldier keeps its own armor.
        var (campaign, request) = Requested(Rules, deployment);
        Started(campaign);
        var soldier = Assert.Single(Battle(campaign).Units, static unit => unit.Faction == BattleFaction.Player);
        Assert.Equal(("SUIT", "ARMOR", 1), (soldier.ArmorId, soldier.OriginalArmorId, soldier.MovementType));
        var personal = campaign.Capture().Bases[0].Soldiers.Single(state => state.Id == Assert.Single(request.Soldiers)).Personal!;
        Assert.Equal("ARMOR", personal.Armor);
    }

    [Fact]
    public void EnviroEffectsDrawEachFactionsConditionAndTheBattleRecordsItsConditionAndEffects()
    {
        // SavedBattleGame::applyEnviroEffects draws RNG::percent(globalChance) for STR_FRIENDLY, STR_HOSTILE and
        // STR_NEUTRAL; a faction without a condition has the default chance of 100.
        var rules = Rules.Replace("    armorTransformations: {ARMOR: SUIT}\n",
            "    armorTransformations: {ARMOR: SUIT}\n    environmentalConditions:\n      STR_FRIENDLY: {globalChance: 100}\n" +
            "      STR_HOSTILE: {globalChance: 0}\n", StringComparison.Ordinal);
        var (campaign, _) = Requested(rules, "UFO_SUITED");
        Started(campaign);
        var battle = Battle(campaign);
        Assert.Equal(("ENV_SUIT", (true, false, true), ""), (battle.EnviroEffectsId, battle.EnvironmentalConditions, battle.StartingConditionId));
        // Without enviro effects nothing is drawn and every condition is off; the starting condition is recorded.
        (campaign, _) = Requested(Rules, "UFO_GEAR");
        Started(campaign);
        battle = Battle(campaign);
        Assert.Equal(("", (false, false, false), "SC_GEAR"), (battle.EnviroEffectsId, battle.EnvironmentalConditions, battle.StartingConditionId));
    }

    [Theory]
    [InlineData("    music: [DEPLOY_MUSIC]\n", "    music: [TERRAIN_MUSIC]\n", "DEPLOY_MUSIC")]
    [InlineData("", "    music: [TERRAIN_MUSIC]\n", "TERRAIN_MUSIC")]
    [InlineData("", "", "")]
    [InlineData("    music: [DEPLOY_A, DEPLOY_B, DEPLOY_C]\n", "", "DEPLOY_A|DEPLOY_B|DEPLOY_C")]
    public void TheBattlesMusicIsTheDeploymentsElseTheTerrains(string deploymentMusic, string terrainMusic, string expected)
    {
        // BattlescapeGenerator::setMusic: a pick from the deployment's music, else from the terrain's, else none.
        var (music, drawn) = Music(deploymentMusic, terrainMusic);
        Assert.Contains(music, expected.Split('|'));
        // The pick draws from the battle's stream (RNG::generate), even from a single track; no music draws nothing.
        var (_, silent) = Music("", "");
        Assert.Equal(expected.Length != 0, drawn != silent);
    }

    private static (string Music, ulong State) Music(string deploymentMusic, string terrainMusic)
    {
        var rules = Rules.Replace("    enviroEffects: ENV_SUIT\n", "    enviroEffects: ENV_SUIT\n" + deploymentMusic, StringComparison.Ordinal)
            .Replace("    mapDataSets: [BLANKS, TESTSET]\n", "    mapDataSets: [BLANKS, TESTSET]\n" + terrainMusic, StringComparison.Ordinal);
        var (campaign, _) = Requested(rules, "UFO_SUITED");
        Started(campaign);
        return (Battle(campaign).Music, campaign.Random.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARequestWithoutVehicleAmmoWhoseCraftDiffersIsRefused(bool craftGone)
    {
        // A request without recorded ammo takes the craft's only while the craft carries exactly its vehicles.
        var (campaign, _) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 25), new("VEHICLE_B", 0)], adjust: snapshot =>
            snapshot with
            {
                World = snapshot.World with
                {
                    DeploymentRequest = craftGone
                        ? snapshot.World.DeploymentRequest! with { VehicleAmmo = [], CraftId = 999 }
                        : snapshot.World.DeploymentRequest! with { VehicleAmmo = [], Vehicles = ["VEHICLE"] },
                },
            });
        Assert.Equal(Oxce.Gameplay.Battles.Generation.XcomDeployment.NoVehicleAmmo("VEHICLE"), Refusal(campaign, new StartBattle()));
    }

    [Fact]
    public void ARequestSavedWithoutVehicleAmmoTakesTheAmmoOfTheCraftsVehicles()
    {
        // Requests saved before they recorded vehicle ammo: the craft still carries the request's vehicles.
        var (campaign, _) = Requested(Rules, "UFO_CRAFTED", [new("VEHICLE", 25), new("VEHICLE_B", 0)], adjust: static snapshot =>
            snapshot with { World = snapshot.World with { DeploymentRequest = snapshot.World.DeploymentRequest! with { VehicleAmmo = [] } } });
        Started(campaign);
        Assert.Equal(25, Assert.Single(Battle(campaign).Items, static item => item.ItemType == "VEHICLE_AMMO").AmmoQuantity);
    }

    [Fact]
    public void TheCraftsItemsStartOnTheCraftInventoryTileAndALayoutlessSoldierTakesThem()
    {
        var (campaign, request) = Requested(Rules, "UFO_CRAFTED", items: new Dictionary<string, int> { ["SUPPLY"] = 2 });
        Assert.Equal(2, request.Items["SUPPLY"]);
        Started(campaign);
        var battle = Battle(campaign);
        // createItemForTile for each item of the craft (IDs in creation order, X-COM property); autoEquip then gives both
        // to the soldier, who has no layout: the belt, cheapest from the empty right hand, cell by cell.
        var soldier = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Player);
        Assert.Equal([(0, "SUPPLY", (int?)soldier.Id, "STR_BELT", 0, true), (1, "SUPPLY", soldier.Id, "STR_BELT", 1, true)],
            battle.Items.Select(static item => (item.Id, item.ItemType, item.OwnerUnitId, item.Slot, item.SlotX, item.XcomProperty)));
        Assert.Equal(2, battle.NextItemId);
    }

    [Fact]
    public void WithoutACraftMapTheSoldierSpawnsOnAnXcomNode()
    {
        var (campaign, request) = Requested(Rules);
        Started(campaign);
        var battle = Battle(campaign);
        // getSpawnNode: the X-COM nodes (rank 1) share the highest priority; the soldier takes one of them, with a random
        // facing, and its tile is the inventory tile.
        var soldier = Assert.Single(battle.Units, static unit => unit.Faction == BattleFaction.Player);
        Assert.Equal(Assert.Single(request.Soldiers), soldier.Id);
        var spawns = battle.Map.Nodes.Where(static node => !node.IsDummy && node.Rank == 1).Select(static node => node.Position).ToArray();
        Assert.Equal(2, spawns.Length);
        Assert.Contains(soldier.Position!.Value, spawns);
        Assert.Equal(soldier.Position, battle.CraftInventoryTile);
        Assert.InRange(soldier.Direction, 0, 7);
        Assert.Equal([soldier.Id], campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer)!.Units.Select(static unit => unit.Id));
    }

    [Fact]
    public void DigTunnelOpensTheMarkedEdgeBetweenTwoBlocks()
    {
        var (campaign, _) = Requested(Rules, "UFO_TUNNEL");
        Started(campaign);
        var map = Battle(campaign).Map;
        (int, int) Part(int x, int y, BattleTilePart part) => map.Part(map.TileIndex(new(x, y, 0)), part);
        // drillModules east across rows 3-6: the floor replacement (TESTSET 1) on the west block's last column, the north
        // wall replacement closing both sides, and the corner on the east block where it had no north wall.
        Assert.All(Enumerable.Range(3, 4), y => Assert.Equal((1, 1), Part(9, y, BattleTilePart.Floor)));
        Assert.Equal((0, 1), Part(9, 2, BattleTilePart.Floor));
        Assert.Equal((0, 1), Part(9, 7, BattleTilePart.Floor));
        Assert.Equal((0, 1), Part(9, 3, BattleTilePart.NorthWall));
        Assert.Equal((0, 1), Part(9, 7, BattleTilePart.NorthWall));
        Assert.Equal((0, 1), Part(10, 3, BattleTilePart.NorthWall));
        Assert.Equal((-1, -1), Part(10, 4, BattleTilePart.NorthWall));
        Assert.Equal((0, 1), Part(10, 4, BattleTilePart.Floor));
    }

    [Fact]
    public void VerticalLevelsStackTheGroundAFillerAndTheReservedCeiling()
    {
        var (campaign, _) = Requested(Rules, "UFO_LEVELS");
        Started(campaign);
        var map = Battle(campaign).Map;
        (int, int) Floor(int x, int z) => map.Part(map.TileIndex(new(x, 5, z)), BattleTilePart.Floor);
        // On each block: the ground (SMALL, one level), the ceiling reserved first (ROOF, height 1 from its rule),
        // then a middle level (SMALL) filling the one level left, and the ceiling loaded on top.
        foreach (var x in new[] { 5, 15 })
            Assert.Equal([(0, 1), (0, 1), (1, 1)], Enumerable.Range(0, 3).Select(z => Floor(x, z)));
        // Each level's routes form a segment after the ground's; the ground node gets a vertical link to the node above.
        var real = map.Nodes.Where(static node => !node.IsDummy).ToArray();
        Assert.Equal(8, real.Length);
        var upper = real.Where(static node => node.Position.Z == 1).ToArray();
        Assert.Equal(4, upper.Length);
        Assert.All(upper, node => Assert.True(node.Segment > 1));
        foreach (var lower in real.Where(static node => node.Position.Z == 0))
        {
            var above = Assert.Single(upper, node => node.Position.X == lower.Position.X && node.Position.Y == lower.Position.Y);
            Assert.Contains(above.Id, lower.Links);
            Assert.Contains(lower.Id, above.Links);
        }
    }

    [Theory]
    [InlineData("UFO_OVERRUN", "Map block MAPS/BIG.MAP (20x10) placed at (10, 0) runs past the 20x20 map.")]
    [InlineData("UFO_OVERRUN_SOUTH", "Map block MAPS/TALL.MAP (10x20) placed at (0, 10) runs past the 20x20 map.")]
    public void AVerticalLevelsBlockRunningPastTheMapsSideIsRefused(string deployment, string reason)
    {
        // On the east (or south) cell of a 20x20 map, the ground is SMALL and the ceiling names BIG, 20 wide (or TALL,
        // 20 long), by index: the reference's getTile returns null past the side and loadMAP dereferences it.
        var (campaign, _) = Requested(Rules, deployment);
        var before = campaign.Capture();
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new StartBattle()).Events));
        Assert.Equal((CampaignBattles.GenerationFailedCode, reason), (blocked.Code, blocked.Reason));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void AMapThatDoesNotFillIsRefusedAndLeavesTheCampaignAsItWas()
    {
        // "Map failed to fully generate." is an exception in the reference; the port refuses and puts its draws back.
        var (campaign, request) = Requested(Rules, "UFO_UNFILLED");
        var before = campaign.Capture();
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new StartBattle()).Events));
        Assert.Equal((CampaignBattles.GenerationFailedCode, "Map failed to fully generate."), (blocked.Code, blocked.Reason));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Equal(request, campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest());
        Assert.Null(campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer));
    }

    [Fact]
    public void ARequestWithoutAnAlienDeploymentOrTerrainFilesIsRefusedAndStaysPending()
    {
        // Mod::getDeployment(type, true) throws in the reference; the port refuses before any change.
        var (campaign, request) = Requested(null);
        Assert.Equal("", request.DeploymentId);
        var before = campaign.Capture();
        var blocked = Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new StartBattle()).Events));
        Assert.Equal((CampaignBattles.NoBattleDeployment.Code, CampaignBattles.NoBattleDeployment.Text), (blocked.Code, blocked.Reason));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.Equal(request, campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest());

        var (withRules, _) = Requested(Rules);
        withRules.BattleTerrainFiles = null;
        Assert.Equal(CampaignBattles.NoTerrainFiles.Text, Refusal(withRules, new StartBattle()));
    }

    /// <summary>
    /// S7: the battle validator (run on every generated battle) names a unit that is not on its tiles or could not stand
    /// there, a node outside the map or linking to no live node, and an item without exactly one holder.
    /// </summary>
    [Fact]
    public void TheValidatorNamesUnitsNodesAndItemsThatBreakTheBattlesInvariants()
    {
        var (campaign, _) = Requested(Rules, "UFO_ALIENS");
        Started(campaign);
        var battle = Battle(campaign);
        var map = battle.Map;
        battle.Validate();
        string Broken(Action breakIt, Action repair)
        {
            breakIt();
            try { return Assert.Throws<InvalidOperationException>(battle.Validate).Message; }
            finally
            {
                repair();
                battle.Validate();
            }
        }

        void Move(BattleUnit unit, BattlePosition to)
        {
            map.SetUnitAt(map.TileIndex(unit.Position!.Value), null);
            unit.Position = to;
            map.SetUnitAt(map.TileIndex(to), unit.Id);
        }

        // Units: off their tile, standing where setUnitPosition refuses them, or a tile naming a unit that is gone.
        var alien = battle.Units.First(static unit => unit.Faction == BattleFaction.Hostile);
        var at = alien.Position!.Value;
        var free = Positions(map).First(position => map.UnitAt(map.TileIndex(position)) is null &&
            Oxce.Gameplay.Battles.Generation.BattlePlacement.TryPosition(map, alien, position) == position);
        Assert.Equal($"Battle unit {alien.Id} is not on its tile {free}.",
            Broken(() => alien.Position = free, () => alien.Position = at));
        var refused = Positions(map).First(position => map.UnitAt(map.TileIndex(position)) is null &&
            Oxce.Gameplay.Battles.Generation.BattlePlacement.TryPosition(map, alien, position) is null);
        Assert.Equal($"Battle unit {alien.Id} cannot stand at {refused}.", Broken(() => Move(alien, refused), () => Move(alien, at)));
        Assert.Equal($"Tile {map.TileIndex(free)} names battle unit 4242, which is not in the battle.",
            Broken(() => map.SetUnitAt(map.TileIndex(free), 4242), () => map.SetUnitAt(map.TileIndex(free), null)));

        // Nodes: a link past the node list, to the culled node SMALL's RMP leaves as a dummy, or a value below -5.
        var node = map.Nodes.First(static node => !node.IsDummy && node.Links.Count != 0);
        var link = node.Links[0];
        var dummy = map.Nodes.ToList().FindIndex(static candidate => candidate.IsDummy);
        Assert.True(dummy >= 0);
        foreach (var broken in new[] { map.Nodes.Count, dummy, -6 })
            Assert.Equal($"Route node {node.Id} links to {broken}, which is no node of the map.",
                Broken(() => node.Links[0] = broken, () => node.Links[0] = link));

        // Items: the alien's gun also on a tile, carried by a missing unit or by nobody, its clip in a second weapon.
        var gun = battle.Items.First(item => item.OwnerUnitId == alien.Id && item.AmmoSlots[0] is not null);
        var clip = gun.AmmoSlots[0]!.Value;
        Assert.Equal($"Battle item {gun.Id} has 2 holders.", Broken(() => gun.Tile = at, () => gun.Tile = null));
        Assert.Equal($"Battle item {gun.Id} belongs to missing unit 4242.", Broken(() => gun.OwnerUnitId = 4242, () => gun.OwnerUnitId = alien.Id));
        Assert.Equal($"Battle item {gun.Id} has 0 holders.", Broken(() => gun.OwnerUnitId = null, () => gun.OwnerUnitId = alien.Id));
        var other = battle.Items.First(item => item.Id != gun.Id && item.Id != clip);
        var otherSlot = other.AmmoSlots[3];
        Assert.Equal($"Battle item {clip} has 2 holders.", Broken(() => other.AmmoSlots[3] = clip, () => other.AmmoSlots[3] = otherSlot));
        Assert.Equal($"Battle item {other.Id} is loaded with {gun.Id}, which is no loaded ammo of the battle.",
            Broken(() => other.AmmoSlots[3] = gun.Id, () => other.AmmoSlots[3] = otherSlot));
        Assert.Equal($"Battle item {gun.Id} is outside the map.", Broken(() =>
        {
            gun.OwnerUnitId = null;
            gun.Tile = new BattlePosition(map.Width, 0, 0);
        }, () =>
        {
            gun.Tile = null;
            gun.OwnerUnitId = alien.Id;
        }));
        var slot = gun.Slot;
        Assert.Equal($"Battle item {gun.Id} is carried in no inventory section.", Broken(() => gun.Slot = "", () => gun.Slot = slot));

        // Breaks the lists only add to, each on a battle of its own: a node at another index or outside the map, an item
        // listed twice or also in guaranteed recovery.
        static string Fresh(Action<BattleState> breakIt)
        {
            var (fresh, _) = Requested(Rules, "UFO_ALIENS");
            Started(fresh);
            var battle = Battle(fresh);
            breakIt(battle);
            return Assert.Throws<InvalidOperationException>(battle.Validate).Message;
        }
        Assert.Equal($"Route node 0 is at index {map.Nodes.Count}.", Fresh(static battle =>
            battle.Map.AddNode(new BattleNode(0, new BattlePosition(1, 1, 0), 0, 0, 0, 0, 0, 0, []))));
        Assert.Equal($"Route node {map.Nodes.Count} is outside the map.", Fresh(battle =>
            battle.Map.AddNode(new BattleNode(map.Nodes.Count, new BattlePosition(0, 0, map.Height), 0, 0, 0, 0, 0, 0, []))));
        Assert.Equal($"Battle item {gun.Id} is listed twice.", Fresh(battle => battle.AddItem(battle.Items.Single(item => item.Id == gun.Id))));
        Assert.Equal($"Battle item {gun.Id} is both on the map and in guaranteed recovery.", Fresh(battle =>
        {
            var item = battle.Items.Single(item => item.Id == gun.Id);
            battle.SendToLimbo([item]);
            battle.AddItem(item);
        }));
    }

    /// <summary>
    /// S7: loadRMP offsets every link up to 250 without checking the file's node count, so an RMP link past the node list
    /// (or to another block's culled node) is refused as content, the campaign left as it was.
    /// </summary>
    [Theory]
    [InlineData(200, "links to 200")]
    [InlineData(4, "links to 4")]
    public void AnRmpLinkToNoNodeOfTheMapIsRefused(byte link, string reason)
    {
        var (campaign, _) = Requested(Rules, "UFO_ALIENS");
        // The first SMALL block's first node links to a node index that does not exist (200), or past its own three nodes
        // to the second SMALL block's culled node (index 3 + 1).
        campaign.BattleTerrainFiles = TerrainFiles(files => files["ROUTES/SMALL.RMP"] =
            [.. RouteNode(9, 5, 0, [253, 251, link], rank: 4, priority: 1), .. RouteNode(12, 5, 0, []),
                .. RouteNode(2, 2, 0, [], rank: 1, priority: 1)]);
        var before = campaign.Capture();
        var refused = Refusal(campaign, new StartBattle());
        Assert.StartsWith("Route node ", refused, StringComparison.Ordinal);
        Assert.EndsWith($"{reason}, which is no node of the map.", refused, StringComparison.Ordinal);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static IEnumerable<BattlePosition> Positions(BattleMap map)
    {
        for (var z = 0; z < map.Height; z++)
            for (var y = 0; y < map.Length; y++)
                for (var x = 0; x < map.Width; x++) yield return new BattlePosition(x, y, z);
    }

    [Fact]
    public void WithoutARequestThereIsNoBattleToStart()
    {
        var (_, campaign) = StrategicLandingTests.Create(rules: Rules, files: RequiredFiles("BLANKS", "TESTSET"));
        Assert.Equal(WorldCommandReasons.NoDeploymentRequest, Refusal(campaign, new StartBattle()));
    }

    private static (int, int, int, int, int, int, int, int, int, string, string, int) Shape(RuntimeDeploymentBattle battle) =>
        (battle.Width, battle.Length, battle.Height, battle.TurnLimit, battle.ChronoTrigger, battle.CheatTurn, battle.Shade,
            battle.MinShade, battle.MaxShade, string.Join(',', battle.Terrains), battle.MapScript, battle.MaximumDepth);

    [Fact]
    public void DeploymentBattlePropertiesAreLinkedWithTheReferenceDefaults()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], Rules, files: RequiredFiles("BLANKS", "TESTSET"));
        var deployments = content.RuntimeRules.AlienDeployments;
        Assert.Equal((20, 20, 2, 15, 2, 12, 9, -1, -1, "TEST_TERRAIN", "", 0), Shape(deployments.GetRequiredValue("UFO_SCOUT").Battle));
        // AlienDeployment's constructor: no size, no turn limit, FORCE_LOSE, cheat turn 20, no shade overrides and
        // no map script of its own (the terrain's script applies).
        Assert.Equal((0, 0, 0, 0, 0, 20, -1, -1, -1, "", "", 0), Shape(deployments.GetRequiredValue("SITE_DEPLOY").Battle));
        Assert.Equal(Shape(RuntimeDeploymentBattle.Default), Shape(deployments.GetRequiredValue("SITE_DEPLOY").Battle));
        Assert.Equal("RESIZED", deployments.GetRequiredValue("UFO_RESIZED").Battle.MapScript);
        // MapScript::load: "blocks" clears the groups; frequencies and maximum uses follow the selection.
        var commands = content.RuntimeRules.Battle.MapScripts["RESIZED"].Commands;
        Assert.Equal((2, 2, 2, 4, 3), (commands[0].SizeX, commands[0].SizeY, commands[0].SizeZ, commands[3].Label, commands[2].Label));
        Assert.Equal([-3], commands[3].Conditionals);
        // RuleCraft and RuleUfo battlescapeTerrainData: terrains of their own, named by their "name".
        var ship = content.RuntimeRules.Crafts.GetRequiredValue("SHIP").BattlescapeTerrain!;
        Assert.Equal(("SHIP_MAP", "BLANKS,SHIPSET", "SHIPBLOCK"), (ship.Id, string.Join(',', ship.MapDataSets), Assert.Single(ship.MapBlocks).Name));
        var scout = content.RuntimeRules.Ufos.GetRequiredValue("UFO_SCOUT").BattlescapeTerrain!;
        Assert.Equal(("SCOUT_MAP", 20, "0,5"), (scout.Id, Assert.Single(scout.MapBlocks).Length, string.Join(',', scout.MapBlocks[0].Groups)));
        Assert.Null(content.RuntimeRules.Ufos.GetRequiredValue("UFO_HUNTER").BattlescapeTerrain);
        // AlienDeployment data: rank, quantities, outside chance and item sets by alien item level.
        var data = deployments.GetRequiredValue("UFO_SCOUT").Battle.Data;
        Assert.Equal((2, "", 1, 0, 3, 1, 0, 50), (data[0].AlienRank, data[0].CustomUnitType, data[0].LowQuantity, data[0].MediumQuantity,
            data[0].HighQuantity, data[0].DifficultyQuantity, data[0].ExtraQuantity, data[0].PercentageOutsideUfo));
        Assert.Equal(["ITEM_A", "ITEM_B|ITEM_C"], data[0].ItemSets.Select(static set => string.Join('|', set)));
        Assert.Equal(("VEHICLE", 0), (data[1].CustomUnitType, data[1].AlienRank));
        Assert.Null(deployments.GetRequiredValue("UFO_SCOUT").Battle.DataInvalid);
    }
}
