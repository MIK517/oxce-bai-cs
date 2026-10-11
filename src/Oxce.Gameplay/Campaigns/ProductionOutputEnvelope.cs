using Oxce.Mods.Rulesets.EquipmentProduction;

namespace Oxce.Gameplay.Campaigns;

/// <summary>Pure upper quantities and optional lower stock bounds; never selects an outcome.</summary>
internal static class ProductionOutputEnvelope
{
    internal static bool TryCreate(IReadOnlyList<RandomProducedItems> choices,
        Func<string, bool> allowsVariation, ref long work, out IReadOnlyDictionary<string, int>? items) =>
        TryCreate(choices, allowsVariation, null, ref work, out items, out _);

    internal static bool TryCreateQuantityRanges(IReadOnlyList<RandomProducedItems> choices,
        Func<string, bool> allowsVariation, Func<string, bool> allowsQuantityVariation, ref long work,
        out IReadOnlyDictionary<string, int>? items, out IReadOnlyDictionary<string, int>? minimum) =>
        TryCreate(choices, allowsVariation, allowsQuantityVariation, ref work, out items, out minimum);

    private static bool TryCreate(IReadOnlyList<RandomProducedItems> choices,
        Func<string, bool> allowsVariation, Func<string, bool>? allowsQuantityVariation, ref long work,
        out IReadOnlyDictionary<string, int>? items, out IReadOnlyDictionary<string, int>? minimum)
    {
        items = null;
        minimum = null;
        Dictionary<string, int>? quantityMinimum = null;
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
            var inert = allowsVariation(item.Key);
            // Protected quantities do not waive presence or ordered reuse effects.
            // Even a zero-quantity present entry executes the native reuse call.
            if (!inert && (item.Value.Present != count || allowsQuantityVariation?.Invoke(item.Key) != true)) return false;
            // Inert for servicing does not mean inert for a later needItem grant.
            // Keep its lower stock too; absence in any choice means zero.
            if (allowsQuantityVariation is null) continue;
            if (++work > 1_000_000) return false;
            (quantityMinimum ??= new(StringComparer.Ordinal)).Add(item.Key, item.Value.Present == count ? item.Value.Minimum : 0);
        }
        // Reuse changes craft status immediately. Even identical quantities must
        // retain the relative delivery order of effects that are not inert.
        string[]? effectOrder = null;
        foreach (var choice in choices)
        {
            work += 1L + choice.Items.Count;
            if (work > 1_000_000) return false;
            if (choice.Weight == 0) continue;
            var order = choice.Items.Keys.Where(id => !allowsVariation(id)).ToArray();
            if (effectOrder is null) effectOrder = order;
            else if (!effectOrder.SequenceEqual(order, StringComparer.Ordinal)) return false;
        }
        if (differs) items = bounds.ToDictionary(item => item.Key, item => item.Value.Maximum, StringComparer.Ordinal);
        minimum = quantityMinimum;
        return true;
    }
}
