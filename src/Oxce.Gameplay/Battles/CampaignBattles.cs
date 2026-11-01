using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>The pending deployment request, handed over to the battle generator once (ADR 0038).</summary>
internal interface ICampaignDeploymentHandOver
{
    CampaignDeploymentRequest? PendingDeploymentRequest { get; }

    /// <summary>Clears the pending request after its battle was generated.</summary>
    void ConsumeDeploymentRequest();

    /// <summary>Marks the request's world target (a UFO, mission site or alien base) as in the battlescape.</summary>
    void EnterBattlescape(WorldTargetReference target);

    /// <summary>
    /// BriefingState's constructor for a base defense: the base is no longer a retaliation target, and a multi-UFO retaliation
    /// mission is marked as in progress.
    /// </summary>
    void BriefBaseDefense(int baseId);

    /// <summary>The rule of the campaign's UFO in the battlescape, or "".</summary>
    string UfoInBattlescape { get; }
}

/// <summary>
/// The campaign's battle (<c>SavedGame::_battleGame</c>): at most one, generated from the pending deployment
/// request. While it exists the campaign refuses every strategic command and time.
/// </summary>
internal sealed class CampaignBattles(CampaignState campaign) : ICampaignCapability, IBattleQuery, IBattleBriefingQuery, IBattleInventoryQuery, IBattleViewQuery
{
    internal static readonly CampaignReason BattleInProgress = new("battle.in-progress",
        "A battle is in progress; the campaign continues after its debriefing.");

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
        registry.Query<IBattleBriefingQuery>(this);
        registry.Query<IBattleInventoryQuery>(this);
        registry.Query<IBattleViewQuery>(this);
        registry.Command<StartBattle>(command => Start(command));
        registry.Command<BeginBriefing>(BeginBriefing);
        registry.Command<ConfirmBriefing>(_ => ConfirmBriefing());
        registry.Command<StartFirstTurn>(_ => StartFirstTurn());
        registry.Command<MoveInventoryItem>(MoveInventory);
        registry.Command<AutoPlaceInventoryItem>(AutoPlaceInventory);
        registry.Command<SelectUnit>(SelectUnit);
        registry.Command<SelectNextUnit>(SelectNextUnit);
        registry.Command<SkipUnit>(SkipUnit);
        registry.Command<ReselectSkippedUnit>(ReselectSkippedUnit);
        registry.Command<UseInventoryFeature>(command =>
            Blocked(_battle is null or { Phase: BattlePhase.Briefing } ? BattleInventoryReasons.Unavailable : BattleInventoryReasons.For(command.Feature)));
        // The geoscape is frozen during a battle: time stops in the preflight, every other command here.
        registry.Preflight(CampaignPreflightOrder.Battle, "battle", (_, _) => _battle is not null ? BattleInProgress.Text : null);
        registry.CommandGate(command =>
        {
            if (_battle is null) return null;
            if (command is not (AdvanceCampaignTime or IBattleCommand)) return Blocked(BattleInProgress);
            // The battlescape's constructor runs checkForCasualties on every battle it opens, a loaded one included; that is turn
            // processing's (branch 6), so a running battle with a unit the check would take out refuses its commands by name.
            return _battle is { Phase: BattlePhase.InProgress } running && running.HasUncheckedCasualty()
                ? Blocked(BattleFlowReasons.CasualtiesUnsupported)
                : null;
        });
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
        // A battle before its first turn is briefed again: the briefing's craft or base is the one the campaign flags in the battlescape.
        // A save keeps the lines the briefing set (not that it ran): lines present mean the briefing already named the mission, so
        // a client that opens the briefing after the load cannot name it twice or draw a second operation name.
        if (_battle.Turn < 1)
            _battle.BriefingOpened = _battle.TargetName.Length != 0 || _battle.CraftOrBaseName.Length != 0;
        if (_battle.Turn < 1)
            foreach (var owner in campaign.BaseStates)
            {
                if (owner.Crafts.Any(static craft => craft.Logistics is { InBattlescape: true }))
                {
                    _battle.BriefingFromCraft = true;
                    _battle.BriefingBaseId = owner.Id;
                    break;
                }
                if (owner.InBattlescape) _battle.BriefingBaseId = owner.Id;
            }
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

