using System.Collections.ObjectModel;
using Oxce.Core.Random;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Campaigns;

public sealed class CampaignState : ICampaignCommandTarget, ICampaignQuery
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
    private ScriptValueState? _scriptValues;

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
        ValidateState();
    }

    public CampaignIdentity Identity { get; private set; }
    public CampaignDifficulty Difficulty { get; }
    public CampaignTime Time { get; private set; }
    public int Ending { get; }
    public int MonthsPassed { get; private set; }
    public int DaysPassed { get; private set; }

    public CampaignCommandResult Execute(ICampaignCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_transactionGate)
        {
            return command switch
            {
                AdvanceCampaignTime advance => Advance(advance),
                PlaceStartingBase place => Place(place),
                _ => throw new ArgumentException($"Unsupported campaign command '{command.GetType().Name}'.",
                    nameof(command)),
            };
        }
    }

    public CampaignSnapshot Capture()
    {
        lock (_transactionGate) return CaptureCore();
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

    private CampaignSnapshot CaptureCore() => new(
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
            CampaignSnapshot.ReadOnly(_scriptValues?.Capture() ?? []));

    public static CampaignState Restore(
        CampaignSnapshot snapshot,
        RuntimeContent content,
        IStatefulRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
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
        var bases = snapshot.Bases.Select(source => RestoreBase(source, rules)).ToArray();
        EnsureUnique(snapshot.Countries.Select(static country => country.RuleId), "country rule IDs");
        EnsureUnique(snapshot.Regions.Select(static region => region.RuleId), "region rule IDs");
        EnsureCraftIds(snapshot.Bases.SelectMany(static item => item.Crafts));
        EnsureSoldierIds(snapshot.Bases.SelectMany(static item => item.Soldiers));

        var previousRandomState = random.State;
        try
        {
            random.Restore(snapshot.RandomState);
            return new CampaignState(
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
                RestoreScriptValues(content, "GeoscapeGame", snapshot.ScriptValues));
        }
        catch
        {
            random.Restore(previousRandomState);
            throw;
        }
    }

    internal int NextId(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_nextIds.TryGetValue(name, out var next)) next = 1;
        _nextIds[name] = checked(next + 1);
        return next;
    }

    private CampaignCommandResult Advance(AdvanceCampaignTime command)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(command.FiveSecondTicks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(command.FiveSecondTicks, MaximumCommandTicks);
        var previous = Time;
        var fiveSeconds = 0;
        var tenMinutes = 0;
        var thirtyMinutes = 0;
        var oneHour = 0;
        var oneDay = 0;
        var oneMonth = 0;
        for (var index = 0; index < command.FiveSecondTicks; index++)
        {
            Time = Time.Advance(out var trigger);
            switch (trigger)
            {
                case CampaignTimeTrigger.FiveSeconds: fiveSeconds++; break;
                case CampaignTimeTrigger.TenMinutes: tenMinutes++; break;
                case CampaignTimeTrigger.ThirtyMinutes: thirtyMinutes++; break;
                case CampaignTimeTrigger.OneHour: oneHour++; break;
                case CampaignTimeTrigger.OneDay: oneDay++; DaysPassed++; break;
                case CampaignTimeTrigger.OneMonth: oneMonth++; MonthsPassed++; break;
                default: throw new InvalidOperationException($"Unknown campaign time trigger '{trigger}'.");
            }
        }
        var summary = new CampaignTimeTriggerSummary(
            command.FiveSecondTicks, fiveSeconds, tenMinutes, thirtyMinutes, oneHour, oneDay, oneMonth);
        return new CampaignCommandResult([new CampaignTimeAdvanced(previous, Time, summary)]);
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
        target.Name = command.Name;
        target.Longitude = command.Longitude;
        target.Latitude = command.Latitude;
        return new CampaignCommandResult(
            [new StartingBasePlaced(command.BaseIndex, command.Name, command.Longitude, command.Latitude)]);
    }

    private void ValidateState()
    {
        RequiredHistory(_funds, "funds");
        RequiredHistory(_maintenance, "maintenance");
        RequiredHistory(_incomes, "incomes");
        RequiredHistory(_expenditures, "expenditures");
        RequiredHistory(_researchScores, "research scores");
        if (MonthsPassed < -1) throw new ArgumentOutOfRangeException(nameof(MonthsPassed));
        if (DaysPassed < 0) throw new ArgumentOutOfRangeException(nameof(DaysPassed));
        if (_bases.Count == 0) throw new InvalidDataException("A campaign must contain at least one base.");
        EnsureUnique(_bases.Select(static item => item.Id), "base IDs");
        foreach (var pair in _nextIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
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
            facility.HadPreviousFacility))),
        CampaignSnapshot.ReadOnly(state.Crafts.Select(craft =>
            new CraftSnapshot(_content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id))),
        CampaignSnapshot.ReadOnly(state.Soldiers.Select(soldier =>
            new SoldierSnapshot(_content.RuntimeRules.Soldiers.GetExternalId(soldier.Rule), soldier.Id))),
        new ReadOnlyDictionary<string, int>(state.Items.ToDictionary(
            item => _content.RuntimeRules.Items.GetExternalId(item.Key), static item => item.Value,
            StringComparer.Ordinal)),
        state.Scientists,
        state.Engineers);

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

    private static BaseState RestoreBase(BaseSnapshot source, RuntimeRuleCatalog rules)
    {
        if (source.Id < 0) throw new InvalidDataException("Base IDs cannot be negative.");
        if (!double.IsFinite(source.Longitude) || !double.IsFinite(source.Latitude))
            throw new InvalidDataException("Base coordinates must be finite.");
        var facilities = source.Facilities.Select(facility => new FacilityState(
            rules.Facilities.GetRequired(facility.RuleId), facility.X, facility.Y, facility.BuildTime,
            facility.Ammo, facility.AmmoMissingReported, facility.Disabled, facility.HadPreviousFacility)).ToArray();
        var crafts = source.Crafts.Select(craft => new CraftState(
            rules.Crafts.GetRequired(craft.RuleId), PositiveId(craft.Id, "craft"))).ToArray();
        var soldiers = source.Soldiers.Select(soldier => new SoldierState(
            rules.Soldiers.GetRequired(soldier.RuleId), PositiveId(soldier.Id, "soldier"))).ToArray();
        var items = new Dictionary<RuleHandle<ItemRuleFamily>, int>();
        foreach (var pair in source.Items)
        {
            if (pair.Value <= 0) throw new InvalidDataException($"Item '{pair.Key}' has a non-positive quantity.");
            items.Add(rules.Items.GetRequired(pair.Key), pair.Value);
        }
        return new BaseState(source.Id, source.Name, source.Longitude, source.Latitude, facilities, crafts,
            soldiers, items, source.Scientists, source.Engineers);
    }

    private static void ValidateBase(BaseState state, RuntimeRuleCatalog rules)
    {
        if (state.Scientists < 0 || state.Engineers < 0)
            throw new InvalidDataException("Base personnel counts cannot be negative.");
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
        public bool Pact { get; } = pact;
        public bool NewPact { get; } = newPact;
        public bool CancelPact { get; } = cancelPact;
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
        public int Scientists { get; } = scientists;
        public int Engineers { get; } = engineers;
        public bool IsPlaced => Name.Length != 0;
    }

    internal sealed record FacilityState(
        RuleHandle<FacilityRuleFamily> Rule,
        int X,
        int Y,
        int BuildTime,
        int Ammo,
        bool AmmoMissingReported,
        bool Disabled,
        bool HadPreviousFacility);

    internal sealed record CraftState(RuleHandle<CraftRuleFamily> Rule, int Id);

    internal sealed record SoldierState(RuleHandle<SoldierRuleFamily> Rule, int Id);
}
