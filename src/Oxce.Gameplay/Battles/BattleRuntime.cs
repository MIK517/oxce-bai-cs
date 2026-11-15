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
        IBattleHooks? hooks = null, BattleOptions? options = null)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        Random = random ?? throw new ArgumentNullException(nameof(random));
        Link = link;
        _trial = trial ?? throw new ArgumentNullException(nameof(trial));
        // Set once, here: a rule that draws or reads the campaign finds them on the state.
        if (!ReferenceEquals(state.Rules, rules)) throw new ArgumentException("The battle's rules are not the campaign's.", nameof(state));
        state.Random = random;
        state.CampaignLink = link;
        state.Hooks = hooks ?? DefaultBattleHooks.Instance;
        state.Options = options ?? BattleOptions.Default;
        RegisterCommands();
    }

    /// <summary>The user options the battle's rules read (<see cref="BattleState.Options"/> holds the same set).</summary>
    public BattleOptions Options => State.Options;

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
        copy.Options = State.Options;
    }

    /// <summary>The live link for a copy: the campaign's values, with the memorial left alone (a death in a trial must not reach the campaign).</summary>
    private sealed class TrialLink(IBattleCampaignLink live) : IBattleCampaignLink
    {
        public int MissionCount => live.MissionCount;

        public int Difficulty => live.Difficulty;

        public BattleResearchGate Research => live.Research;

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
        State.BeginCommand();
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
        finally
        {
            // One bump of the revision for the command, whatever it changed (a refusal that changed nothing leaves it).
            State.EndCommand();
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
        Register<WalkUnit>(command => RunOnTrialCopy(battle => battle.Walk(command.UnitId, command.Target, command.Move, command.IgnoreSpottedEnemies)));
        Register<MoveUnitUpDown>(command => RunOnTrialCopy(battle => battle.MoveUpDown(command.UnitId, command.Up)));
        Register<Attack>(Attack);
        Register<PrimeItem>(command => RunOnTrialCopy(battle => PrimeItem(battle, command.UnitId, command.ItemId, true, command.Turns)));
        Register<UnprimeItem>(command => RunOnTrialCopy(battle => PrimeItem(battle, command.UnitId, command.ItemId, false, 0)));
        Register<UseInventoryFeature>(UseInventoryFeature);
    }

    /// <summary>The unload and the pre-battle priming of the inventory screen; the other features are refused by name.</summary>
    private CampaignCommandResult UseInventoryFeature(UseInventoryFeature command)
    {
        Func<IReadOnlyList<ICampaignEvent>>? apply;
        switch (command.Feature)
        {
            case InventoryFeature.UnloadAmmo:
                if (State.PlanUnload(command.UnitId, command.ItemId, command.Shift, out apply) is { } unloadRefusal) return Blocked(unloadRefusal);
                return Applied(apply!);
            case InventoryFeature.Prime:
                if (State.PlanInventoryPrime(command.UnitId, command.ItemId, command.Turns, out apply) is { } primeRefusal) return Blocked(primeRefusal);
                return Applied(apply!);
            default:
                return Blocked(State.Phase == BattlePhase.Briefing ? BattleInventoryReasons.Unavailable : BattleInventoryReasons.For(command.Feature));
        }
    }

    /// <summary>The pick-up and drop of <c>Inventory::mouseClick</c> (a drop on a weapon loads it); a refusal changes nothing.</summary>
    private CampaignCommandResult MoveInventory(MoveInventoryItem command)
    {
        if (State.PlanMove(command.UnitId, command.ItemId, command.Slot ?? "", command.SlotX, command.SlotY, command.Shift, out var move, out var load) is { } refusal)
            return Blocked(refusal);
        return load is not null ? Applied(load) : Moved(move!);
    }

    private CampaignCommandResult AutoPlaceInventory(AutoPlaceInventoryItem command)
    {
        if (State.PlanAutoPlace(command.UnitId, command.ItemId, command.Smart, out var move) is { } refusal)
            return Blocked(refusal);
        // Smart choice with no section that takes a ground item: the reference "moves" it to the ground it lies on, at no cost.
        if (move is null)
            return new CampaignCommandResult([new InventoryItemMoved(command.UnitId, command.ItemId, BattleState.GroundSlot, BattleState.GroundSlot, 0, 0, 0)]);
        return Moved(move);
    }

    private CampaignCommandResult Moved(InventoryMove move) => Applied(() =>
    {
        State.ApplyMove(move);
        return [new InventoryItemMoved(move.Unit.Id, move.Item.Id, move.From.Id, move.To.Id, move.X, move.Y, move.Cost)];
    });

    /// <summary>An inventory change that is planned and checked: it runs, and in a running battle the inventory then closes.</summary>
    private CampaignCommandResult Applied(Func<IReadOnlyList<ICampaignEvent>> apply)
    {
        // InventoryState's destructor in a running battle starts with gravity on the selected unit's tile (a unit without a floor falls).
        var selected = State.Phase == BattlePhase.InProgress && State.SelectedUnitId is { } chosen ? State.UnitById(chosen) : null;
        if (selected?.Position is not null && State.FallsRefusal(selected) is { } fall) return Blocked(fall);
        var events = apply();
        // The port has no close command, so each change closes the inventory (the fall it starts was checked above).
        if (State.Phase == BattlePhase.InProgress)
        {
            State.CloseInventory();
            State.ResolveFalls();
        }
        return new CampaignCommandResult(events);
    }

    private CampaignCommandResult SelectUnit(SelectUnit command)
    {
        if (State.Select(command.UnitId, out var saved) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitSelected(command.UnitId, saved)]);
    }

    private CampaignCommandResult SelectNextUnit(SelectNextUnit command)
    {
        if (State.SelectAdjacent(command.UnitId, command.Backwards, command.InventoryOnly, out var result, out var saved) is { } refusal)
            return Blocked(refusal);
        // Nothing to select leaves the selection empty, as in the reference; the event names the unit, or -1.
        return new CampaignCommandResult([new UnitSelected(result?.Id ?? -1, saved)]);
    }

    private CampaignCommandResult SkipUnit(SkipUnit command)
    {
        if (State.Skip(command.UnitId, command.ByDistance, out var result, out var saved) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitSkipped(command.UnitId, result?.Id ?? -1, saved)]);
    }

    private CampaignCommandResult TurnUnit(TurnUnit command)
    {
        if (State.TurnTowards(command.UnitId, command.Target, out var turn) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitTurned(command.UnitId, turn.Direction, turn.Eighths, turn.TimeUnitsSpent, turn.Stop, turn.Door, turn.DoorsOpened, turn.DoorCentre)]);
    }

    private CampaignCommandResult KneelUnit(KneelUnit command) => RunOnTrialCopy(battle => battle.KneelAction(command.UnitId));

    private CampaignCommandResult SelectSpotter(SelectSpotter command)
    {
        if (State.SelectSpotter(command.UnitId, command.TargetUnitId, out var result, out var saved) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitSelected(result!.Id, saved)]);
    }

    private CampaignCommandResult ReselectSkippedUnit(ReselectSkippedUnit command)
    {
        if (State.ReselectSkipped(command.UnitId) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult([new UnitSelected(command.UnitId, true)]);
    }

    /// <summary>
    /// The attack command (S6: the hit, S7: the aimed, snap and auto shots): run on a copy first like a walk, because what a state meets that is refused by name must leave the
    /// real battle as it was. The events are the rules' own, in order (the turn's, each shot, then its hit, deaths and blasts).
    /// </summary>
    private CampaignCommandResult Attack(Attack command)
    {
        var research = Link?.Research ?? BattleResearchGate.Everything;
        return command.Kind switch
        {
            BattleAimKind.Hit => RunOnTrialCopy(battle => battle.MeleeAttackAction(research, command.UnitId, command.ItemId, command.AltPressed)),
            BattleAimKind.AutoShot or BattleAimKind.SnapShot or BattleAimKind.AimedShot =>
                RunOnTrialCopy(battle => battle.ShootAction(research, command.UnitId, command.ItemId, command.Kind, command.Target, command.ForceFire, command.SprayWaypoints)),
            BattleAimKind.Prime or BattleAimKind.Unprime => Blocked(BattleAttackReasons.PrimeIsItsOwnCommand),
            BattleAimKind.Throw => RunOnTrialCopy(battle => battle.ThrowAction(command.UnitId, command.ItemId, command.Target, command.ForceFire)),
            BattleAimKind.Launch => RunOnTrialCopy(battle => battle.LaunchAction(research, command.UnitId, command.ItemId, command.Waypoints)),
            _ => Blocked(BattleAttackReasons.KindUnsupported),
        };
    }

    /// <summary>Priming or unpriming: the state's change and its event (the research is the saved game's, as for a shot).</summary>
    private CampaignReason? PrimeItem(BattleState battle, int unitId, int itemId, bool prime, int turns)
    {
        var research = Link?.Research ?? BattleResearchGate.Everything;
        if (battle.PrimeAction(research, unitId, itemId, prime, turns, out var fuse) is { } refusal) return refusal;
        battle.Events.Add(prime ? new ItemPrimed(unitId, itemId, fuse) : new ItemUnprimed(unitId, itemId));
        return null;
    }

    /// <summary>
    /// A command that runs a state of the list (<c>UnitWalkBState</c>, <c>MeleeAttackBState</c>): first on a copy of the battle, so that what it meets that is not ported (a burning floor's
    /// hit, a fall that finds no escape) is refused before the real one changes anything; then on the
    /// battle, with the events the rules raised.
    /// </summary>
    private CampaignCommandResult RunOnTrialCopy(Func<BattleState, CampaignReason?> move)
    {
        if (_trial(State, move) is { } trial) return Blocked(trial);
        Events.Clear();
        if (move(State) is { } refusal) return Blocked(refusal);
        return new CampaignCommandResult(Events.Drain());
    }
}
