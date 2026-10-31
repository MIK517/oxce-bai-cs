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

    /// <summary>Marks the request's world target (a UFO, mission site or alien base) as in the battlescape.</summary>
    void EnterBattlescape(WorldTargetReference target);

    /// <summary>The rule of the campaign's UFO in the battlescape, or "".</summary>
    string UfoInBattlescape { get; }
}

/// <summary>
/// The campaign's battle (<c>SavedGame::_battleGame</c>): at most one, generated from the pending deployment
/// request. While it exists the campaign refuses every strategic command and time.
/// </summary>
internal sealed class CampaignBattles(CampaignState campaign) : ICampaignCapability, IBattleQuery
{
    internal static readonly CampaignReason BattleInProgress = new("battle.in-progress",
        "A battle is in progress; the campaign continues after its debriefing.");

    /// <summary>
    /// The named restriction on saving a battle before its first turn (Phase 7 branch 2 S5). The reference saves only
    /// after the briefing and <c>startFirstTurn</c>, and loads a save straight into the battlescape, so a turn-0 save would
    /// stay before its first turn there; Phase 7 branch 3 retires it with the briefing and the first turn.
    /// </summary>
    internal static readonly CampaignReason BattleSaveBeforeFirstTurn = new("battle.save-before-first-turn",
        "Saving a battle before its first turn requires the briefing and the first turn (Phase 7 branch 3).");

    /// <summary>A save holding a battle restores the battle's map, which loads the installation's terrain files.</summary>
    internal const string RestoreNeedsTerrainFiles =
        "The save holds a battle, whose map needs the installation's terrain files, which the host has not attached.";

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
        registry.SaveGate(() => _battle is { Turn: < 1 } ? BattleSaveBeforeFirstTurn.Text : null);
        registry.Capture(snapshot => _battle is null ? snapshot : snapshot with { Battle = _battle.Capture() });
        registry.Restore(Restore);
    }

    /// <summary>
    /// SavedGame::load's <c>battleGame</c>: the battle restored with the campaign's soldiers and difficulty, its map's data
    /// sets loaded from the attached terrain files, and the deployment derived with the campaign's UFO in the battlescape.
    /// </summary>
    private void Restore(CampaignSnapshot snapshot)
    {
        _battle = null;
        if (snapshot.Battle is not { } saved) return;
        var files = campaign.BattleTerrainFiles ?? throw new InvalidDataException(RestoreNeedsTerrainFiles);
        var ufo = campaign.Handlers.Operation<ICampaignDeploymentHandOver>().UfoInBattlescape;
        _battle = BattleState.Restore(saved, campaign.Content, Loader(files), ufo, BattleRestoreCampaign.From(campaign));
    }

    private BattleTerrainLoader Loader(IBattleTerrainFiles files)
    {
        if (!ReferenceEquals(files, _loaderFiles))
        {
            _loader = new BattleTerrainLoader(campaign.Content.RuntimeRules.Battle, files);
            _loaderFiles = files;
        }
        return _loader!;
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
        var loader = Loader(files);
        // A failed generation leaves nothing behind: its draws are put back, so the refusal changes nothing.
        var random = campaign.Random.State;
        BattleState battle;
        try
        {
            battle = BattleGenerator.Generate(campaign, request, loader);
        }
        catch (Generation.MapGenerationException failure)
        {
            campaign.Random.Restore(random);
            return Blocked(new CampaignReason(GenerationFailedCode, failure.Message));
        }
        battle.Validate();
        // deployXCOM sends disabled vehicles and items back to the base's storage; a base defense takes its stored
        // vehicles and the stores its defenders equip out of it.
        var owner = campaign.BaseById(request.BaseId)!;
        ChangeStorage(owner, battle.ReturnedToBase, 1);
        ChangeStorage(owner, battle.TakenFromBase, -1);
        ChangeArmor(owner, battle.SoldierArmors);
        EnterBattlescape(owner, request, handOver);
        handOver.ConsumeDeploymentRequest();
        _battle = battle;
        return new CampaignCommandResult([new BattleStarted(battle.MissionType, battle.DeploymentId)]);
    }

    /// <summary>
    /// deployXCOM's armor pass on the geoscape soldiers: each soldier wears its battle armor, its own remembered as
    /// <c>transformedArmor</c> (the environment's transformation) or <c>replacedArmor</c> (the starting condition's).
    /// </summary>
    private static void ChangeArmor(CampaignState.BaseState owner, IReadOnlyDictionary<int, BattleSoldierArmor> armors)
    {
        for (var index = 0; index < owner.Soldiers.Count; index++)
        {
            var soldier = owner.Soldiers[index];
            if (!armors.TryGetValue(soldier.Id, out var change) || soldier.Personal is not { } personal) continue;
            owner.Soldiers[index] = soldier with
            {
                Personal = change.Transformed
                    ? personal with { Armor = change.Armor, TransformedArmor = personal.Armor }
                    : personal with { Armor = change.Armor, ReplacedArmor = personal.Armor },
            };
        }
    }

    /// <summary>
    /// The <c>inBattlescape</c> flags BattlescapeGenerator's setters set: the craft (setCraft) and its landing's target
    /// (setUfo, setMissionSite, setAlienBase), or the defended base (setBase) for a base defense.
    /// </summary>
    private void EnterBattlescape(CampaignState.BaseState owner, CampaignDeploymentRequest request, ICampaignDeploymentHandOver handOver)
    {
        if (request.CraftTypeId.Length == 0)
        {
            owner.InBattlescape = true;
            return;
        }
        var index = campaign.CraftIndex(owner, request.CraftId, request.CraftTypeId);
        if (index >= 0 && owner.Crafts[index] is { Logistics: { } logistics } craft)
            // Craft::setInBattlescape(true) stops the craft (setSpeed(0): no speed, no speed vector).
            owner.Crafts[index] = craft with
            {
                Logistics = logistics with { InBattlescape = true, Speed = 0, SpeedRadian = 0, SpeedLongitude = 0, SpeedLatitude = 0 },
            };
        handOver.EnterBattlescape(request.Target);
    }

    /// <summary>Adds (sign 1) or removes (sign -1) the quantities from the base's storage; an emptied entry goes.</summary>
    private void ChangeStorage(CampaignState.BaseState owner, IReadOnlyDictionary<string, int> quantities, int sign)
    {
        var items = campaign.Content.RuntimeRules.Items;
        foreach (var (type, quantity) in quantities)
        {
            var handle = items.GetRequired(type);
            var left = checked(owner.Items.GetValueOrDefault(handle) + sign * quantity);
            if (left > 0) owner.Items[handle] = left;
            else owner.Items.Remove(handle);
        }
    }

    private static CampaignCommandResult Blocked(CampaignReason reason) =>
        new([CampaignActionBlocked.From(reason)]);
}
