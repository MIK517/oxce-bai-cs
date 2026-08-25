using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Loading;

namespace Oxce.Mods.Rulesets.Presentation;

internal static class PresentationSpecialRulesComposer
{
    public static PresentationSpecialRules Compose(ModLoadPlan plan, RulesetCompositionOptions options)
    {
        var strings = new Dictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        var sprites = new Dictionary<string, List<ExtraSpriteDeclaration>>(StringComparer.Ordinal);
        var sounds = new List<ExtraSoundDeclaration>();
        var operations = 0;

        foreach (var group in plan.Groups)
        {
            foreach (var file in group.Rulesets)
            {
                using var input = file.OpenRead();
                var stream = YamlCompatibilityReader.Parse(input, file.SourcePath, options.Yaml);
                if (stream.Documents.Count != 1)
                {
                    throw new YamlFormatException("Ruleset files must contain exactly one YAML document.",
                        stream.Documents.Count == 0 ? UnknownSpan(file.SourcePath) : stream.Documents[1].Span);
                }

                if (stream.Documents[0].Root is YamlNullNode)
                {
                    continue;
                }

                if (stream.Documents[0].Root is not YamlMappingNode root)
                {
                    throw new YamlFormatException("Ruleset document root must be a mapping.", stream.Documents[0].Root.Span);
                }

                ReadSequence(root, "extraStrings", item =>
                {
                    Count(item);
                    ReadStrings(item, strings);
                });
                ReadSequence(root, "extraSprites", item =>
                {
                    Count(item);
                    ReadSprite(item, group.Mod.Metadata.Id, file.Provenance.LayerId, file.SourcePath, sprites);
                });
                ReadSequence(root, "extraSounds", item =>
                {
                    Count(item);
                    sounds.Add(ReadSound(item, group.Mod.Metadata.Id, file.Provenance.LayerId, file.SourcePath));
                });
            }
        }

        return new PresentationSpecialRules(strings, sprites, sounds);

        void Count(YamlMappingNode item)
        {
            operations = checked(operations + 1);
            if (operations > options.MaximumRuleOperations)
            {
                throw new YamlFormatException(
                    $"Ruleset input exceeds the {options.MaximumRuleOperations}-operation limit.", item.Span);
            }
        }
    }

    private static void ReadSequence(YamlMappingNode root, string key, Action<YamlMappingNode> read)
    {
        if (!root.TryGet(key, out var node)) return;
        if (node is not YamlSequenceNode sequence)
        {
            throw new YamlFormatException($"Rule section '{key}' must be a sequence.", node!.Span);
        }
        foreach (var item in sequence.Items)
        {
            if (item is not YamlMappingNode mapping)
            {
                throw new YamlFormatException($"Entries in rule section '{key}' must be mappings.", item.Span);
            }
            read(mapping);
        }
    }

    private static void ReadStrings(
        YamlMappingNode item,
        Dictionary<string, SortedDictionary<string, string>> destination)
    {
        var type = RequiredString(item, "type");
        if (!destination.TryGetValue(type, out var language))
        {
            language = new SortedDictionary<string, string>(StringComparer.Ordinal);
            destination.Add(type, language);
        }

        if (!item.TryGet("strings", out var node) || node is not YamlMappingNode mapping)
        {
            return;
        }

        foreach (var entry in mapping.Entries)
        {
            var key = YamlValueReader.ReadString(entry.Key);
            if (entry.Value is YamlMappingNode plural)
            {
                foreach (var form in plural.Entries)
                {
                    language[key + "_" + YamlValueReader.ReadString(form.Key)] =
                        YamlValueReader.ReadString(form.Value);
                }
            }
            else
            {
                language[key] = YamlValueReader.ReadString(entry.Value);
            }
        }
    }

    private static void ReadSprite(
        YamlMappingNode item,
        string modId,
        string layerId,
        string sourcePath,
        Dictionary<string, List<ExtraSpriteDeclaration>> destination)
    {
        if (item.TryGet("delete", out var deleted))
        {
            destination.Remove(YamlValueReader.ReadString(deleted!));
            return;
        }

        var type = OptionalString(item, "type", string.Empty);
        var single = false;
        var files = new SortedDictionary<int, string>();
        if (type.Length == 0)
        {
            type = OptionalString(item, "typeSingle", string.Empty);
            single = type.Length != 0;
            var fileSingle = OptionalString(item, "fileSingle", string.Empty);
            if (fileSingle.Length != 0) files[0] = fileSingle;
        }
        if (type.Length == 0)
        {
            throw new YamlFormatException("extraSprites entry is missing 'type' or 'typeSingle'.", item.Span);
        }
        if (item.TryGet("files", out var fileNode))
        {
            files = YamlValueReader.ReadMap(
                fileNode!, YamlValueReader.ReadInt32, YamlValueReader.ReadString);
        }

        var declaration = new ExtraSpriteDeclaration(
            type,
            OptionalInt(item, "width", 320),
            OptionalInt(item, "height", 200),
            OptionalBoolean(item, "singleImage", single),
            OptionalInt(item, "subX", 0),
            OptionalInt(item, "subY", 0),
            new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(files),
            Source(item, modId, layerId, sourcePath));
        if (!destination.TryGetValue(type, out var list))
        {
            list = [];
            destination.Add(type, list);
        }
        list.Add(declaration);
    }

    private static ExtraSoundDeclaration ReadSound(
        YamlMappingNode item,
        string modId,
        string layerId,
        string sourcePath)
    {
        var type = RequiredString(item, "type");
        var files = item.TryGet("files", out var node)
            ? YamlValueReader.ReadMap(node!, YamlValueReader.ReadInt32, YamlValueReader.ReadString)
            : new SortedDictionary<int, string>();
        return new ExtraSoundDeclaration(
            type,
            new System.Collections.ObjectModel.ReadOnlyDictionary<int, string>(files),
            Source(item, modId, layerId, sourcePath));
    }

    private static RuleOperationSource Source(YamlMappingNode item, string modId, string layerId, string sourcePath) =>
        new(layerId, modId, sourcePath, item.Span);

    private static string RequiredString(YamlMappingNode mapping, string key) =>
        mapping.TryGet(key, out var node) && node is not YamlNullNode
            ? YamlValueReader.ReadString(node!)
            : throw new YamlFormatException($"Rule entry is missing '{key}'.", mapping.Span);

    private static string OptionalString(YamlMappingNode mapping, string key, string defaultValue) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadString(node!) : defaultValue;

    private static int OptionalInt(YamlMappingNode mapping, string key, int defaultValue) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadInt32(node!) : defaultValue;

    private static bool OptionalBoolean(YamlMappingNode mapping, string key, bool defaultValue) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadBoolean(node!) : defaultValue;

    private static SourceSpan UnknownSpan(string sourcePath)
    {
        var position = new SourcePosition(1, 1, 0);
        return new SourceSpan(sourcePath, position, position);
    }
}
