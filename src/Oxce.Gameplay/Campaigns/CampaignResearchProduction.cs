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

public sealed record CampaignResearchChoice(string RuleId, int Cost, string? UnavailableReason);
public sealed record CampaignResearchProject(string RuleId, int Assigned, int Spent, int Cost, string Progress);
public sealed record CampaignProductionChoice(string RuleId, int Time, int Cost, int Space, string? UnavailableReason);
public sealed record CampaignProductionProject(
    string RuleId, int Assigned, int Spent, int Amount, int Produced, bool Infinite, bool Sell);
public sealed record CampaignResearchProduction(
    int ScientistsAvailable, int LaboratoriesAvailable, int EngineersAvailable, int WorkshopsAvailable,
    IReadOnlyList<CampaignResearchChoice> ResearchChoices, IReadOnlyList<CampaignResearchProject> Research,
    IReadOnlyList<CampaignProductionChoice> ProductionChoices, IReadOnlyList<CampaignProductionProject> Productions);

public sealed partial class CampaignState : ICampaignResearchProductionQuery
{
    private bool? _researchEventWeightsFit;

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
        }
        if (highest >= CampaignTimeTrigger.OneHour)
            foreach (var owner in _bases)
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
            if (rule.ReturnsItem) ReturnHeldResearchItem(owner, rule);
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
        if (!_content.RuntimeRules.Research.TryGet(initial, out var initialHandle) ||
            _researchRuleStatus.GetValueOrDefault(initial) == 2) return;
        var markDiscovered = !_content.RuntimeRules.Research[initialHandle].Value.Repeatable;
        var queue = new List<string> { initial };
        var queued = new HashSet<string>(StringComparer.Ordinal) { initial };
        for (var offset = 0; offset < queue.Count; offset++)
        {
            var id = queue[offset];
            if (!_content.RuntimeRules.Research.TryGet(id, out var handle) ||
                _researchRuleStatus.GetValueOrDefault(id) == 2) continue;
            var rule = _content.RuntimeRules.Research[handle].Value;
            var wasComplete = _completedResearch.Contains(id);
            // Captured before disables/re-enables, like SavedGame::addFinishedResearch step 1.
            var hadProtectedUnlock = wasComplete && HasProtectedUnlock(rule);
            if (!wasComplete)
            {
                if (markDiscovered)
                {
                    _completedResearch.Add(id);
                    discoveries.Add(id);
                }
                _researchScores[^1] = checked(_researchScores[^1] + rule.Points);
                foreach (var disabled in rule.Disables)
                {
                    _completedResearch.Remove(disabled);
                    _researchRuleStatus[disabled] = 2;
                }
            }
            foreach (var enabled in rule.Reenables)
                if (_researchRuleStatus.GetValueOrDefault(enabled) == 2) _researchRuleStatus[enabled] = 0;
            if (wasComplete && !hadProtectedUnlock) continue;
            HashSet<string>? unlocked = null;
            foreach (var candidate in _content.RuntimeRules.Research.Rules)
            {
                if (candidate.Value.Cost != 0 || queued.Contains(candidate.Id)) continue;
                if ((candidate.Value.Requirements.Count == 0 || rule.Unlocks.Contains(candidate.Id, StringComparer.Ordinal)) &&
                    ZeroCostResearchAvailable(owner, candidate.Id, candidate.Value, ref unlocked) && queued.Add(candidate.Id))
                    queue.Add(candidate.Id);
            }
        }
    }

    /// <summary>
    /// SavedGame::getAvailableResearchProjects for the completing base: besides the global
    /// rules, a topic already running there, lacking its needed item, or lacking required
    /// base functions is not auto-completed.
    /// </summary>
    private bool ZeroCostResearchAvailable(
        BaseState owner, string id, RuntimeResearchRule rule, ref HashSet<string>? unlocked)
    {
        unlocked ??= ExplicitlyUnlockedResearch();
        if (CoreResearchUnavailable(id, rule, ResearchEligibilityOptions.None, unlocked) is not null) return false;
        var handle = _content.RuntimeRules.Research.GetRequired(id);
        if (owner.Research.Any(project => project.Rule == handle)) return false;
        if (rule.NeedItem && (rule.NeededItem is null || !_content.RuntimeRules.Items.TryGet(rule.NeededItem, out var item) ||
            owner.Items.GetValueOrDefault(item) == 0)) return false;
        return HasFunctions(owner, rule.RequiredBaseFunctions);
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
        return !rule.Repeatable && _completedResearch.Contains(id) && !HasRemainingResearchReward(rule)
            ? "Research is already complete." : null;
    }

    private string? SelectResearchReward(RuntimeResearchRule rule)
    {
        var candidates = rule.GetOneFree.Where(IsNew).ToList();
        foreach (var protectedGroup in rule.GetOneFreeProtected)
            if (_completedResearch.Contains(protectedGroup.Prerequisite))
                candidates.AddRange(protectedGroup.Topics.Where(IsNew));
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
            if (id.Length != 0)
                _nextIds[id] = checked(_nextIds.TryGetValue(id, out var value) ? value + 1 : 2);
        foreach (var id in rule.DecreaseCounters)
            if (id.Length != 0)
                _nextIds[id] = _nextIds.TryGetValue(id, out var value) ? Math.Max(1, value - 1) : 1;
        if (rule.SpawnedEvent.Length != 0)
            effects.Notify(new CampaignStrategicEventRequested(rule.SpawnedEvent, source));
        var selectedEvent = SelectWeighted(rule.Events, static entry => entry.Value,
            "Strategic event weights exceed the supported range.");
        if (selectedEvent.HasValue)
            effects.Notify(new CampaignStrategicEventRequested(selectedEvent.Value.Key, source));
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
            foreach (var original in owner.Productions.ToArray())
            {
                var production = original;
                if (production.IsFallback)
                {
                    var freeEngineers = owner.Engineers;
                    var freeWorkshops = AvailableWorkshops(owner) - UsedWorkshopSpace(owner);
                    if (production.Spent == 0 && production.Assigned == 0)
                        freeWorkshops -= _content.RuntimeRules.Manufacture[production.Rule].Value.Space;
                    if (freeEngineers > 0 && freeWorkshops > 0)
                    {
                        var assigned = Math.Min(freeEngineers, freeWorkshops);
                        owner.Engineers -= assigned;
                        production = production with { Assigned = checked(production.Assigned + assigned) };
                        owner.Productions[owner.Productions.IndexOf(original)] = production;
                    }
                }
                var before = Produced(production);
                var progressed = production with { Spent = checked(production.Spent + production.Assigned) };
                var after = Produced(progressed);
                var requested = progressed.Infinite ? after - before : Math.Min(after, progressed.Amount) - before;
                var made = 0;
                string? stop = null;
                while (made < requested)
                {
                    CompleteProductionUnit(owner, progressed, effects);
                    made++;
                    if (!progressed.Infinite && before + made >= progressed.Amount) break;
                    stop = StartProductionUnit(owner, _content.RuntimeRules.Manufacture[progressed.Rule].Value,
                        checkLivingSpace: made >= requested);
                    if (stop is not null) break;
                }
                var complete = !progressed.Infinite && after >= progressed.Amount;
                if (complete || stop is not null)
                {
                    owner.Engineers = checked(owner.Engineers + progressed.Assigned);
                    owner.Productions.Remove(production);
                }
                else
                {
                    var index = owner.Productions.IndexOf(production);
                    owner.Productions[index] = progressed;
                }
                if (made != 0 || stop is not null || complete)
                    effects.Notify(new CampaignProductionProgress(owner.Id,
                        _content.RuntimeRules.Manufacture.GetExternalId(progressed.Rule), made,
                        stop ?? (complete ? "STR_PRODUCTION_COMPLETE" : null)));
            }
        }
    }

    private string? StartProductionUnit(
        BaseState owner, RuntimeManufactureRule rule, bool initial = false, bool checkLivingSpace = true)
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
        foreach (var material in materialPlan)
        {
            if (material.Item is { } item) ChangeStock(owner.Items, item, -material.Quantity);
            else if (material.Craft is { } removed)
            {
                foreach (var unloaded in CraftLogistics.UnloadedItems(removed.Logistics!, _content.RuntimeRules))
                    ChangeStock(owner.Items, _content.RuntimeRules.Items.GetRequired(unloaded.Key), unloaded.Value);
                owner.Crafts.Remove(removed);
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
        var result = new List<ProductionMaterialConsumption>();
        var availableCrafts = owner.Crafts.Where(craft => craft.Logistics?.Status != "STR_OUT").ToList();
        foreach (var material in rule.RequiredMaterials)
        {
            if (material.Item is { } item)
            {
                if (owner.Items.GetValueOrDefault(item) < material.Quantity)
                {
                    plan = [];
                    reason = "STR_NOT_ENOUGH_MATERIALS";
                    return false;
                }
                result.Add(new(material.Id, material.Quantity, item, null));
                continue;
            }
            if (material.Craft is not { } craft) continue;
            for (var count = 0; count < material.Quantity; count++)
            {
                var index = availableCrafts.FindIndex(value => value.Rule == craft);
                if (index < 0)
                {
                    plan = [];
                    reason = "STR_NOT_ENOUGH_MATERIALS";
                    return false;
                }
                var selected = availableCrafts[index];
                if (selected.Logistics is null)
                {
                    plan = [];
                    reason = "Required craft material has unresolved logistics state.";
                    return false;
                }
                availableCrafts.RemoveAt(index);
                result.Add(new(material.Id, 1, null, selected));
            }
        }
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
        var saleValue = 0L;
        if (production.Sell)
            foreach (var material in rule.ProducedMaterials)
                if (material.Item is { } item)
                {
                    var price = ItemPrice(material.Id, _content.RuntimeRules.Items[item].Value,
                        buying: false, out var unavailable);
                    if (unavailable is not null) throw new InvalidOperationException(unavailable);
                    saleValue = checked(saleValue + checked((long)price * material.Quantity));
                }
        if (!TryStageAccounting(saleValue, out var saleAccounting))
            throw new InvalidDataException("Production accounting exceeds the supported range.");
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
            effects.Notify(new CampaignStrategicEventRequested(selectedEvent.Value.Key,
                _content.RuntimeRules.Manufacture.GetExternalId(production.Rule)));
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
        if (quantity <= 0) return;
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
            var logistics = CraftLogistics.LoadStarting(_content.RuntimeRules.Crafts[craft].Value, null);
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
        _content.RuntimeRules.Manufacture[state.Rule].Value.Time > 0
            ? state.Spent / _content.RuntimeRules.Manufacture[state.Rule].Value.Time
            : state.Amount;
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
    private void ReturnHeldResearchItem(BaseState owner, RuntimeResearchRule rule)
    {
        if (HoldsResearchItem(rule) && rule.NeededItem is { } id &&
            _content.RuntimeRules.Items.TryGet(id, out var item)) ChangeStock(owner.Items, item, 1);
    }

    private void RemoveResearchProject(BaseState owner, ResearchProjectState project)
    {
        owner.Scientists = checked(owner.Scientists + project.Assigned);
        ReturnHeldResearchItem(owner, _content.RuntimeRules.Research[project.Rule].Value);
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
