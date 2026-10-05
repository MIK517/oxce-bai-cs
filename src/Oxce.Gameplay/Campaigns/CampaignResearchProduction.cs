using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed record ConfigureResearchProject(
    int BaseId, string RuleId, int AssignedScientists, bool Cancel = false) : ICampaignCommand;
public sealed record ConfigureProductionProject(
    int BaseId, string RuleId, int AssignedEngineers, int Amount,
    bool Infinite = false, bool Sell = false, bool Cancel = false) : ICampaignCommand;
public sealed record CampaignResearchChanged(int BaseId, string RuleId, int Assigned, bool Removed) : ICampaignEvent;
public sealed record CampaignResearchCompleted(
    int BaseId, string RuleId, IReadOnlyList<string> Discoveries) : ICampaignEvent;
public sealed record CampaignProductionChanged(
    int BaseId, string RuleId, int Assigned, int Amount, bool Removed) : ICampaignEvent;
public sealed record CampaignProductionProgress(
    int BaseId, string RuleId, int Produced, string? StopReason) : ICampaignEvent;
public sealed record CampaignStrategicEventRequested(string RuleId, string Source) : ICampaignEvent;

public interface ICampaignResearchProductionQuery
{
    CampaignResearchProduction QueryResearchProduction(int baseId);
}

/// <summary>Read-only proof for guards that run before the daily research handler.</summary>
internal interface ICampaignResearchPreflight
{
    IReadOnlySet<string> GuaranteedResearchAfterDailyProgress();
    IReadOnlySet<string> PossibleResearchAfterDailyProgress();
    bool TryForecastDailyStocks(ref long work, out DailyResearchStockForecast forecast);
    bool HasDisabledDailyProjects(ref long work);
}

/// <summary>World arcs use SavedGame::addFinishedResearch, without daily project side effects.</summary>
internal interface ICampaignArcResearch
{
    bool IsResearched(string id);
    string? GrantReason(string id);
    string Grant(string id);
    bool ScoreCapacityFits(IReadOnlyList<IReadOnlySet<string>> possibleGrantSlots);
    IReadOnlySet<string> PossibleDisables(string id);
    IReadOnlySet<string> PossibleAutomaticResearch();
    string? Lookup(string id);
}

public sealed record CampaignResearchChoice(string RuleId, int Cost, string? UnavailableReason);
public sealed record CampaignResearchProject(string RuleId, int Assigned, int Spent, int Cost, string Progress);
public sealed record CampaignProductionChoice(string RuleId, int Time, int Cost, int Space, string? UnavailableReason);
public sealed record CampaignProductionProject(
    string RuleId, int Assigned, int Spent, int Amount, int Produced, bool Infinite, bool Sell);
public sealed record CampaignResearchProduction(
    int ScientistsAvailable, int LaboratoriesAvailable, int EngineersAvailable, int WorkshopsAvailable,
    IReadOnlyList<CampaignResearchChoice> ResearchChoices, IReadOnlyList<CampaignResearchProject> Research,
    IReadOnlyList<CampaignProductionChoice> ProductionChoices, IReadOnlyList<CampaignProductionProject> Productions);

public sealed partial class CampaignState : ICampaignResearchProductionQuery, ICampaignResearchPreflight, ICampaignArcResearch, ICampaignProductionPreflight
{
    private void RegisterResearchProduction(CampaignCapabilityRegistry registry)
    {
        registry.Operation<ICampaignResearchPreflight>(this);
        registry.Operation<ICampaignProductionPreflight>(this);
        registry.Operation<ICampaignArcResearch>(this);
        registry.Operation<ICampaignCustomCounters>(this);
        registry.Command<ConfigureResearchProject>(ConfigureResearch);
        registry.Command<ConfigureProductionProject>(ConfigureProduction);
        registry.Preflight(CampaignPreflightOrder.ResearchProduction, "research and production",
            (_, highest) => PreflightResearchProduction(highest));
        registry.Timed(CampaignTimeTrigger.OneHour, CampaignTimeOrder.HourProduction, "production", AdvanceProductionHourly);
        registry.Capture(snapshot => snapshot with
        {
            CompletedResearch = CampaignSnapshot.ReadOnly(_completedResearch.Order(StringComparer.Ordinal)),
            ResearchRuleStatus = CampaignSnapshot.ReadOnlyIds(_researchRuleStatus),
            ManufactureRuleStatus = CampaignSnapshot.ReadOnlyIds(_manufactureRuleStatus),
        });
        registry.Restore(snapshot =>
        {
            _completedResearch = snapshot.CompletedResearch.ToHashSet(StringComparer.Ordinal);
            _researchRuleStatus = new Dictionary<string, int>(snapshot.ResearchRuleStatus, StringComparer.Ordinal);
            _manufactureRuleStatus = new Dictionary<string, int>(snapshot.ManufactureRuleStatus, StringComparer.Ordinal);
        });
    }

    private bool? _researchEventWeightsFit;

    internal bool HasCompletedResearch(string id) => _completedResearch.Contains(id);

    internal ICampaignResearchPreflight ResearchPreflight => _handlers.Operation<ICampaignResearchPreflight>();

    internal ICampaignArcResearch ArcResearch => _handlers.Operation<ICampaignArcResearch>();

    bool ICampaignArcResearch.IsResearched(string id) => _debugMode || _completedResearch.Contains(id);

    string? ICampaignArcResearch.GrantReason(string id)
    {
        if (_bases.Count == 0) return "Arc research requires a headquarters base.";
        return _content.RuntimeRules.Research.TryGet(id, out _) ? null : $"Required arc research '{id}' does not exist.";
    }

    string ICampaignArcResearch.Grant(string id)
    {
        var rule = _content.RuntimeRules.Research[_content.RuntimeRules.Research.GetRequired(id)].Value;
        var discoveries = new List<string>();
        AddFinishedResearch(_bases[0], id, discoveries);
        if (rule.Lookup.Length == 0 || !_content.RuntimeRules.Research.TryGet(rule.Lookup, out _)) return id;
        AddFinishedResearch(_bases[0], rule.Lookup, discoveries);
        return rule.Lookup;
    }

    string? ICampaignArcResearch.Lookup(string id) =>
        _content.RuntimeRules.Research.TryGet(id, out var handle) &&
        _content.RuntimeRules.Research[handle].Value.Lookup.Length != 0
            ? _content.RuntimeRules.Research[handle].Value.Lookup : null;

    IReadOnlySet<string> ICampaignArcResearch.PossibleDisables(string id) =>
        _content.RuntimeRules.Research.TryGet(id, out var handle)
            ? _content.RuntimeRules.Research[handle].Value.Disables.ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

