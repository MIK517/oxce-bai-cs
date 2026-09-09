using System.Buffers.Binary;

namespace Oxce.Formats.Terrain;

public readonly record struct WorldPoint(double Longitude, double Latitude);
public sealed record WorldPolygon(int Texture, IReadOnlyList<WorldPoint> Points);

/// <summary>RuleGlobe::loadDat: ten signed little-endian shorts per triangle or quadrilateral.</summary>
public static class WorldMapCodec
{
    public const int MaximumPolygons = 100_000;
    public static IReadOnlyList<WorldPolygon> Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length % 20 != 0 || bytes.Length / 20 > MaximumPolygons)
            throw new InvalidDataException("WORLD.DAT contains a truncated record or too many polygons.");
        var polygons = new WorldPolygon[bytes.Length / 20];
        for (var index = 0; index < polygons.Length; index++)
        {
            var record = bytes.Slice(index * 20, 20);
            var points = new WorldPoint[BinaryPrimitives.ReadInt16LittleEndian(record[12..]) == -1 ? 3 : 4];
            for (var vertex = 0; vertex < points.Length; vertex++)
                points[vertex] = new(BinaryPrimitives.ReadInt16LittleEndian(record[(vertex * 4)..]) * Math.PI / 1440,
                    BinaryPrimitives.ReadInt16LittleEndian(record[(vertex * 4 + 2)..]) * Math.PI / 1440);
            polygons[index] = new(BinaryPrimitives.ReadInt16LittleEndian(record[16..]), Array.AsReadOnly(points));
        }
        return Array.AsReadOnly(polygons);
    }
}