    private CampaignCommandResult Start(StartBattle command)
    {
        // The craft deployment preview skips the briefing's inventory and the new-turn scripts (isPreview); it is a battle mode of its own.
        if (command.Preview) return Blocked(BattleFlowReasons.PreviewUnsupported);
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
        battle.BriefingFromCraft = request.CraftTypeId.Length != 0;
        battle.BriefingBaseId = request.BaseId;
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

    /// <summary>
    /// BriefingState's constructor (the names the saved game keeps) and its page: the target and the craft or base lines the
    /// client composed with its language, a random operation name when the mod has them, and a base defense's retaliation flag.
    /// </summary>
    private CampaignCommandResult BeginBriefing(BeginBriefing command)
    {
        if (_battle is not { Phase: BattlePhase.Briefing } battle) return Blocked(BattleFlowReasons.NotInBriefing);
        if (battle.BriefingOpened) return Blocked(BattleFlowReasons.BriefingAlreadyOpen);
        battle.NameMission(campaign.Content.RuntimeRules, command.TargetName ?? "", command.CraftOrBaseName ?? "");
        if (!battle.BriefingFromCraft && battle.MissionType == BaseDefenseMissionType && battle.BriefingBaseId >= 0)
            campaign.Handlers.Operation<ICampaignDeploymentHandOver>().BriefBaseDefense(battle.BriefingBaseId);
        return new CampaignCommandResult([new BriefingOpened(battle.TargetName, battle.CraftOrBaseName)]);
    }

    private const string BaseDefenseMissionType = BattleSnapshot.BaseDefenseMission;

    /// <summary>
    /// BriefingState::btnOkClick and NextTurnState's turn-0 work. Everything that can refuse is checked first, so a refusal
    /// changes nothing: no live aliens (the aliens' crash screen), a wave that could deploy at the start (branch 6), a spawn
    /// near a tile without a floor (branch 4), a script or a revive the first turn cannot do yet. Then primed spawn items
    /// spawn, and bug hunt mode is checked.
    /// </summary>
    private CampaignCommandResult ConfirmBriefing()
    {
        if (_battle is not { Phase: BattlePhase.Briefing } battle) return Blocked(BattleFlowReasons.NotInBriefing);
        var content = campaign.Content;
        var rules = content.RuntimeRules;
        // The battlescape's constructor runs checkForCasualties before the spawn; it is turn processing's.
        if (battle.HasUncheckedCasualty()) return Blocked(BattleFlowReasons.CasualtiesUnsupported);
        // The reference spawns first and tallies after: aliens that only the primed items bring count. Without any alien now, a
        // battle is refused here when no spawn could add one, and otherwise once a trial spawn on a copy shows none arriving.
        var noAliensYet = battle.Tally().LiveAliens == 0;
        if (noAliensYet && !battle.PrimedSpawnsMayAddAliens(rules)) return Blocked(BattleFlowReasons.NoLiveAliens);
        if (ReinforcementsRefusal(battle, rules) is { } reinforcements) return Blocked(reinforcements);
        if (battle.SpawnNeedsGravity(rules)) return Blocked(BattleFlowReasons.SpawnNeedsGravity);
        if (battle.SpawnLacksItemLevels(rules)) return Blocked(BattleFlowReasons.SpawnContentInvalid);
        if (battle.FirstTurnRefusal(content, BattleRestoreCampaign.From(campaign)) is { } later) return Blocked(later);
        if (noAliensYet && SpawnLeavesNoAliens(battle, content)) return Blocked(BattleFlowReasons.NoLiveAliens);
        var (units, items) = battle.SpawnFromPrimedItems(content, (int)campaign.Difficulty, campaign.Random);
        battle.CheckBugHuntMode(rules);
        battle.EnterInventory(rules);
        return new CampaignCommandResult([new BriefingConfirmed(units, items)]);
    }

    /// <summary>
    /// Whether the primed items' spawns, run on a copy of the battle with the campaign's stream put back afterwards, leave the
    /// aliens without a live unit (a chance that fails, or no room for the unit): the refusal then changes nothing.
    /// </summary>
    private bool SpawnLeavesNoAliens(BattleState battle, RuntimeContent content)
    {
        if (campaign.BattleTerrainFiles is not { } files) return false;
        var scratch = BattleState.Restore(battle.Capture(), content, Loader(files),
            campaign.Handlers.Operation<ICampaignDeploymentHandOver>().UfoInBattlescape, BattleRestoreCampaign.From(campaign));
        var state = campaign.Random.State;
        try
        {
            scratch.SpawnFromPrimedItems(content, (int)campaign.Difficulty, campaign.Random);
            return scratch.Tally().LiveAliens == 0;
        }
        finally
        {
            campaign.Random.Restore(state);
        }
    }

    /// <summary>
    /// NextTurnState::determineReinforcements' pre-requisites at turn 0 (the player side's turn before the first): a wave of the
    /// battle's reinforcements deployment that passes its difficulty range and its trigger (the objectives, the turns or the
    /// turn range) could deploy, which turn processing does (branch 6); whether its odds then hold is left out, so that no
    /// draw is spent on a refusal.
    /// </summary>
    private CampaignReason? ReinforcementsRefusal(BattleState battle, RuntimeRuleCatalog rules)
    {
        if (battle.ReinforcementsDeployment.Length == 0 || !rules.AlienDeployments.TryGet(battle.ReinforcementsDeployment, out var handle)) return null;
        var difficulty = (int)campaign.Difficulty;
        foreach (var wave in rules.AlienDeployments[handle].Value.Battle.Reinforcements)
        {
            if (difficulty < wave.MinimumDifficulty || difficulty > wave.MaximumDifficulty) continue;
            if (wave.ObjectiveDestroyed)
            {
                if (battle.ObjectiveCount <= 0 || battle.ObjectivesDestroyed != battle.ObjectiveCount) continue;
            }
            else if (wave.Turns.Count != 0)
            {
                if (!wave.Turns.Contains(battle.Turn)) continue;
            }
            else if (battle.Turn < wave.MinimumTurn || wave.MaximumTurn != -1 && battle.Turn > wave.MaximumTurn)
            {
                continue;
            }
            // The wave's runs so far are 0 before the first turn. The reference draws its odds before it looks at the runs, so only a
            // wave that spends no draw (odds of 100 or more) is skipped here; otherwise the draw is branch 6's and the battle is refused.
            if (wave.MaximumRuns != -1 && wave.MaximumRuns <= 0 && wave.ExecutionOdds >= 100) continue;
            return BattleFlowReasons.ReinforcementsUnsupported;
        }
        return null;
    }

    /// <summary>SavedBattleGame::startFirstTurn when the pre-battle inventory closes.</summary>
    private CampaignCommandResult StartFirstTurn()
    {
        if (_battle is not { Phase: BattlePhase.Inventory } battle) return Blocked(BattleFlowReasons.NotBeforeFirstTurn);
        var content = campaign.Content;
        if (battle.Tally().LiveSoldiers == 0) return Blocked(BattleFlowReasons.NoLiveSoldiers);
        if (battle.FirstTurnRefusal(content, BattleRestoreCampaign.From(campaign)) is { } refusal) return Blocked(refusal);
        // btnOkClick saves the layout first, unless the alternate craft equipment management keeps it with the base screens.
        var saved = campaign.Options.AlternateCraftEquipmentManagement ? 0 : SaveEquipmentLayout(battle, content.RuntimeRules);
        battle.StartFirstTurn(content, campaign.Random);
        return new CampaignCommandResult([new BattleFirstTurnStarted(battle.SelectedUnitId ?? -1) { LayoutsSaved = saved }]);
    }

    /// <summary>BriefingState's page: which deployment's briefing data shows, and the lines the briefing set.</summary>
    public BattleBriefing? QueryBriefing() => campaign.Read(() =>
    {
        if (_battle is not { Phase: not BattlePhase.InProgress } battle) return null;
        var rules = campaign.Content.RuntimeRules;
        var deployments = rules.AlienDeployments;
        RuntimeAlienDeploymentRule? Find(string id) => id.Length != 0 && deployments.TryGet(id, out var handle) ? deployments[handle].Value : null;
        var mission = battle.MissionType;
        var deployment = Find(mission);
        if (mission == BaseDefenseMissionType)
        {
            if (Find(battle.AlienCustomDeploy) is { } custom && custom.Battle.Briefing.Description.Length != 0) deployment = custom;
        }
        else if (deployment is null && battle.BriefingFromCraft)
        {
            // The craft's destination is the UFO that is in the battlescape; a fake underwater UFO uses the custom mission's deployment.
            var ufo = campaign.Handlers.Operation<ICampaignDeploymentHandOver>().UfoInBattlescape;
            if (ufo.Length != 0) deployment = Find(battle.AlienCustomMission.Length != 0 ? battle.AlienCustomMission : ufo);
        }
        var data = deployment?.Battle.Briefing ?? RuntimeBriefing.Default;
        return new BattleBriefing(data.Title.Length != 0 ? data.Title : mission, data.Description.Length != 0 ? data.Description : mission + "_BRIEFING",
            data.Music, data.Background, data.Cutscene, data.Palette, data.TextOffset, data.ShowTarget, data.ShowCraft,
            rules.Campaign.OperationNamesFirst.Count != 0, battle.TargetName, battle.CraftOrBaseName, battle.Phase);
    });


    /// <summary>The pick-up and drop of <c>Inventory::mouseClick</c>; a refusal changes nothing.</summary>
    private CampaignCommandResult MoveInventory(MoveInventoryItem command)
    {
        if (_battle is not { } battle) return Blocked(BattleInventoryReasons.Unavailable);
        if (battle.PlanMove(campaign.Content.RuntimeRules, command.UnitId, command.ItemId, command.Slot ?? "", command.SlotX, command.SlotY, out var move) is { } refusal)
            return Blocked(refusal);
        return Moved(battle, move!);
    }

    private CampaignCommandResult AutoPlaceInventory(AutoPlaceInventoryItem command)
    {
        if (_battle is not { } battle) return Blocked(BattleInventoryReasons.Unavailable);
        if (battle.PlanAutoPlace(campaign.Content.RuntimeRules, command.UnitId, command.ItemId, command.Smart, out var move) is { } refusal)
            return Blocked(refusal);
        // Smart choice with no section that takes a ground item: the reference "moves" it to the ground it lies on, at no cost.
        if (move is null)
            return new CampaignCommandResult([new InventoryItemMoved(command.UnitId, command.ItemId, BattleState.GroundSlot, BattleState.GroundSlot, 0, 0, 0)]);
        return Moved(battle, move);
    }

    private static CampaignCommandResult Moved(BattleState battle, InventoryMove move)
    {
        battle.ApplyMove(move);
        return new CampaignCommandResult([new InventoryItemMoved(move.Unit.Id, move.Item.Id, move.From.Id, move.To.Id, move.X, move.Y, move.Cost)]);
    }

    /// <summary>
    /// InventoryState::saveEquipmentLayout: every soldier of the battle gets the layout of its unit's inventory, in the campaign.
    /// Returns the number of soldiers whose layout was written.
    /// </summary>
    private int SaveEquipmentLayout(BattleState battle, RuntimeRuleCatalog rules)
    {
        var layouts = battle.SaveEquipmentLayout(rules);
        var written = 0;
        foreach (var owner in campaign.BaseStates)
            for (var index = 0; index < owner.Soldiers.Count; index++)
            {
                var soldier = owner.Soldiers[index];
                if (!layouts.TryGetValue(soldier.Id, out var layout) || soldier.Personal is not { } personal) continue;
                owner.Soldiers[index] = soldier with { Personal = personal with { EquipmentLayout = layout } };
                written++;
            }
        return written;
    }

    private CampaignCommandResult SelectUnit(SelectUnit command)
    {
        if (_battle is not { } battle) return Blocked(BattleSelectionReasons.Unavailable);
        if (battle.Select(command.UnitId, out var saved) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitSelected(command.UnitId, saved)]);
    }

