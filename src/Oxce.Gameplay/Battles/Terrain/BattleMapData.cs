using Oxce.Formats.Terrain;

namespace Oxce.Gameplay.Battles.Terrain;

/// <summary>TilePart: the four parts of a tile, in MAP record order.</summary>
public enum BattleTilePart
{
    Floor = 0,
    WestWall = 1,
    NorthWall = 2,
    ObjectPart = 3,
}

/// <summary>
/// One terrain object (MapData), read from an MCD record and patched by the data set's MCD patch. Immutable once its
/// data set has loaded.
/// </summary>
public sealed class BattleMapData
{
    public const int SpriteCount = 8;
    public const int LoftLayers = 12;
    public const int BlockValues = 6;

    private readonly int[] _sprites = new int[SpriteCount];
    private readonly int[] _lofts = new int[LoftLayers];
    private readonly int[] _block = new int[BlockValues];

    /// <summary>MapDataSet::loadData's field mapping from one MCD record.</summary>
    internal BattleMapData(McdTerrainRecord record)
    {
        var frames = record.Frames.Span;
        for (var frame = 0; frame < SpriteCount; frame++) _sprites[frame] = frames[frame];
        YOffset = record.PositionLevel;
        SpecialType = record.TargetType;
        ObjectType = (BattleTilePart)record.TileType;
        TimeUnitsWalk = record.TimeUnitsWalk;
        TimeUnitsFly = record.TimeUnitsFly;
        TimeUnitsSlide = record.TimeUnitsSlide;
        IsUfoDoor = record.IsUfoDoor;
        StopsLineOfSight = record.StopsLineOfSight;
        HasNoFloor = record.HasNoFloor;
        BigWall = record.BigWall;
        IsGravLift = record.IsGravLift;
        IsDoor = record.IsDoor;
        BlocksFire = record.BlocksFire;
        BlocksSmoke = record.BlocksSmoke;
        IsBaseModule = record.IsXcomBase;
        TerrainLevel = record.TerrainLevel;
        FootstepSound = record.FootstepSound;
        AlternateMcd = record.AlternateMcd;
        DieMcd = record.DieMcd;
        // setBlockValue(Light_Block, Stop_LOS, HE_Block, Block_Smoke, Flammable, HE_Block).
        _block[0] = record.LightBlock;
        _block[1] = record.StopsLineOfSight ? 255 : 0;
        _block[2] = record.HighExplosiveBlock;
        _block[3] = record.BlockSmokeValue == 1 ? 256 : 0;
        _block[4] = record.Flammable;
        _block[5] = record.HighExplosiveBlock;
        LightSource = record.LightSource;
        Armor = record.Armor;
        Flammable = record.Flammable;
        Fuel = record.Fuel;
        ExplosiveType = record.HighExplosiveType;
        Explosive = record.HighExplosiveStrength;
        MiniMapIndex = record.MiniMapIndex;
        var lofts = record.LoftIds.Span;
        for (var layer = 0; layer < LoftLayers; layer++) _lofts[layer] = lofts[layer];
    }

    public int Sprite(int frame) => _sprites[frame];
    public int LoftId(int layer) => _lofts[layer];

    /// <summary>MapData::getBlock: 0 light, 1 vision, 2 high explosive, 3 smoke, 4 fire, 5 gas.</summary>
    public int Block(int type) => _block[type];

    public int YOffset { get; }
    public int SpecialType { get; internal set; }
    public BattleTilePart ObjectType { get; internal set; }
    public int TimeUnitsWalk { get; internal set; }
    public int TimeUnitsFly { get; internal set; }
    public int TimeUnitsSlide { get; internal set; }
    public bool IsUfoDoor { get; }
    public bool StopsLineOfSight { get; private set; }
    public bool HasNoFloor { get; internal set; }
    public int BigWall { get; internal set; }
    public bool IsGravLift { get; }
    public bool IsDoor { get; }
    public bool BlocksFire { get; }
    public bool BlocksSmoke { get; }
    public bool IsBaseModule { get; }
    public int TerrainLevel { get; internal set; }
    public int FootstepSound { get; internal set; }
    public int AlternateMcd { get; }
    public int DieMcd { get; internal set; }
    public int LightSource { get; }
    public int Armor { get; internal set; }
    public int Flammable { get; internal set; }
    public int Fuel { get; internal set; }
    public int ExplosiveType { get; }
    public int Explosive { get; internal set; }
    public ushort MiniMapIndex { get; }

    /// <summary>MapData::setStopLOS, which also resets the vision block.</summary>
    internal void SetStopsLineOfSight(bool value)
    {
        StopsLineOfSight = value;
        _block[1] = value ? 255 : 0;
    }

    /// <summary>MapData::setHEBlock.</summary>
    internal void SetHighExplosiveBlock(int value) => _block[2] = value;

    internal void SetLoftId(int layer, int value) => _lofts[layer] = value;
}

/// <summary>A loaded MapDataSet: the objects of one MCD file, patched.</summary>
public sealed class BattleMapDataSet(string name, IReadOnlyList<BattleMapData> objects)
{
    public string Name { get; } = name;
    public IReadOnlyList<BattleMapData> Objects { get; } = objects;
}

/// <summary>
/// A loaded map block: its MAP tiles (height from the MAP header, which <c>loadMAP</c> takes over the rule's) and the
/// raw RMP routes, which are decoded where the block is placed.
/// </summary>
public sealed class BattleMapBlockData(string name, XcomMapData map, ReadOnlyMemory<byte> routes)
{
    public string Name { get; } = name;
    public XcomMapData Map { get; } = map;
    public ReadOnlyMemory<byte> Routes { get; } = routes;
}
