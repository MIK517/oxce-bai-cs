using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.EquipmentProduction;

internal sealed class ItemCategoryRuleLoader : TypedRuleFamilyLoader<ItemCategoryBuilder, ItemCategoryRule>
{
    public ItemCategoryRuleLoader() : base(EquipmentYaml.Section("itemCategories")) { }
    protected override ItemCategoryBuilder Create(UnresolvedRule rule) =>
        new(rule.Id, checked((rule.CreationOrdinal + 1) * 100));
    protected override void Apply(ItemCategoryBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.ReplaceBy = reader.ReadString("replaceBy", builder.ReplaceBy);
        builder.Hidden = reader.ReadBoolean("hidden", builder.Hidden);
        builder.ListOrder = reader.ReadInt32("listOrder", builder.ListOrder);
        EquipmentYaml.EditableNames(reader, "invOrder", builder.InventoryOrder);
    }
    protected override ItemCategoryRule Freeze(ItemCategoryBuilder builder) =>
        new(builder.ReplaceBy, builder.Hidden, builder.ListOrder, builder.InventoryOrder.AsReadOnly());
}

internal sealed class WeaponSetRuleLoader : IdOnlyTypedRuleFamilyLoader<WeaponSetBuilder, WeaponSetRule>
{
    public WeaponSetRuleLoader() : base(EquipmentYaml.Section("weaponSets")) { }
    protected override WeaponSetBuilder Create(string id) => new(id);
    protected override void Apply(WeaponSetBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        EquipmentYaml.EditableNames(reader, "weapons", builder.Weapons);
    }
    protected override WeaponSetRule Freeze(WeaponSetBuilder builder) => new(builder.Weapons.AsReadOnly());
}

internal sealed class CraftWeaponRuleLoader : IdOnlyTypedRuleFamilyLoader<CraftWeaponBuilder, CraftWeaponRule>
{
    public CraftWeaponRuleLoader() : base(EquipmentYaml.Section("craftWeapons")) { }
    protected override CraftWeaponBuilder Create(string id) => new(id);
    protected override void Apply(CraftWeaponBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.UfopediaType = reader.ReadString("ufopediaType", builder.UfopediaType);
        builder.Tooltip = reader.ReadString("tooltip", builder.Tooltip);
        if (reader.TryGet("stats", out var stats)) EquipmentYaml.ApplyStats(builder.Stats, stats!);
        if (reader.TryGet("sprite", out var sprite))
            builder.Sprite = PresentationYaml.ReadIndexReference(sprite!, reader.Source.ModId);
        if (reader.TryGet("sound", out var sound))
            builder.Sound = PresentationYaml.ReadIndexReference(sound!, reader.Source.ModId);
        foreach (var key in builder.Integers.Keys.ToArray())
            builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        foreach (var key in builder.Booleans.Keys.ToArray())
            builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        builder.Launcher = reader.ReadString("launcher", builder.Launcher);
        builder.Clip = reader.ReadString("clip", builder.Clip);
    }
    protected override CraftWeaponRule Freeze(CraftWeaponBuilder builder) => new(
        builder.UfopediaType,
        builder.Tooltip,
        builder.Sprite,
        builder.Sound,
        EquipmentReadOnly.Dictionary(builder.Integers),
        EquipmentReadOnly.Dictionary(builder.Booleans),
        builder.Launcher,
        builder.Clip,
        EquipmentYaml.FreezeStats(builder.Stats));
}

