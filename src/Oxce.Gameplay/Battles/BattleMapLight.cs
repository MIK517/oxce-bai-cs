namespace Oxce.Gameplay.Battles;

/// <summary>Tile.h's <c>LightLayers</c>: the four layers of light a tile holds, in the order the engine recomputes them.</summary>
internal enum BattleLightLayer
{
    Ambient = 0,
    Fire = 1,
    Items = 2,
    Units = 3,
}

/// <summary>
/// The battle map's light state (Phase 7 branch 4 S3): the four light layers of every tile (<c>Tile::_light</c>) and the engine's
/// visibility blockage cache (<c>TileEngine::_blockVisibility</c>). Both are runtime state: they are computed at generation, on load and
/// after the changes the reference recomputes them for, and are never saved.
/// </summary>
internal sealed partial class BattleMap
{
    /// <summary>Tile::_light's layer count (LL_MAX).</summary>
    public const int LightLayerCount = 4;

    private short[] _light = [];
    private uint[] _blockDirections = [];
    private byte[] _bigWalls = [];
    private byte[] _heights = [];

    private void ResetLight(int tiles)
    {
        _light = new short[tiles * LightLayerCount];
        _blockDirections = new uint[tiles];
        _bigWalls = new byte[tiles];
        _heights = new byte[tiles];
    }

    /// <summary>Tile::getLight: the light of one layer.</summary>
    public int Light(int tile, BattleLightLayer layer) => _light[tile * LightLayerCount + (int)layer];

    /// <summary>Tile::addLight: the layer takes the light only when it is brighter.</summary>
    public void AddLight(int tile, int light, BattleLightLayer layer)
    {
        ref var value = ref _light[tile * LightLayerCount + (int)layer];
        if (value < light) value = checked((short)light);
    }

    /// <summary>Tile::resetLightMulti: the layer and every layer after it go dark.</summary>
    public void ResetLightMulti(int tile, BattleLightLayer layer)
    {
        for (var index = (int)layer; index < LightLayerCount; index++) _light[tile * LightLayerCount + index] = 0;
    }

    /// <summary>Tile::getLightMulti: the brightest of the layers up to and including this one.</summary>
    public int LightMulti(int tile, BattleLightLayer layer)
    {
        var light = 0;
        for (var index = (int)layer; index >= 0; index--) light = Math.Max(light, _light[tile * LightLayerCount + index]);
        return light;
    }

    /// <summary>Tile::getShade: 15 less the brightest layer, at least 0 (0 is full light).</summary>
    public int Shade(int tile)
    {
        var light = 0;
        for (var index = 0; index < LightLayerCount; index++) light = Math.Max(light, _light[tile * LightLayerCount + index]);
        return Math.Max(0, 15 - light);
    }

    /// <summary><see cref="BattleLightCache"/>'s record of the tile, as <c>VisibilityBlockCache</c> holds it.</summary>
    public BattleLightCache LightCache(int tile) => new(_blockDirections[tile], _bigWalls[tile], _heights[tile]);

    public void SetLightCache(int tile, BattleLightCache cache)
    {
        _blockDirections[tile] = cache.BlockDirections;
        _bigWalls[tile] = cache.BigWalls;
        _heights[tile] = cache.Height;
    }

    /// <summary>The tile at a position, or -1 outside the map (SavedBattleGame::getTile's null).</summary>
    public int TileAt(int x, int y, int z) =>
        (uint)x < (uint)Width && (uint)y < (uint)Length && (uint)z < (uint)Height ? x + y * Width + z * Width * Length : -1;

    /// <summary>The tile beside a position in a compass direction (0-7), <paramref name="levelOffset"/> levels up or down, or -1 outside the map.</summary>
    public int Neighbour(BattlePosition position, int direction, int levelOffset = 0)
    {
        var (dx, dy) = BattleGeometry.DirectionVector(direction);
        return TileAt(position.X + dx, position.Y + dy, position.Z + levelOffset);
    }

    /// <summary>The whole map as a <see cref="BattleMapSubset"/>.</summary>
    public BattleMapSubset Area => new(0, Width, 0, Length);
}

/// <summary>
/// TileEngine's <c>VisibilityBlockCache</c> and its bit helpers: one bit per direction and level for "light and sight cannot pass", the
/// direction bits that are big walls, and the tile's terrain height in voxels (24 for an impassable object).
/// </summary>
internal readonly record struct BattleLightCache(uint BlockDirections, byte BigWalls, byte Height)
{
    private const int DirectionMultiplier = 9;
    private const int DirectionOffset = DirectionMultiplier + 1;

    /// <summary>selectBit(dir, z): <paramref name="direction"/> 0-7 or -1 for straight up or down, <paramref name="z"/> -1, 0 or +1.</summary>
    private static uint SelectBit(int direction, int z) => 1u << (DirectionOffset + DirectionMultiplier * z + direction);

    private static readonly uint BlockDown = SelectBit(-1, -1);
    private static readonly uint BlockUp = SelectBit(-1, +1);
    private static readonly uint FireBit = SelectBit(+7, +1) << 1;
    private static readonly uint SmokeBit = SelectBit(+7, +1) << 2;

    public bool Blocks(int direction, int z) => (BlockDirections & SelectBit(direction, z)) != 0;

    /// <summary>
    /// The tile line's test of one step from this tile (<c>calculateLineTile</c> and the enhanced <c>addLight</c>): <see cref="Blocks"/>, except
    /// that a big wall on the same level does not stop a line that ends on the tile behind it (<paramref name="endsAtTarget"/>).
    /// </summary>
    public bool BlocksStep(int direction, int z, bool endsAtTarget) =>
        Blocks(direction, z) && !(z == 0 && direction >= 0 && IsBigWall(direction) && endsAtTarget);
    public bool BlocksUp => (BlockDirections & BlockUp) != 0;
    public bool BlocksDown => (BlockDirections & BlockDown) != 0;
    public bool HasFire => (BlockDirections & FireBit) != 0;
    public bool HasSmoke => (BlockDirections & SmokeBit) != 0;
    public bool IsBigWall(int direction) => (BigWalls & (1u << direction)) != 0;

    public BattleLightCache WithBlock(int direction, int z, bool blocked) => blocked ? this with { BlockDirections = BlockDirections | SelectBit(direction, z) } : this;
    public BattleLightCache WithBlockUp(bool blocked) => blocked ? this with { BlockDirections = BlockDirections | BlockUp } : this;
    public BattleLightCache WithBlockDown(bool blocked) => blocked ? this with { BlockDirections = BlockDirections | BlockDown } : this;
    public BattleLightCache WithFire(bool fire) => fire ? this with { BlockDirections = BlockDirections | FireBit } : this;
    public BattleLightCache WithSmoke(bool smoke) => smoke ? this with { BlockDirections = BlockDirections | SmokeBit } : this;
    public BattleLightCache WithBigWall(int direction, bool bigWall) => bigWall ? this with { BigWalls = (byte)(BigWalls | 1 << direction) } : this;
}
