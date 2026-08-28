using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.PersonnelTactical;

namespace Oxce.Mods.Rulesets.MissionEvents;

internal static class MissionEventYaml
{
    public static RuleSectionDefinition Section(string name) => RuleSectionRegistry.TryGetNamed(name, out var section) ? section! : throw new InvalidOperationException();
    public static List<string> Strings(YamlNode node, string key) => Sequence(node, YamlValueReader.ReadString, key);
    public static List<int> Integers(YamlNode node, string key) => Sequence(node, YamlValueReader.ReadInt32, key);
    public static List<T> Sequence<T>(YamlNode node, Func<YamlNode, T> read, string key)
    { if (node is not YamlSequenceNode sequence) throw new YamlFormatException($"{key} must be a sequence.", node.Span); return sequence.Items.Select(read).ToList(); }
    public static Dictionary<string, int> IntMap(YamlNode node, string key) => Map(node, YamlValueReader.ReadInt32, key);
    public static Dictionary<string, bool> BoolMap(YamlNode node, string key) => Map(node, YamlValueReader.ReadBoolean, key);
    public static Dictionary<string, T> Map<T>(YamlNode node, Func<YamlNode, T> read, string key)
    { if (node is not YamlMappingNode) throw new YamlFormatException($"{key} must be a mapping.", node.Span); return YamlValueReader.ReadMap(node, YamlValueReader.ReadString, read).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal); }
    public static Dictionary<string, ulong> Weights(YamlNode node, string key)
    { var value = new SortedDictionary<string, ulong>(StringComparer.Ordinal); PersonnelTacticalYaml.ApplyWeights(value, node, key); return new(value, StringComparer.Ordinal); }
    public static List<WeightedTimelineEntry> Timeline(YamlNode node, string key)
    { if (node is not YamlMappingNode mapping) throw new YamlFormatException($"{key} must be a mapping.", node.Span); return mapping.Entries.Select(entry => new WeightedTimelineEntry(YamlValueReader.ReadUInt64(entry.Key), MissionReadOnly.Dictionary(Weights(entry.Value, key)))).ToList(); }
    public static long ReadLong(RulePropertyReader reader, string key, long value) => reader.TryGet(key, out var node) ? YamlValueReader.ReadInt64(node!) : value;
    public static YamlMappingNode Overlay(YamlMappingNode current, YamlMappingNode? previous) => PersonnelTacticalYaml.Overlay(current, previous);
}
