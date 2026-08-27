using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.Items;

internal sealed class ItemRuleLoader : TypedRuleFamilyLoader<ItemRuleBuilder, ItemRule>
{
    private static readonly string[] ScriptKeys =
    [
        "damageBonus", "meleeBonus", "accuracyMultiplier", "meleeMultiplier", "throwMultiplier",
        "closeQuartersMultiplier", "recolorItemSprite", "selectItemSprite", "vaporParticleAmmo",
        "vaporParticleWeapon", "reactionWeaponAction", "tryPsiAttackItem", "tryMeleeAttackItem",
        "hitUnitAmmo", "damageUnitAmmo", "damageSpecialUnitAmmo", "createItem", "newTurnItem",
        "sellCostItem", "buyCostItem", "statsForNerdsItem",
    ];

    public ItemRuleLoader() : base(GetSection()) { }
    protected override ItemRuleBuilder Create(string id) => throw new NotSupportedException();
    protected override ItemRuleBuilder Create(UnresolvedRule rule) =>
        new(rule.Id, checked((rule.CreationOrdinal + 1) * 100));

    protected override void Apply(ItemRuleBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        ApplyStrings(builder, reader);
        ApplyEditableNames(reader, "requires", builder.Requirements);
        ApplyEditableNames(reader, "requiresBuy", builder.BuyRequirements);
        ApplyEditableNames(reader, "requiresBuyBaseFunc", builder.RequiredBuyBaseFunctions, unique: true);
        ApplyEditableNames(reader, "categories", builder.Categories);
        ApplyEditableMap(reader, "recoveryDividers", builder.RecoveryDividers, YamlValueReader.ReadInt32);
        ApplyEditableMap(reader, "recoveryTransformations", builder.RecoveryTransformations,
            node => YamlValueReader.ReadSequence(node, YamlValueReader.ReadInt32).ToList());

        ApplyResources(builder, reader);
        ApplyBattleType(builder, reader);
        ApplyDamage(builder.Damage, reader, "damageType", "blastRadius", "damageAlter");
        ApplyDamage(builder.MeleeDamage, reader, "meleeType", null, "meleeAlter");
        reader.Defer("skillApplied", "stat-bonus compilation belongs to Phase 4");
        reader.Defer("strengthApplied", "stat-bonus compilation belongs to Phase 4");

        if (reader.TryGet("flatRate", out var flatRate))
            GetUseFlat(builder, "Use").Time = YamlValueReader.ReadBoolean(flatRate!);
        ApplyActions(builder, reader);
        ApplyFuse(builder.FuseTriggers, reader);
        ApplyAmmo(builder, reader);
        ApplyNestedRules(builder, reader);
        ApplyScalarValues(builder, reader);
        ApplyLegacyAliases(builder, reader);

        foreach (var key in ScriptKeys)
            reader.Defer(key, "item script compilation belongs to Phase 4");
        reader.DeferRemaining("dynamic item script values require Phase 4 registration");
    }

    protected override ItemRule Freeze(ItemRuleBuilder builder)
    {
        var actions = builder.Actions.ToDictionary(
            pair => pair.Key,
            pair => new ItemActionRule(
                pair.Value.Accuracy, pair.Value.Range, pair.Value.Shots, pair.Value.SpendPerShot,
                pair.Value.FollowProjectiles, pair.Value.AmmoSlot,
                pair.Value.AmmoZombieUnitChanceOverride, pair.Value.AmmoSpawnUnitChanceOverride,
                pair.Value.AmmoSpawnItemChanceOverride, pair.Value.Cost.Freeze(), pair.Value.Flat.Freeze(),
                pair.Value.Arcing, pair.Value.Name, pair.Value.ShortName),
            StringComparer.Ordinal);
        return new ItemRule(
            new ItemScalarValues(builder),
            builder.Requirements.AsReadOnly(),
            builder.BuyRequirements.AsReadOnly(),
            builder.RequiredBuyBaseFunctions.AsReadOnly(),
            builder.Categories.AsReadOnly(),
            ReadOnly(builder.RecoveryDividers),
            ReadOnlyLists(builder.RecoveryTransformations),
            Array.AsReadOnly(builder.CompatibleAmmo.Select(value =>
                (IReadOnlyList<string>)value.AsReadOnly()).ToArray()),
            Array.AsReadOnly((int[])builder.TimeUnitsToLoad.Clone()),
            Array.AsReadOnly((int[])builder.TimeUnitsToUnload.Clone()),
            builder.SupportedInventorySections.AsReadOnly(),
            ReadOnly(builder.ZombieUnitsByMaleArmor),
            ReadOnly(builder.ZombieUnitsByFemaleArmor),
            ReadOnly(builder.ZombieUnitsByType),
            ReadOnly(builder.ResourceIndexes),
            ReadOnlyLists(builder.ResourceIndexLists),
            new ReadOnlyDictionary<string, ItemActionRule>(actions),
            FreezeUseValues(builder.UseCosts),
            FreezeUseValues(builder.UseFlats),
            new ItemFuseTriggerRule(
                builder.FuseTriggers.DefaultBehavior, builder.FuseTriggers.ThrowTrigger,
                builder.FuseTriggers.ThrowExplode, builder.FuseTriggers.ProximityTrigger,
                builder.FuseTriggers.ProximityExplode),
            FreezeDamage(builder.Damage),
            FreezeDamage(builder.MeleeDamage));
    }

