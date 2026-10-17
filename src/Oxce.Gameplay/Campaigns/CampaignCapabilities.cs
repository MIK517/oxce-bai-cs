using System.Collections.Frozen;

namespace Oxce.Gameplay.Campaigns;

/// <summary>
/// A feature slice of <see cref="CampaignState"/>. A capability registers its commands, time
/// handlers, time preflight checks, queries and snapshot pieces. The campaign keeps the single
/// writer, the transaction gate and whole-graph validation; capabilities never lock on their own.
/// </summary>
internal interface ICampaignCapability
{
    void Register(CampaignCapabilityRegistry registry);
}

/// <summary>Adapts a registration method of a partial <see cref="CampaignState"/> file.</summary>
internal sealed class DelegatedCampaignCapability(Action<CampaignCapabilityRegistry> register) : ICampaignCapability
{
    public void Register(CampaignCapabilityRegistry registry) => register(registry);
}

/// <summary>Checks the next tick without mutating state; returns a stop reason or null.</summary>
internal delegate string? CampaignTimePreflight(CampaignTime next, CampaignTimeTrigger highest, CampaignTimeSpeed selectedSpeed);

/// <summary>
/// Handler order inside one reference time handler (<c>GeoscapeState::time5Seconds</c> ...
/// <c>time1Month</c>). Values are sparse so later capabilities can slot between existing ones;
/// every value must match the order of the corresponding reference statement.
/// </summary>
internal static class CampaignTimeOrder
{
    // time5Seconds: UFO processing precedes craft movement.
    public const int FiveSecondsWorldUfoRetarget = 50;
    public const int FiveSecondsCampaignEnding = 60;
    public const int FiveSecondsWorldUfos = 100;
    public const int FiveSecondsWorldCrafts = 200;
    public const int FiveSecondsWorldInterceptionStart = 250;
    public const int FiveSecondsWorldInterceptionCleanup = 290;
    public const int FiveSecondsWorldUfoCleanup = 300;
    public const int FiveSecondsWorldInterceptionWaits = 350;

    // time10Minutes: craft fuel consumption precedes detection and retargeting.
    public const int TenMinutesWorldCraftFuel = 100;
    public const int TenMinutesWorldBaseDetection = 200;
    public const int TenMinutesWorldAlienBaseHunting = 300;
    public const int TenMinutesWorldUfoRetarget = 400;

    // time1Month: SavedGame::addMonth runs first.
    public const int MonthCalendar = 100;
    public const int MonthWorldScheduling = 105;
    public const int MonthPurchaseLimits = 110;

    // time1Day: GeoscapeState::time1Day base loop (construction, research, soldier recovery and training).
    public const int DayCalendar = 100;
    public const int DayBases = 200;
    public const int DayWorldMissionInterruption = 250;
    public const int DayWorldAlienBases = 300;
    public const int DayDogfightExperience = 400;

    // time1Hour: craft servicing loop, transfers, then production.
    public const int HourCraftServicing = 100;
    public const int HourTransfers = 200;
    public const int HourProduction = 300;
    public const int HourWorldSiteDiscovery = 400;

    // time30Minutes: missions, craft refuelling, then UFO activity and detection.
    public const int ThirtyMinutesWorldMissions = 50;
    public const int ThirtyMinutesWorldCrashExpiry = 60;
    public const int ThirtyMinutesRefuel = 100;
    public const int ThirtyMinutesAutoPatrol = 200;
    public const int ThirtyMinutesWorldUfoDetection = 300;
    public const int ThirtyMinutesWorldSites = 400;
    public const int ThirtyMinutesWorldEvents = 500;
}

/// <summary>Order of time preflight checks; the first non-null reason stops time.</summary>
internal static class CampaignPreflightOrder
{
    public const int Restrictions = 100;
    public const int Interceptions = 120;
    public const int Landings = 122;
    public const int DestroyedCrafts = 125;
    public const int WorldSimulation = 150;
    public const int CraftMovement = 200;
    public const int MonthBoundary = 300;
    public const int MonthlyWorldScheduling = 350;
    public const int Servicing = 400;
    public const int ResearchProduction = 500;
    public const int ProductionCraftTargets = 510;
    public const int Transfers = 600;
}

