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
    private int[] _units = [];
    private string[,] _terrainNames = new string[0, 0];
    private string[,] _blockNames = new string[0, 0];
    private int[,] _reinforcements = new int[0, 0];
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
        _units = new int[tiles];
        Array.Fill(_units, NoUnit);
        _terrainNames = Fill(width / 10, length / 10, "");
        _blockNames = Fill(width / 10, length / 10, "");
        _reinforcements = new int[width / 10, length / 10];
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
    /// Tile::getTUCost: 0 for an empty part, an open UFO door (none is open on a new map) or a big wall of type 4 or more
    /// in the object part; otherwise the part's cost.
    /// </summary>
    public int TimeUnitCost(int tile, BattleTilePart part, int movementType) => MapData(tile, part) is not { } data ? 0
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

    /// <summary>SavedBattleGame::getFlattenedMapTerrainNames, per 10x10 block.</summary>
    public string TerrainName(int blockX, int blockY) => _terrainNames[blockX, blockY];

    /// <summary>SavedBattleGame::getFlattenedMapBlockNames, per 10x10 block.</summary>
    public string BlockName(int blockX, int blockY) => _blockNames[blockX, blockY];

    /// <summary>SavedBattleGame::getReinforcementsBlocks, per 10x10 block.</summary>
    public int ReinforcementBlock(int blockX, int blockY) => _reinforcements[blockX, blockY];

    public void SetBlockInfo(int blockX, int blockY, string terrain, string block, int reinforcements)
    {
        _terrainNames[blockX, blockY] = terrain;
        _blockNames[blockX, blockY] = block;
        _reinforcements[blockX, blockY] = reinforcements;
    }

    private static string[,] Fill(int width, int length, string value)
    {
        var result = new string[width, length];
        for (var x = 0; x < width; x++)
            for (var y = 0; y < length; y++) result[x, y] = value;
        return result;
    }
}
