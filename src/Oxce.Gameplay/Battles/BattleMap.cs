using Oxce.Gameplay.Battles.Terrain;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The battle map (ADR 0038): tiles as structure-of-arrays indexed <c>x + y * width + z * width * length</c>
/// (<c>SavedBattleGame::getTileIndex</c>), the loaded map data sets the tiles refer to, and the per-block bookkeeping
/// <c>SavedBattleGame</c> keeps (terrain and block names, reinforcement blocks).
/// </summary>
internal sealed class BattleMap
{
    public const int PartCount = 4;

    /// <summary>The most tiles the port accepts in one battle; reference maps stay far below.</summary>
    public const int MaximumTiles = 4_000_000;

    private readonly List<BattleMapDataSet> _dataSets;
    private int[] _ids = [];
    private int[] _sets = [];
    private byte[] _discovered = [];
    private byte[] _smoke = [];
    private byte[] _fire = [];
    private byte[] _doorFrames = [];
    private int[] _lastExplored = [];
    private int[] _units = [];
    private string[][] _terrainNames = [];
    private string[][] _blockNames = [];
    private int[][] _reinforcements = [];
    private readonly List<BattleNode> _nodes = [];

    public BattleMap(int width, int length, int height, IEnumerable<BattleMapDataSet>? dataSets = null)
    {
        _dataSets = [.. dataSets ?? []];
        Reset(width, length, height);
    }

    public int Width { get; private set; }
    public int Length { get; private set; }
    public int Height { get; private set; }
    public int TileCount => _ids.Length / PartCount;

    /// <summary>SavedBattleGame::getNodes, in ID order.</summary>
    public IReadOnlyList<BattleNode> Nodes => _nodes;

    public void AddNode(BattleNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes.Add(node);
    }

    /// <summary>SavedBattleGame::getMapDataSets: tile parts refer to these by index.</summary>
    public IReadOnlyList<BattleMapDataSet> DataSets => _dataSets;

    /// <summary>Whether a map of this size is within the port's bounds (sides of whole blocks are not required here).</summary>
    public static bool IsSupportedSize(int width, int length, int height) =>
        width is > 0 and <= BattleState.MaximumMapSide && length is > 0 and <= BattleState.MaximumMapSide &&
        height is > 0 and <= BattleState.MaximumMapHeight && (long)width * length * height <= MaximumTiles;

    /// <summary>SavedBattleGame::initMap: empty tiles of the new size and no nodes; the loaded data sets stay.</summary>
    public void Reset(int width, int length, int height)
    {
        _nodes.Clear();
        if (width < 0 || length < 0 || height < 0 || (long)width * length * height > MaximumTiles)
            throw new ArgumentOutOfRangeException(nameof(width), "The battle map size is outside the supported range.");
        Width = width;
        Length = length;
        Height = height;
        var tiles = width * length * height;
        _ids = new int[tiles * PartCount];
        _sets = new int[tiles * PartCount];
        Array.Fill(_ids, -1);
        Array.Fill(_sets, -1);
        _discovered = new byte[tiles];
        _smoke = new byte[tiles];
        _fire = new byte[tiles];
        _doorFrames = new byte[tiles];
        _lastExplored = new int[tiles * FactionCount];
        _units = new int[tiles];
        Array.Fill(_units, NoUnit);
        _terrainNames = Grid(width / 10, length / 10, "");
        _blockNames = Grid(width / 10, length / 10, "");
        _reinforcements = Grid(width / 10, length / 10, 0);
    }

    public int AddDataSet(BattleMapDataSet dataSet)
    {
        ArgumentNullException.ThrowIfNull(dataSet);
        _dataSets.Add(dataSet);
        return _dataSets.Count - 1;
    }

    public bool Contains(BattlePosition position) =>
        (uint)position.X < (uint)Width && (uint)position.Y < (uint)Length && (uint)position.Z < (uint)Height;

    public int TileIndex(BattlePosition position) => position.X + position.Y * Width + position.Z * Width * Length;

    /// <summary>The position of a tile index (Tile::getPosition), the inverse of <see cref="TileIndex"/>.</summary>
    public BattlePosition PositionOf(int tile) => new(tile % Width, tile / Width % Length, tile / (Width * Length));

    /// <summary>The tiles a unit of the armor size covers from its position, x outer and y inner as the reference loops.</summary>
    public static IEnumerable<BattlePosition> Footprint(BattlePosition position, int size)
    {
        for (var x = 0; x < size; x++)
            for (var y = 0; y < size; y++)
                yield return position with { X = position.X + x, Y = position.Y + y };
    }

