using Oxce.Core.Geometry;
using Oxce.Core.Random;
using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Mods.Rulesets.TerrainDeployment;

namespace Oxce.Gameplay.Battles.Generation;

/// <summary>
/// <c>BattlescapeGenerator::generateMap</c> for terrain maps: map script commands placing terrain blocks, then the
/// level-0 floor fill. Content and random failures the reference throws on (a block that cannot be placed, a map that
/// does not fill, a malformed file) end generation with a reason (<see cref="MapGenerationException"/>); the caller
/// adopts nothing then.
/// </summary>
internal sealed class BattleMapGenerator
{
    private const string BaseDefenseMissionType = "STR_BASE_DEFENSE";
    private const int LandingZoneGroup = 1;

    private readonly RuntimeBattleRules _rules;
    private readonly RuntimeRuleCatalog _catalog;
    private readonly BattleTerrainLoader _loader;
    private readonly IRandomSource _random;
    private readonly string _missionType;
    private readonly RuntimeTerrain _terrain;
    private readonly RuntimeTerrain _globeTerrain;
    private readonly RuntimeTerrain _baseTerrain;
    private readonly BattleMap _map;
    private readonly Dictionary<RuntimeTerrain, int> _loadedTerrains = new(ReferenceEqualityComparer.Instance);
    private RuntimeMapBlock?[,] _blocks = new RuntimeMapBlock?[0, 0];
    private bool[,] _covered = new bool[0, 0];
    private int[,] _segments = new int[0, 0];
    private MapDirection[,] _drillMap = new MapDirection[0, 0];
    private int _blocksToDo;
    private int _markAsReinforcementsBlock;
    private readonly BattleMapInputs _inputs;
    private string _craftRuleId;
    private bool _craftDeployed;
    private RuntimeMapBlock? _craftMap;
    private (int X, int Y, int Width, int Length) _craftPosition;
    private int _craftZ;
    private RuntimeTerrain? _ufoTerrain;
    private string _ufoType = "";
    private readonly List<(RuntimeMapBlock Map, int X, int Y, int Width, int Length)> _ufoMaps = [];
    private readonly List<int> _ufoZ = [];
    private bool[,] _landingZone = new bool[0, 0];
    private readonly List<LevelState> _verticalLevels = [];
    private readonly List<(int X, int Y)> _placedBlockRects = [];
    private readonly List<(RuntimeMapBlock Block, int X, int Y, int Z)> _verticalLevelSegments = [];
    private RuntimeTerrain? _alternateTerrain;
    private readonly List<BattlePosition> _backupInventoryTiles = [];
    private readonly List<BattleMapItem> _items = [];
    private bool _generateFuel = true;
    private string _scriptId = "";

    private BattleMapGenerator(RuntimeRuleCatalog rules, BattleTerrainLoader loader, IRandomSource random, string missionType,
        RuntimeTerrain terrain, RuntimeTerrain globeTerrain, int width, int length, int height, BattleMapInputs inputs)
    {
        _inputs = inputs;
        _craftRuleId = inputs.CraftId;
        _catalog = rules;
        _rules = rules.Battle;
        _loader = loader;
        _random = random;
        _missionType = missionType;
        _terrain = terrain;
        _globeTerrain = globeTerrain;
        _baseTerrain = terrain;
        _map = new BattleMap(width, length, height);
    }

    /// <summary>Generates the terrain map for a battle, or throws <see cref="MapGenerationException"/> with the reason.</summary>
    public static BattleMapResult Generate(RuntimeRuleCatalog rules, BattleTerrainLoader loader, IRandomSource random, string missionType,
        RuntimeTerrain terrain, RuntimeTerrain globeTerrain, RuntimeMapScript script, int width, int length, int height,
        BattleMapInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(inputs);
        var generator = new BattleMapGenerator(rules, loader, random, missionType, terrain, globeTerrain, width, length, height, inputs);
        generator.Run(script);
        return new BattleMapResult(generator._map, generator._craftDeployed, generator._craftPosition.X, generator._craftPosition.Y,
            generator._craftZ, generator._craftRuleId, generator._backupInventoryTiles.AsReadOnly())
        {
            Items = generator._items.AsReadOnly(),
            GenerateFuel = generator._generateFuel,
        };
    }

    private void Run(RuntimeMapScript script)
    {
        _scriptId = script.Id;
        Initialize();
        // Load in the default terrain data.
        foreach (var set in LoadTerrain(_terrain)) _map.AddDataSet(set);
        _loadedTerrains[_terrain] = 0;
        if (_missionType == BaseDefenseMissionType)
            throw new MapGenerationException("Base defense maps are generated from the base layout (Phase 7 branch 1 S6).");

        var conditionals = new Dictionary<int, bool>();
        foreach (var command in script.Commands)
        {
            if (command.Label > 0 && conditionals.ContainsKey(command.Label))
                throw new MapGenerationException("Map generator encountered an error: multiple commands are sharing the same label.");
            conditionals[command.Label] = false;
            if (!ConditionsHold(command, conditionals)) continue;
            // A deployed craft runs only the commands for one of its groups.
            if (_craftDeployed && command.CraftGroups.Count != 0 && !command.CraftGroups.Any(CraftRule.Groups.Contains)) continue;
            // RNG::percent draws even for a certain chance.
            if (!RandomChance.Percent(_random, command.ExecutionChances)) continue;
            if (command.VerticalLevelsInvalid is { } invalidLevels)
                throw new MapGenerationException($"Map script verticalLevels are invalid: {invalidLevels}");
            var selection = new MapScriptSelection(command);
            // One success flag for all executions of the command (the reference's reference into the map).
            var success = false;
            for (var execution = 0; execution < command.Executions; execution++)
            {
                _alternateTerrain = null;
                var doLevels = PopulateVerticalLevels(command);
                var current = 0;
                // An addLine with levels starts from its line level, not the ground or the first level.
                if (doLevels && command.Type == MapScriptCommandType.AddLine)
                {
                    current = _verticalLevels.FindIndex(static level => level.Type == VerticalLevelType.Line);
                    if (current < 0)
                        throw new MapGenerationException(
                            "Map generator encountered an error: An addLine command with verticalLevels doesn't contain a level for the line.");
                }
                _placedBlockRects.Clear();
                var terrain = _terrain;
                if (command.RandomTerrain.Count != 0)
                    terrain = PickTerrain(command.RandomTerrain[_random.NextInclusive(0, command.RandomTerrain.Count - 1)]);
                _alternateTerrain = terrain;
                if (doLevels)
                {
                    // The reference dereferences the first level even when none applies to the command.
                    if (_verticalLevels.Count == 0)
                        throw new MapGenerationException("Map script verticalLevels leave no level for this command.");
                    selection.InitVerticalLevel(_verticalLevels[current].Blocks, _verticalLevels[current].Groups);
                    if (_verticalLevels[current].Terrain.Length != 0) terrain = PickTerrain(_verticalLevels[current].Terrain);
                }
                _markAsReinforcementsBlock = command.MarkAsReinforcementsBlock ? 1 : 0;
                success = Execute(command, selection, terrain, success, doLevels);
            }
            conditionals[command.Label] = success;
        }
        if (_blocksToDo != 0) throw new MapGenerationException("Map failed to fully generate.");
        var dataSetOffset = _map.DataSets.Count;
        LoadNodes();
        var craftDataSetOffset = 0;
        if (_ufoMaps.Count != 0 && _ufoTerrain is { } ufoTerrain)
        {
            foreach (var set in LoadTerrain(ufoTerrain))
            {
                _map.AddDataSet(set);
                craftDataSetOffset++;
            }
            for (var index = 0; index < _ufoMaps.Count; index++)
            {
                var (map, x, y, width, length) = _ufoMaps[index];
                LoadMap(map, x * 10, y * 10, _ufoZ[index], ufoTerrain, dataSetOffset, false, ufoIndex: index);
                LoadRoutes(map, x * 10, y * 10, _ufoZ[index], UfoSegment);
                MarkCraftArea(x, y, width, length, _ufoType, map.Name, UfoSegment);
            }
        }
        if (_craftMap is { } craftMap)
        {
            var craft = CraftRule;
            var craftTerrain = craft.BattlescapeTerrain!;
            foreach (var set in LoadTerrain(craftTerrain)) _map.AddDataSet(set);
            LoadMap(craftMap, _craftPosition.X * 10, _craftPosition.Y * 10, _craftZ, craftTerrain, dataSetOffset + craftDataSetOffset,
                craft.MapVisible, craft: true);
            LoadRoutes(craftMap, _craftPosition.X * 10, _craftPosition.Y * 10, _craftZ, CraftSegment);
            MarkCraftArea(_craftPosition.X, _craftPosition.Y, _craftPosition.Width, _craftPosition.Length, _craftRuleId, craftMap.Name, CraftSegment);
            for (var x = _craftPosition.X * 10 - 1; x <= _craftPosition.X * 10 + craftMap.Width; x++)
                for (var y = _craftPosition.Y * 10 - 1; y <= _craftPosition.Y * 10 + craftMap.Length; y++)
                    for (var z = _map.Height - 1; z >= _craftZ; z--)
                        if (_map.Contains(new BattlePosition(x, y, z)))
                            _map.SetDiscovered(_map.TileIndex(new BattlePosition(x, y, z)), BattleTilePart.Floor, craft.MapVisible);
        }
        FillLevelZeroFloors();
        AttachNodeLinks();
    }

