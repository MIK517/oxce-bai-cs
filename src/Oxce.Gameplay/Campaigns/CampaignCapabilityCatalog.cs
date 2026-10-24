namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    /// <summary>Composition root: the capabilities installed into every campaign.</summary>
    private ICampaignCapability[] CreateCapabilities() =>
    [
        new DelegatedCampaignCapability(RegisterCalendar),
        new DelegatedCampaignCapability(RegisterBaseConstruction),
        new DelegatedCampaignCapability(RegisterPersonnel),
        new DelegatedCampaignCapability(RegisterLogistics),
        new DelegatedCampaignCapability(RegisterCraftServicing),
        new DelegatedCampaignCapability(RegisterResearchProduction),
        new World.CampaignWorld(this),
        new DelegatedCampaignCapability(RegisterMonthlyCycle),
        new DelegatedCampaignCapability(RegisterEvents),
        new Battles.CampaignBattles(this),
        .. _testCapabilities?.Invoke(this) ?? [],
    ];

    // Read before the capabilities are created, and handed to shadow copies.
    private readonly Func<CampaignState, ICampaignCapability[]>? _testCapabilities = TestCapabilities.Value;

    /// <summary>Test-only: capabilities added to campaigns created on this async flow, shadows included.</summary>
    internal static readonly AsyncLocal<Func<CampaignState, ICampaignCapability[]>?> TestCapabilities = new();

    private CampaignHandlerTable BuildHandlers()
    {
        var registry = new CampaignCapabilityRegistry();
        foreach (var capability in CreateCapabilities()) capability.Register(registry);
        return registry.Build();
    }

    internal CampaignHandlerTable Handlers => _handlers;
}
