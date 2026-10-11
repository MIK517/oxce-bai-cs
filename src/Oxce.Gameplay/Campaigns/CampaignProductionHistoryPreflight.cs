using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private static string? ReserveProductionHistory(ProductionState project, RuntimeManufactureRule rule, int units, ref long work)
    {
        if (rule.RandomProducedItems.Count == 0) return null;
        Dictionary<string, long> maximumRandom = new(StringComparer.Ordinal);
        foreach (var set in rule.RandomProducedItems)
        {
            if (++work > 1_000_000) return "Random production history exceeds the supported work limit.";
            if (set.Weight == 0) continue;
            foreach (var item in set.Items)
            {
                if (++work > 1_000_000) return "Random production history exceeds the supported work limit.";
                maximumRandom[item.Key] = Math.Max(maximumRandom.GetValueOrDefault(item.Key), item.Value);
            }
        }
        if (!project.Sell)
            foreach (var material in rule.ProducedMaterials)
            {
                if (++work > 1_000_000) return "Random production history exceeds the supported work limit.";
                if (material.Item is not null)
                    maximumRandom[material.Id] = maximumRandom.GetValueOrDefault(material.Id) + material.Quantity;
            }
        foreach (var item in maximumRandom)
        {
            if (++work > 1_000_000) return "Random production history exceeds the supported work limit.";
            if ((Int128)project.RandomProductionInfo.GetValueOrDefault(item.Key) + (Int128)item.Value * units > int.MaxValue)
                return "Random production history exceeds the supported range.";
        }
        return null;
    }
}
