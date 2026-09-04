using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.PersonnelTactical;

internal sealed class InventoryRuleLoader : TypedRuleFamilyLoader<InventoryBuilder, InventoryRule>
{
    public InventoryRuleLoader() : base(PersonnelTacticalYaml.Section("invs")) { }
    protected override InventoryBuilder Create(UnresolvedRule rule) =>
        new(rule.Id, checked((rule.CreationOrdinal + 1) * 10));
    protected override void Apply(InventoryBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.X = reader.ReadInt32("x", builder.X);
        builder.Y = reader.ReadInt32("y", builder.Y);
        builder.Type = reader.ReadInt32("type", builder.Type);
        builder.ListOrder = reader.ReadInt32("listOrder", builder.ListOrder);
        if (reader.TryGet("slots", out var slots)) builder.Slots = ReadSlots(slots!);
        if (reader.TryGet("costs", out var costs))
            builder.Costs = YamlValueReader.ReadMap(
                costs!, YamlValueReader.ReadString, YamlValueReader.ReadInt32, StringComparer.Ordinal).ToDictionary();
    }
    protected override InventoryRule Freeze(InventoryBuilder builder) => new(
        builder.X,
        builder.Y,
        builder.Type,
        builder.Slots.AsReadOnly(),
        PersonnelReadOnly.Dictionary(builder.Costs),
        builder.ListOrder,
        builder.Id == "STR_RIGHT_HAND" ? 2 : builder.Id == "STR_LEFT_HAND" ? 1 : 0);

    private static List<InventorySlotRule> ReadSlots(YamlNode node)
    {
        if (node is not YamlSequenceNode sequence) return [];
        return sequence.Items.Select(ReadSlot).ToList();
    }

    private static InventorySlotRule ReadSlot(YamlNode node)
    {
        if (node is YamlSequenceNode sequence && sequence.Items.Count == 2)
            return new(YamlValueReader.ReadInt32(sequence.Items[0]), YamlValueReader.ReadInt32(sequence.Items[1]));
        if (node is YamlMappingNode mapping)
            return new(YamlValueReader.ReadInt32(mapping, "x", 0), YamlValueReader.ReadInt32(mapping, "y", 0));
        throw new YamlFormatException("Inventory slot must be an x/y mapping or pair.", node.Span);
    }
}

internal sealed class ArmorRuleLoader : TypedRuleFamilyLoader<ArmorBuilder, ArmorRule>
{
    private static readonly string[] DeferredKeys =
    [
        "recovery", "psiDefence", "meleeDodge", "battleUnitScripts",
    ];

    public ArmorRuleLoader() : base(PersonnelTacticalYaml.Section("armors")) { }
    protected override ArmorBuilder Create(UnresolvedRule rule) =>
        new(rule.Id, checked((rule.CreationOrdinal + 1) * 100));

    protected override void Apply(ArmorBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        foreach (var key in builder.Strings.Keys.ToArray())
        {
            if (!reader.TryGet(key, out var node)) continue;
            builder.Strings[key] = key is "storeItem" or "selfDestructItem" or "specialWeapon" or
                "requires" or "requiresAward" or "requiresBonus"
                ? PersonnelTacticalYaml.ReadNullableName(node!)
                : YamlValueReader.ReadString(node!);
        }
        ApplyCorpse(builder, reader);
        PersonnelTacticalYaml.EditableNames(reader, "builtInWeapons", builder.BuiltInWeapons);
        PersonnelTacticalYaml.EditableNames(reader, "units", builder.Units);
        PersonnelTacticalYaml.EditableIntegers(reader, "ranks", builder.Ranks);
        ApplyLayers(builder, reader);

        foreach (var key in builder.Integers.Keys.ToArray())
            builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        foreach (var key in builder.Reals.Keys.ToArray())
            if (reader.TryGet(key, out var real)) builder.Reals[key] = YamlValueReader.ReadDouble(real!);
        foreach (var key in builder.Booleans.Keys.ToArray())
            builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        if (reader.TryGet("ai", out var ai)) ApplyAi(builder, ai!);
        ApplyMoveCosts(builder, reader);
        ApplyResources(builder, reader);
        if (reader.TryGet("stats", out var stats)) PersonnelTacticalYaml.ApplyStats(builder.Stats, stats!, nonZeroMerge: true);
        ApplyDamageModifiers(builder, reader);
        PersonnelTacticalYaml.EditableIntegers(reader, "loftempsSet", builder.Loftemps);
        if (reader.TryGet("loftemps", out var loftemp)) builder.Loftemps = [YamlValueReader.ReadInt32(loftemp!)];
        ApplySizeBooleans(builder, reader);
        ApplyColors(builder, reader);

        foreach (var key in DeferredKeys)
            reader.Defer(key, "stat-bonus and tactical script compilation belongs to Phase 4");
        reader.DeferRemaining("dynamic armor script values require Phase 4 registration");
    }

