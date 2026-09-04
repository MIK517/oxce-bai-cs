using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.EquipmentProduction;

internal sealed class ResearchRuleLoader : TypedRuleFamilyLoader<ResearchBuilder, ResearchRule>
{
    public ResearchRuleLoader() : base(EquipmentYaml.Section("research")) { }
    protected override ResearchBuilder Create(UnresolvedRule rule) =>
        new(rule.Id, checked((rule.CreationOrdinal + 1) * 100));
    protected override void Apply(ResearchBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.Lookup = reader.ReadString("lookup", builder.Lookup);
        builder.Cutscene = reader.ReadString("cutscene", builder.Cutscene);
        builder.SpawnedItem = reader.ReadString("spawnedItem", builder.SpawnedItem);
        builder.SpawnedItemCount = reader.ReadInt32("spawnedItemCount", builder.SpawnedItemCount);
        EquipmentYaml.EditableNames(reader, "spawnedItemList", builder.SpawnedItemList);
        EquipmentYaml.EditableNames(reader, "decreaseCounter", builder.DecreaseCounters);
        EquipmentYaml.EditableNames(reader, "increaseCounter", builder.IncreaseCounters);
        builder.SpawnedEvent = reader.ReadString("spawnedEvent", builder.SpawnedEvent);
        ProductionYaml.ApplyWeights(reader, "events", builder.Events);
        builder.Cost = reader.ReadInt32("cost", builder.Cost);
        builder.Points = reader.ReadInt32("points", builder.Points);
        EquipmentYaml.EditableNames(reader, "dependencies", builder.Dependencies);
        EquipmentYaml.EditableNames(reader, "unlocks", builder.Unlocks);
        EquipmentYaml.EditableNames(reader, "disables", builder.Disables);
        EquipmentYaml.EditableNames(reader, "reenables", builder.Reenables);
        EquipmentYaml.EditableNames(reader, "getOneFree", builder.GetOneFree);
        EquipmentYaml.EditableNames(reader, "requires", builder.Requirements);
        EquipmentYaml.EditableNames(reader, "requiresBaseFunc", builder.RequiredBaseFunctions, unique: true);
        builder.SequentialGetOneFree = reader.ReadBoolean("sequentialGetOneFree", builder.SequentialGetOneFree);
        if (reader.TryGet("getOneFreeProtected", out var protectedTopics))
            ProductionYaml.ApplyProtectedTopics(builder.GetOneFreeProtected, protectedTopics!);
        if (reader.TryGet("neededItem", out var neededItem))
            builder.NeededItem = neededItem is YamlNullNode ? null : YamlValueReader.ReadString(neededItem!);
        builder.NeedItem = reader.ReadBoolean("needItem", builder.NeedItem);
        builder.DestroyItem = reader.ReadBoolean("destroyItem", builder.DestroyItem);
        builder.ReturnsItem = reader.ReadBoolean("returnsItem", builder.ReturnsItem);
        builder.UnlockFinalMission = reader.ReadBoolean("unlockFinalMission", builder.UnlockFinalMission);
        builder.Repeatable = reader.ReadBoolean("repeatable", builder.Repeatable);
        builder.ListOrder = reader.ReadInt32("listOrder", builder.ListOrder);
        reader.DeferRemaining("dynamic research script values require Phase 4 registration");
    }
    protected override ResearchRule Freeze(ResearchBuilder builder) => new(
        builder.Lookup,
        builder.Cutscene,
        builder.SpawnedItem,
        builder.SpawnedItemCount,
        builder.SpawnedEvent,
        new ReadOnlyDictionary<string, ulong>(builder.Events),
        builder.Cost,
        builder.Points,
        builder.SpawnedItemList.AsReadOnly(),
        builder.DecreaseCounters.AsReadOnly(),
        builder.IncreaseCounters.AsReadOnly(),
        builder.Dependencies.AsReadOnly(),
        builder.Unlocks.AsReadOnly(),
        builder.Disables.AsReadOnly(),
        builder.Reenables.AsReadOnly(),
        builder.GetOneFree.AsReadOnly(),
        builder.Requirements.AsReadOnly(),
        builder.RequiredBaseFunctions.AsReadOnly(),
        builder.SequentialGetOneFree,
        Array.AsReadOnly(builder.GetOneFreeProtected.Select(value =>
            new ResearchProtectedTopics(value.Prerequisite, value.Topics.AsReadOnly())).ToArray()),
        builder.NeededItem,
        builder.NeedItem,
        builder.DestroyItem,
        builder.ReturnsItem,
        builder.UnlockFinalMission,
        builder.Repeatable,
        builder.ListOrder);
}

