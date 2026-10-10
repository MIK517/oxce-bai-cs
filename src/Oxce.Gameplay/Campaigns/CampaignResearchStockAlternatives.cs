namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private const int MaximumResearchStockAlternatives = 4096;

    bool ICampaignResearchPreflight.TryForecastDailyStocks(ref long work, out DailyResearchStockForecast forecast, bool includeDiscoveries)
    {
        if (TryForecastDailyStockBranch(ref work, out forecast, includeDiscoveries, [], out var pendingChoices)) return true;
        // Exact discovery callers cannot treat different research identities as one.
        if (includeDiscoveries || pendingChoices == 0) return false;
        Queue<int[]> pending = [];
        var created = 1;
        if (!EnqueueResearchStockBranches(pending, [], pendingChoices, ref created, ref work)) return false;
        DailyResearchStockForecast? common = null;
        while (pending.TryDequeue(out var choices))
        {
            if (!TryForecastDailyStockBranch(ref work, out var branch, false, choices, out pendingChoices))
            {
                if (pendingChoices == 0)
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
                if (work > 1_000_000 || !SameResearchStockConsequences(common, branch)) return false;
            }
        }
        if (common is null) return false;
        // Every terminal branch agrees. No chosen research identity is exported;
        // execution still consumes RNG and commits the actual chosen reward.
        forecast = common with { Work = work };
        return true;
    }

    private static bool EnqueueResearchStockBranches(Queue<int[]> pending, int[] prefix, int count,
        ref int created, ref long projectionWork)
    {
        // Bound both repeated projection work and stored branch prefixes before
        // allocation. Duplicate candidate labels remain in reference order.
        if (count > MaximumResearchStockAlternatives - created) return false;
        projectionWork += (long)(prefix.Length + 1) * count;
        if (projectionWork > 1_000_000) return false;
        for (var index = 0; index < count; index++)
        {
            var path = new int[prefix.Length + 1];
            prefix.CopyTo(path, 0);
            path[^1] = index;
            pending.Enqueue(path);
        }
        created += count;
        return true;
    }

    private static bool SameResearchStockConsequences(DailyResearchStockForecast first, DailyResearchStockForecast second)
    {
        if (first.NextTransferId != second.NextTransferId || first.ScoreChange != second.ScoreChange ||
            first.DiaryEntries != second.DiaryEntries || !first.CounterChanges.SequenceEqual(second.CounterChanges) ||
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