internal sealed class CraftRuleLoader : TypedRuleFamilyLoader<CraftBuilder, CraftRule>
{
    public CraftRuleLoader() : base(EquipmentYaml.Section("crafts")) { }
    protected override CraftBuilder Create(UnresolvedRule rule) =>
        new(rule.Id, checked((rule.CreationOrdinal + 1) * 100));
    protected override void Apply(CraftBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        EquipmentYaml.EditableNames(reader, "requires", builder.Requirements);
        EquipmentYaml.EditableNames(reader, "requiresBuyBaseFunc", builder.RequiredBuyBaseFunctions, unique: true);
        foreach (var key in builder.Strings.Keys.ToArray())
            builder.Strings[key] = reader.ReadString(key, builder.Strings[key]);
        if (reader.TryGet("sprite", out var sprite))
            builder.Sprite = PresentationYaml.ReadIndexReference(sprite!, reader.Source.ModId);
        if (reader.TryGet("skinSprites", out var skins))
            builder.SkinSprites = skins is YamlSequenceNode
                ? EquipmentYaml.IndexList(skins, reader.Source.ModId)
                : [];
        EquipmentYaml.ApplyStats(builder.Stats, reader);
        if (reader.TryGet("marker", out var marker))
            builder.Marker = PresentationYaml.ReadIndexReference(marker!, reader.Source.ModId);
        foreach (var key in builder.Integers.Keys.ToArray())
            builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        foreach (var key in builder.Booleans.Keys.ToArray())
            builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        if (reader.TryGet("craftInventoryTile", out var tile))
            builder.CraftInventoryTile = YamlValueReader.ReadSequence(tile!, YamlValueReader.ReadInt32).ToList();
        EquipmentYaml.EditableIntegers(reader, "groups", builder.Groups);
        EquipmentYaml.EditableIntegers(reader, "allowedSoldierGroups", builder.AllowedSoldierGroups);
        EquipmentYaml.EditableIntegers(reader, "allowedArmorGroups", builder.AllowedArmorGroups);
        if (reader.TryGet("limitArmorGroups", out var limits))
            builder.ArmorGroupLimits = YamlValueReader.ReadMap(
                limits!, YamlValueReader.ReadInt32, YamlValueReader.ReadInt32).ToDictionary();
        ApplyWeaponTypes(builder, reader);
        ApplyFixedStrings(builder.WeaponStrings, reader, "weaponStrings");
        ApplyFixedStrings(builder.FixedWeapons, reader, "fixedWeapons");
        if (reader.TryGet("selectSound", out var selectSound))
            builder.SelectSounds = EquipmentYaml.IndexList(selectSound!, reader.Source.ModId);
        if (reader.TryGet("takeoffSound", out var takeoffSound))
            builder.TakeoffSounds = EquipmentYaml.IndexList(takeoffSound!, reader.Source.ModId);
        if (reader.TryGet("pilotSoldierBonusesRequired", out var pilotBonuses))
            builder.RequiredPilotBonuses = EquipmentYaml.StringList(pilotBonuses!);
        reader.Defer("battlescapeTerrainData", "inline craft terrain loading belongs to the terrain slice");
        reader.Defer("deployment", "craft deployment loading belongs to the deployment slice");
        reader.Defer("pilotMinStatsRequired", "pilot stat linking belongs to the personnel slice");
        reader.DeferRemaining("dynamic craft script values require Phase 4 registration");
    }
    protected override CraftRule Freeze(CraftBuilder builder) => new(
        EquipmentReadOnly.Dictionary(builder.Integers),
        EquipmentReadOnly.Dictionary(builder.Booleans),
        EquipmentReadOnly.Dictionary(builder.Strings),
        builder.Requirements.AsReadOnly(),
        builder.RequiredBuyBaseFunctions.AsReadOnly(),
        builder.Sprite,
        builder.SkinSprites.AsReadOnly(),
        builder.Marker,
        builder.SelectSounds.AsReadOnly(),
        builder.TakeoffSounds.AsReadOnly(),
        EquipmentYaml.FreezeStats(builder.Stats),
        builder.CraftInventoryTile.AsReadOnly(),
        builder.Groups.AsReadOnly(),
        builder.AllowedSoldierGroups.AsReadOnly(),
        builder.AllowedArmorGroups.AsReadOnly(),
        new ReadOnlyDictionary<int, int>(builder.ArmorGroupLimits),
        Array.AsReadOnly(builder.WeaponTypes.Select(row => (IReadOnlyList<int>)row.AsReadOnly()).ToArray()),
        Array.AsReadOnly((string[])builder.WeaponStrings.Clone()),
        Array.AsReadOnly((string[])builder.FixedWeapons.Clone()),
        builder.RequiredPilotBonuses.AsReadOnly());