    protected override ArmorRule Freeze(ArmorBuilder builder)
    {
        var spriteColors = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
        {
            ["spriteFaceColor"] = builder.FaceColors.AsReadOnly(),
            ["spriteHairColor"] = builder.HairColors.AsReadOnly(),
            ["spriteRankColor"] = builder.RankColors.AsReadOnly(),
            ["spriteUtileColor"] = builder.UtileColors.AsReadOnly(),
        };
        return new ArmorRule(
            PersonnelReadOnly.Dictionary(builder.Strings),
            PersonnelReadOnly.Dictionary(builder.Integers),
            PersonnelReadOnly.Dictionary(builder.NullableIntegers),
            PersonnelReadOnly.Dictionary(builder.Reals),
            PersonnelReadOnly.Dictionary(builder.Booleans),
            PersonnelReadOnly.Dictionary(builder.NullableBooleans),
            builder.CorpseBattle.AsReadOnly(),
            builder.BuiltInWeapons.AsReadOnly(),
            builder.Units.AsReadOnly(),
            builder.Ranks.AsReadOnly(),
            builder.Loftemps.AsReadOnly(),
            builder.DamageModifiers.AsReadOnly(),
            PersonnelTacticalYaml.FreezeStats(builder.Stats),
            PersonnelReadOnly.Dictionary(builder.MoveCosts),
            PersonnelReadOnly.Dictionary(builder.ResourceIndexes),
            new ReadOnlyDictionary<string, IReadOnlyList<RuleIndexReference>>(builder.ResourceIndexLists.ToDictionary(
                pair => pair.Key, pair => (IReadOnlyList<RuleIndexReference>)pair.Value.AsReadOnly(), StringComparer.Ordinal)),
            new ReadOnlyDictionary<string, IReadOnlyList<int>>(spriteColors),
            new ReadOnlyDictionary<int, string>(new Dictionary<int, string>(builder.LayerSpecificPrefixes)),
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(builder.LayerDefinitions.ToDictionary(
                pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.AsReadOnly(), StringComparer.Ordinal)));
    }

    private static void ApplyCorpse(ArmorBuilder builder, RulePropertyReader reader)
    {
        if (reader.TryGet("corpseItem", out var corpseItem))
        {
            var value = YamlValueReader.ReadString(corpseItem!);
            builder.CorpseBattle = [value];
            builder.Strings["corpseGeo"] = value;
        }
        else if (reader.TryGet("corpseBattle", out var corpseBattle))
        {
            PersonnelTacticalYaml.ApplyEditableList(
                builder.CorpseBattle, corpseBattle!, YamlValueReader.ReadString, "corpseBattle");
            if (builder.CorpseBattle.Count == 0)
                throw new YamlFormatException("corpseBattle must contain at least one item.", corpseBattle!.Span);
            builder.Strings["corpseGeo"] = builder.CorpseBattle[0];
        }
    }

    private static void ApplyLayers(ArmorBuilder builder, RulePropertyReader reader)
    {
        if (reader.TryGet("layersSpecificPrefix", out var prefixes))
            builder.LayerSpecificPrefixes = YamlValueReader.ReadMap(
                prefixes!, YamlValueReader.ReadInt32, YamlValueReader.ReadString).ToDictionary();
        if (reader.TryGet("layersDefinition", out var definitions))
            builder.LayerDefinitions = YamlValueReader.ReadMap(
                definitions!, YamlValueReader.ReadString,
                node => PersonnelTacticalYaml.StringList(node, "layersDefinition"), StringComparer.Ordinal).ToDictionary();
    }

