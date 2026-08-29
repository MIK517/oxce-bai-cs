using System.Collections.ObjectModel;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Loading;

namespace Oxce.Mods.Rulesets.TerrainDeployment;

internal static class TerrainSpecialRulesComposer
{
    private static readonly HashSet<string> Commands = new(StringComparer.Ordinal)
    { "addBlock", "addLine", "addCraft", "addUFO", "digTunnel", "fillArea", "checkBlock", "removeBlock", "resize" };
    private static readonly string[] PatchIntegers =
    { "bigWall", "TUWalk", "TUFly", "TUSlide", "deathTile", "terrainHeight", "specialType", "explosive", "armor", "flammability", "fuel", "footstepSound", "HEBlock", "objectType" };

    public static TerrainSpecialRules Compose(ModLoadPlan plan, RulesetCompositionOptions options)
        => Compose(RulesetDocumentCatalog.Parse(plan, options), options);

    public static TerrainSpecialRules Compose(RulesetDocumentCatalog documents, RulesetCompositionOptions options)
    {
        var scripts = new Dictionary<string, MapScriptRule>(StringComparer.Ordinal);
        var patches = new Dictionary<string, List<McdPatchEntry>>(StringComparer.Ordinal);
        var patchSources = new Dictionary<string, RuleOperationSource>(StringComparer.Ordinal);
        var operations = 0;
        foreach (var document in documents.Documents)
        {
            var root = document.Root;
            ReadSection(root, "mapScripts", item =>
            {
                Count(item); var source = Source(item, document.Mod.Metadata.Id,
                    document.File.Provenance.LayerId, document.File.SourcePath);
                var id = RequiredString(item, "type");
                if (item.TryGet("delete", out var deleted)) id = YamlValueReader.ReadString(deleted!);
                scripts[id] = new MapScriptRule(id, ReadCommands(item), source);
            });
            ReadSection(root, "MCDPatches", item =>
            {
                Count(item); var id = RequiredString(item, "type");
                if (!patches.TryGetValue(id, out var entries)) patches[id] = entries = [];
                entries.AddRange(ReadPatchEntries(item));
                patchSources[id] = Source(item, document.Mod.Metadata.Id,
                    document.File.Provenance.LayerId, document.File.SourcePath);
            });
        }
        return new TerrainSpecialRules(
            new ReadOnlyDictionary<string, MapScriptRule>(scripts),
            new ReadOnlyDictionary<string, McdPatchRule>(patches.ToDictionary(pair => pair.Key,
                pair => new McdPatchRule(pair.Key, pair.Value.AsReadOnly(), patchSources[pair.Key]), StringComparer.Ordinal)));

        void Count(YamlMappingNode item)
        {
            operations = checked(operations + 1);
            if (operations > options.MaximumRuleOperations) throw Error(item.Span,
                $"Ruleset input exceeds the {options.MaximumRuleOperations}-operation limit.");
        }
    }

    private static ReadOnlyCollection<MapScriptCommand> ReadCommands(YamlMappingNode rule)
    {
        if (!rule.TryGet("commands", out var node)) return Array.AsReadOnly(Array.Empty<MapScriptCommand>());
        if (node is not YamlSequenceNode sequence) throw Error(node!.Span, "mapScripts commands must be a sequence.");
        return Array.AsReadOnly(sequence.Items.Select(ReadCommand).ToArray());
    }

