using System.Collections.ObjectModel;
using Oxce.Core.Random;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState : ICampaignCommandTarget, ICampaignQuery
{
    public const int BaseGridSize = 6;
    public const int MaximumCommandTicks = 1_000_000;

    private readonly RuntimeContent _content;
    private readonly IStatefulRandomSource _random;
    private readonly List<CountryState> _countries;
    private readonly List<RegionState> _regions;
    private readonly List<BaseState> _bases;
    private readonly SortedDictionary<string, int> _nextIds;
    private readonly List<long> _funds;
    private readonly List<long> _maintenance;
    private readonly List<long> _incomes;
    private readonly List<long> _expenditures;
    private readonly List<int> _researchScores;
    private readonly object _transactionGate = new();
    private readonly CampaignHandlerTable _handlers;
    private ScriptValueState? _scriptValues;
    private IReadOnlyList<CampaignRestriction> _restrictions = [];
    private HashSet<string> _completedResearch = new(StringComparer.Ordinal);
    private Dictionary<string, int> _researchRuleStatus = new(StringComparer.Ordinal);
    private Dictionary<string, int> _manufactureRuleStatus = new(StringComparer.Ordinal);
    private Dictionary<string, int> _monthlyPurchaseLog = new(StringComparer.Ordinal);
    private bool _debugMode;

    /// <summary>SavedGame::getWarned/setWarned, read and changed by the monthly report.</summary>
    internal bool Warned { get; set; }

    /// <summary>SavedGame::_missionStatistics, which commendations read; unchanged until Phase 7 adds battles.</summary>
    internal IReadOnlyList<MissionStatisticSnapshot> MissionStatistics { get; private init; } = [];
    private readonly List<DeadSoldierSnapshot> _deadSoldiers = [];
    private CampaignTimeProfile? _timeProfile;

    internal CampaignState(
        RuntimeContent content,
        IStatefulRandomSource random,
        CampaignIdentity identity,
        CampaignDifficulty difficulty,
        CampaignTime time,
        int ending,
        int monthsPassed,
        int daysPassed,
        IEnumerable<long> funds,
        IEnumerable<long> maintenance,
        IEnumerable<long> incomes,
        IEnumerable<long> expenditures,
        IEnumerable<int> researchScores,
        IEnumerable<KeyValuePair<string, int>> nextIds,
        IEnumerable<CountryState> countries,
        IEnumerable<RegionState> regions,
        IEnumerable<BaseState> bases,
        ScriptValueState? scriptValues)
    {
        _content = content;
        _random = random;
        Identity = ValidateIdentity(identity);
        Difficulty = ValidateDifficulty(difficulty);
        Time = time.Validate();
        Ending = ending;
        MonthsPassed = monthsPassed;
        DaysPassed = daysPassed;
        _funds = funds.ToList();
        _maintenance = maintenance.ToList();
        _incomes = incomes.ToList();
        _expenditures = expenditures.ToList();
        _researchScores = researchScores.ToList();
        _nextIds = new SortedDictionary<string, int>(nextIds.ToDictionary(), StringComparer.Ordinal);
        _countries = countries.ToList();
        _regions = regions.ToList();
        _bases = bases.ToList();
        _scriptValues = scriptValues;
        _handlers = BuildHandlers();
        ValidateState();
    }

    public CampaignIdentity Identity { get; private set; }
    public CampaignOptions Options { get; internal init; } = new();

    /// <summary>Optional time-advance diagnostics (<see cref="CampaignTimeProfile"/>); null by default. It
    /// never changes gameplay and is not saved.</summary>
    public CampaignTimeProfile? TimeProfile
    {
        get { lock (_transactionGate) return _timeProfile; }
        set { lock (_transactionGate) _timeProfile = value; }
    }

    internal CampaignTimeProfile? ActiveTimeProfile => _timeProfile;

    /// <summary>
    /// The installation's terrain files, which battle generation reads (ADR 0038). The host attaches them; they are
    /// not campaign state, are not saved and are not copied to shadows. Null means battles cannot be generated.
    /// </summary>
    public Battles.Terrain.IBattleTerrainFiles? BattleTerrainFiles
    {
        get { lock (_transactionGate) return _battleTerrainFiles; }
        set { lock (_transactionGate) _battleTerrainFiles = value; }
    }

    private Battles.Terrain.IBattleTerrainFiles? _battleTerrainFiles;
    public CampaignDifficulty Difficulty { get; }
    public CampaignTime Time { get; private set; }
    public int Ending { get; private set; }

    /// <summary>SavedGame::setEnding: 1 is END_WIN, 2 is END_LOSE.</summary>
    internal void EndCampaign(int ending) => Ending = ending;

    /// <summary>Removes a destroyed base with everything it holds (Base's destructor).</summary>
    internal void RemoveBase(BaseState owner) => _bases.Remove(owner);
    public int MonthsPassed { get; private set; }
    public int DaysPassed { get; private set; }

    public CampaignCommandResult Execute(ICampaignCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_transactionGate)
        {
            if (_faulted is { } faulted) return new CampaignCommandResult([new CampaignActionBlocked(faulted)]);
            var restriction = _restrictions.FirstOrDefault(r => r.BaseId is null && r.BlocksLogistics && r.BlocksTime);
            if (restriction is not null) return new CampaignCommandResult([new CampaignActionBlocked(restriction.Feature)]);
            if (Ending != 0) return new CampaignCommandResult([new CampaignActionBlocked("This campaign has ended.")]);
            if (!_handlers.TryGetCommand(command.GetType(), out var handler))
                throw new ArgumentException($"Unsupported campaign command '{command.GetType().Name}'.",
                    nameof(command));
            return _handlers.CommandGate(command) ?? handler(command);
        }
    }

    /// <summary>
    /// Why the campaign cannot be saved now, or null. The reference cannot save under a modal window whose OK has
    /// gameplay effects (D2); a save made there would lose them on reload.
    /// </summary>
    public string? QuerySaveRefusal()
    {
        lock (_transactionGate) return SaveRefusal();
    }

    // An ended campaign's open windows are moot: the reference's game-over video drops its queued popups (ADR 0036).
    private string? SaveRefusal() => Ending != 0 ? null : _handlers.SaveGate();

    /// <summary>The snapshot to save, taken under the same lock as the refusal check; null with the reason when refused.</summary>
    public CampaignSnapshot? CaptureForSave(out string? refusal)
    {
        lock (_transactionGate)
        {
            refusal = SaveRefusal();
            return refusal is null ? CaptureCore() : null;
        }
    }

    public CampaignSnapshot Capture()
    {
        lock (_transactionGate) return CaptureCore();
    }

    /// <summary>Returns a capability query; implementations take the transaction gate themselves.</summary>
    public TQuery? GetQuery<TQuery>() where TQuery : class => _handlers.Query<TQuery>();

    internal RuntimeContent Content => _content;

    internal IStatefulRandomSource Random => _random;

    // Concrete lists keep per-tick world loops on struct enumerators.
    internal List<BaseState> BaseStates => _bases;

    /// <summary>The base with the ID, or null.</summary>
    internal BaseState? BaseById(int id) => _bases.Find(candidate => candidate.Id == id);

    /// <summary>The index in the base's crafts of the craft with the ID and type (crafts are numbered per type), or -1.</summary>
    internal int CraftIndex(BaseState owner, int craftId, string craftType) =>
        owner.Crafts.FindIndex(craft => craft.Id == craftId && _content.RuntimeRules.Crafts.GetExternalId(craft.Rule) == craftType);

    /// <summary>The base's craft with the ID and type, or null.</summary>
    internal CraftState? CraftById(BaseState owner, int craftId, string craftType) =>
        CraftIndex(owner, craftId, craftType) is var index and >= 0 ? owner.Crafts[index] : null;

    internal List<CountryState> CountryStates => _countries;

    internal List<RegionState> RegionStates => _regions;

    internal T Read<T>(Func<T> query)
    {
        lock (_transactionGate) return query();
    }

    public CampaignOverview QueryOverview()
    {
        lock (_transactionGate)
        {
            return new CampaignOverview(
                Identity.Id,
                Identity.Name,
                Time,
                DaysPassed,
                _funds[^1],
                _countries.Count,
                _regions.Count,
                CampaignSnapshot.ReadOnly(_bases.Select(QueryBase)));
        }
    }

    public CampaignStores QueryStores(int baseId)
    {
        lock (_transactionGate)
        {
            var state = FindBase(baseId);
            var incoming = state.Transfers.Where(t => !t.Delivered && t.Kind == CampaignTransferKind.Item)
                .GroupBy(t => t.RuleId, StringComparer.Ordinal).ToDictionary(g => g.Key,
                    g => checked((int)g.Sum(t => (long)t.Quantity)), StringComparer.Ordinal);
            var items = _content.RuntimeRules.Items.Rules.Select(r => new CampaignStoreItem(r.Id,
                r.Value.Name.Length == 0 ? r.Id : r.Value.Name,
                state.Items.GetValueOrDefault(_content.RuntimeRules.Items.GetRequired(r.Id)),
                incoming.GetValueOrDefault(r.Id), r.Value.Size)).Where(i => i.Stored != 0 || i.Incoming != 0);
            var transfers = state.Transfers.Where(t => !t.Delivered).Select(t => new CampaignIncomingTransfer(
                t.Id, t.Kind, t.Soldier?.Personal?.Name ?? (t.Kind switch
                {
                    CampaignTransferKind.Scientist => "STR_SCIENTIST",
                    CampaignTransferKind.Engineer => "STR_ENGINEER",
                    _ => t.RuleId,
                }), t.Quantity, t.Hours));
            return new(baseId, UsedStores(state), AvailableStores(state), UsedQuarters(state), AvailableQuarters(state),
                CampaignSnapshot.ReadOnly(items), CampaignSnapshot.ReadOnly(transfers))
            { CapacityLimitation = MissingCraftInventory(state) };
        }
    }

    private CampaignSnapshot CaptureCore() => _handlers.Capture(new(
            Identity with { ActiveMods = CampaignSnapshot.ReadOnly(Identity.ActiveMods) },
            Difficulty,
            Time,
            Ending,
            MonthsPassed,
            DaysPassed,
            _random.State,
            CampaignSnapshot.ReadOnly(_funds),
            CampaignSnapshot.ReadOnly(_maintenance),
            CampaignSnapshot.ReadOnly(_incomes),
            CampaignSnapshot.ReadOnly(_expenditures),
            CampaignSnapshot.ReadOnly(_researchScores),
            CampaignSnapshot.ReadOnlyIds(_nextIds),
            CampaignSnapshot.ReadOnly(_countries.Select(CaptureCountry)),
            CampaignSnapshot.ReadOnly(_regions.Select(CaptureRegion)),
            CampaignSnapshot.ReadOnly(_bases.Select(CaptureBase)),
            CampaignSnapshot.ReadOnly(_scriptValues?.Capture() ?? []))
    {
        Restrictions = CampaignSnapshot.ReadOnly(_restrictions),
        DeadSoldiers = CampaignSnapshot.ReadOnly(_deadSoldiers),
        DebugMode = _debugMode,
        Warned = Warned,
        MissionStatistics = MissionStatistics,
        Options = Options,
    });

    /// <summary>
    /// SavedGame::killSoldier with resetArmor and Soldier::die: the soldier leaves its base for the memorial in
    /// its rule's default armor, without craft, training, wounds or recovery, dated now.
    /// </summary>
    internal void KillSoldier(BaseState owner, SoldierState soldier)
    {
        if (!owner.Soldiers.Remove(soldier)) throw new InvalidOperationException("The soldier is not at that base.");
        var rules = _content.RuntimeRules;
        var rule = rules.Soldiers[soldier.Rule].Value;
        var personal = soldier.Personal is not { } alive ? null : alive with
        {
            Armor = rules.Armors.GetExternalId(rule.Armor),
            ReplacedArmor = "",
            TransformedArmor = "",
            CraftType = "",
            CraftId = 0,
            PsiTraining = false,
            Training = false,
            ReturnToTrainingWhenHealed = false,
            ManaMissing = 0,
            HealthMissing = 0,
            Recovery = 0,
        };
        _deadSoldiers.Add(new DeadSoldierSnapshot(
            new SoldierSnapshot(rules.Soldiers.GetExternalId(soldier.Rule), soldier.Id) { PreservationKey = soldier.PreservationKey, Personal = personal },
            Time));
    }

    public static CampaignState Restore(
        CampaignSnapshot snapshot,
        RuntimeContent content,
        IStatefulRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Options);
        if (snapshot.Options.MaximumBases <= 0) throw new InvalidDataException("Maximum base count must be positive.");
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(random);
        if (!content.Capabilities.Has(ContentLoadStage.RuntimeLinked))
            throw new InvalidOperationException("Campaign restoration requires runtime-linked content.");

        var rules = content.RuntimeRules;
        var countries = snapshot.Countries.Select(country => new CountryState(
            rules.Countries.GetRequired(country.RuleId),
            RequiredHistory(country.Funding, "country funding"),
            RequiredHistory(country.ActivityXcom, "country XCOM activity"),
            RequiredHistory(country.ActivityAlien, "country alien activity"),
            country.Pact,
            country.NewPact,
            country.CancelPact,
            RestoreScriptValues(content, "Country", country.ScriptValues))).ToArray();
        var regions = snapshot.Regions.Select(region => new RegionState(
            rules.Regions.GetRequired(region.RuleId),
            RequiredHistory(region.ActivityXcom, "region XCOM activity"),
            RequiredHistory(region.ActivityAlien, "region alien activity"))).ToArray();
        var bases = snapshot.Bases.Select(source => RestoreBase(source, content)).ToArray();
        foreach (var owner in bases) ValidateAssignments(owner, rules);
        EnsureUnique(snapshot.Countries.Select(static country => country.RuleId), "country rule IDs");
        EnsureUnique(snapshot.Regions.Select(static region => region.RuleId), "region rule IDs");
        EnsureCraftIds(snapshot.Bases.SelectMany(static item => item.Crafts.Concat(
            item.Transfers.Where(t => !t.Delivered && t.Craft is not null).Select(t => t.Craft!))));
        EnsureSoldierIds(snapshot.Bases.SelectMany(static item => item.Soldiers.Concat(
            item.Transfers.Where(t => !t.Delivered && t.Soldier is not null).Select(t => t.Soldier!))));
        EnsureUnique(snapshot.Bases.SelectMany(static item => item.Transfers.Select(t => t.Id)), "transfer IDs");
        EnsureUnique(snapshot.CompletedResearch, "completed research IDs");
        foreach (var research in snapshot.CompletedResearch) ArgumentException.ThrowIfNullOrWhiteSpace(research);
        foreach (var purchase in snapshot.MonthlyPurchaseLog)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(purchase.Key);
            if (purchase.Value < 0) throw new InvalidDataException("Monthly purchase counts cannot be negative.");
        }
        if (snapshot.CustomRuleCraftDeployments.Sum(static pair => pair.Value.Count) > MaximumLogisticsLines)
            throw new InvalidDataException("Custom craft deployments exceed their supported size.");

        var previousRandomState = random.State;
        try
        {
            random.Restore(snapshot.RandomState);
            var campaign = new CampaignState(
                content,
                random,
                snapshot.Identity,
                snapshot.Difficulty,
                snapshot.Time,
                snapshot.Ending,
                snapshot.MonthsPassed,
                snapshot.DaysPassed,
                snapshot.Funds,
                snapshot.Maintenance,
                snapshot.Incomes,
                snapshot.Expenditures,
                snapshot.ResearchScores,
                snapshot.NextIds,
                countries,
                regions,
                bases,
                RestoreScriptValues(content, "GeoscapeGame", snapshot.ScriptValues))
            {
                _restrictions = CampaignSnapshot.ReadOnly(snapshot.Restrictions),
                _debugMode = snapshot.DebugMode,
                Warned = snapshot.Warned,
                MissionStatistics = CampaignSnapshot.ReadOnly(snapshot.MissionStatistics),
                // Mods that fix user options override the options a save was made with.
                Options = snapshot.Options.WithFixedUserOptions(content.RuntimeRules.Campaign.FixedUserOptions),
            };
            foreach (var dead in snapshot.DeadSoldiers)
            {
                ArgumentNullException.ThrowIfNull(dead?.Soldier);
                campaign._deadSoldiers.Add(dead);
            }
            campaign._handlers.Restore(snapshot);
            campaign._handlers.Validate();
            return campaign;
        }
        catch
        {
            random.Restore(previousRandomState);
            throw;
        }
    }

    /// <summary>Initializes capability state of a campaign created by <see cref="CampaignFactory"/>.</summary>
    internal CampaignState InitializeNew()
    {
        _handlers.Initialize();
        _handlers.Validate();
        return this;
    }

    // SavedGame::getId keys are external names: whitespace is significant, only empty is invalid.
    internal int NextId(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!_nextIds.TryGetValue(name, out var next)) next = 1;
        _nextIds[name] = checked(next + 1);
        return next;
    }

    internal int PeekNextId(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _nextIds.GetValueOrDefault(name, 1);
    }

    private CampaignCommandResult Advance(AdvanceCampaignTime command)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(command.FiveSecondTicks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(command.FiveSecondTicks, MaximumCommandTicks);
        if (!Enum.IsDefined(command.SelectedSpeed)) throw new ArgumentOutOfRangeException(nameof(command), "Selected speed is invalid.");
        var previous = Time;
        _logisticsQuote = null;
        var effects = new TimeEffects(this, command.SelectedSpeed);
        var result = CampaignTimeDispatcher.Advance(Time, command.FiveSecondTicks, effects);
        var advanced = new CampaignTimeAdvanced(previous, Time, result.Summary) { SelectedSpeed = command.SelectedSpeed };
        if (effects.Events is { } notifications)
        {
            notifications.Insert(0, advanced);
            if (result.BlockedReason is { } blocked) notifications.Add(new CampaignActionBlocked(blocked));
            return new CampaignCommandResult(CampaignSnapshot.ReadOnly(notifications));
        }
        return new CampaignCommandResult(result.BlockedReason is { } reason
            ? [advanced, new CampaignActionBlocked(reason)] : [advanced]);
    }

    internal sealed class TimeEffects(CampaignState campaign,
        CampaignTimeSpeed selectedSpeed = CampaignTimeSpeed.FiveSeconds) : ICampaignTimeEffects
    {
        public List<ICampaignEvent>? Events { get; private set; }
        /// <summary>Craft identified by base, rule type and ID; a craft list can grow within a tick.</summary>
        internal List<(int BaseId, string CraftTypeId, int CraftId)> AutoPatrolCandidates { get; } = [];
        public CampaignTime Current => campaign.Time;
        public CampaignTimeSpeed SelectedSpeed { get; } = selectedSpeed;
        public void Notify(ICampaignEvent notification) => (Events ??= []).Add(notification);

        public string? Preflight(CampaignTime nextTime, CampaignTimeTrigger highestTrigger) =>
            campaign._handlers.Preflight(nextTime, highestTrigger, SelectedSpeed, campaign._timeProfile);

        public bool Apply(CampaignTime current, CampaignTimeTrigger trigger)
        {
            campaign.Time = current;
            campaign._handlers.Apply(trigger, this, campaign._timeProfile);
            return Events is not null;
        }

        public int StagePoints(CampaignTime nextTime, CampaignTimeTrigger highestTrigger) =>
            campaign._handlers.StagePoints(nextTime, highestTrigger) |
            (StageEveryTick && highestTrigger > CampaignTimeTrigger.FiveSeconds ? 1 << (int)highestTrigger : 0);

        /// <summary>Verification only: the shadow's snapshots at each stage point, compared by <see cref="Restage"/>.</summary>
        private Queue<string>? _stagedSnapshots;

        // ADR 0035: the shadow runs the tick down to each stage point and preflights the rest there. It shares the
        // random source, whose state is put back afterwards, so the real tick draws the same values.
        public string? PreflightStages(CampaignTime nextTime, CampaignTimeTrigger highestTrigger, int stagePoints)
        {
            var randomState = campaign._random.State;
            try
            {
                var shadow = campaign.CreateShadow();
                StagedTicksOnThisThread++;
                if (VerifyStagedSnapshots)
                {
                    StagingVerification.AssertSame(StagingVerification.Canonical(campaign.Capture(), campaign._content), shadow.Capture(),
                        campaign._content, "a new shadow copy");
                    _stagedSnapshots = new();
                }
                var effects = new TimeEffects(shadow, SelectedSpeed);
                // Execution reads the pass its own preflight builds, so the shadow needs the same first preflight.
                if (effects.Preflight(nextTime, highestTrigger) is { } first)
                    throw new InvalidOperationException($"A shadow campaign disagreed with its original's preflight: {first}");
                for (var trigger = (int)highestTrigger; trigger > 0; trigger--)
                {
                    // A handler that finds input it cannot use (malformed histories, arithmetic the reference leaves
                    // undefined, a script it cannot run) refuses the tick here, before the real campaign changes.
                    try { effects.Apply(nextTime, (CampaignTimeTrigger)trigger); }
                    catch (Exception error) when (error is InvalidDataException or OverflowException) { return error.Message; }
                    if ((stagePoints & (1 << trigger)) == 0) continue;
                    _stagedSnapshots?.Enqueue(StagingVerification.Canonical(shadow.Capture(), campaign._content));
                    if (effects.Preflight(nextTime, (CampaignTimeTrigger)(trigger - 1)) is { } reason) return reason;
                    if ((stagePoints & ((1 << trigger) - 1)) == 0) break;
                }
                return null;
            }
            finally
            {
                campaign._random.Restore(randomState);
            }
        }

        public void Restage(CampaignTime current, CampaignTimeTrigger remainingHighestTrigger)
        {
            if (_stagedSnapshots is { Count: > 0 } snapshots)
                StagingVerification.AssertSame(snapshots.Dequeue(), campaign.Capture(), campaign._content, "its shadow copy at a stage point");
            if (Preflight(current, remainingHighestTrigger) is { } reason)
            {
                // The higher levels already ran, so the campaign is mid-tick: refuse every later command.
                campaign._faulted = $"A staged time preflight disagreed with its shadow copy: {reason}";
                throw new InvalidOperationException(campaign._faulted);
            }
        }
    }

    private static readonly AsyncLocal<bool?> StageEveryTickOnThisFlow = new();

    /// <summary>
    /// Test-only (ADR 0035): stage every tick after its highest level, so verification runs exercise the shadow
    /// copy everywhere. It never changes a result; a difference is a defect in the copy or in a forecast. Setting it
    /// applies to the current async flow, ahead of <see cref="StageEveryTickEverywhere"/>, so a test that measures
    /// unstaged cost can turn it off.
    /// </summary>
    internal static bool StageEveryTick
    {
        get => StageEveryTickOnThisFlow.Value ?? StageEveryTickEverywhere;
        set => StageEveryTickOnThisFlow.Value = value;
    }

    /// <summary>Test-only: <see cref="StageEveryTick"/> for every campaign in the process (verification runs).</summary>
    internal static bool StageEveryTickEverywhere { get; set; }

    /// <summary>Test-only: staged ticks on this thread, which shows that a test actually staged.</summary>
    [ThreadStatic] internal static long StagedTicksOnThisThread;

    /// <summary>
    /// Test-only: compare the shadow's snapshot with the original when it is created and at every stage point. The
    /// test assemblies enable it for every test; it never changes a result.
    /// </summary>
    internal static bool VerifyStagedSnapshots { get; set; }

    /// <summary>Set when a staged recheck disagreed after the tick's higher levels ran; every later command refuses.</summary>
    private string? _faulted;

    /// <summary>A copy for staged preflights: the save-neutral state, this random source and transient state.</summary>
    internal CampaignState CreateShadow()
    {
        var previous = TestCapabilities.Value;
        TestCapabilities.Value = _testCapabilities;
        try
        {
            var shadow = Restore(Capture(), _content, _random);
            _handlers.CopyTransientTo(shadow._handlers);
            return shadow;
        }
        finally { TestCapabilities.Value = previous; }
    }

    private void RegisterCalendar(CampaignCapabilityRegistry registry)
    {
        registry.Operation<ICampaignInitialWorldCalendar>(this);
        registry.Command<AdvanceCampaignTime>(Advance);
        registry.Command<PlaceStartingBase>(Place);
        registry.Preflight(CampaignPreflightOrder.Restrictions, "restrictions", (_, _) =>
        {
            foreach (var restriction in _restrictions)
                if (restriction.BlocksTime) return restriction.Feature;
            return null;
        });
        registry.Timed(CampaignTimeTrigger.OneMonth, CampaignTimeOrder.MonthCalendar, "months passed",
            _ => MonthsPassed++);
        registry.Timed(CampaignTimeTrigger.OneDay, CampaignTimeOrder.DayCalendar, "days passed",
            _ => DaysPassed++);
    }

    private CampaignCommandResult Place(PlaceStartingBase command)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(command.BaseIndex);
        if ((uint)command.BaseIndex >= (uint)_bases.Count) throw new ArgumentOutOfRangeException(nameof(command));
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Name);
        if (!double.IsFinite(command.Longitude) || command.Longitude < 0 || command.Longitude >= 2 * Math.PI)
            throw new ArgumentOutOfRangeException(nameof(command), "Longitude must be in [0, 2π).");
        if (!double.IsFinite(command.Latitude) || command.Latitude < -Math.PI / 2 || command.Latitude > Math.PI / 2)
            throw new ArgumentOutOfRangeException(nameof(command), "Latitude must be in [-π/2, π/2].");
        var target = _bases[command.BaseIndex];
        if (target.IsPlaced) throw new InvalidOperationException("The starting base is already placed.");
        if (BaseSiteRestriction(command.Longitude, command.Latitude, true) is { } siteReason) return Blocked(siteReason);
        target.Name = command.Name;
        target.Longitude = command.Longitude;
        target.Latitude = command.Latitude;
        for (var index = 0; index < target.Crafts.Count; index++)
            if (target.Crafts[index].Logistics is { } state)
                target.Crafts[index] = target.Crafts[index] with
                { Logistics = state with { Longitude = command.Longitude, Latitude = command.Latitude } };
        _logisticsQuote = null;
        return new CampaignCommandResult(
            [new StartingBasePlaced(command.BaseIndex, command.Name, command.Longitude, command.Latitude)]);
    }

    private void ValidateState()
    {
        if (Options.MaximumBases <= 0) throw new InvalidDataException("Maximum base count must be positive.");
        RequiredHistory(_funds, "funds");
        RequiredHistory(_maintenance, "maintenance");
        RequiredHistory(_incomes, "incomes");
        RequiredHistory(_expenditures, "expenditures");
        RequiredHistory(_researchScores, "research scores");
        if (MonthsPassed < -1) throw new ArgumentOutOfRangeException(nameof(MonthsPassed));
        if (DaysPassed < 0) throw new ArgumentOutOfRangeException(nameof(DaysPassed));
        // A started campaign may have lost its last base; the next five-second tick ends it.
        if (_bases.Count == 0 && MonthsPassed < 0) throw new InvalidDataException("A campaign must contain at least one base.");
        EnsureUnique(_bases.Select(static item => item.Id), "base IDs");
        foreach (var pair in _nextIds)
        {
            // Native custom counters skip only empty names; whitespace is part of their identity.
            ArgumentException.ThrowIfNullOrEmpty(pair.Key);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pair.Value);
        }
        foreach (var item in _bases) ValidateBase(item, _content.RuntimeRules);
        var craftIds = new HashSet<(string Type, int Id)>();
        var soldierIds = new HashSet<int>();
        foreach (var baseState in _bases)
        {
            foreach (var craft in baseState.Crafts)
            {
                var identity = (_content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id);
                if (!craftIds.Add(identity)) throw new InvalidDataException("Duplicate craft identity was found.");
            }
            foreach (var soldier in baseState.Soldiers)
                if (!soldierIds.Add(soldier.Id)) throw new InvalidDataException("Duplicate soldier ID was found.");
        }
    }

    private CountrySnapshot CaptureCountry(CountryState state) => new(
        _content.RuntimeRules.Countries.GetExternalId(state.Rule),
        CampaignSnapshot.ReadOnly(state.Funding),
        CampaignSnapshot.ReadOnly(state.ActivityXcom),
        CampaignSnapshot.ReadOnly(state.ActivityAlien),
        state.Pact,
        state.NewPact,
        state.CancelPact,
        CampaignSnapshot.ReadOnly(state.ScriptValues?.Capture() ?? []));

    private RegionSnapshot CaptureRegion(RegionState state) => new(
        _content.RuntimeRules.Regions.GetExternalId(state.Rule),
        CampaignSnapshot.ReadOnly(state.ActivityXcom),
        CampaignSnapshot.ReadOnly(state.ActivityAlien));

    private BaseSnapshot CaptureBase(BaseState state) => new(
        state.Id,
        state.Name,
        state.Longitude,
        state.Latitude,
        CampaignSnapshot.ReadOnly(state.Facilities.Select(facility => new FacilitySnapshot(
            _content.RuntimeRules.Facilities.GetExternalId(facility.Rule), facility.X, facility.Y,
            facility.BuildTime, facility.Ammo, facility.AmmoMissingReported, facility.Disabled,
            facility.HadPreviousFacility)
        { PreservationKey = facility.PreservationKey })),
        CampaignSnapshot.ReadOnly(state.Crafts.Select(craft =>
            new CraftSnapshot(_content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id) { PreservationKey = craft.PreservationKey, Logistics = craft.Logistics, ScriptValues = craft.ScriptValues?.Capture() ?? [] })),
        CampaignSnapshot.ReadOnly(state.Soldiers.Select(soldier =>
            new SoldierSnapshot(_content.RuntimeRules.Soldiers.GetExternalId(soldier.Rule), soldier.Id) { PreservationKey = soldier.PreservationKey, Personal = soldier.Personal })),
        new ReadOnlyDictionary<string, int>(state.Items.ToDictionary(
            item => _content.RuntimeRules.Items.GetExternalId(item.Key), static item => item.Value,
            StringComparer.Ordinal)),
        state.Scientists,
        state.Engineers)
    {
        Transfers = CampaignSnapshot.ReadOnly(state.Transfers),
        FakeUnderwater = state.FakeUnderwater,
        RetaliationTarget = state.RetaliationTarget,
        RetaliationMissionId = state.RetaliationMissionId,
        Research = CampaignSnapshot.ReadOnly(state.Research.Select(project => new ResearchProjectSnapshot(
            _content.RuntimeRules.Research.GetExternalId(project.Rule), project.Assigned, project.Spent, project.Cost))),
        Productions = CampaignSnapshot.ReadOnly(state.Productions.Select(production => new ProductionSnapshot(
            _content.RuntimeRules.Manufacture.GetExternalId(production.Rule), production.Assigned,
            production.Spent, production.Amount, production.Infinite, production.Sell, production.IsFallback,
            new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(production.RandomProductionInfo, StringComparer.Ordinal))))),
    };

    private CampaignBaseOverview QueryBase(BaseState state) => new(
        state.Id,
        state.Name,
        state.Longitude,
        state.Latitude,
        CampaignSnapshot.ReadOnly(state.Facilities.Select(facility =>
        {
            var rule = _content.RuntimeRules.Facilities[facility.Rule].Value;
            return new CampaignFacilityOverview(
                facility.X, facility.Y, rule.SizeX, rule.SizeY, facility.BuildTime);
        })),
        state.Crafts.Count,
        state.Soldiers.Count,
        state.Items.Count,
        state.Scientists,
        state.Engineers);

    private static BaseState RestoreBase(BaseSnapshot source, RuntimeContent content)
    {
        var rules = content.RuntimeRules;
        if (source.Id < 0) throw new InvalidDataException("Base IDs cannot be negative.");
        if (!double.IsFinite(source.Longitude) || !double.IsFinite(source.Latitude))
            throw new InvalidDataException("Base coordinates must be finite.");
        var facilities = source.Facilities.Select(facility => new FacilityState(
            rules.Facilities.GetRequired(facility.RuleId), facility.X, facility.Y, facility.BuildTime,
            facility.Ammo, facility.AmmoMissingReported, facility.Disabled, facility.HadPreviousFacility)
        { PreservationKey = facility.PreservationKey }).ToArray();
        var crafts = source.Crafts.Select(craft => new CraftState(
            rules.Crafts.GetRequired(craft.RuleId), PositiveId(craft.Id, "craft"))
        {
            PreservationKey = craft.PreservationKey,
            Logistics = RestoreCraftLogistics(craft.Logistics, rules),
            ScriptValues = RestoreScriptValues(content, "Craft", craft.ScriptValues)
        }).ToArray();
        var soldiers = source.Soldiers.Select(soldier => new SoldierState(
            rules.Soldiers.GetRequired(soldier.RuleId), PositiveId(soldier.Id, "soldier"))
        { PreservationKey = soldier.PreservationKey, Personal = RestorePersonal(soldier.Personal, rules) }).ToArray();
        var items = new Dictionary<RuleHandle<ItemRuleFamily>, int>();
        foreach (var pair in source.Items)
        {
            if (pair.Value <= 0) throw new InvalidDataException($"Item '{pair.Key}' has a non-positive quantity.");
            items.Add(rules.Items.GetRequired(pair.Key), pair.Value);
        }
        var result = new BaseState(source.Id, source.Name, source.Longitude, source.Latitude, facilities, crafts,
            soldiers, items, source.Scientists, source.Engineers);
        result.Research.AddRange(source.Research.Select(project => new ResearchProjectState(
            rules.Research.GetRequired(project.RuleId), project.Assigned, project.Spent, project.Cost)));
        result.Productions.AddRange(source.Productions.Select(production => new ProductionState(
            rules.Manufacture.GetRequired(production.RuleId), production.Assigned, production.Spent,
            production.Amount, production.Infinite, production.Sell, production.IsFallback,
            new Dictionary<string, int>(production.RandomProductionInfo, StringComparer.Ordinal))));
        result.FakeUnderwater = source.FakeUnderwater;
        result.RetaliationTarget = source.RetaliationTarget;
        result.RetaliationMissionId = source.RetaliationMissionId;
        foreach (var transfer in source.Transfers)
        {
            ValidateTransfer(transfer, rules);
            result.Transfers.Add(transfer with
            {
                Soldier = transfer.Soldier is { } soldier ? soldier with { Personal = RestorePersonal(soldier.Personal, rules) } : null,
                Craft = transfer.Craft is { } craft ? craft with
                {
                    Logistics = RestoreCraftLogistics(craft.Logistics, rules),
                    ScriptValues = RestoreScriptValues(content, "Craft", craft.ScriptValues)?.Capture() ?? [],
                } : null,
            });
        }
        return result;
    }

    private static void ValidateTransfer(TransferSnapshot transfer, RuntimeRuleCatalog rules)
    {
        PositiveId(transfer.Id, "transfer");
        if (!Enum.IsDefined(transfer.Kind) || transfer.Quantity < 0 ||
            transfer.Quantity == 0 && transfer.Kind != CampaignTransferKind.Item)
            throw new InvalidDataException("Transfer kind or quantity is invalid.");
        if (transfer.Hours == int.MinValue) throw new InvalidDataException("Transfer hours would overflow.");
        if ((transfer.Soldier is not null) != (transfer.Kind == CampaignTransferKind.Soldier) ||
            (transfer.Craft is not null) != (transfer.Kind == CampaignTransferKind.Craft))
            throw new InvalidDataException("Transfer payload does not match its kind.");
        if (transfer.Soldier is { } soldier)
        {
            rules.Soldiers.GetRequired(soldier.RuleId);
            PositiveId(soldier.Id, "soldier");
            if (transfer.Quantity != 1 || transfer.RuleId != soldier.RuleId)
                throw new InvalidDataException("Soldier transfer payload is inconsistent.");
        }
        if (transfer.Craft is { } craft)
        {
            rules.Crafts.GetRequired(craft.RuleId);
            PositiveId(craft.Id, "craft");
            if (transfer.Quantity != 1 || transfer.RuleId != craft.RuleId)
                throw new InvalidDataException("Craft transfer payload is inconsistent.");
        }
        if (transfer.Kind == CampaignTransferKind.Item) rules.Items.GetRequired(transfer.RuleId);
    }

    private static void ValidateBase(BaseState state, RuntimeRuleCatalog rules)
    {
        if (state.Scientists < 0 || state.Engineers < 0)
            throw new InvalidDataException("Base personnel counts cannot be negative.");
        var usedQuarters = state.Soldiers.Count + (long)state.Scientists + state.Engineers;
        usedQuarters += state.Research.Sum(project => (long)project.Assigned);
        usedQuarters += state.Productions.Sum(project => (long)project.Assigned);
        usedQuarters += state.Productions.Count(project => rules.Manufacture[project.Rule].Value.SpawnedPersonType.Length != 0);
        var incomingItems = new Dictionary<RuleHandle<ItemRuleFamily>, long>();
        foreach (var transfer in state.Transfers.Where(transfer => !transfer.Delivered))
        {
            if (transfer.Kind is CampaignTransferKind.Soldier or CampaignTransferKind.Scientist or CampaignTransferKind.Engineer)
                usedQuarters += transfer.Quantity;
            if (transfer.Kind == CampaignTransferKind.Item)
            {
                var item = rules.Items.GetRequired(transfer.RuleId);
                incomingItems[item] = incomingItems.GetValueOrDefault(item) + transfer.Quantity;
                if (incomingItems[item] > int.MaxValue)
                    throw new InvalidDataException($"Incoming quantity for item '{transfer.RuleId}' exceeds the supported range.");
            }
        }
        if (usedQuarters > int.MaxValue)
            throw new InvalidDataException("Base personnel including incoming transfers exceed the supported range.");
        foreach (var prisonType in rules.Items.Rules.Where(entry => entry.Value.IsAlien).Select(entry => entry.Value.PrisonType).Distinct())
        {
            var contained = state.Items.Where(pair => rules.Items[pair.Key].Value is { IsAlien: true } rule && rule.PrisonType == prisonType)
                .Sum(pair => (long)pair.Value);
            contained += incomingItems.Where(pair => rules.Items[pair.Key].Value is { IsAlien: true } rule && rule.PrisonType == prisonType)
                .Sum(pair => pair.Value);
            if (contained > int.MaxValue)
                throw new InvalidDataException($"Alien containment usage for prison type {prisonType} exceeds the supported range.");
        }
        foreach (var project in state.Research)
        {
            rules.Research.Validate(project.Rule);
            if (project.Assigned < 0 || project.Spent < 0 || project.Cost < 0)
                throw new InvalidDataException("Research project values cannot be negative.");
        }
        foreach (var production in state.Productions)
        {
            rules.Manufacture.Validate(production.Rule);
            if (production.Assigned < 0 || production.Spent < 0 || production.Amount < 0 ||
                production.RandomProductionInfo.Any(pair => pair.Key.Length == 0 || pair.Value < 0))
                throw new InvalidDataException("Production values cannot be negative.");
        }
        if (state.Research.DistinctBy(project => project.Rule).Count() != state.Research.Count)
            throw new InvalidDataException("A base cannot contain duplicate research projects.");
        if (state.Productions.DistinctBy(production => production.Rule).Count() != state.Productions.Count)
            throw new InvalidDataException("A base cannot contain duplicate production projects.");
        if (UsedLaboratories(state) > AvailableLaboratories(state, rules))
            throw new InvalidDataException("Assigned scientists exceed laboratory capacity.");
        if (UsedWorkshopSpace(state, rules) > AvailableWorkshops(state, rules))
            throw new InvalidDataException("Assigned production exceeds workshop capacity.");
        if (!double.IsFinite(state.Longitude) || state.Longitude < 0 || state.Longitude >= 2 * Math.PI)
            throw new InvalidDataException("Base longitude must be in [0, 2π).");
        if (!double.IsFinite(state.Latitude) || state.Latitude < -Math.PI / 2 || state.Latitude > Math.PI / 2)
            throw new InvalidDataException("Base latitude must be in [-π/2, π/2].");
        var occupied = new bool[BaseGridSize, BaseGridSize];
        foreach (var facility in state.Facilities)
        {
            var definition = rules.Facilities[facility.Rule].Value;
            if (facility.X < 0 || facility.Y < 0 || facility.X + definition.SizeX > BaseGridSize ||
                facility.Y + definition.SizeY > BaseGridSize)
                throw new InvalidDataException("A base facility is outside the 6x6 base grid.");
            for (var x = facility.X; x < facility.X + definition.SizeX; x++)
                for (var y = facility.Y; y < facility.Y + definition.SizeY; y++)
                {
                    if (occupied[x, y]) throw new InvalidDataException("Base facilities overlap.");
                    occupied[x, y] = true;
                }
        }
    }

    private static ScriptValueState? RestoreScriptValues(
        RuntimeContent content,
        string ownerName,
        IReadOnlyList<ScriptValueEntry> entries)
    {
        var type = content.Tags.Types.FirstOrDefault(candidate => candidate.Name == ownerName);
        if (type is null)
        {
            if (entries.Count != 0) throw new InvalidDataException($"Script owner type '{ownerName}' is unavailable.");
            return null;
        }
        if (!ScriptValueState.TryRestore(content.Tags, type.Id, entries, out var state, out var diagnostics))
            throw new InvalidDataException(string.Join(Environment.NewLine, diagnostics.Select(static item => item.Message)));
        return state;
    }

    private static List<T> RequiredHistory<T>(IEnumerable<T> source, string name)
    {
        var result = source.ToList();
        if (result.Count == 0 || result.Count > 12)
            throw new InvalidDataException($"Campaign {name} must contain between 1 and 12 entries.");
        return result;
    }

    private static CampaignIdentity ValidateIdentity(CampaignIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.MasterId);
        if (identity.ActiveMods.Count == 0 || !identity.ActiveMods.Contains(identity.MasterId, StringComparer.Ordinal))
            throw new InvalidDataException("The campaign mod list must contain its master.");
        if (identity.ActiveMods.Any(string.IsNullOrWhiteSpace) ||
            identity.ActiveMods.Distinct(StringComparer.Ordinal).Count() != identity.ActiveMods.Count)
            throw new InvalidDataException("The campaign mod list must contain distinct non-empty IDs.");
        return identity with { ActiveMods = CampaignSnapshot.ReadOnly(identity.ActiveMods) };
    }

    private static CampaignDifficulty ValidateDifficulty(CampaignDifficulty difficulty) =>
        Enum.IsDefined(difficulty) ? difficulty : throw new ArgumentOutOfRangeException(nameof(difficulty));

    private static int PositiveId(int value, string kind) => value > 0
        ? value
        : throw new InvalidDataException($"{kind} IDs must be positive.");

    private static void EnsureUnique<T>(IEnumerable<T> values, string name) where T : notnull
    {
        var seen = new HashSet<T>();
        if (values.Any(value => !seen.Add(value))) throw new InvalidDataException($"Duplicate {name} were found.");
    }

    private static void EnsureCraftIds(IEnumerable<CraftSnapshot> values)
    {
        var ids = new HashSet<(string Type, int Id)>();
        foreach (var value in values)
        {
            if (!ids.Add((value.RuleId, value.Id)))
                throw new InvalidDataException("Duplicate craft identity was found.");
        }
    }

    private static void EnsureSoldierIds(IEnumerable<SoldierSnapshot> values)
    {
        var ids = new HashSet<int>();
        foreach (var value in values)
            if (!ids.Add(value.Id)) throw new InvalidDataException("Duplicate soldier ID was found.");
    }

    internal sealed class CountryState(
        RuleHandle<CountryRuleFamily> rule,
        List<int> funding,
        List<int> activityXcom,
        List<int> activityAlien,
        bool pact,
        bool newPact,
        bool cancelPact,
        ScriptValueState? scriptValues)
    {
        public RuleHandle<CountryRuleFamily> Rule { get; } = rule;
        public List<int> Funding { get; } = funding;
        public List<int> ActivityXcom { get; } = activityXcom;
        public List<int> ActivityAlien { get; } = activityAlien;
        public bool Pact { get; set; } = pact;
        public bool NewPact { get; set; } = newPact;
        public bool CancelPact { get; set; } = cancelPact;
        public ScriptValueState? ScriptValues { get; } = scriptValues;
    }

    internal sealed class RegionState(
        RuleHandle<RegionRuleFamily> rule,
        List<int> activityXcom,
        List<int> activityAlien)
    {
        public RuleHandle<RegionRuleFamily> Rule { get; } = rule;
        public List<int> ActivityXcom { get; } = activityXcom;
        public List<int> ActivityAlien { get; } = activityAlien;
    }

    internal sealed class BaseState(
        int id,
        string name,
        double longitude,
        double latitude,
        IEnumerable<FacilityState> facilities,
        IEnumerable<CraftState> crafts,
        IEnumerable<SoldierState> soldiers,
        Dictionary<RuleHandle<ItemRuleFamily>, int> items,
        int scientists,
        int engineers)
    {
        public int Id { get; } = id;
        public string Name { get; set; } = name;
        public double Longitude { get; set; } = longitude;
        public double Latitude { get; set; } = latitude;
        public List<FacilityState> Facilities { get; } = facilities.ToList();
        public List<CraftState> Crafts { get; } = crafts.ToList();
        public List<SoldierState> Soldiers { get; } = soldiers.ToList();
        public Dictionary<RuleHandle<ItemRuleFamily>, int> Items { get; } = items;
        public int Scientists { get; set; } = scientists;
        public int Engineers { get; set; } = engineers;
        public List<TransferSnapshot> Transfers { get; } = [];
        public List<ResearchProjectState> Research { get; } = [];
        public List<ProductionState> Productions { get; } = [];
        public bool IsPlaced => Name.Length != 0;
        public bool FakeUnderwater { get; set; }
        public bool RetaliationTarget { get; set; }
        public int? RetaliationMissionId { get; set; }
    }

    internal sealed record ResearchProjectState(
        RuleHandle<ResearchRuleFamily> Rule, int Assigned, int Spent, int Cost);

    internal sealed record ProductionState(
        RuleHandle<ManufactureRuleFamily> Rule, int Assigned, int Spent, int Amount,
        bool Infinite, bool Sell, bool IsFallback, Dictionary<string, int> RandomProductionInfo);

    internal sealed record FacilityState(
        RuleHandle<FacilityRuleFamily> Rule,
        int X,
        int Y,
        int BuildTime,
        int Ammo,
        bool AmmoMissingReported,
        bool Disabled,
        bool HadPreviousFacility)
    {
        public string PreservationKey { get; init; } = string.Empty;
    }

    internal sealed record CraftState(RuleHandle<CraftRuleFamily> Rule, int Id)
    {
        public ScriptValueState? ScriptValues { get; init; }
        public string PreservationKey { get; init; } = string.Empty;
        public CraftLogisticsState? Logistics { get; init; }
    }

    internal sealed record SoldierState(RuleHandle<SoldierRuleFamily> Rule, int Id)
    {
        public string PreservationKey { get; init; } = string.Empty;
        public SoldierPersonalState? Personal { get; init; }
    }
}
