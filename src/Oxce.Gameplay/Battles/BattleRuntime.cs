using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// Runs a rule on a copy of the battle with the campaign's stream put back afterwards and returns what it refused, or null: the host's trial copy
/// (ADR 0040), kept until S11 deletes it. The copy has the live battle's campaign link, so it reads what the live run reads.
/// </summary>
internal delegate CampaignReason? BattleTrial(BattleState battle, Func<BattleState, CampaignReason?> run);

/// <summary>
/// The battle's runtime (ADR 0038): the state, the rules, the random source, the campaign link, the hooks and the event sink a battle command needs,
/// set once when the battle is generated or restored, and the battle's own registry of commands. <see cref="CampaignBattles"/> forwards a battle
/// command to <see cref="Execute"/> once; no entry point assigns the random source or the link again. An exception in a command is a defect: it
/// faults the battle, which refuses every later command, and the host offers the last save. A command the registry does not know is a refusal.
/// </summary>
internal sealed class BattleRuntime
{
    internal static readonly CampaignReason Faulted = new("battle.faulted",
        "The battle stopped after an internal error and refuses further commands; load the last save.");

    internal static readonly CampaignReason UnknownCommand = new("battle.command-unknown",
        "The battle does not know this command.");

    private readonly Dictionary<Type, Func<IBattleCommand, CampaignCommandResult>> _commands = [];
    private readonly BattleTrial _trial;

