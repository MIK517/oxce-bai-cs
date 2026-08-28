using Oxce.Core.Diagnostics;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.PersonnelTactical;
using Oxce.Mods.Rulesets.TerrainDeployment;

namespace Oxce.Mods.Rulesets.MissionEvents;

public sealed class MissionEventRuleCatalog
{
    private MissionEventRuleCatalog(TypedRuleSection<UfoTrajectoryRule> trajectories, TypedRuleSection<AlienMissionRule> missions,
        TypedRuleSection<StrategicScriptRule> arcs, TypedRuleSection<StrategicScriptRule> eventScripts,
        TypedRuleSection<EventRule> events, TypedRuleSection<StrategicScriptRule> missionScripts,
        TypedRuleSection<StrategicScriptRule> adhocScripts, IReadOnlyDictionary<string, UfopaediaArticleRule> articles)
    { UfoTrajectories = trajectories; AlienMissions = missions; ArcScripts = arcs; EventScripts = eventScripts; Events = events; MissionScripts = missionScripts; AdhocScripts = adhocScripts; Ufopaedia = articles; Capabilities = ContentLoadCapabilities.Composed.AdvanceTo(ContentLoadStage.Typed); }
    public TypedRuleSection<UfoTrajectoryRule> UfoTrajectories { get; }
    public TypedRuleSection<AlienMissionRule> AlienMissions { get; }
    public TypedRuleSection<StrategicScriptRule> ArcScripts { get; }
    public TypedRuleSection<StrategicScriptRule> EventScripts { get; }
    public TypedRuleSection<EventRule> Events { get; }
    public TypedRuleSection<StrategicScriptRule> MissionScripts { get; }
    public TypedRuleSection<StrategicScriptRule> AdhocScripts { get; }
    public IReadOnlyDictionary<string, UfopaediaArticleRule> Ufopaedia { get; }
    public ContentLoadCapabilities Capabilities { get; }

    public static MissionEventRuleCatalog Load(ModLoadPlan plan, IDiagnosticSink? diagnostics = null,
        RulesetCompositionOptions? compositionOptions = null, TypedRuleLoadOptions? typedOptions = null)
    {
        diagnostics ??= NullDiagnosticSink.Instance; compositionOptions ??= new(); compositionOptions.Validate();
        var unresolved = RulesetComposer.Compose(plan, Names.Select(MissionEventYaml.Section), diagnostics, compositionOptions);
        return new(new UfoTrajectoryRuleLoader().Load(Required(unresolved, "ufoTrajectories"), diagnostics, typedOptions),
            new AlienMissionRuleLoader().Load(Required(unresolved, "alienMissions"), diagnostics, typedOptions),
            new StrategicScriptRuleLoader("arcScripts", StrategicScriptKind.Arc).Load(Required(unresolved, "arcScripts"), diagnostics, typedOptions),
            new StrategicScriptRuleLoader("eventScripts", StrategicScriptKind.Event).Load(Required(unresolved, "eventScripts"), diagnostics, typedOptions),
            new EventRuleLoader().Load(Required(unresolved, "events"), diagnostics, typedOptions),
            new StrategicScriptRuleLoader("missionScripts", StrategicScriptKind.Mission).Load(Required(unresolved, "missionScripts"), diagnostics, typedOptions),
            new StrategicScriptRuleLoader("adhocScripts", StrategicScriptKind.Mission).Load(Required(unresolved, "adhocScripts"), diagnostics, typedOptions),
            UfopaediaComposer.Compose(plan, compositionOptions));
    }