    private static bool ConditionsHold(RuntimeMapScriptCommand command, Dictionary<int, bool> conditionals)
    {
        // Positive numbers need the labelled command to have succeeded, negative ones to have failed.
        foreach (var condition in command.Conditionals)
        {
            if (!conditionals.TryGetValue(Math.Abs(condition), out var succeeded))
                throw new MapGenerationException(
                    "Map generator encountered an error: conditional command expected a label that did not exist before this command.");
            if (condition > 0 && !succeeded || condition < 0 && succeeded) return false;
        }
        return true;
    }

    private bool Execute(RuntimeMapScriptCommand command, MapScriptSelection selection, RuntimeTerrain terrain, bool success, bool doLevels)
    {
        switch (command.Type)
        {
            case MapScriptCommandType.AddBlock:
                {
                    var block = selection.NextBlock(terrain, _random);
                    if (block is null || !SelectPosition(command.Rectangles, block, out var x, out var y)) return success;
                    var added = AddBlock(x, y, block, terrain);
                    if (added && doLevels)
                    {
                        _placedBlockRects.Add((x, y));
                        LoadVerticalLevels(command, selection, repopulate: false);
                    }
                    return added || success;
                }
            case MapScriptCommandType.FillArea:
                {
                    for (var block = selection.NextBlock(terrain, _random); block is not null; block = selection.NextBlock(terrain, _random))
                    {
                        // Fill area succeeds if even one block is added; every selected position gets its levels.
                        if (!SelectPosition(command.Rectangles, block, out var x, out var y)) break;
                        success = AddBlock(x, y, block, terrain) || success;
                        _placedBlockRects.Add((x, y));
                    }
                    if (doLevels && _placedBlockRects.Count != 0) LoadVerticalLevels(command, selection, repopulate: true);
                    return success;
                }
            case MapScriptCommandType.AddLine:
                {
                    var added = AddLine(command.Direction, command.Rectangles, terrain, command.VerticalGroup, command.HorizontalGroup,
                        command.CrossingGroup);
                    if (doLevels && added) LoadVerticalLevels(command, selection, repopulate: true);
                    return added;
                }
            case MapScriptCommandType.AddCraft:
                return AddCraftCommand(command, selection, terrain, doLevels) || success;
            case MapScriptCommandType.AddUfo:
                return AddUfoCommand(command, selection, terrain, doLevels) || success;
            case MapScriptCommandType.DigTunnel:
                DrillModules(command, terrain);
                // This command is fail-proof.
                return true;
            case MapScriptCommandType.CheckBlock:
                return CheckBlock(command, terrain, success);
            case MapScriptCommandType.RemoveBlock:
                return RemoveBlocks(command);
            case MapScriptCommandType.Resize:
                Resize(command);
                return success;
            default:
                throw new MapGenerationException(
                    $"Map script command {command.Type} is generated in Phase 7 branch 1 S3b.");
        }
    }

    /// <summary>BattlescapeGenerator::init: the block grid and tiles for the current size.</summary>
    private void Initialize() => Initialize(_map.Width, _map.Length, _map.Height);

    private void Initialize(int width, int length, int height)
    {
        _map.Reset(width, length, height);
        _blocks = new RuntimeMapBlock?[width / 10, length / 10];
        _covered = new bool[width / 10, length / 10];
        _segments = new int[width / 10, length / 10];
        _landingZone = new bool[width / 10, length / 10];
        _drillMap = new MapDirection[width / 10, length / 10];
        _verticalLevels.Clear();
        _verticalLevelSegments.Clear();
        _blocksToDo = width / 10 * (length / 10);
    }

    private bool Occupied(int x, int y) => _blocks[x, y] is not null || _covered[x, y];

    /// <summary>BattlescapeGenerator::selectPosition: an even pick among the free positions in the rectangles.</summary>
    private bool SelectPosition(IReadOnlyList<MapRectangle> rectangles, RuntimeMapBlock block, out int x, out int y) =>
        SelectPosition(rectangles, block.Width, block.Length, out x, out y);

    private bool SelectPosition(IReadOnlyList<MapRectangle> rectangles, int width, int length, out int x, out int y)
    {
        var mapWidth = _map.Width / 10;
        var mapLength = _map.Length / 10;
        var sizeX = width / 10;
        var sizeY = length / 10;
        IReadOnlyList<MapRectangle> available = rectangles.Count == 0 ? [new MapRectangle(0, 0, mapWidth, mapLength)] : rectangles;
        var valid = new List<(int X, int Y)>();
        foreach (var rectangle in available)
        {
            if (sizeX > rectangle.Width || sizeY > rectangle.Height) continue;
            for (var candidateX = rectangle.X; candidateX + sizeX <= rectangle.X + rectangle.Width && candidateX + sizeX <= mapWidth; candidateX++)
                for (var candidateY = rectangle.Y; candidateY + sizeY <= rectangle.Y + rectangle.Height && candidateY + sizeY <= mapLength; candidateY++)
                {
                    if (valid.Contains((candidateX, candidateY))) continue;
                    var free = true;
                    for (var checkX = candidateX; checkX != candidateX + sizeX; checkX++)
                        for (var checkY = candidateY; checkY != candidateY + sizeY; checkY++)
                            if (!InGrid(checkX, checkY) || Occupied(checkX, checkY)) free = false;
                    if (free) valid.Add((candidateX, candidateY));
                }
        }
        x = y = 0;
        if (valid.Count == 0) return false;
        (x, y) = valid[_random.NextInclusive(0, valid.Count - 1)];
        return true;
    }

    // A rectangle starting at a negative position reads outside the reference's grid; the port treats it as occupied.
    private bool InGrid(int x, int y) => (uint)x < (uint)_blocks.GetLength(0) && (uint)y < (uint)_blocks.GetLength(1);

    /// <summary>BattlescapeGenerator::addBlock, including its check that skips the block's last row.</summary>
    private bool AddBlock(int x, int y, RuntimeMapBlock block, RuntimeTerrain terrain)
    {
        var sizeX = (block.Width - 1) / 10;
        var sizeY = (block.Length - 1) / 10;
        for (var dx = 0; dx <= sizeX; dx++)
            for (var dy = 0; dy != sizeY; dy++)
                if (Occupied(x + dx, y + dy)) return false;
        for (var dx = 0; dx <= sizeX; dx++)
            for (var dy = 0; dy <= sizeY; dy++)
            {
                _map.SetBlockInfo(x + dx, y + dy, terrain.Id, block.Name, _markAsReinforcementsBlock);
                _covered[x + dx, y + dy] = true;
                _blocksToDo--;
            }
        // The south and east edges are marked for drilling; the far corner (or a single block) for both.
        for (var dx = 0; dx <= sizeX; dx++) _drillMap[x + dx, y + sizeY] = MapDirection.Vertical;
        for (var dy = 0; dy <= sizeY; dy++) _drillMap[x + sizeX, y + dy] = MapDirection.Horizontal;
        _drillMap[x + sizeX, y + sizeY] = MapDirection.Both;
        _blocks[x, y] = block;
        var visible = _missionType == BaseDefenseMissionType;
        LoadMap(block, x * 10, y * 10, 0, terrain, LoadExtraTerrain(terrain), visible);
        return true;
    }

