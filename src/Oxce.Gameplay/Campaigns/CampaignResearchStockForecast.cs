using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

internal sealed record DailyResearchStockForecast(
    IReadOnlyDictionary<int, IReadOnlyDictionary<string, int>> StockChanges,
    // Exact projections retain native order. Stock-only projections keep reusable
    // items in order and canonicalize the other arrivals, without merging transfers.
    IReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, int>>> Arrivals)
{
    public string? UnavailableReason { get; init; }
    public int? NextTransferId { get; init; }
    public IReadOnlyList<CustomCounterChange> CounterChanges { get; init; } = [];
    public IReadOnlySet<string>? CompletedResearch { get; init; }
    public IReadOnlyDictionary<string, int>? ResearchStatuses { get; init; }
    public IReadOnlyDictionary<int, IReadOnlySet<string>>? ActiveResearch { get; init; }
    // Scalar metadata is supplied only for exact discovery projections.
    public long? ScoreChange { get; init; }
    // Relative extrema after every ordered score mutation, including the initial zero.
    public long? MinimumScorePrefix { get; init; }
    public long? MaximumScorePrefix { get; init; }
    public long Work { get; init; }
    public long? DiaryEntries { get; init; }
    // World-only arrival upper quantities and conservative maximum-minus-minimum
    // deltas. Arrival identities, order and transfer counts agree across states.
    public IReadOnlyDictionary<int, IReadOnlyDictionary<string, int>> ArrivalUncertainty { get; init; } =
        FrozenDictionary<int, IReadOnlyDictionary<string, int>>.Empty;
    // World-only bounded exact states with common refunds/counters/arrival order.
    // Summary discovery identities are guaranteed across every retained state.
    public IReadOnlyList<DailyResearchStockForecast> DiscoveryAlternatives { get; init; } = [];
}

public sealed partial class CampaignState
{
    private static readonly DailyResearchStockForecast EmptyResearchStockForecast = new(
        new ReadOnlyDictionary<int, IReadOnlyDictionary<string, int>>(new Dictionary<int, IReadOnlyDictionary<string, int>>()),
        new ReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, int>>>(new Dictionary<int, IReadOnlyList<KeyValuePair<string, int>>>()));

