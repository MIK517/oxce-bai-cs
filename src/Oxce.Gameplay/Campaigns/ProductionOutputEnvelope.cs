using Oxce.Mods.Rulesets.EquipmentProduction;

namespace Oxce.Gameplay.Campaigns;

/// <summary>Pure upper quantities for Production::step choices; never selects an outcome.</summary>
internal static class ProductionOutputEnvelope
{
    internal static bool TryCreate(IReadOnlyList<RandomProducedItems> choices,
        Func<string, bool> allowsVariation, ref long work, out IReadOnlyDictionary<string, int>? items)
    {
        items = null;
        Dictionary<string, (int Maximum, int Minimum, int Present)> bounds = new(StringComparer.Ordinal);
        var count = 0;
        foreach (var choice in choices)
        {
            work += 1L + choice.Items.Count;
            if (work > 1_000_000) return false;
            if (choice.Weight == 0) continue;
            count++;
            items ??= choice.Items;
            foreach (var item in choice.Items)
            {
                if (bounds.TryGetValue(item.Key, out var prior))
                    bounds[item.Key] = (Math.Max(prior.Maximum, item.Value), Math.Min(prior.Minimum, item.Value), prior.Present + 1);
                else bounds.Add(item.Key, (item.Value, item.Value, 1));
            }
        }
        var differs = false;
        foreach (var item in bounds)
        {
            if (++work > 1_000_000) return false;
            if (item.Value.Present == count && item.Value.Minimum == item.Value.Maximum) continue;
            differs = true;
            if (!allowsVariation(item.Key)) return false;
        }
        if (differs) items = bounds.ToDictionary(item => item.Key, item => item.Value.Maximum, StringComparer.Ordinal);
        return true;
    }
}