    /// <summary>
    /// BattlescapeGenerator::addLine: a road across the map through a free position in the rectangles, along which every
    /// existing block must be of the crossing direction's group (up to 22 tries). Empty cells get a block of the line's
    /// group; blocks of the other road become a crossing in place (the block names stay as they were). Both directions
    /// draw the vertical line first and the horizontal one only if it succeeded.
    /// </summary>
    private bool AddLine(MapDirection direction, IReadOnlyList<MapRectangle> rectangles, RuntimeTerrain terrain, int verticalGroup,
        int horizontalGroup, int crossingGroup)
    {
        if (direction == MapDirection.Both)
        {
            if (!AddLine(MapDirection.Vertical, rectangles, terrain, verticalGroup, horizontalGroup, crossingGroup)) return false;
            AddLine(MapDirection.Horizontal, rectangles, terrain, verticalGroup, horizontalGroup, crossingGroup);
            return true;
        }
        var vertical = direction == MapDirection.Vertical;
        var comparator = vertical ? horizontalGroup : verticalGroup;
        var typeToAdd = vertical ? verticalGroup : horizontalGroup;
        var limit = vertical ? _map.Length / 10 : _map.Width / 10;
        var tries = 0;
        int roadX = 0, roadY = 0;
        var placed = false;
        while (!placed)
        {
            placed = SelectPosition(rectangles, 10, 10, out roadX, out roadY);
            for (var step = 0; placed && step < limit; step++)
            {
                var (x, y) = vertical ? (roadX, step) : (step, roadY);
                if (BlockAt(x, y) is { } existing && !existing.Groups.Contains(comparator)) placed = false;
            }
            if (tries++ > 20) return false;
        }
        for (var step = 0; step < limit; step++)
        {
            var (x, y) = vertical ? (roadX, step) : (step, roadY);
            if (BlockAt(x, y) is not { } existing)
            {
                var block = MapScriptSelection.RandomBlock(terrain, 10, 10, typeToAdd, force: true, _random)
                    ?? throw new MapGenerationException(
                        $"Map script command `addLine` failed (1). Terrain {terrain.Id}, block group {typeToAdd}, script {_scriptId}");
                AddBlock(x, y, block, terrain);
                if (!_placedBlockRects.Contains((x, y))) _placedBlockRects.Add((x, y));
            }
            else if (existing.Groups.Contains(comparator))
            {
                var crossing = MapScriptSelection.RandomBlock(terrain, 10, 10, crossingGroup, force: true, _random)
                    ?? throw new MapGenerationException(
                        $"Map script command `addLine` failed (2). Terrain {terrain.Id}, block group {crossingGroup}, script {_scriptId}");
                _blocks[x, y] = crossing;
                ClearModule(x * 10, y * 10, 10, 10);
                LoadMap(crossing, x * 10, y * 10, 0, terrain, LoadExtraTerrain(terrain), discovered: false);
                if (!_placedBlockRects.Contains((x, y))) _placedBlockRects.Add((x, y));
            }
        }
        return true;
    }

    /// <summary>
    /// BattlescapeGenerator::drillModules: on the tunnel level, opens every marked edge between two placed blocks across
    /// the rectangle's span (3, 3, 3, 3 without rectangles), clearing walls and walkable-free objects, laying the floor
    /// replacement and closing the sides with the wall and corner replacements. The replacements are the terrain's data
    /// sets; the port records them at the terrain's loaded offset (the reference writes the terrain-relative set index
    /// while pointing at the right object, so its IDs only agree for the deployment terrain).
    /// </summary>
    private void DrillModules(RuntimeMapScriptCommand command, RuntimeTerrain terrain)
    {
        var data = command.Tunnel ?? throw new MapGenerationException("Map script command digTunnel has no tunnelData.");
        if (data.Invalid is { } invalid) throw new MapGenerationException($"Map script digTunnel tunnelData is invalid: {invalid}");
        var offset = LoadExtraTerrain(terrain);
        (int Id, int Set)? Replacement(string type)
        {
            if (!data.Replacements.TryGetValue(type, out var replacement)) return null;
            if ((uint)replacement.Set >= (uint)terrain.MapDataSets.Count ||
                (uint)replacement.Entry >= (uint)_map.DataSets[offset + replacement.Set].Objects.Count)
                throw new MapGenerationException(
                    $"Map script digTunnel replacement {type} refers to missing object {replacement.Entry} of set {replacement.Set} in terrain {terrain.Id}.");
            return (replacement.Entry, offset + replacement.Set);
        }
        var westWall = Replacement("westWall");
        var northWall = Replacement("northWall");
        var corner = Replacement("corner");
        var floor = Replacement("floor");
        var rectangle = command.Rectangles.Count != 0 ? command.Rectangles[0] : new MapRectangle(3, 3, 3, 3);
        var level = data.Level;
        int Tile(int x, int y)
        {
            var position = new BattlePosition(x, y, level);
            if (!_map.Contains(position))
                throw new MapGenerationException($"Map script digTunnel reaches outside the map at {x}, {y}, {level}.");
            return _map.TileIndex(position);
        }
        void Set(int tile, BattleTilePart part, (int Id, int Set)? value) =>
            _map.SetPart(tile, part, value?.Id ?? -1, value?.Set ?? -1);
        void ClearUnwalkableObject(int tile)
        {
            if (_map.MapData(tile, BattleTilePart.ObjectPart) is { TimeUnitsWalk: 0 }) Set(tile, BattleTilePart.ObjectPart, null);
        }
        var blocksX = _map.Width / 10;
        var blocksY = _map.Length / 10;
        for (var i = 0; i < blocksX; i++)
            for (var j = 0; j < blocksY; j++)
            {
                if (!Occupied(i, j)) continue;
                if (command.Direction != MapDirection.Vertical && i < blocksX - 1 &&
                    _drillMap[i, j] is MapDirection.Horizontal or MapDirection.Both && Occupied(i + 1, j))
                {
                    // Drill east.
                    for (var k = rectangle.Y; k != rectangle.Y + rectangle.Height; k++)
                    {
                        if (!_map.Contains(new BattlePosition(i * 10 + 9, j * 10 + k, level))) continue;
                        var tile = Tile(i * 10 + 9, j * 10 + k);
                        Set(tile, BattleTilePart.WestWall, null);
                        Set(tile, BattleTilePart.ObjectPart, null);
                        if (floor is not null) Set(tile, BattleTilePart.Floor, floor);
                        var next = Tile((i + 1) * 10, j * 10 + k);
                        Set(next, BattleTilePart.WestWall, null);
                        ClearUnwalkableObject(next);
                    }
                    if (northWall is not null)
                    {
                        Set(Tile(i * 10 + 9, j * 10 + rectangle.Y), BattleTilePart.NorthWall, northWall);
                        Set(Tile(i * 10 + 9, j * 10 + rectangle.Y + rectangle.Height), BattleTilePart.NorthWall, northWall);
                    }
                    if (corner is not null)
                    {
                        var tile = Tile((i + 1) * 10, j * 10 + rectangle.Y);
                        if (_map.MapData(tile, BattleTilePart.NorthWall) is null) Set(tile, BattleTilePart.NorthWall, corner);
                    }
                }
                if (command.Direction != MapDirection.Horizontal && j < blocksY - 1 &&
                    _drillMap[i, j] is MapDirection.Vertical or MapDirection.Both && Occupied(i, j + 1))
                {
                    // Drill south.
                    for (var k = rectangle.X; k != rectangle.X + rectangle.Width; k++)
                    {
                        if (!_map.Contains(new BattlePosition(i * 10 + k, j * 10 + 9, level))) continue;
                        var tile = Tile(i * 10 + k, j * 10 + 9);
                        Set(tile, BattleTilePart.NorthWall, null);
                        Set(tile, BattleTilePart.ObjectPart, null);
                        if (floor is not null) Set(tile, BattleTilePart.Floor, floor);
                        var next = Tile(i * 10 + k, (j + 1) * 10);
                        Set(next, BattleTilePart.NorthWall, null);
                        ClearUnwalkableObject(next);
                    }
                    if (westWall is not null)
                    {
                        Set(Tile(i * 10 + rectangle.X, j * 10 + 9), BattleTilePart.WestWall, westWall);
                        Set(Tile(i * 10 + rectangle.X + rectangle.Width, j * 10 + 9), BattleTilePart.WestWall, westWall);
                    }
                    if (corner is not null)
                    {
                        var tile = Tile(i * 10 + rectangle.X, (j + 1) * 10);
                        if (_map.MapData(tile, BattleTilePart.WestWall) is null) Set(tile, BattleTilePart.WestWall, corner);
                    }
                }
            }
    }