internal sealed class ManufactureRuleLoader : TypedRuleFamilyLoader<ManufactureBuilder, ManufactureRule>
{
    public ManufactureRuleLoader() : base(EquipmentYaml.Section("manufacture")) { }
    protected override ManufactureBuilder Create(UnresolvedRule rule) =>
        new(rule.Id, checked((rule.CreationOrdinal + 1) * 100));
    protected override void Apply(ManufactureBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.Category = reader.ReadString("category", builder.Category);
        EquipmentYaml.EditableNames(reader, "requires", builder.Requirements);
        EquipmentYaml.EditableNames(reader, "requiresBaseFunc", builder.RequiredBaseFunctions, unique: true);
        builder.Space = reader.ReadInt32("space", builder.Space);
        builder.Time = reader.ReadInt32("time", builder.Time);
        builder.Cost = reader.ReadInt32("cost", builder.Cost);
        builder.Points = reader.ReadInt32("points", builder.Points);
        builder.Refund = reader.ReadBoolean("refund", builder.Refund);
        ProductionYaml.ApplyEditableIntMap(reader, "requiredItems", builder.RequiredItems);
        ProductionYaml.ApplyEditableIntMap(reader, "producedItems", builder.ProducedItems);
        if (reader.TryGet("randomProducedItems", out var randomItems))
            builder.RandomProducedItems = ProductionYaml.ReadRandomProducedItems(randomItems!);
        builder.SpawnedPersonType = reader.ReadString("spawnedPersonType", builder.SpawnedPersonType);
        builder.SpawnedPersonName = reader.ReadString("spawnedPersonName", builder.SpawnedPersonName);
        if (reader.TryGet("spawnedSoldier", out var spawnedSoldier))
        {
            if (spawnedSoldier is not YamlMappingNode mapping)
                throw new YamlFormatException("Manufacture spawnedSoldier must be a mapping.", spawnedSoldier!.Span);
            builder.SpawnedSoldierTemplate = ProductionYaml.Overlay(mapping, builder.SpawnedSoldierTemplate);
        }
        if (reader.TryGet("transferTimes", out var transferTimes))
            builder.TransferTimes = YamlValueReader.ReadSequence(transferTimes!, YamlValueReader.ReadInt32).ToList();
        ProductionYaml.ApplyWeights(reader, "events", builder.Events);
        builder.ListOrder = reader.ReadInt32("listOrder", builder.ListOrder);
    }
    protected override ManufactureRule Freeze(ManufactureBuilder builder) => new(
        builder.Category,
        builder.Requirements.AsReadOnly(),
        builder.RequiredBaseFunctions.AsReadOnly(),
        builder.Space,
        builder.Time,
        builder.Cost,
        builder.Points,
        builder.Refund,
        EquipmentReadOnly.Dictionary(builder.RequiredItems),
        EquipmentReadOnly.Dictionary(builder.ProducedItems),
        builder.RandomProducedItems.AsReadOnly(),
        builder.SpawnedPersonType,
        builder.SpawnedPersonName,
        builder.SpawnedSoldierTemplate,
        builder.TransferTimes.AsReadOnly(),
        new ReadOnlyDictionary<string, ulong>(builder.Events),
        builder.ListOrder);
}

internal sealed class ManufactureShortcutRuleLoader :
    IdOnlyTypedRuleFamilyLoader<ManufactureShortcutBuilder, ManufactureShortcutRule>
{
    public ManufactureShortcutRuleLoader() : base(EquipmentYaml.Section("manufactureShortcut")) { }
    protected override ManufactureShortcutBuilder Create(string id) => new(id);
    protected override void Apply(ManufactureShortcutBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.StartFrom = reader.ReadString("startFrom", builder.StartFrom);
        if (reader.TryGet("breakDownItems", out var items))
            builder.BreakDownItems = EquipmentYaml.StringList(items!);
        builder.BreakDownRequirements = reader.ReadBoolean("breakDownRequires", builder.BreakDownRequirements);
        builder.BreakDownRequiredBaseFunctions = reader.ReadBoolean(
            "breakDownRequiresBaseFunc", builder.BreakDownRequiredBaseFunctions);
    }
    protected override ManufactureShortcutRule Freeze(ManufactureShortcutBuilder builder) => new(
        builder.StartFrom,
        builder.BreakDownItems.AsReadOnly(),
        builder.BreakDownRequirements,
        builder.BreakDownRequiredBaseFunctions);
}

