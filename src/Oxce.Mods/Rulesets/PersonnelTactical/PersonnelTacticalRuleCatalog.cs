using Oxce.Core.Diagnostics;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;

namespace Oxce.Mods.Rulesets.PersonnelTactical;

public sealed class PersonnelTacticalRuleCatalog
{
    private PersonnelTacticalRuleCatalog(
        TypedRuleSection<InventoryRule> inventories,
        TypedRuleSection<ArmorRule> armors,
        TypedRuleSection<SkillRule> skills,
        TypedRuleSection<SoldierRule> soldiers,
        TypedRuleSection<UnitRule> units,
        TypedRuleSection<SoldierBonusRule> bonuses,
        TypedRuleSection<SoldierTransformationRule> transformations,
        TypedRuleSection<CommendationRule> commendations)
    {
        Inventories = inventories;
        Armors = armors;
        Skills = skills;
        Soldiers = soldiers;
        Units = units;
        Bonuses = bonuses;
        Transformations = transformations;
        Commendations = commendations;
        Capabilities = ContentLoadCapabilities.Composed.AdvanceTo(ContentLoadStage.Typed);
    }

    public TypedRuleSection<InventoryRule> Inventories { get; }
    public TypedRuleSection<ArmorRule> Armors { get; }
    public TypedRuleSection<SkillRule> Skills { get; }
    public TypedRuleSection<SoldierRule> Soldiers { get; }
    public TypedRuleSection<UnitRule> Units { get; }
    public TypedRuleSection<SoldierBonusRule> Bonuses { get; }
    public TypedRuleSection<SoldierTransformationRule> Transformations { get; }
    public TypedRuleSection<CommendationRule> Commendations { get; }
    public ContentLoadCapabilities Capabilities { get; }

    public static PersonnelTacticalRuleCatalog Load(
        ModLoadPlan plan,
        IDiagnosticSink? diagnostics = null,
        RulesetCompositionOptions? compositionOptions = null,
        TypedRuleLoadOptions? typedOptions = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        diagnostics ??= NullDiagnosticSink.Instance;
        compositionOptions ??= new RulesetCompositionOptions();
        compositionOptions.Validate();
        var unresolved = RulesetComposer.Compose(
            plan, Names.Select(PersonnelTacticalYaml.Section), diagnostics, compositionOptions);
        return new PersonnelTacticalRuleCatalog(
            new InventoryRuleLoader().Load(Required(unresolved, "invs"), diagnostics, typedOptions),
            new ArmorRuleLoader().Load(Required(unresolved, "armors"), diagnostics, typedOptions),
            new SkillRuleLoader().Load(Required(unresolved, "skills"), diagnostics, typedOptions),
            new SoldierRuleLoader().Load(Required(unresolved, "soldiers"), diagnostics, typedOptions),
            new UnitRuleLoader().Load(Required(unresolved, "units"), diagnostics, typedOptions),
            new SoldierBonusRuleLoader().Load(Required(unresolved, "soldierBonuses"), diagnostics, typedOptions),
            new SoldierTransformationRuleLoader().Load(
                Required(unresolved, "soldierTransformation"), diagnostics, typedOptions),
            new CommendationRuleLoader().Load(Required(unresolved, "commendations"), diagnostics, typedOptions));
    }