    private static void ApplyAi(ArmorBuilder builder, YamlNode node)
    {
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException("Armor ai must be a mapping.", node.Span);
        foreach (var key in builder.NullableIntegers.Keys.ToArray())
            if (mapping.TryGet(key, out var value)) builder.NullableIntegers[key] = PersonnelTacticalYaml.ReadNullableInt32(value!);
    }

    private static void ApplyMoveCosts(ArmorBuilder builder, RulePropertyReader reader)
    {
        if (!reader.TryGet("moveCost", out var node)) return;
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException("Armor moveCost must be a mapping.", node!.Span);
        foreach (var key in builder.MoveCosts.Keys.ToArray())
        {
            if (!mapping.TryGet(key, out var pair)) continue;
            if (pair is not YamlSequenceNode sequence || sequence.Items.Count < 2)
                throw new YamlFormatException($"Armor moveCost {key} must be a pair.", pair!.Span);
            builder.MoveCosts[key] = new(
                YamlValueReader.ReadInt32(sequence.Items[0]), YamlValueReader.ReadInt32(sequence.Items[1]));
        }
    }

    private static void ApplyResources(ArmorBuilder builder, RulePropertyReader reader)
    {
        if (reader.TryGet("moveSound", out var moveSound))
            builder.ResourceIndexes["moveSound"] = PresentationYaml.ReadIndexReference(moveSound!, reader.Source.ModId);
        foreach (var key in ArmorBuilder.SoundListKeys)
            if (reader.TryGet(key, out var sound))
                builder.ResourceIndexLists[key] = PersonnelTacticalYaml.IndexList(sound!, reader.Source.ModId);
        if (reader.TryGet("customArmorPreviewIndex", out var preview))
            builder.ResourceIndexLists["customArmorPreviewIndex"] =
                PersonnelTacticalYaml.IndexList(preview!, reader.Source.ModId);
    }

    private static void ApplyDamageModifiers(ArmorBuilder builder, RulePropertyReader reader)
    {
        if (!reader.TryGet("damageModifier", out var node)) return;
        if (node is not YamlSequenceNode sequence) return;
        for (var index = 0; index < Math.Min(builder.DamageModifiers.Count, sequence.Items.Count); index++)
            builder.DamageModifiers[index] = YamlValueReader.ReadDouble(sequence.Items[index]);
    }

    private static void ApplySizeBooleans(ArmorBuilder builder, RulePropertyReader reader)
    {
        if (reader.TryGet("size", out var sizeNode) && YamlValueReader.ReadInt32(sizeNode!) != 1)
        {
            builder.NullableBooleans["fearImmune"] = true;
            builder.NullableBooleans["bleedImmune"] = true;
            builder.NullableBooleans["painImmune"] = true;
            builder.NullableBooleans["zombiImmune"] = true;
            builder.NullableBooleans["ignoresMeleeThreat"] = true;
            builder.NullableBooleans["createsMeleeThreat"] = false;
        }
        foreach (var key in builder.NullableBooleans.Keys.ToArray())
        {
            if (key == "zombiImmune" && builder.Integers["size"] != 1) continue;
            if (reader.TryGet(key, out var value))
                builder.NullableBooleans[key] = PersonnelTacticalYaml.ReadNullableBoolean(value!);
        }
    }

    private static void ApplyColors(ArmorBuilder builder, RulePropertyReader reader)
    {
        PersonnelTacticalYaml.EditableIntegers(reader, "spriteFaceColor", builder.FaceColors);
        PersonnelTacticalYaml.EditableIntegers(reader, "spriteHairColor", builder.HairColors);
        PersonnelTacticalYaml.EditableIntegers(reader, "spriteRankColor", builder.RankColors);
        PersonnelTacticalYaml.EditableIntegers(reader, "spriteUtileColor", builder.UtileColors);
    }
}
