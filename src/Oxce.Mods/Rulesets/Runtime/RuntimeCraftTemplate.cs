using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed record RuntimeCraftTemplateWeapon(string RuleId, int Ammo, bool Rearming, bool Disabled);
public sealed record RuntimeCraftTemplateVehicle(string RuleId, int Ammo);
public sealed record RuntimeCraftTemplate(string Name, int Fuel, int Damage, string Status, int ExcessFuel, bool LowFuel,
    IReadOnlyList<RuntimeCraftTemplateWeapon?> Weapons, IReadOnlyDictionary<string, int> Items,
    IReadOnlyList<RuntimeCraftTemplateVehicle> Vehicles);

internal static class RuntimeCraftTemplateLoader
{
    public static RuntimeCraftTemplate? Read(YamlMappingNode? map)
    {
        if (map is null) return null;
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        if (map.TryGet("items", out var node) && node is YamlMappingNode itemMap)
            foreach (var pair in itemMap.Entries)
                if (pair.ScalarKey is { } key) items.Add(key, YamlValueReader.ReadInt32(pair.Value));
        return new(String(map, "name", ""), Integer(map, "fuel"), Integer(map, "damage"), String(map, "status", "STR_READY"),
            Integer(map, "excessFuel"), Boolean(map, "lowFuel"),
            Array.AsReadOnly(Maps("weapons").Select(w => String(w, "type", "0") == "0" ? null :
                new RuntimeCraftTemplateWeapon(String(w, "type", ""), Integer(w, "ammo"), Boolean(w, "rearming"), Boolean(w, "disabled"))).ToArray()),
            new ReadOnlyDictionary<string, int>(items), Array.AsReadOnly(Maps("vehicles").Select(v =>
                new RuntimeCraftTemplateVehicle(String(v, "type", ""), Integer(v, "ammo"))).ToArray()));

        IEnumerable<YamlMappingNode> Maps(string key) => map.TryGet(key, out var value) && value is YamlSequenceNode sequence
            ? sequence.Items.Select(v => v as YamlMappingNode ?? throw new InvalidDataException($"Starting craft {key} requires mappings.")) : [];
    }

    private static int Integer(YamlMappingNode map, string key) => map.TryGet(key, out var node) ? YamlValueReader.ReadInt32(node!) : 0;
    private static bool Boolean(YamlMappingNode map, string key) => map.TryGet(key, out var node) && YamlValueReader.ReadBoolean(node!);
    private static string String(YamlMappingNode map, string key, string fallback) => map.TryGet(key, out var node) ? YamlValueReader.ReadString(node!) : fallback;
}
