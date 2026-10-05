using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? ForecastHuntingHourlyInputs(CampaignState.BaseState owner,
        List<CampaignState.CraftState> sources, Dictionary<RuleHandle<ItemRuleFamily>, int> stocks,
        CampaignTimeTrigger highest, bool researchMayChangeStocks,
        IReadOnlyList<KeyValuePair<string, int>>? researchArrivals, ref long work)
    {
        var rules = campaign.Content.RuntimeRules;
        var fuelItems = new HashSet<RuleHandle<ItemRuleFamily>>();
        foreach (var craft in sources)
            if (rules.Crafts[craft.Rule].Value.RefuelItem is { } item) fuelItems.Add(item);
        foreach (var transfer in owner.Transfers)
            if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Craft is { } incoming &&
                rules.Crafts[rules.Crafts.GetRequired(incoming.RuleId)].Value.RefuelItem is { } item) fuelItems.Add(item);
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
                var weapon = state.Weapons.FirstOrDefault(weapon => weapon is { Rearming: true });
                try
                {
                    if (CraftServicing.TryForecastRearm(state, rules, serviceStocks, out var result))
                    {
                        state = result.State;
                        if (result.ClipsUsed != 0)
                        {
                            var clip = rules.Items.GetRequired(result.Clip);
                            stocks[clip] = stocks.GetValueOrDefault(clip) - result.ClipsUsed;
                            serviceStocks[result.Clip] = stocks[clip];
                        }
                    }
                    else if (weapon is not null &&
                        rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value.Clip is { Length: > 0 } clip &&
                        fuelItems.Contains(rules.Items.GetRequired(clip)))
                        return "UFO retargeting requires stochastic craft ammunition stock forecasts.";
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
            if (rule.AmmoItem is { } consumed && result.Used != 0)
                stocks[consumed] = stocks.GetValueOrDefault(consumed) - result.Used;
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
        foreach (var production in owner.Productions)
        {
            if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
            var rule = rules.Manufacture[production.Rule].Value;
            if ((long)production.Spent + production.Assigned > int.MaxValue)
                return "Production progress exceeds the supported range.";
            if (rule.Time < 0) return "Production rule contains an unsupported negative value.";
            // Existing units were paid when started. Without a completion, non-fallback
            // projects cannot consume or deliver stocks/craft during this hour.
            if (!production.IsFallback && ProductionProgress.Requested(rule.Time, production.Spent,
                production.Assigned, production.Amount, production.Infinite) == 0) continue;
            work += rule.RequiredMaterials.Count * (long)sources.Count + rule.ProducedMaterials.Count + sources.Count;
            if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
            if (rule.RequiredMaterials.Any(material => material.Quantity > 0 && material.Craft is { } type && sources.Any(craft =>
                    craft.Rule == type && craft.Logistics?.Status != "STR_OUT")))
                return "UFO retargeting requires production craft-consumption forecasts.";
            if (fuelItems.Count != 0) return "UFO retargeting requires production fuel-stock forecasts.";
            foreach (var craft in sources)
                if (craft.Logistics is { Status: "STR_REFUELLING" or "STR_READY" } state &&
                    state.Weapons.Any(weapon => weapon is { Disabled: false } &&
                        rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value is { Clip.Length: > 0 } weaponRule &&
                        weapon.Ammo < weaponRule.AmmoMaximum))
                    return "UFO retargeting requires same-tick craft ammunition reuse forecasts.";
        }
        return null;
    }
}
