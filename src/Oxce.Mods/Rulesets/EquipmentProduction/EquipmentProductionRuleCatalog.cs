using Oxce.Core.Diagnostics;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Items;

namespace Oxce.Mods.Rulesets.EquipmentProduction;

public sealed class EquipmentProductionRuleCatalog
{
    private EquipmentProductionRuleCatalog(
        TypedRuleSection<ItemCategoryRule> itemCategories,
        TypedRuleSection<WeaponSetRule> weaponSets,
        TypedRuleSection<CraftWeaponRule> craftWeapons,
        TypedRuleSection<CraftRule> crafts,
        TypedRuleSection<UfoRule> ufos,
        TypedRuleSection<ResearchRule> research,
        TypedRuleSection<ManufactureRule> manufacture,
        TypedRuleSection<ManufactureShortcutRule> manufactureShortcuts)
    {
        ItemCategories = itemCategories;
        WeaponSets = weaponSets;
        CraftWeapons = craftWeapons;
        Crafts = crafts;
        Ufos = ufos;
        Research = research;
        Manufacture = manufacture;
        ManufactureShortcuts = manufactureShortcuts;
        Capabilities = ContentLoadCapabilities.Composed.AdvanceTo(ContentLoadStage.Typed);
    }

    public TypedRuleSection<ItemCategoryRule> ItemCategories { get; }
    public TypedRuleSection<WeaponSetRule> WeaponSets { get; }
    public TypedRuleSection<CraftWeaponRule> CraftWeapons { get; }
    public TypedRuleSection<CraftRule> Crafts { get; }
    public TypedRuleSection<UfoRule> Ufos { get; }
    public TypedRuleSection<ResearchRule> Research { get; }
    public TypedRuleSection<ManufactureRule> Manufacture { get; }
    public TypedRuleSection<ManufactureShortcutRule> ManufactureShortcuts { get; }
    public ContentLoadCapabilities Capabilities { get; }

