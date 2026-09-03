using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.PersonnelTactical;

internal sealed class SkillRuleLoader : TypedRuleFamilyLoader<SkillBuilder, SkillRule>
{
    public SkillRuleLoader() : base(PersonnelTacticalYaml.Section("skills")) { }
    protected override SkillBuilder Create(string id) => new(id);
    protected override void Apply(SkillBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        var targetMode = reader.ReadInt32("targetMode", builder.TargetMode);
        builder.TargetMode = targetMode is >= 0 and <= 16 ? targetMode : 0;
        var battleType = reader.ReadInt32("battleType", builder.BattleType);
        builder.BattleType = battleType is >= 0 and <= 11 ? battleType : 0;
        builder.IsPsiRequired = reader.ReadBoolean("isPsiRequired", builder.IsPsiRequired);
        builder.CheckHandsOnly = reader.ReadBoolean("checkHandsOnly", builder.CheckHandsOnly);
        builder.CheckHandsOnly2 = reader.ReadBoolean("checkHandsOnly2", builder.CheckHandsOnly2);
        PersonnelTacticalYaml.ApplyCost(reader, builder.Cost);
        PersonnelTacticalYaml.ApplyFlat(reader, builder.Flat);
        PersonnelTacticalYaml.EditableNames(reader, "compatibleWeapons", builder.CompatibleWeapons);
        PersonnelTacticalYaml.EditableNames(reader, "requiredBonuses", builder.RequiredBonuses);
        reader.DeferRemaining("dynamic skill script values require Phase 4 registration");
    }
    protected override SkillRule Freeze(SkillBuilder builder) => new(
        builder.TargetMode,
        builder.BattleType,
        builder.IsPsiRequired,
        builder.CheckHandsOnly,
        builder.CheckHandsOnly2,
        builder.Cost.Freeze(),
        builder.Flat.Freeze(),
        builder.CompatibleWeapons.AsReadOnly(),
        builder.RequiredBonuses.AsReadOnly());
}

