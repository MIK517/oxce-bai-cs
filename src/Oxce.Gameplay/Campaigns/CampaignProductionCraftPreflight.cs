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
}
