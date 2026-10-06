using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? ForecastHuntingHourlyInputs(CampaignState.BaseState owner,
        List<CampaignState.CraftState> sources, Dictionary<RuleHandle<ItemRuleFamily>, int> stocks,
        CampaignTimeTrigger highest, bool researchMayChangeStocks,
        IReadOnlyList<KeyValuePair<string, int>>? researchArrivals,
        Dictionary<RuleHandle<ItemRuleFamily>, int> uncertainty, ref long work)
    {
        var rules = campaign.Content.RuntimeRules;
        var fuelItems = new HashSet<RuleHandle<ItemRuleFamily>>();
        foreach (var craft in sources)
            if (rules.Crafts[craft.Rule].Value.RefuelItem is { } item) fuelItems.Add(item);
        foreach (var transfer in owner.Transfers)
            if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Craft is { } incoming &&
                rules.Crafts[rules.Crafts.GetRequired(incoming.RuleId)].Value.RefuelItem is { } item) fuelItems.Add(item);
        foreach (var project in owner.Productions)
        {
            var product = rules.Manufacture[project.Rule].Value;
            work += 1L + product.ProducedMaterials.Count;
            if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
            if (product.TransferTimes.Count > 2 && product.TransferTimes[2] > 0) continue;
            foreach (var material in product.ProducedMaterials)
                if (material.Craft is { } craft && material.Quantity > 0 && rules.Crafts[craft].Value.RefuelItem is { } fuel)
                    fuelItems.Add(fuel);
        }
        if (researchMayChangeStocks && fuelItems.Count != 0)
            return "UFO retargeting requires daily research fuel-stock forecasts.";
        // Completion elsewhere can cancel held projects in this base and refund fuel.
        // Spawned items also arrive/reuse before refuelling, even for non-item-fuel craft.
        if (researchMayChangeStocks && sources.Any(craft => craft.Logistics is { Status: not "STR_OUT" } state &&
            state.Weapons.Any(weapon => weapon is { Disabled: false } &&
                rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value is { Clip.Length: > 0 } weaponRule &&
                weapon.Ammo < weaponRule.AmmoMaximum)))
            return "UFO retargeting requires daily research ammunition-reuse forecasts.";

        work += stocks.Count;
        if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
        Dictionary<string, int>? minimumServiceStocks = null;
        var serviceStocks = stocks.ToDictionary(pair => rules.Items.GetExternalId(pair.Key), pair => pair.Value, StringComparer.Ordinal);
        for (var index = 0; index < sources.Count; index++)
        {
            var craft = sources[index];
            var state = craft.Logistics!;
            var rule = rules.Crafts[craft.Rule].Value;
            if (state.Status == "STR_REPAIRS")
            {
                if (rule.RepairRate < 0 || (long)state.Damage - rule.RepairRate < int.MinValue)
                    return "Craft service rate exceeds the supported range.";
                state = CraftServicing.Repair(state, rule);
            }
            else if (state.Status == "STR_REARMING")
            {
                try
                {
                    work += 1L + state.Weapons.Count * 4L;
                    if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                    if (!CraftServicing.TryForecastRearmRange(state, rules, serviceStocks, out var upper))
                        return "UFO retargeting requires stochastic craft ammunition transition forecasts.";
                    var lower = upper;
                    if (minimumServiceStocks is not null &&
                        (!CraftServicing.TryForecastRearmRange(state, rules, minimumServiceStocks, out lower) ||
                         lower.MissingAmmo != upper.MissingAmmo || !lower.State.Weapons.SequenceEqual(upper.State.Weapons) ||
                         (lower.State with { Weapons = upper.State.Weapons }) != upper.State))
                        return "UFO retargeting requires stochastic shared craft ammunition forecasts.";
                    if (upper.Clip.Length != 0)
                    {
                        var clip = rules.Items.GetRequired(upper.Clip);
                        var maximum = serviceStocks.GetValueOrDefault(upper.Clip) - upper.MinimumUsed;
                        var minimum = (minimumServiceStocks ?? serviceStocks).GetValueOrDefault(upper.Clip) - lower.MaximumUsed;
                        if (maximum != minimum && fuelItems.Contains(clip))
                            return "UFO retargeting requires stochastic craft ammunition stock forecasts.";
                        if (maximum != minimum && minimumServiceStocks is null)
                        {
                            work += serviceStocks.Count;
                            if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                            minimumServiceStocks = new(serviceStocks, StringComparer.Ordinal);
                        }
                        stocks[clip] = serviceStocks[upper.Clip] = maximum;
                        if (minimumServiceStocks is not null) minimumServiceStocks[upper.Clip] = minimum;
                        if (minimum != maximum) uncertainty[clip] = maximum - minimum;
                        else uncertainty.Remove(clip);
                    }
                    state = upper.State;
                }
                catch (Exception exception) when (exception is OverflowException or InvalidDataException or ArgumentOutOfRangeException)
                { return "Craft weapon service values exceed the supported range."; }
            }
            sources[index] = craft with { Logistics = state };
        }
        foreach (var facility in owner.Facilities)
        {
            if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
            var rule = rules.Facilities[facility.Rule].Value;
            if (rule.RearmRate < 0 || facility.Ammo < 0) return "Facility service values cannot be negative.";
            var result = FacilityServicing.Rearm(facility.Ammo, rule.AmmoMaximum, rule.RearmRate,
                highest >= CampaignTimeTrigger.OneDay ? Math.Max(0, facility.BuildTime - 1) : facility.BuildTime,
                facility.AmmoMissingReported, rule.AmmoItem is not null,
                rule.AmmoItem is { } item ? stocks.GetValueOrDefault(item) : 0);
            if (rule.AmmoItem is { } consumed)
            {
                var maximum = stocks.GetValueOrDefault(consumed);
                var minimum = maximum - uncertainty.GetValueOrDefault(consumed);
                var lower = FacilityServicing.Rearm(facility.Ammo, rule.AmmoMaximum, rule.RearmRate,
                    highest >= CampaignTimeTrigger.OneDay ? Math.Max(0, facility.BuildTime - 1) : facility.BuildTime,
                    facility.AmmoMissingReported, true, minimum);
                if (lower.Ammo != result.Ammo || lower.MissingReported != result.MissingReported || lower.Notify != result.Notify)
                    return "UFO retargeting requires stochastic facility ammunition forecasts.";
                stocks[consumed] = maximum - result.Used;
                var difference = stocks[consumed] - (minimum - lower.Used);
                if (difference != 0) uncertainty[consumed] = difference;
                else uncertainty.Remove(consumed);
            }
        }
        // Transfers run after all hourly servicing and before half-hour refuelling.
        // ItemsArrivingState reuses delivered items after all due craft have arrived.
        List<RuleHandle<ItemRuleFamily>> reusableItems = [];
        foreach (var transfer in owner.Transfers)
        {
            if (transfer.Delivered || transfer.Hours > 1) continue;
            if (transfer.Hours <= 0) return "Undelivered transfer arrival requires positive remaining hours.";
            if (transfer.Craft is { Logistics: { } logistics } incoming)
            {
                var handle = rules.Crafts.GetRequired(incoming.RuleId);
                var rule = rules.Crafts[handle].Value;
                if (!CraftLogistics.TryEffectiveServiceCapacities(rule, logistics.Weapons, rules, out var capacities) ||
                    capacities.FuelMaximum < 0) return "Craft service capacity exceeds the supported range.";
                sources.Add(new(handle, incoming.Id)
                { Logistics = CraftLogistics.Arrive(logistics, rule, rules, owner.Longitude, owner.Latitude) });
            }
            else if (transfer.Kind == CampaignTransferKind.Item)
            {
                var item = rules.Items.GetRequired(transfer.RuleId);
                var quantity = (long)stocks.GetValueOrDefault(item) + transfer.Quantity;
                if (quantity > int.MaxValue) return "Transfer arrivals exceed the supported stock range.";
                stocks[item] = (int)quantity;
                if (rules.Items[item].Value.BattleType != 0) continue;
                reusableItems.Add(item);
            }
        }
        if (researchArrivals is not null)
            foreach (var arrival in researchArrivals)
            {
                if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                var item = rules.Items.GetRequired(arrival.Key);
                var quantity = (long)stocks.GetValueOrDefault(item) + arrival.Value;
                if (quantity > int.MaxValue) return "Research arrivals exceed the supported stock range.";
                stocks[item] = (int)quantity;
                if (rules.Items[item].Value.BattleType == 0) reusableItems.Add(item);
            }
        work += reusableItems.Count * (long)sources.Count;
        if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
        foreach (var item in reusableItems)
            for (var index = 0; index < sources.Count; index++)
            {
                var craft = sources[index];
                var rule = rules.Crafts[craft.Rule].Value;
                if (!CraftLogistics.TryEffectiveServiceCapacities(rule, craft.Logistics!.Weapons, rules, out _))
                    return "Craft service capacity exceeds the supported range.";
                sources[index] = craft with { Logistics = CraftServicing.ReuseItem(craft.Logistics, rule, rules, item) };
            }
        return null;
    }
}