    private static void ApplyStrings(ItemRuleBuilder builder, RulePropertyReader reader)
    {
        foreach (var key in ItemRuleSchema.StringDefaults.Keys)
            builder.Strings[key] = reader.ReadString(key, builder.Strings[key]);
        foreach (var key in builder.NullableNames.Keys.ToArray())
        {
            if (!reader.TryGet(key, out var node)) continue;
            builder.NullableNames[key] = node is YamlNullNode ? null : YamlValueReader.ReadString(node!);
        }
    }

    private static void ApplyBattleType(ItemRuleBuilder builder, RulePropertyReader reader)
    {
        if (!reader.TryGet("battleType", out var node)) return;
        var battleType = YamlValueReader.ReadInt32(node!);
        builder.Integers["battleType"] = battleType;
        builder.Booleans["ignoreInCraftEquip"] = battleType is 0 or 11;
        if (battleType == 9)
        {
            builder.Booleans["psiRequired"] = true;
            builder.Integers["dropoff"] = 1;
            builder.Actions["Aimed"].Range = 0;
            builder.Integers["targetMatrix"] = 6;
        }
        else
        {
            builder.Booleans["psiRequired"] = false;
        }
        builder.Integers["fuseType"] = battleType switch { 5 => -2, 4 => -1, _ => -3 };
        builder.Actions["Melee"].AmmoSlot = battleType == 3 ? 0 : -1;
        if (battleType == 11) builder.Damage.ResetForPredefinedType(3);
        builder.MeleeDamage.ResetForPredefinedType(7);
    }

    private static void ApplyDamage(
        ItemDamageBuilder damage,
        RulePropertyReader reader,
        string typeKey,
        string? legacyRadiusKey,
        string alterKey)
    {
        if (reader.TryGet(typeKey, out var type)) damage.ResetForPredefinedType(YamlValueReader.ReadInt32(type!));
        if (legacyRadiusKey is not null && reader.TryGet(legacyRadiusKey, out var radius))
            damage.Integers["FixRadius"] = YamlValueReader.ReadInt32(radius!);
        if (!reader.TryGet(alterKey, out var alter)) return;
        if (alter is not YamlMappingNode mapping)
            throw new YamlFormatException($"Item {alterKey} must be a mapping.", alter!.Span);
        foreach (var entry in mapping.Entries)
        {
            var key = entry.ScalarKey ?? throw new YamlFormatException(
                $"Item {alterKey} keys must be scalars.", entry.Key.Span);
            if (DamageIntegerKeys.Contains(key)) damage.Integers[key] = YamlValueReader.ReadInt32(entry.Value);
            else if (DamageRealKeys.Contains(key)) damage.Reals[key] = YamlValueReader.ReadDouble(entry.Value);
            else if (DamageBooleanKeys.Contains(key)) damage.Booleans[key] = YamlValueReader.ReadBoolean(entry.Value);
            else throw new YamlFormatException($"Unknown item damage property '{key}'.", entry.Key.Span);
        }
    }