    /// <summary>
    /// BattlescapeGenerator::populateVerticalLevels: copies of the command's levels in their working order: the first
    /// ground level (group 1 by default for a craft or UFO), then the fillers (the first craft level for a craft or UFO,
    /// used once; the first line level for addLine; every middle, empty and decoration level), then the first ceiling.
    /// False when the command has no levels.
    /// </summary>
    private bool PopulateVerticalLevels(RuntimeMapScriptCommand command)
    {
        _verticalLevels.Clear();
        if (command.VerticalLevels.Count == 0) return false;
        var craftCommand = command.Type is MapScriptCommandType.AddCraft or MapScriptCommandType.AddUfo;
        if (command.VerticalLevels.FirstOrDefault(static level => level.Type == VerticalLevelType.Ground) is { } ground)
        {
            var state = new LevelState(ground);
            if (craftCommand && state.Groups.Count == 0) state.Groups = [1];
            _verticalLevels.Add(state);
        }
        var fillerFound = false;
        foreach (var level in command.VerticalLevels)
            switch (level.Type)
            {
                case VerticalLevelType.Craft when craftCommand && !fillerFound:
                    _verticalLevels.Add(new LevelState(level) { MaximumRepeats = 1 });
                    fillerFound = true;
                    break;
                case VerticalLevelType.Line when command.Type == MapScriptCommandType.AddLine && !fillerFound:
                    _verticalLevels.Add(new LevelState(level));
                    fillerFound = true;
                    break;
                case VerticalLevelType.Middle or VerticalLevelType.Empty or VerticalLevelType.Decoration:
                    _verticalLevels.Add(new LevelState(level));
                    break;
            }
        if (command.VerticalLevels.FirstOrDefault(static level => level.Type == VerticalLevelType.Ceiling) is { } ceiling)
            _verticalLevels.Add(new LevelState(ceiling));
        return true;
    }

    /// <summary>
    /// BattlescapeGenerator::loadVerticalLevels: stacks levels on each placed position up to the command's height (the map
    /// height when unset or larger): the ground (or line) level already placed, the ceiling reserved first, then the
    /// fillers in turn while they fit (at most 20 misses). An addLine whose first level is not its line re-places the
    /// ground and loads the line block as a filler. A craft level raises the craft or UFO; the craft map itself loads after
    /// the script. Every loaded level becomes a route segment. A block's height is its MAP header's once read, else its
    /// rule's, as the reference's shared block keeps it.
    /// </summary>
    private void LoadVerticalLevels(RuntimeMapScriptCommand command, MapScriptSelection selection, bool repopulate,
        RuntimeMapBlock? craftMap = null)
    {
        var visible = _missionType == BaseDefenseMissionType;
        foreach (var (x, y) in _placedBlockRects.ToArray())
        {
            if (repopulate) PopulateVerticalLevels(command);
            if (_verticalLevels.Count == 0)
                throw new MapGenerationException("Map script verticalLevels leave no level for this command.");
            var block = BlockAt(x, y);
            var current = 0;
            var terrain = _alternateTerrain ?? _terrain;
            var zOffset = 0;
            var levelsLeft = command.SizeZ < 1 || command.SizeZ > _map.Height ? _map.Height : command.SizeZ;
            RuntimeMapBlock? lineBlock = null;
            if (command.Type == MapScriptCommandType.AddLine && _verticalLevels[0].Type != VerticalLevelType.Line)
            {
                lineBlock = _blocks[x, y];
                ClearModule(x * 10, y * 10, 10, 10);
                // Re-added by addBlock.
                _blocksToDo++;
                selection.InitVerticalLevel(_verticalLevels[0].Blocks, _verticalLevels[0].Groups);
                var groundTerrain = PickTerrain(_verticalLevels[0].Terrain);
                block = selection.NextBlock(groundTerrain, _random);
                if (block is not null) AddBlock(x, y, block, groundTerrain);
            }
            var z = LevelHeight(_verticalLevels[current], block);
            zOffset += z;
            levelsLeft -= z;
            if (_verticalLevels[current].Type is VerticalLevelType.Ground or VerticalLevelType.Line) _verticalLevels.RemoveAt(current);
            else current++;

            // Reserve the space for the ceiling (none when no level is left, where the reference reads past its list).
            RuntimeMapBlock? ceilingBlock = null;
            var ceilingTerrain = terrain;
            if (_verticalLevels.Count != 0 && _verticalLevels[^1].Type == VerticalLevelType.Ceiling)
            {
                var ceiling = _verticalLevels[^1];
                selection.InitVerticalLevel(ceiling.Blocks, ceiling.Groups);
                if (ceiling.Terrain.Length != 0) ceilingTerrain = PickTerrain(ceiling.Terrain);
                ceilingBlock = selection.NextBlock(ceilingTerrain, _random);
                if (ceilingBlock is not null)
                {
                    z = ceiling.SizeZ == -1 ? BlockHeight(ceilingBlock) : ceiling.SizeZ;
                    if (z > levelsLeft) ceilingBlock = null;
                    else
                    {
                        levelsLeft -= z;
                        _verticalLevels.RemoveAt(_verticalLevels.Count - 1);
                    }
                }
            }

            const int maximumTries = 20;
            var tries = 0;
            // The reference loops forever on levels that add no height; the port stops after this many rounds.
            for (var rounds = 0; _verticalLevels.Count > 0 && levelsLeft > 0 && tries <= maximumTries; rounds++)
            {
                if (rounds > MaximumLevelRounds)
                    throw new MapGenerationException("Map script verticalLevels repeat levels that add no height.");
                if (current >= _verticalLevels.Count) current = 0;
                var level = _verticalLevels[current];
                var levelTerrain = terrain;
                RuntimeMapBlock? levelBlock = null;
                z = 0;
                switch (level.Type)
                {
                    case VerticalLevelType.Craft:
                        if (command.Type == MapScriptCommandType.AddCraft && zOffset > _craftZ) _craftZ = zOffset;
                        else if (command.Type == MapScriptCommandType.AddUfo && zOffset > _ufoZ[^1]) _ufoZ[^1] = zOffset;
                        z = level.SizeZ == -1 ? BlockHeight(craftMap!) : level.SizeZ;
                        if (z > levelsLeft)
                            throw new MapGenerationException($"Map Generator has encountered an error: craft or ufo map{craftMap!.Name}could not be placed because either the map is too tall or the verticalLevels place it too high.");
                        break;
                    case VerticalLevelType.Empty:
                        z = level.SizeZ == -1 ? 1 : level.SizeZ;
                        break;
                    default:
                        if (level.Terrain.Length != 0) levelTerrain = PickTerrain(level.Terrain);
                        if (command.Type == MapScriptCommandType.AddLine && level.Type == VerticalLevelType.Line && lineBlock is not null)
                        {
                            levelBlock = lineBlock;
                        }
                        else
                        {
                            selection.InitVerticalLevel(level.Blocks, level.Groups);
                            levelBlock = selection.NextBlock(levelTerrain, _random);
                        }
                        if (levelBlock is not null) z = level.SizeZ == -1 ? BlockHeight(levelBlock) : level.SizeZ;
                        else tries++;
                        break;
                }
                if (z > levelsLeft)
                {
                    tries++;
                    continue;
                }
                if (levelBlock is not null)
                {
                    LoadMap(levelBlock, x * 10, y * 10, zOffset, levelTerrain, LoadExtraTerrain(levelTerrain), visible);
                    _verticalLevelSegments.Add((levelBlock, x, y, zOffset));
                }
                levelsLeft -= z;
                zOffset += z;
                if (--level.MaximumRepeats == 0) _verticalLevels.RemoveAt(current);
                else current++;
            }
            if (ceilingBlock is not null)
            {
                LoadMap(ceilingBlock, x * 10, y * 10, zOffset, ceilingTerrain, LoadExtraTerrain(ceilingTerrain), visible);
                _verticalLevelSegments.Add((ceilingBlock, x, y, zOffset));
            }
        }
    }

