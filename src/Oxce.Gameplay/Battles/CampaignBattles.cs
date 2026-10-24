using Oxce.Gameplay.Battles.Terrain;
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

    internal static readonly CampaignReason NoTerrainFiles = new("battle.no-terrain-files",
        "Battle generation needs the installation's terrain files, which the host has not attached.");

    /// <summary>Code of the reasons generation stops with (the reference throws or crashes there).</summary>
    internal const string GenerationFailedCode = "battle.generation-failed";

    private BattleState? _battle;
    private BattleTerrainLoader? _loader;
    private IBattleTerrainFiles? _loaderFiles;

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
        if (campaign.BattleTerrainFiles is not { } files) return Blocked(NoTerrainFiles);
        if (!ReferenceEquals(files, _loaderFiles))
        {
            _loader = new BattleTerrainLoader(campaign.Content.RuntimeRules.Battle, files);
            _loaderFiles = files;
        }
        // A failed generation leaves nothing behind: its draws are put back, so the refusal changes nothing.
        var random = campaign.Random.State;
        BattleState battle;
        try
        {
            battle = BattleGenerator.Generate(campaign, request, _loader!);
        }
        catch (Generation.MapGenerationException failure)
        {
            campaign.Random.Restore(random);
            return Blocked(new CampaignReason(GenerationFailedCode, failure.Message));
        }
        battle.Validate();
        // deployXCOM sends disabled vehicles and items back to the base's storage.
        if (battle.ReturnedToBase.Count != 0)
        {
            var owner = campaign.BaseStates.Find(candidate => candidate.Id == request.BaseId)!;
            var items = campaign.Content.RuntimeRules.Items;
            foreach (var (type, quantity) in battle.ReturnedToBase)
            {
                var handle = items.GetRequired(type);
                owner.Items[handle] = checked(owner.Items.GetValueOrDefault(handle) + quantity);
            }
        }
        // A base defense takes its stored vehicles and the stores its defenders equip out of the base.
        if (battle.TakenFromBase.Count != 0)
        {
            var owner = campaign.BaseStates.Find(candidate => candidate.Id == request.BaseId)!;
            var items = campaign.Content.RuntimeRules.Items;
            foreach (var (type, quantity) in battle.TakenFromBase)
            {
                var handle = items.GetRequired(type);
                var left = owner.Items.GetValueOrDefault(handle) - quantity;
                if (left > 0) owner.Items[handle] = left;
                else owner.Items.Remove(handle);
            }
        }
        handOver.ConsumeDeploymentRequest();
        _battle = battle;
        return new CampaignCommandResult([new BattleStarted(battle.MissionType, battle.DeploymentId)]);
    }

    private static CampaignCommandResult Blocked(CampaignReason reason) =>
        new([CampaignActionBlocked.From(reason)]);
}