    private CampaignCommandResult SelectNextUnit(SelectNextUnit command)
    {
        if (_battle is not { } battle) return Blocked(BattleSelectionReasons.Unavailable);
        if (battle.SelectAdjacent(campaign.Content.RuntimeRules, command.UnitId, command.Backwards, command.InventoryOnly, out var result, out var saved) is { } refusal)
            return Blocked(refusal);
        // Nothing to select leaves the selection empty, as in the reference; the event names the unit, or -1.
        return new CampaignCommandResult([new UnitSelected(result?.Id ?? -1, saved)]);
    }

    private CampaignCommandResult SkipUnit(SkipUnit command)
    {
        if (_battle is not { } battle) return Blocked(BattleSelectionReasons.Unavailable);
        if (battle.Skip(campaign.Content.RuntimeRules, command.UnitId, command.ByDistance, out var result, out var saved) is { } refusal)
            return Blocked(refusal);
        return new CampaignCommandResult([new UnitSkipped(command.UnitId, result?.Id ?? -1, saved)]);
    }

    private CampaignCommandResult ReselectSkippedUnit(ReselectSkippedUnit command)
    {
        if (_battle is not { } battle) return Blocked(BattleSelectionReasons.Unavailable);
        if (battle.ReselectSkipped(campaign.Content.RuntimeRules, command.UnitId) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitSelected(command.UnitId, true)]);
    }

    public BattleSelectionView? QuerySelection(BattleViewer viewer) =>
        campaign.Read(() => _battle?.SelectionView(campaign.Content.RuntimeRules, viewer));

    public BattleLevelView? QueryLevel(BattleViewer viewer, int level) => campaign.Read(() => _battle?.LevelView(viewer, level));

    public BattleUnitInfo? QueryUnitInfo(BattleViewer viewer, int unitId) =>
        campaign.Read(() => _battle?.UnitInfo(campaign.Content.RuntimeRules, viewer, unitId));

    public BattleLegalActions? QueryLegalActions(BattleViewer viewer, int unitId) =>
        campaign.Read(() => _battle?.LegalActions(campaign.Content.RuntimeRules, viewer, unitId));

    public BattleInventoryView? QueryInventory(BattleViewer viewer, int unitId) =>
        campaign.Read(() => _battle?.InventoryView(campaign.Content.RuntimeRules, viewer, unitId));

    public string? CheckInventoryMove(BattleViewer viewer, int unitId, int itemId, string slot, int slotX, int slotY) => campaign.Read(() =>
    {
        if (_battle is not { } battle) return BattleInventoryReasons.Unavailable.Code;
        if (viewer.Faction != BattleFaction.Player) return BattleInventoryReasons.UnitUnavailable.Code;
        return battle.PlanMove(campaign.Content.RuntimeRules, unitId, itemId, slot ?? "", slotX, slotY, out _)?.Code;
    });

    private static CampaignCommandResult Blocked(CampaignReason reason) =>
        new([CampaignActionBlocked.From(reason)]);
}