    private static void ApplyActions(ItemRuleBuilder builder, RulePropertyReader reader)
    {
        ApplyActionScalar(reader, "accuracyAimed", builder.Actions["Aimed"], (a, v) => a.Accuracy = v);
        ApplyActionScalar(reader, "accuracyAuto", builder.Actions["Auto"], (a, v) => a.Accuracy = v);
        ApplyActionScalar(reader, "accuracySnap", builder.Actions["Snap"], (a, v) => a.Accuracy = v);
        ApplyActionScalar(reader, "accuracyMelee", builder.Actions["Melee"], (a, v) => a.Accuracy = v);
        ApplyActionScalar(reader, "aimRange", builder.Actions["Aimed"], (a, v) => a.Range = v);
        ApplyActionScalar(reader, "autoRange", builder.Actions["Auto"], (a, v) => a.Range = v);
        ApplyActionScalar(reader, "snapRange", builder.Actions["Snap"], (a, v) => a.Range = v);
        ApplyActionScalar(reader, "autoShots", builder.Actions["Auto"], (a, v) => a.Shots = v);
        foreach (var pair in builder.Actions)
        {
            ApplyCost(reader, pair.Key, pair.Value.Cost);
            ApplyFlat(reader, pair.Key, pair.Value.Flat);
            if (!reader.TryGet("conf" + pair.Key, out var node)) continue;
            if (node is not YamlMappingNode mapping)
                throw new YamlFormatException($"Item conf{pair.Key} must be a mapping.", node!.Span);
            pair.Value.Shots = Read(mapping, "shots", pair.Value.Shots);
            pair.Value.SpendPerShot = Read(mapping, "spendPerShot", pair.Value.SpendPerShot);
            pair.Value.FollowProjectiles = Read(mapping, "followProjectiles", pair.Value.FollowProjectiles);
            pair.Value.Name = Read(mapping, "name", pair.Value.Name);
            pair.Value.ShortName = Read(mapping, "shortName", pair.Value.ShortName);
            if (mapping.TryGet("ammoSlot", out var ammoSlot))
            {
                var value = YamlValueReader.ReadInt32(ammoSlot!);
                if (value is >= -1 and < 4) pair.Value.AmmoSlot = value;
            }
            pair.Value.AmmoZombieUnitChanceOverride = ReadNullableInt(
                mapping, "ammoZombieUnitChanceOverride", pair.Value.AmmoZombieUnitChanceOverride);
            pair.Value.AmmoSpawnUnitChanceOverride = ReadNullableInt(
                mapping, "ammoSpawnUnitChanceOverride", pair.Value.AmmoSpawnUnitChanceOverride);
            pair.Value.AmmoSpawnItemChanceOverride = ReadNullableInt(
                mapping, "ammoSpawnItemChanceOverride", pair.Value.AmmoSpawnItemChanceOverride);
            pair.Value.Arcing = Read(mapping, "arcing", pair.Value.Arcing);
        }
        ApplyCost(reader, "Use", GetUseCost(builder, "Use"));
        ApplyCost(reader, "MindControl", GetUseCost(builder, "MindControl"));
        ApplyCost(reader, "Panic", GetUseCost(builder, "Panic"));
        ApplyCost(reader, "Throw", GetUseCost(builder, "Throw"));
        ApplyCost(reader, "Prime", GetUseCost(builder, "Prime"));
        ApplyCost(reader, "Unprime", GetUseCost(builder, "Unprime"));
        ApplyFlat(reader, "Use", GetUseFlat(builder, "Use"));
        ApplyFlat(reader, "Throw", GetUseFlat(builder, "Throw"));
        ApplyFlat(reader, "Prime", GetUseFlat(builder, "Prime"));
        ApplyFlat(reader, "Unprime", GetUseFlat(builder, "Unprime"));
    }

    private static void ApplyFuse(ItemFuseTriggerBuilder fuse, RulePropertyReader reader)
    {
        if (!reader.TryGet("fuseTriggerEvents", out var node)) return;
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException("Item fuseTriggerEvents must be a mapping.", node!.Span);
        fuse.DefaultBehavior = Read(mapping, "defaultBehavior", fuse.DefaultBehavior);
        fuse.ThrowTrigger = Read(mapping, "throwTrigger", fuse.ThrowTrigger);
        fuse.ThrowExplode = Read(mapping, "throwExplode", fuse.ThrowExplode);
        fuse.ProximityTrigger = Read(mapping, "proximityTrigger", fuse.ProximityTrigger);
        fuse.ProximityExplode = Read(mapping, "proximityExplode", fuse.ProximityExplode);
    }

