namespace Oxce.Core.Geometry;

/// <summary>
/// A three-dimensional coordinate with the signed 16-bit component storage used by
/// the reference engine's battlescape Position type.
/// </summary>
public readonly record struct Position3(short X, short Y, short Z)
{
    public const int TileWidth = 16;

    public const int TileHeight = 24;

    public Position3(int x, int y, int z)
        : this(unchecked((short)x), unchecked((short)y), unchecked((short)z))
    {
    }

    public static Position3 operator +(Position3 left, Position3 right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    public static Position3 operator -(Position3 left, Position3 right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    public Position3 ToVoxel() => new(X * TileWidth, Y * TileWidth, Z * TileHeight);

    public Position3 ToTile() => new(X / TileWidth, Y / TileWidth, Z / TileHeight);

    public Position3 ClipVoxel() => new(X % TileWidth, Y % TileWidth, Z % TileHeight);

    public static int DistanceSquared(Position3 first, Position3 second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        var z = first.Z - second.Z;
        return unchecked((x * x) + (y * y) + (z * z));
    }

    public static int Distance2DSquared(Position3 first, Position3 second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return unchecked((x * x) + (y * y));
    }

    public static int Distance2D(Position3 first, Position3 second) =>
        (int)Math.Ceiling(Math.Sqrt(Distance2DSquared(first, second)));
}
