using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private CraftLogisticsState ProducedCraftLogistics(BaseState owner, RuleHandle<CraftRuleFamily> craft)
    {
        var rules = _content.RuntimeRules;
        var definition = rules.Crafts[craft].Value;
        return CraftLogistics.Arrive(CraftLogistics.Purchase(definition, rules, owner.Longitude, owner.Latitude),
            definition, rules, owner.Longitude, owner.Latitude);
    }

    private string? ProducedCraftServiceReason(BaseState owner, RuleHandle<CraftRuleFamily> craft)
    {
        try
        {
            var rules = _content.RuntimeRules;
            var definition = rules.Crafts[craft].Value;
            var state = ProducedCraftLogistics(owner, craft);
            if (!CraftLogistics.TryEffectiveServiceCapacities(definition, state.Weapons, rules, out var capacities) ||
                capacities.FuelMaximum < 0 || definition.RefuelRate < 0)
                return "Produced craft service values exceed the supported range.";
        }
        catch (Exception error) when (error is OverflowException or InvalidDataException or ArgumentOutOfRangeException)
        { return "Produced craft service values exceed the supported range."; }
        return null;
    }

    private string? ProductionCraftCapacity(IReadOnlyDictionary<RuleHandle<CraftRuleFamily>, long> births, ref long work)
    {
        long population = births.Values.Sum();
        foreach (var owner in _bases)
        {
            work += owner.Crafts.Count + (long)owner.Transfers.Count;
            if (work > 1_000_000) return "Craft production exceeds the supported work limit.";
            population += owner.Crafts.Count + owner.Transfers.Count(transfer => !transfer.Delivered && transfer.Craft is not null);
        }
        if (population > 100_000) return "Craft production exceeds the supported population limit.";
        foreach (var birth in births)
        {
            work += _bases.Sum(owner => owner.Crafts.Count + (long)owner.Transfers.Count);
            if (work > 1_000_000) return "Craft production exceeds the supported work limit.";
            if (birth.Value > int.MaxValue - (long)NextCraftId(_content.RuntimeRules.Crafts.GetExternalId(birth.Key)))
                return "Craft production exhausts the craft identity range.";
        }
        return null;
    }

    private string? ForecastProducedCraftIdentity(RuleHandle<CraftRuleFamily> craft,
        IReadOnlyDictionary<int, List<CraftState>> sources, Dictionary<RuleHandle<CraftRuleFamily>, long> counters,
        CampaignTimeTrigger highest, ref long work, out int identity)
    {
        identity = 0;
        var type = _content.RuntimeRules.Crafts.GetExternalId(craft);
        if (!counters.TryGetValue(craft, out var next))
        {
            var range = CustomCounters.BeforeWorldCreation(type, highest);
            if (range.Minimum != range.Maximum) return "Craft production requires daily research identity forecasts.";
            next = range.Maximum;
        }
        foreach (var owner in _bases)
        {
            work += sources[owner.Id].Count + owner.Transfers.Count * 2L;
            if (work > 1_000_000) return "Craft production exceeds the supported work limit.";
            foreach (var existing in sources[owner.Id])
                if (existing.Rule == craft) next = Math.Max(next, (long)existing.Id + 1);
            var removesDelivered = owner.Transfers.Any(transfer => !transfer.Delivered && transfer.Hours <= 1);
            foreach (var transfer in owner.Transfers)
                if (!(removesDelivered && (transfer.Delivered || transfer.Hours <= 1)) && transfer.Craft is { } pending && pending.RuleId == type)
                    next = Math.Max(next, (long)pending.Id + 1);
        }
        if (next >= int.MaxValue) return "Craft production exhausts the craft identity range.";
        identity = (int)next;
        counters[craft] = next + 1;
        return null;
    }
}
