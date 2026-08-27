using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.PersonnelTactical;

internal sealed class SoldierBonusRuleLoader : TypedRuleFamilyLoader<SoldierBonusBuilder, SoldierBonusRule>
{
    public SoldierBonusRuleLoader() : base(PersonnelTacticalYaml.Section("soldierBonuses")) { }
    protected override SoldierBonusBuilder Create(string id) => throw new NotSupportedException();
    protected override SoldierBonusBuilder Create(UnresolvedRule rule) =>
        new(rule.Id, checked((rule.CreationOrdinal + 1) * 100));
    protected override void Apply(SoldierBonusBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        foreach (var key in builder.Integers.Keys.ToArray())
            builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        if (reader.TryGet("stats", out var stats))
            PersonnelTacticalYaml.ApplyStats(builder.Stats, stats!, nonZeroMerge: true);
        builder.ListOrder = reader.ReadInt32("listOrder", builder.ListOrder);
        reader.Defer("recovery", "soldier recovery stat-bonus compilation belongs to Phase 4");
        reader.DeferRemaining("dynamic soldier-bonus script values require Phase 4 registration");
    }
    protected override SoldierBonusRule Freeze(SoldierBonusBuilder builder) => new(
        PersonnelReadOnly.Dictionary(builder.Integers),
        PersonnelTacticalYaml.FreezeStats(builder.Stats),
        builder.ListOrder);
}

