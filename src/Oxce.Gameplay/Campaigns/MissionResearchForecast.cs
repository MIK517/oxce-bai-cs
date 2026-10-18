using System.Collections.Frozen;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    /// <summary>Ordered, bounded alternative outcomes of world research grants; never consumes RNG.</summary>
    internal sealed class MissionResearchForecast
    {
        private const long MaximumWork = ForecastLimits.Work;
        private const string WorkReason = "Mission research exceeds the supported discovery work limit.";
        private readonly CampaignState _campaign;
        private readonly RuntimeRuleCatalog _rules;
        private readonly int _completedBuildTime;
        private readonly DailyResearchStockForecast? _precedingResearch;
        private readonly IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? _firstBaseItems;
        private IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? _otherFirstBaseItemUncertainty;
        private List<Outcome> _outcomes;
        private long _work;
        private readonly long _functionWork;
        private readonly long _precedingProductionMinimum;
        private readonly long _precedingProductionMaximum;
        private readonly int _precedingMissionBirths;
        private readonly IReadOnlySet<int>? _precedingMissionRemovals;
        private string? _reason;
        private IReadOnlySet<string> _guaranteedResearch;
        private IReadOnlySet<string> _possibleResearch;
        private IReadOnlySet<string> _guaranteedExplicitTopics = new HashSet<string>(StringComparer.Ordinal);
        private IReadOnlySet<string> _possibleExplicitTopics = new HashSet<string>(StringComparer.Ordinal);

        internal MissionResearchForecast(CampaignState campaign, CampaignTimeTrigger highest = CampaignTimeTrigger.FiveSeconds,
            DailyResearchStockForecast? precedingResearch = null,
            IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? firstBaseItems = null, long precedingWork = 0,
            int precedingMissionBirths = 0, IReadOnlySet<int>? precedingMissionRemovals = null,
            IReadOnlyList<RuleHandle<ItemRuleFamily>>? uncertainFirstBaseItems = null,
            IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? firstBaseItemUncertainty = null)
        {
            _campaign = campaign;
            _rules = campaign.Content.RuntimeRules;
            _precedingResearch = precedingResearch;
            _firstBaseItems = firstBaseItems;
            _otherFirstBaseItemUncertainty = firstBaseItemUncertainty;
            _precedingMissionBirths = precedingMissionBirths;
            _precedingMissionRemovals = precedingMissionRemovals;
            // Construction decrements positive timers before the daily research
            // and world callers. The real GrantReason runs after that transition.
            _completedBuildTime = highest >= CampaignTimeTrigger.OneDay ? 1 : 0;
            var completed = precedingResearch?.CompletedResearch ?? campaign._completedResearch;
            var statuses = precedingResearch?.ResearchStatuses ?? campaign._researchRuleStatus;
            _work = Math.Max(precedingResearch?.Work ?? 0, precedingWork) + completed.Count + (long)statuses.Count +
                (firstBaseItems?.Count ?? 0) + (precedingMissionRemovals?.Count ?? 0);
            _outcomes = [new(new(completed, StringComparer.Ordinal),
                new(statuses, StringComparer.Ordinal), campaign._researchScores[^1] + (precedingResearch?.ScoreChange ?? 0),
                precedingResearch?.DiaryEntries ?? 0, new(StringComparer.Ordinal), activeResearch: precedingResearch?.ActiveResearch)];
            _guaranteedResearch = _possibleResearch = _outcomes[0].Completed;
            if (precedingResearch?.DiscoveryAlternatives is { Count: > 0 } alternatives)
            {
                if (alternatives.Count > 256) { _reason = ForecastLimits.MissionResearchOutcomes; return; }
                if (firstBaseItems is not null && firstBaseItemUncertainty is not null && campaign._bases.Count != 0)
                {
                    var baseId = campaign._bases[0].Id;
                    var dailyDeltas = precedingResearch.ArrivalUncertainty.GetValueOrDefault(baseId);
                    _work += firstBaseItemUncertainty.Count * 2L;
                    if (_work > MaximumWork) { _reason = WorkReason; return; }
                    Dictionary<RuleHandle<ItemRuleFamily>, int> remaining = [];
                    foreach (var item in firstBaseItemUncertainty)
                    {
                        var daily = dailyDeltas?.GetValueOrDefault(_rules.Items.GetExternalId(item.Key)) ?? 0;
                        if (daily > item.Value) { _reason = ForecastLimits.MissionSiteResearchItemStocksRange; return; }
                        if (daily != item.Value) remaining.Add(item.Key, item.Value - daily);
                    }
                    _otherFirstBaseItemUncertainty = remaining.ToFrozenDictionary();
                }
                List<Outcome> initial = [];
                foreach (var branch in alternatives)
                {
                    _work += 1L + branch.CompletedResearch!.Count + branch.ResearchStatuses!.Count;
                    if (_work > MaximumWork) { _reason = WorkReason; return; }
                    initial.Add(new(new(branch.CompletedResearch, StringComparer.Ordinal),
                        new(branch.ResearchStatuses, StringComparer.Ordinal), campaign._researchScores[^1] + branch.ScoreChange!.Value,
                        branch.DiaryEntries!.Value, new(StringComparer.Ordinal), activeResearch: branch.ActiveResearch, items: BranchItems(branch)));
                    if (_reason is not null) return;
                }
                if (Publish(initial) is not null) return;
            }
            if (campaign._bases.Count == 0) return;
            if (uncertainFirstBaseItems is not null) PrepareItemAlternatives(uncertainFirstBaseItems);
            _reason ??= campaign.ProductionPreflight.ScoreBounds(highest, ref _work, out _precedingProductionMinimum, out _precedingProductionMaximum);
            if (_reason is not null) return;
            var owner = campaign._bases[0];
            _functionWork = owner.Facilities.Count + campaign._countries.Count + (long)campaign._regions.Count;
            foreach (var facility in owner.Facilities) _functionWork += _rules.Facilities[facility.Rule].Value.ProvidedBaseFunctions.Count;
            foreach (var country in campaign._countries)
            {
                var rule = _rules.Countries[country.Rule].Value;
                _functionWork += rule.Areas.Count + (long)rule.ProvidedBaseFunctions.Count;
            }
            foreach (var region in campaign._regions)
            {
                var rule = _rules.Regions[region.Rule].Value;
                _functionWork += rule.Areas.Count + (long)rule.ProvidedBaseFunctions.Count;
            }
            _work += owner.Facilities.Count + campaign._countries.Count + (long)campaign._regions.Count;
        }

        private FrozenDictionary<RuleHandle<ItemRuleFamily>, int>? BranchItems(DailyResearchStockForecast branch)
        {
            if (_firstBaseItems is null || _campaign._bases.Count == 0 || _precedingResearch!.ArrivalUncertainty.Count == 0) return null;
            var baseId = _campaign._bases[0].Id;
            var upper = _precedingResearch.Arrivals.GetValueOrDefault(baseId);
            if (upper is null) return null;
            _work += _firstBaseItems.Count * 2L + upper.Count;
            if (_work > MaximumWork) { _reason = WorkReason; return null; }
            Dictionary<RuleHandle<ItemRuleFamily>, int> items = new(_firstBaseItems);
            var exact = branch.Arrivals[baseId];
            for (var index = 0; index < upper.Count; index++)
            {
                var item = _rules.Items.GetRequired(upper[index].Key);
                var quantity = (long)items.GetValueOrDefault(item) - upper[index].Value + exact[index].Value;
                if (quantity is < 0 or > int.MaxValue)
                { _reason = ForecastLimits.MissionSiteResearchItemStocksRange; return null; }
                items[item] = (int)quantity;
            }
            return items.ToFrozenDictionary();
        }

        public long Work => _work;
        public IReadOnlySet<string> GuaranteedResearch => _guaranteedResearch;
        public IReadOnlySet<string> PossibleResearch => _possibleResearch;
        public IReadOnlySet<string> GuaranteedExplicitTopics => _guaranteedExplicitTopics;
        public IReadOnlySet<string> PossibleExplicitTopics => _possibleExplicitTopics;

        private void PrepareItemAlternatives(IReadOnlyList<RuleHandle<ItemRuleFamily>> items)
        {
            HashSet<RuleHandle<ItemRuleFamily>> seen = [];
            foreach (var item in items)
            {
                if (++_work > MaximumWork) { _reason = WorkReason; return; }
                if (!seen.Add(item)) continue;
                List<Outcome> next = [];
                foreach (var prior in _outcomes)
                {
                    _work += 1L + (prior.AbsentItems?.Count ?? 0);
                    if (_work > MaximumWork) { _reason = WorkReason; return; }
                    if (next.Count == 256) { _reason = ForecastLimits.MissionResearchOutcomes; return; }
                    var stocks = prior.Items ?? _firstBaseItems;
                    if (_otherFirstBaseItemUncertainty is not null && stocks is not null &&
                        (stocks.GetValueOrDefault(item) == 0 || stocks.GetValueOrDefault(item) > _otherFirstBaseItemUncertainty.GetValueOrDefault(item)))
                    {
                        next.Add(prior);
                        continue;
                    }
                    if (next.Count > 254) { _reason = ForecastLimits.MissionResearchOutcomes; return; }
                    // Initial research sets are shared only before grants, which
                    // always clone them. Item masks stay immutable across grants.
                    HashSet<RuleHandle<ItemRuleFamily>> absent = prior.AbsentItems is { } earlier ? new(earlier) : [];
                    absent.Add(item);
                    next.Add(prior);
                    next.Add(new(prior.Completed, prior.Statuses, prior.Score, prior.DiaryEntries, prior.ExplicitTopics, absent, prior.ActiveResearch, prior.Items));
                }
                _outcomes = next;
            }
        }

        public string? Advance(string id)
        {
            if (_reason is not null) return _reason;
            if (_campaign._bases.Count == 0 || !_rules.Research.TryGet(id, out var handle)) return null;
            // Daily generation precedes every site grant. Reserve validation and
            // explicit interruption of the enlarged mission list on each call.
            _work += 2L * _precedingMissionBirths;
            if (_campaign._handlers.Operation<ICampaignMissionResearchInterruption>().ValidationReason(ref _work, _precedingMissionRemovals) is { } invalid)
                return _reason = invalid;
            List<Outcome> next = [];
            foreach (var prior in _outcomes)
            {
                // Real per-site GrantReason also starts a fresh snapshot proof.
                // Reserve that repeated copy, even though this ordered proof reuses its context.
                _work += prior.Completed.Count + (long)prior.Statuses.Count;
                var primary = Clone(prior);
                if (_reason is not null) break;
                MissionResearchSequence.Primary(_rules, id, (topic, _) => Grant(primary, topic));
                if (_reason is not null) break;
                var rule = _rules.Research[handle].Value;
                _work += rule.GetOneFree.Count;
                foreach (var group in rule.GetOneFreeProtected) _work += 1L + group.Topics.Count;
                if (_work > MaximumWork) { _reason = WorkReason; break; }
                var candidates = Progress(primary).RewardCandidates(rule);
                _work += candidates.Count;
                if (_work > MaximumWork) { _reason = WorkReason; break; }
                var choices = rule.SequentialGetOneFree ? candidates.Take(1) : candidates.Distinct(StringComparer.Ordinal);
                var found = false;
                foreach (var bonus in choices)
                {
                    found = true;
                    if (next.Count == 256) { _reason = ForecastLimits.MissionResearchOutcomes; break; }
                    var outcome = Clone(primary);
                    if (_reason is not null) break;
                    if (_rules.Research.TryGet(bonus, out _))
                        MissionResearchSequence.Bonus(_rules, bonus, (topic, _) => Grant(outcome, topic));
                    if (_reason is not null) break;
                    next.Add(outcome);
                }
                if (_reason is not null) break;
                if (!found)
                {
                    if (next.Count == 256) { _reason = ForecastLimits.MissionResearchOutcomes; break; }
                    next.Add(primary);
                }
            }
            return Publish(next);
        }

        public string? AdvanceOptional(string id, out IReadOnlySet<string> whenGranted)
        {
            var skipped = _outcomes;
            var reason = Advance(id);
            whenGranted = _guaranteedResearch;
            if (reason is not null || _campaign._bases.Count == 0 || !_rules.Research.TryGet(id, out _)) return reason;
            return IncludeSkipped(skipped);
        }

        public string? AdvanceOptionalSequence(Func<string?> advance)
        {
            var skipped = _outcomes;
            if (advance() is { } reason) return reason;
            return ReferenceEquals(skipped, _outcomes) ? _reason : IncludeSkipped(skipped);
        }

        public string? AdvanceChoices(IEnumerable<string> ids, bool optional,
            out IReadOnlyDictionary<string, IReadOnlySet<string>> whenGranted)
        {
            Dictionary<string, IReadOnlySet<string>> conditional = new(StringComparer.Ordinal);
            whenGranted = conditional;
            List<string> choices = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (var id in ids)
            {
                if (++_work > MaximumWork) return _reason = WorkReason;
                if (!seen.Add(id)) continue;
                if (choices.Count == 256) return _reason = ForecastLimits.MissionResearchOutcomes;
                choices.Add(id);
            }
            if (choices.Count == 0) return _reason;
            if (choices.Count == 1)
            {
                IReadOnlySet<string> research;
                string? reason;
                if (optional) reason = AdvanceOptional(choices[0], out research);
                else { reason = Advance(choices[0]); research = _guaranteedResearch; }
                if (reason is not null) return reason;
                conditional.Add(choices[0], research);
                return null;
            }
            var prior = _outcomes;
            var guaranteed = _guaranteedResearch;
            var possible = _possibleResearch;
            var guaranteedExplicit = _guaranteedExplicitTopics;
            var possibleExplicit = _possibleExplicitTopics;
            List<Outcome> combined = [];
            foreach (var id in choices)
            {
                _outcomes = prior;
                _guaranteedResearch = guaranteed;
                _possibleResearch = possible;
                _guaranteedExplicitTopics = guaranteedExplicit;
                _possibleExplicitTopics = possibleExplicit;
                if (Advance(id) is { } reason) return reason;
                conditional.Add(id, _guaranteedResearch);
                if (combined.Count + _outcomes.Count > 256) return _reason = ForecastLimits.MissionResearchOutcomes;
                combined.AddRange(_outcomes);
            }
            if (optional)
            {
                if (combined.Count + prior.Count > 256) return _reason = ForecastLimits.MissionResearchOutcomes;
                combined.AddRange(prior);
            }
            return Publish(combined);
        }

        public string? AdvanceSequenceChoices(IEnumerable<Func<string?>> advances)
        {
            var prior = _outcomes;
            var guaranteed = _guaranteedResearch;
            var possible = _possibleResearch;
            var guaranteedExplicit = _guaranteedExplicitTopics;
            var possibleExplicit = _possibleExplicitTopics;
            List<Outcome> combined = [];
            foreach (var advance in advances)
            {
                if (++_work > MaximumWork) return _reason = WorkReason;
                _outcomes = prior;
                _guaranteedResearch = guaranteed;
                _possibleResearch = possible;
                _guaranteedExplicitTopics = guaranteedExplicit;
                _possibleExplicitTopics = possibleExplicit;
                if (advance() is { } reason) return reason;
                if (combined.Count + _outcomes.Count > 256) return _reason = ForecastLimits.MissionResearchOutcomes;
                combined.AddRange(_outcomes);
            }
            return combined.Count == 0 ? _reason : Publish(combined);
        }

        private string? IncludeSkipped(List<Outcome> skipped)
        {
            if (_outcomes.Count + skipped.Count > 256)
                return _reason = ForecastLimits.MissionResearchOutcomes;
            List<Outcome> combined = [.. _outcomes, .. skipped];
            return Publish(combined);
        }

        private string? Publish(List<Outcome> next)
        {
            if (_reason is null)
            {
                // Cache summaries once, charging copying, union and intersection work.
                // Later phase consumers cannot repeatedly allocate or scan the alternatives.
                foreach (var outcome in next) _work += 4L * (outcome.Completed.Count + (long)outcome.ExplicitTopics.Count);
                if (_work > MaximumWork) _reason = WorkReason;
                else
                {
                    _outcomes = next;
                    _guaranteedResearch = Intersection(static outcome => outcome.Completed);
                    _possibleResearch = Union(static outcome => outcome.Completed);
                    _guaranteedExplicitTopics = Intersection(static outcome => outcome.ExplicitTopics);
                    _possibleExplicitTopics = Union(static outcome => outcome.ExplicitTopics);
                }
            }
            return _reason;
        }

        private Outcome Clone(Outcome outcome)
        {
            _work += outcome.Completed.Count + (long)outcome.Statuses.Count + outcome.ExplicitTopics.Count + 1;
            if (_work > MaximumWork) _reason = WorkReason;
            return _reason is not null ? outcome : new(new(outcome.Completed, StringComparer.Ordinal),
                new(outcome.Statuses, StringComparer.Ordinal), outcome.Score, outcome.DiaryEntries,
                new(outcome.ExplicitTopics, StringComparer.Ordinal), outcome.AbsentItems, outcome.ActiveResearch, outcome.Items);
        }

        private ResearchDiscoveryProgress Progress(Outcome outcome) => new(_rules, outcome.Completed, outcome.Statuses);

        private void Grant(Outcome outcome, string topic)
        {
            if (_reason is not null) return;
            outcome.ExplicitTopics.Add(topic);
            if (!outcome.Completed.Contains(topic) && outcome.Statuses.GetValueOrDefault(topic) != 2) outcome.DiaryEntries++;
            var owner = _campaign._bases[0];
            var supported = Progress(outcome).Advance(topic, (candidateId, candidate) =>
            {
                var eligibilityWork = owner.Research.Count + (long)candidate.RequiredBaseFunctions.Count +
                    (candidate.RequiredBaseFunctions.Count == 0 ? 0 : _functionWork) + 1;
                if (eligibilityWork > MaximumWork - _work) return (false, eligibilityWork);
                var candidateHandle = _rules.Research.GetRequired(candidateId);
                var active = outcome.ActiveResearch?.GetValueOrDefault(owner.Id)?.Contains(candidateId) ??
                    owner.Research.Any(project => project.Rule == candidateHandle);
                var addedItems = _precedingResearch?.StockChanges.GetValueOrDefault(owner.Id)?.GetValueOrDefault(candidate.NeededItem ?? "") ?? 0;
                return (!active &&
                    (!candidate.NeedItem || candidate.NeededItem is { } needed && _rules.Items.TryGet(needed, out var item) &&
                        outcome.AbsentItems?.Contains(item) != true &&
                        ((outcome.Items ?? _firstBaseItems) is { } items ? items.GetValueOrDefault(item) :
                            (long)owner.Items.GetValueOrDefault(item) + addedItems) != 0) && _campaign.HasFunctions(owner, candidate.RequiredBaseFunctions, _completedBuildTime), eligibilityWork);
            }, (discovered, points, _) =>
            {
                outcome.Score += points;
                if (discovered != topic) outcome.DiaryEntries++;
                if ((Int128)outcome.Score + _precedingProductionMinimum < int.MinValue ||
                    (Int128)outcome.Score + _precedingProductionMaximum > int.MaxValue)
                    _reason = ForecastLimits.MissionResearchScoreRange;
            }, ref _work, MaximumWork);
            if (!supported) _reason = WorkReason;
            if (!_campaign.ResearchDiaryCapacityFits(outcome.DiaryEntries))
                _reason ??= "Mission research diary exceeds its supported population or identity range.";
        }

        private HashSet<string> Intersection(Func<Outcome, HashSet<string>> select)
        {
            var result = new HashSet<string>(select(_outcomes[0]), StringComparer.Ordinal);
            foreach (var outcome in _outcomes.Skip(1)) result.IntersectWith(select(outcome));
            return result;
        }

        private HashSet<string> Union(Func<Outcome, HashSet<string>> select)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var outcome in _outcomes) result.UnionWith(select(outcome));
            return result;
        }

        private sealed class Outcome(HashSet<string> completed, Dictionary<string, int> statuses, long score,
            long diaryEntries, HashSet<string> explicitTopics, IReadOnlySet<RuleHandle<ItemRuleFamily>>? absentItems = null,
            IReadOnlyDictionary<int, IReadOnlySet<string>>? activeResearch = null,
            IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? items = null)
        {
            public HashSet<string> Completed { get; } = completed;
            public Dictionary<string, int> Statuses { get; } = statuses;
            public HashSet<string> ExplicitTopics { get; } = explicitTopics;
            public IReadOnlySet<RuleHandle<ItemRuleFamily>>? AbsentItems { get; } = absentItems;
            public IReadOnlyDictionary<int, IReadOnlySet<string>>? ActiveResearch { get; } = activeResearch;
            public IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? Items { get; } = items;
            public long Score { get; set; } = score;
            public long DiaryEntries { get; set; } = diaryEntries;
        }
    }
}
