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
    ];

    private CampaignHandlerTable BuildHandlers()
    {
        var registry = new CampaignCapabilityRegistry();
        foreach (var capability in CreateCapabilities()) capability.Register(registry);
        return registry.Build();
    }

    internal CampaignHandlerTable Handlers => _handlers;
}
