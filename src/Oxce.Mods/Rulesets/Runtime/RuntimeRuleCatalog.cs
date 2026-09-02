using System.Collections.ObjectModel;
using Oxce.Mods.Resources;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed record RuntimeRuleCompatibilityEntry(
    string Family,
    string RuleId,
    IReadOnlyList<DeferredRuleProperty> DeferredProperties);

public sealed class RuntimeRuleCompatibilitySidecar
{
    internal RuntimeRuleCompatibilitySidecar(IEnumerable<RuntimeRuleCompatibilityEntry> entries) =>
        Entries = Array.AsReadOnly(entries.ToArray());

    public IReadOnlyList<RuntimeRuleCompatibilityEntry> Entries { get; }
}

public sealed class RuntimeRuleCatalog
{
    internal RuntimeRuleCatalog(
        ContentGenerationId generation,
        RuntimeRuleFamily<CountryRuleFamily, RuntimeCountryRule> countries,
        RuntimeRuleFamily<RegionRuleFamily, RuntimeRegionRule> regions,
        RuntimeRuleFamily<FacilityRuleFamily, RuntimeFacilityRule> facilities,
        RuntimeRuleFamily<CraftRuleFamily, RuntimeCraftRule> crafts,
        RuntimeRuleFamily<CraftWeaponRuleFamily, RuntimeIdentityRule> craftWeapons,
        RuntimeRuleFamily<ItemRuleFamily, RuntimeItemRule> items,
        RuntimeRuleFamily<SoldierRuleFamily, RuntimeSoldierRule> soldiers,
        RuntimeRuleFamily<ArmorRuleFamily, RuntimeArmorRule> armors,
        RuntimeRuleFamily<SkillRuleFamily, RuntimeIdentityRule> skills,
        RuntimeRuleFamily<ResearchRuleFamily, RuntimeIdentityRule> research,
        RuntimeRuleFamily<EventRuleFamily, RuntimeIdentityRule> events,
        RuntimeRuleFamily<RuntimeScriptFamily, RuntimeScriptRule> scripts,
        RuntimeCampaignSettings campaign,
        RuntimeRuleCompatibilitySidecar compatibility)
    {
        Generation = generation;
        Countries = countries;
        Regions = regions;
        Facilities = facilities;
        Crafts = crafts;
        CraftWeapons = craftWeapons;
        Items = items;
        Soldiers = soldiers;
        Armors = armors;
        Skills = skills;
        Research = research;
        Events = events;
        Scripts = scripts;
        Campaign = campaign;
        Compatibility = compatibility;
    }

    public ContentGenerationId Generation { get; }
    public RuntimeRuleFamily<CountryRuleFamily, RuntimeCountryRule> Countries { get; }
    public RuntimeRuleFamily<RegionRuleFamily, RuntimeRegionRule> Regions { get; }
    public RuntimeRuleFamily<FacilityRuleFamily, RuntimeFacilityRule> Facilities { get; }
    public RuntimeRuleFamily<CraftRuleFamily, RuntimeCraftRule> Crafts { get; }
    public RuntimeRuleFamily<CraftWeaponRuleFamily, RuntimeIdentityRule> CraftWeapons { get; }
    public RuntimeRuleFamily<ItemRuleFamily, RuntimeItemRule> Items { get; }
    public RuntimeRuleFamily<SoldierRuleFamily, RuntimeSoldierRule> Soldiers { get; }
    public RuntimeRuleFamily<ArmorRuleFamily, RuntimeArmorRule> Armors { get; }
    public RuntimeRuleFamily<SkillRuleFamily, RuntimeIdentityRule> Skills { get; }
    public RuntimeRuleFamily<ResearchRuleFamily, RuntimeIdentityRule> Research { get; }
    public RuntimeRuleFamily<EventRuleFamily, RuntimeIdentityRule> Events { get; }
    public RuntimeRuleFamily<RuntimeScriptFamily, RuntimeScriptRule> Scripts { get; }
    public RuntimeCampaignSettings Campaign { get; }
    public RuntimeRuleCompatibilitySidecar Compatibility { get; }
}

public sealed record RuntimeRuleLinkIssue(
    string Family,
    string RuleId,
    string Property,
    string RelatedId,
    string Message,
    RuleOperationSource? Source);

public sealed record RuntimeRuleLinkResult(
    RuntimeRuleCatalog Catalog,
    IReadOnlyList<RuntimeRuleLinkIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
