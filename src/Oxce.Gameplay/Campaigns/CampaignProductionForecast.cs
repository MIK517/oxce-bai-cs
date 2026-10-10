using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>Production-owned hourly stock projections and ID bounds before world creation.</summary>
internal interface ICampaignProductionPreflight
{
    // Stocks and uncertainty are caller-owned scratch maps; the operation may
    // add a base uncertainty map when random output quantities first vary.
    string? ForecastStocks(IReadOnlyDictionary<int, List<CampaignState.CraftState>> crafts,
        IReadOnlyDictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> stocks,
        Dictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> stockUncertainty,
        int? nextResearchTransferId, bool uncertainResearch, IReadOnlyList<CustomCounterChange>? researchCounters,
        CampaignTimeTrigger highest, ref long work);
    string? CraftRemovalWork(int ufos, CampaignTimeTrigger highest, ref long work, out IReadOnlySet<string> types);
    (long Minimum, long Maximum) CraftCounter(string name, CampaignTimeTrigger highest,
        (long Minimum, long Maximum) before);
    string? CraftPopulationBound(CampaignTimeTrigger highest, ref long work, out long population);
}

public sealed partial class CampaignState
{
    internal ICampaignProductionPreflight ProductionPreflight => _handlers.Operation<ICampaignProductionPreflight>();

    string? ICampaignProductionPreflight.CraftRemovalWork(int ufos, CampaignTimeTrigger highest, ref long work, out IReadOnlySet<string> types)
    {
        types = System.Collections.Frozen.FrozenSet<string>.Empty;
        if (highest < CampaignTimeTrigger.OneHour) return null;
        HashSet<string>? possibleTypes = null;
        const string reason = "Craft target removal exceeds the supported work limit.";
        long actors = ufos, removals = 0;
        var rules = _content.RuntimeRules;
        foreach (var owner in _bases)
        {
            actors += owner.Crafts.Count + owner.Transfers.Count(transfer => !transfer.Delivered && transfer.Craft is not null && transfer.Hours <= 1);
            work += owner.Transfers.Count;
            if (ForecastProductionAssignments(owner, highest, out var assignments, ref work) is { } staffing) return staffing;
            for (var index = 0; index < owner.Productions.Count; index++)
            {
                var project = owner.Productions[index]; var rule = rules.Manufacture[project.Rule].Value;
                work += rule.RequiredMaterials.Count + (long)rule.ProducedMaterials.Count;
                if (work > 1_000_000) return reason;
                var units = ProductionProgress.Requested(rule.Time, project.Spent, assignments[index], project.Amount, project.Infinite);
                var starts = units - (!project.Infinite && ProductionProgress.Produced(rule.Time,
                    project.Spent + assignments[index], project.Amount) >= project.Amount && units != 0 ? 1 : 0);
                foreach (var material in rule.RequiredMaterials)
                    if (material.Craft is { } craft && starts > 0)
                    {
                        removals += starts;
                        (possibleTypes ??= new(StringComparer.Ordinal)).Add(rules.Crafts.GetExternalId(craft));
                    }
                foreach (var material in rule.ProducedMaterials)
                    if (material.Craft is not null) actors = Math.Min(1_000_001, actors + Math.Min(1_000_001, units * (long)material.Quantity));
            }
        }
        if (removals != 0 && (actors > 1_000_000 || removals > (1_000_000 - work) / Math.Max(1, actors))) return reason;
        work += removals * actors;
        if (possibleTypes is not null) types = possibleTypes;
        return null;
    }