    private static void ApplyAmmo(ItemRuleBuilder builder, RulePropertyReader reader)
    {
        ApplyAmmoSlot(builder, reader, 0);
        if (!reader.TryGet("ammo", out var ammo)) return;
        if (ammo is not YamlMappingNode slots)
            throw new YamlFormatException("Item ammo must be a mapping.", ammo!.Span);
        for (var slot = 0; slot < 4; slot++)
        {
            if (!slots.TryGet(slot.ToString(System.Globalization.CultureInfo.InvariantCulture), out var node)) continue;
            if (node is not YamlMappingNode mapping)
                throw new YamlFormatException("Item ammo slot must be a mapping.", node!.Span);
            if (mapping.TryGet("compatibleAmmo", out var names))
                CampaignStartYaml.ApplyEditableNames(builder.CompatibleAmmo[slot], names!, unique: false);
            builder.TimeUnitsToLoad[slot] = Read(mapping, "tuLoad", builder.TimeUnitsToLoad[slot]);
            builder.TimeUnitsToUnload[slot] = Read(mapping, "tuUnload", builder.TimeUnitsToUnload[slot]);
        }
    }

    private static void ApplyAmmoSlot(ItemRuleBuilder builder, RulePropertyReader reader, int slot)
    {
        if (reader.TryGet("compatibleAmmo", out var names))
            CampaignStartYaml.ApplyEditableNames(builder.CompatibleAmmo[slot], names!, unique: false);
        builder.TimeUnitsToLoad[slot] = reader.ReadInt32("tuLoad", builder.TimeUnitsToLoad[slot]);
        builder.TimeUnitsToUnload[slot] = reader.ReadInt32("tuUnload", builder.TimeUnitsToUnload[slot]);
    }

    private static void ApplyNestedRules(ItemRuleBuilder builder, RulePropertyReader reader)
    {
        if (reader.TryGet("inventoryMoveCost", out var moveCost))
        {
            if (moveCost is not YamlMappingNode mapping)
                throw new YamlFormatException("Item inventoryMoveCost must be a mapping.", moveCost!.Span);
            builder.Integers["inventoryMoveCostPercent"] = Read(
                mapping, "basePercent", builder.Integers["inventoryMoveCostPercent"]);
        }
        if (reader.TryGet("ai", out var ai))
        {
            if (ai is not YamlMappingNode mapping)
                throw new YamlFormatException("Item ai must be a mapping.", ai!.Span);
            builder.Integers["aiUseDelay"] = Read(mapping, "useDelay", builder.Integers["aiUseDelay"]);
            builder.Integers["aiMeleeHitCount"] = Read(
                mapping, "meleeHitCount", builder.Integers["aiMeleeHitCount"]);
        }
        ApplyEditableNames(reader, "supportedInventorySections", builder.SupportedInventorySections);
        ApplyEditableMap(reader, "zombieUnitByArmorMale", builder.ZombieUnitsByMaleArmor, YamlValueReader.ReadString);
        ApplyEditableMap(reader, "zombieUnitByArmorFemale", builder.ZombieUnitsByFemaleArmor, YamlValueReader.ReadString);
        ApplyEditableMap(reader, "zombieUnitByType", builder.ZombieUnitsByType, YamlValueReader.ReadString);
    }

    private static void ApplyScalarValues(ItemRuleBuilder builder, RulePropertyReader reader)
    {
        foreach (var key in ItemRuleSchema.IntegerDefaults.Keys)
        {
            if (key is "inventoryMoveCostPercent" or "aiUseDelay" or "aiMeleeHitCount" or
                "spawnUnitChance" or "zombieUnitChance" or "spawnItemChance") continue;
            builder.Integers[key] = reader.ReadInt32(key, builder.Integers[key]);
        }
        foreach (var key in ItemRuleSchema.RealDefaults.Keys)
            if (reader.TryGet(key, out var node)) builder.Reals[key] = YamlValueReader.ReadDouble(node!);
        foreach (var key in ItemRuleSchema.BooleanDefaults.Keys)
            builder.Booleans[key] = reader.ReadBoolean(key, builder.Booleans[key]);
        foreach (var key in new[] { "spawnUnitChance", "zombieUnitChance", "spawnItemChance" })
        {
            if (!reader.TryGet(key, out var node)) continue;
            builder.Integers[key] = node is YamlNullNode ? -1 : YamlValueReader.ReadInt32(node!);
        }
    }

