using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? ForecastHuntingHourlyInputs(CampaignState.BaseState owner,
        List<CampaignState.CraftState> sources, Dictionary<RuleHandle<ItemRuleFamily>, int> stocks,
        CampaignTimeTrigger highest, ref long work)
    {
        var rules = campaign.Content.RuntimeRules;
        var fuelItems = new HashSet<RuleHandle<ItemRuleFamily>>();
        foreach (var craft in sources)
            if (rules.Crafts[craft.Rule].Value.RefuelItem is { } item) fuelItems.Add(item);
        foreach (var transfer in owner.Transfers)
            if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Craft is { } incoming &&
                rules.Crafts[rules.Crafts.GetRequired(incoming.RuleId)].Value.RefuelItem is { } item) fuelItems.Add(item);
        if (highest >= CampaignTimeTrigger.OneDay && fuelItems.Count != 0 &&
            owner.Research.Any(project => (long)project.Spent + project.Assigned >= project.Cost))
            return "UFO retargeting requires daily research fuel-stock forecasts.";
        foreach (var facility in owner.Facilities)
        {
            if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
            var rule = rules.Facilities[facility.Rule].Value;
            if (rule.AmmoItem is { } item && fuelItems.Contains(item) && rule.RearmRate > 0 &&
                facility.Ammo < rule.AmmoMaximum && (facility.BuildTime == 0 || highest >= CampaignTimeTrigger.OneDay && facility.BuildTime == 1))
                return "UFO retargeting requires facility ammunition stock forecasts.";
        }
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
                if (weapon is null) state = state with { Status = "STR_REFUELLING" };
                else
                {
                    var weaponRule = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
                    if (weaponRule.Clip.Length != 0 && fuelItems.Contains(rules.Items.GetRequired(weaponRule.Clip)))
                        return "UFO retargeting requires craft ammunition stock forecasts.";
                }
            }
            sources[index] = craft with { Logistics = state };
        }
        // Transfers run after all hourly servicing and before half-hour refuelling.
        // ItemsArrivingState reuses delivered items after all due craft have arrived.
        List<RuleHandle<ItemRuleFamily>> reusableItems = [];
        foreach (var transfer in owner.Transfers)
        {
            if (transfer.Delivered || transfer.Hours > 1) continue;
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
            var rule = rules.Manufacture[production.Rule].Value;
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
