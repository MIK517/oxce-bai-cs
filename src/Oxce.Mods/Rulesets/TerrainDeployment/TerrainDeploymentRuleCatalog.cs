using Oxce.Core.Diagnostics;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.PersonnelTactical;

namespace Oxce.Mods.Rulesets.TerrainDeployment;

public sealed class TerrainDeploymentRuleCatalog
{
    private TerrainDeploymentRuleCatalog(TypedRuleSection<TerrainRule> terrains,
        TypedRuleSection<AlienRaceRule> races, TypedRuleSection<EnviroEffectsRule> effects,
        TypedRuleSection<StartingConditionRule> conditions, TypedRuleSection<AlienDeploymentRule> deployments,
        TerrainSpecialRules special)
        : this(terrains, special.MapScripts, special.McdPatches, races, effects, conditions, deployments)
    {
    }

    [System.Text.Json.Serialization.JsonConstructor]
    internal TerrainDeploymentRuleCatalog(TypedRuleSection<TerrainRule> terrains,
        IReadOnlyDictionary<string, MapScriptRule> mapScripts,
        IReadOnlyDictionary<string, McdPatchRule> mcdPatches,
        TypedRuleSection<AlienRaceRule> alienRaces,
        TypedRuleSection<EnviroEffectsRule> enviroEffects,
        TypedRuleSection<StartingConditionRule> startingConditions,
        TypedRuleSection<AlienDeploymentRule> alienDeployments)
    {
        Terrains = terrains; AlienRaces = alienRaces; EnviroEffects = enviroEffects; StartingConditions = startingConditions;
        AlienDeployments = alienDeployments; MapScripts = mapScripts; McdPatches = mcdPatches;
        Capabilities = ContentLoadCapabilities.Composed.AdvanceTo(ContentLoadStage.Typed);
    }
    public TypedRuleSection<TerrainRule> Terrains { get; }
    public IReadOnlyDictionary<string, MapScriptRule> MapScripts { get; }
    public IReadOnlyDictionary<string, McdPatchRule> McdPatches { get; }
    public TypedRuleSection<AlienRaceRule> AlienRaces { get; }
    public TypedRuleSection<EnviroEffectsRule> EnviroEffects { get; }
    public TypedRuleSection<StartingConditionRule> StartingConditions { get; }
    public TypedRuleSection<AlienDeploymentRule> AlienDeployments { get; }
    public ContentLoadCapabilities Capabilities { get; }

    public static TerrainDeploymentRuleCatalog Load(ModLoadPlan plan, IDiagnosticSink? diagnostics = null,
        RulesetCompositionOptions? compositionOptions = null, TypedRuleLoadOptions? typedOptions = null)
    {
        diagnostics ??= NullDiagnosticSink.Instance; compositionOptions ??= new(); compositionOptions.Validate();
        var documents = RulesetDocumentCatalog.Parse(plan, compositionOptions);
        var unresolved = RulesetComposer.Compose(documents, Names.Select(TerrainDeploymentYaml.Section), diagnostics, compositionOptions);
        return Load(unresolved, documents, diagnostics, compositionOptions, typedOptions);
    }

    internal static TerrainDeploymentRuleCatalog Load(UnresolvedRuleCatalog unresolved,
        RulesetDocumentCatalog documents, IDiagnosticSink diagnostics,
        RulesetCompositionOptions compositionOptions, TypedRuleLoadOptions? typedOptions)
    {
        return new(new TerrainRuleLoader().Load(Required(unresolved, "terrains"), diagnostics, typedOptions),
            new AlienRaceRuleLoader().Load(Required(unresolved, "alienRaces"), diagnostics, typedOptions),
            new EnviroEffectsRuleLoader().Load(Required(unresolved, "enviroEffects"), diagnostics, typedOptions),
            new StartingConditionRuleLoader().Load(Required(unresolved, "startingConditions"), diagnostics, typedOptions),
            new AlienDeploymentRuleLoader().Load(Required(unresolved, "alienDeployments"), diagnostics, typedOptions),
            TerrainSpecialRulesComposer.Compose(documents, compositionOptions));
    }

