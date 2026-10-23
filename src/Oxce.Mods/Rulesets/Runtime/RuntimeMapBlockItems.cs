using System.Collections.ObjectModel;
using System.Text;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>A map block position (Position: three Sint16 values).</summary>
public sealed record RuntimeBlockPosition(int X, int Y, int Z);

/// <summary>MapBlock <c>items</c>: an item type and the positions it lies on.</summary>
public sealed record RuntimeMapBlockItem(string Type, IReadOnlyList<RuntimeBlockPosition> Positions);

/// <summary>MapBlock <c>fuseTimers</c>: the fuse timer range an item type is primed with.</summary>
public sealed record RuntimeFuseRange(int Minimum, int Maximum);

/// <summary>RandomizedItems: an amount of items picked from a list (once, or per item when mixed) at a position.</summary>
public sealed record RuntimeRandomizedItems(RuntimeBlockPosition Position, int Amount, int FuseTimerMinimum, int FuseTimerMaximum, bool Mixed,
    IReadOnlyList<string> ItemList);

/// <summary>ExtendedItems: an item at positions, a fuse timer range and the ammo (type and quantity) loaded into it.</summary>
public sealed record RuntimeExtendedItems(string Type, IReadOnlyList<RuntimeBlockPosition> Positions, int FuseTimerMinimum, int FuseTimerMaximum,
    IReadOnlyList<RuntimeAmmoDefinition> Ammo);

public sealed record RuntimeAmmoDefinition(string Type, int Quantity);

/// <summary>The items a map block places (MapBlock::load's items, fuseTimers, randomizedItems, extendedItems).</summary>
public sealed record RuntimeMapBlockItems(
    IReadOnlyList<RuntimeMapBlockItem> Items,
    IReadOnlyDictionary<string, RuntimeFuseRange> FuseTimers,
    IReadOnlyList<RuntimeRandomizedItems> RandomizedItems,
    IReadOnlyList<RuntimeExtendedItems> ExtendedItems)
{
    public static RuntimeMapBlockItems None { get; } = new([], new ReadOnlyDictionary<string, RuntimeFuseRange>(new Dictionary<string, RuntimeFuseRange>()), [], []);

    private static readonly Comparer<byte[]> Utf8Order = Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right));

    /// <summary>Reads the four nodes; returns null and the reason when one is malformed (the reference fails to load it).</summary>
    internal static RuntimeMapBlockItems? Read(YamlNode? items, YamlNode? fuseTimers, YamlNode? randomized, YamlNode? extended, out string? invalid)
    {
        invalid = null;
        if (items is null && fuseTimers is null && randomized is null && extended is null) return None;
        try
        {
            // std::map<std::string, ...>: keys in UTF-8 byte order, a repeated key keeps the last value.
            var itemMap = new Dictionary<string, IReadOnlyList<RuntimeBlockPosition>>(StringComparer.Ordinal);
            foreach (var entry in Mapping(items, "items")) itemMap[Key(entry)] = Positions(entry.Value);
            var fuses = new Dictionary<string, RuntimeFuseRange>(StringComparer.Ordinal);
            foreach (var entry in Mapping(fuseTimers, "fuseTimers"))
            {
                var pair = Pair(entry.Value);
                fuses[Key(entry)] = new(YamlValueReader.ReadInt32(pair.First), YamlValueReader.ReadInt32(pair.Second));
            }
            var randomItems = Sequence(randomized, "randomizedItems").Select(static node =>
            {
                var map = Map(node);
                return new RuntimeRandomizedItems(map.TryGet("position", out var position) ? Position(position!) : new(0, 0, 0),
                    YamlValueReader.ReadInt32(map, "amount", 1), YamlValueReader.ReadInt32(map, "fuseTimerMin", -1),
                    YamlValueReader.ReadInt32(map, "fuseTimerMax", -1), map.TryGet("mixed", out var mixed) && YamlValueReader.ReadBoolean(mixed!),
                    Array.AsReadOnly(Sequence(map.TryGet("itemList", out var list) ? list : null, "itemList").Select(YamlValueReader.ReadString).ToArray()));
            }).ToArray();
            var extendedItems = Sequence(extended, "extendedItems").Select(static node =>
            {
                var map = Map(node);
                return new RuntimeExtendedItems(map.TryGet("type", out var type) ? YamlValueReader.ReadString(type!) : "",
                    map.TryGet("pos", out var positions) ? Positions(positions!) : [],
                    YamlValueReader.ReadInt32(map, "fuseTimerMin", -1), YamlValueReader.ReadInt32(map, "fuseTimerMax", -1),
                    Array.AsReadOnly(Sequence(map.TryGet("ammoDef", out var ammo) ? ammo : null, "ammoDef").Select(static definition =>
                    {
                        var pair = Pair(definition);
                        return new RuntimeAmmoDefinition(YamlValueReader.ReadString(pair.First), YamlValueReader.ReadInt32(pair.Second));
                    }).ToArray()));
            }).ToArray();
            return new(
                Array.AsReadOnly(itemMap.OrderBy(static pair => Encoding.UTF8.GetBytes(pair.Key), Utf8Order)
                    .Select(static pair => new RuntimeMapBlockItem(pair.Key, pair.Value)).ToArray()),
                new ReadOnlyDictionary<string, RuntimeFuseRange>(fuses), Array.AsReadOnly(randomItems), Array.AsReadOnly(extendedItems));
        }
        catch (Exception exception) when (exception is YamlFormatException or InvalidDataException or FormatException or OverflowException)
        {
            invalid = exception.Message;
            return null;
        }
    }

    private static IReadOnlyList<YamlMappingEntry> Mapping(YamlNode? node, string key) => node switch
    {
        null => [],
        YamlMappingNode map => map.Entries,
        _ => throw new InvalidDataException($"Map block '{key}' must be a mapping."),
    };

    private static IReadOnlyList<YamlNode> Sequence(YamlNode? node, string key) => node switch
    {
        null => [],
        YamlSequenceNode sequence => sequence.Items,
        _ => throw new InvalidDataException($"Map block '{key}' must be a sequence."),
    };

    private static YamlMappingNode Map(YamlNode node) => node as YamlMappingNode ?? throw new InvalidDataException("A map block item entry must be a mapping.");

    private static string Key(YamlMappingEntry entry) => entry.ScalarKey ?? throw new InvalidDataException("A map block item key must be a scalar.");

    // std::pair reads from a two-element sequence.
    private static (YamlNode First, YamlNode Second) Pair(YamlNode node) => node is YamlSequenceNode { Items.Count: 2 } sequence
        ? (sequence.Items[0], sequence.Items[1]) : throw new InvalidDataException("A map block pair must be a sequence of two values.");

    private static RuntimeBlockPosition Position(YamlNode node) => node is YamlSequenceNode { Items.Count: >= 3 } sequence
        ? new(checked((short)YamlValueReader.ReadInt32(sequence.Items[0])), checked((short)YamlValueReader.ReadInt32(sequence.Items[1])),
            checked((short)YamlValueReader.ReadInt32(sequence.Items[2])))
        : throw new InvalidDataException("A map block position must be a sequence of three values.");

    private static ReadOnlyCollection<RuntimeBlockPosition> Positions(YamlNode node) =>
        Array.AsReadOnly(Sequence(node, "positions").Select(Position).ToArray());
}