    private static void ApplyWeaponTypes(CraftBuilder builder, RulePropertyReader reader)
    {
        if (!reader.TryGet("weaponTypes", out var node)) return;
        if (node is not YamlSequenceNode slots)
            throw new YamlFormatException("Craft weaponTypes must be a sequence.", node!.Span);
        for (var slot = 0; slot < Math.Min(4, slots.Items.Count); slot++)
        {
            var value = slots.Items[slot];
            if (value is YamlScalarNode)
            {
                var type = YamlValueReader.ReadInt32(value);
                for (var index = 0; index < 8; index++) builder.WeaponTypes[slot][index] = type;
            }
            else if (value is YamlSequenceNode types)
            {
                var count = Math.Min(8, types.Items.Count);
                for (var index = 0; index < count; index++)
                    builder.WeaponTypes[slot][index] = YamlValueReader.ReadInt32(types.Items[index]);
                for (var index = count; index < 8; index++)
                    builder.WeaponTypes[slot][index] = builder.WeaponTypes[slot][0];
            }
            else throw new YamlFormatException($"Invalid weapon type for craft '{builder.Id}'.", value.Span);
        }
    }

    private static void ApplyFixedStrings(string[] target, RulePropertyReader reader, string key)
    {
        if (!reader.TryGet(key, out var node)) return;
        if (node is not YamlSequenceNode sequence)
            throw new YamlFormatException($"Craft {key} must be a sequence.", node!.Span);
        for (var index = 0; index < Math.Min(target.Length, sequence.Items.Count); index++)
            target[index] = YamlValueReader.ReadString(sequence.Items[index]);
    }
}

internal sealed class UfoRuleLoader : IdOnlyTypedRuleFamilyLoader<UfoBuilder, UfoRule>
{
    public UfoRuleLoader() : base(EquipmentYaml.Section("ufos")) { }
    protected override UfoBuilder Create(string id) => new(id);
    protected override void Apply(UfoBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.Size = reader.ReadString("size", builder.Size);
        if (builder.Size == "STR_MEDIUM") builder.Size = "STR_MEDIUM_UC";
        foreach (var key in builder.Integers.Keys.ToArray())
            builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        builder.Integers["blobSize"] = Math.Min(builder.Integers["blobSize"], 7);
        foreach (var key in builder.Booleans.Keys.ToArray())
            builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        if (reader.TryGet("marker", out var marker))
            builder.Marker = PresentationYaml.ReadIndexReference(marker!, reader.Source.ModId);
        if (reader.TryGet("markerLand", out var markerLand))
            builder.LandedMarker = PresentationYaml.ReadIndexReference(markerLand!, reader.Source.ModId);
        if (reader.TryGet("markerCrash", out var markerCrash))
            builder.CrashedMarker = PresentationYaml.ReadIndexReference(markerCrash!, reader.Source.ModId);
        EquipmentYaml.ApplyStats(builder.Stats, reader);
        builder.ModSprite = reader.ReadString("modSprite", builder.ModSprite);
        builder.HitImage = reader.ReadString("hitImage", builder.HitImage);
        if (reader.TryGet("raceBonus", out var raceBonus)) ApplyRaceBonuses(builder, raceBonus!);
        foreach (var key in builder.Sounds.Keys.ToArray())
        {
            if (reader.TryGet(key, out var sound))
                builder.Sounds[key] = PresentationYaml.ReadIndexReference(sound!, reader.Source.ModId);
        }
        reader.Defer("battlescapeTerrainData", "inline UFO terrain loading belongs to the terrain slice");
        reader.DeferRemaining("dynamic UFO script values require Phase 4 registration");
    }
    protected override UfoRule Freeze(UfoBuilder builder) => new(
        EquipmentReadOnly.Dictionary(builder.Integers),
        EquipmentReadOnly.Dictionary(builder.Booleans),
        builder.Size,
        builder.ModSprite,
        builder.HitImage,
        builder.Marker,
        builder.LandedMarker,
        builder.CrashedMarker,
        EquipmentReadOnly.Dictionary(builder.Sounds),
        EquipmentYaml.FreezeUfoStats(builder.Stats),
        new ReadOnlyDictionary<string, UfoStats>(builder.RaceBonuses.ToDictionary(
            pair => pair.Key, pair => EquipmentYaml.FreezeUfoStats(pair.Value), StringComparer.Ordinal)));

