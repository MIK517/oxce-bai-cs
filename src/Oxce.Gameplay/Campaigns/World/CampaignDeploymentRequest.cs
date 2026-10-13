namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// The battle a confirmed landing asks for (D4): what <c>ConfirmLandingState::btnYesClick</c> hands to
/// <c>BattlescapeGenerator</c>. The mission type and the deployments are rule ids (empty when the mod
/// has none); textures are globe texture ids (-1 for none); the shade is the battlescape shade (0-15).
/// The craft keeps its pre-battle world state; its crew, vehicles and items are recorded as they were
/// when the landing was confirmed. Saved as the port-only <c>oxcePortDeploymentRequest</c> node.
/// </summary>
public sealed record CampaignDeploymentRequest(
    string MissionType,
    string DeploymentId,
    string AlienCustomDeploymentId,
    string AlienCustomMissionId,
    string Race,
    int MissionTexture,
    int GlobeTexture,
    int Shade,
    WorldTargetReference Target,
    int BaseId,
    string CraftTypeId,
    int CraftId)
{
    /// <summary>Soldiers on board, in base order.</summary>
    public IReadOnlyList<int> Soldiers { get; init; } = [];

    /// <summary>Vehicles on board by rule id, in craft order.</summary>
    public IReadOnlyList<string> Vehicles { get; init; } = [];

    /// <summary>BattlescapeGenerator::setUfoDamagePercentage for a base defense (lessAliensDuringBaseDefense).</summary>
    public int UfoDamagePercentage { get; init; }

    /// <summary>Items on board after any required-item destruction.</summary>
    public IReadOnlyDictionary<string, int> Items { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
}

/// <summary>A confirmed landing recorded its deployment request; the campaign waits for the battle.</summary>
public sealed record DeploymentRequested(CampaignDeploymentRequest Request) : ICampaignEvent;

/// <summary>
/// The Phase 7 seam: takes the pending deployment request, exactly once. Until tactical execution
/// exists the campaign does not continue past it (the request cannot be withdrawn).
/// </summary>
public sealed record TakeDeploymentRequest : ICampaignCommand;

/// <summary>The request taken by <see cref="TakeDeploymentRequest"/>.</summary>
public sealed record DeploymentRequestTaken(CampaignDeploymentRequest Request) : ICampaignEvent;
