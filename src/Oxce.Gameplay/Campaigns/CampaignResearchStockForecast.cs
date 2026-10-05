using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

internal sealed record DailyResearchStockForecast(
    IReadOnlyDictionary<int, IReadOnlyDictionary<string, int>> StockChanges,
    IReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, int>>> Arrivals)
{
    public string? UnavailableReason { get; init; }
}

public sealed partial class CampaignState
{
    private static readonly DailyResearchStockForecast EmptyResearchStockForecast = new(
        new ReadOnlyDictionary<int, IReadOnlyDictionary<string, int>>(new Dictionary<int, IReadOnlyDictionary<string, int>>()),
        new ReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, int>>>(new Dictionary<int, IReadOnlyList<KeyValuePair<string, int>>>()));

    bool ICampaignResearchPreflight.TryForecastDailyStocks(ref long work, out DailyResearchStockForecast forecast)
    {
        forecast = EmptyResearchStockForecast;
        if (DailyItemsUnchanged(ref work)) return true;
        var rules = _content.RuntimeRules;
        // Automatic discoveries can disable a held project. Reward, lookup and
        // protected unlock choices require a richer research-state projection.
        work += rules.Research.Count;
        if (work > 1_000_000 || rules.Research.Rules.Any(entry => entry.Value.Cost == 0 && entry.Value.Disables.Count != 0)) return false;
        Dictionary<int, List<ResearchProjectState>> projects = [];
        Dictionary<int, Dictionary<string, int>> changes = [];
        Dictionary<int, List<KeyValuePair<string, int>>> arrivals = [];
        foreach (var owner in _bases)
        {
            work += owner.Research.Count;
            if (work > 1_000_000) return false;
            projects.Add(owner.Id, new(owner.Research));
            changes.Add(owner.Id, new(StringComparer.Ordinal));
            arrivals.Add(owner.Id, []);
            foreach (var project in owner.Research)
            {
                if ((long)project.Spent + project.Assigned < project.Cost) continue;
                var entry = rules.Research[project.Rule];
                var rule = entry.Value;
                if (rule.GetOneFree.Count != 0 || rule.GetOneFreeProtected.Count != 0 || LookupChangesStatuses(rule) ||
                    rule.Unlocks.Count != 0 || rule.Disables.Count != 0 || rule.Reenables.Count != 0 ||
                    HoldsResearchItem(rule) && rule.DestroyItem && !rule.ReturnsItem && rules.Units.TryGet(entry.Id, out _)) return false;
            }
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
            HashSet<RuleHandle<ResearchRuleFamily>> sideEffects = [];
            foreach (var project in finished)
            {
                var rule = rules.Research[project.Rule].Value;
                if (ResearchItemLifecycle.ReturnsHeldItem(HoldsResearchItem(rule), rule.ReturnsItem, true)) Refund(owner.Id, rule);
                sideEffects.Add(project.Rule);
            }
            local.RemoveAll(project => project.Spent >= project.Cost);
            work += rules.Research.Count;
            if (work > 1_000_000) return false;
            foreach (var entry in rules.Research.Rules)
            {
                if (!sideEffects.Contains(rules.Research.GetRequired(entry.Id))) continue;
                foreach (var other in _bases)
                {
                    work += projects[other.Id].Count * 2L;
                    if (work > 1_000_000) return false;
                    var project = projects[other.Id].FirstOrDefault(project => project.Rule == rules.Research.GetRequired(entry.Id));
                    if (project is null) continue;
                    if (ResearchItemLifecycle.ReturnsHeldItem(HoldsResearchItem(entry.Value), entry.Value.ReturnsItem,
                        project.Spent >= project.Cost)) Refund(other.Id, entry.Value);
                    projects[other.Id].Remove(project);
                }
                work += entry.Value.SpawnedItemList.Count + 1L;
                if (work > 1_000_000) return false;
                if (entry.Value.SpawnedItem.Length != 0 && rules.Items.TryGet(entry.Value.SpawnedItem, out _))
                    arrivals[owner.Id].Add(new(entry.Value.SpawnedItem, Math.Max(1, entry.Value.SpawnedItemCount)));
                foreach (var id in entry.Value.SpawnedItemList)
                    if (rules.Items.TryGet(id, out _)) arrivals[owner.Id].Add(new(id, 1));
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
                if (_researchRuleStatus.GetValueOrDefault(entry.Id) == 2 &&
                    ResearchItemLifecycle.ReturnsHeldItem(HoldsResearchItem(entry.Value), entry.Value.ReturnsItem,
                        project.Spent >= project.Cost)) Refund(owner.Id, entry.Value);
            }
        }
        var arrivalCount = arrivals.Values.Sum(items => (long)items.Count);
        if (arrivalCount != 0)
        {
            work += _bases.Sum(owner => (long)owner.Transfers.Count);
            if (work > 1_000_000) return false;
            if ((long)NextTransferId() + arrivalCount > int.MaxValue)
            {
                forecast = EmptyResearchStockForecast with { UnavailableReason = "Research arrivals exhaust the transfer identity range." };
                return false;
            }
        }
        forecast = new(new ReadOnlyDictionary<int, IReadOnlyDictionary<string, int>>(changes.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyDictionary<string, int>)new ReadOnlyDictionary<string, int>(pair.Value))),
            new ReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, int>>>(arrivals.ToDictionary(
                pair => pair.Key, pair => (IReadOnlyList<KeyValuePair<string, int>>)pair.Value.AsReadOnly())));
        return true;

        bool LookupChangesStatuses(RuntimeResearchRule rule)
        {
            if (rule.Lookup.Length == 0 || !rules.Research.TryGet(rule.Lookup, out var handle)) return false;
            var lookup = rules.Research[handle].Value;
            // Daily lookups call addFinishedResearch, never primary side effects.
            // Automatic status changes are checked at catalogue scope above.
            return lookup.Disables.Count != 0 || lookup.Reenables.Count != 0;
        }

        void Refund(int baseId, RuntimeResearchRule rule)
        {
            if (rule.NeededItem is { } id && rules.Items.TryGet(id, out _))
                changes[baseId][id] = changes[baseId].GetValueOrDefault(id) + 1;
        }
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