    private static void ApplyRaceBonuses(UfoBuilder builder, YamlNode node)
    {
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException("UFO raceBonus must be a mapping.", node.Span);
        foreach (var entry in mapping.Entries)
        {
            var id = entry.ScalarKey ?? throw new YamlFormatException("UFO race bonus keys must be scalars.", entry.Key.Span);
            if (!builder.RaceBonuses.TryGetValue(id, out var stats))
            {
                stats = new UfoStatsBuilder();
                builder.RaceBonuses[id] = stats;
            }
            EquipmentYaml.ApplyStats(stats, entry.Value);
        }
    }
}

internal static class EquipmentYaml
{
    public static RuleSectionDefinition Section(string name) =>
        RuleSectionRegistry.TryGetNamed(name, out var section) ? section! : throw new InvalidOperationException();

    public static void EditableNames(RulePropertyReader reader, string key, List<string> target, bool unique = false)
    {
        if (reader.TryGet(key, out var node)) CampaignStartYaml.ApplyEditableNames(target, node!, unique);
    }

    public static void EditableIntegers(RulePropertyReader reader, string key, List<int> target)
    {
        if (!reader.TryGet(key, out var node)) return;
        if (node is not YamlSequenceNode)
            throw new YamlFormatException("Editable integer collections must be sequences.", node!.Span);
        var values = YamlValueReader.ReadSequence(node!, YamlValueReader.ReadInt32);
        switch (node!.Tag)
        {
            case null:
            case "!!seq":
            case "!info": target.Clear(); target.AddRange(values); break;
            case "!add": target.AddRange(values); break;
            case "!remove": foreach (var value in values) target.RemoveAll(item => item == value); break;
            default: throw new YamlFormatException($"Unsupported collection tag '{node.Tag}'.", node.Span);
        }
    }

    public static List<string> StringList(YamlNode node)
    {
        if (node is not YamlSequenceNode)
            throw new YamlFormatException("Expected a string sequence.", node.Span);
        return YamlValueReader.ReadSequence(node, YamlValueReader.ReadString).ToList();
    }

    public static List<RuleIndexReference> IndexList(YamlNode node, string modId)
    {
        var values = node is YamlSequenceNode sequence
            ? sequence.Items.Select(value => PresentationYaml.ReadIndexReference(value, modId))
            : [PresentationYaml.ReadIndexReference(node, modId)];
        return values.Where(value => value.Index != -1).ToList();
    }

    public static void ApplyStats(CraftStatsBuilder stats, RulePropertyReader reader)
    {
        foreach (var key in CraftStatsBuilder.IntegerKeys)
            stats.Integers[key] = reader.ReadInt32(key, stats.Integers[key]);
        if (reader.TryGet("maxStorageSpace", out var storage))
            stats.MaximumStorageSpace = YamlValueReader.ReadDouble(storage!);
        if (stats is UfoStatsBuilder ufo)
        {
            ufo.CraftCustomDeployment = reader.ReadString("craftCustomDeploy", ufo.CraftCustomDeployment);
            ufo.MissionCustomDeployment = reader.ReadString("missionCustomDeploy", ufo.MissionCustomDeployment);
        }
    }

    public static void ApplyStats(CraftStatsBuilder stats, YamlNode node)
    {
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException("Craft stats must be a mapping.", node.Span);
        foreach (var key in CraftStatsBuilder.IntegerKeys)
            if (mapping.TryGet(key, out var value)) stats.Integers[key] = YamlValueReader.ReadInt32(value!);
        if (mapping.TryGet("maxStorageSpace", out var storage))
            stats.MaximumStorageSpace = YamlValueReader.ReadDouble(storage!);
        if (stats is UfoStatsBuilder ufo)
        {
            ufo.CraftCustomDeployment = YamlValueReader.ReadString(
                mapping, "craftCustomDeploy", ufo.CraftCustomDeployment);
            ufo.MissionCustomDeployment = YamlValueReader.ReadString(
                mapping, "missionCustomDeploy", ufo.MissionCustomDeployment);
        }
    }

    public static CraftStats FreezeStats(CraftStatsBuilder stats) =>
        new(EquipmentReadOnly.Dictionary(stats.Integers), stats.MaximumStorageSpace);

    public static UfoStats FreezeUfoStats(UfoStatsBuilder stats) =>
        new(FreezeStats(stats), stats.CraftCustomDeployment, stats.MissionCustomDeployment);
}
