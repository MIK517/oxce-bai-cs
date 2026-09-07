using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed record RuntimeSoldierTemplate(
    IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans,
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyDictionary<string, short> InitialStats,
    IReadOnlyDictionary<string, short> CurrentStats,
    float? Recovery,
    IReadOnlyList<string> UnsupportedFields)
{
    public IReadOnlyDictionary<string, int>? PreviousTransformations { get; init; }
    public IReadOnlyDictionary<string, int>? TransformationBonuses { get; init; }
    public IReadOnlyDictionary<string, int>? RandomTransformationBonuses { get; init; }
    public int TransformationBonusesCount { get; init; } = 1;
}

internal static class RuntimeSoldierTemplateLoader
{
    private static readonly string[] IntegerFields = ["nationality", "rank", "gender", "look", "lookVariant", "missions", "kills", "stuns", "manaMissing", "healthMissing", "improvement", "psiStrImprovement"];
    private static readonly string[] BooleanFields = ["psiTraining", "training", "returnToTrainingWhenHealed", "allowAutoCombat", "isLeeroyJenkins", "corpseRecovered"];
    private static readonly string[] StringFields = ["name", "callsign", "armor", "replacedArmor", "transformedArmor", "personalEquipmentArmor"];
    private static readonly string[] NestedFields = ["dailyDogfightExperienceCache", "equipmentLayout", "personalEquipmentLayout",
        "death", "diary", "scriptValues"];

    public static RuntimeSoldierTemplate? Read(YamlMappingNode? node)
    {
        if (node is null) return null;
        var recovery = node.TryGet("recovery", out var recoveryNode) ? (float?)YamlValueReader.ReadDouble(recoveryNode!) : null;
        if (recovery is { } value && !float.IsFinite(value)) throw new InvalidDataException("Soldier recovery must be finite.");
        return new(Read(IntegerFields, YamlValueReader.ReadInt32), Read(BooleanFields, YamlValueReader.ReadBoolean),
            Read(StringFields, YamlValueReader.ReadString), Stats("initialStats"), Stats("currentStats"), recovery,
            Array.AsReadOnly(NestedFields.Where(key => node.TryGet(key, out _)).ToArray()))
        {
            PreviousTransformations = Counts("previousTransformations"),
            TransformationBonuses = Counts("transformationBonuses"),
            RandomTransformationBonuses = Counts("randomTransformationBonuses", weights: true),
            TransformationBonusesCount = node.TryGet("transformationBonusesCount", out var count) ? YamlValueReader.ReadInt32(count!) : 1,
        };

        IReadOnlyDictionary<string, int>? Counts(string key, bool weights = false)
        {
            if (!node.TryGet(key, out var field)) return null;
            var map = field as YamlMappingNode ?? throw new InvalidDataException($"Soldier {key} requires a mapping.");
            if (map.Entries.Count > 10_000) throw new InvalidDataException($"Soldier {key} exceeds the entry limit.");
            var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
            long total = 0;
            foreach (var entry in map.Entries)
            {
                var id = YamlValueReader.ReadString(entry.Key);
                int value;
                if (weights)
                {
                    var weight = YamlValueReader.ReadUInt64(entry.Value);
                    if (weight > int.MaxValue || (total += (long)weight) > int.MaxValue)
                        throw new InvalidDataException("Soldier bonus weights exceed the reference integer random range.");
                    value = (int)weight;
                }
                else value = YamlValueReader.ReadInt32(entry.Value);
                if (!result.TryAdd(id, value)) throw new InvalidDataException($"Soldier {key} contains a duplicate key.");
            }
            return new ReadOnlyDictionary<string, int>(result);
        }

        ReadOnlyDictionary<string, T> Read<T>(IEnumerable<string> keys, Func<YamlNode, T> parse)
        {
            var values = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var key in keys)
                if (node.TryGet(key, out var field)) values.Add(key, parse(field!));
            return new(values);
        }
        ReadOnlyDictionary<string, short> Stats(string key)
        {
            var values = new Dictionary<string, short>(StringComparer.Ordinal);
            if (node.TryGet(key, out var field) && field is YamlMappingNode map)
                foreach (var entry in map.Entries)
                    if (entry.ScalarKey is { } name) values.Add(name, YamlValueReader.ReadInt16(entry.Value));
            return new(values);
        }
    }
}