    private const int MaximumLevelRounds = 10_000;

    /// <summary>A level's height: its own size, or the block's when it has none (a missing block there is fatal in the reference).</summary>
    private int LevelHeight(LevelState level, RuntimeMapBlock? block) => level.SizeZ != -1 ? level.SizeZ
        : block is not null ? BlockHeight(block)
        : throw new MapGenerationException("Map script verticalLevels found no block for the first level.");

    /// <summary>MapBlock::getSizeZ: the MAP header's height once the block has been read, else the rule's.</summary>
    private int BlockHeight(RuntimeMapBlock block) => _loader.LoadedLevels(block.Name) ?? block.Height;

    /// <summary>A working copy of a vertical level; its repeats count down as it is used.</summary>
    private sealed class LevelState(RuntimeVerticalLevel level)
    {
        public VerticalLevelType Type { get; } = level.Type;
        public IReadOnlyList<int> Groups { get; set; } = level.Groups;
        public IReadOnlyList<int> Blocks { get; } = level.Blocks;
        public int SizeZ { get; } = level.SizeZ;
        public int MaximumRepeats { get; set; } = level.MaximumRepeats;
        public string Terrain { get; } = level.Terrain;
    }

    /// <summary>The checkBlock command: is there a block (of a group, or one of the listed blocks) in the rectangles?</summary>
    private bool CheckBlock(RuntimeMapScriptCommand command, RuntimeTerrain terrain, bool success)
    {
        foreach (var rectangle in command.Rectangles)
        {
            if (success) break;
            for (var x = rectangle.X; x != rectangle.X + rectangle.Width && x != _map.Width / 10 && !success; x++)
                for (var y = rectangle.Y; y != rectangle.Y + rectangle.Height && y != _map.Length / 10 && !success; y++)
                {
                    if (!InGrid(x, y)) continue;
                    var block = BlockAt(x, y);
                    if (command.Groups.Count != 0)
                    {
                        foreach (var group in command.Groups)
                        {
                            if (success) break;
                            success = block is not null && block.Groups.Contains(group);
                        }
                    }
                    else if (command.Blocks.Count != 0)
                    {
                        foreach (var index in command.Blocks)
                        {
                            if (success) break;
                            if ((uint)index < (uint)terrain.MapBlocks.Count) success = ReferenceEquals(_blocks[x, y], terrain.MapBlocks[index]);
                        }
                    }
                    else
                    {
                        success = block is not null;
                    }
                }
        }
        return success;
    }

    // The reference marks the cells a block covers with a dummy block in group 0 and no name.
    private static readonly RuntimeMapBlock Dummy = new("dummy", 10, 10, 4, [0], [], []);

    private RuntimeMapBlock? BlockAt(int x, int y) => _blocks[x, y] ?? (_covered[x, y] ? Dummy : null);

    /// <summary>
    /// BattlescapeGenerator::removeBlocks. With block indices the reference removes every placed block once any listed
    /// index is valid, whichever block it is; the port keeps that.
    /// </summary>
    private bool RemoveBlocks(RuntimeMapScriptCommand command)
    {
        var deleted = new List<(int X, int Y)>();
        foreach (var rectangle in command.Rectangles)
            for (var x = rectangle.X; x != rectangle.X + rectangle.Width && x != _map.Width / 10; x++)
                for (var y = rectangle.Y; y != rectangle.Y + rectangle.Height && y != _map.Length / 10; y++)
                {
                    if (!InGrid(x, y) || _blocks[x, y] is not { } block || deleted.Contains((x, y))) continue;
                    if (command.Groups.Count != 0 ? command.Groups.Any(block.Groups.Contains)
                        : command.Blocks.Count == 0 || command.Blocks.Any(index => (uint)index < (uint)_terrain.MapBlocks.Count))
                        deleted.Add((x, y));
                }
        foreach (var (x, y) in deleted)
        {
            var block = _blocks[x, y]!;
            ClearModule(x * 10, y * 10, block.Width, block.Length);
            for (var dx = x; dx != x + block.Width / 10; dx++)
                for (var dy = y; dy != y + block.Length / 10; dy++)
                {
                    _map.SetBlockInfo(dx, dy, "", "", 0);
                    _blocks[dx, dy] = null;
                    _covered[dx, dy] = false;
                    _blocksToDo++;
                    _verticalLevelSegments.RemoveAll(segment => segment.X == dx && segment.Y == dy);
                }
        }
        return deleted.Count != 0;
    }

    private void ClearModule(int x, int y, int sizeX, int sizeY)
    {
        for (var z = 0; z != _map.Height; z++)
            for (var dx = x; dx != x + sizeX; dx++)
                for (var dy = y; dy != y + sizeY; dy++)
                {
                    var tile = _map.TileIndex(new BattlePosition(dx, dy, z));
                    for (var part = BattleTilePart.Floor; part <= BattleTilePart.ObjectPart; part++) _map.SetPart(tile, part, -1, -1);
                }
    }

    /// <summary>The resize command, allowed only before any block is placed.</summary>
    private void Resize(RuntimeMapScriptCommand command)
    {
        if (_missionType == BaseDefenseMissionType)
            throw new MapGenerationException("Map Generator encountered an error: Base defense map cannot be resized.");
        if (_blocksToDo < _map.Width / 10 * (_map.Length / 10))
            throw new MapGenerationException("Map Generator encountered an error: One does not simply resize the map after adding blocks.");
        var width = command.SizeX > 0 && command.SizeX != _map.Width / 10 ? command.SizeX * 10 : _map.Width;
        var length = command.SizeY > 0 && command.SizeY != _map.Length / 10 ? command.SizeY * 10 : _map.Length;
        var height = command.SizeZ > 0 && command.SizeZ != _map.Height ? command.SizeZ : _map.Height;
        if (!BattleMap.IsSupportedSize(width, length, height))
            throw new MapGenerationException($"A resized battle map of {width}x{length}x{height} is outside the supported size.");
        // init(false) also forgets which terrains are loaded; their data sets stay in the battle.
        _loadedTerrains.Clear();
        Initialize(width, length, height);
    }

    private RuntimeCraftRule? Craft(string id) =>
        id.Length != 0 && _catalog.Crafts.TryGet(id, out var handle) ? _catalog.Crafts[handle].Value : null;

    private RuntimeUfoRule? Ufo(string id) =>
        id.Length != 0 && _catalog.Ufos.TryGet(id, out var handle) ? _catalog.Ufos[handle].Value : null;

    private const int CraftSegment = 1000;
    private const int UfoSegment = 2000;

    private RuntimeCraftRule CraftRule => Craft(_craftRuleId)
        ?? throw new MapGenerationException($"Craft {_craftRuleId} does not exist.");

    /// <summary>
    /// The addCraft command: the craft's rules may be swapped by the command's craft name and then by the starting
    /// condition's transformation of the current craft; a random block of its terrain lands on a free position, and
    /// its landing zone gets blocks from the command's selection. An unplaced craft fails the map unless the command
    /// has a label.
    /// </summary>
    private bool AddCraftCommand(RuntimeMapScriptCommand command, MapScriptSelection selection, RuntimeTerrain terrain, bool doLevels)
    {
        if (_craftRuleId.Length == 0) return false;
        var replacement = Craft(command.CraftName) is null ? null : command.CraftName;
        if (_inputs.StartingCondition is { } condition)
            replacement = condition.CraftTransformations.TryGetValue(_craftRuleId, out var transformed) ? transformed : replacement;
        if (replacement is not null && Craft(replacement) is not null) _craftRuleId = replacement;
        var craftTerrain = CraftRule.BattlescapeTerrain
            ?? throw new MapGenerationException($"Craft {_craftRuleId} has no battlescape terrain.");
        var craftMap = MapScriptSelection.RandomBlock(craftTerrain, 999, 999, 0, force: false, _random)
            ?? throw new MapGenerationException($"Craft {_craftRuleId} has no map block in group 0.");
        if (!PlaceCraft(craftMap, command, selection, terrain, out var position))
        {
            if (command.Label == 0)
                throw new MapGenerationException($"Map generator encountered an error: Craft (MapBlock: {craftMap.Name}) could not be placed on the map. You may try turning on 'save scumming' to work around this issue.");
            return false;
        }
        _craftMap = craftMap;
        _craftPosition = position;
        LoadLandingZone(position, terrain, doLevels);
        if (doLevels) LoadVerticalLevels(command, selection, repopulate: true, craftMap);
        _craftDeployed = true;
        return true;
    }