    (long Minimum, long Maximum) ICampaignProductionPreflight.CraftCounter(string name, CampaignTimeTrigger highest,
        (long Minimum, long Maximum) before)
    {
        if (highest < CampaignTimeTrigger.OneHour) return before;
        long certain = 0;
        long certainCrafts = 0, certainSoldiers = 0;
        var makesCraft = false;
        var makesSoldier = false;
        long possible = 0;
        long work = 0;
        var consumesCraft = false;
        var rules = _content.RuntimeRules;
        foreach (var owner in _bases)
        {
            long released = 0;
            long engineers = owner.Engineers;
            work += owner.Transfers.Count;
            foreach (var transfer in owner.Transfers)
                if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Kind == CampaignTransferKind.Engineer)
                    engineers += transfer.Quantity;
            foreach (var production in owner.Productions)
            {
                var rule = rules.Manufacture[production.Rule].Value;
                long assignedMaximum = production.Assigned;
                if (production.IsFallback)
                {
                    work += owner.Facilities.Count;
                    long workshops = 0;
                    foreach (var facility in owner.Facilities)
                        if (facility.BuildTime == 0 || highest >= CampaignTimeTrigger.OneDay && facility.BuildTime == 1)
                            workshops += Math.Max(0, rules.Facilities[facility.Rule].Value.Workshops);
                    assignedMaximum += Math.Min(engineers + released, workshops);
                }
                // Conservatively bound all assigned staff; the native caller defers
                // their release until every project in this base has stepped.
                released += assignedMaximum;
                work += 1L + rule.RequiredMaterials.Count + rule.ProducedMaterials.Count;
                if (work > 1_000_000) throw new InvalidDataException("Craft production ID forecasts exceed the supported work limit.");
                consumesCraft |= rule.RequiredMaterials.Any(material => material.Craft is { } craft &&
                    rules.Crafts.GetExternalId(craft) == name);
                long quantity = 0;
                foreach (var material in rule.ProducedMaterials)
                    if (material.Quantity > 0 && material.Craft is { } craft && rules.Crafts.GetExternalId(craft) == name)
                        quantity += material.Quantity;
                var soldierQuantity = name == "STR_SOLDIER" && rule.SpawnedPersonType is not "" and not "STR_SCIENTIST" and not "STR_ENGINEER" ? 1 : 0;
                if (quantity == 0 && soldierQuantity == 0) continue;
                if (rule.Time <= 0) continue; // Produced returns Amount before and after.
                var done = production.Spent / rule.Time;
                var after = ((long)production.Spent + production.Assigned) / rule.Time;
                var afterMaximum = ((long)production.Spent + assignedMaximum) / rule.Time;
                var requested = (production.Infinite ? after : Math.Min(after, production.Amount)) - done;
                var requestedMaximum = (production.Infinite ? afterMaximum : Math.Min(afterMaximum, production.Amount)) - done;
                if (requestedMaximum <= 0) continue;
                makesCraft |= quantity > 0;
                makesSoldier |= soldierQuantity > 0;
                if ((long)production.Spent + assignedMaximum > int.MaxValue)
                    throw new InvalidDataException("Fallback production progress forecasts exceed the supported range.");
                // The first unit is already paid. Further units may stop for materials,
                // funds or capacity; never credit their uncertain IDs as collision headroom.
                if (requested > 0)
                {
                    certainCrafts += quantity;
                    certainSoldiers += soldierQuantity;
                    certain += quantity + soldierQuantity;
                }
                quantity += soldierQuantity;
                if (quantity > 100_000 || requestedMaximum > (100_000 - possible) / quantity)
                    throw new InvalidDataException("Craft production ID forecasts exceed the supported population limit.");
                possible += quantity * requestedMaximum;
            }
        }
        if (possible == 0) return before;
        long floor = 1, soldierFloor = 1;
        foreach (var owner in _bases)
        {
            work += 1L + owner.Crafts.Count + owner.Transfers.Count * 2L + (makesSoldier ? owner.Soldiers.Count : 0);
            if (work > 1_000_000) throw new InvalidDataException("Craft production ID forecasts exceed the supported work limit.");
            foreach (var craft in owner.Crafts)
                if (makesCraft && rules.Crafts.GetExternalId(craft.Rule) == name) floor = Math.Max(floor, (long)craft.Id + 1);
            if (makesSoldier)
                foreach (var soldier in owner.Soldiers) soldierFloor = Math.Max(soldierFloor, (long)soldier.Id + 1);
            var removesDelivered = owner.Transfers.Any(transfer => !transfer.Delivered && transfer.Hours <= 1);
            foreach (var transfer in owner.Transfers)
            {
                if (removesDelivered && transfer.Delivered) continue;
                if (makesCraft && transfer.Craft is { } craft && craft.RuleId == name) floor = Math.Max(floor, (long)craft.Id + 1);
                if (makesSoldier && transfer.Soldier is { } soldier) soldierFloor = Math.Max(soldierFloor, (long)soldier.Id + 1);
            }
        }
        // Consumption can remove the saved highest craft before the allocator
        // scans it. The persisted counter remains a floor even in that outcome.
        var minimum = before.Minimum + certain;
        if (!consumesCraft && certainCrafts != 0) minimum = Math.Max(minimum, floor + certainCrafts);
        if (certainSoldiers != 0) minimum = Math.Max(minimum, soldierFloor + certainSoldiers);
        return (minimum, Math.Max(before.Maximum, Math.Max(floor, soldierFloor)) + possible);
    }
}
