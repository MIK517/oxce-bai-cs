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
    {
        Terrains = terrains; AlienRaces = races; EnviroEffects = effects; StartingConditions = conditions;
        AlienDeployments = deployments; MapScripts = special.MapScripts; McdPatches = special.McdPatches;
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
        var unresolved = RulesetComposer.Compose(plan, Names.Select(TerrainDeploymentYaml.Section), diagnostics, compositionOptions);
        return new(new TerrainRuleLoader().Load(Required(unresolved, "terrains"), diagnostics, typedOptions),
            new AlienRaceRuleLoader().Load(Required(unresolved, "alienRaces"), diagnostics, typedOptions),
            new EnviroEffectsRuleLoader().Load(Required(unresolved, "enviroEffects"), diagnostics, typedOptions),
            new StartingConditionRuleLoader().Load(Required(unresolved, "startingConditions"), diagnostics, typedOptions),
            new AlienDeploymentRuleLoader().Load(Required(unresolved, "alienDeployments"), diagnostics, typedOptions),
            TerrainSpecialRulesComposer.Compose(plan, compositionOptions));
    }

    public TerrainDeploymentValidation ValidateRelationships(ItemRuleCatalog items,
        EquipmentProductionRuleCatalog equipment, PersonnelTacticalRuleCatalog personnel,
        IDiagnosticSink? diagnostics = null)
    {
        diagnostics ??= NullDiagnosticSink.Instance; var issues = new List<TerrainDeploymentValidationIssue>();
        foreach (var terrain in Terrains.Rules)
        {
            Optional(terrain, "enviroEffects", terrain.Value.EnviroEffects, EnviroEffects);
            OptionalScript(terrain, "script", terrain.Value.MapScript);
            foreach (var id in terrain.Value.MapScripts) OptionalScript(terrain, "mapScripts", id);
        }
        foreach (var script in MapScripts.Values)
            foreach (var command in script.Commands)
                foreach (var id in command.RandomTerrain)
                    if (id is not "globeTerrain" and not "baseTerrain" && !Terrains.TryGet(id, out _))
                        MissingSpecial(script, "randomTerrain", id);
        foreach (var effect in EnviroEffects.Rules)
            foreach (var pair in effect.Value.ArmorTransformations)
            {
                Require(effect, "armorTransformations", pair.Key, personnel.Armors);
                Require(effect, "armorTransformations", pair.Value, personnel.Armors);
            }
        foreach (var condition in StartingConditions.Rules)
        {
            foreach (var id in condition.Value.RequiredItems.Keys) Require(condition, "requiredItems", id, items.Items);
            foreach (var pair in condition.Value.CraftTransformations)
            { Require(condition, "craftTransformations", pair.Key, equipment.Crafts); Require(condition, "craftTransformations", pair.Value, equipment.Crafts); }
            foreach (var id in condition.Value.NameCollections["forbiddenArmorsInNextStage"])
                Require(condition, "forbiddenArmorsInNextStage", id, personnel.Armors);
        }
        foreach (var deployment in AlienDeployments.Rules)
        {
            Optional(deployment, "enviroEffects", deployment.Value.Strings["enviroEffects"], EnviroEffects);
            Optional(deployment, "startingCondition", deployment.Value.Strings["startingCondition"], StartingConditions);
            Optional(deployment, "race", deployment.Value.Strings["race"], AlienRaces);
            foreach (var id in deployment.Value.RandomRaces) Require(deployment, "randomRace", id, AlienRaces);
            foreach (var id in deployment.Value.Terrains) Require(deployment, "terrains", id, Terrains);
            OptionalScript(deployment, "script", deployment.Value.Strings["script"]);
            foreach (var id in deployment.Value.MapScripts) OptionalScript(deployment, "mapScripts", id);
            var bounty = deployment.Value.Strings["missionBountyItem"];
            if (bounty.Length != 0) Require(deployment, "missionBountyItem", bounty, items.Items);
        }
        return new(issues.AsReadOnly());

        void OptionalScript<T>(TypedRule<T> owner, string property, string id) where T : notnull
        { if (id.Length != 0 && id != "DEFAULT" && !MapScripts.ContainsKey(id)) Missing(owner, property, id); }
        void Optional<T, TRule>(TypedRule<T> owner, string property, string id, TypedRuleSection<TRule> section) where T : notnull where TRule : notnull
        { if (id.Length != 0 && !section.TryGet(id, out _)) Missing(owner, property, id); }
        void Require<T, TRule>(TypedRule<T> owner, string property, string id, TypedRuleSection<TRule> section) where T : notnull where TRule : notnull
        { if (!section.TryGet(id, out _)) Missing(owner, property, id); }
        void Missing<T>(TypedRule<T> owner, string property, string id) where T : notnull
        {
            issues.Add(new(owner.Id, property, id, "Referenced rule does not exist."));
            diagnostics.Report(new(ModDiagnosticCodes.MissingRuleReference, DiagnosticSeverity.Error,
                $"Rule '{owner.Id}' property '{property}' references missing rule '{id}'.", owner.LastUpdateSource.Span,
                new(owner.LastUpdateSource.LayerId, owner.LastUpdateSource.ModId, owner.Value.GetType().Name, owner.Id, id)));
        }
        void MissingSpecial(MapScriptRule owner, string property, string id)
        {
            issues.Add(new(owner.Id, property, id, "Referenced rule does not exist."));
            diagnostics.Report(new(ModDiagnosticCodes.MissingRuleReference, DiagnosticSeverity.Error,
                $"Map script '{owner.Id}' property '{property}' references missing terrain '{id}'.",
                owner.LastUpdateSource.Span, new(owner.LastUpdateSource.LayerId, owner.LastUpdateSource.ModId,
                    "mapScripts", owner.Id, id)));
        }
    }

    private static UnresolvedRuleSection Required(UnresolvedRuleCatalog catalog, string name) =>
        catalog.TryGetSection(name, out var section) ? section! : throw new InvalidOperationException();
    private static readonly string[] Names = ["terrains", "alienRaces", "enviroEffects", "startingConditions", "alienDeployments"];
}

public sealed record TerrainDeploymentValidationIssue(string RuleId, string Property, string RelatedId, string Message);
public sealed record TerrainDeploymentValidation(IReadOnlyList<TerrainDeploymentValidationIssue> Issues)
{ public bool IsValid => Issues.Count == 0; }