    public TerrainDeploymentValidation ValidateRelationships(ItemRuleCatalog items,
        EquipmentProductionRuleCatalog equipment, PersonnelTacticalRuleCatalog personnel,
        IDiagnosticSink? diagnostics = null)
    {
        diagnostics ??= NullDiagnosticSink.Instance; var issues = new List<TerrainDeploymentValidationIssue>();
        foreach (var terrain in Terrains.Rules)
        {
            DeferOptional(terrain, "enviroEffects", terrain.Value.EnviroEffects, EnviroEffects);
            DeferOptionalScript(terrain, "script", terrain.Value.MapScript);
            foreach (var id in terrain.Value.MapScripts) DeferOptionalScript(terrain, "mapScripts", id);
        }
        foreach (var script in MapScripts.Values)
            foreach (var command in script.Commands)
                foreach (var id in command.RandomTerrain)
                    if (id is not "globeTerrain" and not "baseTerrain" && !Terrains.TryGet(id, out _))
                        DeferredSpecial(script, "randomTerrain", id);
        foreach (var effect in EnviroEffects.Rules)
            foreach (var pair in effect.Value.ArmorTransformations)
            {
                Require(effect, "armorTransformations", pair.Key, personnel.Armors);
                Require(effect, "armorTransformations", pair.Value, personnel.Armors);
            }
        foreach (var condition in StartingConditions.Rules)
        {
            foreach (var id in condition.Value.RequiredItems.Keys)
                if (!items.Items.TryGet(id, out _)) Deferred(condition, "requiredItems", id);
            foreach (var pair in condition.Value.CraftTransformations)
            { Require(condition, "craftTransformations", pair.Key, equipment.Crafts); Require(condition, "craftTransformations", pair.Value, equipment.Crafts); }
            foreach (var id in condition.Value.NameCollections["forbiddenArmorsInNextStage"])
                Require(condition, "forbiddenArmorsInNextStage", id, personnel.Armors);
        }
        foreach (var deployment in AlienDeployments.Rules)
        {
            DeferOptional(deployment, "enviroEffects", deployment.Value.Strings["enviroEffects"], EnviroEffects);
            DeferOptional(deployment, "startingCondition", deployment.Value.Strings["startingCondition"], StartingConditions);
            DeferOptional(deployment, "race", deployment.Value.Strings["race"], AlienRaces);
            foreach (var id in deployment.Value.RandomRaces)
                if (!AlienRaces.TryGet(id, out _)) Deferred(deployment, "randomRace", id);
            foreach (var id in deployment.Value.Terrains)
                if (!Terrains.TryGet(id, out _)) Deferred(deployment, "terrains", id);
            DeferOptionalScript(deployment, "script", deployment.Value.Strings["script"]);
            foreach (var id in deployment.Value.MapScripts) DeferOptionalScript(deployment, "mapScripts", id);
            var bounty = deployment.Value.Strings["missionBountyItem"];
            if (bounty.Length != 0 && !items.Items.TryGet(bounty, out _))
                Deferred(deployment, "missionBountyItem", bounty);
        }
        return new(issues.AsReadOnly());

        void DeferOptionalScript<T>(TypedRule<T> owner, string property, string id) where T : notnull
        { if (id.Length != 0 && id != "DEFAULT" && !MapScripts.ContainsKey(id)) Deferred(owner, property, id); }
        void DeferOptional<T, TRule>(TypedRule<T> owner, string property, string id, TypedRuleSection<TRule> section) where T : notnull where TRule : notnull
        { if (id.Length != 0 && !section.TryGet(id, out _)) Deferred(owner, property, id); }
        void Require<T, TRule>(TypedRule<T> owner, string property, string id, TypedRuleSection<TRule> section) where T : notnull where TRule : notnull
        { if (!section.TryGet(id, out _)) Missing(owner, property, id); }
        void Missing<T>(TypedRule<T> owner, string property, string id) where T : notnull
        {
            issues.Add(new(owner.Id, property, id, "Referenced rule does not exist."));
            diagnostics.Report(new(ModDiagnosticCodes.MissingRuleReference, DiagnosticSeverity.Error,
                $"Rule '{owner.Id}' property '{property}' references missing rule '{id}'.", owner.LastUpdateSource.Span,
                new(owner.LastUpdateSource.LayerId, owner.LastUpdateSource.ModId, owner.Value.GetType().Name, owner.Id, id)));
        }
        void Deferred<T>(TypedRule<T> owner, string property, string id) where T : notnull =>
            RuleReferenceDiagnostics.ReportDeferred(
                diagnostics, owner.LastUpdateSource, owner.Value.GetType().Name, owner.Id, property, id);
        void DeferredSpecial(MapScriptRule owner, string property, string id)
        {
            RuleReferenceDiagnostics.ReportDeferred(
                diagnostics, owner.LastUpdateSource, "mapScripts", owner.Id, property, id);
        }
    }

    private static UnresolvedRuleSection Required(UnresolvedRuleCatalog catalog, string name) =>
        catalog.TryGetSection(name, out var section) ? section! : throw new InvalidOperationException();
    private static readonly string[] Names = ["terrains", "alienRaces", "enviroEffects", "startingConditions", "alienDeployments"];
}

public sealed record TerrainDeploymentValidationIssue(string RuleId, string Property, string RelatedId, string Message);
public sealed record TerrainDeploymentValidation(IReadOnlyList<TerrainDeploymentValidationIssue> Issues)
{ public bool IsValid => Issues.Count == 0; }
