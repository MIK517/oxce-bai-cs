namespace Oxce.Gameplay.Campaigns;

/// <summary>Production-owned bounds for IDs allocated before world creation.</summary>
internal interface ICampaignProductionPreflight
{
    (long Minimum, long Maximum) CraftCounter(string name, CampaignTimeTrigger highest,
        (long Minimum, long Maximum) before);
}

public sealed partial class CampaignState
{
    internal ICampaignProductionPreflight ProductionPreflight => _handlers.Operation<ICampaignProductionPreflight>();

    (long Minimum, long Maximum) ICampaignProductionPreflight.CraftCounter(string name, CampaignTimeTrigger highest,
        (long Minimum, long Maximum) before)
    {
        if (highest < CampaignTimeTrigger.OneHour) return before;
        long certain = 0;
        long possible = 0;
        long work = 0;
        var consumesCraft = false;
        var rules = _content.RuntimeRules;
        foreach (var owner in _bases)
            foreach (var production in owner.Productions)
            {
                var rule = rules.Manufacture[production.Rule].Value;
                work += 1L + rule.RequiredMaterials.Count + rule.ProducedMaterials.Count;
                if (work > 1_000_000) throw new InvalidDataException("Craft production ID forecasts exceed the supported work limit.");
                consumesCraft |= rule.RequiredMaterials.Any(material => material.Quantity > 0 && material.Craft is { } craft &&
                    rules.Crafts.GetExternalId(craft) == name);
                long quantity = 0;
                foreach (var material in rule.ProducedMaterials)
                    if (material.Quantity > 0 && material.Craft is { } craft && rules.Crafts.GetExternalId(craft) == name)
                        quantity += material.Quantity;
                if (quantity == 0) continue;
                if (production.IsFallback)
                    throw new InvalidDataException("Shared world marker production requires fallback staffing forecasts.");
                if (rule.Time <= 0) continue; // Produced returns Amount before and after.
                var done = production.Spent / rule.Time;
                var after = ((long)production.Spent + production.Assigned) / rule.Time;
                var requested = (production.Infinite ? after : Math.Min(after, production.Amount)) - done;
                if (requested <= 0) continue;
                // The first unit is already paid. Further units may stop for materials,
                // funds or capacity; never credit their uncertain IDs as collision headroom.
                certain += quantity;
                possible += quantity * requested;
                if (possible > 100_000)
                    throw new InvalidDataException("Craft production ID forecasts exceed the supported population limit.");
            }
        if (possible == 0) return before;
        if (consumesCraft)
            throw new InvalidDataException("Shared world marker production requires craft-consumption forecasts.");
        long floor = 1;
        foreach (var owner in _bases)
        {
            work += owner.Crafts.Count + (long)owner.Transfers.Count;
            if (work > 1_000_000) throw new InvalidDataException("Craft production ID forecasts exceed the supported work limit.");
            foreach (var craft in owner.Crafts)
                if (rules.Crafts.GetExternalId(craft.Rule) == name) floor = Math.Max(floor, (long)craft.Id + 1);
            foreach (var transfer in owner.Transfers)
                if (!transfer.Delivered && transfer.Craft is { } craft && craft.RuleId == name) floor = Math.Max(floor, (long)craft.Id + 1);
        }
        return (Math.Max(before.Minimum, floor) + certain, Math.Max(before.Maximum, floor) + possible);
    }
}
