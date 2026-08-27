using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.PersonnelTactical;

internal static class PersonnelTacticalYaml
{
    public static RuleSectionDefinition Section(string name) =>
        RuleSectionRegistry.TryGetNamed(name, out var section) ? section! : throw new InvalidOperationException();

    public static void EditableNames(RulePropertyReader reader, string key, List<string> target, bool unique = false)
    {
        if (reader.TryGet(key, out var node)) CampaignStartYaml.ApplyEditableNames(target, node!, unique);
    }

    public static void EditableIntegers(RulePropertyReader reader, string key, List<int> target)
    {
        if (!reader.TryGet(key, out var node)) return;
        ApplyEditableList(target, node!, YamlValueReader.ReadInt32, key);
    }

    public static void ApplyStats(UnitStatsBuilder target, YamlNode node, bool nonZeroMerge)
    {
        if (node is not YamlMappingNode mapping) return;
        foreach (var key in UnitStatsBuilder.Keys)
        {
            if (!mapping.TryGet(key, out var value)) continue;
            var parsed = YamlValueReader.ReadInt16(value!);
            if (!nonZeroMerge || parsed != 0) target.Values[key] = parsed;
        }
    }

    public static UnitStatsRule FreezeStats(UnitStatsBuilder builder) =>
        new(PersonnelReadOnly.Dictionary(builder.Values));

    public static bool? ReadNullableBoolean(YamlNode node) =>
        node is YamlNullNode ? null : YamlValueReader.ReadBoolean(node);

    public static int? ReadNullableInt32(YamlNode node) =>
        node is YamlNullNode ? null : YamlValueReader.ReadInt32(node);

    public static string ReadNullableName(YamlNode node)
    {
        if (node is YamlNullNode) return "STR_NULL";
        var value = YamlValueReader.ReadString(node);
        if (value == "STR_NULL") throw new YamlFormatException("STR_NULL is reserved for explicit null.", node.Span);
        return value;
    }

    public static List<string> StringList(YamlNode node, string key)
    {
        if (node is not YamlSequenceNode)
            throw new YamlFormatException($"{key} must be a sequence.", node.Span);
        return YamlValueReader.ReadSequence(node, YamlValueReader.ReadString).ToList();
    }

    public static List<int> IntegerList(YamlNode node, string key)
    {
        if (node is not YamlSequenceNode)
            throw new YamlFormatException($"{key} must be a sequence.", node.Span);
        return YamlValueReader.ReadSequence(node, YamlValueReader.ReadInt32).ToList();
    }

    public static List<RuleIndexReference> IndexList(YamlNode node, string modId)
    {
        var values = node is YamlSequenceNode sequence
            ? sequence.Items.Select(value => PresentationYaml.ReadIndexReference(value, modId))
            : [PresentationYaml.ReadIndexReference(node, modId)];
        return values.Where(value => value.Index != -1).ToList();
    }

    public static void ApplyCost(RulePropertyReader reader, ItemUseValuesBuilder<int?> cost)
    {
        if (reader.TryGet("tuUse", out var time)) cost.Time = ReadNullableInt16(time!);
        if (!reader.TryGet("costUse", out var node)) return;
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException("Skill costUse must be a mapping.", node!.Span);
        ApplyUseValues(mapping, cost, ReadNullableInt16);
    }

    public static void ApplyFlat(RulePropertyReader reader, ItemUseValuesBuilder<bool?> flat)
    {
        if (!reader.TryGet("flatUse", out var node)) return;
        if (node is YamlMappingNode mapping) ApplyUseValues(mapping, flat, ReadNullableBoolean);
        else flat.Time = ReadNullableBoolean(node!);
    }

    public static YamlMappingNode Overlay(YamlMappingNode current, YamlMappingNode? previous)
    {
        if (previous is null) return current;
        var entries = current.Entries.ToList();
        var keys = current.Entries.Select(entry => entry.ScalarKey).Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);
        entries.AddRange(previous.Entries.Where(entry => entry.ScalarKey is null || !keys.Contains(entry.ScalarKey)));
        return new YamlMappingNode(current.Span, entries, current.Tag, current.Anchor);
    }

    public static void ApplyEditableIntMap(
        RulePropertyReader reader, string key, Dictionary<string, int> target)
    {
        if (!reader.TryGet(key, out var node)) return;
        ApplyEditableMap(target, node!, YamlValueReader.ReadString, YamlValueReader.ReadInt32, key);
    }

    public static void ApplyWeights(SortedDictionary<string, ulong> target, YamlNode node, string key)
    {
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException($"{key} must be a mapping.", node.Span);
        foreach (var entry in mapping.Entries)
        {
            var id = entry.ScalarKey ?? throw new YamlFormatException($"{key} keys must be scalars.", entry.Key.Span);
            var weight = YamlValueReader.ReadUInt64(entry.Value);
            if (weight == 0) target.Remove(id); else target[id] = weight;
        }
    }

    public static void ApplyEditableList<T>(List<T> target, YamlNode node, Func<YamlNode, T> read, string key)
    {
        if (node is not YamlSequenceNode sequence)
            throw new YamlFormatException($"{key} must be a sequence.", node.Span);
        var values = sequence.Items.Select(read).ToArray();
        switch (node.Tag)
        {
            case null:
            case "!!seq":
            case "!info": target.Clear(); target.AddRange(values); break;
            case "!add": target.AddRange(values); break;
            case "!remove": foreach (var value in values) target.RemoveAll(item => EqualityComparer<T>.Default.Equals(item, value)); break;
            default: throw new YamlFormatException($"Unsupported collection tag '{node.Tag}'.", node.Span);
        }
    }

    public static void ApplyEditableMap<TKey, TValue>(
        Dictionary<TKey, TValue> target,
        YamlNode node,
        Func<YamlNode, TKey> readKey,
        Func<YamlNode, TValue> readValue,
        string key)
        where TKey : notnull
    {
        if (node.Tag == "!remove")
        {
            if (node is not YamlSequenceNode remove)
                throw new YamlFormatException($"{key} !remove must be a sequence.", node.Span);
            foreach (var item in remove.Items) target.Remove(readKey(item));
            return;
        }
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException($"{key} must be a mapping.", node.Span);
        if (node.Tag is null or "!!map" or "!info") target.Clear();
        else if (node.Tag != "!add") throw new YamlFormatException($"Unsupported collection tag '{node.Tag}'.", node.Span);
        foreach (var entry in mapping.Entries) target[readKey(entry.Key)] = readValue(entry.Value);
    }

    private static int? ReadNullableInt16(YamlNode node) =>
        node is YamlNullNode ? null : YamlValueReader.ReadInt16(node);

    private static void ApplyUseValues<T>(YamlMappingNode mapping, ItemUseValuesBuilder<T> values, Func<YamlNode, T> read)
    {
        if (mapping.TryGet("time", out var node)) values.Time = read(node!);
        if (mapping.TryGet("energy", out node)) values.Energy = read(node!);
        if (mapping.TryGet("morale", out node)) values.Morale = read(node!);
        if (mapping.TryGet("health", out node)) values.Health = read(node!);
        if (mapping.TryGet("stun", out node)) values.Stun = read(node!);
        if (mapping.TryGet("mana", out node)) values.Mana = read(node!);
    }
}
