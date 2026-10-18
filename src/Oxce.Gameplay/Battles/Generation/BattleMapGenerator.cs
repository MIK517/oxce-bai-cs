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
    private int _blocksToDo;
    private int _markAsReinforcementsBlock;
    private string _scriptId = "";

    private BattleMapGenerator(RuntimeBattleRules rules, BattleTerrainLoader loader, IRandomSource random, string missionType,
        RuntimeTerrain terrain, RuntimeTerrain globeTerrain, int width, int length, int height)
    {
        _rules = rules;
        _loader = loader;
        _random = random;
        _missionType = missionType;
        _terrain = terrain;
        _globeTerrain = globeTerrain;
        _baseTerrain = terrain;
        _map = new BattleMap(width, length, height);
    }

    /// <summary>Generates the terrain map for a battle, or throws <see cref="MapGenerationException"/> with the reason.</summary>
    public static BattleMap Generate(RuntimeBattleRules rules, BattleTerrainLoader loader, IRandomSource random, string missionType,
        RuntimeTerrain terrain, RuntimeTerrain globeTerrain, RuntimeMapScript script, int width, int length, int height)
    {
        ArgumentNullException.ThrowIfNull(script);
        var generator = new BattleMapGenerator(rules, loader, random, missionType, terrain, globeTerrain, width, length, height);
        generator.Run(script);
        return generator._map;
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
            // RNG::percent draws even for a certain chance.
            if (!RandomChance.Percent(_random, command.ExecutionChances)) continue;
            if (command.HasVerticalLevels)
                throw new MapGenerationException("Map script vertical levels are generated in Phase 7 branch 1 S3b.");
            var selection = new MapScriptSelection(command);
            // One success flag for all executions of the command (the reference's reference into the map).
            var success = false;
            for (var execution = 0; execution < command.Executions; execution++)
            {
                var terrain = _terrain;
                if (command.RandomTerrain.Count != 0)
                    terrain = PickTerrain(command.RandomTerrain[_random.NextInclusive(0, command.RandomTerrain.Count - 1)]);
                _markAsReinforcementsBlock = command.MarkAsReinforcementsBlock ? 1 : 0;
                success = Execute(command, selection, terrain, success);
            }
            conditionals[command.Label] = success;
        }
        if (_blocksToDo != 0) throw new MapGenerationException("Map failed to fully generate.");
        LoadNodes();
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

    private bool Execute(RuntimeMapScriptCommand command, MapScriptSelection selection, RuntimeTerrain terrain, bool success)
    {
        switch (command.Type)
        {
            case MapScriptCommandType.AddBlock:
                {
                    var block = selection.NextBlock(terrain, _random);
                    return block is not null && SelectPosition(command.Rectangles, block, out var x, out var y) && AddBlock(x, y, block, terrain)
                        || success;
                }
            case MapScriptCommandType.FillArea:
                {
                    for (var block = selection.NextBlock(terrain, _random); block is not null; block = selection.NextBlock(terrain, _random))
                    {
                        // Fill area succeeds if even one block is added.
                        if (!SelectPosition(command.Rectangles, block, out var x, out var y)) break;
                        success = AddBlock(x, y, block, terrain) || success;
                    }
                    return success;
                }
            case MapScriptCommandType.AddLine:
                return AddLine(command.Direction, command.Rectangles, terrain, command.VerticalGroup, command.HorizontalGroup,
                    command.CrossingGroup);
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
            }
            else if (existing.Groups.Contains(comparator))
            {
                var crossing = MapScriptSelection.RandomBlock(terrain, 10, 10, crossingGroup, force: true, _random)
                    ?? throw new MapGenerationException(
                        $"Map script command `addLine` failed (2). Terrain {terrain.Id}, block group {crossingGroup}, script {_scriptId}");
                _blocks[x, y] = crossing;
                ClearModule(x * 10, y * 10, 10, 10);
                LoadMap(crossing, x * 10, y * 10, 0, terrain, LoadExtraTerrain(terrain), discovered: false);
            }
        }
        return true;
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
    private void LoadMap(RuntimeMapBlock block, int offsetX, int offsetY, int offsetZ, RuntimeTerrain terrain, int dataSetOffset, bool discovered)
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
                break;
            }
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
                if (_blocks[x, y] is { } block) LoadRoutes(block, x * 10, y * 10, 0, segment++);
            }
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

/// <summary>A reason battle generation stopped (the reference throws or crashes there).</summary>
internal sealed class MapGenerationException(string reason) : Exception(reason);
