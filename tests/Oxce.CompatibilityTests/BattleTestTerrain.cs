using Oxce.Formats.Terrain;
using Oxce.Gameplay.Battles.Terrain;

namespace Oxce.CompatibilityTests;

/// <summary>Synthetic terrain files for battle tests: MCD records, MAP blocks and an in-memory file port.</summary>
internal static class BattleTestTerrain
{
    /// <summary>Terrain files by virtual path; counts reads so tests can check caching.</summary>
    public sealed class Files(Dictionary<string, byte[]> files) : IBattleTerrainFiles
    {
        public int Reads { get; private set; }

        public ReadOnlyMemory<byte>? Read(string virtualPath)
        {
            Reads++;
            return files.TryGetValue(virtualPath, out var data) ? data : (ReadOnlyMemory<byte>?)null;
        }
    }

    /// <summary>One 62-byte MCD record with the given byte offsets set.</summary>
    public static byte[] Record(params (int Offset, byte Value)[] values)
    {
        var record = new byte[McdTerrainCodec.RecordSize];
        foreach (var (offset, value) in values) record[offset] = value;
        return record;
    }

    public static byte[] Mcd(params byte[][] records) => [.. records.SelectMany(static record => record)];

    /// <summary>
    /// A MAP file: the header is length, width, levels; tiles run x fastest, then y, from the top level down. The
    /// floor of each level is given top level first.
    /// </summary>
    public static byte[] Map(int length, int width, params byte[] floorsTopDown)
    {
        var bytes = new List<byte> { (byte)length, (byte)width, (byte)floorsTopDown.Length };
        foreach (var floor in floorsTopDown)
            for (var tile = 0; tile < width * length; tile++) bytes.AddRange([floor, 0, 0, 0]);
        return [.. bytes];
    }

    /// <summary>One 24-byte RMP node: local y, x, z, five raw links (251-255 are the edge and unused values), rank, priority.</summary>
    public static byte[] RouteNode(int x, int y, int z, byte[] links, byte rank = 0, byte priority = 0)
    {
        var node = new byte[24];
        (node[0], node[1], node[2]) = ((byte)y, (byte)x, (byte)z);
        for (var link = 0; link < 5; link++) node[4 + link * 3] = link < links.Length ? links[link] : (byte)255;
        node[20] = rank;
        node[23] = priority;
        return node;
    }

    private static readonly string[] DataSetExtensions = ["MCD", "PCK", "TAB"];

    /// <summary>Empty MCD, PCK and TAB files for each data set, which the content build requires to exist.</summary>
    public static Dictionary<string, byte[]> RequiredFiles(params string[] dataSets) => dataSets
        .SelectMany(static set => DataSetExtensions.Select(extension => $"TERRAIN/{set}.{extension}"))
        .ToDictionary(static path => path, static _ => Array.Empty<byte>());
}