    /// <summary>A tile part's map data ID and data set index (Tile::getMapData), or (-1, -1) when the part is empty.</summary>
    public (int Id, int Set) Part(int tile, BattleTilePart part) =>
        (_ids[tile * PartCount + (int)part], _sets[tile * PartCount + (int)part]);

    /// <summary>The terrain object in a tile part, or null.</summary>
    public BattleMapData? MapData(int tile, BattleTilePart part)
    {
        var (id, set) = Part(tile, part);
        return id < 0 || set < 0 ? null : _dataSets[set].Objects[id];
    }

    /// <summary>Tile::setMapData; (-1, -1) clears the part.</summary>
    public void SetPart(int tile, BattleTilePart part, int id, int set)
    {
        if (id >= 0 && ((uint)set >= (uint)_dataSets.Count || (uint)id >= (uint)_dataSets[set].Objects.Count))
            throw new InvalidOperationException($"Tile part refers to missing map data {id} in set {set}.");
        _ids[tile * PartCount + (int)part] = id < 0 ? -1 : id;
        _sets[tile * PartCount + (int)part] = id < 0 ? -1 : set;
    }

    private const int NoUnit = int.MinValue;

    /// <summary>Tile::getUnit: the ID of the unit standing on the tile, or null.</summary>
    public int? UnitAt(int tile) => _units[tile] == NoUnit ? null : _units[tile];

    public void SetUnitAt(int tile, int? unitId) => _units[tile] = unitId ?? NoUnit;

    /// <summary>MapData::getTUCost: walking, flying or sliding (movement types 0, 1, 2).</summary>
    public static int MovementCost(BattleMapData data, int movementType) => movementType switch
    {
        0 => data.TimeUnitsWalk,
        1 => data.TimeUnitsFly,
        2 => data.TimeUnitsSlide,
        _ => 0,
    };

    /// <summary>
    /// Tile::getTUCost: 0 for an empty part, an open UFO door or a big wall of type 4 or more in the object part; otherwise
    /// the part's cost.
    /// </summary>
    public int TimeUnitCost(int tile, BattleTilePart part, int movementType) => MapData(tile, part) is not { } data ? 0
        : data.IsUfoDoor && HasDoorFrame(tile, part) ? 0
        : part == BattleTilePart.ObjectPart && data.BigWall >= 4 ? 0
        : MovementCost(data, movementType);

    /// <summary>Tile's cached terrain level: the floor's, or the object's when lower (more negative is higher).</summary>
    public int TerrainLevel(int tile)
    {
        var level = MapData(tile, BattleTilePart.Floor)?.TerrainLevel ?? 0;
        return MapData(tile, BattleTilePart.ObjectPart) is { } item ? Math.Min(item.TerrainLevel, level) : level;
    }

    /// <summary>Tile::hasNoFloor: no floor (or a no-floor floor), unless the tile below is raised to the full height.</summary>
    public bool HasNoFloor(BattlePosition position)
    {
        var tile = TileIndex(position);
        var noFloor = MapData(tile, BattleTilePart.Floor) is not { } floor || floor.HasNoFloor;
        if (noFloor && position.Z > 0 && TerrainLevel(TileIndex(position with { Z = position.Z - 1 })) == -24) return false;
        return noFloor;
    }

    /// <summary>Tile::getFloorSpecialTileType (TILE, 0, without a floor).</summary>
    public int FloorSpecialType(int tile) => MapData(tile, BattleTilePart.Floor)?.SpecialType ?? 0;

    public bool IsDiscovered(int tile, BattleTilePart part) => (_discovered[tile] & (1 << (int)part)) != 0;

    /// <summary>Tile::setDiscovered: discovering a floor also discovers both walls.</summary>
    public void SetDiscovered(int tile, BattleTilePart part, bool discovered)
    {
        if (IsDiscovered(tile, part) == discovered) return;
        if (!discovered) _discovered[tile] = (byte)(_discovered[tile] & ~(1 << (int)part));
        else
        {
            _discovered[tile] |= (byte)(1 << (int)part);
            if (part == BattleTilePart.Floor)
                _discovered[tile] |= (1 << (int)BattleTilePart.WestWall) | (1 << (int)BattleTilePart.NorthWall);
        }
    }

    /// <summary>Discovered flags as the save holds them (Tile::loadBinary): each part set alone, without setDiscovered's walls.</summary>
    public void RestoreDiscovered(int tile, BattleTilePart part, bool discovered) => _discovered[tile] = discovered
        ? (byte)(_discovered[tile] | 1 << (int)part)
        : (byte)(_discovered[tile] & ~(1 << (int)part));

    /// <summary>Tile::getSmoke and getFire (Uint8).</summary>
    public int Smoke(int tile) => _smoke[tile];
    public int Fire(int tile) => _fire[tile];

