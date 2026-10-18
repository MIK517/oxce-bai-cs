using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>SavedGame::handleResearchUnlockedByMissions; never applies daily primary side effects.</summary>
internal interface ICampaignMissionResearch
{
    string? GrantReason(string id);
    bool Grant(string id, string deploymentId);
    CampaignState.MissionResearchForecast CreateForecast(CampaignTimeTrigger highest = CampaignTimeTrigger.FiveSeconds,
        DailyResearchStockForecast? precedingResearch = null,
        IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? firstBaseItems = null, long precedingWork = 0,
        int precedingMissionBirths = 0, IReadOnlySet<int>? precedingMissionRemovals = null,
        IReadOnlyList<RuleHandle<ItemRuleFamily>>? uncertainFirstBaseItems = null,
        IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? firstBaseItemUncertainty = null);
}

/// <summary>The world owns immediate interruption for the explicit mission-source grants.</summary>
internal interface ICampaignMissionResearchInterruption
{
    string? ValidationReason(ref long work, IReadOnlySet<int>? precedingMissionRemovals = null);
    void Interrupt(IReadOnlyList<string> explicitTopics);
}

/// <summary>Native caller order, shared by owned execution and read-only capacity proofs.</summary>
internal static class MissionResearchSequence
{
    public static IReadOnlyList<string> Apply(RuntimeRuleCatalog rules, string initial,
        Action<string, ResearchDiscoverySource> diary, Action<string> grant,
        Func<RuntimeResearchRule, string?> selectReward)
    {
        List<string> topics = [];
        var rule = rules.Research.GetRequiredValue(initial);
        Primary(rules, initial, ApplyTopic);
        if (selectReward(rule) is { } bonus && rules.Research.TryGet(bonus, out var bonusHandle))
            Bonus(rules, rules.Research.GetExternalId(bonusHandle), ApplyTopic);
        return topics.AsReadOnly();

        void ApplyTopic(string id, ResearchDiscoverySource source)
        {
            topics.Add(id);
            diary(id, source);
            grant(id);
        }
    }

    public static void Primary(RuntimeRuleCatalog rules, string id, Action<string, ResearchDiscoverySource> apply)
    {
        apply(id, ResearchDiscoverySource.Mission);
        var lookup = rules.Research.GetRequiredValue(id).Lookup;
        if (lookup.Length != 0) apply(lookup, ResearchDiscoverySource.Mission);
    }

    public static void Bonus(RuntimeRuleCatalog rules, string id, Action<string, ResearchDiscoverySource> apply)
    {
        apply(id, ResearchDiscoverySource.FreeFrom);
        var lookup = rules.Research.GetRequiredValue(id).Lookup;
        if (lookup.Length != 0) apply(lookup, ResearchDiscoverySource.FreeFrom);
    }
}

public sealed partial class CampaignState : ICampaignMissionResearch
{
    internal ICampaignMissionResearch MissionResearch => _handlers.Operation<ICampaignMissionResearch>();

    string? ICampaignMissionResearch.GrantReason(string id)
    {
        // Native null research/all bases lost is a no-op, even with malformed unused mission links.
        if (_bases.Count == 0 || !_content.RuntimeRules.Research.TryGet(id, out _)) return null;
        return MissionResearch.CreateForecast().Advance(id);
    }

    MissionResearchForecast ICampaignMissionResearch.CreateForecast(CampaignTimeTrigger highest, DailyResearchStockForecast? precedingResearch,
        IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? firstBaseItems, long precedingWork,
        int precedingMissionBirths, IReadOnlySet<int>? precedingMissionRemovals,
        IReadOnlyList<RuleHandle<ItemRuleFamily>>? uncertainFirstBaseItems,
        IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? firstBaseItemUncertainty) =>
        new(this, highest, precedingResearch, firstBaseItems, precedingWork, precedingMissionBirths, precedingMissionRemovals, uncertainFirstBaseItems, firstBaseItemUncertainty);

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
