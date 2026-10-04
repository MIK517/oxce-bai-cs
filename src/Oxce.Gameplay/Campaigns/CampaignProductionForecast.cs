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
        {
            long released = 0;
            foreach (var production in owner.Productions)
            {
                var rule = rules.Manufacture[production.Rule].Value;
                long assignedMaximum = production.Assigned;
                if (production.IsFallback)
                {
                    work += owner.Facilities.Count;
                    assignedMaximum += Math.Min((long)owner.Engineers + released, Math.Max(0, AvailableWorkshops(owner)));
                }
                // Earlier projects may finish/stop and release their staff. This is
                // an upper bound; fallback decisions still execute only in the handler.
                released += assignedMaximum;
                work += 1L + rule.RequiredMaterials.Count + rule.ProducedMaterials.Count;
                if (work > 1_000_000) throw new InvalidDataException("Craft production ID forecasts exceed the supported work limit.");
                consumesCraft |= rule.RequiredMaterials.Any(material => material.Quantity > 0 && material.Craft is { } craft &&
                    rules.Crafts.GetExternalId(craft) == name);
                long quantity = 0;
                foreach (var material in rule.ProducedMaterials)
                    if (material.Quantity > 0 && material.Craft is { } craft && rules.Crafts.GetExternalId(craft) == name)
                        quantity += material.Quantity;
                if (quantity == 0) continue;
                if (rule.Time <= 0) continue; // Produced returns Amount before and after.
                var done = production.Spent / rule.Time;
                var after = ((long)production.Spent + production.Assigned) / rule.Time;
                var afterMaximum = ((long)production.Spent + assignedMaximum) / rule.Time;
                var requested = (production.Infinite ? after : Math.Min(after, production.Amount)) - done;
                var requestedMaximum = (production.Infinite ? afterMaximum : Math.Min(afterMaximum, production.Amount)) - done;
                if (requestedMaximum <= 0) continue;
                if ((long)production.Spent + assignedMaximum > int.MaxValue)
                    throw new InvalidDataException("Fallback production progress forecasts exceed the supported range.");
                // The first unit is already paid. Further units may stop for materials,
                // funds or capacity; never credit their uncertain IDs as collision headroom.
                if (requested > 0) certain += quantity;
                if (quantity > 100_000 || requestedMaximum > (100_000 - possible) / quantity)
                    throw new InvalidDataException("Craft production ID forecasts exceed the supported population limit.");
                possible += quantity * requestedMaximum;
            }
        }
        if (possible == 0) return before;
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
        // Consumption can remove the saved highest craft before the allocator
        // scans it. The persisted counter remains a floor even in that outcome.
        var minimum = certain == 0 ? before.Minimum :
            (consumesCraft ? before.Minimum : Math.Max(before.Minimum, floor)) + certain;
        return (minimum, Math.Max(before.Maximum, floor) + possible);
    }
}
