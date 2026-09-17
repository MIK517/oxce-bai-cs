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
internal delegate string? CampaignTimePreflight(CampaignTime next, CampaignTimeTrigger highest);

/// <summary>
/// Handler order inside one reference time handler (<c>GeoscapeState::time5Seconds</c> ...
/// <c>time1Month</c>). Values are sparse so later capabilities can slot between existing ones;
/// every value must match the order of the corresponding reference statement.
/// </summary>
internal static class CampaignTimeOrder
{
    // time1Month: SavedGame::addMonth runs first.
    public const int MonthCalendar = 100;
    public const int MonthPurchaseLimits = 110;

    // time1Day: GeoscapeState::time1Day base loop (construction, research, soldier recovery and training).
    public const int DayCalendar = 100;
    public const int DayBases = 200;

    // time1Hour: craft servicing loop, transfers, then production.
    public const int HourCraftServicing = 100;
    public const int HourTransfers = 200;
    public const int HourProduction = 300;

    // time30Minutes: craft refuelling in the base loop.
    public const int ThirtyMinutesRefuel = 100;
}

/// <summary>Order of time preflight checks; the first non-null reason stops time.</summary>
internal static class CampaignPreflightOrder
{
    public const int Restrictions = 100;
    public const int WorldSimulation = 150;
    public const int CraftMovement = 200;
    public const int MonthBoundary = 300;
    public const int Servicing = 400;
    public const int ResearchProduction = 500;
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

    public void Preflight(int order, string name, CampaignTimePreflight check)
    {
        ArgumentNullException.ThrowIfNull(check);
        Add(_preflights, new Ordered<CampaignTimePreflight>(order, name, check), "preflight");
    }

    public void Timed(CampaignTimeTrigger trigger, int order, string name, Action<CampaignState.TimeEffects> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!Enum.IsDefined(trigger)) throw new ArgumentOutOfRangeException(nameof(trigger));
        Add(_handlers[(int)trigger], new Ordered<Action<CampaignState.TimeEffects>>(order, name, handler), trigger.ToString());
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
        _queries.ToFrozenDictionary());

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

    internal readonly record struct Ordered<T>(int Order, string Name, T Handler);
}

internal sealed class CampaignHandlerTable(
    FrozenDictionary<Type, Func<ICampaignCommand, CampaignCommandResult>> commands,
    CampaignCapabilityRegistry.Ordered<CampaignTimePreflight>[] preflights,
    CampaignCapabilityRegistry.Ordered<Action<CampaignState.TimeEffects>>[][] handlers,
    Func<CampaignSnapshot, CampaignSnapshot>[] captures,
    Action<CampaignSnapshot>[] restores,
    Action[] validators,
    Action[] initializers,
    FrozenDictionary<Type, object> queries)
{
    public bool TryGetCommand(Type type, out Func<ICampaignCommand, CampaignCommandResult> handler) =>
        commands.TryGetValue(type, out handler!);

    public IReadOnlyCollection<Type> CommandTypes => commands.Keys;

    public string? Preflight(CampaignTime next, CampaignTimeTrigger highest)
    {
        foreach (var check in preflights)
            if (check.Handler(next, highest) is { } reason) return reason;
        return null;
    }

    public void Apply(CampaignTimeTrigger trigger, CampaignState.TimeEffects effects)
    {
        foreach (var handler in handlers[(int)trigger]) handler.Handler(effects);
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
}