    private static void ApplyLegacyAliases(ItemRuleBuilder builder, RulePropertyReader reader)
    {
        if (reader.TryGet("isExplodingInHands", out var exploding))
            builder.Integers["explodeInventory"] = YamlValueReader.ReadBoolean(exploding!) ? 2 : 0;
        builder.Integers["explodeInventory"] = reader.ReadInt32(
            "explodeInventory", builder.Integers["explodeInventory"]);
        if (reader.TryGet("psiTargetMatrix", out var matrix))
            builder.Integers["targetMatrix"] = YamlValueReader.ReadInt32(matrix!);
        builder.Integers["targetMatrix"] = reader.ReadInt32("targetMatrix", builder.Integers["targetMatrix"]);
    }

    private static void ApplyResources(ItemRuleBuilder builder, RulePropertyReader reader)
    {
        foreach (var key in builder.ResourceIndexes.Keys.ToArray())
        {
            if (!reader.TryGet(key, out var node)) continue;
            builder.ResourceIndexes[key] = PresentationYaml.ReadIndexReference(node!, reader.Source.ModId);
        }
        foreach (var key in builder.ResourceIndexLists.Keys.ToArray())
        {
            if (!reader.TryGet(key, out var node)) continue;
            var values = node is YamlSequenceNode sequence
                ? sequence.Items.Select(value => PresentationYaml.ReadIndexReference(value, reader.Source.ModId))
                : [PresentationYaml.ReadIndexReference(node!, reader.Source.ModId)];
            builder.ResourceIndexLists[key] = values.Where(value => value.Index != -1).ToList();
        }
    }

    private static void ApplyCost(RulePropertyReader reader, string suffix, ItemUseValuesBuilder<int?> cost)
    {
        if (reader.TryGet("tu" + suffix, out var time)) cost.Time = ReadNullableInt(time!);
        if (!reader.TryGet("cost" + suffix, out var node)) return;
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException($"Item cost{suffix} must be a mapping.", node!.Span);
        ApplyUseValues(mapping, cost, ReadNullableInt);
    }

    private static void ApplyFlat(RulePropertyReader reader, string suffix, ItemUseValuesBuilder<bool?> flat)
    {
        if (!reader.TryGet("flat" + suffix, out var node)) return;
        if (node is YamlMappingNode mapping) ApplyUseValues(mapping, flat, ReadNullableBool);
        else flat.Time = ReadNullableBool(node!);
    }

    private static void ApplyUseValues<T>(YamlMappingNode mapping, ItemUseValuesBuilder<T> values, Func<YamlNode, T> read)
    {
        if (mapping.TryGet("time", out var node)) values.Time = read(node!);
        if (mapping.TryGet("energy", out node)) values.Energy = read(node!);
        if (mapping.TryGet("morale", out node)) values.Morale = read(node!);
        if (mapping.TryGet("health", out node)) values.Health = read(node!);
        if (mapping.TryGet("stun", out node)) values.Stun = read(node!);
        if (mapping.TryGet("mana", out node)) values.Mana = read(node!);
    }

    private static ItemUseValuesBuilder<int?> GetUseCost(ItemRuleBuilder builder, string key)
        => builder.UseCosts[key];

    private static ItemUseValuesBuilder<bool?> GetUseFlat(ItemRuleBuilder builder, string key)
        => builder.UseFlats[key];

    private static void ApplyActionScalar(
        RulePropertyReader reader, string key, ItemActionBuilder action, Action<ItemActionBuilder, int> apply)
    {
        if (reader.TryGet(key, out var node)) apply(action, YamlValueReader.ReadInt32(node!));
    }

    private static void ApplyEditableNames(
        RulePropertyReader reader, string key, List<string> target, bool unique = false)
    {
        if (reader.TryGet(key, out var node)) CampaignStartYaml.ApplyEditableNames(target, node!, unique);
    }

