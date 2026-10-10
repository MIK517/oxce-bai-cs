using System.Collections.Frozen;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private const int MaximumResearchStockAlternatives = 4096;

    bool ICampaignResearchPreflight.TryForecastDailyStocks(ref long work, out DailyResearchStockForecast forecast, bool includeDiscoveries) =>
        TryForecastDailyStocks(ref work, out forecast, includeDiscoveries, retainWorldAlternatives: false);

    bool ICampaignResearchPreflight.TryForecastDailyWorldStocks(ref long work, out DailyResearchStockForecast forecast) =>
        TryForecastDailyStocks(ref work, out forecast, includeDiscoveries: true, retainWorldAlternatives: true);

    private bool TryForecastDailyStocks(ref long work, out DailyResearchStockForecast forecast, bool includeDiscoveries, bool retainWorldAlternatives)
    {
        if (TryForecastDailyStockBranch(ref work, out forecast, includeDiscoveries, [], out var pendingChoices)) return true;
        // Branching is safe only when every final consequence consumed by the caller agrees.
        if (pendingChoices.Count == 0) return false;
        Queue<int[]> pending = [];
        var created = 1;
        if (!EnqueueResearchStockBranches(pending, [], pendingChoices, ref created, ref work)) return false;
        DailyResearchStockForecast? common = null;
        List<DailyResearchStockForecast>? alternatives = retainWorldAlternatives ? [] : null;
        while (pending.TryDequeue(out var choices))
        {
            if (!TryForecastDailyStockBranch(ref work, out var branch, includeDiscoveries, choices, out pendingChoices))
            {
                if (pendingChoices.Count == 0)
                {
                    forecast = branch;
                    return false;
                }
                if (!EnqueueResearchStockBranches(pending, choices, pendingChoices, ref created, ref work)) return false;
                continue;
            }
            if (common is null) common = branch;
            else
            {
                work += branch.CounterChanges.Count + branch.StockChanges.Count + (long)branch.Arrivals.Count;
                foreach (var changes in branch.StockChanges.Values) work += changes.Count;
                foreach (var arrivals in branch.Arrivals.Values) work += arrivals.Count;
                if (includeDiscoveries)
                {
                    work += branch.CompletedResearch!.Count + (long)branch.ResearchStatuses!.Count + branch.ActiveResearch!.Count;
                    foreach (var active in branch.ActiveResearch.Values) work += active.Count;
                }
                if (work > 1_000_000 || !SameResearchStockConsequences(common, branch, includeMetadata: !retainWorldAlternatives) ||
                    includeDiscoveries && !retainWorldAlternatives && !SameResearchDiscoveryConsequences(common, branch)) return false;
            }
            if (alternatives is not null)
            {
                var repeated = false;
                foreach (var earlier in alternatives)
                {
                    work += 1L + branch.CompletedResearch!.Count + branch.ResearchStatuses!.Count + branch.ActiveResearch!.Count;
                    foreach (var active in branch.ActiveResearch.Values) work += active.Count;
                    if (work > 1_000_000) return false;
                    if (earlier.ScoreChange != branch.ScoreChange || earlier.DiaryEntries != branch.DiaryEntries ||
                        !SameResearchDiscoveryConsequences(earlier, branch)) continue;
                    repeated = true;
                    break;
                }
                if (!repeated)
                {
                    if (alternatives.Count == 256)
                    {
                        forecast = branch with { UnavailableReason = "Daily research exceeds the supported world outcome limit." };
                        return false;
                    }
                    if (++work > 1_000_000) return false;
                    alternatives.Add(branch);
                }
            }
        }
        if (common is null) return false;
        // Shared stock/counter effects agree. Exact callers require one common state;
        // the world can retain bounded discovery states without selecting a reward.
        if (alternatives?.Count > 1)
        {
            work += 2L * common.CompletedResearch!.Count;
            foreach (var branch in alternatives) work += branch.CompletedResearch!.Count;
            if (work > 1_000_000) return false;
            var guaranteed = new HashSet<string>(common.CompletedResearch, StringComparer.Ordinal);
            foreach (var branch in alternatives) guaranteed.IntersectWith(branch.CompletedResearch!);
            forecast = common with
            {
                Work = work,
                CompletedResearch = guaranteed.ToFrozenSet(StringComparer.Ordinal),
                ResearchStatuses = null,
                ActiveResearch = null,
                ScoreChange = null,
                DiaryEntries = null,
                DiscoveryAlternatives = alternatives.AsReadOnly(),
            };
        }
        else forecast = common with { Work = work };
        return true;
    }

    private static bool EnqueueResearchStockBranches(Queue<int[]> pending, int[] prefix, IReadOnlyList<string> candidates,
        ref int created, ref long projectionWork)
    {
        // Duplicate labels weight actual selection, but have the same pure
        // consequence at this choice. Retain the first index of each identity.
        projectionWork += candidates.Count;
        if (projectionWork > 1_000_000) return false;
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (var index = 0; index < candidates.Count; index++)
        {
            if (seen.Contains(candidates[index])) continue;
            if (created == MaximumResearchStockAlternatives) return false;
            projectionWork += prefix.Length + 1L;
            if (projectionWork > 1_000_000) return false;
            seen.Add(candidates[index]);
            var path = new int[prefix.Length + 1];
            prefix.CopyTo(path, 0);
            path[^1] = index;
            pending.Enqueue(path);
            created++;
        }
        return true;
    }

    private static bool SameResearchDiscoveryConsequences(DailyResearchStockForecast first, DailyResearchStockForecast second)
    {
        if (!first.CompletedResearch!.SetEquals(second.CompletedResearch!) ||
            first.ResearchStatuses!.Count != second.ResearchStatuses!.Count ||
            first.ActiveResearch!.Count != second.ActiveResearch!.Count) return false;
        foreach (var status in first.ResearchStatuses)
            if (!second.ResearchStatuses.TryGetValue(status.Key, out var value) || value != status.Value) return false;
        foreach (var owner in first.ActiveResearch)
            if (!second.ActiveResearch.TryGetValue(owner.Key, out var active) || !owner.Value.SetEquals(active)) return false;
        return true;
    }

    private static bool SameResearchStockConsequences(DailyResearchStockForecast first, DailyResearchStockForecast second, bool includeMetadata = true)
    {
        if (first.NextTransferId != second.NextTransferId ||
            includeMetadata && (first.ScoreChange != second.ScoreChange || first.DiaryEntries != second.DiaryEntries) ||
            !first.CounterChanges.SequenceEqual(second.CounterChanges) ||
            first.StockChanges.Count != second.StockChanges.Count || first.Arrivals.Count != second.Arrivals.Count) return false;
        foreach (var owner in first.StockChanges)
        {
            if (!second.StockChanges.TryGetValue(owner.Key, out var other) || owner.Value.Count != other.Count) return false;
            foreach (var item in owner.Value)
                if (!other.TryGetValue(item.Key, out var quantity) || quantity != item.Value) return false;
        }
        foreach (var owner in first.Arrivals)
            if (!second.Arrivals.TryGetValue(owner.Key, out var other) || !owner.Value.SequenceEqual(other)) return false;
        return true;
    }
}