internal sealed class CampaignCapabilityRegistry
{
    private readonly Dictionary<Type, Func<ICampaignCommand, CampaignCommandResult>> _commands = [];
    private readonly List<Ordered<CampaignTimePreflight>> _preflights = [];
    private readonly List<Ordered<Action<CampaignState.TimeEffects>>>[] _handlers =
        Enumerable.Range(0, TriggerCount).Select(static _ => new List<Ordered<Action<CampaignState.TimeEffects>>>()).ToArray();
    private readonly List<Func<CampaignSnapshot, CampaignSnapshot>> _captures = [];
    private readonly List<Action<CampaignSnapshot>> _restores = [];
    private readonly List<Action> _validators = [];
    private readonly List<Action> _initializers = [];
    private readonly Dictionary<Type, object> _queries = [];
    private readonly Dictionary<Type, object> _operations = [];
    private readonly List<Func<ICampaignCommand, CampaignCommandResult?>> _commandGates = [];
    private readonly List<CampaignStagePoint> _stagePoints = [];
    private readonly List<CampaignTransientCopy> _transients = [];

    internal const int TriggerCount = (int)CampaignTimeTrigger.OneMonth + 1;

    public void Command<TCommand>(Func<TCommand, CampaignCommandResult> handler)
        where TCommand : class, ICampaignCommand
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (typeof(TCommand).IsAbstract || typeof(TCommand).IsInterface)
            throw new ArgumentException("Commands are dispatched by their exact concrete type.", nameof(handler));
        if (!_commands.TryAdd(typeof(TCommand), command => handler((TCommand)command)))
            throw new InvalidOperationException($"Command '{typeof(TCommand).Name}' has more than one handler.");
    }

    /// <summary>
    /// A check every command passes before its handler; a non-null result refuses the command. Used for
    /// campaign-wide holds such as a pending deployment request.
    /// </summary>
    public void CommandGate(Func<ICampaignCommand, CampaignCommandResult?> gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        _commandGates.Add(gate);
    }

    public void Preflight(int order, string name, CampaignTimePreflight check)
    {
        ArgumentNullException.ThrowIfNull(check);
        Add(_preflights, new Ordered<CampaignTimePreflight>(order, name, check, CampaignTimeProfile.PreflightPrefix + name), "preflight");
    }

    public void Preflight(int order, string name, Func<CampaignTime, CampaignTimeTrigger, string?> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        Preflight(order, name, (next, highest, _) => check(next, highest));
    }

    public void Timed(CampaignTimeTrigger trigger, int order, string name, Action<CampaignState.TimeEffects> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!Enum.IsDefined(trigger)) throw new ArgumentOutOfRangeException(nameof(trigger));
        Add(_handlers[(int)trigger], new Ordered<Action<CampaignState.TimeEffects>>(order, name, handler, CampaignTimeProfile.ApplyPrefix + name), trigger.ToString());
    }

    /// <summary>
    /// A stage point (ADR 0035): when <paramref name="applies"/> holds for the next tick, the rest of that tick after
    /// <paramref name="level"/> is preflighted on a shadow copy that has run the handlers down to that level.
    /// </summary>
    public void StagePoint(CampaignTimeTrigger level, string name, Func<CampaignTime, CampaignTimeTrigger, bool> applies)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(applies);
        if (level is <= CampaignTimeTrigger.FiveSeconds or > CampaignTimeTrigger.OneMonth)
            throw new ArgumentOutOfRangeException(nameof(level), "A stage point needs a later level to check.");
        _stagePoints.Add(new(level, name, applies));
    }

    /// <summary>
    /// Copies state that snapshots drop (open windows, queues) into the same capability of a shadow copy
    /// (ADR 0035). The shadow only runs handlers above five seconds and preflights; it never changes what it
    /// shares with the original.
    /// </summary>
    public void Transient<TCapability>(string name, TCapability owner, Action<TCapability, TCapability> copyTo) where TCapability : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(copyTo);
        if (_transients.Any(existing => existing.Name == name))
            throw new InvalidOperationException($"The transient copy '{name}' is registered twice.");
        _transients.Add(new(name, owner, (source, shadow) => copyTo((TCapability)source, (TCapability)shadow)));
    }

    /// <summary>Adds this capability's owned state to a captured snapshot.</summary>
    public void Capture(Func<CampaignSnapshot, CampaignSnapshot> contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        _captures.Add(contributor);
    }

    /// <summary>Restores owned state after the base graph exists and before the campaign is published.</summary>
    public void Restore(Action<CampaignSnapshot> restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        _restores.Add(restore);
    }

    /// <summary>Validates owned state against the complete restored graph.</summary>
    public void Validate(Action validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        _validators.Add(validate);
    }

    /// <summary>Initializes owned state for a newly created campaign.</summary>
    public void Initialize(Action initialize)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        _initializers.Add(initialize);
    }

    public void Query<TQuery>(TQuery implementation) where TQuery : class
    {
        ArgumentNullException.ThrowIfNull(implementation);
        if (!typeof(TQuery).IsInterface) throw new ArgumentException("Queries are published through interfaces.");
        if (!_queries.TryAdd(typeof(TQuery), implementation))
            throw new InvalidOperationException($"Query '{typeof(TQuery).Name}' has more than one implementation.");
    }

    internal CampaignHandlerTable Build() => new(
        _commands.ToFrozenDictionary(),
        Sorted(_preflights),
        _handlers.Select(Sorted).ToArray(),
        [.. _captures],
        [.. _restores],
        [.. _validators],
        [.. _initializers],
        _queries.ToFrozenDictionary(),
        _operations.ToFrozenDictionary(),
        [.. _commandGates],
        [.. _stagePoints],
        [.. _transients]);

    /// <summary>Internal operations run only within the caller's existing campaign transaction.</summary>
    public void Operation<TOperation>(TOperation implementation) where TOperation : class
    {
        ArgumentNullException.ThrowIfNull(implementation);
        if (!typeof(TOperation).IsInterface || !_operations.TryAdd(typeof(TOperation), implementation))
            throw new InvalidOperationException("An operation must have a unique interface registration.");
    }

    private static void Add<T>(List<Ordered<T>> target, Ordered<T> entry, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Name);
        if (target.Any(existing => existing.Order == entry.Order))
            throw new InvalidOperationException($"Two {scope} handlers use order {entry.Order}; ordering must be explicit.");
        if (target.Any(existing => existing.Name == entry.Name))
            throw new InvalidOperationException($"The {scope} handler '{entry.Name}' is registered twice.");
        target.Add(entry);
    }

    private static Ordered<T>[] Sorted<T>(List<Ordered<T>> source) =>
        [.. source.OrderBy(static entry => entry.Order)];

    /// <param name="ProfileName">The name <see cref="CampaignTimeProfile"/> reports, built once at registration.</param>
    internal readonly record struct Ordered<T>(int Order, string Name, T Handler, string ProfileName = "");
}