    public BattleRuntime(BattleState state, RuntimeRuleCatalog rules, IRandomSource random, IBattleCampaignLink? link, BattleTrial trial,
        IBattleHooks? hooks = null)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        Random = random ?? throw new ArgumentNullException(nameof(random));
        Link = link;
        _trial = trial ?? throw new ArgumentNullException(nameof(trial));
        // Set once, here: a rule that draws or reads the campaign finds them on the state.
        state.Random = random;
        state.CampaignLink = link;
        state.Hooks = hooks ?? DefaultBattleHooks.Instance;
        RegisterCommands();
    }

    public BattleState State { get; }

    public RuntimeRuleCatalog Rules { get; }

    /// <summary>The campaign's stream, which every rule of the battle draws from.</summary>
    public IRandomSource Random { get; }

    public IBattleCampaignLink? Link { get; }

    /// <summary>The events the rules raise, in order; a command returns what it held and empties it.</summary>
    public BattleEventSink Events => State.Events;

    public long Revision => State.Revision;

    /// <summary>What stopped the battle (the exception's description), or null while it runs.</summary>
    public string? Fault { get; private set; }

    /// <summary>
    /// Gives a trial copy of the battle what the live battle has (the campaign's stream, a link that reads what the live link reads but changes
    /// nothing, and the hooks), so both runs see the same values.
    /// </summary>
    internal void Adopt(BattleState copy)
    {
        copy.Random = Random;
        copy.CampaignLink = Link is null ? null : new TrialLink(Link);
        copy.Hooks = State.Hooks;
    }

    /// <summary>The live link for a copy: the campaign's values, with the memorial left alone (a death in a trial must not reach the campaign).</summary>
    private sealed class TrialLink(IBattleCampaignLink live) : IBattleCampaignLink
    {
        public int MissionCount => live.MissionCount;

        public int Difficulty => live.Difficulty;

        public BattleSoldierDescription? DescribeSoldier(int soldierId) => live.DescribeSoldier(soldierId);

        public void KillSoldier(int soldierId, BattleUnitKillSnapshot cause)
        {
        }
    }

    /// <summary>Registers the handler of one command type (exact type); a second handler is a defect.</summary>
    internal void Register<TCommand>(Func<TCommand, CampaignCommandResult> handler)
        where TCommand : class, IBattleCommand
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!_commands.TryAdd(typeof(TCommand), command => handler((TCommand)command)))
            throw new InvalidOperationException($"Battle command '{typeof(TCommand).Name}' has more than one handler.");
    }

    /// <summary>
    /// Runs a battle command: a faulted battle and an unknown command are refused before anything changes; an exception faults the battle.
    /// The sink starts empty, and what the command raised is what it returns (a refusal returns its reason alone).
    /// </summary>
    public CampaignCommandResult Execute(IBattleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Fault is not null) return Blocked(Faulted);
        if (!_commands.TryGetValue(command.GetType(), out var handler)) return Blocked(UnknownCommand);
        return Guard(() => handler(command));
    }

    /// <summary>
    /// Runs one step of the battle with the fault path: a faulted battle refuses it before anything changes, and an exception faults the battle.
    /// The campaign-side commands of the pre-battle flow (the briefing, its confirmation and the first turn) run through here too.
    /// </summary>
    internal CampaignCommandResult Guard(Func<CampaignCommandResult> run, bool returnRaised = true)
    {
        if (Fault is not null) return Blocked(Faulted);
        Events.Clear();
        try
        {
            var result = run();
            // The pre-battle flow's result is its own event alone (its hidden casualty check and falls are not shown to the client).
            if (!returnRaised)
            {
                Events.Clear();
                return result;
            }
            // A command's own result carries its own events; what the rules raised along the way follows in order. A refusal returns its reason alone.
            if (Events.Count == 0) return result;
            if (result.Events.Any(static raised => raised is CampaignActionBlocked))
            {
                Events.Clear();
                return result;
            }
            return new CampaignCommandResult([.. result.Events, .. Events.Drain()]);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Fault = $"{failure.GetType().Name}: {failure.Message}";
            Events.Clear();
            return Blocked(Faulted);
        }
    }

    internal static CampaignCommandResult Blocked(CampaignReason reason) => new([CampaignActionBlocked.From(reason)]);

    private void RegisterCommands()
    {
        Register<MoveInventoryItem>(MoveInventory);
        Register<AutoPlaceInventoryItem>(AutoPlaceInventory);
        Register<SelectUnit>(SelectUnit);
        Register<SelectNextUnit>(SelectNextUnit);
        Register<SkipUnit>(SkipUnit);
        Register<ReselectSkippedUnit>(ReselectSkippedUnit);
        Register<SelectSpotter>(SelectSpotter);
        Register<TurnUnit>(TurnUnit);
        Register<KneelUnit>(KneelUnit);
        Register<WalkUnit>(command => RunMovement(battle => battle.Walk(Rules, command.UnitId, command.Target, command.Move, command.IgnoreSpottedEnemies)));
        Register<MoveUnitUpDown>(command => RunMovement(battle => battle.MoveUpDown(Rules, command.UnitId, command.Up)));
        Register<UseInventoryFeature>(command =>
            Blocked(State.Phase == BattlePhase.Briefing ? BattleInventoryReasons.Unavailable : BattleInventoryReasons.For(command.Feature)));
    }

    /// <summary>The pick-up and drop of <c>Inventory::mouseClick</c>; a refusal changes nothing.</summary>
    private CampaignCommandResult MoveInventory(MoveInventoryItem command)
    {
        if (State.PlanMove(Rules, command.UnitId, command.ItemId, command.Slot ?? "", command.SlotX, command.SlotY, out var move) is { } refusal)
            return Blocked(refusal);
        return Moved(move!);
    }

    private CampaignCommandResult AutoPlaceInventory(AutoPlaceInventoryItem command)
    {
        if (State.PlanAutoPlace(Rules, command.UnitId, command.ItemId, command.Smart, out var move) is { } refusal)
            return Blocked(refusal);
        // Smart choice with no section that takes a ground item: the reference "moves" it to the ground it lies on, at no cost.
        if (move is null)
            return new CampaignCommandResult([new InventoryItemMoved(command.UnitId, command.ItemId, BattleState.GroundSlot, BattleState.GroundSlot, 0, 0, 0)]);
        return Moved(move);
    }

    private CampaignCommandResult Moved(InventoryMove move)
    {
        // InventoryState's destructor in a running battle starts with gravity on the selected unit's tile (a unit without a floor falls).
        var selected = State.Phase == BattlePhase.InProgress && State.SelectedUnitId is { } chosen ? State.UnitById(chosen) : null;
        if (selected?.Position is not null && State.FallsRefusal(Rules, selected) is { } fall) return Blocked(fall);
        State.ApplyMove(move);
        // The port has no close command, so each move closes the inventory (the fall it starts was checked above).
        if (State.Phase == BattlePhase.InProgress)
        {
            State.CloseInventory(Rules);
            State.ResolveFalls(Rules);
        }
        return new CampaignCommandResult([new InventoryItemMoved(move.Unit.Id, move.Item.Id, move.From.Id, move.To.Id, move.X, move.Y, move.Cost)]);
    }

    private CampaignCommandResult SelectUnit(SelectUnit command)
    {
        if (State.Select(command.UnitId, out var saved) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitSelected(command.UnitId, saved)]);
    }

    private CampaignCommandResult SelectNextUnit(SelectNextUnit command)
    {
        if (State.SelectAdjacent(Rules, command.UnitId, command.Backwards, command.InventoryOnly, out var result, out var saved) is { } refusal)
            return Blocked(refusal);
        // Nothing to select leaves the selection empty, as in the reference; the event names the unit, or -1.
        return new CampaignCommandResult([new UnitSelected(result?.Id ?? -1, saved)]);
    }

    private CampaignCommandResult SkipUnit(SkipUnit command)
    {
        if (State.Skip(Rules, command.UnitId, command.ByDistance, out var result, out var saved) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitSkipped(command.UnitId, result?.Id ?? -1, saved)]);
    }

    private CampaignCommandResult TurnUnit(TurnUnit command)
    {
        if (State.TurnTowards(Rules, command.UnitId, command.Target, out var turn) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitTurned(command.UnitId, turn.Direction, turn.Eighths, turn.TimeUnitsSpent, turn.Stop, turn.Door, turn.DoorsOpened, turn.DoorCentre)]);
    }

    private CampaignCommandResult KneelUnit(KneelUnit command)
    {
        if (State.KneelUnit(Rules, command.UnitId, out var kneeling, out var spent) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitKneeled(command.UnitId, kneeling, spent)]);
    }

    private CampaignCommandResult SelectSpotter(SelectSpotter command)
    {
        if (State.SelectSpotter(Rules, command.UnitId, command.TargetUnitId, out var result, out var saved) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitSelected(result!.Id, saved)]);
    }

    private CampaignCommandResult ReselectSkippedUnit(ReselectSkippedUnit command)
    {
        if (State.ReselectSkipped(Rules, command.UnitId) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitSelected(command.UnitId, true)]);
    }

    /// <summary>
    /// A movement that runs <c>UnitWalkBState</c>: first on a copy of the battle, so that what it meets that is not ported (the reaction fire after
    /// a step, a proximity explosion, a burning floor, a fall that knocks a unit out) is refused before the real one changes anything; then on the
    /// battle, with the events the rules raised.
    /// </summary>
    private CampaignCommandResult RunMovement(Func<BattleState, CampaignReason?> move)
    {
        if (_trial(State, move) is { } trial) return Blocked(trial);
        Events.Clear();
        if (move(State) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult(Events.Drain());
    }
}