    /// <summary>
    /// The addUFO command: the command's UFO, else the battle's UFO, else the deployment's custom UFO. The UFO terrain
    /// found by an earlier command stays in use when a later one finds none, as in the reference.
    /// </summary>
    private bool AddUfoCommand(RuntimeMapScriptCommand command, MapScriptSelection selection, RuntimeTerrain terrain, bool doLevels)
    {
        if (Ufo(command.UfoName) is { } named)
        {
            _ufoTerrain = named.BattlescapeTerrain;
            _ufoType = command.UfoName;
        }
        else if (_inputs.UfoId.Length != 0 && Ufo(_inputs.UfoId) is { } battleUfo)
        {
            _ufoTerrain = battleUfo.BattlescapeTerrain;
            _ufoType = _inputs.UfoId;
        }
        else if (_inputs.CustomUfo.Length != 0)
        {
            var custom = Ufo(_inputs.CustomUfo)
                ?? throw new MapGenerationException($"UFO {_inputs.CustomUfo} does not exist.");
            _ufoTerrain = custom.BattlescapeTerrain;
            _ufoType = _inputs.CustomUfo;
        }
        if (_ufoTerrain is not { } ufoTerrain) return false;
        var ufoMap = MapScriptSelection.RandomBlock(ufoTerrain, 999, 999, 0, force: false, _random)
            ?? throw new MapGenerationException($"UFO {_ufoType} has no map block in group 0.");
        if (!PlaceCraft(ufoMap, command, selection, terrain, out var position))
        {
            if (command.Label == 0 && !command.CanBeSkipped)
                throw new MapGenerationException($"Map generator encountered an error: UFO (MapBlock: {ufoMap.Name}) could not be placed on the map. You may try turning on 'save scumming' to work around this issue.");
            return false;
        }
        _ufoMaps.Add((ufoMap, position.X, position.Y, position.Width, position.Length));
        LoadLandingZone(position, terrain, doLevels);
        _ufoZ.Add(0);
        if (doLevels) LoadVerticalLevels(command, selection, repopulate: true, ufoMap);
        return true;
    }

    /// <summary>
    /// BattlescapeGenerator::addCraft: a free position for the craft or UFO; each cell under it becomes landing zone
    /// and, when free, gets the next block of the command's selection (no dummy marks, no drill edges).
    /// </summary>
    private bool PlaceCraft(RuntimeMapBlock craftMap, RuntimeMapScriptCommand command, MapScriptSelection selection, RuntimeTerrain terrain,
        out (int X, int Y, int Width, int Length) position)
    {
        position = default;
        if (!SelectPosition(command.Rectangles, craftMap.Width, craftMap.Length, out var x, out var y)) return false;
        position = (x, y, craftMap.Width / 10, craftMap.Length / 10);
        // With levels, the landing zone comes from the first level (the ground).
        if (_verticalLevels.Count != 0) selection.InitVerticalLevel(_verticalLevels[0].Blocks, _verticalLevels[0].Groups);
        for (var dx = 0; dx < position.Width; dx++)
            for (var dy = 0; dy < position.Length; dy++)
            {
                _landingZone[x + dx, y + dy] = true;
                var block = selection.NextBlock(terrain, _random);
                if (block is null || Occupied(x + dx, y + dy)) continue;
                _map.SetBlockInfo(x + dx, y + dy, terrain.Id, block.Name, _markAsReinforcementsBlock);
                _blocks[x + dx, y + dy] = block;
                _blocksToDo--;
            }
        return true;
    }

    /// <summary>The landing zone blocks' maps, loaded as soon as the craft or UFO is placed.</summary>
    private void LoadLandingZone((int X, int Y, int Width, int Length) position, RuntimeTerrain terrain, bool doLevels)
    {
        for (var x = position.X; x < position.X + position.Width; x++)
            for (var y = position.Y; y < position.Y + position.Length; y++)
                if (_blocks[x, y] is { } block)
                {
                    LoadMap(block, x * 10, y * 10, 0, terrain, LoadExtraTerrain(terrain), _missionType == BaseDefenseMissionType);
                    if (doLevels) _placedBlockRects.Add((x, y));
                }
    }

    /// <summary>The block names and segments under a craft or UFO name it, not the terrain.</summary>
    private void MarkCraftArea(int x, int y, int width, int length, string type, string mapName, int segment)
    {
        for (var dx = 0; dx < width; dx++)
            for (var dy = 0; dy < length; dy++)
            {
                _map.SetBlockInfo(x + dx, y + dy, type, mapName, _map.ReinforcementBlock(x + dx, y + dy));
                _segments[x + dx, y + dy] = segment;
            }
    }

    /// <summary>BattlescapeGenerator::pickTerrain for a command's random terrain.</summary>
    private RuntimeTerrain PickTerrain(string name) => name switch
    {
        "baseTerrain" => _baseTerrain,
        "globeTerrain" => _globeTerrain,
        // A missing alternate terrain is logged and the deployment's terrain used instead.
        "" => _terrain,
        _ => _rules.Terrains.GetValueOrDefault(name) ?? _terrain,
    };

    /// <summary>BattlescapeGenerator::loadExtraTerrain: the data set offset of a terrain, loading it the first time.</summary>
    private int LoadExtraTerrain(RuntimeTerrain terrain)
    {
        if (_loadedTerrains.TryGetValue(terrain, out var offset)) return offset;
        offset = _map.DataSets.Count;
        foreach (var set in LoadTerrain(terrain)) _map.AddDataSet(set);
        _loadedTerrains[terrain] = offset;
        return offset;
    }

    private IReadOnlyList<BattleMapDataSet> LoadTerrain(RuntimeTerrain terrain)
    {
        if (_loader.TryLoadTerrain(terrain, out var sets) is { } reason) throw new MapGenerationException(reason);
        return sets!;
    }

    /// <summary>BattlescapeGenerator::loadMAP for a terrain block (no craft or UFO offsets).</summary>
    private void LoadMap(RuntimeMapBlock block, int offsetX, int offsetY, int offsetZ, RuntimeTerrain terrain, int dataSetOffset, bool discovered,
        bool craft = false, int ufoIndex = -1)
    {
        if (_loader.TryLoadBlock(block, out var data) is { } reason) throw new MapGenerationException(reason);
        var file = data!.Map;
        var path = $"MAPS/{block.Name}.MAP";
        if (file.Levels > _map.Height)
            throw new MapGenerationException($"Height of map {path} too big for this mission, block is {file.Levels}, expected: {_map.Height}");
        var x = offsetX;
        var y = offsetY;
        var z = offsetZ + file.Levels - 1;
        // A block placed on the ground sits on the highest level that already has a floor.
        for (var level = _map.Height - 1; level > 0; level--)
            if (_map.MapData(_map.TileIndex(new BattlePosition(x, y, level)), BattleTilePart.Floor) is not null && offsetZ == 0)
            {
                z += level;
                if (craft && _craftZ == 0) _craftZ = level;
                if (ufoIndex >= 0 && _ufoZ[ufoIndex] == 0) _ufoZ[ufoIndex] = level;
                break;
            }
        // Map block items (and the inventory pile) sit on the craft's or UFO's raised level.
        var itemZ = offsetZ;
        if (craft && itemZ == 0) itemZ += _craftZ;
        if (ufoIndex >= 0 && itemZ == 0) itemZ += _ufoZ[ufoIndex];
        if (block.CraftInventoryTile.Count >= 3)
            _backupInventoryTiles.Add(new BattlePosition(block.CraftInventoryTile[0] + offsetX, block.CraftInventoryTile[1] + offsetY,
                block.CraftInventoryTile[2] + itemZ));
        if (z > _map.Height - 1)
            throw new MapGenerationException(_missionType == BaseDefenseMissionType
                ? "Something is wrong with your base, check your log file for additional information."
                : "Something is wrong in your map definitions, craft/ufo map is too tall?");
        foreach (var record in file.Tiles)
        {
            var tile = _map.TileIndex(new BattlePosition(x, y, z));
            SetPart(tile, BattleTilePart.Floor, record.Floor, terrain, dataSetOffset);
            SetPart(tile, BattleTilePart.WestWall, record.WestWall, terrain, dataSetOffset);
            SetPart(tile, BattleTilePart.NorthWall, record.NorthWall, terrain, dataSetOffset);
            SetPart(tile, BattleTilePart.ObjectPart, record.ObjectPart, terrain, dataSetOffset);
            _map.SetDiscovered(tile, BattleTilePart.Floor, discovered || block.RevealedFloors.Contains(z));
            if (++x == file.Width + offsetX)
            {
                x = offsetX;
                y++;
            }
            if (y == file.Length + offsetY)
            {
                y = offsetY;
                z--;
            }
        }
        PlaceBlockItems(block, offsetX, offsetY, itemZ, file.Width, file.Length, file.Levels);
    }

