using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    /// <summary>Ordered, bounded alternative outcomes of world research grants; never consumes RNG.</summary>
    internal sealed class MissionResearchForecast
    {
        private const long MaximumWork = 1_000_000;
        private const string WorkReason = "Mission research exceeds the supported discovery work limit.";
        private readonly CampaignState _campaign;
        private readonly RuntimeRuleCatalog _rules;
        private readonly int _completedBuildTime;
        private readonly DailyResearchStockForecast? _precedingResearch;
        private readonly IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? _firstBaseItems;
        private List<Outcome> _outcomes;
        private long _work;
        private readonly long _functionWork;
        private string? _reason;
        private IReadOnlySet<string> _guaranteedResearch;
        private IReadOnlySet<string> _possibleResearch;
        private IReadOnlySet<string> _guaranteedExplicitTopics = new HashSet<string>(StringComparer.Ordinal);
        private IReadOnlySet<string> _possibleExplicitTopics = new HashSet<string>(StringComparer.Ordinal);

        internal MissionResearchForecast(CampaignState campaign, CampaignTimeTrigger highest = CampaignTimeTrigger.FiveSeconds,
            DailyResearchStockForecast? precedingResearch = null,
            IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? firstBaseItems = null, long precedingWork = 0)
        {
            _campaign = campaign;
            _rules = campaign.Content.RuntimeRules;
            _precedingResearch = precedingResearch;
            _firstBaseItems = firstBaseItems;
            // Construction decrements positive timers before the daily research
            // and world callers. The real GrantReason runs after that transition.
            _completedBuildTime = highest >= CampaignTimeTrigger.OneDay ? 1 : 0;
            var completed = precedingResearch?.CompletedResearch ?? campaign._completedResearch;
            var statuses = precedingResearch?.ResearchStatuses ?? campaign._researchRuleStatus;
            _work = Math.Max(precedingResearch?.Work ?? 0, precedingWork) + completed.Count + (long)statuses.Count + (firstBaseItems?.Count ?? 0);
            _outcomes = [new(new(completed, StringComparer.Ordinal),
                new(statuses, StringComparer.Ordinal), campaign._researchScores[^1] + (precedingResearch?.ScoreChange ?? 0),
                precedingResearch?.DiaryEntries ?? 0, new(StringComparer.Ordinal))];
            _guaranteedResearch = _possibleResearch = _outcomes[0].Completed;
            if (campaign._bases.Count == 0) return;
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

        public long Work => _work;
        public IReadOnlySet<string> GuaranteedResearch => _guaranteedResearch;
        public IReadOnlySet<string> PossibleResearch => _possibleResearch;
        public IReadOnlySet<string> GuaranteedExplicitTopics => _guaranteedExplicitTopics;
        public IReadOnlySet<string> PossibleExplicitTopics => _possibleExplicitTopics;

        public string? Advance(string id)
        {
            if (_reason is not null) return _reason;
            if (_campaign._bases.Count == 0 || !_rules.Research.TryGet(id, out var handle)) return null;
            if (_campaign._handlers.Operation<ICampaignMissionResearchInterruption>().ValidationReason(ref _work) is { } invalid)
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
                    if (next.Count == 256) { _reason = "Mission research exceeds the supported outcome limit."; break; }
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
                    if (next.Count == 256) { _reason = "Mission research exceeds the supported outcome limit."; break; }
                    next.Add(primary);
                }
            }
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
                new(outcome.ExplicitTopics, StringComparer.Ordinal));
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
                var active = _precedingResearch?.ActiveResearch?.GetValueOrDefault(owner.Id)?.Contains(candidateId) ??
                    owner.Research.Any(project => project.Rule == candidateHandle);
                var addedItems = _precedingResearch?.StockChanges.GetValueOrDefault(owner.Id)?.GetValueOrDefault(candidate.NeededItem ?? "") ?? 0;
                return (!active &&
                    (!candidate.NeedItem || candidate.NeededItem is { } needed && _rules.Items.TryGet(needed, out var item) &&
                        (_firstBaseItems is not null ? _firstBaseItems.GetValueOrDefault(item) :
                            (long)owner.Items.GetValueOrDefault(item) + addedItems) != 0) && _campaign.HasFunctions(owner, candidate.RequiredBaseFunctions, _completedBuildTime), eligibilityWork);
            }, (discovered, points, _) =>
            {
                outcome.Score += points;
                if (discovered != topic) outcome.DiaryEntries++;
                if (outcome.Score is < int.MinValue or > int.MaxValue) _reason = "Mission research score exceeds the supported range.";
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
            long diaryEntries, HashSet<string> explicitTopics)
        {
            public HashSet<string> Completed { get; } = completed;
            public Dictionary<string, int> Statuses { get; } = statuses;
            public HashSet<string> ExplicitTopics { get; } = explicitTopics;
            public long Score { get; set; } = score;
            public long DiaryEntries { get; set; } = diaryEntries;
        }
    }
}