internal sealed record CampaignStagePoint(CampaignTimeTrigger Level, string Name, Func<CampaignTime, CampaignTimeTrigger, bool> Applies);

internal sealed record CampaignTransientCopy(string Name, object Owner, Action<object, object> CopyTo);

internal sealed class CampaignHandlerTable(
    FrozenDictionary<Type, Func<ICampaignCommand, CampaignCommandResult>> commands,
    CampaignCapabilityRegistry.Ordered<CampaignTimePreflight>[] preflights,
    CampaignCapabilityRegistry.Ordered<Action<CampaignState.TimeEffects>>[][] handlers,
    Func<CampaignSnapshot, CampaignSnapshot>[] captures,
    Action<CampaignSnapshot>[] restores,
    Action[] validators,
    Action[] initializers,
    FrozenDictionary<Type, object> queries,
    FrozenDictionary<Type, object> operations,
    Func<ICampaignCommand, CampaignCommandResult?>[] commandGates,
    CampaignStagePoint[] stagePoints,
    CampaignTransientCopy[] transients)
{
    /// <summary>The stage points of the next tick as a bit per trigger level (ADR 0035).</summary>
    public int StagePoints(CampaignTime next, CampaignTimeTrigger highest)
    {
        var points = 0;
        foreach (var point in stagePoints)
            if (point.Level <= highest && point.Applies(next, highest)) points |= 1 << (int)point.Level;
        return points;
    }

    private readonly CampaignTransientCopy[] _transients = transients;

    /// <summary>Copies transient state into a shadow built from the same registrations.</summary>
    public void CopyTransientTo(CampaignHandlerTable shadow)
    {
        if (shadow._transients.Length != _transients.Length)
            throw new InvalidOperationException("A shadow campaign must have the same transient registrations.");
        for (var index = 0; index < _transients.Length; index++)
        {
            if (shadow._transients[index].Name != _transients[index].Name ||
                shadow._transients[index].Owner.GetType() != _transients[index].Owner.GetType())
                throw new InvalidOperationException("A shadow campaign must have the same transient registrations.");
            _transients[index].CopyTo(_transients[index].Owner, shadow._transients[index].Owner);
        }
    }

    /// <summary>The first gate refusal for the command, or null.</summary>
    public CampaignCommandResult? CommandGate(ICampaignCommand command)
    {
        foreach (var gate in commandGates)
            if (gate(command) is { } refused) return refused;
        return null;
    }

    public bool TryGetCommand(Type type, out Func<ICampaignCommand, CampaignCommandResult> handler) =>
        commands.TryGetValue(type, out handler!);

    public IReadOnlyCollection<Type> CommandTypes => commands.Keys;

    public string? Preflight(CampaignTime next, CampaignTimeTrigger highest,
        CampaignTimeSpeed selectedSpeed = CampaignTimeSpeed.FiveSeconds, CampaignTimeProfile? profile = null)
    {
        if (profile is not null) return ProfiledPreflight(next, highest, selectedSpeed, profile);
        foreach (var check in preflights)
            if (check.Handler(next, highest, selectedSpeed) is { } reason) return reason;
        return null;
    }

    public void Apply(CampaignTimeTrigger trigger, CampaignState.TimeEffects effects, CampaignTimeProfile? profile = null)
    {
        if (profile is not null)
        {
            ProfiledApply(trigger, effects, profile);
            return;
        }
        foreach (var handler in handlers[(int)trigger]) handler.Handler(effects);
    }

    private string? ProfiledPreflight(CampaignTime next, CampaignTimeTrigger highest, CampaignTimeSpeed selectedSpeed,
        CampaignTimeProfile profile)
    {
        foreach (var check in preflights)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var reason = check.Handler(next, highest, selectedSpeed);
            profile.Record(check.ProfileName, started);
            if (reason is not null) return reason;
        }
        return null;
    }

    private void ProfiledApply(CampaignTimeTrigger trigger, CampaignState.TimeEffects effects, CampaignTimeProfile profile)
    {
        foreach (var handler in handlers[(int)trigger])
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            handler.Handler(effects);
            profile.Record(handler.ProfileName, started);
        }
    }

    public IReadOnlyList<string> HandlerNames(CampaignTimeTrigger trigger) =>
        [.. handlers[(int)trigger].Select(static handler => handler.Name)];

    public IReadOnlyList<string> PreflightNames => [.. preflights.Select(static check => check.Name)];

    public CampaignSnapshot Capture(CampaignSnapshot snapshot)
    {
        foreach (var capture in captures) snapshot = capture(snapshot);
        return snapshot;
    }

    public void Restore(CampaignSnapshot snapshot)
    {
        foreach (var restore in restores) restore(snapshot);
    }

    public void Validate()
    {
        foreach (var validate in validators) validate();
    }

    public void Initialize()
    {
        foreach (var initialize in initializers) initialize();
    }

    public TQuery? Query<TQuery>() where TQuery : class =>
        queries.TryGetValue(typeof(TQuery), out var query) ? (TQuery)query : null;

    internal TOperation Operation<TOperation>() where TOperation : class =>
        (TOperation)operations[typeof(TOperation)];
}