    public PersonnelTacticalValidation ValidateRelationships(
        ItemRuleCatalog items,
        EquipmentProductionRuleCatalog equipment,
        IDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(equipment);
        diagnostics ??= NullDiagnosticSink.Instance;
        var issues = new List<PersonnelTacticalValidationIssue>();
        ValidateInventories();
        ValidateArmors();
        ValidateSkills();
        ValidateSoldiers();
        ValidateUnits();
        ValidateTransformations();
        ValidateCommendations();
        return new PersonnelTacticalValidation(
            Array.AsReadOnly(issues.ToArray()),
            BuildCaches());

        void ValidateInventories()
        {
            foreach (var rule in Inventories.Rules)
                foreach (var id in rule.Value.Costs.Keys)
                    if (!Inventories.TryGet(id, out _)) Missing(rule, "costs", id);
        }

        void ValidateArmors()
        {
            foreach (var rule in Armors.Rules)
            {
                foreach (var id in rule.Value.CorpseBattle) RequireItem(rule, "corpseBattle", id);
                foreach (var id in rule.Value.BuiltInWeapons) RequireItem(rule, "builtInWeapons", id);
                foreach (var id in rule.Value.Units)
                    if (!Soldiers.TryGet(id, out _)) Missing(rule, "units", id);
                RequireOptionalItem(rule, "corpseGeo", rule.Value.Strings["corpseGeo"]);
                RequireOptionalItem(rule, "storeItem", rule.Value.Strings["storeItem"], allowNone: true);
                RequireOptionalItem(rule, "selfDestructItem", rule.Value.Strings["selfDestructItem"]);
                RequireOptionalItem(rule, "specialWeapon", rule.Value.Strings["specialWeapon"]);
                RequireOptional(rule, "requires", rule.Value.Strings["requires"], equipment.Research);
                RequireOptional(rule, "requiresAward", rule.Value.Strings["requiresAward"], Commendations);
                RequireOptional(rule, "requiresBonus", rule.Value.Strings["requiresBonus"], Bonuses);
                if (rule.Value.CorpseBattle.Count != rule.Value.TotalSize)
                    Invalid(rule, "corpseBattle", null, "Battle corpse count must match armor size squared.");
                if (rule.Value.Loftemps.Count != rule.Value.TotalSize)
                    Invalid(rule, "loftempsSet", null, "Loft template count must match armor size squared.");
                if (rule.Value.Strings["corpseGeo"].Length == 0)
                    Invalid(rule, "corpseGeo", null, "A geoscape corpse item is required.");
            }
        }

        void ValidateSkills()
        {
            foreach (var rule in Skills.Rules)
            {
                foreach (var id in rule.Value.CompatibleWeapons) RequireItem(rule, "compatibleWeapons", id);
                foreach (var id in rule.Value.RequiredBonuses)
                    if (!Bonuses.TryGet(id, out _)) Missing(rule, "requiredBonuses", id);
            }
        }

        void ValidateSoldiers()
        {
            foreach (var rule in Soldiers.Rules)
            {
                var armor = rule.Value.Strings["armor"];
                if (armor.Length == 0 || !Armors.TryGet(armor, out _)) Missing(rule, "armor", armor);
                var weapon = rule.Value.Strings["specialWeapon"];
                if (weapon.Length != 0)
                {
                    if (!items.Items.TryGet(weapon, out var item)) Missing(rule, "specialWeapon", weapon);
                    else if (item!.Value.Values.GetInteger("battleType") is 1 or 3 &&
                        item.Value.Values.GetInteger("clipSize") == 0)
                        Invalid(rule, "specialWeapon", weapon, "Firearm and melee special weapons require internal ammo.");
                }
                foreach (var skill in rule.Value.Skills)
                    if (!Skills.TryGet(skill, out _)) Missing(rule, "skills", skill);
            }
        }

        void ValidateUnits()
        {
            foreach (var rule in Units.Rules)
            {
                var armor = rule.Value.Strings["armor"];
                if (armor.Length == 0 || !Armors.TryGet(armor, out _)) Missing(rule, "armor", armor);
                var spawned = rule.Value.Strings["spawnUnit"];
                if (spawned.Length != 0 && !Units.TryGet(spawned, out _)) Missing(rule, "spawnUnit", spawned);
                foreach (var set in rule.Value.BuiltInWeaponSets)
                    foreach (var id in set) RequireItem(rule, "builtInWeaponSets", id);
                var liveAlien = rule.Value.Strings["liveAlien"];
                if (liveAlien != "STR_NULL") RequireOptionalItem(rule, "liveAlien", liveAlien);
                var recovery = rule.Value.Strings["civilianRecoveryType"];
                if (recovery.Length != 0 && recovery is not "STR_NULL" and not "STR_ENGINEER" and not "STR_SCIENTIST" &&
                    !Soldiers.TryGet(recovery, out _) && !items.Items.TryGet(recovery, out _))
                    Missing(rule, "civilianRecoveryType", recovery);
            }
        }

        void ValidateTransformations()
        {
            foreach (var rule in Transformations.Rules)
            {
                foreach (var id in rule.Value.Requirements)
                    if (!equipment.Research.TryGet(id, out _)) Missing(rule, "requires", id);
                RequireOptionalItem(rule, "producedItem", rule.Value.Strings["producedItem"]);
                RequireOptional(rule, "producedSoldierType", rule.Value.Strings["producedSoldierType"], Soldiers);
                RequireOptional(rule, "producedSoldierArmor", rule.Value.Strings["producedSoldierArmor"], Armors);
                RequireOptional(rule, "soldierBonusType", rule.Value.Strings["soldierBonusType"], Bonuses);
                foreach (var id in rule.Value.AllowedSoldierTypes)
                    if (!Soldiers.TryGet(id, out _)) Missing(rule, "allowedSoldierTypes", id);
                foreach (var pair in new[]
                {
                    ("requiredPreviousTransformations", rule.Value.RequiredPreviousTransformations),
                    ("forbiddenPreviousTransformations", rule.Value.ForbiddenPreviousTransformations),
                    ("removeTransformations", rule.Value.RemovedTransformations),
                })
                    foreach (var id in pair.Item2)
                        if (!Transformations.TryGet(id, out _)) Missing(rule, pair.Item1, id);
                foreach (var id in rule.Value.RequiredItems.Keys) RequireItem(rule, "requiredItems", id);
                foreach (var id in rule.Value.RequiredCommendations.Keys)
                    if (!Commendations.TryGet(id, out _)) Missing(rule, "requiredCommendations", id);
            }
        }

        void ValidateCommendations()
        {
            foreach (var rule in Commendations.Rules)
            {
                foreach (var id in rule.Value.SoldierBonusTypes)
                    if (!Bonuses.TryGet(id, out _)) Missing(rule, "soldierBonusTypes", id);
                foreach (var id in rule.Value.Requirements)
                    if (!equipment.Research.TryGet(id, out _)) Missing(rule, "requires", id);
                foreach (var id in rule.Value.Units)
                    if (!Soldiers.TryGet(id, out _)) Missing(rule, "units", id);
            }
        }

        PersonnelDerivedCaches BuildCaches()
        {
            var eligible = Armors.Rules.Where(rule =>
                    rule.Value.InfiniteSupply || items.Items.TryGet(rule.Value.Strings["storeItem"], out _))
                .OrderBy(rule => rule.Value.Integers["listOrder"])
                .ThenBy(rule => rule.Id, StringComparer.Ordinal)
                .Select(rule => rule.Id)
                .ToArray();
            var storage = Armors.Rules.Select(rule => rule.Value.Strings["storeItem"])
                .Where(id => id.Length != 0 && id != "STR_NONE" && items.Items.TryGet(id, out _))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            return new(Array.AsReadOnly(eligible), Array.AsReadOnly(storage));
        }

        void RequireItem<T>(TypedRule<T> owner, string property, string id) where T : notnull
        {
            if (!items.Items.TryGet(id, out _)) Missing(owner, property, id);
        }

        void RequireOptionalItem<T>(TypedRule<T> owner, string property, string id, bool allowNone = false) where T : notnull
        {
            if (id.Length == 0 || id == "STR_NULL" || allowNone && id == "STR_NONE") return;
            RequireItem(owner, property, id);
        }

        void RequireOptional<T, TRule>(
            TypedRule<T> owner, string property, string id, TypedRuleSection<TRule> target)
            where T : notnull where TRule : notnull
        {
            if (id.Length != 0 && id != "STR_NULL" && !target.TryGet(id, out _)) Missing(owner, property, id);
        }

        void Missing<T>(TypedRule<T> owner, string property, string id) where T : notnull =>
            Report(owner, property, id, "Referenced rule does not exist.", ModDiagnosticCodes.MissingRuleReference);

        void Invalid<T>(TypedRule<T> owner, string property, string? id, string message) where T : notnull =>
            Report(owner, property, id, message, ModDiagnosticCodes.InvalidRuleRelationship);

        void Report<T>(TypedRule<T> owner, string property, string? id, string message, string code) where T : notnull
        {
            issues.Add(new PersonnelTacticalValidationIssue(owner.Id, property, id, message));
            diagnostics.Report(new DiagnosticEvent(
                code,
                DiagnosticSeverity.Error,
                $"Rule '{owner.Id}' property '{property}' is invalid: {message}",
                owner.LastUpdateSource.Span,
                new DiagnosticContext(owner.LastUpdateSource.LayerId, owner.LastUpdateSource.ModId,
                    SectionName<T>(), owner.Id, id)));
        }
    }