internal sealed class SoldierRuleLoader : TypedRuleFamilyLoader<SoldierBuilder, SoldierRule>
{
    public SoldierRuleLoader() : base(PersonnelTacticalYaml.Section("soldiers")) { }
    protected override SoldierBuilder Create(string id) => throw new NotSupportedException();
    protected override SoldierBuilder Create(UnresolvedRule rule) => new(rule.Id, rule.CreationOrdinal + 1);
    protected override void Apply(SoldierBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        PersonnelTacticalYaml.EditableNames(reader, "requires", builder.Requirements);
        PersonnelTacticalYaml.EditableNames(reader, "requiresBuyBaseFunc", builder.RequiredBuyBaseFunctions, unique: true);
        foreach (var key in builder.Strings.Keys.ToArray())
            builder.Strings[key] = reader.ReadString(key, builder.Strings[key]);
        foreach (var key in builder.Integers.Keys.ToArray())
            builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        foreach (var key in builder.Booleans.Keys.ToArray())
            builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        ApplyStats(builder, reader);
        ApplyResources(builder, reader);
        ApplyNamesAndStrings(builder, reader);
        if (reader.TryGet("spawnedSoldier", out var spawned))
        {
            if (spawned is not YamlMappingNode mapping)
                throw new YamlFormatException("Soldier spawnedSoldier must be a mapping.", spawned!.Span);
            builder.SpawnedSoldierTemplate = PersonnelTacticalYaml.Overlay(mapping, builder.SpawnedSoldierTemplate);
        }
        reader.DeferRemaining("dynamic soldier script values require Phase 4 registration");
    }
    protected override SoldierRule Freeze(SoldierBuilder builder) => new(
        PersonnelReadOnly.Dictionary(builder.Strings),
        PersonnelReadOnly.Dictionary(builder.Integers),
        PersonnelReadOnly.Dictionary(builder.Booleans),
        builder.Requirements.AsReadOnly(),
        builder.RequiredBuyBaseFunctions.AsReadOnly(),
        PersonnelTacticalYaml.FreezeStats(builder.MinimumStats),
        PersonnelTacticalYaml.FreezeStats(builder.MaximumStats),
        PersonnelTacticalYaml.FreezeStats(builder.StatCaps),
        PersonnelTacticalYaml.FreezeStats(builder.TrainingStatCaps),
        PersonnelTacticalYaml.FreezeStats(builder.DogfightExperience),
        new ReadOnlyDictionary<string, IReadOnlyList<RuleIndexReference>>(builder.Sounds.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<RuleIndexReference>)pair.Value.AsReadOnly(), StringComparer.Ordinal)),
        PersonnelReadOnly.Dictionary(builder.Sprites),
        builder.SoldierNames.AsReadOnly(),
        builder.StatStrings.AsReadOnly(),
        builder.RankStrings.AsReadOnly(),
        builder.Skills.AsReadOnly(),
        builder.SpawnedSoldierTemplate);

    private static void ApplyStats(SoldierBuilder builder, RulePropertyReader reader)
    {
        if (reader.TryGet("minStats", out var minimum))
            PersonnelTacticalYaml.ApplyStats(builder.MinimumStats, minimum!, nonZeroMerge: true);
        if (reader.TryGet("maxStats", out var maximum))
            PersonnelTacticalYaml.ApplyStats(builder.MaximumStats, maximum!, nonZeroMerge: true);
        if (reader.TryGet("statCaps", out var caps))
            PersonnelTacticalYaml.ApplyStats(builder.StatCaps, caps!, nonZeroMerge: true);
        if (reader.TryGet("trainingStatCaps", out var training))
            PersonnelTacticalYaml.ApplyStats(builder.TrainingStatCaps, training!, nonZeroMerge: true);
        else if (caps is not null)
            PersonnelTacticalYaml.ApplyStats(builder.TrainingStatCaps, caps, nonZeroMerge: true);
        if (reader.TryGet("dogfightExperience", out var experience))
            PersonnelTacticalYaml.ApplyStats(builder.DogfightExperience, experience!, nonZeroMerge: true);
    }

    private static void ApplyResources(SoldierBuilder builder, RulePropertyReader reader)
    {
        foreach (var key in SoldierBuilder.SoundKeys)
            if (reader.TryGet(key, out var node))
                builder.Sounds[key] = PersonnelTacticalYaml.IndexList(node!, reader.Source.ModId);
        foreach (var key in builder.Sprites.Keys.ToArray())
            if (reader.TryGet(key, out var node))
                builder.Sprites[key] = PresentationYaml.ReadIndexReference(node!, reader.Source.ModId);
    }

    private static void ApplyNamesAndStrings(SoldierBuilder builder, RulePropertyReader reader)
    {
        if (reader.TryGet("soldierNames", out var names))
        {
            foreach (var node in (names as YamlSequenceNode)?.Items ?? [])
            {
                var value = YamlValueReader.ReadString(node);
                if (value == "delete") builder.SoldierNames.Clear(); else builder.SoldierNames.Add(value);
            }
        }
        if (reader.TryGet("statStrings", out var statStrings))
        {
            if (statStrings is YamlSequenceNode sequence) builder.StatStrings.AddRange(sequence.Items);
        }
        PersonnelTacticalYaml.EditableNames(reader, "rankStrings", builder.RankStrings);
        PersonnelTacticalYaml.EditableNames(reader, "skills", builder.Skills);
    }
}

