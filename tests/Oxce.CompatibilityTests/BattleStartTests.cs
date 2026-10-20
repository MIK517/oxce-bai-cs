using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
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
    private const string Rules = """
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
          - type: UFO_RESIZED
            width: 30
            length: 30
            height: 2
            terrains: [TEST_TERRAIN]
            script: RESIZED
          - type: UFO_ROADS
            width: 30
            length: 30
            height: 2
            terrains: [TEST_TERRAIN]
            script: ROADS
          - type: UFO_CRAFTED
            width: 30
            length: 30
            height: 2
            terrains: [TEST_TERRAIN]
            script: CRAFTED
          - type: UFO_TUNNEL
            width: 20
            length: 10
            height: 2
            terrains: [TEST_TERRAIN]
            script: TUNNEL
          - type: UFO_LEVELS
            width: 20
            length: 10
            height: 3
            terrains: [TEST_TERRAIN]
            script: LEVELS
          - type: UFO_UNFILLED
            width: 20
            length: 20
            height: 2
            terrains: [TEST_TERRAIN]
            script: UNFILLED
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
            script: BASIC
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
          - type: UNFILLED
            commands:
              - {type: addBlock, groups: [1]}
        """;

    private static Files TerrainFiles() => new(new()
    {
        ["TERRAIN/BLANKS.MCD"] = Mcd(Record(), Record((42, 1))),
        ["TERRAIN/TESTSET.MCD"] = Mcd(Record((42, 2)), Record((42, 3))),
        // BIG has a floor on its top level only; SMALL has one level of TESTSET's first object.
        ["MAPS/BIG.MAP"] = Map(10, 20, 3, 0),
        ["ROUTES/BIG.RMP"] = [],
        ["MAPS/SMALL.MAP"] = Map(10, 10, 2),
        // A node on the east edge linking east (253), west (251) and to the second node, which lies outside the
        // block (x 12) and is culled; then an X-COM spawn node (rank 1, priority 1).
        ["ROUTES/SMALL.RMP"] = [.. RouteNode(9, 5, 0, [253, 251, 1], rank: 4), .. RouteNode(12, 5, 0, []),
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
        ["TERRAIN/UFOSET.MCD"] = Mcd(Record((42, 8))),
        ["MAPS/SHIPBLOCK.MAP"] = Map(10, 10, 2),
        ["ROUTES/SHIPBLOCK.RMP"] = RouteNode(4, 4, 0, []),
        ["MAPS/SCOUTBLOCK.MAP"] = Map(20, 10, 2),
        ["ROUTES/SCOUTBLOCK.RMP"] = [],
        ["ROUTES/CROSSING.RMP"] = [],
    });

    private static (CampaignState Campaign, CampaignDeploymentRequest Request) Requested(string? rules, string ufo = "UFO_SCOUT")
    {
        // The landing UFO is UFO_SCOUT: the deployment under test takes its name and the default one steps aside.
        if (rules is not null && ufo != "UFO_SCOUT")
            rules = rules.Replace("type: UFO_SCOUT\n    width", "type: UFO_SCOUT_DEFAULT\n    width", StringComparison.Ordinal)
                .Replace("type: " + ufo + "\n", "type: UFO_SCOUT\n", StringComparison.Ordinal);
        var (_, campaign) = StrategicLandingTests.Create(rules: rules, files: rules is null ? null : RequiredFiles("BLANKS", "TESTSET"));
        campaign.BattleTerrainFiles = TerrainFiles();
        var requested = AssertLandingRequested(campaign);
        var result = campaign.Execute(new AnswerLanding(requested.Confirmation, LandingAnswer.Yes));
        return (campaign, Assert.IsType<DeploymentRequested>(Assert.Single(result.Events)).Request);
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

        var started = Assert.IsType<BattleStarted>(Assert.Single(campaign.Execute(new StartBattle()).Events));
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
        Assert.IsType<BattleStarted>(Assert.Single(campaign.Execute(new StartBattle()).Events));
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
        Assert.IsType<BattleStarted>(Assert.Single(campaign.Execute(new StartBattle()).Events));
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
        Assert.IsType<BattleStarted>(Assert.Single(campaign.Execute(new StartBattle()).Events));
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
        var soldier = Assert.Single(battle.Units);
        var first = new BattlePosition(craft.X * 10, craft.Y * 10, 0);
        Assert.Equal((BattleFaction.Player, first, first, soldier.Id), (soldier.Faction, soldier.Position, battle.CraftInventoryTile, battle.SelectedUnitId));
        Assert.Equal(soldier.Id, map.UnitAt(map.TileIndex(first)));
        Assert.Equal(soldier.SoldierId, soldier.Id);
        Assert.Equal(BattleController.Human(0), soldier.Controller);
    }

    [Fact]
    public void WithoutACraftMapTheSoldierSpawnsOnAnXcomNode()
    {
        var (campaign, request) = Requested(Rules);
        Assert.IsType<BattleStarted>(Assert.Single(campaign.Execute(new StartBattle()).Events));
        var battle = Battle(campaign);
        // getSpawnNode: the X-COM nodes (rank 1) share the highest priority; the soldier takes one of them, with a random
        // facing, and its tile is the inventory tile.
        var soldier = Assert.Single(battle.Units);
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
        Assert.IsType<BattleStarted>(Assert.Single(campaign.Execute(new StartBattle()).Events));
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
        Assert.IsType<BattleStarted>(Assert.Single(campaign.Execute(new StartBattle()).Events));
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
    }
}
