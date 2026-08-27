using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.PersonnelTactical;

namespace Oxce.Mods.Rulesets.TerrainDeployment;

internal static class TerrainDeploymentYaml
{
    public static RuleSectionDefinition Section(string name) =>
        RuleSectionRegistry.TryGetNamed(name, out var section) ? section! : throw new InvalidOperationException();

    public static List<string> Strings(YamlNode node, string property) =>
        Values(node, YamlValueReader.ReadString, property);

    public static List<int> Integers(YamlNode node, string property) =>
        Values(node, YamlValueReader.ReadInt32, property);

    public static List<T> Values<T>(YamlNode node, Func<YamlNode, T> read, string property)
    {
        if (node is YamlSequenceNode sequence) return sequence.Items.Select(read).ToList();
        if (node is YamlScalarNode) return [read(node)];
        throw new YamlFormatException($"{property} must be a scalar or sequence.", node.Span);
    }

    public static void EditableNames(RulePropertyReader reader, string key, List<string> target)
    {
        if (reader.TryGet(key, out var node))
            PersonnelTacticalYaml.ApplyEditableList(target, node!, YamlValueReader.ReadString, key);
    }

    public static IReadOnlyDictionary<string, ulong> ReadWeights(YamlNode node)
    {
        var result = new SortedDictionary<string, ulong>(StringComparer.Ordinal);
        PersonnelTacticalYaml.ApplyWeights(result, node, "weights");
        return TerrainReadOnly.Dictionary(result);
    }

    public static List<KeyValuePair<ulong, IReadOnlyDictionary<string, ulong>>> ReadWeightTimeline(YamlNode node, string property)
    {
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException($"{property} must be a mapping.", node.Span);
        return mapping.Entries.Select(entry => new KeyValuePair<ulong, IReadOnlyDictionary<string, ulong>>(
            YamlValueReader.ReadUInt64(entry.Key), ReadWeights(entry.Value))).ToList();
    }

    public static List<int> FixedPair(YamlNode node, IReadOnlyList<int> current, string property)
    {
        if (node is not YamlSequenceNode sequence)
            throw new YamlFormatException($"{property} must be a sequence.", node.Span);
        var result = current.ToList();
        for (var index = 0; index < Math.Min(2, sequence.Items.Count); index++)
            result[index] = YamlValueReader.ReadInt32(sequence.Items[index]);
        return result;
    }
}