    private static MapScriptCommand ReadCommand(YamlNode node)
    {
        if (node is not YamlMappingNode map) throw Error(node.Span, "Map script commands must be mappings.");
        var name = RequiredString(map, "type");
        if (!Commands.Contains(name)) throw Error(node.Span, $"Unknown map script command '{name}'.");
        var type = name switch
        {
            "addBlock" => MapScriptCommandType.AddBlock,
            "addLine" => MapScriptCommandType.AddLine,
            "addCraft" => MapScriptCommandType.AddCraft,
            "addUFO" => MapScriptCommandType.AddUfo,
            "digTunnel" => MapScriptCommandType.DigTunnel,
            "fillArea" => MapScriptCommandType.FillArea,
            "checkBlock" => MapScriptCommandType.CheckBlock,
            "removeBlock" => MapScriptCommandType.RemoveBlock,
            _ => MapScriptCommandType.Resize,
        };
        var groups = ReadInts(map, "groups", type is MapScriptCommandType.AddCraft or MapScriptCommandType.AddUfo ? [1] : []);
        var blocks = ReadInts(map, "blocks", []);
        var selectionSize = map.TryGet("blocks", out _) ? blocks.Count : groups.Count;
        var frequencies = Enumerable.Repeat(1, selectionSize).ToList();
        var maximumUses = Enumerable.Repeat(-1, selectionSize).ToList();
        OverlayFixed(map, "freqs", frequencies); OverlayFixed(map, "maxUses", maximumUses);
        var direction = ReadDirection(map, type);
        var size = type == MapScriptCommandType.Resize ? new List<int> { 0, 0, 0 } : new List<int> { 1, 1, 0 };
        if (map.TryGet("size", out var sizeNode))
        {
            var read = TerrainDeploymentYaml.Integers(sizeNode!, "size");
            if (sizeNode is YamlScalarNode) size = [read[0], read[0], size[2]];
            else for (var i = 0; i < Math.Min(3, read.Count); i++) size[i] = read[i];
        }
        var terrain = map.TryGet("terrain", out var terrainNode) ? new List<string> { YamlValueReader.ReadString(terrainNode!) } : [];
        if (map.TryGet("randomTerrain", out var random)) terrain = TerrainDeploymentYaml.Strings(random!, "randomTerrain");
        return new(type, ReadRectangles(map), ReadInts(map, "craftGroups", []), ReadInts(map, "conditionals", []), size.AsReadOnly(),
            groups.AsReadOnly(), blocks.AsReadOnly(), frequencies.AsReadOnly(), maximumUses.AsReadOnly(), direction,
            ReadInt(map, "verticalGroup", 1), ReadInt(map, "horizontalGroup", 2), ReadInt(map, "crossingGroup", 3),
            ReadBool(map, "canBeSkipped", true), ReadBool(map, "markAsReinforcementsBlock", false),
            ReadInt(map, "executionChances", 100), ReadInt(map, "executions", 1), ReadString(map, "UFOName", ""),
            ReadString(map, "craftName", ""), terrain.AsReadOnly(), Math.Abs(ReadInt(map, "label", 0)),
            Get(map, "tunnelData"), Get(map, "verticalLevels"));
    }

    private static ReadOnlyCollection<MapRectangle> ReadRectangles(YamlMappingNode map)
    {
        if (!map.TryGet("rects", out var node)) return Array.AsReadOnly(Array.Empty<MapRectangle>());
        if (node is not YamlSequenceNode sequence) throw Error(node!.Span, "rects must be a sequence.");
        return Array.AsReadOnly(sequence.Items.Select(item =>
        {
            var values = TerrainDeploymentYaml.Integers(item, "rects");
            if (values.Count != 4) throw Error(item.Span, "Each map-script rectangle must have four values.");
            return new MapRectangle(values[0], values[1], values[2], values[3]);
        }).ToArray());
    }

    private static MapDirection ReadDirection(YamlMappingNode map, MapScriptCommandType type)
    {
        var result = MapDirection.None;
        if (map.TryGet("direction", out var node)) result = YamlValueReader.ReadString(node!) switch
        {
            var text when text.StartsWith("V", StringComparison.OrdinalIgnoreCase) => MapDirection.Vertical,
            var text when text.StartsWith("H", StringComparison.OrdinalIgnoreCase) => MapDirection.Horizontal,
            var text when text.StartsWith("B", StringComparison.OrdinalIgnoreCase) => MapDirection.Both,
            _ => throw Error(node!.Span, "direction must be Vertical, Horizontal, or Both."),
        };
        if (result == MapDirection.None && type is MapScriptCommandType.DigTunnel or MapScriptCommandType.AddLine)
            throw Error(map.Span, $"A direction is required for {type}.");
        return result;
    }