    // BattleType BT_CORPSE and BattleFuseType BFT_NONE.
    private const int CorpseBattleType = 11;
    private const int NoFuse = -3;

    /// <summary>
    /// loadMAP's map block items: the block's items (with their fuse timer range), randomized items and extended items
    /// with their ammo. The random draws happen here, in the reference's order; the items are created once the battle
    /// exists, in this order, so they take the first item IDs as in the reference.
    /// </summary>
    private void PlaceBlockItems(RuntimeMapBlock block, int offsetX, int offsetY, int offsetZ, int width, int length, int levels)
    {
        if (block.ItemsInvalid is { } invalid) throw new MapGenerationException($"Map block {block.Name}: {invalid}");
        var items = block.Items;
        // A block with an items array means fuel is not deployed algorithmically.
        if (_generateFuel) _generateFuel = items.Items.Count == 0;
        BattlePosition Tile(RuntimeBlockPosition position, string kind, string type)
        {
            if (position.X >= width || position.Y >= length || position.Z >= levels)
                throw new MapGenerationException($"{kind} {type} is outside of map block {block.Name}, position: [{position.X},{position.Y},{position.Z}], " +
                    $"block size: [{width},{length},{levels}]");
            return new(position.X + offsetX, position.Y + offsetY, position.Z + offsetZ);
        }
        RuntimeItemRule Rule(string type, string kind)
        {
            if (!_catalog.Items.TryGet(type, out var handle)) throw new MapGenerationException($"Map block {block.Name} places unknown item '{type}'.");
            var rule = _catalog.Items[handle].Value;
            if (rule.BattleType == CorpseBattleType)
                throw new MapGenerationException($"Placing corpse items (battleType: 11) on the map is not allowed. {kind}: {type}, map block: {block.Name}");
            return rule;
        }
        int Fuse(int minimum, int maximum) => minimum <= maximum ? _random.NextInclusive(minimum, maximum)
            : throw new MapGenerationException($"Map block {block.Name} has a fuse timer range [{minimum}, {maximum}] that is reversed.");

        foreach (var item in items.Items)
        {
            Rule(item.Type, "Item");
            var fuse = items.FuseTimers.GetValueOrDefault(item.Type);
            foreach (var position in item.Positions)
                _items.Add(new(item.Type, Tile(position, "Item", item.Type), fuse is null ? null : Fuse(fuse.Minimum, fuse.Maximum)));
        }
        foreach (var random in items.RandomizedItems)
        {
            if (random.ItemList.Count == 0) continue;
            var type = random.ItemList[_random.NextInclusive(0, random.ItemList.Count - 1)];
            var rule = _catalog.Items.TryGet(type, out var first) ? _catalog.Items[first].Value
                : throw new MapGenerationException($"Map block {block.Name} places unknown item '{type}'.");
            for (var count = 0; count < random.Amount; count++)
            {
                if (random.Mixed)
                {
                    type = random.ItemList[_random.NextInclusive(0, random.ItemList.Count - 1)];
                    rule = Rule(type, "Item");
                }
                else Rule(type, "Item");
                var tile = Tile(random.Position, "Random item", type);
                int? fuse = _catalog.Battle.Equipment.Items[type].FuseType != NoFuse && random.FuseTimerMinimum > -1 &&
                    random.FuseTimerMaximum > -1 && random.FuseTimerMinimum <= random.FuseTimerMaximum
                    ? _random.NextInclusive(random.FuseTimerMinimum, random.FuseTimerMaximum) : null;
                _items.Add(new(type, tile, fuse));
            }
        }
        foreach (var extended in items.ExtendedItems)
        {
            var rule = Rule(extended.Type, "Item");
            var weaponWithAmmo = rule.BattleType is 1 or 3 && rule.CompatibleAmmo.Take(4).Any(static slot => slot.Count != 0);
            foreach (var position in extended.Positions)
            {
                var tile = Tile(position, "Extended item", extended.Type);
                int? fuse = extended.FuseTimerMinimum > -1 && extended.FuseTimerMaximum > -1 && extended.FuseTimerMinimum <= extended.FuseTimerMaximum
                    ? _random.NextInclusive(extended.FuseTimerMinimum, extended.FuseTimerMaximum) : null;
                var weapon = _items.Count;
                _items.Add(new(extended.Type, tile, fuse));
                var loaded = new HashSet<int>();
                foreach (var ammo in extended.Ammo)
                {
                    var ammoRule = Rule(ammo.Type, "Ammo item");
                    var placed = new BattleMapItem(ammo.Type, tile, null)
                    {
                        AmmoQuantity = ammo.Quantity > 0 && ammo.Quantity < ammoRule.ClipSize ? ammo.Quantity : null,
                    };
                    if (weaponWithAmmo)
                    {
                        var slot = SlotForAmmo(rule, ammo.Type);
                        if (slot == -1)
                            throw new MapGenerationException($"Ammo is not compatible. Weapon: {extended.Type}, ammo: {ammo.Type}, map block: {block.Name}");
                        if (!loaded.Add(slot))
                            throw new MapGenerationException($"Weapon is already loaded. Weapon: {extended.Type}, ammo: {ammo.Type}, map block: {block.Name}");
                        placed = placed with { LoadInto = weapon, Slot = slot };
                    }
                    _items.Add(placed);
                }
            }
        }
    }

    /// <summary>RuleItem::getSlotForAmmo.</summary>
    private int SlotForAmmo(RuntimeItemRule weapon, string ammo)
    {
        for (var slot = 0; slot < Math.Min(4, weapon.CompatibleAmmo.Count); slot++)
            foreach (var handle in weapon.CompatibleAmmo[slot])
                if (_catalog.Items.GetExternalId(handle) == ammo) return slot;
        return -1;
    }

    /// <summary>One MAP tile part: RuleTerrain::getMapData across the terrain's data sets, BLANKS 0 when past them.</summary>
    private void SetPart(int tile, BattleTilePart part, byte value, RuntimeTerrain terrain, int dataSetOffset)
    {
        if (value == 0) return;
        var id = (int)value;
        var set = dataSetOffset;
        var found = false;
        for (var index = 0; index < terrain.MapDataSets.Count; index++)
        {
            var count = _map.DataSets[dataSetOffset + index].Objects.Count;
            if (id < count)
            {
                found = true;
                break;
            }
            id -= count;
            set++;
        }
        if (!found)
        {
            // Someone at Microprose made an error in the map: the reference uses object 0 of the first set.
            id = 0;
            set = 0;
            if (_map.DataSets.Count == 0 || _map.DataSets[0].Objects.Count == 0)
                throw new MapGenerationException($"Terrain {terrain.Id} refers past its data sets and has no blank object to use.");
        }
        if (dataSetOffset > 0) _map.SetPart(tile, BattleTilePart.ObjectPart, -1, -1);
        _map.SetPart(tile, part, id, set);
    }

    /// <summary>
    /// BattlescapeGenerator::loadNodes: every placed block's routes, one segment per block in row order. Every cell gets
    /// the segment number current when it is visited, covered cells included.
    /// </summary>
    private void LoadNodes()
    {
        var segment = 0;
        for (var y = 0; y < _map.Length / 10; y++)
            for (var x = 0; x < _map.Width / 10; x++)
            {
                _segments[x, y] = segment;
                // The landing zone under a craft or UFO has no routes of its own while the craft is on the ground.
                if (_blocks[x, y] is { } block && !(block.Groups.Contains(LandingZoneGroup) && _landingZone[x, y] && _craftZ == 0))
                    LoadRoutes(block, x * 10, y * 10, 0, segment++);
            }
        foreach (var (block, x, y, z) in _verticalLevelSegments) LoadRoutes(block, x * 10, y * 10, z, segment++);
    }