    private static string SectionName<T>() where T : notnull => typeof(T) switch
    {
        var type when type == typeof(InventoryRule) => "invs",
        var type when type == typeof(ArmorRule) => "armors",
        var type when type == typeof(SkillRule) => "skills",
        var type when type == typeof(SoldierRule) => "soldiers",
        var type when type == typeof(UnitRule) => "units",
        var type when type == typeof(SoldierBonusRule) => "soldierBonuses",
        var type when type == typeof(SoldierTransformationRule) => "soldierTransformation",
        var type when type == typeof(CommendationRule) => "commendations",
        _ => typeof(T).Name,
    };

    private static UnresolvedRuleSection Required(UnresolvedRuleCatalog catalog, string name) =>
        catalog.TryGetSection(name, out var section) ? section! : throw new InvalidOperationException();

    private static readonly string[] Names =
    [
        "invs", "armors", "skills", "soldiers", "units", "soldierBonuses",
        "soldierTransformation", "commendations",
    ];
}

public sealed record PersonnelTacticalValidationIssue(
    string RuleId,
    string Property,
    string? RelatedId,
    string Message);

public sealed record PersonnelDerivedCaches(
    IReadOnlyList<string> ArmorsForSoldiers,
    IReadOnlyList<string> ArmorStorageItems);

public sealed record PersonnelTacticalValidation(
    IReadOnlyList<PersonnelTacticalValidationIssue> Issues,
    PersonnelDerivedCaches Caches)
{
    public bool IsValid => Issues.Count == 0;
}
