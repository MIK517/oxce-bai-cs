using Oxce.Formats.Binary;

namespace Oxce.Formats.Terrain;

public static class XcomMapCodec
{
    public const int DefaultMaximumTiles = 16 * 1024 * 1024;

    public static XcomMapData Decode(BinaryDataReader input, int maxTiles = DefaultMaximumTiles)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTiles);
        if (input.Remaining < 3)
        {
            throw new InvalidDataException("MAP input is too short to contain its dimensions.");
        }

        var length = input.ReadByte();
        var width = input.ReadByte();
        var levels = input.ReadByte();
        if (width == 0 || length == 0 || levels == 0)
        {
            throw new InvalidDataException("MAP dimensions must all be positive.");
        }

        var tileCount = checked(width * length * levels);
        if (tileCount > maxTiles)
        {
            throw new InvalidDataException(
                $"MAP declares {tileCount} tiles, exceeding the {maxTiles}-tile limit.");
        }

        var tileBytes = checked(tileCount * XcomMapTileRecord.Size);
        if (input.Remaining < tileBytes)
        {
            throw new InvalidDataException(
                $"MAP declares {tileCount} tiles requiring {tileBytes} bytes with only {input.Remaining} remaining.");
        }

        if (input.Remaining - tileBytes >= XcomMapTileRecord.Size)
        {
            throw new InvalidDataException("MAP contains complete tile records beyond its declared dimensions.");
        }

        var tiles = new XcomMapTileRecord[tileCount];
        for (var index = 0; index < tiles.Length; index++)
        {
            tiles[index] = new XcomMapTileRecord(
                input.ReadByte(),
                input.ReadByte(),
                input.ReadByte(),
                input.ReadByte());
        }

        return new XcomMapData(width, length, levels, tiles, input.ReadMemory(input.Remaining));
    }
}

public sealed class XcomMapData
{
    private readonly XcomMapTileRecord[] _tiles;

    internal XcomMapData(
        int width,
        int length,
        int levels,
        XcomMapTileRecord[] tiles,
        ReadOnlyMemory<byte> trailingData)
    {
        Width = width;
        Length = length;
        Levels = levels;
        _tiles = tiles;
        TrailingData = trailingData;
    }

    public int Width { get; }

    public int Length { get; }

    public int Levels { get; }

    public IReadOnlyList<XcomMapTileRecord> Tiles => _tiles;

    public ReadOnlyMemory<byte> TrailingData { get; }

    public XcomMapTileRecord GetTile(int x, int y, int z)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Length);
        ArgumentOutOfRangeException.ThrowIfNegative(z);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(z, Levels);
        var layerFromTop = Levels - 1 - z;
        var index = checked(((layerFromTop * Length) + y) * Width + x);
        return _tiles[index];
    }
}

public readonly record struct XcomMapTileRecord(
    byte Floor,
    byte WestWall,
    byte NorthWall,
    byte ObjectPart)
{
    public const int Size = 4;
}