    /// <summary>
    /// BattlescapeGenerator::loadRMP: nodes at the block's offset (z counted down from the block's MAP height), links
    /// offset by the nodes already loaded; a node outside the block is culled to a dummy and links to it end.
    /// </summary>
    private void LoadRoutes(RuntimeMapBlock block, int offsetX, int offsetY, int offsetZ, int segment)
    {
        if (_loader.TryLoadBlock(block, out var data) is { } reason) throw new MapGenerationException(reason);
        RmpRouteMap routes;
        try
        {
            routes = RmpRouteCodec.Decode(new BinaryDataReader(data!.Routes), block.Width, block.Length, data.Map.Levels,
                _map.Nodes.Count, new Position3(checked((short)offsetX), checked((short)offsetY), checked((short)offsetZ)), segment);
        }
        catch (InvalidDataException exception)
        {
            throw new MapGenerationException($"Invalid RMP file: ROUTES/{block.Name}.RMP: {exception.Message}");
        }
        foreach (var node in routes.Nodes)
            _map.AddNode(node.IsDummy ? BattleNode.Dummy() : new BattleNode(node.Id,
                new BattlePosition(node.Position.X, node.Position.Y, node.Position.Z), node.Segment, node.Type, node.Rank,
                node.Flags, node.Reserved, node.Priority, node.Links));
    }

    /// <summary>
    /// BattlescapeGenerator::attachNodeLinks. The first pass joins each node's edge links (north, east, south, west) to
    /// the matching inverse link of a node in the neighbouring segment. The second pass joins the same edges by block
    /// position and adds vertical links between nodes of the same block on adjacent levels within three tiles; its
    /// upward check compares with the map height rather than the top level, as the reference does.
    /// </summary>
    private void AttachNodeLinks()
    {
        var nodes = _map.Nodes;
        var blocksX = _map.Width / 10;
        var blocksY = _map.Length / 10;
        var lastSegment = 0;
        foreach (var value in _segments) lastSegment = Math.Max(lastSegment, value);
        ReadOnlySpan<int> directions = [-2, -3, -4, -5];
        ReadOnlySpan<int> inverted = [-4, -5, -2, -3];
        Span<int> neighbours = stackalloc int[4];
        foreach (var node in nodes)
        {
            if (node.IsDummy) continue;
            if (node.Segment > lastSegment) break;
            var segmentX = node.Position.X / 10;
            var segmentY = node.Position.Y / 10;
            neighbours[0] = segmentX == blocksX - 1 ? -1 : _segments[segmentX + 1, segmentY];
            neighbours[1] = segmentY == blocksY - 1 ? -1 : _segments[segmentX, segmentY + 1];
            neighbours[2] = segmentX == 0 ? -1 : _segments[segmentX - 1, segmentY];
            neighbours[3] = segmentY == 0 ? -1 : _segments[segmentX, segmentY - 1];
            for (var link = 0; link < node.Links.Count; link++)
                for (var direction = 0; direction < 4; direction++)
                {
                    if (node.Links[link] != directions[direction]) continue;
                    foreach (var other in nodes)
                    {
                        if (other.IsDummy || other.Segment != neighbours[direction]) continue;
                        for (var otherLink = 0; otherLink < other.Links.Count; otherLink++)
                            if (other.Links[otherLink] == inverted[direction])
                            {
                                other.Links[otherLink] = node.Id;
                                node.Links[link] = other.Id;
                            }
                    }
                }
        }

        foreach (var node in nodes)
        {
            if (node.IsDummy || node.Segment > lastSegment) continue;
            var nodeX = node.Position.X / 10;
            var nodeY = node.Position.Y / 10;
            var nodeZ = node.Position.Z;
            // In std::map order: -6 (down), -5 (west), -4 (south), -3 (east), -2 (north), -1 (up).
            var searches = new List<(int Direction, int DX, int DY, int DZ)>();
            if (nodeZ != 0) searches.Add((-6, 0, 0, -1));
            if (nodeX != 0) searches.Add((-5, -1, 0, 0));
            if (nodeY != blocksY - 1) searches.Add((-4, 0, 1, 0));
            if (nodeX != blocksX - 1) searches.Add((-3, 1, 0, 0));
            if (nodeY != 0) searches.Add((-2, 0, -1, 0));
            if (nodeZ != _map.Height) searches.Add((-1, 0, 0, 1));
            foreach (var (direction, dx, dy, dz) in searches)
            {
                var vertical = direction is -1 or -6;
                var linkIndex = node.Links.IndexOf(direction);
                if (linkIndex < 0 && !vertical) continue;
                foreach (var other in nodes)
                {
                    if (other.IsDummy) continue;
                    if (other.Position.X / 10 - dx != nodeX || other.Position.Y / 10 - dy != nodeY || other.Position.Z - dz != nodeZ) continue;
                    if (!vertical)
                        for (var otherLink = 0; otherLink < other.Links.Count; otherLink++)
                            if (Inverse(other.Links[otherLink]) is { } inverse && inverse == node.Links[linkIndex])
                            {
                                other.Links[otherLink] = node.Id;
                                node.Links[linkIndex] = other.Id;
                            }
                    if (vertical && Math.Abs(node.Position.X - other.Position.X) + Math.Abs(node.Position.Y - other.Position.Y) <= 3 &&
                        !other.Links.Contains(node.Id))
                    {
                        other.Links.Add(node.Id);
                        node.Links.Add(other.Id);
                    }
                }
            }
        }

        static int? Inverse(int link) => link switch { -2 => -4, -3 => -5, -4 => -2, -5 => -3, _ => null };
    }

    /// <summary>The special hack that fills empty level-0 floors with the scorched earth tile (BLANKS object 1, set 0).</summary>
    private void FillLevelZeroFloors()
    {
        for (var x = 0; x < _map.Width; x++)
            for (var y = 0; y < _map.Length; y++)
            {
                var tile = _map.TileIndex(new BattlePosition(x, y, 0));
                if (_map.MapData(tile, BattleTilePart.Floor) is not null) continue;
                if (_map.DataSets.Count == 0 || _map.DataSets[0].Objects.Count < 2)
                    throw new MapGenerationException("The map has an empty floor and its first data set has no scorched earth object.");
                _map.SetPart(tile, BattleTilePart.Floor, 1, 0);
            }
    }
}

/// <summary>
/// What the battle brings to the map besides its terrain: the landing craft (empty for none), the target UFO (empty for
/// none), the deployment's custom UFO and the starting condition.
/// </summary>
internal sealed record BattleMapInputs(string CraftId, string UfoId, string CustomUfo, RuntimeStartingCondition? StartingCondition)
{
    /// <summary>The battle's UFO crashed (Ufo::CRASHED): its power sources explode at the start.</summary>
    public bool UfoCrashed { get; init; }
}

/// <summary>
/// The generated map and what the unit deployment needs from the generation: whether and where the craft landed (block
/// cells and level), which craft's rules it ended with, and the map blocks' inventory tiles.
/// </summary>
internal sealed record BattleMapResult(BattleMap Map, bool CraftDeployed, int CraftX, int CraftY, int CraftZ, string CraftRuleId,
    IReadOnlyList<BattlePosition> BackupInventoryTiles)
{
    /// <summary>The map blocks' items, in the order loadMAP created them.</summary>
    public IReadOnlyList<BattleMapItem> Items { get; init; } = [];

    /// <summary>BattlescapeGenerator::_generateFuel: false once a loaded block had an items array.</summary>
    public bool GenerateFuel { get; init; } = true;
}

/// <summary>
/// A map block item to create on a tile: its fuse timer and ammo quantity when set, and the earlier item (and slot) it is
/// loaded into, or -1.
/// </summary>
internal sealed record BattleMapItem(string ItemType, BattlePosition Tile, int? FuseTimer)
{
    public int? AmmoQuantity { get; init; }
    public int LoadInto { get; init; } = -1;
    public int Slot { get; init; }
}

/// <summary>A reason battle generation stopped (the reference throws or crashes there).</summary>
internal sealed class MapGenerationException(string reason) : Exception(reason);
