using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private static string? PossibleProductionTransfers(RuntimeManufactureRule rule, bool sell, int units, ref long work, out long count)
    {
        long perUnit = rule.SpawnedPersonType.Length == 0 ? 0 : 1;
        foreach (var material in rule.ProducedMaterials)
        {
            if (++work > 1_000_000) { count = 0; return "Production arrivals exceed the supported work limit."; }
            if (!sell && material.Item is not null && rule.TransferTimes.Count != 0 && rule.TransferTimes[0] > 0) perUnit++;
            if (material.Craft is not null && rule.TransferTimes.Count > 2 && rule.TransferTimes[2] > 0) perUnit += material.Quantity;
        }
        long randomEntries = 0;
        foreach (var set in rule.RandomProducedItems)
        {
            if (++work > 1_000_000) { count = 0; return "Production arrivals exceed the supported work limit."; }
            if (set.Weight > 0) randomEntries = Math.Max(randomEntries, set.Items.Count);
        }
        if (rule.TransferTimes.Count != 0 && rule.TransferTimes[0] > 0) perUnit += randomEntries;
        count = (long)Int128.Min(100_001, (Int128)perUnit * units);
        return null;
    }

    private string? ProductionPersonnelTransferCapacity(long possible, CampaignTimeTrigger highest, ref long work)
    {
        if (highest >= CampaignTimeTrigger.OneDay)
        {
            // Each primary/reward topic queues its item side effects at most
            // once per base in the daily handler. Bound all reachable kinds
            // without selecting a reward or depending on uncertain cancellation.
            foreach (var entry in _content.RuntimeRules.Research.Rules)
            {
                work += 1L + entry.Value.SpawnedItemList.Count;
                if (work > 1_000_000) return "Production arrivals exceed the supported work limit.";
                long births = entry.Value.SpawnedItem.Length != 0 && _content.RuntimeRules.Items.TryGet(entry.Value.SpawnedItem, out _) ? 1 : 0;
                births += entry.Value.SpawnedItemList.Count(id => _content.RuntimeRules.Items.TryGet(id, out _));
                possible += births * _bases.Count;
            }
        }
        work += _bases.Sum(owner => owner.Transfers.Count + (long)owner.Productions.Count + owner.Research.Count);
        if (work > 1_000_000) return "Production arrivals exceed the supported work limit.";
        // Reserve against the whole saved population. Do not rely on current
        // arrivals/notification cleanup creating capacity before later hooks.
        if (possible + _bases.Sum(owner => (long)owner.Transfers.Count) > 100_000)
            return "Production personnel arrivals exceed the supported population limit.";
        return possible > int.MaxValue - (long)NextTransferId()
            ? "Production arrivals exhaust the transfer identity range." : null;
    }
}
