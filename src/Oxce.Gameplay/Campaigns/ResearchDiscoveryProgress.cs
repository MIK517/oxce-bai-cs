using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>Shared SavedGame::addFinishedResearch transition over owned or projected state.</summary>
internal sealed class ResearchDiscoveryProgress(
    RuntimeRuleCatalog rules, HashSet<string> completed, Dictionary<string, int> statuses)
{
    public bool IsNew(string id) => !completed.Contains(id) && statuses.GetValueOrDefault(id) != 2;

    public List<string> RewardCandidates(RuntimeResearchRule rule)
    {
        var candidates = rule.GetOneFree.Where(IsNew).ToList();
        foreach (var group in rule.GetOneFreeProtected)
            if (completed.Contains(group.Prerequisite)) candidates.AddRange(group.Topics.Where(IsNew));
        return candidates;
    }

    public bool HasRemainingReward(RuntimeResearchRule rule) => rule.GetOneFree.Any(IsNew) ||
        rule.GetOneFreeProtected.Any(group => completed.Contains(group.Prerequisite) && group.Topics.Any(IsNew));

    public bool HasProtectedUnlock(RuntimeResearchRule rule) => rule.Unlocks.Any(id =>
        rules.Research.TryGet(id, out var handle) && rules.Research[handle].Value.Requirements.Count != 0 && IsNew(id));

    public bool Advance(string initial, Func<string, RuntimeResearchRule, (bool Eligible, long Work)> baseEligible,
        Action<string, int, bool>? discovered, ref long work, long maximumWork)
    {
        if (!rules.Research.TryGet(initial, out var initialHandle) || statuses.GetValueOrDefault(initial) == 2) return true;
        var markDiscovered = !rules.Research[initialHandle].Value.Repeatable;
        List<string> queue = [initial];
        HashSet<string> queued = new(StringComparer.Ordinal) { initial };
        for (var offset = 0; offset < queue.Count; offset++)
        {
            if (++work > maximumWork) return false;
            var id = queue[offset];
            if (!rules.Research.TryGet(id, out var handle)) continue;
            var rule = rules.Research[handle].Value;
            work += rule.Unlocks.Count + (long)rule.Disables.Count + rule.Reenables.Count;
            if (work > maximumWork) return false;
            var wasComplete = completed.Contains(id);
            var hadProtectedUnlock = wasComplete && HasProtectedUnlock(rule);
            if (!wasComplete)
            {
                if (markDiscovered) completed.Add(id);
                discovered?.Invoke(id, rule.Points, markDiscovered);
                foreach (var disabled in rule.Disables)
                {
                    completed.Remove(disabled);
                    statuses[disabled] = 2;
                }
            }
            foreach (var enabled in rule.Reenables)
                if (statuses.GetValueOrDefault(enabled) == 2) statuses[enabled] = 0;
            if (wasComplete && !hadProtectedUnlock) continue;
            HashSet<string>? unlocked = null;
            foreach (var candidate in rules.Research.Rules)
            {
                work += 1L + candidate.Value.Dependencies.Count + candidate.Value.Requirements.Count +
                    candidate.Value.GetOneFree.Count + candidate.Value.Unlocks.Count;
                foreach (var group in candidate.Value.GetOneFreeProtected) work += 1L + group.Topics.Count;
                if (work > maximumWork) return false;
                if (candidate.Value.Cost != 0 || queued.Contains(candidate.Id) ||
                    candidate.Value.Requirements.Count != 0 && !rule.Unlocks.Contains(candidate.Id, StringComparer.Ordinal)) continue;
                if (statuses.GetValueOrDefault(candidate.Id) == 2 ||
                    candidate.Value.Requirements.Any(required => !completed.Contains(required)) ||
                    completed.Contains(candidate.Id) && !HasRemainingReward(candidate.Value) && !HasProtectedUnlock(candidate.Value)) continue;
                if (candidate.Value.Dependencies.Any(required => !completed.Contains(required)))
                {
                    if (unlocked is null)
                    {
                        unlocked = new(StringComparer.Ordinal);
                        foreach (var prior in completed)
                        {
                            if (++work > maximumWork) return false;
                            if (!rules.Research.TryGet(prior, out var priorHandle)) continue;
                            var unlocks = rules.Research[priorHandle].Value.Unlocks;
                            work += unlocks.Count;
                            if (work > maximumWork) return false;
                            unlocked.UnionWith(unlocks);
                        }
                    }
                    if (!unlocked.Contains(candidate.Id)) continue;
                }
                var eligibility = baseEligible(candidate.Id, candidate.Value);
                work += eligibility.Work;
                if (work > maximumWork) return false;
                if (eligibility.Eligible && queued.Add(candidate.Id)) queue.Add(candidate.Id);
            }
        }
        return true;
    }
}