    public static EquipmentProductionRuleCatalog Load(
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
            plan, Names.Select(EquipmentYaml.Section), diagnostics, compositionOptions);
        return new EquipmentProductionRuleCatalog(
            new ItemCategoryRuleLoader().Load(Required(unresolved, "itemCategories"), diagnostics, typedOptions),
            new WeaponSetRuleLoader().Load(Required(unresolved, "weaponSets"), diagnostics, typedOptions),
            new CraftWeaponRuleLoader().Load(Required(unresolved, "craftWeapons"), diagnostics, typedOptions),
            new CraftRuleLoader().Load(Required(unresolved, "crafts"), diagnostics, typedOptions),
            new UfoRuleLoader().Load(Required(unresolved, "ufos"), diagnostics, typedOptions),
            new ResearchRuleLoader().Load(Required(unresolved, "research"), diagnostics, typedOptions),
            new ManufactureRuleLoader().Load(Required(unresolved, "manufacture"), diagnostics, typedOptions),
            new ManufactureShortcutRuleLoader().Load(
                Required(unresolved, "manufactureShortcut"), diagnostics, typedOptions));
    }

    public EquipmentProductionValidation ValidateRelationships(
        ItemRuleCatalog items,
        IDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        diagnostics ??= NullDiagnosticSink.Instance;
        var issues = new List<EquipmentProductionValidationIssue>();
        ValidateItemCategories();
        ValidateWeaponSets();
        ValidateCraftWeapons();
        ValidateCrafts();
        ValidateResearch();
        ValidateManufacture();
        ValidateShortcuts();
        return new EquipmentProductionValidation(Array.AsReadOnly(issues.ToArray()));

        void ValidateItemCategories()
        {
            foreach (var category in ItemCategories.Rules)
                if (category.Value.ReplaceBy.Length != 0 && !ItemCategories.TryGet(category.Value.ReplaceBy, out _))
                    Missing(category, "replaceBy", category.Value.ReplaceBy);
        }

        void ValidateWeaponSets()
        {
            foreach (var set in WeaponSets.Rules)
                foreach (var id in set.Value.Weapons)
                    if (!items.Items.TryGet(id, out _)) Missing(set, "weapons", id);
        }

        void ValidateCraftWeapons()
        {
            foreach (var weapon in CraftWeapons.Rules)
            {
                if (weapon.Value.Launcher.Length == 0) Invalid(weapon, "launcher", null, "Launcher item is required.");
                else if (!items.Items.TryGet(weapon.Value.Launcher, out _))
                    Missing(weapon, "launcher", weapon.Value.Launcher);
                TypedRule<ItemRule>? clip = null;
                if (weapon.Value.Clip.Length != 0 && !items.Items.TryGet(weapon.Value.Clip, out clip))
                    Missing(weapon, "clip", weapon.Value.Clip);
                if (weapon.Value.Booleans["unifiedDamageFormula"] &&
                    weapon.Value.Launcher.Length == 0 && weapon.Value.Clip.Length == 0)
                    Invalid(weapon, "unifiedDamageFormula", null, "A clip or launcher is required.");
                if (weapon.Value.Integers["projectileType"] < 4 && weapon.Value.Integers["damage"] > 0 &&
                    weapon.Value.Integers["projectileSpeed"] <= 0)
                    Invalid(weapon, "projectileSpeed", null, "Missile-like weapons require positive speed.");
                if (weapon.Value.Integers["ammoMax"] != 0 && weapon.Value.Integers["rearmRate"] <= 0)
                    Invalid(weapon, "rearmRate", null, "Rearm rate must be positive when ammoMax is set.");
                if (weapon.Value.Integers["ammoMax"] != 0 && clip is not null &&
                    clip.Value.Values.GetInteger("clipSize") > weapon.Value.Integers["rearmRate"])
                    Invalid(weapon, "clip", weapon.Value.Clip, "Clip size exceeds the rearm rate.");
            }
        }

        void ValidateCrafts()
        {
            foreach (var craft in Crafts.Rules)
            {
                var refuel = craft.Value.Strings["refuelItem"];
                if (refuel.Length != 0 && !items.Items.TryGet(refuel, out _)) Missing(craft, "refuelItem", refuel);
                foreach (var weapon in craft.Value.FixedWeapons.Where(value => value.Length != 0))
                    if (!CraftWeapons.TryGet(weapon, out _)) Missing(craft, "fixedWeapons", weapon);
            }
        }

        void ValidateResearch()
        {
            foreach (var rule in Research.Rules)
            {
                if (rule.Value.Requirements.Count != 0 && rule.Value.Cost != 0)
                    Invalid(rule, "cost", null, "Research with requirements must have zero cost.");
                foreach (var pair in new[]
                {
                    ("lookup", One(rule.Value.Lookup == rule.Id ? "" : rule.Value.Lookup)),
                    ("dependencies", rule.Value.Dependencies), ("unlocks", rule.Value.Unlocks),
                    ("disables", rule.Value.Disables), ("reenables", rule.Value.Reenables),
                    ("getOneFree", rule.Value.GetOneFree), ("requires", rule.Value.Requirements),
                })
                    foreach (var id in pair.Item2)
                        if (!Research.TryGet(id, out _)) Missing(rule, pair.Item1, id);
                foreach (var group in rule.Value.GetOneFreeProtected)
                {
                    if (!Research.TryGet(group.Prerequisite, out _))
                        Missing(rule, "getOneFreeProtected", group.Prerequisite);
                    foreach (var id in group.Topics)
                        if (!Research.TryGet(id, out _)) Missing(rule, "getOneFreeProtected", id);
                }
                if (rule.Value.NeedItem && rule.Value.NeededItem is { Length: > 0 } needed &&
                    !items.Items.TryGet(needed, out _)) Missing(rule, "neededItem", needed);
            }
        }

        void ValidateManufacture()
        {
            foreach (var rule in Manufacture.Rules)
            {
                if (rule.Value.Time <= 0) Invalid(rule, "time", null, "Manufacturing time must be greater than zero.");
                foreach (var research in rule.Value.Requirements)
                    if (!Research.TryGet(research, out _)) Missing(rule, "requires", research);
                if (rule.Value.Category == "STR_CRAFT")
                {
                    var produced = rule.Value.ProducedItems.FirstOrDefault();
                    if (produced.Key is null) Invalid(rule, "producedItems", null, "No craft is defined for production.");
                    else
                    {
                        if (produced.Value != 1) Invalid(rule, "producedItems", produced.Key,
                            "Only one craft can be built per production unit.");
                        if (!Crafts.TryGet(produced.Key, out _)) Missing(rule, "producedItems", produced.Key);
                    }
                }
                else foreach (var produced in rule.Value.ProducedItems.Keys)
                    if (!items.Items.TryGet(produced, out _)) Missing(rule, "producedItems", produced);
                foreach (var required in rule.Value.RequiredItems.Keys)
                    if (!items.Items.TryGet(required, out _) && !Crafts.TryGet(required, out _))
                        Missing(rule, "requiredItems", required);
                foreach (var group in rule.Value.RandomProducedItems)
                    foreach (var produced in group.Items.Keys)
                        if (!items.Items.TryGet(produced, out _)) Missing(rule, "randomProducedItems", produced);
            }
        }

        void ValidateShortcuts()
        {
            foreach (var shortcut in ManufactureShortcuts.Rules)
            {
                if (Manufacture.TryGet(shortcut.Id, out _))
                    Invalid(shortcut, "name", shortcut.Id, "Shortcut name conflicts with a manufacture rule.");
                if (!Manufacture.TryGet(shortcut.Value.StartFrom, out _))
                    Missing(shortcut, "startFrom", shortcut.Value.StartFrom);
                foreach (var id in shortcut.Value.BreakDownItems)
                {
                    if (!items.Items.TryGet(id, out _)) Missing(shortcut, "breakDownItems", id);
                    if (!Manufacture.TryGet(id, out _)) Missing(shortcut, "breakDownItems", id);
                }
            }
        }

        void Missing<T>(TypedRule<T> owner, string property, string id) where T : notnull
        {
            Report(owner, property, id, "Referenced rule does not exist.", ModDiagnosticCodes.MissingRuleReference);
        }

        void Invalid<T>(TypedRule<T> owner, string property, string? id, string message) where T : notnull
        {
            Report(owner, property, id, message, ModDiagnosticCodes.InvalidRuleRelationship);
        }

        void Report<T>(TypedRule<T> owner, string property, string? id, string message, string code) where T : notnull
        {
            issues.Add(new EquipmentProductionValidationIssue(owner.Id, property, id, message));
            diagnostics.Report(new DiagnosticEvent(
                code,
                DiagnosticSeverity.Error,
                $"Rule '{owner.Id}' property '{property}' is invalid: {message}",
                owner.LastUpdateSource.Span,
                new DiagnosticContext(owner.LastUpdateSource.LayerId, owner.LastUpdateSource.ModId,
                    SectionName<T>(), owner.Id, id)));
        }
    }

    private static IReadOnlyList<string> One(string value) => value.Length == 0 ? [] : [value];

    private static string SectionName<T>() where T : notnull => typeof(T) switch
    {
        var type when type == typeof(ItemCategoryRule) => "itemCategories",
        var type when type == typeof(WeaponSetRule) => "weaponSets",
        var type when type == typeof(CraftWeaponRule) => "craftWeapons",
        var type when type == typeof(CraftRule) => "crafts",
        var type when type == typeof(UfoRule) => "ufos",
        var type when type == typeof(ResearchRule) => "research",
        var type when type == typeof(ManufactureRule) => "manufacture",
        var type when type == typeof(ManufactureShortcutRule) => "manufactureShortcut",
        _ => typeof(T).Name,
    };

    private static UnresolvedRuleSection Required(UnresolvedRuleCatalog catalog, string name) =>
        catalog.TryGetSection(name, out var section) ? section! : throw new InvalidOperationException();

    private static readonly string[] Names =
    [
        "itemCategories", "weaponSets", "craftWeapons", "crafts", "ufos", "research", "manufacture",
        "manufactureShortcut",
    ];
}

public sealed record EquipmentProductionValidationIssue(
    string RuleId,
    string Property,
    string? RelatedId,
    string Message);

public sealed record EquipmentProductionValidation(IReadOnlyList<EquipmentProductionValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