    private static void ApplyEditableMap<T>(
        RulePropertyReader reader, string key, Dictionary<string, T> target, Func<YamlNode, T> read)
    {
        if (!reader.TryGet(key, out var node)) return;
        if (node!.Tag == "!remove")
        {
            if (node is not YamlSequenceNode sequence)
                throw new YamlFormatException($"Item {key} !remove value must be a sequence.", node.Span);
            foreach (var item in sequence.Items) target.Remove(YamlValueReader.ReadString(item));
            return;
        }
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException($"Item {key} must be a mapping.", node.Span);
        if (node.Tag is null or "!!map" or "!info") target.Clear();
        else if (node.Tag != "!add") throw new YamlFormatException($"Unsupported collection tag '{node.Tag}'.", node.Span);
        foreach (var entry in mapping.Entries)
        {
            var name = entry.ScalarKey ?? throw new YamlFormatException($"Item {key} keys must be scalars.", entry.Key.Span);
            target[name] = read(entry.Value);
        }
    }

    private static ItemDamageRule FreezeDamage(ItemDamageBuilder damage) => new(
        damage.PredefinedType, ReadOnly(damage.Integers), ReadOnly(damage.Reals), ReadOnly(damage.Booleans));

    private static ReadOnlyDictionary<string, ItemUseValues<T>> FreezeUseValues<T>(
        Dictionary<string, ItemUseValuesBuilder<T>> values) =>
        new ReadOnlyDictionary<string, ItemUseValues<T>>(values.ToDictionary(
            pair => pair.Key, pair => pair.Value.Freeze(), StringComparer.Ordinal));

    private static ReadOnlyDictionary<string, T> ReadOnly<T>(Dictionary<string, T> values) =>
        new ReadOnlyDictionary<string, T>(new Dictionary<string, T>(values, StringComparer.Ordinal));

    private static ReadOnlyDictionary<string, IReadOnlyList<T>> ReadOnlyLists<T>(Dictionary<string, List<T>> values) =>
        new ReadOnlyDictionary<string, IReadOnlyList<T>>(values.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<T>)pair.Value.AsReadOnly(), StringComparer.Ordinal));

    private static int Read(YamlMappingNode mapping, string key, int current) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadInt32(node!) : current;
    private static bool Read(YamlMappingNode mapping, string key, bool current) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadBoolean(node!) : current;
    private static string Read(YamlMappingNode mapping, string key, string current) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadString(node!) : current;
    private static int ReadNullableInt(YamlMappingNode mapping, string key, int current) =>
        mapping.TryGet(key, out var node) ? ReadNullableInt(node!) ?? -1 : current;
    private static int? ReadNullableInt(YamlNode node) => node is YamlNullNode ? null : YamlValueReader.ReadInt32(node);
    private static bool? ReadNullableBool(YamlNode node) => node is YamlNullNode ? null : YamlValueReader.ReadBoolean(node);

    private static readonly HashSet<string> DamageIntegerKeys = new(StringComparer.Ordinal)
    {
        "FixRadius", "RandomType", "ResistType", "ArmorIgnore", "FireThreshold", "SmokeThreshold",
        "RandomWoundType", "TileDamageMethod", "TileDamageLimit",
    };
    private static readonly HashSet<string> DamageRealKeys = new(StringComparer.Ordinal)
    {
        "ArmorEffectiveness", "RadiusEffectiveness", "RadiusReduction", "ToHealth", "ToMana", "ToArmor",
        "ToArmorPre", "ToWound", "ToItem", "ToTile", "ToStun", "ToEnergy", "ToTime", "ToMorale",
    };
    private static readonly HashSet<string> DamageBooleanKeys = new(StringComparer.Ordinal)
    {
        "FireBlastCalc", "IgnoreDirection", "IgnoreSelfDestruct", "IgnorePainImmunity",
        "IgnoreNormalMoraleLose", "IgnoreOverKill", "RandomHealth", "RandomMana", "RandomArmor",
        "RandomArmorPre", "RandomWound", "RandomItem", "RandomTile", "RandomStun", "RandomEnergy",
        "RandomTime", "RandomMorale",
    };

    private static RuleSectionDefinition GetSection() => RuleSectionRegistry.TryGetNamed("items", out var section)
        ? section! : throw new InvalidOperationException();
}