internal static class ProductionYaml
{
    public static void ApplyWeights(RulePropertyReader reader, string key, SortedDictionary<string, ulong> target)
    {
        if (!reader.TryGet(key, out var node)) return;
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException($"{key} must be a mapping.", node!.Span);
        foreach (var entry in mapping.Entries)
        {
            var id = entry.ScalarKey ?? throw new YamlFormatException($"{key} keys must be scalars.", entry.Key.Span);
            var weight = YamlValueReader.ReadUInt64(entry.Value);
            if (weight == 0) target.Remove(id); else target[id] = weight;
        }
    }

    public static void ApplyEditableIntMap(
        RulePropertyReader reader, string key, Dictionary<string, int> target)
    {
        if (!reader.TryGet(key, out var node)) return;
        if (node!.Tag == "!remove")
        {
            if (node is not YamlSequenceNode remove)
                throw new YamlFormatException($"{key} !remove must be a sequence.", node.Span);
            foreach (var value in remove.Items) target.Remove(YamlValueReader.ReadString(value));
            return;
        }
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException($"{key} must be a mapping.", node.Span);
        if (node.Tag is null or "!!map" or "!info") target.Clear();
        else if (node.Tag != "!add") throw new YamlFormatException($"Unsupported collection tag '{node.Tag}'.", node.Span);
        foreach (var entry in mapping.Entries)
        {
            var id = entry.ScalarKey ?? throw new YamlFormatException($"{key} keys must be scalars.", entry.Key.Span);
            target[id] = YamlValueReader.ReadInt32(entry.Value);
        }
    }

    public static void ApplyProtectedTopics(List<ResearchProtectedBuilder> target, YamlNode node)
    {
        if (node.Tag == "!remove")
        {
            if (node is not YamlSequenceNode remove)
                throw new YamlFormatException("getOneFreeProtected !remove must be a sequence.", node.Span);
            var names = remove.Items.Select(YamlValueReader.ReadString).ToHashSet(StringComparer.Ordinal);
            target.RemoveAll(value => names.Contains(value.Prerequisite));
            return;
        }
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException("getOneFreeProtected must be a mapping.", node.Span);
        if (node.Tag is null or "!!map" or "!info") target.Clear();
        else if (node.Tag != "!add") throw new YamlFormatException($"Unsupported collection tag '{node.Tag}'.", node.Span);
        foreach (var entry in mapping.Entries)
        {
            var prerequisite = entry.ScalarKey ?? throw new YamlFormatException(
                "getOneFreeProtected keys must be scalars.", entry.Key.Span);
            var existing = target.FirstOrDefault(value => value.Prerequisite == prerequisite);
            if (existing is null)
            {
                existing = new ResearchProtectedBuilder(prerequisite, []);
                target.Add(existing);
            }
            ApplyEditableStringNode(existing.Topics, entry.Value);
        }
    }

    public static List<RandomProducedItems> ReadRandomProducedItems(YamlNode node)
    {
        if (node is not YamlSequenceNode sequence)
            throw new YamlFormatException("randomProducedItems must be a sequence.", node.Span);
        return sequence.Items.Select(value =>
        {
            var pair = YamlValueReader.ReadPair(value, YamlValueReader.ReadInt32, ReadStringIntMap);
            return new RandomProducedItems(pair.First, EquipmentReadOnly.Dictionary(pair.Second));
        }).ToList();
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

    private static Dictionary<string, int> ReadStringIntMap(YamlNode node)
    {
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException("Random produced item set must be a mapping.", node.Span);
        return YamlValueReader.ReadMap(
            mapping, YamlValueReader.ReadString, YamlValueReader.ReadInt32, StringComparer.Ordinal).ToDictionary();
    }

    private static void ApplyEditableStringNode(List<string> target, YamlNode node)
    {
        if (node is not YamlSequenceNode)
            throw new YamlFormatException("Protected research topics must be a sequence.", node.Span);
        var values = YamlValueReader.ReadSequence(node, YamlValueReader.ReadString);
        switch (node.Tag)
        {
            case null:
            case "!!seq":
            case "!info": target.Clear(); target.AddRange(values); break;
            case "!add": target.AddRange(values); break;
            case "!remove": foreach (var value in values) target.RemoveAll(item => item == value); break;
            default: throw new YamlFormatException($"Unsupported collection tag '{node.Tag}'.", node.Span);
        }
    }
}
