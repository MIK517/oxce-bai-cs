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
public sealed record CampaignProductionChoice(string RuleId, int Time, int Cost, string? UnavailableReason);
public sealed record CampaignProductionProject(
    string RuleId, int Assigned, int Spent, int Amount, int Produced, bool Infinite, bool Sell);
public sealed record CampaignResearchProduction(
    int ScientistsAvailable, int LaboratoriesAvailable, int EngineersAvailable, int WorkshopsAvailable,
    IReadOnlyList<CampaignResearchChoice> ResearchChoices, IReadOnlyList<CampaignResearchProject> Research,
    IReadOnlyList<CampaignProductionChoice> ProductionChoices, IReadOnlyList<CampaignProductionProject> Productions);

public sealed partial class CampaignState : ICampaignResearchProductionQuery
{
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
            var rule = _content.RuntimeRules.Research[project.Rule].Value;
            owner.Scientists = checked(owner.Scientists + project.Assigned);
            ReturnHeldResearchItem(owner, rule);
            owner.Research.RemoveAt(index);
            return new([new CampaignResearchChanged(owner.Id, command.RuleId, 0, true)]);
        }
        if (index < 0)
        {
            if (ResearchUnavailable(owner, handle, direct: true) is { } reason) return Blocked(reason);
            var rule = _content.RuntimeRules.Research[handle].Value;
            if (command.AssignedScientists > owner.Scientists ||
                command.AssignedScientists > FreeLaboratories(owner))
                return Blocked("STR_NOT_ENOUGH_LAB_SPACE");
            var cost = checked(rule.Cost * _random.NextInclusive(50, 150) / 100);
            if (rule.Cost > 0) cost = Math.Max(1, cost);
            if (HoldsResearchItem(rule))
            {
                if (rule.NeededItem is null || !_content.RuntimeRules.Items.TryGet(rule.NeededItem, out var item) ||
                    owner.Items.GetValueOrDefault(item) <= 0) return Blocked("Required research item is unavailable.");
                ChangeStock(owner.Items, item, -1);
            }
            owner.Research.Add(new(handle, 0, 0, cost));
            index = owner.Research.Count - 1;
        }
        var current = owner.Research[index];
        var delta = command.AssignedScientists - current.Assigned;
        if (delta > 0 && (delta > owner.Scientists || command.AssignedScientists > FreeLaboratories(owner) + current.Assigned))
            return Blocked("STR_NOT_ENOUGH_LAB_SPACE");
        owner.Scientists = checked(owner.Scientists - delta);
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
        if (command.Cancel)
        {
            if (index < 0) return Blocked("Production queue was not found.");
            var project = owner.Productions[index];
            var rule = _content.RuntimeRules.Manufacture[project.Rule].Value;
            owner.Engineers = checked(owner.Engineers + project.Assigned);
            if (rule.Refund) RefundProductionUnit(owner, rule);
            owner.Productions.RemoveAt(index);
            return new([new CampaignProductionChanged(owner.Id, command.RuleId, 0, project.Amount, true)]);
        }
        if (index < 0)
        {
            if (command.Amount == 0 && !command.Infinite) return Blocked("Production amount must be positive.");
            if (ProductionUnavailable(owner, handle) is { } reason) return Blocked(reason);
            var rule = _content.RuntimeRules.Manufacture[handle].Value;
            if (command.AssignedEngineers > owner.Engineers ||
                command.AssignedEngineers + rule.Space > AvailableWorkshops(owner))
                return Blocked("STR_NOT_ENOUGH_WORK_SPACE");
            if (StartProductionUnit(owner, rule) is { } startReason) return Blocked(startReason);
            owner.Productions.Add(new(handle, 0, 0, command.Amount, command.Infinite,
                command.Sell, false, new Dictionary<string, int>(StringComparer.Ordinal)));
            index = owner.Productions.Count - 1;
        }
        var current = owner.Productions[index];
        var nextAmount = command.Infinite ? Math.Max(1, command.Amount) : command.Amount;
        if (!command.Infinite && nextAmount <= Produced(current))
            return Blocked("Production amount cannot be less than completed units.");
        var delta = command.AssignedEngineers - current.Assigned;
        if (delta > 0 && (delta > owner.Engineers ||
            command.AssignedEngineers + UsedWorkshopSpace(owner, current) > AvailableWorkshops(owner)))
            return Blocked("STR_NOT_ENOUGH_WORK_SPACE");
        owner.Engineers = checked(owner.Engineers - delta);
        owner.Productions[index] = current with
        {
            Assigned = command.AssignedEngineers, Amount = nextAmount,
            Infinite = command.Infinite, Sell = command.Sell,
        };
        return new([new CampaignProductionChanged(owner.Id, command.RuleId,
            command.AssignedEngineers, nextAmount, false)]);
    }

    public CampaignResearchProduction QueryResearchProduction(int baseId)
    {
        lock (_transactionGate)
        {
            var owner = FindBase(baseId);
            var research = _content.RuntimeRules.Research.Rules
                .OrderBy(rule => rule.Value.ListOrder).ThenBy(rule => rule.Id, StringComparer.Ordinal)
                .Select(rule => new CampaignResearchChoice(rule.Id, rule.Value.Cost,
                    ResearchUnavailable(owner, _content.RuntimeRules.Research.GetRequired(rule.Id), direct: true)))
                .ToArray();
            var manufacture = _content.RuntimeRules.Manufacture.Rules
                .OrderBy(rule => rule.Value.ListOrder).ThenBy(rule => rule.Id, StringComparer.Ordinal)
                .Select(rule => new CampaignProductionChoice(rule.Id, rule.Value.Time, rule.Value.Cost,
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

    private string? ResearchUnavailable(BaseState owner, RuleHandle<ResearchRuleFamily> handle, bool direct)
    {
        var id = _content.RuntimeRules.Research.GetExternalId(handle);
        var rule = _content.RuntimeRules.Research[handle].Value;
        if (_researchRuleStatus.GetValueOrDefault(id) == 2) return "Research is permanently disabled.";
        if (direct && rule.Requirements.Count != 0) return "Research is only available as an indirect unlock.";
        var explicitlyUnlocked = _completedResearch.SelectMany(completed =>
            _content.RuntimeRules.Research.TryGet(completed, out var prior)
                ? _content.RuntimeRules.Research[prior].Value.Unlocks : []).Contains(id, StringComparer.Ordinal);
        if (!explicitlyUnlocked && !_debugMode && rule.Dependencies.Any(required => !_completedResearch.Contains(required)))
            return "Research dependencies are incomplete.";
        if (!_debugMode && rule.Requirements.Any(required => !_completedResearch.Contains(required)))
            return "Research requirements are incomplete.";
        if (!rule.Repeatable && _completedResearch.Contains(id) && !HasRemainingResearchReward(rule))
            return "Research is already complete.";
        if (owner.Research.Any(project => project.Rule == handle)) return "Research is already active.";
        if (!WeightsFit(rule.Events)) return "Strategic event weights exceed the supported range.";
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
        if (rule.RandomProducedItems.Sum(set => (long)set.Weight) is <= 0 or > int.MaxValue &&
            rule.RandomProducedItems.Count != 0)
            return "Random production weights exceed the supported range.";
        if (rule.RandomProducedItems.SelectMany(set => set.Items).Any(pair =>
            pair.Value < 0 || !_content.RuntimeRules.Items.TryGet(pair.Key, out _)))
            return "Random production references an invalid item quantity or ID.";
        if (!WeightsFit(rule.Events)) return "Strategic event weights exceed the supported range.";
        return null;
    }

    private string? PreflightResearchProduction(CampaignTimeTrigger highest)
    {
        if (highest >= CampaignTimeTrigger.OneDay)
        {
            if (_content.RuntimeRules.Research.Rules.Any(rule => !WeightsFit(rule.Value.Events)))
                return "Strategic event weights exceed the supported range.";
            foreach (var owner in _bases)
                foreach (var project in owner.Research)
                {
                    if ((long)project.Spent + project.Assigned > int.MaxValue)
                        return "Research progress exceeds the supported range.";
                    if (!WeightsFit(_content.RuntimeRules.Research[project.Rule].Value.Events))
                        return "Strategic event weights exceed the supported range.";
                }
        }
        if (highest >= CampaignTimeTrigger.OneHour)
            foreach (var owner in _bases)
                foreach (var production in owner.Productions)
                {
                    if ((long)production.Spent + production.Assigned > int.MaxValue)
                        return "Production progress exceeds the supported range.";
                    if (!WeightsFit(_content.RuntimeRules.Manufacture[production.Rule].Value.Events))
                        return "Strategic event weights exceed the supported range.";
                }
        return null;
    }

    private void AdvanceResearchDaily(BaseState owner, TimeEffects effects)
    {
        var finished = new List<ResearchProjectState>();
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
            var discoveries = CompleteResearch(owner, id, rule, effects);
            effects.Notify(new CampaignResearchCompleted(owner.Id, id, discoveries));
        }
    }

    private ReadOnlyCollection<string> CompleteResearch(
        BaseState owner, string id, RuntimeResearchRule rule, TimeEffects effects)
    {
        var discoveries = new List<string>();
        var bonus = SelectResearchReward(rule);
        if (bonus is not null)
        {
            AddFinishedResearch(owner, bonus, discoveries);
            if (_content.RuntimeRules.Research.TryGet(bonus, out var bonusHandle))
            {
                var bonusRule = _content.RuntimeRules.Research[bonusHandle].Value;
                if (bonusRule.Lookup.Length != 0) AddFinishedResearch(owner, bonusRule.Lookup, discoveries);
                ApplyResearchSideEffects(owner, bonusRule, bonus, effects);
            }
        }
        AddFinishedResearch(owner, id, discoveries);
        if (rule.Lookup.Length != 0) AddFinishedResearch(owner, rule.Lookup, discoveries);
        ApplyResearchSideEffects(owner, rule, id, effects);
        return Array.AsReadOnly(discoveries.ToArray());
    }

    private void AddFinishedResearch(BaseState owner, string initial, List<string> discoveries)
    {
        var queue = new List<string> { initial };
        for (var offset = 0; offset < queue.Count; offset++)
        {
            var id = queue[offset];
            if (!_content.RuntimeRules.Research.TryGet(id, out var handle) ||
                _researchRuleStatus.GetValueOrDefault(id) == 2) continue;
            var rule = _content.RuntimeRules.Research[handle].Value;
            var wasComplete = _completedResearch.Contains(id);
            if (!wasComplete)
            {
                if (!rule.Repeatable)
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
            if (wasComplete && !HasProtectedUnlock(rule)) continue;
            foreach (var candidate in _content.RuntimeRules.Research.Rules)
            {
                if (candidate.Value.Cost != 0 || queue.Contains(candidate.Id, StringComparer.Ordinal)) continue;
                if (ResearchAvailableWithoutBase(candidate.Id, candidate.Value) &&
                    (candidate.Value.Requirements.Count == 0 || rule.Unlocks.Contains(candidate.Id, StringComparer.Ordinal)))
                    queue.Add(candidate.Id);
            }
        }
    }

    private bool ResearchAvailableWithoutBase(string id, RuntimeResearchRule rule)
    {
        if (_researchRuleStatus.GetValueOrDefault(id) == 2) return false;
        var unlocked = _completedResearch.SelectMany(completed =>
            _content.RuntimeRules.Research.TryGet(completed, out var prior)
                ? _content.RuntimeRules.Research[prior].Value.Unlocks : []).Contains(id, StringComparer.Ordinal);
        return (unlocked || rule.Dependencies.All(_completedResearch.Contains)) &&
            rule.Requirements.All(_completedResearch.Contains) &&
            (!(_completedResearch.Contains(id) && !rule.Repeatable) || HasRemainingResearchReward(rule));
    }

    private string? SelectResearchReward(RuntimeResearchRule rule)
    {
        var candidates = rule.GetOneFree.Where(IsNewResearch).ToList();
        foreach (var protectedGroup in rule.GetOneFreeProtected)
            if (_completedResearch.Contains(protectedGroup.Prerequisite))
                candidates.AddRange(protectedGroup.Topics.Where(IsNewResearch));
        if (candidates.Count == 0) return null;
        return rule.SequentialGetOneFree ? candidates[0] : candidates[_random.NextExclusive(candidates.Count)];
        bool IsNewResearch(string id) => !_completedResearch.Contains(id) &&
            _researchRuleStatus.GetValueOrDefault(id) != 2;
    }

    private bool HasRemainingResearchReward(RuntimeResearchRule rule) =>
        rule.GetOneFree.Any(IsNew) || rule.GetOneFreeProtected.Any(group => group.Topics.Any(IsNew));
    private bool HasProtectedUnlock(RuntimeResearchRule rule) => rule.Unlocks.Any(id =>
        _content.RuntimeRules.Research.TryGet(id, out var handle) &&
        _content.RuntimeRules.Research[handle].Value.Requirements.Count != 0 &&
        !_completedResearch.Contains(id) && _researchRuleStatus.GetValueOrDefault(id) != 2);
    private bool IsNew(string id) => !_completedResearch.Contains(id) &&
        _researchRuleStatus.GetValueOrDefault(id) != 2;

    private void ApplyResearchSideEffects(
        BaseState owner, RuntimeResearchRule rule, string source, TimeEffects effects)
    {
        if (rule.SpawnedItem.Length != 0 && rule.SpawnedItemCount != 0 &&
            _content.RuntimeRules.Items.TryGet(rule.SpawnedItem, out var item))
            ChangeStock(owner.Items, item, rule.SpawnedItemCount);
        foreach (var id in rule.SpawnedItemList)
            if (_content.RuntimeRules.Items.TryGet(id, out var spawned)) ChangeStock(owner.Items, spawned, 1);
        foreach (var id in rule.IncreaseCounters)
            if (id.Length != 0)
                _nextIds[id] = checked(_nextIds.TryGetValue(id, out var value) ? value + 1 : 2);
        foreach (var id in rule.DecreaseCounters)
            if (id.Length != 0)
                _nextIds[id] = _nextIds.TryGetValue(id, out var value) ? Math.Max(1, value - 1) : 1;
        if (rule.SpawnedEvent.Length != 0)
            effects.Notify(new CampaignStrategicEventRequested(rule.SpawnedEvent, source));
        if (SelectWeightedEvent(rule.Events) is { } eventId)
            effects.Notify(new CampaignStrategicEventRequested(eventId, source));
        foreach (var project in _bases.SelectMany(baseState => baseState.Research)
            .Where(project => rule.Disables.Contains(
                _content.RuntimeRules.Research.GetExternalId(project.Rule), StringComparer.Ordinal)).ToArray())
            foreach (var baseState in _bases.Where(baseState => baseState.Research.Contains(project)))
            {
                baseState.Scientists = checked(baseState.Scientists + project.Assigned);
                baseState.Research.Remove(project);
            }
    }

    private void AdvanceProductionHourly(TimeEffects effects)
    {
        foreach (var owner in _bases)
        {
            foreach (var production in owner.Productions.ToArray())
            {
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
                    stop = StartProductionUnit(owner, _content.RuntimeRules.Manufacture[progressed.Rule].Value);
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

    private string? StartProductionUnit(BaseState owner, RuntimeManufactureRule rule)
    {
        if (_funds[^1] < rule.Cost) return "STR_NOT_ENOUGH_MONEY";
        foreach (var material in rule.RequiredMaterials)
        {
            if (material.Item is { } item && owner.Items.GetValueOrDefault(item) < material.Quantity)
                return "STR_NOT_ENOUGH_MATERIALS";
            if (material.Craft is { } craft && owner.Crafts.Count(value => value.Rule == craft) < material.Quantity)
                return "STR_NOT_ENOUGH_MATERIALS";
        }
        _funds[^1] = checked(_funds[^1] - rule.Cost);
        _expenditures[^1] = checked(_expenditures[^1] + rule.Cost);
        foreach (var material in rule.RequiredMaterials)
        {
            if (material.Item is { } item) ChangeStock(owner.Items, item, -material.Quantity);
            if (material.Craft is { } craft)
                for (var count = 0; count < material.Quantity; count++)
                {
                    var removed = owner.Crafts.First(value => value.Rule == craft);
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
        return null;
    }

    private void RefundProductionUnit(BaseState owner, RuntimeManufactureRule rule)
    {
        _funds[^1] = checked(_funds[^1] + rule.Cost);
        _incomes[^1] = checked(_incomes[^1] + rule.Cost);
        foreach (var material in rule.RequiredMaterials)
            if (material.Item is { } item) ChangeStock(owner.Items, item, material.Quantity);
    }

    private void CompleteProductionUnit(BaseState owner, ProductionState production, TimeEffects effects)
    {
        var rule = _content.RuntimeRules.Manufacture[production.Rule].Value;
        foreach (var material in rule.ProducedMaterials)
        {
            if (material.Item is { } item)
            {
                if (production.Sell)
                {
                    var value = checked((long)_content.RuntimeRules.Items[item].Value.CostSell * material.Quantity);
                    _funds[^1] = checked(_funds[^1] + value);
                    _incomes[^1] = checked(_incomes[^1] + value);
                }
                else DeliverProducedItem(owner, material.Id, material.Quantity, rule);
            }
            else if (material.Craft is { } craft) ProduceCraft(owner, craft, material.Quantity, rule);
        }
        if (rule.RandomProducedItems.Count != 0)
        {
            var total = rule.RandomProducedItems.Sum(set => (long)set.Weight);
            var roll = _random.NextInclusive(1, checked((int)total));
            foreach (var set in rule.RandomProducedItems)
            {
                roll -= set.Weight;
                if (roll > 0) continue;
                foreach (var item in set.Items)
                {
                    DeliverProducedItem(owner, item.Key, item.Value, rule);
                    production.RandomProductionInfo[item.Key] =
                        checked(production.RandomProductionInfo.GetValueOrDefault(item.Key) + item.Value);
                }
                break;
            }
        }
        ProducePerson(owner, rule);
        if (SelectWeightedEvent(rule.Events) is { } eventId)
            effects.Notify(new CampaignStrategicEventRequested(eventId,
                _content.RuntimeRules.Manufacture.GetExternalId(production.Rule)));
        _researchScores[^1] = checked(_researchScores[^1] + rule.Points);
    }

    private string? SelectWeightedEvent(IReadOnlyDictionary<string, ulong> events)
    {
        var total = events.Aggregate(0UL, (value, pair) => checked(value + pair.Value));
        if (total == 0) return null;
        if (total > int.MaxValue) throw new InvalidDataException("Strategic event weights exceed the supported range.");
        var roll = _random.NextInclusive(1, (int)total);
        foreach (var pair in events)
        {
            if ((ulong)roll <= pair.Value) return pair.Key;
            roll -= checked((int)pair.Value);
        }
        throw new InvalidOperationException("Weighted event selection did not produce a result.");
    }

    private static bool WeightsFit(IReadOnlyDictionary<string, ulong> events)
    {
        ulong total = 0;
        foreach (var value in events.Values)
        {
            if (value > int.MaxValue || total > (ulong)int.MaxValue - value) return false;
            total += value;
        }
        return true;
    }

    private void DeliverProducedItem(BaseState owner, string id, int quantity, RuntimeManufactureRule rule)
    {
        if (quantity <= 0) return;
        var transferHours = rule.TransferTimes.Count == 0 ? 0 : Math.Max(0, rule.TransferTimes[0]);
        if (transferHours == 0) ChangeStock(owner.Items, _content.RuntimeRules.Items.GetRequired(id), quantity);
        else
        {
            var transferId = NextId("oxcePortTransfer");
            owner.Transfers.Add(new(transferId, transferHours, CampaignTransferKind.Item, id, quantity)
            {
                PreservationKey = FormattableString.Invariant($"created:{Identity.Id}:transfer:{transferId}"),
            });
        }
    }

    private void ProduceCraft(BaseState owner, RuleHandle<CraftRuleFamily> craft, int quantity, RuntimeManufactureRule rule)
    {
        var id = _content.RuntimeRules.Crafts.GetExternalId(craft);
        var transferHours = rule.TransferTimes.Count < 3 ? 0 : Math.Max(0, rule.TransferTimes[2]);
        for (var count = 0; count < quantity; count++)
        {
            var craftId = NextId(id);
            var key = FormattableString.Invariant($"created:{Identity.Id}:craft:{id}:{craftId}");
            var logistics = CraftLogistics.LoadStarting(_content.RuntimeRules.Crafts[craft].Value, null);
            var value = new CraftSnapshot(id, craftId) { PreservationKey = key, Logistics = logistics };
            if (transferHours == 0)
                owner.Crafts.Add(new(craft, craftId) { PreservationKey = key, Logistics = logistics });
            else
            {
                var transferId = NextId("oxcePortTransfer");
                owner.Transfers.Add(new(transferId, transferHours, CampaignTransferKind.Craft,
                    id, 1, Craft: value)
                {
                    PreservationKey = FormattableString.Invariant($"created:{Identity.Id}:transfer:{transferId}"),
                });
            }
        }
    }

    private void ProducePerson(BaseState owner, RuntimeManufactureRule rule)
    {
        if (rule.SpawnedPersonType.Length == 0) return;
        var hours = Math.Max(1, rule.TransferTimes.Count < 2 ? 24 : rule.TransferTimes[1]);
        var transferId = NextId("oxcePortTransfer");
        var key = FormattableString.Invariant($"created:{Identity.Id}:transfer:{transferId}");
        if (rule.SpawnedPersonType == "STR_SCIENTIST")
            owner.Transfers.Add(new(transferId, hours, CampaignTransferKind.Scientist, "", 1)
            { PreservationKey = key });
        else if (rule.SpawnedPersonType == "STR_ENGINEER")
            owner.Transfers.Add(new(transferId, hours, CampaignTransferKind.Engineer, "", 1)
            { PreservationKey = key });
        else
        {
            var handle = _content.RuntimeRules.Soldiers.GetRequired(rule.SpawnedPersonType);
            var definition = _content.RuntimeRules.Soldiers[handle].Value;
            var soldierId = NextId("STR_SOLDIER");
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
            owner.Transfers.Add(new(transferId, hours, CampaignTransferKind.Soldier,
                rule.SpawnedPersonType, 1, Soldier: soldier) { PreservationKey = key });
        }
    }

    private int FreeLaboratories(BaseState owner) =>
        Math.Max(0, owner.Facilities.Where(f => f.BuildTime == 0)
            .Sum(f => FacilityRule(f).Laboratories) - owner.Research.Sum(project => project.Assigned));
    private int AvailableWorkshops(BaseState owner) =>
        owner.Facilities.Where(f => f.BuildTime == 0).Sum(f => FacilityRule(f).Workshops);
    private int UsedWorkshopSpace(BaseState owner, ProductionState? exclude = null) =>
        owner.Productions.Where(project => project != exclude && (project.Assigned != 0 || project.Spent != 0))
            .Sum(project => checked(project.Assigned + _content.RuntimeRules.Manufacture[project.Rule].Value.Space));
    private int Produced(ProductionState state) =>
        _content.RuntimeRules.Manufacture[state.Rule].Value.Time > 0
            ? state.Spent / _content.RuntimeRules.Manufacture[state.Rule].Value.Time
            : state.Amount;
    private static bool HoldsResearchItem(RuntimeResearchRule rule) =>
        rule.NeedItem && (rule.DestroyItem || rule.ReturnsItem);
    private void ReturnHeldResearchItem(BaseState owner, RuntimeResearchRule rule)
    {
        if (HoldsResearchItem(rule) && rule.NeededItem is { } id &&
            _content.RuntimeRules.Items.TryGet(id, out var item)) ChangeStock(owner.Items, item, 1);
    }

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
}
