using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;

namespace Oxce.Gameplay.Battles;

/// <summary>The pending deployment request, handed over to the battle generator once (ADR 0038).</summary>
internal interface ICampaignDeploymentHandOver
{
    CampaignDeploymentRequest? PendingDeploymentRequest { get; }

    /// <summary>Clears the pending request after its battle was generated.</summary>
    void ConsumeDeploymentRequest();
}

/// <summary>
/// The campaign's battle (<c>SavedGame::_battleGame</c>): at most one, generated from the pending deployment
/// request. While it exists the campaign refuses every strategic command and time.
/// </summary>
internal sealed class CampaignBattles(CampaignState campaign) : ICampaignCapability, IBattleQuery
{
    internal static readonly CampaignReason BattleInProgress = new("battle.in-progress",
        "A battle is in progress; the campaign continues after its debriefing.");

    // Retired by Phase 7 branch 2 (battle saves).
    internal static readonly CampaignReason BattleSaveUnsupported = new("battle.save-unsupported",
        "Saving during a battle requires battle saves (Phase 7 branch 2).");

    internal static readonly CampaignReason NoBattleDeployment = new("battle.no-deployment",
        "The deployment request names no alien deployment to generate a battle from.");

    private BattleState? _battle;

    public void Register(CampaignCapabilityRegistry registry)
    {
        registry.Query<IBattleQuery>(this);
        registry.Command<StartBattle>(_ => Start());
        // The geoscape is frozen during a battle: time stops in the preflight, every other command here.
        registry.Preflight(CampaignPreflightOrder.Battle, "battle", (_, _) => _battle is not null ? BattleInProgress.Text : null);
        registry.CommandGate(command => _battle is not null && command is not AdvanceCampaignTime ? Blocked(BattleInProgress) : null);
        registry.SaveGate(() => _battle is not null ? BattleSaveUnsupported.Text : null);
    }

    internal BattleState? Battle => _battle;

    public BattleOverview? QueryBattle(BattleViewer viewer) => campaign.Read(() => _battle?.Overview(viewer));

    private CampaignCommandResult Start()
    {
        var handOver = campaign.Handlers.Operation<ICampaignDeploymentHandOver>();
        if (handOver.PendingDeploymentRequest is not { } request)
            return Blocked(WorldCommandReasons.For(WorldCommandReasons.NoDeploymentRequest));
        if (BattleGenerator.Refusal(campaign, request) is { } refusal) return Blocked(refusal);
        var battle = BattleGenerator.Generate(campaign, request);
        battle.Validate();
        handOver.ConsumeDeploymentRequest();
        _battle = battle;
        return new CampaignCommandResult([new BattleStarted(battle.MissionType, battle.DeploymentId)]);
    }

    private static CampaignCommandResult Blocked(CampaignReason reason) =>
        new([CampaignActionBlocked.From(reason)]);
}
