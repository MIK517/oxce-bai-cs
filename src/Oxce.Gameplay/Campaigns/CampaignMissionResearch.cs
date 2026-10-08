using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>SavedGame::handleResearchUnlockedByMissions; never applies daily primary side effects.</summary>
internal interface ICampaignMissionResearch
{
    string? GrantReason(string id);
    bool Grant(string id, string deploymentId);
}

/// <summary>The world owns immediate interruption for the explicit mission-source grants.</summary>
internal interface ICampaignMissionResearchInterruption
{
    string? ValidationReason();
    void Interrupt(IReadOnlyList<string> explicitTopics);
}

/// <summary>Native caller order, shared by owned execution and read-only capacity proofs.</summary>
internal static class MissionResearchSequence
{
    public static IReadOnlyList<string> Apply(RuntimeRuleCatalog rules, string initial,
        Action<string, ResearchDiscoverySource> diary, Action<string> grant,
        Func<RuntimeResearchRule, string?> selectReward)
    {
        var rule = rules.Research[rules.Research.GetRequired(initial)].Value;
        List<string> topics = [];
        ApplyTopic(initial, ResearchDiscoverySource.Mission);
        if (rule.Lookup.Length != 0) ApplyTopic(rule.Lookup, ResearchDiscoverySource.Mission);
        if (selectReward(rule) is { } bonus && rules.Research.TryGet(bonus, out var bonusHandle))
        {
            ApplyTopic(bonus, ResearchDiscoverySource.FreeFrom);
            var lookup = rules.Research[bonusHandle].Value.Lookup;
            if (lookup.Length != 0) ApplyTopic(lookup, ResearchDiscoverySource.FreeFrom);
        }
        return topics.AsReadOnly();

        void ApplyTopic(string id, ResearchDiscoverySource source)
        {
            topics.Add(id);
            diary(id, source);
            grant(id);
        }
    }
}

public sealed partial class CampaignState : ICampaignMissionResearch
{
    internal ICampaignMissionResearch MissionResearch => _handlers.Operation<ICampaignMissionResearch>();

    string? ICampaignMissionResearch.GrantReason(string id)
    {
        // Native null research/all bases lost is a no-op, even with malformed unused mission links.
        if (_bases.Count == 0 || !_content.RuntimeRules.Research.TryGet(id, out _)) return null;
        var interruption = _handlers.Operation<ICampaignMissionResearchInterruption>();
        if (interruption.ValidationReason() is { } invalid) return invalid;
        var rules = _content.RuntimeRules;
        var owner = _bases[0];
        var completed = new HashSet<string>(_completedResearch, StringComparer.Ordinal);
        var statuses = new Dictionary<string, int>(_researchRuleStatus, StringComparer.Ordinal);
        var progress = new ResearchDiscoveryProgress(rules, completed, statuses);
        long work = completed.Count + (long)statuses.Count;
        // HasFunctions scans geography and unions provided names for each eligible
        // automatic candidate. Charge those lists, not just their owner counts.
        long functionWork = owner.Facilities.Count + _countries.Count + (long)_regions.Count;
        foreach (var facility in owner.Facilities)
            functionWork += rules.Facilities[facility.Rule].Value.ProvidedBaseFunctions.Count;
        foreach (var country in _countries)
        {
            var rule = rules.Countries[country.Rule].Value;
            functionWork += rule.Areas.Count + (long)rule.ProvidedBaseFunctions.Count;
        }
        foreach (var region in _regions)
        {
            var rule = rules.Regions[region.Rule].Value;
            functionWork += rule.Areas.Count + (long)rule.ProvidedBaseFunctions.Count;
        }
        work += owner.Facilities.Count + _countries.Count + (long)_regions.Count;
        if (work > 1_000_000) return "Mission research exceeds the supported discovery work limit.";
        long score = _researchScores[^1];
        long additions = 0;
        string? reason = null;
        MissionResearchSequence.Apply(rules, id, (topic, _) =>
        {
            if (!completed.Contains(topic) && statuses.GetValueOrDefault(topic) != 2) additions++;
        }, topic =>
        {
            if (reason is not null) return;
            var supported = progress.Advance(topic, (candidateId, candidate) =>
            {
                var eligibilityWork = owner.Research.Count + (long)candidate.RequiredBaseFunctions.Count +
                    (candidate.RequiredBaseFunctions.Count == 0 ? 0 : functionWork) + 1;
                // Reject before HasFunctions can walk a large geography or allocate
                // its union. Advance then charges this rejected query and stops.
                if (eligibilityWork > 1_000_000 - work) return (false, eligibilityWork);
                var handle = rules.Research.GetRequired(candidateId);
                return (!owner.Research.Any(project => project.Rule == handle) &&
                    (!candidate.NeedItem || candidate.NeededItem is { } needed && rules.Items.TryGet(needed, out var item) &&
                        owner.Items.GetValueOrDefault(item) != 0) && HasFunctions(owner, candidate.RequiredBaseFunctions),
                    eligibilityWork);
            }, (discovered, points, _) =>
            {
                score += points;
                if (discovered != topic) additions++;
                if (score is < int.MinValue or > int.MaxValue) reason = "Mission research score exceeds the supported range.";
            }, ref work, 1_000_000);
            if (!supported) reason = "Mission research exceeds the supported discovery work limit.";
        }, rule =>
        {
            if (reason is not null) return null;
            work += rule.GetOneFree.Count;
            foreach (var group in rule.GetOneFreeProtected) work += 1L + group.Topics.Count;
            if (work > 1_000_000) { reason = "Mission research exceeds the supported discovery work limit."; return null; }
            var candidates = progress.RewardCandidates(rule);
            if (!rule.SequentialGetOneFree && candidates.Count > 1 && candidates.Any(candidate => candidate != candidates[0]))
            {
                reason = "Mission research depends on uncertain reward outcomes.";
                return null;
            }
            return candidates.Count == 0 ? null : candidates[0];
        });
        return reason ?? (ResearchDiaryCapacityFits(additions) ? null :
            "Mission research diary exceeds its supported population or identity range.");
    }

    bool ICampaignMissionResearch.Grant(string id, string deploymentId)
    {
        if (_bases.Count == 0 || !_content.RuntimeRules.Research.TryGet(id, out _)) return false;
        if (MissionResearch.GrantReason(id) is { } reason) throw new InvalidOperationException(reason);
        var discoveries = new List<string>();
        var topics = MissionResearchSequence.Apply(_content.RuntimeRules, id, (topic, source) =>
            RecordResearchDiary(topic, source, source == ResearchDiscoverySource.Mission ? deploymentId : id, considerDebug: false),
            topic => AddFinishedResearch(_bases[0], topic, discoveries), SelectResearchReward);
        _handlers.Operation<ICampaignMissionResearchInterruption>().Interrupt(topics);
        return true;
    }
}
