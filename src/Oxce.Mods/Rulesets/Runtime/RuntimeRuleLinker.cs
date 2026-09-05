using System.Collections.ObjectModel;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Resources;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.Phase3;
using Oxce.Mods.Rulesets.PersonnelTactical;
using Oxce.Mods.Rulesets.Presentation;
using EventRule = Oxce.Mods.Rulesets.MissionEvents.EventRule;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed record RuntimeRuleLinkOptions
{
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Compiles compatibility-facing typed rules into the dense, generation-scoped form used by gameplay.
/// Reference behavior: RuleCountry.cpp/RuleRegion.cpp/RuleBaseFacility.cpp/RuleCraft.cpp/RuleItem.cpp/
/// RuleSoldier.cpp afterLoad methods and Mod.cpp newSave/getStartingBase at reference commit 4df3a5e.
/// </summary>
public static class RuntimeRuleLinker
{
    public static RuntimeRuleLinkResult Link(
        Phase3ContentCatalog content,
        ResolvedResourceCatalog resources,
        IReadOnlyList<ContentScriptArtifact>? scripts = null,
        IDiagnosticSink? diagnostics = null,
        RuntimeRuleLinkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(resources);
        scripts ??= [];
        diagnostics ??= NullDiagnosticSink.Instance;
        options ??= new RuntimeRuleLinkOptions();
        var cancellationToken = options.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var generation = resources.Generation;
        var issues = new List<RuntimeRuleLinkIssue>();

        var countryHandles = Handles<CountryRuleFamily, CountryRule>(generation, content.CampaignStart.Countries);
        var regionHandles = Handles<RegionRuleFamily, RegionRule>(generation, content.CampaignStart.Regions);
        var facilityHandles = Handles<FacilityRuleFamily, BaseFacilityRule>(generation, content.CampaignStart.Facilities);
        var craftHandles = Handles<CraftRuleFamily, CraftRule>(generation, content.EquipmentProduction.Crafts);
        var craftWeaponHandles = Handles<CraftWeaponRuleFamily, CraftWeaponRule>(
            generation, content.EquipmentProduction.CraftWeapons);
        var itemHandles = Handles<ItemRuleFamily, ItemRule>(generation, content.Items.Items);
        var soldierHandles = Handles<SoldierRuleFamily, SoldierRule>(generation, content.PersonnelTactical.Soldiers);
        var armorHandles = Handles<ArmorRuleFamily, ArmorRule>(generation, content.PersonnelTactical.Armors);
        var skillHandles = Handles<SkillRuleFamily, SkillRule>(generation, content.PersonnelTactical.Skills);
        var researchHandles = Handles<ResearchRuleFamily, ResearchRule>(generation, content.EquipmentProduction.Research);
        var eventHandles = Handles<EventRuleFamily, EventRule>(generation, content.MissionEvents.Events);
        var scriptBuild = BuildScripts(generation, scripts, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var countries = BuildFamily<CountryRuleFamily, CountryRule, RuntimeCountryRule>(
            generation, content.CampaignStart.Countries, rule => new RuntimeCountryRule(
                rule.Value.FundingBase,
                rule.Value.FundingCap,
                rule.Value.LabelLongitude,
                rule.Value.LabelLatitude,
                rule.Value.LabelColor,
                rule.Value.ZoomLevel,
                rule.Value.Areas,
                rule.Value.ProvidedBaseFunctions,
                rule.Value.ForbiddenBaseFunctions,
                rule.Value.SignedPactEvent,
                OptionalRequired(eventHandles, rule.Value.SignedPactEvent, "countries", rule, "signedPactEvent"),
                rule.Value.RejoinedXcomEvent,
                OptionalRequired(eventHandles, rule.Value.RejoinedXcomEvent, "countries", rule, "rejoinedXcomEvent"),
                ScriptsFor(scriptBuild.ByOwner, "countries", rule.Id)));

        cancellationToken.ThrowIfCancellationRequested();
        var regions = BuildFamily<RegionRuleFamily, RegionRule, RuntimeRegionRule>(
            generation, content.CampaignStart.Regions, rule => new RuntimeRegionRule(
                rule.Value.BaseCost,
                rule.Value.Areas,
                rule.Value.MissionZones,
                rule.Value.MissionWeights,
                rule.Value.RegionWeight,
                rule.Value.MissionRegion,
                OptionalRuntime(regionHandles, rule.Value.MissionRegion),
                rule.Value.ProvidedBaseFunctions,
                rule.Value.ForbiddenBaseFunctions));

        cancellationToken.ThrowIfCancellationRequested();
        var facilities = BuildFamily<FacilityRuleFamily, BaseFacilityRule, RuntimeFacilityRule>(
            generation, content.CampaignStart.Facilities, rule => new RuntimeFacilityRule(
                rule.Value.SizeX,
                rule.Value.SizeY,
                rule.Value.Lift,
                rule.Value.BuildCost,
                rule.Value.RefundValue,
                rule.Value.BuildTime,
                rule.Value.MonthlyCost,
                rule.Value.Storage,
                rule.Value.Personnel,
                rule.Value.Aliens,
                rule.Value.Crafts,
                rule.Value.Laboratories,
                rule.Value.Workshops,
                rule.Value.PsiLaboratories,
                rule.Value.ListOrder,
                rule.Value.MapName,
                References(researchHandles, rule.Value.Requires),
                RequiredMany(facilityHandles, rule.Value.BuildOverFacilities, "facilities", rule,
                    "buildOverFacilities"),
                RequiredMany(facilityHandles, rule.Value.LeavesBehindOnSell, "facilities", rule,
                    "leavesBehindOnSell"),
                OptionalRequired(facilityHandles, rule.Value.DestroyedFacility, "facilities", rule,
                    "destroyedFacility"),
                OptionalRequired(itemHandles, rule.Value.AmmoItem, "facilities", rule, "ammoItem"),
                Array.AsReadOnly(rule.Value.BuildCostItems.Select(pair => new RuntimeFacilityItemCost(
                    pair.Key,
                    OptionalRuntime(itemHandles, pair.Key),
                    pair.Value.Build,
                    pair.Value.Refund)).ToArray()),
                Resource(rule.Value.SpriteShape, "BASEBITS.PCK", ResourceKind.Sprite, resources),
                Resource(rule.Value.SpriteFacility, "BASEBITS.PCK", ResourceKind.Sprite, resources),
                Resource(rule.Value.FireSound, "GEO.CAT", ResourceKind.Sound, resources),
                Resource(rule.Value.HitSound, "GEO.CAT", ResourceKind.Sound, resources),
                Resource(rule.Value.PlaceSound, "GEO.CAT", ResourceKind.Sound, resources)));

        cancellationToken.ThrowIfCancellationRequested();
        var crafts = BuildFamily<CraftRuleFamily, CraftRule, RuntimeCraftRule>(
            generation, content.EquipmentProduction.Crafts, rule => new RuntimeCraftRule(
                rule.Value.Integers["listOrder"],
                rule.Value.Integers["costBuy"],
                rule.Value.Integers["costRent"],
                rule.Value.Integers["costSell"],
                rule.Value.Integers["transferTime"],
                rule.Value.Stats.Get("fuelMax"),
                rule.Value.Stats.Get("damageMax"),
                rule.Value.Stats.Get("speedMax"),
                rule.Value.Stats.Get("soldiers"),
                rule.Value.Stats.Get("vehicles"),
                rule.Value.EffectiveMaximumUnits,
                rule.Value.EffectiveMaximumVehiclesAndLargeSoldiers,
                rule.Value.Booleans["allowLanding"],
                References(researchHandles, rule.Value.Requirements),
                OptionalRequired(itemHandles, rule.Value.Strings["refuelItem"], "crafts", rule, "refuelItem"),
                RequiredMany(craftWeaponHandles, rule.Value.FixedWeapons.Where(static id => id.Length != 0),
                    "crafts", rule, "fixedWeapons"),
                ScriptsFor(scriptBuild.ByOwner, "crafts", rule.Id)));

        cancellationToken.ThrowIfCancellationRequested();
        var items = BuildFamily<ItemRuleFamily, ItemRule, RuntimeItemRule>(
            generation, content.Items.Items, rule => new RuntimeItemRule(
                rule.Value.Values.GetString("name"),
                rule.Value.Values.GetInteger("listOrder"),
                rule.Value.Values.GetInteger("costBuy"),
                rule.Value.Values.GetInteger("costSell"),
                rule.Value.Values.GetInteger("transferTime"),
                rule.Value.Values.GetInteger("weight"),
                rule.Value.Values.Real("size"),
                rule.Value.Values.GetInteger("battleType"),
                rule.Value.Values.GetInteger("clipSize"),
                RequiredMany(researchHandles, rule.Value.Requirements, "items", rule, "requires"),
                Array.AsReadOnly(rule.Value.CompatibleAmmo
                    .Select(group => RequiredMany(itemHandles, group, "items", rule, "compatibleAmmo"))
                    .ToArray()),
                ScriptsFor(scriptBuild.ByOwner, "items", rule.Id)));

        cancellationToken.ThrowIfCancellationRequested();
        var armors = BuildFamily<ArmorRuleFamily, ArmorRule, RuntimeArmorRule>(
            generation, content.PersonnelTactical.Armors, rule => new RuntimeArmorRule(
                rule.Value.Integers["listOrder"],
                rule.Value.Integers["size"],
                rule.Value.EffectiveSpaceOccupied,
                rule.Value.Strings["storeItem"],
                OptionalItem(itemHandles, rule.Value.Strings["storeItem"])));

        cancellationToken.ThrowIfCancellationRequested();
        var soldiers = BuildFamily<SoldierRuleFamily, SoldierRule, RuntimeSoldierRule>(
            generation, content.PersonnelTactical.Soldiers, rule => new RuntimeSoldierRule(
                rule.Value.Integers["listOrder"],
                rule.Value.Integers["group"],
                rule.Value.Integers["costBuy"],
                rule.Value.Integers["costSalary"],
                rule.Value.Integers["transferTime"],
                rule.Value.Booleans["allowPromotion"],
                rule.Value.Booleans["allowPiloting"],
                RequiredRule(armorHandles, rule.Value.Strings["armor"], "soldiers", rule, "armor"),
                References(researchHandles, rule.Value.Requirements),
                RequiredMany(skillHandles, rule.Value.Skills, "soldiers", rule, "skills"),
                ScriptsFor(scriptBuild.ByOwner, "soldiers", rule.Id)));

        cancellationToken.ThrowIfCancellationRequested();
        var settings = BuildCampaignSettings(content.CampaignStart.Settings);
        var compatibility = new RuntimeRuleCompatibilitySidecar(CompatibilityEntries(content));
        var catalog = new RuntimeRuleCatalog(
            generation,
            countries,
            regions,
            facilities,
            crafts,
            IdentityFamily<CraftWeaponRuleFamily, CraftWeaponRule>(generation, content.EquipmentProduction.CraftWeapons),
            items,
            soldiers,
            armors,
            IdentityFamily<SkillRuleFamily, SkillRule>(generation, content.PersonnelTactical.Skills),
            IdentityFamily<ResearchRuleFamily, ResearchRule>(generation, content.EquipmentProduction.Research),
            IdentityFamily<EventRuleFamily, EventRule>(generation, content.MissionEvents.Events),
            scriptBuild.Family,
            settings);
        cancellationToken.ThrowIfCancellationRequested();
        return new RuntimeRuleLinkResult(catalog, compatibility, Array.AsReadOnly(issues.ToArray()));

        RuntimeCampaignSettings BuildCampaignSettings(CampaignStartSettings source)
        {
            var templates = source.StartingBases
                .OrderBy(static pair => pair.Key)
                .Select(pair => StartingBase(pair.Key, pair.Value))
                .ToArray();
            return new RuntimeCampaignSettings(
                source.StartingTime,
                source.StartingDifficulty,
                source.InitialFunding,
                source.CostHireEngineer,
                source.CostHireScientist,
                source.CostEngineer,
                source.CostScientist,
                source.PersonnelTransferTime,
                source.GlobalTransferCostMultiplier,
                source.GlobalTransferCostDivisor,
                OptionalRuntime(researchHandles, source.PsiUnlockResearch),
                OptionalRuntime(researchHandles, source.FakeUnderwaterBaseUnlockResearch),
                OptionalRuntime(researchHandles, source.NewBaseUnlockResearch),
                OptionalRuntime(researchHandles, source.HireScientistsUnlockResearch),
                OptionalRuntime(researchHandles, source.HireEngineersUnlockResearch),
                OptionalRuntime(facilityHandles, source.DestroyedFacility),
                Array.AsReadOnly(templates));
        }

        RuntimeStartingBaseTemplate StartingBase(StartingBaseVariant variant, YamlMappingNode node)
        {
            var soldierRules = content.PersonnelTactical.Soldiers.Rules;
            var defaultSoldier = soldierRules.Count == 0 ? string.Empty : soldierRules[0].Id;
            var facilities = Sequence(node, "facilities").Select(entry => new RuntimeStartingFacility(
                RequiredId(facilityHandles, Type(entry), "startingBase", variant.ToString(), "facilities"),
                Integer(entry, "x"), Integer(entry, "y"), Integer(entry, "buildTime"))).ToArray();
            var crafts = Sequence(node, "crafts").Select(entry => new RuntimeStartingCraft(
                RequiredId(craftHandles, Type(entry), "startingBase", variant.ToString(), "crafts"),
                Integer(entry, "id"))).ToArray();
            var soldiers = Sequence(node, "soldiers").Select(entry => new RuntimeStartingSoldier(
                RequiredId(soldierHandles, Type(entry, defaultSoldier), "startingBase", variant.ToString(),
                    "soldiers"), Integer(entry, "id"))).ToArray();
            var items = Mapping(node, "items").Select(entry => new RuntimeStartingItem(
                RequiredId(itemHandles, entry.ScalarKey ?? string.Empty, "startingBase", variant.ToString(), "items"),
                ScalarInteger(entry.Value))).ToArray();
            var randomCount = 0;
            var random = Array.Empty<RuntimeStartingSoldierBatch>();
            if (node.TryGet("randomSoldiers", out var randomNode))
            {
                if (randomNode is YamlMappingNode randomMap)
                {
                    random = randomMap.Entries.Select(entry => new RuntimeStartingSoldierBatch(
                        RequiredId(soldierHandles, entry.ScalarKey ?? string.Empty, "startingBase", variant.ToString(),
                            "randomSoldiers"), ScalarInteger(entry.Value))).ToArray();
                }
                else
                {
                    randomCount = ScalarInteger(randomNode!);
                }
            }

            return new RuntimeStartingBaseTemplate(
                variant,
                Array.AsReadOnly(facilities),
                Array.AsReadOnly(crafts),
                Array.AsReadOnly(soldiers),
                Array.AsReadOnly(items),
                randomCount,
                Array.AsReadOnly(random),
                Integer(node, "scientists"),
                Integer(node, "engineers"));
        }

        RuleHandle<TFamily> RequiredRule<TFamily, TRule>(
            Dictionary<string, RuleHandle<TFamily>> handles,
            string id,
            string family,
            TypedRule<TRule> owner,
            string property)
            where TRule : notnull => RequiredId(handles, id, family, owner.Id, property, owner.LastUpdateSource);

        RuleHandle<TFamily> RequiredId<TFamily>(
            Dictionary<string, RuleHandle<TFamily>> handles,
            string id,
            string family,
            string ownerId,
            string property,
            RuleOperationSource? source = null)
        {
            if (handles.TryGetValue(id, out var handle)) return handle;
            Report(family, ownerId, property, id, "Referenced runtime rule does not exist.", source);
            return default;
        }

        RuleHandleList<TFamily> RequiredMany<TFamily, TRule>(
            Dictionary<string, RuleHandle<TFamily>> handles,
            IEnumerable<string> ids,
            string family,
            TypedRule<TRule> owner,
            string property)
            where TRule : notnull => new(ids.Select(id => RequiredRule(handles, id, family, owner, property)));

        RuleHandle<TFamily>? OptionalRequired<TFamily, TRule>(
            Dictionary<string, RuleHandle<TFamily>> handles,
            string id,
            string family,
            TypedRule<TRule> owner,
            string property)
            where TRule : notnull => id.Length == 0 ? null : RequiredRule(handles, id, family, owner, property);

        void Report(
            string family,
            string ownerId,
            string property,
            string id,
            string message,
            RuleOperationSource? source)
        {
            issues.Add(new RuntimeRuleLinkIssue(family, ownerId, property, id, message, source));
            diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.InvalidRuntimeRuleLink,
                DiagnosticSeverity.Error,
                $"Runtime {family} rule '{ownerId}' property '{property}' references missing rule '{id}'.",
                source?.Span,
                new DiagnosticContext(source?.LayerId, source?.ModId, family, ownerId, id)));
        }
    }

    private static Dictionary<string, RuleHandle<TFamily>> Handles<TFamily, TRule>(
        ContentGenerationId generation,
        TypedRuleSection<TRule> source)
        where TRule : notnull => source.Rules.Select((rule, index) => new
        {
            rule.Id,
            Handle = new RuleHandle<TFamily>(generation, index),
        }).ToDictionary(static pair => pair.Id, static pair => pair.Handle, StringComparer.Ordinal);

    private static RuntimeRuleFamily<TFamily, TProjection> BuildFamily<TFamily, TRule, TProjection>(
        ContentGenerationId generation,
        TypedRuleSection<TRule> source,
        Func<TypedRule<TRule>, TProjection> project)
        where TRule : notnull
        where TProjection : notnull => new(generation, source.Rules.Select(rule =>
            new RuntimeRule<TProjection>(rule.Id, project(rule), rule.CreationSource, rule.LastUpdateSource)));

    private static RuntimeRuleFamily<TFamily, RuntimeIdentityRule> IdentityFamily<TFamily, TRule>(
        ContentGenerationId generation,
        TypedRuleSection<TRule> source)
        where TRule : notnull => BuildFamily<TFamily, TRule, RuntimeIdentityRule>(
            generation, source, static rule => new RuntimeIdentityRule(rule.Id));

    private static RuleHandle<TFamily>? OptionalRuntime<TFamily>(
        Dictionary<string, RuleHandle<TFamily>> handles,
        string id) => id.Length != 0 && handles.TryGetValue(id, out var handle) ? handle : null;

    private static RuntimeRuleReferenceList<TFamily> References<TFamily>(
        Dictionary<string, RuleHandle<TFamily>> handles,
        IEnumerable<string> ids) => new(ids.Select(id =>
            new RuntimeRuleReference<TFamily>(id, OptionalRuntime(handles, id))));

    private static RuleHandle<ItemRuleFamily>? OptionalItem(
        Dictionary<string, RuleHandle<ItemRuleFamily>> handles,
        string id) => id.Length != 0 && id != "STR_NONE" && handles.TryGetValue(id, out var handle) ? handle : null;

    private static RuntimeIndexedResourceReference Resource(
        RuleIndexReference reference,
        string defaultSet,
        ResourceKind kind,
        ResolvedResourceCatalog resources)
    {
        var set = defaultSet;
        if (resources.TryResolveIndex(kind, set, reference.ModId, reference.Index, out var resolved))
        {
            return new RuntimeIndexedResourceReference(
                set,
                resolved!.RuntimeIndex,
                kind,
                resolved.Handle);
        }
        return new RuntimeIndexedResourceReference(
            set,
            reference.Index,
            kind,
            null);
    }

    private static ScriptBuild BuildScripts(
        ContentGenerationId generation,
        IReadOnlyList<ContentScriptArtifact> scripts,
        CancellationToken cancellationToken)
    {
        var owners = new Dictionary<(string Section, string Id), List<RuleHandle<RuntimeScriptFamily>>>();
        var rules = new RuntimeRule<RuntimeScriptRule>[scripts.Count];
        for (var index = 0; index < scripts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var script = scripts[index];
            var handle = new RuleHandle<RuntimeScriptFamily>(generation, index);
            var key = (script.OwnerSection, script.OwnerId);
            if (!owners.TryGetValue(key, out var list)) owners.Add(key, list = []);
            list.Add(handle);
            var position = new SourcePosition(Math.Max(1, script.SourceLine), 1, 0);
            var source = new RuleOperationSource(
                string.Empty,
                string.Empty,
                script.SourcePath,
                new SourceSpan(script.SourcePath, position, position));
            var id = $"{script.Scope}/{script.OwnerSection}/{script.OwnerId}/{script.ParserName}/{index}";
            rules[index] = new RuntimeRule<RuntimeScriptRule>(id, new RuntimeScriptRule(script), source, source);
        }

        return new ScriptBuild(new RuntimeRuleFamily<RuntimeScriptFamily, RuntimeScriptRule>(generation, rules), owners);
    }

    private static RuleHandleList<RuntimeScriptFamily> ScriptsFor(
        Dictionary<(string Section, string Id), List<RuleHandle<RuntimeScriptFamily>>> owners,
        string section,
        string id) => owners.TryGetValue((section, id), out var handles) ? new(handles) : new([]);

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode node, string key) =>
        node.TryGet(key, out var value) && value is YamlSequenceNode sequence ? sequence.Items : [];

    private static IEnumerable<YamlMappingEntry> Mapping(YamlMappingNode node, string key) =>
        node.TryGet(key, out var value) && value is YamlMappingNode mapping ? mapping.Entries : [];

    private static string Type(YamlNode node, string defaultValue = "")
    {
        if (node is YamlMappingNode mapping && mapping.TryGet("type", out var type))
            return YamlValueReader.ReadString(type!);
        return node is YamlScalarNode ? YamlValueReader.ReadString(node) : defaultValue;
    }

    private static int Integer(YamlNode node, string key) => node is YamlMappingNode mapping &&
        mapping.TryGet(key, out var value) ? YamlValueReader.ReadInt32(value!) : 0;

    private static int ScalarInteger(YamlNode node) => YamlValueReader.ReadInt32(node);

    private static IEnumerable<RuntimeRuleCompatibilityEntry> CompatibilityEntries(Phase3ContentCatalog content)
    {
        return Entries("countries", content.CampaignStart.Countries)
            .Concat(Entries("regions", content.CampaignStart.Regions))
            .Concat(Entries("facilities", content.CampaignStart.Facilities))
            .Concat(Entries("crafts", content.EquipmentProduction.Crafts))
            .Concat(Entries("items", content.Items.Items))
            .Concat(Entries("armors", content.PersonnelTactical.Armors))
            .Concat(Entries("soldiers", content.PersonnelTactical.Soldiers));

        static IEnumerable<RuntimeRuleCompatibilityEntry> Entries<TRule>(
            string family,
            TypedRuleSection<TRule> section)
            where TRule : notnull => section.Rules
            .Where(static rule => rule.DeferredProperties.Count != 0)
            .Select(rule => new RuntimeRuleCompatibilityEntry(family, rule.Id, rule.DeferredProperties));
    }

    private sealed record ScriptBuild(
        RuntimeRuleFamily<RuntimeScriptFamily, RuntimeScriptRule> Family,
        Dictionary<(string Section, string Id), List<RuleHandle<RuntimeScriptFamily>>> ByOwner);
}