    IReadOnlySet<string> ICampaignArcResearch.PossibleAutomaticResearch() =>
        _content.RuntimeRules.Research.Rules.Where(entry => entry.Value.Cost == 0)
            .Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);

    bool ICampaignArcResearch.ScoreCapacityFits(IReadOnlyList<IReadOnlySet<string>> possibleGrantSlots)
    {
        // Each slot grants at most one candidate. A direct grant and its optional
        // lookup each run an automatic zero-cost chain; bound both score signs.
        long automaticPositive = 0;
        long automaticNegative = 0;
        foreach (var entry in _content.RuntimeRules.Research.Rules)
            if (entry.Value.Cost == 0)
            {
                automaticPositive += Math.Max(0, (long)entry.Value.Points);
                automaticNegative += Math.Min(0, (long)entry.Value.Points);
            }
        long positive = 0;
        long negative = 0;
        foreach (var slot in possibleGrantSlots)
        {
            long slotPositive = 0;
            long slotNegative = 0;
            foreach (var id in slot)
            {
                var rule = _content.RuntimeRules.Research[_content.RuntimeRules.Research.GetRequired(id)].Value;
                var candidatePositive = Math.Max(0, (long)rule.Points) + automaticPositive;
                var candidateNegative = Math.Min(0, (long)rule.Points) + automaticNegative;
                if (rule.Lookup.Length != 0 && _content.RuntimeRules.Research.TryGet(rule.Lookup, out var lookup))
                {
                    var linked = _content.RuntimeRules.Research[lookup].Value;
                    candidatePositive += Math.Max(0, (long)linked.Points) + automaticPositive;
                    candidateNegative += Math.Min(0, (long)linked.Points) + automaticNegative;
                }
                slotPositive = Math.Max(slotPositive, candidatePositive);
                slotNegative = Math.Min(slotNegative, candidateNegative);
            }
            positive += slotPositive;
            negative += slotNegative;
        }
        var score = (long)_researchScores[^1];
        return positive <= int.MaxValue - score && negative >= int.MinValue - score;
    }

    IReadOnlySet<string> ICampaignResearchPreflight.GuaranteedResearchAfterDailyProgress()
    {
        var rules = _content.RuntimeRules.Research;
        var finishing = FinishingResearchTopics();
        if (finishing.Count == 0) return _completedResearch;

        var possible = DailyResearchPossibilities(finishing);
        var possiblyDisabled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in possible)
            if (rules.TryGet(id, out var handle)) possiblyDisabled.UnionWith(rules[handle].Value.Disables);

        var guaranteed = new HashSet<string>(_completedResearch, StringComparer.Ordinal);
        foreach (var id in finishing)
        {
            AddGuaranteed(id);
            AddGuaranteed(rules[rules.GetRequired(id)].Value.Lookup);
        }
        guaranteed.ExceptWith(possiblyDisabled);
        return guaranteed;

        void AddGuaranteed(string id)
        {
            // Repeatable research is not marked discovered, and disabled topics are ignored.
            // Re-enabling and random/automatic discoveries can only add uncertain outcomes;
            // they must not authorize an otherwise unavailable event payload tick.
            if (rules.TryGet(id, out var handle) && !rules[handle].Value.Repeatable &&
                _researchRuleStatus.GetValueOrDefault(id) != 2)
                guaranteed.Add(id);
        }
    }

    IReadOnlySet<string> ICampaignResearchPreflight.PossibleResearchAfterDailyProgress()
    {
        var finishing = FinishingResearchTopics();
        if (finishing.Count == 0) return _completedResearch;
        var possible = DailyResearchPossibilities(finishing);
        possible.UnionWith(_completedResearch);
        return possible;
    }

    private HashSet<string> FinishingResearchTopics()
    {
        var finishing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owner in _bases)
            foreach (var project in owner.Research)
                if ((long)project.Spent + project.Assigned >= project.Cost)
                    finishing.Add(_content.RuntimeRules.Research.GetExternalId(project.Rule));
        return finishing;
    }

    private HashSet<string> DailyResearchPossibilities(IReadOnlySet<string> finishing)
    {
        var rules = _content.RuntimeRules.Research;
        // CompleteResearch applies a possible free topic and its lookup before the primary
        // topic and its lookup. AddFinishedResearch can also discover zero-cost topics.
        // Include all such possible disablers without choosing rewards or consuming RNG.
        var possible = new HashSet<string>(finishing, StringComparer.Ordinal);
        foreach (var id in finishing)
        {
            var rule = rules[rules.GetRequired(id)].Value;
            possible.UnionWith(rule.GetOneFree);
            foreach (var group in rule.GetOneFreeProtected) possible.UnionWith(group.Topics);
        }
        var topics = possible.ToArray();
        foreach (var id in topics)
            if (rules.TryGet(id, out var handle) && rules[handle].Value.Lookup.Length != 0)
                possible.Add(rules[handle].Value.Lookup);
        foreach (var entry in rules.Rules)
            if (entry.Value.Cost == 0) possible.Add(entry.Id);
        return possible;
    }

    private void ScheduleStrategicEvent(string ruleId, string source, TimeEffects effects)
    {
        _handlers.Operation<World.ICampaignEventScheduling>().Schedule(ruleId);
        effects.Notify(new CampaignStrategicEventRequested(ruleId, source));
    }

    private CampaignCommandResult ConfigureResearch(ConfigureResearchProject command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RuleId);
        ArgumentOutOfRangeException.ThrowIfNegative(command.AssignedScientists);
        var owner = FindBase(command.BaseId);
        if (FacilityActionRestriction(owner) is { } restriction) return Blocked(restriction);
        var handle = _content.RuntimeRules.Research.GetRequired(command.RuleId);
        var index = owner.Research.FindIndex(project => project.Rule == handle);
        if (command.Cancel)
        {
            if (index < 0) return Blocked("Research project was not found.");
            var project = owner.Research[index];
            if (ResearchRecoveryReason(owner, project) is { } recoveryReason) return Blocked(recoveryReason);
            RemoveResearchProject(owner, project);
            return new([new CampaignResearchChanged(owner.Id, command.RuleId, 0, true)]);
        }
        if (index < 0)
        {
            if (ResearchUnavailable(owner, handle, direct: true) is { } reason) return Blocked(reason);
            var rule = _content.RuntimeRules.Research[handle].Value;
            if (command.AssignedScientists > owner.Scientists ||
                command.AssignedScientists > FreeLaboratories(owner))
                return Blocked("STR_NOT_ENOUGH_LAB_SPACE");
            RuleHandle<ItemRuleFamily>? heldItem = null;
            if (HoldsResearchItem(rule))
            {
                if (rule.NeededItem is null || !_content.RuntimeRules.Items.TryGet(rule.NeededItem, out var item) ||
                    owner.Items.GetValueOrDefault(item) <= 0) return Blocked("Required research item is unavailable.");
                heldItem = item;
            }
            // ResearchInfoState: cost * RNG(50, 150) / 100, at least 1 for positive costs.
            var cost = (long)rule.Cost * _random.NextInclusive(50, 150) / 100;
            if (rule.Cost > 0) cost = Math.Max(1, cost);
            if (cost > int.MaxValue) return Blocked("Research cost exceeds the supported range.");
            if (heldItem is { } held) ChangeStock(owner.Items, held, -1);
            owner.Research.Add(new(handle, 0, 0, (int)cost));
            index = owner.Research.Count - 1;
        }
        var current = owner.Research[index];
        if (!TryReassignProjectStaff(command.AssignedScientists, current.Assigned, owner.Scientists,
            FreeLaboratories(owner) + (long)current.Assigned, out var remainingScientists))
            return Blocked("STR_NOT_ENOUGH_LAB_SPACE");
        owner.Scientists = remainingScientists;
        owner.Research[index] = current with { Assigned = command.AssignedScientists };
        return new([new CampaignResearchChanged(owner.Id, command.RuleId, command.AssignedScientists, false)]);
    }

    private CampaignCommandResult ConfigureProduction(ConfigureProductionProject command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.RuleId);
        ArgumentOutOfRangeException.ThrowIfNegative(command.AssignedEngineers);
        ArgumentOutOfRangeException.ThrowIfNegative(command.Amount);
        var owner = FindBase(command.BaseId);
        if (FacilityActionRestriction(owner) is { } restriction) return Blocked(restriction);
        var handle = _content.RuntimeRules.Manufacture.GetRequired(command.RuleId);
        var index = owner.Productions.FindIndex(project => project.Rule == handle);
        var rule = _content.RuntimeRules.Manufacture[handle].Value;
        if (command.Cancel)
        {
            if (index < 0) return Blocked("Production queue was not found.");
            var project = owner.Productions[index];
            var refundAccounting = default(AccountingState);
            if (rule.Refund && !TryStageAccounting(rule.Cost, out refundAccounting))
                return Blocked("Production accounting exceeds the supported range.");
            owner.Engineers = checked(owner.Engineers + project.Assigned);
            if (rule.Refund) RefundProductionUnit(owner, rule, refundAccounting);
            owner.Productions.RemoveAt(index);
            return new([new CampaignProductionChanged(owner.Id, command.RuleId, 0, project.Amount, true)]);
        }
        var isNew = index < 0;
        if (isNew)
        {
            if (command.Amount == 0 && !command.Infinite) return Blocked("Production amount must be positive.");
            if (ProductionUnavailable(owner, handle) is { } reason) return Blocked(reason);
        }
        // A new project is planned as an empty queue entry; every check below runs before any mutation
        // because rejected commands are not rolled back.
        var current = isNew
            ? new ProductionState(handle, 0, 0, 0, false, false, false, new(StringComparer.Ordinal))
            : owner.Productions[index];
        var nextAmount = command.Infinite ? Math.Max(1, command.Amount) : command.Amount;
        if (!command.Infinite && nextAmount <= Produced(current))
            return Blocked("Production amount cannot be less than completed units.");
        // ManufactureInfoState::moreEngineer starts counting the required space once staff is assigned.
        var occupiesSpace = command.AssignedEngineers > 0 || current.Spent > 0;
        var workshopCapacity = AvailableWorkshops(owner) - (long)UsedWorkshopSpace(owner, current) -
            (occupiesSpace ? rule.Space : 0);
        if (!TryReassignProjectStaff(command.AssignedEngineers, current.Assigned, owner.Engineers,
            workshopCapacity, out var remainingEngineers))
            return Blocked("STR_NOT_ENOUGH_WORK_SPACE");
        if (ProducedCraft(rule) is { } craft)
        {
            // Crafts cannot be queued infinitely and each additional unit reserves one hangar.
            if (command.Infinite) return Blocked("Craft production cannot be infinite.");
            var hangarType = _content.RuntimeRules.Crafts[craft].Value.HangarType;
            if (nextAmount > current.Amount && nextAmount - (long)current.Amount >
                AvailableHangars(owner, hangarType) - (long)UsedHangars(owner, hangarType))
                return Blocked("STR_NO_FREE_HANGARS_FOR_CRAFT_PRODUCTION");
        }
        if (command.Sell && !current.Sell && !CanAutoSell(rule))
            return Blocked("Production does not support autosell.");
        if (isNew && command.Sell && ProductionSellUnavailable(rule) is { } sellReason) return Blocked(sellReason);
        if (isNew && StartProductionUnit(owner, rule, initial: true) is { } startReason) return Blocked(startReason);
        owner.Engineers = remainingEngineers;
        var updated = current with
        {
            Assigned = command.AssignedEngineers,
            Amount = nextAmount,
            Infinite = command.Infinite,
            Sell = command.Sell,
        };
        if (isNew) owner.Productions.Add(updated);
        else owner.Productions[index] = updated;
        return new([new CampaignProductionChanged(owner.Id, command.RuleId,
            command.AssignedEngineers, nextAmount, false)]);
    }

    public CampaignResearchProduction QueryResearchProduction(int baseId)
    {
        lock (_transactionGate)
        {
            var owner = FindBase(baseId);
            var unlocked = ExplicitlyUnlockedResearch();
            var research = _content.RuntimeRules.Research.Rules
                .OrderBy(rule => rule.Value.ListOrder).ThenBy(rule => rule.Id, StringComparer.Ordinal)
                .Select(rule => new CampaignResearchChoice(rule.Id, rule.Value.Cost,
                    ResearchUnavailable(owner, _content.RuntimeRules.Research.GetRequired(rule.Id), direct: true, unlocked)))
                .ToArray();
            var manufacture = _content.RuntimeRules.Manufacture.Rules
                .OrderBy(rule => rule.Value.ListOrder).ThenBy(rule => rule.Id, StringComparer.Ordinal)
                .Select(rule => new CampaignProductionChoice(rule.Id, rule.Value.Time, rule.Value.Cost, rule.Value.Space,
                    ProductionUnavailable(owner, _content.RuntimeRules.Manufacture.GetRequired(rule.Id))))
                .ToArray();
            return new(owner.Scientists, FreeLaboratories(owner), owner.Engineers,
                Math.Max(0, AvailableWorkshops(owner) - UsedWorkshopSpace(owner)),
                Array.AsReadOnly(research),
                Array.AsReadOnly(owner.Research.Select(project =>
                {
                    var id = _content.RuntimeRules.Research.GetExternalId(project.Rule);
                    return new CampaignResearchProject(id, project.Assigned, project.Spent,
                        project.Cost, ResearchProgress(project));
                }).ToArray()),
                Array.AsReadOnly(manufacture),
                Array.AsReadOnly(owner.Productions.Select(project => new CampaignProductionProject(
                    _content.RuntimeRules.Manufacture.GetExternalId(project.Rule), project.Assigned,
                    project.Spent, project.Amount, Produced(project), project.Infinite, project.Sell)).ToArray()));
        }
    }

    private string? ResearchUnavailable(BaseState owner, RuleHandle<ResearchRuleFamily> handle, bool direct,
        IReadOnlySet<string>? unlocked = null)
    {
        var id = _content.RuntimeRules.Research.GetExternalId(handle);
        var rule = _content.RuntimeRules.Research[handle].Value;
        var options = (direct ? ResearchEligibilityOptions.Direct : ResearchEligibilityOptions.None) |
            (_debugMode ? ResearchEligibilityOptions.IgnoreProgressRequirements : ResearchEligibilityOptions.None);
        if (CoreResearchUnavailable(id, rule, options, unlocked) is { } coreReason) return coreReason;
        if (owner.Research.Any(project => project.Rule == handle)) return "Research is already active.";
        if (!WeightsFit(rule.Events, static entry => entry.Value))
            return "Strategic event weights exceed the supported range.";
        if (rule.NeedItem && (rule.NeededItem is null || !_content.RuntimeRules.Items.TryGet(rule.NeededItem, out var item) ||
            owner.Items.GetValueOrDefault(item) == 0)) return "Required research item is unavailable.";
        return HasFunctions(owner, rule.RequiredBaseFunctions) ? null : "Required base functions are unavailable.";
    }

    private string? ProductionUnavailable(BaseState owner, RuleHandle<ManufactureRuleFamily> handle)
    {
        var id = _content.RuntimeRules.Manufacture.GetExternalId(handle);
        var rule = _content.RuntimeRules.Manufacture[handle].Value;
        if (_manufactureRuleStatus.GetValueOrDefault(id) == 2) return "Production is hidden.";
        if (!_debugMode && rule.Requirements.Any(required => !_completedResearch.Contains(required)))
            return "Required research is incomplete.";
        if (!HasFunctions(owner, rule.RequiredBaseFunctions)) return "Required base functions are unavailable.";
        return ProductionRuleInvalid(rule);
    }

    /// <summary>Rule defects that would make hourly progression throw; shared by start and time preflight.</summary>
    private string? ProductionRuleInvalid(RuntimeManufactureRule rule)
    {
        if (rule.Time < 0 || rule.Space < 0 || rule.Cost < 0 ||
            rule.RequiredMaterials.Any(material => material.Quantity < 0) ||
            rule.ProducedMaterials.Any(material => material.Quantity < 0))
            return "Production rule contains an unsupported negative value.";
        if (rule.RequiredMaterials.Any(material => material.Item is null && material.Craft is null) ||
            rule.ProducedMaterials.Any(material => material.Item is null && material.Craft is null))
            return "Production references an unresolved item or craft.";
        if (rule.SpawnedPersonType.Length != 0 && rule.SpawnedPersonType is not "STR_SCIENTIST" and not "STR_ENGINEER" &&
            !_content.RuntimeRules.Soldiers.TryGet(rule.SpawnedPersonType, out _))
            return "Production references an unresolved person type.";
        if (!WeightsFit(rule.RandomProducedItems, static entry => unchecked((ulong)entry.Weight),
                requirePositive: rule.RandomProducedItems.Count != 0))
            return "Random production weights exceed the supported range.";
        if (rule.RandomProducedItems.SelectMany(set => set.Items).Any(pair =>
            pair.Value < 0 || !_content.RuntimeRules.Items.TryGet(pair.Key, out _)))
            return "Random production references an invalid item quantity or ID.";
        if (!WeightsFit(rule.Events, static entry => entry.Value))
            return "Strategic event weights exceed the supported range.";
        return null;
    }

    private string? PreflightResearchProduction(CampaignTimeTrigger highest)
    {
        var scoreCapacity = new ResearchScoreCapacity(_researchScores[^1]);
        long work = 0;
        if (highest >= CampaignTimeTrigger.OneDay)
        {
            // Any topic can complete through free or lookup discoveries, so all research events are checked.
            // Content is immutable for the campaign lifetime, so the scan result is cached.
            _researchEventWeightsFit ??= _content.RuntimeRules.Research.Rules.All(rule =>
                WeightsFit(rule.Value.Events, static entry => entry.Value));
            if (_researchEventWeightsFit == false)
                return "Strategic event weights exceed the supported range.";
            foreach (var owner in _bases)
                foreach (var project in owner.Research)
                    if ((long)project.Spent + project.Assigned > int.MaxValue)
                        return "Research progress exceeds the supported range.";
            if (ReserveDailyResearchScores(ref scoreCapacity, ref work) is { } scoreReason) return scoreReason;
            if (PreflightResearchRecovery(ref work) is { } recoveryReason) return recoveryReason;
        }
        if (highest >= CampaignTimeTrigger.OneHour)
        {
            var accountingCapacity = new ProductionAccountingCapacity(_funds[^1], _incomes[^1], _expenditures[^1]);
            long possibleTransfers = 0;
            Dictionary<RuleHandle<CraftRuleFamily>, long>? possibleCrafts = null;
            var personnelBirths = false;
            foreach (var owner in _bases)
            {
                foreach (var production in owner.Productions)
                {
                    if ((long)production.Spent + production.Assigned > int.MaxValue)
                        return "Production progress exceeds the supported range.";
                    // Restored saves can carry projects that were never validated by a start command.
                    var manufacture = _content.RuntimeRules.Manufacture[production.Rule].Value;
                    if (ProductionRuleInvalid(manufacture) is { } invalidReason) return invalidReason;
                    if (production.Sell && ProductionSellUnavailable(manufacture) is { } sellReason)
                        return sellReason;
                }
                if (ForecastProductionAssignments(owner, highest, out var assignments, ref work) is { } staffingReason)
                    return staffingReason;
                long possiblePersonnel = 0;
                for (var index = 0; index < owner.Productions.Count; index++)
                {
                    var production = owner.Productions[index];
                    var manufacture = _content.RuntimeRules.Manufacture[production.Rule].Value;
                    var units = ProductionProgress.Requested(manufacture.Time, production.Spent, assignments[index], production.Amount, production.Infinite);
                    if (units == 0) continue;
                    if (PossibleProductionTransfers(manufacture, production.Sell, units, ref work, out var transferCount) is { } transferReason) return transferReason;
                    possibleTransfers += transferCount;
                    foreach (var material in manufacture.ProducedMaterials)
                        if (material.Craft is { } craft)
                        {
                            possibleCrafts ??= [];
                            possibleCrafts[craft] = possibleCrafts.GetValueOrDefault(craft) + units * (long)material.Quantity;
                            if (ProducedCraftServiceReason(owner, craft) is { } craftReason) return craftReason;
                        }
                    if (manufacture.SpawnedPersonType.Length != 0)
                    {
                        personnelBirths = true;
                        possiblePersonnel += units;
                    }
                    if (!scoreCapacity.Reserve(manufacture.Points, manufacture.Points, units))
                        return "Research and production scores exceed the supported range.";
                    if (ReserveProductionHistory(production, manufacture, units, ref work) is { } historyReason) return historyReason;
                    var starts = units - (!production.Infinite && ProductionProgress.Produced(manufacture.Time,
                        production.Spent + assignments[index], production.Amount) >= production.Amount ? 1 : 0);
                    if (!accountingCapacity.Reserve(-manufacture.Cost, -manufacture.Cost, starts))
                        return "Production accounting exceeds the supported range.";
                    if (!production.Sell) continue;
                    foreach (var material in manufacture.ProducedMaterials)
                    {
                        if (++work > 1_000_000) return "Production accounting exceeds the supported work limit.";
                        if (material.Item is not { } item) continue;
                        var rule = _content.RuntimeRules.Items[item].Value;
                        var scripted = rule.Scripts.Any(handle => _content.RuntimeRules.Scripts[handle].Value.Artifact.ParserName == "sellCostItem") ||
                            _content.EventPlans.Any(plan => plan.ParserName == "sellCostItem");
                        var price = StrategicLogisticsMath.AdjustedItemPrice(rule.CostSell,
                            _content.RuntimeRules.Campaign.SellPriceCoefficients[(int)Difficulty]);
                        var minimum = (long)(scripted ? int.MinValue : price) * material.Quantity;
                        var maximum = (long)(scripted ? int.MaxValue : price) * material.Quantity;
                        if (!accountingCapacity.Reserve(minimum, maximum, units))
                            return "Production accounting exceeds the supported range.";
                    }
                }
                if (possiblePersonnel != 0 && UsedQuartersWide(owner) + possiblePersonnel > int.MaxValue)
                    return "Production personnel exceed the supported population range.";
            }
            if (personnelBirths && ProductionPersonnelTransferCapacity(possibleTransfers, highest, ref work) is { } personnelReason) return personnelReason;
            if (possibleCrafts is not null)
            {
                if (ProductionCraftCapacity(possibleCrafts, ref work) is { } craftReason) return craftReason;
                if (!personnelBirths && ProductionPersonnelTransferCapacity(possibleTransfers, highest, ref work) is { } arrivalReason)
                    return arrivalReason.Replace("personnel", "craft", StringComparison.Ordinal);
            }
        }
        return null;
    }

    private void AdvanceResearchDaily(BaseState owner, TimeEffects effects)
    {
        var finished = new List<ResearchProjectState>();
        var sideEffects = new List<string>();
        for (var index = 0; index < owner.Research.Count; index++)
        {
            var project = owner.Research[index];
            project = project with { Spent = checked(project.Spent + project.Assigned) };
            owner.Research[index] = project;
            if (project.Spent >= project.Cost) finished.Add(project);
        }
        foreach (var project in finished)
        {
            var id = _content.RuntimeRules.Research.GetExternalId(project.Rule);
            var rule = _content.RuntimeRules.Research[project.Rule].Value;
            owner.Scientists = checked(owner.Scientists + project.Assigned);
            owner.Research.Remove(project);
            ApplyResearchRecoveredItems(owner, rule, id, finished: true, dailyCompletion: true);
            var discoveries = CompleteResearch(owner, id, rule, sideEffects);
            effects.Notify(new CampaignResearchCompleted(owner.Id, id, discoveries));
        }
        var pendingSideEffects = sideEffects.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in _content.RuntimeRules.Research.Rules
            .Where(entry => pendingSideEffects.Contains(entry.Id)))
        {
            ApplyResearchSideEffects(owner, entry.Value, entry.Id, effects);
        }
    }

    private ReadOnlyCollection<string> CompleteResearch(
        BaseState owner, string id, RuntimeResearchRule rule, List<string> sideEffects)
    {
        var discoveries = new List<string>();
        var bonus = SelectResearchReward(rule);
        if (bonus is not null)
        {
            if (_content.RuntimeRules.Research.TryGet(bonus, out var bonusHandle))
                CompleteResearchTopic(owner, bonus, _content.RuntimeRules.Research[bonusHandle].Value,
                    discoveries, sideEffects);
            else
                AddFinishedResearch(owner, bonus, discoveries);
        }
        CompleteResearchTopic(owner, id, rule, discoveries, sideEffects);
        return Array.AsReadOnly(discoveries.ToArray());
    }

    private void CompleteResearchTopic(
        BaseState owner, string id, RuntimeResearchRule rule, List<string> discoveries, List<string> sideEffects)
    {
        AddFinishedResearch(owner, id, discoveries);
        if (rule.Lookup.Length != 0) AddFinishedResearch(owner, rule.Lookup, discoveries);
        sideEffects.Add(id);
    }

    private void AddFinishedResearch(BaseState owner, string initial, List<string> discoveries)
    {
        long work = 0;
        var progress = new ResearchDiscoveryProgress(_content.RuntimeRules, _completedResearch, _researchRuleStatus);
        progress.Advance(initial, (id, rule) =>
        {
            var handle = _content.RuntimeRules.Research.GetRequired(id);
            var eligible = !owner.Research.Any(project => project.Rule == handle) &&
                (!rule.NeedItem || rule.NeededItem is { } needed && _content.RuntimeRules.Items.TryGet(needed, out var item) &&
                    owner.Items.GetValueOrDefault(item) != 0) && HasFunctions(owner, rule.RequiredBaseFunctions);
            return (eligible, 0L);
        }, (id, points, markDiscovered) =>
        {
            if (markDiscovered) discoveries.Add(id);
            _researchScores[^1] = checked(_researchScores[^1] + points);
        }, ref work, long.MaxValue);
    }

    private string? CoreResearchUnavailable(
        string id, RuntimeResearchRule rule, ResearchEligibilityOptions options, IReadOnlySet<string>? unlocked = null)
    {
        if (_researchRuleStatus.GetValueOrDefault(id) == 2) return "Research is permanently disabled.";
        if (options.HasFlag(ResearchEligibilityOptions.Direct) && rule.Requirements.Count != 0)
            return "Research is only available as an indirect unlock.";
        var ignoreProgressRequirements = options.HasFlag(ResearchEligibilityOptions.IgnoreProgressRequirements);
        // The unlocked-topic scan is the expensive part, so it only runs when a dependency is missing.
        if (!ignoreProgressRequirements && rule.Dependencies.Any(required => !_completedResearch.Contains(required)) &&
            !(unlocked?.Contains(id) ?? IsExplicitlyUnlocked(id)))
            return "Research dependencies are incomplete.";
        if (!ignoreProgressRequirements && rule.Requirements.Any(required => !_completedResearch.Contains(required)))
            return "Research requirements are incomplete.";
        // getAvailableResearchProjects keeps a discovered topic only while it can still yield a
        // free topic or a protected unlock; repeatable topics are simply never marked discovered.
        return _completedResearch.Contains(id) && !HasRemainingResearchReward(rule) && !HasProtectedUnlock(rule)
            ? "Research is already complete." : null;
    }

    private string? SelectResearchReward(RuntimeResearchRule rule)
    {
        var candidates = new ResearchDiscoveryProgress(_content.RuntimeRules, _completedResearch, _researchRuleStatus).RewardCandidates(rule);
        if (candidates.Count == 0) return null;
        return rule.SequentialGetOneFree ? candidates[0] : candidates[_random.NextExclusive(candidates.Count)];
    }

    private bool HasRemainingResearchReward(RuntimeResearchRule rule) =>
        rule.GetOneFree.Any(IsNew) || rule.GetOneFreeProtected.Any(group =>
            _completedResearch.Contains(group.Prerequisite) && group.Topics.Any(IsNew));
    private bool HasProtectedUnlock(RuntimeResearchRule rule) => rule.Unlocks.Any(id =>
        _content.RuntimeRules.Research.TryGet(id, out var handle) &&
        _content.RuntimeRules.Research[handle].Value.Requirements.Count != 0 &&
        !_completedResearch.Contains(id) && _researchRuleStatus.GetValueOrDefault(id) != 2);
    private bool IsNew(string id) => !_completedResearch.Contains(id) &&
        _researchRuleStatus.GetValueOrDefault(id) != 2;
    private bool IsExplicitlyUnlocked(string id) => _completedResearch.Any(completed =>
        _content.RuntimeRules.Research.TryGet(completed, out var prior) &&
        _content.RuntimeRules.Research[prior].Value.Unlocks.Contains(id, StringComparer.Ordinal));
    private HashSet<string> ExplicitlyUnlockedResearch()
    {
        var unlocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var completed in _completedResearch)
            if (_content.RuntimeRules.Research.TryGet(completed, out var prior))
                unlocked.UnionWith(_content.RuntimeRules.Research[prior].Value.Unlocks);
        return unlocked;
    }

    private void ApplyResearchSideEffects(
        BaseState owner, RuntimeResearchRule rule, string source, TimeEffects effects)
    {
        if (rule.SpawnedItem.Length != 0 &&
            _content.RuntimeRules.Items.TryGet(rule.SpawnedItem, out _))
            QueueTransfer(owner, 1, CampaignTransferKind.Item,
                rule.SpawnedItem, Math.Max(1, rule.SpawnedItemCount));
        foreach (var id in rule.SpawnedItemList)
            if (_content.RuntimeRules.Items.TryGet(id, out _))
                QueueTransfer(owner, 1, CampaignTransferKind.Item, id, 1);
        foreach (var id in rule.IncreaseCounters)
            CustomCounters.Apply(new CustomCounterChange(id, true));
        foreach (var id in rule.DecreaseCounters)
            CustomCounters.Apply(new CustomCounterChange(id, false));
        if (rule.SpawnedEvent.Length != 0)
            ScheduleStrategicEvent(rule.SpawnedEvent, source, effects);
        var selectedEvent = SelectWeighted(rule.Events, static entry => entry.Value,
            "Strategic event weights exceed the supported range.");
        if (selectedEvent.HasValue)
            ScheduleStrategicEvent(selectedEvent.Value.Key, source, effects);
        if (!HasRemainingResearchReward(rule) && !HasProtectedUnlock(rule))
        {
            var sourceHandle = _content.RuntimeRules.Research.GetRequired(source);
            foreach (var baseState in _bases)
                foreach (var project in baseState.Research.Where(project => project.Rule == sourceHandle).ToArray())
                    RemoveResearchProject(baseState, project);
        }
    }

    private void RemoveDisabledResearchProjects()
    {
        if (!_researchRuleStatus.ContainsValue(2)) return;
        foreach (var owner in _bases)
            foreach (var project in owner.Research.Where(project => _researchRuleStatus.GetValueOrDefault(
                _content.RuntimeRules.Research.GetExternalId(project.Rule)) == 2).ToArray())
                RemoveResearchProject(owner, project);
    }

    private void AdvanceProductionHourly(TimeEffects effects)
    {
        foreach (var owner in _bases)
        {
            if (owner.Productions.Count == 0) continue;
            List<ProductionState> removals = [];
            foreach (var original in owner.Productions.ToArray())
            {
                var production = original;
                if (production.IsFallback)
                {
                    var additional = checked((int)ProductionProgress.FallbackAdditional(production.Spent,
                        production.Assigned, _content.RuntimeRules.Manufacture[production.Rule].Value.Space,
                        owner.Engineers, AvailableWorkshops(owner) - (long)UsedWorkshopSpace(owner)));
                    if (additional > 0)
                    {
                        production = production with { Assigned = checked(production.Assigned + additional) };
                        owner.Engineers -= additional;
                        owner.Productions[owner.Productions.IndexOf(original)] = production;
                    }
                }
                var before = Produced(production);
                var progressed = production with { Spent = checked(production.Spent + production.Assigned) };
                var after = Produced(progressed);
                var requested = ProductionProgress.Requested(_content.RuntimeRules.Manufacture[production.Rule].Value.Time,
                    production.Spent, production.Assigned, production.Amount, production.Infinite);
                var made = 0;
                string? stop = null;
                while (made < requested)
                {
                    CompleteProductionUnit(owner, progressed, effects);
                    made++;
                    if (!progressed.Infinite && before + made >= progressed.Amount) break;
                    stop = StartProductionUnit(owner, _content.RuntimeRules.Manufacture[progressed.Rule].Value,
                        checkLivingSpace: made >= requested, effects: effects);
                    if (stop is not null) break;
                }
                var complete = !progressed.Infinite && after >= progressed.Amount;
                var index = owner.Productions.IndexOf(production);
                owner.Productions[index] = progressed;
                if (complete || stop is not null) removals.Add(progressed);
                if (made != 0 || stop is not null || complete)
                    effects.Notify(new CampaignProductionProgress(owner.Id,
                        _content.RuntimeRules.Manufacture.GetExternalId(progressed.Rule), made,
                        stop ?? (complete ? "STR_PRODUCTION_COMPLETE" : null)));
            }
            // GeoscapeState::time1Hour removes projects after every step in this base.
            // Released staff and workshop space cannot feed a later fallback this hour.
            foreach (var production in removals)
            {
                owner.Engineers = checked(owner.Engineers + production.Assigned);
                owner.Productions.Remove(production);
            }
        }
    }

    private string? StartProductionUnit(
        BaseState owner, RuntimeManufactureRule rule, bool initial = false, bool checkLivingSpace = true, TimeEffects? effects = null)
    {
        // RuleManufacture::haveEnoughMoneyForOneMoreUnit: free units start even with negative funds.
        if (rule.Cost > 0 && _funds[^1] < rule.Cost) return "STR_NOT_ENOUGH_MONEY";
        if (!TryStageAccounting(-rule.Cost, out var accounting))
            return "Production accounting exceeds the supported range.";
        if (rule.SpawnedPersonType.Length != 0 && checkLivingSpace)
        {
            var available = AvailableQuarters(owner);
            var used = UsedQuarters(owner);
            if (initial ? available <= used : available < used) return "STR_NOT_ENOUGH_LIVING_SPACE";
        }
        if (!TryPlanProductionMaterials(owner, rule, out var materialPlan, out var materialReason))
            return materialReason;
        if (materialPlan.Any(material => material.Craft is not null))
        {
            var craftTargets = materialPlan.Where(material => material.Craft is not null)
                .Select(material => (material.Id, material.Craft!.Id)).ToArray();
            if (_handlers.Operation<World.ICampaignCraftRemoval>().PlanReason(craftTargets) is { } removalReason)
                return removalReason;
        }
        foreach (var material in materialPlan)
        {
            if (material.Item is { } item) ChangeStock(owner.Items, item, -material.Quantity);
            else if (material.Craft is { } removed)
            {
                foreach (var unloaded in CraftLogistics.UnloadedItems(removed.Logistics!, _content.RuntimeRules))
                    ChangeStock(owner.Items, _content.RuntimeRules.Items.GetRequired(unloaded.Key), unloaded.Value);
                owner.Crafts.Remove(removed);
                _handlers.Operation<World.ICampaignCraftRemoval>().Removed(material.Id, removed.Id, effects);
                foreach (var soldier in owner.Soldiers.Where(s => s.Personal is { } personal &&
                    personal.CraftType == material.Id && personal.CraftId == removed.Id).ToArray())
                {
                    var index = owner.Soldiers.IndexOf(soldier);
                    owner.Soldiers[index] = soldier with
                    {
                        Personal = soldier.Personal! with { CraftType = "", CraftId = 0 },
                    };
                }
            }
        }
        PublishAccounting(accounting);
        return null;
    }

    private static bool TryPlanProductionMaterials(
        BaseState owner, RuntimeManufactureRule rule,
        out IReadOnlyList<ProductionMaterialConsumption> plan, out string reason)
    {
        return TryPlanProductionMaterials(owner.Items, owner.Crafts, rule, out plan, out reason);
    }

    private static bool TryPlanProductionMaterials(
        IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int> items, IReadOnlyList<CraftState> crafts,
        RuntimeManufactureRule rule, out IReadOnlyList<ProductionMaterialConsumption> plan, out string reason)
    {
        var result = new List<ProductionMaterialConsumption>();
        List<ProductionMaterialConsumption>? craftPlan = null;
        foreach (var material in rule.RequiredMaterials)
        {
            if (material.Item is { } item)
            {
                if (items.GetValueOrDefault(item) < material.Quantity)
                {
                    plan = [];
                    reason = "STR_NOT_ENOUGH_MATERIALS";
                    return false;
                }
                result.Add(new(material.Id, material.Quantity, item, null));
                continue;
            }
            if (material.Craft is not { } craft) continue;
            // Native eligibility counts grounded craft, while startItem removes
            // the first matching craft once, irrespective of the requested quantity.
            if (crafts.Count(value => value.Rule == craft && value.Logistics?.Status != "STR_OUT") < material.Quantity)
            {
                plan = [];
                reason = "STR_NOT_ENOUGH_MATERIALS";
                return false;
            }
            var selected = crafts.FirstOrDefault(value => value.Rule == craft);
            if (selected is null) continue;
            if (selected.Logistics is null)
            {
                plan = [];
                reason = "Required craft material has unresolved logistics state.";
                return false;
            }
            (craftPlan ??= []).Add(new(material.Id, 1, null, selected));
        }
        // startItem consumes every item before unloading any craft, preserving
        // reference material order within both groups.
        if (craftPlan is not null) result.AddRange(craftPlan);
        plan = result;
        reason = string.Empty;
        return true;
    }

    private void RefundProductionUnit(
        BaseState owner, RuntimeManufactureRule rule, AccountingState accounting)
    {
        foreach (var material in rule.RequiredMaterials)
            if (material.Item is { } item) ChangeStock(owner.Items, item, material.Quantity);
        PublishAccounting(accounting);
    }

    private void CompleteProductionUnit(BaseState owner, ProductionState production, TimeEffects effects)
    {
        var rule = _content.RuntimeRules.Manufacture[production.Rule].Value;
        var saleAccounting = new AccountingState(_funds[^1], _incomes[^1], _expenditures[^1]);
        if (production.Sell)
            foreach (var material in rule.ProducedMaterials)
                if (material.Item is { } item)
                {
                    var price = ItemPrice(material.Id, _content.RuntimeRules.Items[item].Value,
                        buying: false, out var unavailable);
                    if (unavailable is not null) throw new InvalidOperationException(unavailable);
                    // Native setFunds runs separately for every item: mixed signs
                    // must retain both income and expenditure, even when net funds do not change.
                    if (!TryStageAccounting(checked((long)price * material.Quantity), saleAccounting, out var next))
                        throw new InvalidDataException("Production accounting exceeds the supported range.");
                    saleAccounting = next;
                }
        foreach (var material in rule.ProducedMaterials)
        {
            if (material.Item is { } item)
            {
                if (!production.Sell)
                {
                    DeliverProducedItem(owner, material.Id, material.Quantity, rule);
                    if (rule.RandomProducedItems.Count != 0)
                        production.RandomProductionInfo[material.Id] = checked(
                            production.RandomProductionInfo.GetValueOrDefault(material.Id) + material.Quantity);
                }
            }
            else if (material.Craft is { } craft) ProduceCraft(owner, craft, material.Quantity, rule);
        }
        if (production.Sell) PublishAccounting(saleAccounting);
        if (rule.RandomProducedItems.Count != 0)
        {
            var selected = SelectWeighted(rule.RandomProducedItems,
                static entry => unchecked((ulong)entry.Weight),
                "Random production weights exceed the supported range.");
            if (selected.HasValue)
            {
                foreach (var item in selected.Value.Items)
                {
                    DeliverProducedItem(owner, item.Key, item.Value, rule);
                    production.RandomProductionInfo[item.Key] =
                        checked(production.RandomProductionInfo.GetValueOrDefault(item.Key) + item.Value);
                }
            }
        }
        ProducePerson(owner, rule);
        var selectedEvent = SelectWeighted(rule.Events, static entry => entry.Value,
            "Strategic event weights exceed the supported range.");
        if (selectedEvent.HasValue)
            ScheduleStrategicEvent(selectedEvent.Value.Key,
                _content.RuntimeRules.Manufacture.GetExternalId(production.Rule), effects);
        _researchScores[^1] = checked(_researchScores[^1] + rule.Points);
    }

    private string? ProductionSellUnavailable(RuntimeManufactureRule rule)
    {
        foreach (var material in rule.ProducedMaterials)
        {
            if (material.Item is not { } item) continue;
            _ = ItemPrice(material.Id, _content.RuntimeRules.Items[item].Value,
                buying: false, out var unavailable);
            if (unavailable is not null) return unavailable;
        }
        return null;
    }

    private WeightedSelection<T> SelectWeighted<T>(
        IEnumerable<T> entries, Func<T, ulong> weight, string invalidMessage)
    {
        if (!TryGetTotalWeight(entries, weight, out var total)) throw new InvalidDataException(invalidMessage);
        if (total == 0) return default;
        var roll = _random.NextInclusive(1, total);
        foreach (var entry in entries)
        {
            var entryWeight = weight(entry);
            if ((ulong)roll <= entryWeight) return new(true, entry);
            roll -= checked((int)entryWeight);
        }
        throw new InvalidOperationException("Weighted selection did not produce a result.");
    }

    private static bool WeightsFit<T>(
        IEnumerable<T> entries, Func<T, ulong> weight, bool requirePositive = false) =>
        TryGetTotalWeight(entries, weight, out var total) && (!requirePositive || total != 0);

    private static bool TryGetTotalWeight<T>(
        IEnumerable<T> entries, Func<T, ulong> weight, out int total)
    {
        ulong sum = 0;
        foreach (var entry in entries)
        {
            var value = weight(entry);
            if (value > int.MaxValue || sum > (ulong)int.MaxValue - value)
            {
                total = 0;
                return false;
            }
            sum += value;
        }
        total = (int)sum;
        return true;
    }

    private void DeliverProducedItem(BaseState owner, string id, int quantity, RuntimeManufactureRule rule)
    {
        if (quantity < 0) throw new InvalidDataException("Production item quantities cannot be negative.");
        var transferHours = rule.TransferTimes.Count == 0 ? 0 : Math.Max(0, rule.TransferTimes[0]);
        if (transferHours == 0)
        {
            var item = _content.RuntimeRules.Items.GetRequired(id);
            ChangeStock(owner.Items, item, quantity);
            if (_content.RuntimeRules.Items[item].Value.BattleType == 0)
                for (var craftIndex = 0; craftIndex < owner.Crafts.Count; craftIndex++)
                {
                    var craft = owner.Crafts[craftIndex];
                    if (craft.Logistics is null) continue;
                    owner.Crafts[craftIndex] = craft with
                    {
                        Logistics = CraftServicing.ReuseItem(craft.Logistics,
                            _content.RuntimeRules.Crafts[craft.Rule].Value, _content.RuntimeRules, item),
                    };
                }
        }
        else
        {
            QueueTransfer(owner, transferHours, CampaignTransferKind.Item, id, quantity);
        }
    }

    private void ProduceCraft(BaseState owner, RuleHandle<CraftRuleFamily> craft, int quantity, RuntimeManufactureRule rule)
    {
        var id = _content.RuntimeRules.Crafts.GetExternalId(craft);
        var transferHours = rule.TransferTimes.Count < 3 ? 0 : Math.Max(0, rule.TransferTimes[2]);
        for (var count = 0; count < quantity; count++)
        {
            var craftId = NextCraftId(id);
            _nextIds[id] = checked(craftId + 1);
            var key = FormattableString.Invariant($"created:{Identity.Id}:craft:{id}:{craftId}");
            var logistics = ProducedCraftLogistics(owner, craft);
            var value = new CraftSnapshot(id, craftId) { PreservationKey = key, Logistics = logistics };
            if (transferHours == 0)
                owner.Crafts.Add(new(craft, craftId) { PreservationKey = key, Logistics = logistics });
            else
            {
                QueueTransfer(owner, transferHours, CampaignTransferKind.Craft, id, 1, craft: value);
            }
        }
    }

    private void ProducePerson(BaseState owner, RuntimeManufactureRule rule)
    {
        if (rule.SpawnedPersonType.Length == 0) return;
        var hours = Math.Max(1, rule.TransferTimes.Count < 2 ? 24 : rule.TransferTimes[1]);
        if (rule.SpawnedPersonType == "STR_SCIENTIST")
            QueueTransfer(owner, hours, CampaignTransferKind.Scientist, "", 1);
        else if (rule.SpawnedPersonType == "STR_ENGINEER")
            QueueTransfer(owner, hours, CampaignTransferKind.Engineer, "", 1);
        else
        {
            var handle = _content.RuntimeRules.Soldiers.GetRequired(rule.SpawnedPersonType);
            var definition = _content.RuntimeRules.Soldiers[handle].Value;
            var soldierId = NextSoldierId();
            _nextIds["STR_SOLDIER"] = checked(soldierId + 1);
            var personal = SoldierGeneration.Generate(definition,
                _content.RuntimeRules.Armors.GetExternalId(definition.Armor), -1,
                _bases.SelectMany(baseState => baseState.Soldiers)
                    .Select(s => s.Personal?.Name ?? "").ToHashSet(StringComparer.Ordinal), _random);
            if (rule.SpawnedSoldierTemplate is { } template)
                personal = SoldierGeneration.ApplyTemplate(personal, template, definition,
                    _content.RuntimeRules, _random);
            personal = rule.SpawnedPersonName.Length != 0
                ? personal with { Name = rule.SpawnedPersonName }
                : SoldierGeneration.RegenerateName(personal, definition, _random);
            var soldier = new SoldierSnapshot(rule.SpawnedPersonType, soldierId)
            {
                PreservationKey = FormattableString.Invariant($"created:{Identity.Id}:soldier:{soldierId}"),
                Personal = personal,
            };
            QueueTransfer(owner, hours, CampaignTransferKind.Soldier,
                rule.SpawnedPersonType, 1, soldier: soldier);
        }
    }

    private void QueueTransfer(BaseState owner, int hours, CampaignTransferKind kind,
        string ruleId, int quantity, SoldierSnapshot? soldier = null, CraftSnapshot? craft = null)
    {
        // Loaded reference saves assign transfer IDs without a matching counter, so allocation must
        // skip every existing ID exactly like the logistics and transformation paths.
        var transferId = NextTransferId();
        _nextIds["oxcePortTransfer"] = checked(transferId + 1);
        owner.Transfers.Add(new(transferId, hours, kind, ruleId, quantity, soldier, craft)
        {
            PreservationKey = FormattableString.Invariant($"created:{Identity.Id}:transfer:{transferId}"),
        });
    }

    private int Produced(ProductionState state) =>
        ProductionProgress.Produced(_content.RuntimeRules.Manufacture[state.Rule].Value.Time, state.Spent, state.Amount);
    private static bool CanAutoSell(RuntimeManufactureRule rule) =>
        rule.SpawnedPersonType.Length == 0 && rule.RandomProducedItems.Count == 0;
    private static bool TryReassignProjectStaff(
        int requested, int assigned, int available, long capacity, out int remaining)
    {
        var delta = requested - assigned;
        if (delta > 0 && (delta > available || requested > capacity))
        {
            remaining = available;
            return false;
        }
        remaining = checked(available - delta);
        return true;
    }
    private static RuleHandle<CraftRuleFamily>? ProducedCraft(RuntimeManufactureRule rule) =>
        rule.ProducedMaterials.FirstOrDefault(material => material.Craft is not null)?.Craft;
    private static bool HoldsResearchItem(RuntimeResearchRule rule) =>
        rule.NeedItem && (rule.DestroyItem || rule.ReturnsItem);
    private void RemoveResearchProject(BaseState owner, ResearchProjectState project)
    {
        owner.Scientists = checked(owner.Scientists + project.Assigned);
        var rule = _content.RuntimeRules.Research[project.Rule].Value;
        ApplyResearchRecoveredItems(owner, rule, _content.RuntimeRules.Research.GetExternalId(project.Rule),
            project.Spent >= project.Cost, dailyCompletion: false);
        owner.Research.Remove(project);
    }

    [Flags]
    private enum ResearchEligibilityOptions { None = 0, Direct = 1, IgnoreProgressRequirements = 2 }

    private readonly record struct ProductionMaterialConsumption(
        string Id, int Quantity, RuleHandle<ItemRuleFamily>? Item, CraftState? Craft);

    private string ResearchProgress(ResearchProjectState project)
    {
        if (project.Assigned == 0) return "STR_NONE";
        var ruleCost = _content.RuntimeRules.Research[project.Rule].Value.Cost;
        var progress = ruleCost == 0 ? float.PositiveInfinity : (float)project.Spent / ruleCost;
        if (progress <= 0.333f) return "STR_UNKNOWN";
        var rating = ruleCost == 0 ? float.PositiveInfinity : (float)project.Assigned / ruleCost;
        if (rating <= 0.07f) return "STR_POOR";
        if (rating <= 0.13f) return "STR_AVERAGE";
        if (rating <= 0.25f) return "STR_GOOD";
        return "STR_EXCELLENT";
    }

    private readonly record struct WeightedSelection<T>(bool HasValue, T Value);
}
