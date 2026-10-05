namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    bool ICampaignResearchPreflight.DailyItemsUnchanged(ref long work)
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
