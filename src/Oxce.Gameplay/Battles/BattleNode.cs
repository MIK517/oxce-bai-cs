namespace Oxce.Gameplay.Battles;

/// <summary>
/// A route node (Node): spawn and patrol points from the map blocks' RMP files. Links are node IDs, or the
/// reference's special values while unconnected: -1 unused, -2 north, -3 east, -4 south, -5 west. A culled node stays
/// in the list as a dummy so the IDs keep lining up.
/// </summary>
internal sealed class BattleNode
{
    public BattleNode(int id, BattlePosition position, int segment, int type, int rank, int flags, int reserved, int priority,
        IEnumerable<int> links)
    {
        Id = id;
        Position = position;
        Segment = segment;
        Type = type;
        Rank = rank;
        Flags = flags;
        Reserved = reserved;
        Priority = priority;
        Links = [.. links];
    }

    private BattleNode() => Links = [];

    /// <summary>Node(): a culled node.</summary>
    public static BattleNode Dummy() => new() { IsDummy = true };

    public int Id { get; }
    public BattlePosition Position { get; }
    public int Segment { get; }
    public int Type { get; }
    public int Rank { get; }
    public int Flags { get; }
    public int Reserved { get; }
    public int Priority { get; }
    public List<int> Links { get; }
    public bool IsDummy { get; private init; }
}