    public void SetSmoke(int tile, int smoke) => _smoke[tile] = checked((byte)smoke);
    public void SetFire(int tile, int fire) => _fire[tile] = checked((byte)fire);

    /// <summary>
    /// Whether a wall's door frame is set (Tile's <c>currentFrame</c> of the part at 7, as loadBinary and the text load set
    /// it): a UFO door with the frame is open (Tile::isUfoDoorOpen); on other walls the frame has no effect here.
    /// </summary>
    public bool HasDoorFrame(int tile, BattleTilePart part) =>
        part is BattleTilePart.WestWall or BattleTilePart.NorthWall && (_doorFrames[tile] & 1 << (int)part) != 0;

    public void SetDoorFrame(int tile, BattleTilePart part, bool open)
    {
        if (part is not (BattleTilePart.WestWall or BattleTilePart.NorthWall))
            throw new ArgumentOutOfRangeException(nameof(part), "Only walls keep a door frame in the save.");
        _doorFrames[tile] = open ? (byte)(_doorFrames[tile] | 1 << (int)part) : (byte)(_doorFrames[tile] & ~(1 << (int)part));
    }

    /// <summary>Tile::isUfoDoorOpen: the part is a UFO door and its frame is set.</summary>
    public bool IsUfoDoorOpen(int tile, BattleTilePart part) => HasDoorFrame(tile, part) && MapData(tile, part) is { IsUfoDoor: true };

    private const int FactionCount = 3;

    /// <summary>Tile::getLastExplored: the turn the faction last explored the tile, 0 for never.</summary>
    public int LastExplored(int tile, BattleFaction faction) => _lastExplored[tile * FactionCount + FactionIndex(faction)];

    public void SetLastExplored(int tile, BattleFaction faction, int turn) => _lastExplored[tile * FactionCount + FactionIndex(faction)] = turn;

    private static int FactionIndex(BattleFaction faction) => Enum.IsDefined(faction) ? (int)faction
        : throw new ArgumentOutOfRangeException(nameof(faction));

    /// <summary>
    /// Tile::isVoid without the items: no part has map data and there is no smoke (fire, discovered flags and the
    /// explored turns do not count). The battle adds the items on the tile.
    /// </summary>
    public bool HasNoPartsOrSmoke(int tile)
    {
        for (var part = 0; part < PartCount; part++)
            if (_ids[tile * PartCount + part] >= 0) return false;
        return _smoke[tile] == 0;
    }

    /// <summary>SavedBattleGame::getFlattenedMapTerrainNames, per 10x10 block.</summary>
    public string TerrainName(int blockX, int blockY) => _terrainNames[blockX][blockY];

    /// <summary>SavedBattleGame::getFlattenedMapBlockNames, per 10x10 block.</summary>
    public string BlockName(int blockX, int blockY) => _blockNames[blockX][blockY];

    /// <summary>SavedBattleGame::getReinforcementsBlocks, per 10x10 block.</summary>
    public int ReinforcementBlock(int blockX, int blockY) => _reinforcements[blockX][blockY];

    public void SetBlockInfo(int blockX, int blockY, string terrain, string block, int reinforcements)
    {
        _terrainNames[blockX][blockY] = terrain;
        _blockNames[blockX][blockY] = block;
        _reinforcements[blockX][blockY] = reinforcements;
    }

    /// <summary>The three block grids whole, x outer and y inner (the save's form).</summary>
    public IReadOnlyList<IReadOnlyList<string>> TerrainNames => _terrainNames;
    public IReadOnlyList<IReadOnlyList<string>> BlockNames => _blockNames;
    public IReadOnlyList<IReadOnlyList<int>> ReinforcementBlocks => _reinforcements;

    /// <summary>
    /// The block grids as a save holds them (SavedBattleGame::load's tryRead): their shape is the save's, which the load
    /// does not check against the map; a reader past a row's end is the reference's undefined indexing.
    /// </summary>
    public void RestoreBlockGrids(IReadOnlyList<IReadOnlyList<string>> terrainNames, IReadOnlyList<IReadOnlyList<string>> blockNames,
        IReadOnlyList<IReadOnlyList<int>> reinforcements)
    {
        _terrainNames = [.. terrainNames.Select(static row => row.ToArray())];
        _blockNames = [.. blockNames.Select(static row => row.ToArray())];
        _reinforcements = [.. reinforcements.Select(static row => row.ToArray())];
    }

    private static T[][] Grid<T>(int width, int length, T value)
    {
        var result = new T[width][];
        for (var x = 0; x < width; x++)
        {
            result[x] = new T[length];
            Array.Fill(result[x], value);
        }
        return result;
    }
}
