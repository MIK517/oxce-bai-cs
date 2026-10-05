using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private (RuleHandle<ItemRuleFamily>? Held, RuleHandle<ItemRuleFamily>? Corpse, int Corpses) ResearchRecoveredItems(
        RuntimeResearchRule rule, string id, bool finished, bool dailyCompletion)
    {
        var rules = _content.RuntimeRules;
        RuleHandle<ItemRuleFamily>? held = ResearchItemLifecycle.ReturnsHeldItem(HoldsResearchItem(rule), rule.ReturnsItem, finished) &&
            rule.NeededItem is { } needed && rules.Items.TryGet(needed, out var heldItem) ? heldItem : null;
        RuleHandle<ItemRuleFamily>? corpse = null;
        // Both native callers look up the unit by research name, not neededItem.
        if (rules.Units.TryGet(id, out var unit) && rules.Units[unit].Value.Armor is { } armor)
            corpse = rules.Armors[armor].Value.CorpseGeoscape;
        var recoverable = corpse is { } item && rules.Items[item].Value is { Recoverable: true, CorpseRecoverable: true };
        var quantity = ResearchItemLifecycle.CorpseQuantity(Options.RetainCorpses, rule.NeedItem, rule.DestroyItem,
            rule.ReturnsItem, finished, dailyCompletion, recoverable);
        return (held, corpse, quantity);
    }

    private void ApplyResearchRecoveredItems(BaseState owner, RuntimeResearchRule rule, string id, bool finished, bool dailyCompletion)
    {
        var recovered = ResearchRecoveredItems(rule, id, finished, dailyCompletion);
        if (recovered.Held is { } held) ChangeStock(owner.Items, held, 1);
        if (recovered.Corpse is { } corpse && recovered.Corpses != 0) ChangeStock(owner.Items, corpse, recovered.Corpses);
    }

    private string? ResearchRecoveryReason(BaseState owner, ResearchProjectState project)
    {
        var entry = _content.RuntimeRules.Research[project.Rule];
        var recovered = ResearchRecoveredItems(entry.Value, entry.Id, project.Spent >= project.Cost, dailyCompletion: false);
        Dictionary<RuleHandle<ItemRuleFamily>, long> added = [];
        AddRecovered(added, recovered);
        return added.Any(pair => pair.Value + owner.Items.GetValueOrDefault(pair.Key) > int.MaxValue)
            ? "Research refunds exceed the supported stock range." : null;
    }

    private string? PreflightResearchRecovery(ref long work)
    {
        var finishing = FinishingResearchTopics();
        var possible = finishing.Count == 0 ? new HashSet<string>(StringComparer.Ordinal) : DailyResearchPossibilities(finishing);
        HashSet<string> disabled = new(StringComparer.Ordinal);
        var rules = _content.RuntimeRules.Research;
        foreach (var id in possible)
        {
            if (++work > 1_000_000) return "Research recovery exceeds the supported work limit.";
            if (!rules.TryGet(id, out var handle)) continue;
            var disables = rules[handle].Value.Disables;
            work += disables.Count;
            if (work > 1_000_000) return "Research recovery exceeds the supported work limit.";
            disabled.UnionWith(disables);
        }
        foreach (var owner in _bases)
        {
            Dictionary<RuleHandle<ItemRuleFamily>, long> added = [];
            foreach (var project in owner.Research)
            {
                if (++work > 1_000_000) return "Research recovery exceeds the supported work limit.";
                var entry = rules[project.Rule];
                Dictionary<RuleHandle<ItemRuleFamily>, long> maximum = [];
                if ((long)project.Spent + project.Assigned >= project.Cost)
                    AddRecovered(maximum, ResearchRecoveredItems(entry.Value, entry.Id, finished: true, dailyCompletion: true));
                if (possible.Contains(entry.Id) || disabled.Contains(entry.Id) || _researchRuleStatus.GetValueOrDefault(entry.Id) == 2)
                {
                    Dictionary<RuleHandle<ItemRuleFamily>, long> cancelled = [];
                    AddRecovered(cancelled, ResearchRecoveredItems(entry.Value, entry.Id, project.Spent >= project.Cost, dailyCompletion: false));
                    foreach (var item in cancelled) maximum[item.Key] = Math.Max(maximum.GetValueOrDefault(item.Key), item.Value);
                }
                foreach (var item in maximum) added[item.Key] = added.GetValueOrDefault(item.Key) + item.Value;
            }
            if (added.Any(pair => pair.Value + owner.Items.GetValueOrDefault(pair.Key) > int.MaxValue))
                return "Research refunds exceed the supported stock range.";
        }
        return null;
    }

    private static void AddRecovered(Dictionary<RuleHandle<ItemRuleFamily>, long> added,
        (RuleHandle<ItemRuleFamily>? Held, RuleHandle<ItemRuleFamily>? Corpse, int Corpses) recovered)
    {
        if (recovered.Held is { } held) added[held] = added.GetValueOrDefault(held) + 1;
        if (recovered.Corpse is { } corpse && recovered.Corpses != 0)
            added[corpse] = added.GetValueOrDefault(corpse) + recovered.Corpses;
    }
}
