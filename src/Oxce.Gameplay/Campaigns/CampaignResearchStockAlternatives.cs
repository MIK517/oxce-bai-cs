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
                if (work > 1_000_000 || !SameResearchStockConsequences(common, branch, includeMetadata: !retainWorldAlternatives, includeQuantities: !retainWorldAlternatives) ||
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
                    if (earlier.ScoreChange != branch.ScoreChange || earlier.MinimumScorePrefix != branch.MinimumScorePrefix ||
                        earlier.MaximumScorePrefix != branch.MaximumScorePrefix || earlier.DiaryEntries != branch.DiaryEntries ||
                        !SameResearchDiscoveryConsequences(earlier, branch)) continue;
                    work += branch.Arrivals.Count;
                    foreach (var arrivals in branch.Arrivals.Values) work += arrivals.Count;
                    if (work > 1_000_000) return false;
                    if (!SameResearchArrivals(earlier, branch)) continue;
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
        // Shared refund/counter effects and arrival shape agree. Exact callers require one common state;
        // the world can retain bounded discovery states without selecting a reward.
        if (alternatives?.Count > 1)
        {
            work += 2L * common.CompletedResearch!.Count;
            var minimumPrefix = common.MinimumScorePrefix!.Value;
            var maximumPrefix = common.MaximumScorePrefix!.Value;
            foreach (var branch in alternatives)
            {
                work += branch.CompletedResearch!.Count;
                minimumPrefix = Math.Min(minimumPrefix, branch.MinimumScorePrefix!.Value);
                maximumPrefix = Math.Max(maximumPrefix, branch.MaximumScorePrefix!.Value);
            }
            if (work > 1_000_000) return false;
            if (!TryBoundResearchArrivals(alternatives, ref work, out var arrivals, out var uncertainty)) return false;
            var guaranteed = new HashSet<string>(common.CompletedResearch, StringComparer.Ordinal);
            foreach (var branch in alternatives) guaranteed.IntersectWith(branch.CompletedResearch!);
            forecast = common with
            {
                Work = work,
                Arrivals = arrivals,
                ArrivalUncertainty = uncertainty,
                CompletedResearch = guaranteed.ToFrozenSet(StringComparer.Ordinal),
                ResearchStatuses = null,
                ActiveResearch = null,
                ScoreChange = null,
                MinimumScorePrefix = minimumPrefix,
                MaximumScorePrefix = maximumPrefix,
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

    private static bool SameResearchStockConsequences(DailyResearchStockForecast first, DailyResearchStockForecast second, bool includeMetadata = true, bool includeQuantities = true)
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
            if (!second.Arrivals.TryGetValue(owner.Key, out var other) || owner.Value.Count != other.Count) return false;
            else
                for (var index = 0; index < owner.Value.Count; index++)
                    if (owner.Value[index].Key != other[index].Key || includeQuantities && owner.Value[index].Value != other[index].Value) return false;
        return true;
    }

    private static bool SameResearchArrivals(DailyResearchStockForecast first, DailyResearchStockForecast second)
    {
        if (first.Arrivals.Count != second.Arrivals.Count) return false;
        foreach (var owner in first.Arrivals)
            if (!second.Arrivals.TryGetValue(owner.Key, out var other) || !owner.Value.SequenceEqual(other)) return false;
        return true;
    }

    private static bool TryBoundResearchArrivals(List<DailyResearchStockForecast> alternatives, ref long work,
        out IReadOnlyDictionary<int, IReadOnlyList<KeyValuePair<string, int>>> arrivals,
        out IReadOnlyDictionary<int, IReadOnlyDictionary<string, int>> uncertainty)
    {
        Dictionary<int, IReadOnlyList<KeyValuePair<string, int>>> upper = [];
        Dictionary<int, IReadOnlyDictionary<string, int>> deltas = [];
        arrivals = upper;
        uncertainty = deltas;
        foreach (var owner in alternatives[0].Arrivals)
        {
            work += 1L + owner.Value.Count * (long)(alternatives.Count + 3);
            if (work > 1_000_000) return false;
            List<KeyValuePair<string, int>> projected = new(owner.Value.Count);
            Dictionary<string, int> local = new(StringComparer.Ordinal);
            for (var index = 0; index < owner.Value.Count; index++)
            {
                var item = owner.Value[index];
                var minimum = item.Value;
                var maximum = item.Value;
                foreach (var branch in alternatives)
                {
                    var quantity = branch.Arrivals[owner.Key][index].Value;
                    minimum = Math.Min(minimum, quantity);
                    maximum = Math.Max(maximum, quantity);
                }
                projected.Add(new(item.Key, maximum));
                if (minimum == maximum) continue;
                var delta = (long)local.GetValueOrDefault(item.Key) + maximum - minimum;
                if (delta > int.MaxValue) return false;
                local[item.Key] = (int)delta;
            }
            upper.Add(owner.Key, projected.AsReadOnly());
            if (local.Count != 0) deltas.Add(owner.Key, local.ToFrozenDictionary(StringComparer.Ordinal));
        }
        arrivals = upper.ToFrozenDictionary();
        uncertainty = deltas.ToFrozenDictionary();
        return true;
    }
}