    private bool TryForecastDailyStockBranch(ref long work, out DailyResearchStockForecast forecast, bool includeDiscoveries,
        int[] choices, out IReadOnlyList<string> pendingChoices)
    {
        pendingChoices = [];
        var choiceIndex = 0;
        forecast = EmptyResearchStockForecast;
        if (!includeDiscoveries && DailyItemsUnchanged(ref work)) return true;
        var rules = _content.RuntimeRules;
        work += rules.Research.Count + (long)_completedResearch.Count + _researchRuleStatus.Count;
        if (work > 1_000_000) return false;
        var completed = new HashSet<string>(_completedResearch, StringComparer.Ordinal);
        var statuses = new Dictionary<string, int>(_researchRuleStatus, StringComparer.Ordinal);
        var discovery = new ResearchDiscoveryProgress(rules, completed, statuses);
        Dictionary<int, List<ResearchProjectState>> projects = [];
        Dictionary<int, Dictionary<string, int>> changes = [];
        Dictionary<int, List<KeyValuePair<string, int>>> arrivals = [];
        List<CustomCounterChange>? counters = null;
        long diaryEntries = 0;
        long scoreChange = 0;
        long minimumScorePrefix = 0;
        long maximumScorePrefix = 0;
        foreach (var owner in _bases)
        {
            work += owner.Research.Count;
            if (work > 1_000_000) return false;
            projects.Add(owner.Id, new(owner.Research));
            changes.Add(owner.Id, new(StringComparer.Ordinal));
            arrivals.Add(owner.Id, []);
        }
        foreach (var owner in _bases)
        {
            var local = projects[owner.Id];
            work += local.Count * 4L;
            if (work > 1_000_000) return false;
            for (var index = 0; index < local.Count; index++)
            {
                var project = local[index];
                if ((long)project.Spent + project.Assigned > int.MaxValue) return false;
                local[index] = project with { Spent = project.Spent + project.Assigned };
            }
            var finished = local.Where(project => project.Spent >= project.Cost).ToArray();
            var provided = new HashSet<string>(StringComparer.Ordinal);
            work += owner.Facilities.Count;
            foreach (var facility in owner.Facilities)
            {
                var functions = rules.Facilities[facility.Rule].Value.ProvidedBaseFunctions;
                work += functions.Count;
                if (work > 1_000_000) return false;
                // Daily construction advances before research in this base.
                if (facility.BuildTime <= 1) provided.UnionWith(functions);
            }
            foreach (var country in _countries)
            {
                var rule = rules.Countries[country.Rule].Value;
                work += 1L + rule.Areas.Count + rule.ProvidedBaseFunctions.Count;
                if (work > 1_000_000) return false;
                if (!rule.Areas.Any(area => AreaContains(area, owner.Longitude, owner.Latitude))) continue;
                provided.UnionWith(rule.ProvidedBaseFunctions);
                break;
            }
            foreach (var region in _regions)
            {
                var rule = rules.Regions[region.Rule].Value;
                work += 1L + rule.Areas.Count + rule.ProvidedBaseFunctions.Count;
                if (work > 1_000_000) return false;
                if (!rule.Areas.Any(area => AreaContains(area, owner.Longitude, owner.Latitude))) continue;
                provided.UnionWith(rule.ProvidedBaseFunctions);
                break;
            }
            HashSet<RuleHandle<ResearchRuleFamily>> sideEffects = [];
            foreach (var project in finished)
            {
                var rule = rules.Research[project.Rule].Value;
                Recover(owner.Id, rule, rules.Research.GetExternalId(project.Rule), finished: true, dailyCompletion: true);
                work += local.Count;
                if (work > 1_000_000) return false;
                local.Remove(project);
                RecordDiary(rules.Research.GetExternalId(project.Rule));
                RecordDiary(rule.Lookup);
                work += rule.GetOneFree.Count;
                foreach (var group in rule.GetOneFreeProtected) work += 1L + group.Topics.Count;
                if (work > 1_000_000) return false;
                var candidates = discovery.RewardCandidates(rule);
                work += candidates.Count;
                if (work > 1_000_000) return false;
                var selected = 0;
                if (candidates.Count > 1 && !rule.SequentialGetOneFree && candidates.Any(id => id != candidates[0]))
                {
                    if (choiceIndex >= choices.Length)
                    {
                        pendingChoices = candidates;
                        return false;
                    }
                    selected = choices[choiceIndex++];
                    if ((uint)selected >= (uint)candidates.Count) return false;
                }
                if (candidates.Count != 0 && rules.Research.TryGet(candidates[selected], out var bonus))
                {
                    var reward = rules.Research[bonus];
                    RecordDiary(reward.Id);
                    if (!Discover(reward.Id, ref work)) return false;
                    if (reward.Value.Lookup.Length != 0)
                    {
                        RecordDiary(reward.Value.Lookup);
                        if (!Discover(reward.Value.Lookup, ref work)) return false;
                    }
                    sideEffects.Add(bonus);
                }
                var id = rules.Research.GetExternalId(project.Rule);
                if (!Discover(id, ref work) || rule.Lookup.Length != 0 && !Discover(rule.Lookup, ref work)) return false;
                sideEffects.Add(project.Rule);
            }

            bool Discover(string id, ref long projectionWork)
            {
                long localWork = 0;
                var supported = discovery.Advance(id, (candidateId, candidate) =>
                {
                    var handle = rules.Research.GetRequired(candidateId);
                    var active = local.Any(project => project.Rule == handle);
                    var availableItem = !candidate.NeedItem || candidate.NeededItem is { } needed &&
                        rules.Items.TryGet(needed, out var item) &&
                        (long)owner.Items.GetValueOrDefault(item) + changes[owner.Id].GetValueOrDefault(needed) != 0;
                    return (!active && availableItem && candidate.RequiredBaseFunctions.All(provided.Contains),
                        local.Count + (long)candidate.RequiredBaseFunctions.Count + 1);
                }, (discoveredId, points, _) =>
                {
                    scoreChange += points;
                    minimumScorePrefix = Math.Min(minimumScorePrefix, scoreChange);
                    maximumScorePrefix = Math.Max(maximumScorePrefix, scoreChange);
                    if (discoveredId != id) diaryEntries++;
                }, ref localWork, 1_000_000 - projectionWork);
                projectionWork += localWork;
                return supported;
            }

            void RecordDiary(string id)
            {
                if (!_debugMode && rules.Research.TryGet(id, out _) && !completed.Contains(id) && statuses.GetValueOrDefault(id) != 2)
                    diaryEntries++;
            }
            work += rules.Research.Count;
            if (work > 1_000_000) return false;
            foreach (var entry in rules.Research.Rules)
            {
                if (!sideEffects.Contains(rules.Research.GetRequired(entry.Id))) continue;
                work += entry.Value.GetOneFree.Count + (long)entry.Value.Unlocks.Count;
                foreach (var group in entry.Value.GetOneFreeProtected) work += 1L + group.Topics.Count;
                if (work > 1_000_000) return false;
                if (!discovery.HasRemainingReward(entry.Value) && !discovery.HasProtectedUnlock(entry.Value))
                    foreach (var other in _bases)
                    {
                        work += projects[other.Id].Count * 2L;
                        if (work > 1_000_000) return false;
                        var project = projects[other.Id].FirstOrDefault(project => project.Rule == rules.Research.GetRequired(entry.Id));
                        if (project is null) continue;
                        Recover(other.Id, entry.Value, entry.Id, project.Spent >= project.Cost, dailyCompletion: false);
                        projects[other.Id].Remove(project);
                    }
                work += entry.Value.SpawnedItemList.Count + 1L;
                if (work > 1_000_000) return false;
                if (entry.Value.SpawnedItem.Length != 0 && rules.Items.TryGet(entry.Value.SpawnedItem, out _))
                    arrivals[owner.Id].Add(new(entry.Value.SpawnedItem, Math.Max(1, entry.Value.SpawnedItemCount)));
                foreach (var id in entry.Value.SpawnedItemList)
                    if (rules.Items.TryGet(id, out _)) arrivals[owner.Id].Add(new(id, 1));
                work += entry.Value.IncreaseCounters.Count + (long)entry.Value.DecreaseCounters.Count;
                if (work > 1_000_000) return false;
                foreach (var id in entry.Value.IncreaseCounters)
                    if (id.Length != 0) (counters ??= []).Add(new(id, true));
                foreach (var id in entry.Value.DecreaseCounters)
                    if (id.Length != 0) (counters ??= []).Add(new(id, false));
            }
        }
        // time1Day removes disabled projects after all base loops, even when no
        // topic completed today. These cancellations can return held fuel.
        foreach (var owner in _bases)
        {
            work += projects[owner.Id].Count;
            if (work > 1_000_000) return false;
            foreach (var project in projects[owner.Id])
            {
                var entry = rules.Research[project.Rule];
                if (statuses.GetValueOrDefault(entry.Id) == 2)
                    Recover(owner.Id, entry.Value, entry.Id, project.Spent >= project.Cost, dailyCompletion: false);
            }
        }
        var arrivalCount = arrivals.Values.Sum(items => (long)items.Count);
        int? nextTransfer = null;
        if (arrivalCount != 0)
        {
            work += _bases.Sum(owner => (long)owner.Transfers.Count);
            if (work > 1_000_000) return false;
            if ((long)NextTransferId() + arrivalCount > int.MaxValue)
            {
                forecast = EmptyResearchStockForecast with { UnavailableReason = "Research arrivals exhaust the transfer identity range." };
                return false;
            }
            nextTransfer = (int)((long)NextTransferId() + arrivalCount);
        }
        if (!includeDiscoveries && !CanonicalizeResearchStockArrivals(arrivals, ref work)) return false;
        if (includeDiscoveries)
        {
            work += completed.Count + (long)statuses.Count + projects.Values.Sum(local => (long)local.Count) + projects.Count;
            if (work > 1_000_000) return false;
        }
        forecast = new(new ReadOnlyDictionary<int, IReadOnlyDictionary<string, int>>(changes.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyDictionary<string, int>)new ReadOnlyDictionary<string, int>(pair.Value))),
            new ReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, int>>>(arrivals.ToDictionary(
                pair => pair.Key, pair => (IReadOnlyList<KeyValuePair<string, int>>)pair.Value.AsReadOnly())))
        {
            NextTransferId = nextTransfer,
            CounterChanges = counters?.AsReadOnly() ?? (IReadOnlyList<CustomCounterChange>)[],
            CompletedResearch = includeDiscoveries ? completed.ToFrozenSet(StringComparer.Ordinal) : null,
            ResearchStatuses = includeDiscoveries ? statuses.ToFrozenDictionary(StringComparer.Ordinal) : null,
            ActiveResearch = includeDiscoveries ? projects.ToFrozenDictionary(pair => pair.Key, pair =>
                (IReadOnlySet<string>)pair.Value.Select(project => rules.Research.GetExternalId(project.Rule))
                    .Where(id => statuses.GetValueOrDefault(id) != 2).ToFrozenSet(StringComparer.Ordinal)) : null,
            ScoreChange = includeDiscoveries ? scoreChange : null,
            MinimumScorePrefix = includeDiscoveries ? minimumScorePrefix : null,
            MaximumScorePrefix = includeDiscoveries ? maximumScorePrefix : null,
            Work = work,
            DiaryEntries = includeDiscoveries ? diaryEntries : null,
        };
        return true;

        void Recover(int baseId, RuntimeResearchRule rule, string id, bool finished, bool dailyCompletion)
        {
            var recovered = ResearchRecoveredItems(rule, id, finished, dailyCompletion);
            if (recovered.Held is { } held) Add(held, 1);
            if (recovered.Corpse is { } corpse && recovered.Corpses != 0) Add(corpse, recovered.Corpses);

            void Add(RuleHandle<ItemRuleFamily> item, int quantity)
            {
                var itemId = rules.Items.GetExternalId(item);
                changes[baseId][itemId] = checked(changes[baseId].GetValueOrDefault(itemId) + quantity);
            }
        }
    }

    private bool CanonicalizeResearchStockArrivals(Dictionary<int, List<KeyValuePair<string, int>>> arrivals, ref long work)
    {
        var rules = _content.RuntimeRules;
        foreach (var owner in arrivals)
        {
            var count = owner.Value.Count;
            // Charge the scan before reading labels. An already ordered reusable
            // list needs neither a sort nor scratch allocations.
            work += 1L + count;
            if (work > 1_000_000) return false;
            if (count < 2) continue;
            var otherCount = 0;
            foreach (var item in owner.Value)
                if (rules.Items[rules.Items.GetRequired(item.Key)].Value.BattleType != 0) otherCount++;
            if (otherCount == 0) continue;
            var levels = 0;
            for (var remaining = otherCount; remaining > 1; remaining = (remaining + 1) / 2) levels++;
            // Bound allocations, sort comparisons and copying before work.
            work += count * 4L + otherCount * (long)levels;
            if (work > 1_000_000) return false;
            List<KeyValuePair<string, int>> reusable = new(count - otherCount), other = new(otherCount);
            foreach (var item in owner.Value)
                (rules.Items[rules.Items.GetRequired(item.Key)].Value.BattleType == 0 ? reusable : other).Add(item);
            other.Sort(static (first, second) =>
            {
                var identity = StringComparer.Ordinal.Compare(first.Key, second.Key);
                return identity != 0 ? identity : first.Value.CompareTo(second.Value);
            });
            // ItemsArrivingState skips non-BT_NONE items. They cannot interrupt
            // reuse, but two different reusable clips can: retain that subsequence.
            owner.Value.Clear();
            owner.Value.AddRange(reusable);
            owner.Value.AddRange(other);
        }
        return true;
    }

    bool ICampaignResearchPreflight.HasDisabledDailyProjects(ref long work)
    {
        foreach (var owner in _bases)
            foreach (var project in owner.Research)
            {
                if (++work > 1_000_000) return true;
                if (_researchRuleStatus.GetValueOrDefault(_content.RuntimeRules.Research.GetExternalId(project.Rule)) == 2) return true;
            }
        return false;
    }

    private bool DailyItemsUnchanged(ref long work)
    {
        HashSet<string> sideEffects = new(StringComparer.Ordinal);
        var rules = _content.RuntimeRules;
        foreach (var owner in _bases)
            foreach (var project in owner.Research)
            {
                if (++work > 1_000_000) return false;
                // Cancellation in any base can refund a held item in another base.
                if (HoldsResearchItem(rules.Research[project.Rule].Value)) return false;
                if ((long)project.Spent + project.Assigned < project.Cost) continue;
                var entry = rules.Research[project.Rule];
                sideEffects.Add(entry.Id);
                work += entry.Value.GetOneFree.Count;
                if (work > 1_000_000) return false;
                sideEffects.UnionWith(entry.Value.GetOneFree);
                foreach (var group in entry.Value.GetOneFreeProtected)
                {
                    work += group.Topics.Count;
                    if (work > 1_000_000) return false;
                    sideEffects.UnionWith(group.Topics);
                }
            }
        foreach (var id in sideEffects)
        {
            if (++work > 1_000_000) return false;
            if (!rules.Research.TryGet(id, out var handle)) continue;
            var rule = rules.Research[handle].Value;
            work += rule.IncreaseCounters.Count + (long)rule.DecreaseCounters.Count;
            if (work > 1_000_000) return false;
            foreach (var name in rule.IncreaseCounters)
                if (name.Length != 0) return false;
            foreach (var name in rule.DecreaseCounters)
                if (name.Length != 0) return false;
            if (rule.SpawnedItem.Length != 0 && rules.Items.TryGet(rule.SpawnedItem, out _)) return false;
            foreach (var item in rule.SpawnedItemList)
            {
                if (++work > 1_000_000) return false;
                if (rules.Items.TryGet(item, out _)) return false;
            }
        }
        // Lookup and automatic discoveries use addFinishedResearch only; primary
        // item/event side effects belong to direct completions and the free topic.
        return work <= 1_000_000;
    }
}
