using Oxce.Core.Diagnostics;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.MissionEvents;
using Oxce.Mods.Rulesets.PersonnelTactical;
using Oxce.Mods.Rulesets.Presentation;
using Oxce.Mods.Rulesets.TerrainDeployment;

namespace Oxce.Mods.Rulesets.Phase3;

public sealed class Phase3ContentCatalog
{
    private Phase3ContentCatalog(
        PresentationRuleCatalog presentation,
        CampaignStartRuleCatalog campaignStart,
        ItemRuleCatalog items,
        EquipmentProductionRuleCatalog equipmentProduction,
        PersonnelTacticalRuleCatalog personnelTactical,
        TerrainDeploymentRuleCatalog terrainDeployment,
        MissionEventRuleCatalog missionEvents,
        Phase3ContentValidation validation,
        ContentLoadCapabilities capabilities)
    {
        Presentation = presentation;
        CampaignStart = campaignStart;
        Items = items;
        EquipmentProduction = equipmentProduction;
        PersonnelTactical = personnelTactical;
        TerrainDeployment = terrainDeployment;
        MissionEvents = missionEvents;
        Validation = validation;
        Capabilities = capabilities;
    }

    public PresentationRuleCatalog Presentation { get; }
    public CampaignStartRuleCatalog CampaignStart { get; }
    public ItemRuleCatalog Items { get; }
    public EquipmentProductionRuleCatalog EquipmentProduction { get; }
    public PersonnelTacticalRuleCatalog PersonnelTactical { get; }
    public TerrainDeploymentRuleCatalog TerrainDeployment { get; }
    public MissionEventRuleCatalog MissionEvents { get; }
    public Phase3ContentValidation Validation { get; }
    public ContentLoadCapabilities Capabilities { get; }

    public static Phase3ContentCatalog Load(
        ModLoadPlan plan,
        IDiagnosticSink? diagnostics = null,
        RulesetCompositionOptions? compositionOptions = null,
        TypedRuleLoadOptions? typedOptions = null) =>
        Build(plan, diagnostics, compositionOptions, typedOptions).Catalog;

    public static Phase3ContentBuild Build(
        ModLoadPlan plan,
        IDiagnosticSink? diagnostics = null,
        RulesetCompositionOptions? compositionOptions = null,
        TypedRuleLoadOptions? typedOptions = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        diagnostics ??= NullDiagnosticSink.Instance;
        compositionOptions ??= new RulesetCompositionOptions();
        compositionOptions.Validate();
        var collector = new DiagnosticCollector();
        var sink = new ForwardingDiagnosticSink(diagnostics, collector);

        var documents = RulesetDocumentCatalog.Parse(plan, compositionOptions);
        var composed = RulesetComposer.Compose(documents, sink, compositionOptions);
        var presentation = PresentationRuleCatalog.Load(composed, documents, sink, compositionOptions, typedOptions);
        var campaign = CampaignStartRuleCatalog.Load(composed, documents, sink, compositionOptions, typedOptions);
        var items = ItemRuleCatalog.Load(composed, sink, typedOptions);
        var equipment = EquipmentProductionRuleCatalog.Load(composed, sink, typedOptions);
        var personnel = PersonnelTacticalRuleCatalog.Load(composed, sink, typedOptions);
        var terrain = TerrainDeploymentRuleCatalog.Load(composed, documents, sink, compositionOptions, typedOptions);
        var missions = MissionEventRuleCatalog.Load(composed, documents, sink, compositionOptions, typedOptions);

        var campaignValidation = campaign.ValidateInternalRelationships(sink);
        var itemValidation = items.ValidateInternalRelationships(sink);
        var equipmentValidation = equipment.ValidateRelationships(items, sink);
        var personnelValidation = personnel.ValidateRelationships(items, equipment, sink);
        var terrainValidation = terrain.ValidateRelationships(items, equipment, personnel, sink);
        var missionValidation = missions.ValidateRelationships(
            campaign, items, equipment, personnel, terrain, sink);
        var closureValidation = Phase3ContentClosureValidator.Validate(
            new Phase3ContentCatalogView(campaign, items, equipment, personnel, terrain, missions), sink);
        var validation = new Phase3ContentValidation(
            campaignValidation,
            itemValidation,
            equipmentValidation,
            personnelValidation,
            terrainValidation,
            missionValidation,
            closureValidation);
        var hasError = collector.HasSeverityAtLeast(DiagnosticSeverity.Error);
        var capabilities = ContentLoadCapabilities.Composed.AdvanceTo(ContentLoadStage.Typed);
        if (validation.IsValid && !hasError)
        {
            capabilities = capabilities.AdvanceTo(ContentLoadStage.Linked);
        }

        var catalog = new Phase3ContentCatalog(
            presentation, campaign, items, equipment, personnel, terrain, missions, validation, capabilities);
        return new Phase3ContentBuild(catalog, composed, documents, documents.ParsedFileCount);
    }

    private sealed class ForwardingDiagnosticSink(IDiagnosticSink destination, IDiagnosticSink collector)
        : IDiagnosticSink
    {
        public void Report(DiagnosticEvent diagnostic)
        {
            destination.Report(diagnostic);
            collector.Report(diagnostic);
        }
    }
}

public sealed record Phase3ContentBuild(
    Phase3ContentCatalog Catalog,
    UnresolvedRuleCatalog ComposedRules,
    RulesetDocumentCatalog Documents,
    int ParsedFileCount);

public sealed record Phase3ContentValidation(
    CampaignStartValidation CampaignStart,
    ItemRuleValidation Items,
    EquipmentProductionValidation EquipmentProduction,
    PersonnelTacticalValidation PersonnelTactical,
    TerrainDeploymentValidation TerrainDeployment,
    MissionEventValidation MissionEvents,
    Phase3ContentClosureValidation Closure)
{
    public bool IsValid => CampaignStart.IsValid && Items.IsValid && EquipmentProduction.IsValid &&
        PersonnelTactical.IsValid && TerrainDeployment.IsValid && MissionEvents.IsValid && Closure.IsValid;

    public int IssueCount => CampaignStart.Issues.Count + Items.Issues.Count +
        EquipmentProduction.Issues.Count + PersonnelTactical.Issues.Count +
        TerrainDeployment.Issues.Count + MissionEvents.Issues.Count + Closure.Issues.Count;
}
