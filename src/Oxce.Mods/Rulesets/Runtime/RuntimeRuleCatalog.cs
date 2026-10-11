using System.Collections.ObjectModel;
using System.Text;
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
        RuntimeRuleFamily<CraftWeaponRuleFamily, RuntimeCraftWeaponRule> craftWeapons,
        RuntimeRuleFamily<ItemRuleFamily, RuntimeItemRule> items,
        RuntimeRuleFamily<SoldierRuleFamily, RuntimeSoldierRule> soldiers,
        RuntimeRuleFamily<ArmorRuleFamily, RuntimeArmorRule> armors,
        RuntimeRuleFamily<UnitRuleFamily, RuntimeUnitRule> units,
        RuntimeRuleFamily<InventoryRuleFamily, RuntimeInventoryRule> inventories,
        RuntimeRuleFamily<SoldierBonusRuleFamily, RuntimeSoldierBonusRule> soldierBonuses,
        RuntimeRuleFamily<SoldierTransformationRuleFamily, RuntimeSoldierTransformationRule> soldierTransformations,
        RuntimeRuleFamily<CommendationRuleFamily, RuntimeCommendationRule> commendations,
        RuntimeRuleFamily<SkillRuleFamily, RuntimeIdentityRule> skills,
        RuntimeRuleFamily<ResearchRuleFamily, RuntimeResearchRule> research,
        RuntimeRuleFamily<ManufactureRuleFamily, RuntimeManufactureRule> manufacture,
        RuntimeRuleFamily<EventRuleFamily, RuntimeEventRule> events,
        RuntimeRuleFamily<UfoRuleFamily, RuntimeUfoRule> ufos,
        RuntimeRuleFamily<UfoTrajectoryRuleFamily, RuntimeUfoTrajectoryRule> ufoTrajectories,
        RuntimeRuleFamily<AlienMissionRuleFamily, RuntimeAlienMissionRule> alienMissions,
        RuntimeRuleFamily<AlienDeploymentRuleFamily, RuntimeAlienDeploymentRule> alienDeployments,
        RuntimeRuleFamily<AlienRaceRuleFamily, RuntimeIdentityRule> alienRaces,
        RuntimeRuleFamily<MissionScriptRuleFamily, RuntimeMissionScriptRule> missionScripts,
        RuntimeRuleFamily<AdhocScriptRuleFamily, RuntimeMissionScriptRule> adhocScripts,
        RuntimeRuleFamily<ArcScriptRuleFamily, RuntimeArcScriptRule> arcScripts,
        RuntimeRuleFamily<EventScriptRuleFamily, RuntimeEventScriptRule> eventScripts,
        RuntimeRuleFamily<RuntimeScriptFamily, RuntimeScriptRule> scripts,
        RuntimeCampaignSettings campaign)
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
        Units = units;
        Inventories = inventories;
        SoldierBonuses = soldierBonuses;
        SoldierTransformations = soldierTransformations;
        Commendations = commendations;
        Skills = skills;
        Research = research;
        var byteOrder = Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right));
        ResearchInMapOrder = Array.AsReadOnly(research.Rules
            .Select(static entry => (Entry: entry, Key: Encoding.UTF8.GetBytes(entry.Id)))
            .OrderBy(static pair => pair.Key, byteOrder).Select(static pair => pair.Entry).ToArray());
        Manufacture = manufacture;
        Events = events;
        Ufos = ufos;
        UfoTrajectories = ufoTrajectories;
        AlienMissions = alienMissions;
        AlienDeployments = alienDeployments;
        AlienRaces = alienRaces;
        MissionScripts = missionScripts;
        AdhocScripts = adhocScripts;
        ArcScripts = arcScripts;
        EventScripts = eventScripts;
        Scripts = scripts;
        Campaign = campaign;
    }

    public ContentGenerationId Generation { get; }
    public RuntimeRuleFamily<CountryRuleFamily, RuntimeCountryRule> Countries { get; }
    public RuntimeRuleFamily<RegionRuleFamily, RuntimeRegionRule> Regions { get; }
    public RuntimeRuleFamily<FacilityRuleFamily, RuntimeFacilityRule> Facilities { get; }
    public RuntimeRuleFamily<CraftRuleFamily, RuntimeCraftRule> Crafts { get; }
    public RuntimeRuleFamily<CraftWeaponRuleFamily, RuntimeCraftWeaponRule> CraftWeapons { get; }
    public RuntimeRuleFamily<ItemRuleFamily, RuntimeItemRule> Items { get; }
    public RuntimeRuleFamily<SoldierRuleFamily, RuntimeSoldierRule> Soldiers { get; }
    public RuntimeRuleFamily<ArmorRuleFamily, RuntimeArmorRule> Armors { get; }
    public RuntimeRuleFamily<UnitRuleFamily, RuntimeUnitRule> Units { get; }
    public RuntimeRuleFamily<InventoryRuleFamily, RuntimeInventoryRule> Inventories { get; }
    public RuntimeRuleFamily<SoldierBonusRuleFamily, RuntimeSoldierBonusRule> SoldierBonuses { get; }
    public RuntimeRuleFamily<SoldierTransformationRuleFamily, RuntimeSoldierTransformationRule> SoldierTransformations { get; }
    public RuntimeRuleFamily<CommendationRuleFamily, RuntimeCommendationRule> Commendations { get; }
    public RuntimeRuleFamily<SkillRuleFamily, RuntimeIdentityRule> Skills { get; }
    public RuntimeRuleFamily<ResearchRuleFamily, RuntimeResearchRule> Research { get; }
    /// <summary>SavedGame::getAvailableResearchProjects iterates the UTF-8 std::map, independently of listOrder.</summary>
    public IReadOnlyList<RuntimeRule<RuntimeResearchRule>> ResearchInMapOrder { get; }
    public RuntimeRuleFamily<ManufactureRuleFamily, RuntimeManufactureRule> Manufacture { get; }
    public RuntimeRuleFamily<EventRuleFamily, RuntimeEventRule> Events { get; }
    public RuntimeRuleFamily<UfoRuleFamily, RuntimeUfoRule> Ufos { get; }
    public RuntimeRuleFamily<UfoTrajectoryRuleFamily, RuntimeUfoTrajectoryRule> UfoTrajectories { get; }
    public RuntimeRuleFamily<AlienMissionRuleFamily, RuntimeAlienMissionRule> AlienMissions { get; }
    public RuntimeRuleFamily<AlienDeploymentRuleFamily, RuntimeAlienDeploymentRule> AlienDeployments { get; }
    public RuntimeRuleFamily<AlienRaceRuleFamily, RuntimeIdentityRule> AlienRaces { get; }
    public RuntimeRuleFamily<MissionScriptRuleFamily, RuntimeMissionScriptRule> MissionScripts { get; }
    public RuntimeRuleFamily<AdhocScriptRuleFamily, RuntimeMissionScriptRule> AdhocScripts { get; }
    public RuntimeRuleFamily<ArcScriptRuleFamily, RuntimeArcScriptRule> ArcScripts { get; }
    public RuntimeRuleFamily<EventScriptRuleFamily, RuntimeEventScriptRule> EventScripts { get; }
    public RuntimeRuleFamily<RuntimeScriptFamily, RuntimeScriptRule> Scripts { get; }
    public RuntimeCampaignSettings Campaign { get; }
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
    RuntimeRuleCompatibilitySidecar Compatibility,
    IReadOnlyList<RuntimeRuleLinkIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