    public MissionEventValidation ValidateRelationships(CampaignStartRuleCatalog campaign, ItemRuleCatalog items,
        EquipmentProductionRuleCatalog equipment, PersonnelTacticalRuleCatalog personnel,
        TerrainDeploymentRuleCatalog terrain, IDiagnosticSink? diagnostics = null)
    {
        diagnostics ??= NullDiagnosticSink.Instance; var issues = new List<MissionEventValidationIssue>();
        foreach (var mission in AlienMissions.Rules)
        {
            foreach (var wave in mission.Value.Waves)
            {
                // The reference runtime permits a UFO, a direct deployment, or no spawned object.
                // Trajectories are unconditionally used when a wave runs, but they are not
                // part of the reference loader's eager afterLoad link pass.
                DeferRequired(mission, "waves.trajectory", wave.Trajectory, UfoTrajectories);
            }
            foreach (var entry in mission.Value.RaceWeights) foreach (var id in entry.Weights.Keys) DeferRequired(mission, "raceWeights", id, terrain.AlienRaces);
            foreach (var entry in mission.Value.RegionWeights) foreach (var id in entry.Weights.Keys) DeferRequired(mission, "regionWeights", id, campaign.Regions);
            DeferOptional(mission, "interruptResearch", mission.Value.Strings["interruptResearch"], equipment.Research);
            DeferOptional(mission, "siteType", mission.Value.Strings["siteType"], terrain.AlienDeployments);
            DeferOptional(mission, "operationBaseType", mission.Value.Strings["operationBaseType"], terrain.AlienDeployments);
        }
        ValidateScripts(MissionScripts.Rules, validateEvents: false); ValidateScripts(AdhocScripts.Rules, validateEvents: false);
        ValidateScripts(EventScripts.Rules, validateEvents: true);
        foreach (var arc in ArcScripts.Rules)
        { foreach (var id in arc.Value.Sequential) DeferRequired(arc, "sequentialArcs", id, equipment.Research); foreach (var id in arc.Value.Random.Keys) DeferRequired(arc, "randomArcs", id, equipment.Research); ValidateTriggers(arc); }
        foreach (var rule in Events.Rules)
        {
            foreach (var id in rule.Value.Regions) DeferRequired(rule, "regionList", id, campaign.Regions);
            foreach (var id in rule.Value.EveryMultiItems.Keys.Concat(rule.Value.EveryItems).Concat(rule.Value.RandomItems).Concat(rule.Value.WeightedItems.Keys)) DeferRequired(rule, "items", id, items.Items);
            foreach (var map in rule.Value.RandomMultiItems) foreach (var id in map.Keys) DeferRequired(rule, "randomMultiItemList", id, items.Items);
            foreach (var id in rule.Value.Research) Require(rule, "researchList", id, equipment.Research);
            DeferOptional(rule, "interruptResearch", rule.Value.Strings["interruptResearch"], equipment.Research);
            DeferOptional(rule, "spawnedCraftType", rule.Value.Strings["spawnedCraftType"], equipment.Crafts);
            DeferOptional(rule, "spawnedPersonType", rule.Value.Strings["spawnedPersonType"], personnel.Soldiers);
            foreach (var id in rule.Value.EveryMultiSoldiers.Keys) DeferRequired(rule, "everyMultiSoldierList", id, personnel.Soldiers);
            foreach (var map in rule.Value.RandomMultiSoldiers) foreach (var id in map.Keys) DeferRequired(rule, "randomMultiSoldierList", id, personnel.Soldiers);
        }
        foreach (var article in Ufopaedia)
        {
            foreach (var id in article.Value.Requires.Concat(article.Value.DisabledBy)) if (id != "STR_NULL") DeferSpecial(article.Value, article.Key, "requires", id, equipment.Research);
            switch (article.Value.TypeId)
            {
                case 1: DeferArticle(article, equipment.Crafts); break;
                case 2: DeferArticle(article, equipment.CraftWeapons); break;
                case 3 or 4 or 13 or 14: DeferArticle(article, items.Items); break;
                case 5 or 15: DeferArticle(article, personnel.Armors); break;
                case 6 or 16: DeferArticle(article, campaign.Facilities); break;
                case 9 or 17: DeferArticle(article, equipment.Ufos); break;
                case 11: DeferArticle(article, equipment.Crafts); break;
                case 12: DeferArticle(article, equipment.CraftWeapons); break;
                case 18: DeferArticle(article, personnel.Soldiers); break;
                case 19: DeferArticle(article, personnel.Units); break;
            }
        }
        return new(issues.AsReadOnly());

        void ValidateScripts(IReadOnlyList<TypedRule<StrategicScriptRule>> scripts, bool validateEvents)
        { foreach (var script in scripts) { foreach (var entry in script.Value.MissionWeights) foreach (var id in entry.Weights.Keys) DeferRequired(script, "missionWeights", id, AlienMissions); foreach (var entry in script.Value.RaceWeights) foreach (var id in entry.Weights.Keys) DeferRequired(script, "raceWeights", id, terrain.AlienRaces); foreach (var entry in script.Value.RegionWeights) foreach (var id in entry.Weights.Keys) DeferRequired(script, "regionWeights", id, campaign.Regions); if (validateEvents) { foreach (var id in script.Value.Sequential) DeferRequired(script, "oneTimeSequentialEvents", id, Events); foreach (var id in script.Value.Random.Keys) DeferRequired(script, "oneTimeRandomEvents", id, Events); foreach (var entry in script.Value.EventWeights) foreach (var id in entry.Weights.Keys) DeferRequired(script, "eventWeights", id, Events); } ValidateTriggers(script); } }
        void ValidateTriggers(TypedRule<StrategicScriptRule> script)
        { foreach (var pair in script.Value.Triggers) foreach (var id in pair.Value.Keys) { if (pair.Key == "researchTriggers") DeferRequired(script, pair.Key, id, equipment.Research); else if (pair.Key == "itemTriggers") DeferRequired(script, pair.Key, id, items.Items); else if (pair.Key == "facilityTriggers") DeferRequired(script, pair.Key, id, campaign.Facilities); else if (pair.Key == "soldierTypeTriggers") DeferRequired(script, pair.Key, id, personnel.Soldiers); else if (pair.Key == "xcomBaseInRegionTriggers") DeferRequired(script, pair.Key, id, campaign.Regions); else if (pair.Key is "xcomBaseInCountryTriggers" or "pactCountryTriggers") DeferRequired(script, pair.Key, id, campaign.Countries); } }
        void DeferArticle<TRule>(KeyValuePair<string, UfopaediaArticleRule> article, TypedRuleSection<TRule> section) where TRule : notnull
        { if (!section.TryGet(article.Key, out _)) DeferSpecial(article.Value, article.Key, "id", article.Key, section); }
        void DeferSpecial<TRule>(UfopaediaArticleRule owner, string ownerId, string property, string id, TypedRuleSection<TRule> section) where TRule : notnull
        { if (!section.TryGet(id, out _)) RuleReferenceDiagnostics.ReportDeferred(diagnostics, owner.LastUpdateSource, "ufopaedia", ownerId, property, id); }
        void DeferOptional<T, TRule>(TypedRule<T> owner, string property, string id, TypedRuleSection<TRule> section) where T : notnull where TRule : notnull { if (id.Length != 0) DeferRequired(owner, property, id, section); }
        void DeferRequired<T, TRule>(TypedRule<T> owner, string property, string id, TypedRuleSection<TRule> section) where T : notnull where TRule : notnull
        { if (!section.TryGet(id, out _)) RuleReferenceDiagnostics.ReportDeferred(diagnostics, owner.LastUpdateSource, section.Definition.Name, owner.Id, property, id); }
        void Require<T, TRule>(TypedRule<T> owner, string property, string id, TypedRuleSection<TRule> section) where T : notnull where TRule : notnull { if (!section.TryGet(id, out _)) Missing(owner.Id, property, id, owner.LastUpdateSource, section.Definition.Name); }
        void Missing(string owner, string property, string id, RuleOperationSource source, string type)
        { issues.Add(new(owner, property, id, "Referenced rule does not exist.")); diagnostics.Report(new(ModDiagnosticCodes.MissingRuleReference, DiagnosticSeverity.Error, $"Rule '{owner}' property '{property}' references missing rule '{id}'.", source.Span, new(source.LayerId, source.ModId, type, owner, id))); }
    }
    private static UnresolvedRuleSection Required(UnresolvedRuleCatalog catalog, string name) => catalog.TryGetSection(name, out var section) ? section! : throw new InvalidOperationException();
    private static readonly string[] Names = ["ufoTrajectories", "alienMissions", "arcScripts", "eventScripts", "events", "missionScripts", "adhocScripts"];
}

public sealed record MissionEventValidationIssue(string RuleId, string Property, string RelatedId, string Message);
public sealed record MissionEventValidation(IReadOnlyList<MissionEventValidationIssue> Issues) { public bool IsValid => Issues.Count == 0; }
