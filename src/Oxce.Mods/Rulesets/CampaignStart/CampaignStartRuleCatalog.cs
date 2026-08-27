using Oxce.Core.Diagnostics;
using Oxce.Mods.Loading;

namespace Oxce.Mods.Rulesets.CampaignStart;

public sealed class CampaignStartRuleCatalog
{
    private CampaignStartRuleCatalog(
        TypedRuleSection<CountryRule> countries,
        TypedRuleSection<CountryRule> globeLabels,
        TypedRuleSection<RegionRule> regions,
        TypedRuleSection<BaseFacilityRule> facilities,
        CampaignStartSettings settings)
    {
        Countries = countries;
        GlobeLabels = globeLabels;
        Regions = regions;
        Facilities = facilities;
        Settings = settings;
        Capabilities = ContentLoadCapabilities.Composed.AdvanceTo(ContentLoadStage.Typed);
    }

    public TypedRuleSection<CountryRule> Countries { get; }
    public TypedRuleSection<CountryRule> GlobeLabels { get; }
    public TypedRuleSection<RegionRule> Regions { get; }
    public TypedRuleSection<BaseFacilityRule> Facilities { get; }
    public CampaignStartSettings Settings { get; }
    public ContentLoadCapabilities Capabilities { get; }

    public static CampaignStartRuleCatalog Load(
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
            plan, Names.Select(RequiredDefinition), diagnostics, compositionOptions);
        return new CampaignStartRuleCatalog(
            new CountryRuleLoader("countries").Load(Required(unresolved, "countries"), diagnostics, typedOptions),
            new CountryRuleLoader("extraGlobeLabels").Load(
                Required(unresolved, "extraGlobeLabels"), diagnostics, typedOptions),
            new RegionRuleLoader().Load(Required(unresolved, "regions"), diagnostics, typedOptions),
            new BaseFacilityRuleLoader().Load(Required(unresolved, "facilities"), diagnostics, typedOptions),
            CampaignStartSettingsComposer.Compose(plan, compositionOptions));
    }

    public CampaignStartValidation ValidateInternalRelationships(IDiagnosticSink? diagnostics = null)
    {
        diagnostics ??= NullDiagnosticSink.Instance;
        var issues = new List<CampaignStartValidationIssue>();
        foreach (var facility in Facilities.Rules)
        {
            ValidateReference(facility, facility.Value.DestroyedFacility, "destroyedFacility", sameSize: true);
            foreach (var id in facility.Value.BuildOverFacilities)
                ValidateReference(facility, id, "buildOverFacilities", sameSize: false);
            ValidateLeaves(facility);
            if (facility.Value.MapName.Length == 0)
                Report(facility, "mapName", "Battlescape map name is missing.");
            ValidateStorage(facility);
        }
        if (Settings.DestroyedFacility.Length != 0 && !Facilities.TryGet(Settings.DestroyedFacility, out _))
        {
            var issue = new CampaignStartValidationIssue(
                "global", "destroyedFacility", Settings.DestroyedFacility, "Referenced facility does not exist.");
            issues.Add(issue);
            diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.MissingRuleReference,
                DiagnosticSeverity.Error,
                $"Global destroyedFacility references missing facility '{Settings.DestroyedFacility}'.",
                context: new DiagnosticContext(RuleType: "global", RelatedId: Settings.DestroyedFacility)));
        }
        ValidateBaseFunctionLimit();
        return new CampaignStartValidation(Array.AsReadOnly(issues.ToArray()));

        void ValidateReference(TypedRule<BaseFacilityRule> owner, string id, string property, bool sameSize)
        {
            if (id.Length == 0) return;
            if (!Facilities.TryGet(id, out var target))
            {
                ReportMissing(owner, property, id);
                return;
            }
            if (sameSize && (owner.Value.SizeX != target!.Value.SizeX || owner.Value.SizeY != target.Value.SizeY))
                Report(owner, property, "Destroyed version must have the same dimensions.", id);
        }

        void ValidateLeaves(TypedRule<BaseFacilityRule> owner)
        {
            var names = owner.Value.LeavesBehindOnSell;
            if (names.Count == 0) return;
            foreach (var id in names)
                if (!Facilities.TryGet(id, out _)) ReportMissing(owner, "leavesBehindOnSell", id);
            if (!Facilities.TryGet(names[0], out var first)) return;
            if (first!.Value.SizeX == owner.Value.SizeX && first.Value.SizeY == owner.Value.SizeY)
            {
                if (names.Count != 1)
                    Report(owner, "leavesBehindOnSell", "Only one same-size replacement facility is allowed.");
            }
            else
            {
                foreach (var id in names)
                    if (Facilities.TryGet(id, out var replacement) &&
                        (replacement!.Value.SizeX != 1 || replacement.Value.SizeY != 1))
                        Report(owner, "leavesBehindOnSell", "Different-size replacements must all be size 1.", id);
            }
        }

        void ValidateStorage(TypedRule<BaseFacilityRule> owner)
        {
            var tiles = owner.Value.StorageTiles;
            if (tiles.Count == 1 && tiles[0] == new FacilityPosition(-1, -1, -1)) return;
            foreach (var tile in tiles)
            {
                if (tile.X < 0 || tile.X > 10 * owner.Value.SizeX || tile.Y < 0 || tile.Y > 10 * owner.Value.SizeY ||
                    tile.Z < 0 || tile.Z > 8)
                    Report(owner, "storageTiles", $"Storage tile ({tile.X}, {tile.Y}, {tile.Z}) is outside the facility area.");
            }
        }

        void ReportMissing(TypedRule<BaseFacilityRule> owner, string property, string relatedId)
        {
            var issue = new CampaignStartValidationIssue(owner.Id, property, relatedId, "Referenced facility does not exist.");
            issues.Add(issue);
            diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.MissingRuleReference,
                DiagnosticSeverity.Error,
                $"Facility '{owner.Id}' property '{property}' references missing facility '{relatedId}'.",
                owner.LastUpdateSource.Span,
                new DiagnosticContext(owner.LastUpdateSource.LayerId, owner.LastUpdateSource.ModId,
                    "facilities", owner.Id, relatedId)));
        }

        void Report(TypedRule<BaseFacilityRule> owner, string property, string message, string relatedId = "")
        {
            issues.Add(new CampaignStartValidationIssue(owner.Id, property, relatedId, message));
            diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.InvalidRuleRelationship,
                DiagnosticSeverity.Error,
                $"Facility '{owner.Id}' property '{property}' is invalid: {message}",
                owner.LastUpdateSource.Span,
                new DiagnosticContext(owner.LastUpdateSource.LayerId, owner.LastUpdateSource.ModId,
                    "facilities", owner.Id, relatedId.Length == 0 ? null : relatedId)));
        }

        void ValidateBaseFunctionLimit()
        {
            var names = Countries.Rules.SelectMany(rule =>
                    rule.Value.ProvidedBaseFunctions.Concat(rule.Value.ForbiddenBaseFunctions))
                .Concat(GlobeLabels.Rules.SelectMany(rule =>
                    rule.Value.ProvidedBaseFunctions.Concat(rule.Value.ForbiddenBaseFunctions)))
                .Concat(Regions.Rules.SelectMany(rule =>
                    rule.Value.ProvidedBaseFunctions.Concat(rule.Value.ForbiddenBaseFunctions)))
                .Concat(Facilities.Rules.SelectMany(rule => rule.Value.RequiredBaseFunctions
                    .Concat(rule.Value.ProvidedBaseFunctions).Concat(rule.Value.ForbiddenBaseFunctions)))
                .Concat(Settings.HireScientistsRequiredBaseFunctions)
                .Concat(Settings.HireEngineersRequiredBaseFunctions)
                .Distinct(StringComparer.Ordinal)
                .Take(129)
                .Count();
            if (names <= 128) return;
            issues.Add(new CampaignStartValidationIssue(
                "global", "baseFunctions", string.Empty, "More than 128 distinct base functions are declared."));
            diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.InvalidRuleRelationship,
                DiagnosticSeverity.Error,
                "Campaign rules declare more than the reference limit of 128 distinct base functions.",
                context: new DiagnosticContext(RuleType: "global")));
        }
    }

    private static readonly string[] Names = ["countries", "extraGlobeLabels", "regions", "facilities"];
    private static RuleSectionDefinition RequiredDefinition(string name) =>
        RuleSectionRegistry.TryGetNamed(name, out var section) ? section! : throw new InvalidOperationException();
    private static UnresolvedRuleSection Required(UnresolvedRuleCatalog catalog, string name) =>
        catalog.TryGetSection(name, out var section) ? section! : throw new InvalidOperationException();
}

public sealed record CampaignStartValidationIssue(string RuleId, string Property, string RelatedId, string Message);
public sealed record CampaignStartValidation(IReadOnlyList<CampaignStartValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
