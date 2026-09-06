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
    IReadOnlyList<string> UnsupportedFields);

internal static class RuntimeSoldierTemplateLoader
{
    private static readonly string[] IntegerFields = ["nationality", "rank", "gender", "look", "lookVariant", "missions", "kills", "stuns", "manaMissing", "healthMissing", "improvement", "psiStrImprovement"];
    private static readonly string[] BooleanFields = ["psiTraining", "training", "returnToTrainingWhenHealed", "allowAutoCombat", "isLeeroyJenkins", "corpseRecovered"];
    private static readonly string[] StringFields = ["name", "callsign", "armor", "replacedArmor", "transformedArmor", "personalEquipmentArmor"];
    private static readonly string[] NestedFields = ["dailyDogfightExperienceCache", "equipmentLayout", "personalEquipmentLayout",
        "death", "diary", "previousTransformations", "transformationBonuses", "randomTransformationBonuses", "scriptValues"];

    public static RuntimeSoldierTemplate? Read(YamlMappingNode? node)
    {
        if (node is null) return null;
        var recovery = node.TryGet("recovery", out var recoveryNode) ? (float?)YamlValueReader.ReadDouble(recoveryNode!) : null;
        if (recovery is { } value && !float.IsFinite(value)) throw new InvalidDataException("Soldier recovery must be finite.");
        return new(Read(IntegerFields, YamlValueReader.ReadInt32), Read(BooleanFields, YamlValueReader.ReadBoolean),
            Read(StringFields, YamlValueReader.ReadString), Stats("initialStats"), Stats("currentStats"), recovery,
            Array.AsReadOnly(NestedFields.Where(key => node.TryGet(key, out _)).ToArray()));

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