    private static IEnumerable<McdPatchEntry> ReadPatchEntries(YamlMappingNode rule)
    {
        if (!rule.TryGet("data", out var node)) yield break;
        if (node is not YamlSequenceNode sequence) throw Error(node!.Span, "MCD patch data must be a sequence.");
        foreach (var item in sequence.Items)
        {
            if (item is not YamlMappingNode map) throw Error(item.Span, "MCD patch entries must be mappings.");
            if (!map.TryGet("MCDIndex", out var index)) throw Error(item.Span, "MCD patch entry is missing MCDIndex.");
            var integers = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var key in PatchIntegers) if (map.TryGet(key, out var value)) integers[key] = YamlValueReader.ReadInt32(value!);
            var booleans = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var key in new[] { "noFloor", "stopLOS" }) if (map.TryGet(key, out var value)) booleans[key] = YamlValueReader.ReadBoolean(value!);
            var lofts = map.TryGet("LOFTS", out var loftNode) ? TerrainDeploymentYaml.Integers(loftNode!, "LOFTS").AsReadOnly() : null;
            yield return new(YamlValueReader.ReadUInt64(index!), TerrainReadOnly.Dictionary(integers), TerrainReadOnly.Dictionary(booleans), lofts);
        }
    }

    private static void ReadSection(YamlMappingNode root, string key, Action<YamlMappingNode> read)
    {
        if (!root.TryGet(key, out var node)) return;
        if (node is not YamlSequenceNode sequence) throw Error(node!.Span, $"Rule section '{key}' must be a sequence.");
        foreach (var item in sequence.Items) read(item as YamlMappingNode ?? throw Error(item.Span, $"Entries in '{key}' must be mappings."));
    }
    private static void OverlayFixed(YamlMappingNode map, string key, List<int> target)
    {
        if (!map.TryGet(key, out var node)) return;
        var values = TerrainDeploymentYaml.Integers(node!, key);
        for (var i = 0; i < Math.Min(values.Count, target.Count); i++) target[i] = values[i];
    }
    private static List<int> ReadInts(YamlMappingNode map, string key, IReadOnlyList<int> fallback) =>
        map.TryGet(key, out var node) ? TerrainDeploymentYaml.Integers(node!, key) : fallback.ToList();
    private static int ReadInt(YamlMappingNode map, string key, int fallback) => map.TryGet(key, out var node) ? YamlValueReader.ReadInt32(node!) : fallback;
    private static bool ReadBool(YamlMappingNode map, string key, bool fallback) => map.TryGet(key, out var node) ? YamlValueReader.ReadBoolean(node!) : fallback;
    private static string ReadString(YamlMappingNode map, string key, string fallback) => map.TryGet(key, out var node) ? YamlValueReader.ReadString(node!) : fallback;
    private static string RequiredString(YamlMappingNode map, string key) => map.TryGet(key, out var node) && node is not YamlNullNode
        ? YamlValueReader.ReadString(node!) : throw Error(map.Span, $"Rule entry is missing '{key}'.");
    private static YamlNode? Get(YamlMappingNode map, string key) => map.TryGet(key, out var node) ? node : null;
    private static RuleOperationSource Source(YamlMappingNode node, string modId, string layerId, string path) => new(layerId, modId, path, node.Span);
    private static YamlFormatException Error(SourceSpan span, string message) => new(message, span);
    private static SourceSpan Unknown(string path) { var p = new SourcePosition(1, 1, 0); return new(path, p, p); }
}

internal sealed record TerrainSpecialRules(
    IReadOnlyDictionary<string, MapScriptRule> MapScripts,
    IReadOnlyDictionary<string, McdPatchRule> McdPatches);