internal sealed class UnitRuleLoader : TypedRuleFamilyLoader<UnitBuilder, UnitRule>
{
    public UnitRuleLoader() : base(PersonnelTacticalYaml.Section("units")) { }
    protected override UnitBuilder Create(string id) => new(id);
    protected override void Apply(UnitBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        foreach (var key in builder.Strings.Keys.ToArray())
        {
            if (!reader.TryGet(key, out var node)) continue;
            builder.Strings[key] = key is "civilianRecoveryType" or "spawnedPersonName" or "liveAlien"
                ? PersonnelTacticalYaml.ReadNullableName(node!)
                : YamlValueReader.ReadString(node!);
        }
        foreach (var key in builder.Integers.Keys.ToArray())
            builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        foreach (var key in builder.Booleans.Keys.ToArray())
            builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        if (reader.TryGet("avoidsFire", out var avoidsFire))
            builder.AvoidsFire = PersonnelTacticalYaml.ReadNullableBoolean(avoidsFire!);
        if (reader.TryGet("stats", out var stats)) PersonnelTacticalYaml.ApplyStats(builder.Stats, stats!, nonZeroMerge: true);
        if (builder.Integers["floatHeight"] + builder.Integers["standHeight"] > 25)
            throw new YamlFormatException($"Unit '{builder.Id}' height may not exceed 25.", reader.Source.Span);
        ApplyBuiltInWeapons(builder, reader);
        ApplyResources(builder, reader);
        if (reader.TryGet("spawnedSoldier", out var spawned))
        {
            if (spawned is not YamlMappingNode mapping)
                throw new YamlFormatException("Unit spawnedSoldier must be a mapping.", spawned!.Span);
            builder.SpawnedSoldierTemplate = PersonnelTacticalYaml.Overlay(mapping, builder.SpawnedSoldierTemplate);
        }

        // Unit::load in the reference engine does not read this legacy TFTD property.
        // Consume it without projecting a runtime relationship so strict loading matches that behavior.
        _ = reader.TryGet("zombieUnit", out _);
    }
    protected override UnitRule Freeze(UnitBuilder builder) => new(
        PersonnelReadOnly.Dictionary(builder.Strings),
        PersonnelReadOnly.Dictionary(builder.Integers),
        PersonnelReadOnly.Dictionary(builder.Booleans),
        builder.AvoidsFire,
        PersonnelTacticalYaml.FreezeStats(builder.Stats),
        Array.AsReadOnly(builder.BuiltInWeaponSets.Select(value => (IReadOnlyList<string>)value.AsReadOnly()).ToArray()),
        Array.AsReadOnly(builder.WeightedBuiltInWeaponSets.Select(value =>
            (IReadOnlyDictionary<string, ulong>)new ReadOnlyDictionary<string, ulong>(value)).ToArray()),
        new ReadOnlyDictionary<string, IReadOnlyList<RuleIndexReference>>(builder.Sounds.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<RuleIndexReference>)pair.Value.AsReadOnly(), StringComparer.Ordinal)),
        builder.MoveSound,
        builder.SpawnedSoldierTemplate);

    private static void ApplyBuiltInWeapons(UnitBuilder builder, RulePropertyReader reader)
    {
        if (reader.TryGet("builtInWeaponSets", out var sets))
        {
            builder.BuiltInWeaponSets = sets is YamlSequenceNode sequence
                ? sequence.Items.Select(ReadGenericStringVector).ToList()
                : [];
        }
        if (reader.TryGet("builtInWeapons", out var legacy))
            builder.BuiltInWeaponSets.Add(ReadGenericStringVector(legacy!));
        if (!reader.TryGet("weightedBuiltInWeaponSets", out var weighted)) return;
        if (weighted is not YamlSequenceNode weightedSequence) return;
        foreach (var item in weightedSequence.Items)
        {
            var options = new SortedDictionary<string, ulong>(StringComparer.Ordinal);
            PersonnelTacticalYaml.ApplyWeights(options, item, "weightedBuiltInWeaponSets");
            builder.WeightedBuiltInWeaponSets.Add(options);
        }
    }

    private static List<string> ReadGenericStringVector(YamlNode node) =>
        node is YamlSequenceNode sequence
            ? sequence.Items.Select(YamlValueReader.ReadString).ToList()
            : [];

    private static void ApplyResources(UnitBuilder builder, RulePropertyReader reader)
    {
        foreach (var key in UnitBuilder.SoundKeys)
            if (reader.TryGet(key, out var node))
                builder.Sounds[key] = PersonnelTacticalYaml.IndexList(node!, reader.Source.ModId);
        if (reader.TryGet("moveSound", out var moveSound))
            builder.MoveSound = PresentationYaml.ReadIndexReference(moveSound!, reader.Source.ModId);
    }
}
