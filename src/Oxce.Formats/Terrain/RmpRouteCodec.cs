using Oxce.Core.Geometry;
using Oxce.Formats.Binary;

namespace Oxce.Formats.Terrain;

public static class RmpRouteCodec
{
    public const int RecordSize = 24;
    public const int DefaultMaximumNodes = 1_000_000;

    public static RmpRouteMap Decode(
        BinaryDataReader input,
        int mapWidth,
        int mapLength,
        int mapLevels,
        int nodeOffset = 0,
        Position3 positionOffset = default,
        int segment = 0,
        int maxNodes = DefaultMaximumNodes)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mapLevels);
        ArgumentOutOfRangeException.ThrowIfNegative(nodeOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(maxNodes);

        var nodeCount = input.Remaining / RecordSize;
        if (nodeCount > maxNodes)
        {
            throw new InvalidDataException(
                $"RMP contains {nodeCount} complete nodes, exceeding the {maxNodes}-node limit.");
        }

        var nodes = new RmpRouteNode[nodeCount];
        var badNodeIndexes = new HashSet<int>();
        for (var index = 0; index < nodes.Length; index++)
        {
            var record = input.ReadMemory(RecordSize).Span;
            var localX = record[1];
            var localY = record[0];
            var localZ = record[2];
            if (localX >= mapWidth || localY >= mapLength || localZ >= mapLevels)
            {
                badNodeIndexes.Add(index);
                nodes[index] = RmpRouteNode.CreateDummy(index);
                continue;
            }

            var links = new int[5];
            for (var link = 0; link < links.Length; link++)
            {
                var rawLink = record[4 + (link * 3)];
                links[link] = rawLink <= 250 ? checked(rawLink + nodeOffset) : rawLink - 256;
            }

            nodes[index] = new RmpRouteNode(
                index,
                checked(nodeOffset + index),
                new Position3(
                    positionOffset.X + localX,
                    positionOffset.Y + localY,
                    positionOffset.Z + mapLevels - 1 - localZ),
                segment,
                record[19],
                record[20],
                record[21],
                record[22],
                record[23],
                links,
                isDummy: false);
        }

        if (badNodeIndexes.Count != 0)
        {
            foreach (var node in nodes.Where(node => !node.IsDummy))
            {
                for (var link = 0; link < node.Links.Count; link++)
                {
                    var localTarget = node.Links[link] - nodeOffset;
                    if (badNodeIndexes.Contains(localTarget))
                    {
                        node.ReplaceLink(link, -1);
                    }
                }
            }
        }

        return new RmpRouteMap(nodes, input.ReadMemory(input.Remaining));
    }
}

public sealed class RmpRouteMap
{
    internal RmpRouteMap(RmpRouteNode[] nodes, ReadOnlyMemory<byte> trailingData)
    {
        Nodes = nodes;
        TrailingData = trailingData;
    }

    public IReadOnlyList<RmpRouteNode> Nodes { get; }

    public ReadOnlyMemory<byte> TrailingData { get; }
}

public sealed class RmpRouteNode
{
    private readonly int[] _links;

    internal RmpRouteNode(
        int index,
        int id,
        Position3 position,
        int segment,
        int type,
        int rank,
        int flags,
        int reserved,
        int priority,
        int[] links,
        bool isDummy)
    {
        Index = index;
        Id = id;
        Position = position;
        Segment = segment;
        Type = type;
        Rank = rank;
        Flags = flags;
        Reserved = reserved;
        Priority = priority;
        _links = links;
        IsDummy = isDummy;
    }

    public int Index { get; }

    public int Id { get; }

    public Position3 Position { get; }

    public int Segment { get; }

    public int Type { get; }

    public int Rank { get; }

    public int Flags { get; }

    public int Reserved { get; }

    public int Priority { get; }

    public IReadOnlyList<int> Links => _links;

    public bool IsDummy { get; }

    internal void ReplaceLink(int index, int value) => _links[index] = value;

    internal static RmpRouteNode CreateDummy(int index) =>
        new(index, 0, default, 0, 0, 0, 0, 0, 0, [], isDummy: true);
}