internal sealed class SoldierTransformationRuleLoader :
    TypedRuleFamilyLoader<TransformationBuilder, SoldierTransformationRule>
{
    public SoldierTransformationRuleLoader() : base(PersonnelTacticalYaml.Section("soldierTransformation")) { }
    protected override TransformationBuilder Create(string id) => throw new NotSupportedException();
    protected override TransformationBuilder Create(UnresolvedRule rule) =>
        new(rule.Id, checked((rule.CreationOrdinal + 1) * 100));
    protected override void Apply(TransformationBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        foreach (var key in builder.Strings.Keys.ToArray())
            builder.Strings[key] = reader.ReadString(key, builder.Strings[key]);
        foreach (var key in builder.Integers.Keys.ToArray())
            builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        foreach (var key in builder.Booleans.Keys.ToArray())
            builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        PersonnelTacticalYaml.EditableNames(reader, "requires", builder.Requirements);
        PersonnelTacticalYaml.EditableNames(reader, "requiresBaseFunc", builder.RequiredBaseFunctions, unique: true);
        PersonnelTacticalYaml.EditableNames(reader, "allowedSoldierTypes", builder.AllowedSoldierTypes);
        PersonnelTacticalYaml.EditableNames(
            reader, "requiredPreviousTransformations", builder.RequiredPreviousTransformations);
        PersonnelTacticalYaml.EditableNames(
            reader, "forbiddenPreviousTransformations", builder.ForbiddenPreviousTransformations);
        PersonnelTacticalYaml.EditableNames(reader, "removeTransformations", builder.RemovedTransformations);
        PersonnelTacticalYaml.ApplyEditableIntMap(reader, "requiredItems", builder.RequiredItems);
        PersonnelTacticalYaml.ApplyEditableIntMap(reader, "requiredCommendations", builder.RequiredCommendations);
        foreach (var key in TransformationBuilder.StatSetKeys)
        {
            if (!reader.TryGet(key, out var stats)) continue;
            PersonnelTacticalYaml.ApplyStats(
                builder.StatSets[key], stats!, nonZeroMerge: key == "requiredMaxStats");
        }
        if (reader.TryGet("events", out var events))
            PersonnelTacticalYaml.ApplyWeights(builder.Events, events!, "events");
    }
    protected override SoldierTransformationRule Freeze(TransformationBuilder builder) => new(
        PersonnelReadOnly.Dictionary(builder.Strings),
        PersonnelReadOnly.Dictionary(builder.Integers),
        PersonnelReadOnly.Dictionary(builder.Booleans),
        builder.Requirements.AsReadOnly(),
        builder.RequiredBaseFunctions.AsReadOnly(),
        builder.AllowedSoldierTypes.AsReadOnly(),
        builder.RequiredPreviousTransformations.AsReadOnly(),
        builder.ForbiddenPreviousTransformations.AsReadOnly(),
        builder.RemovedTransformations.AsReadOnly(),
        PersonnelReadOnly.Dictionary(builder.RequiredItems),
        PersonnelReadOnly.Dictionary(builder.RequiredCommendations),
        new ReadOnlyDictionary<string, UnitStatsRule>(builder.StatSets.ToDictionary(
            pair => pair.Key, pair => PersonnelTacticalYaml.FreezeStats(pair.Value), StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, ulong>(builder.Events));
}

internal sealed class CommendationRuleLoader : TypedRuleFamilyLoader<CommendationBuilder, CommendationRule>
{
    public CommendationRuleLoader() : base(PersonnelTacticalYaml.Section("commendations")) { }
    protected override CommendationBuilder Create(string id) => new(id);
    protected override void Apply(CommendationBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.Description = reader.ReadString("description", builder.Description);
        builder.Sprite = reader.ReadInt32("sprite", builder.Sprite);
        ApplyCriteria(builder, reader);
        ApplyKillCriteria(builder, reader);
        PersonnelTacticalYaml.EditableNames(reader, "soldierBonusTypes", builder.SoldierBonusTypes);
        PersonnelTacticalYaml.EditableNames(reader, "missionMarkerFilter", builder.MissionMarkers);
        PersonnelTacticalYaml.EditableNames(reader, "missionTypeFilter", builder.MissionTypes);
        PersonnelTacticalYaml.EditableNames(reader, "requires", builder.Requirements);
        PersonnelTacticalYaml.EditableNames(reader, "units", builder.Units);
    }
    protected override CommendationRule Freeze(CommendationBuilder builder) => new(
        builder.Description,
        builder.Sprite,
        new ReadOnlyDictionary<string, IReadOnlyList<int>>(builder.Criteria.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<int>)pair.Value.AsReadOnly(), StringComparer.Ordinal)),
        Array.AsReadOnly(builder.KillCriteria.Select(value =>
            (IReadOnlyList<CommendationKillCriterion>)value.AsReadOnly()).ToArray()),
        builder.SoldierBonusTypes.AsReadOnly(),
        builder.MissionMarkers.AsReadOnly(),
        builder.MissionTypes.AsReadOnly(),
        builder.Requirements.AsReadOnly(),
        builder.Units.AsReadOnly());

    private static void ApplyCriteria(CommendationBuilder builder, RulePropertyReader reader)
    {
        if (!reader.TryGet("criteria", out var node)) return;
        PersonnelTacticalYaml.ApplyEditableMap(
            builder.Criteria,
            node!,
            YamlValueReader.ReadString,
            value => PersonnelTacticalYaml.IntegerList(value, "criteria"),
            "criteria");
    }

    private static void ApplyKillCriteria(CommendationBuilder builder, RulePropertyReader reader)
    {
        if (!reader.TryGet("killCriteria", out var node)) return;
        if (node is not YamlSequenceNode sequence)
            throw new YamlFormatException("killCriteria must be a sequence.", node!.Span);
        if (node!.Tag is null or "!!seq" or "!info") builder.KillCriteria.Clear();
        else if (node.Tag != "!add")
            throw new YamlFormatException($"Unsupported collection tag '{node.Tag}'.", node.Span);
        foreach (var group in sequence.Items)
        {
            if (group is not YamlSequenceNode groupSequence)
                throw new YamlFormatException("Each killCriteria group must be a sequence.", group.Span);
            builder.KillCriteria.Add(groupSequence.Items.Select(ReadKillCriterion).ToList());
        }
    }

    private static CommendationKillCriterion ReadKillCriterion(YamlNode node)
    {
        var pair = YamlValueReader.ReadPair(
            node,
            YamlValueReader.ReadInt32,
            value => PersonnelTacticalYaml.StringList(value, "killCriteria values"));
        return new(pair.First, pair.Second.AsReadOnly());
    }
}
