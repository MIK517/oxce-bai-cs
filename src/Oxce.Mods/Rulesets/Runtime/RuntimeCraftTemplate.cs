using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed record RuntimeCraftTemplateWeapon(string RuleId, int Ammo, bool Rearming, bool Disabled);
public sealed record RuntimeCraftTemplateVehicle(string RuleId, int Ammo)
{
    public int? Size { get; init; }
    public int? SpaceOccupied { get; init; }
}
public sealed record RuntimeCraftTemplate(string Name, int Fuel, int Damage, string Status, int ExcessFuel, bool LowFuel,
    IReadOnlyList<RuntimeCraftTemplateWeapon?> Weapons, IReadOnlyDictionary<string, int> Items,
    IReadOnlyList<RuntimeCraftTemplateVehicle> Vehicles)
{
    public int Shield { get; init; }
}

internal static class RuntimeCraftTemplateLoader
{
    public static RuntimeCraftTemplate? Read(YamlMappingNode? map)
    {
        if (map is null) return null;
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        if (map.TryGet("items", out var node) && node is YamlMappingNode itemMap)
            foreach (var pair in itemMap.Entries)
                if (pair.ScalarKey is { } key) items.Add(key, YamlValueReader.ReadInt32(pair.Value));
        return new(map.ReadString("name", ""), map.ReadInt32("fuel", 0), map.ReadInt32("damage", 0), map.ReadString("status", "STR_READY"),
            map.ReadInt32("excessFuel", 0), map.ReadBoolean("lowFuel", false),
            Array.AsReadOnly(Maps("weapons").Select(w => w.ReadString("type", "0") == "0" ? null :
                new RuntimeCraftTemplateWeapon(w.ReadString("type", ""), w.ReadInt32("ammo", 0), w.ReadBoolean("rearming", false), w.ReadBoolean("disabled", false))).ToArray()),
            new ReadOnlyDictionary<string, int>(items), Array.AsReadOnly(Maps("vehicles").Select(v =>
                new RuntimeCraftTemplateVehicle(v.ReadString("type", ""), v.ReadInt32("ammo", 0))
                {
                    Size = v.TryGet("size", out _) ? v.ReadInt32("size", 0) : null,
                    SpaceOccupied = v.TryGet("spaceOccupied", out _) ? v.ReadInt32("spaceOccupied", 0) : null,
                }).ToArray()))
        { Shield = map.ReadInt32("shield", 0) };

        IEnumerable<YamlMappingNode> Maps(string key) => map.TryGet(key, out var value) && value is YamlSequenceNode sequence
            ? sequence.Items.Select(v => v as YamlMappingNode ?? throw new InvalidDataException($"Starting craft {key} requires mappings.")) : [];
    }

}
