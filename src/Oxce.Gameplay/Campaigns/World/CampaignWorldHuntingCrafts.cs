using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    /// <summary>Pure craft state at the native retarget caller, before five-second movement.</summary>
    private string? ForecastHuntingCraftState(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CampaignTimeTrigger highest, Dictionary<RuleHandle<ItemRuleFamily>, int>? fuelStocks, out CraftLogisticsState state)
    {
        state = craft.Logistics!;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Crafts[craft.Rule].Value;
        if (highest < CampaignTimeTrigger.ThirtyMinutes || state.Status == "STR_OUT") return null;
        if (highest >= CampaignTimeTrigger.OneHour)
        {
            if (state.Status == "STR_REPAIRS")
            {
                if (rule.RepairRate < 0 || (long)state.Damage - rule.RepairRate < int.MinValue)
                    return "Craft service rate exceeds the supported range.";
                state = CraftServicing.Repair(state, rule);
            }
            else if (state.Status == "STR_REARMING" && !state.Weapons.Any(weapon => weapon is { Rearming: true }))
                state = state with { Status = "STR_REFUELLING" };
        }
        if (state.Status != "STR_REFUELLING") return null;
        if (highest >= CampaignTimeTrigger.OneHour)
        {
            if (rule.RefuelItem is not null)
                return "UFO retargeting requires hourly refuelling stock forecasts.";
            if (owner.Productions.Count != 0 && state.Weapons.Any(weapon => weapon is { Disabled: false } &&
                    rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value is { Clip.Length: > 0 } weaponRule &&
                    weapon.Ammo < weaponRule.AmmoMaximum))
                return "UFO retargeting requires same-tick craft ammunition reuse forecasts.";
        }
        if (rule.RefuelRate < 0 || (long)state.Fuel + rule.RefuelRate > int.MaxValue ||
            !CraftLogistics.TryEffectiveServiceCapacities(rule, state.Weapons, rules, out var capacities) ||
            capacities.FuelMaximum < 0 ||
            (long)state.Fuel + rule.RefuelRate - capacities.FuelMaximum + state.ExcessFuel > int.MaxValue ||
            rule.RefuelItem is not null && state.Fuel < capacities.FuelMaximum &&
            (long)state.Fuel + rule.RefuelRate > capacities.FuelMaximum && rule.RefuelRate <= 0)
            return "Craft service capacity exceeds the supported range.";
        var result = CraftLogistics.Refuel(state, rule, rules,
            rule.RefuelItem is { } item ? fuelStocks!.GetValueOrDefault(item) : 0);
        if (rule.RefuelItem is { } fuelItem && result.FuelItemChange != 0)
        {
            var quantity = (long)fuelStocks!.GetValueOrDefault(fuelItem) + result.FuelItemChange;
            if (quantity is < 0 or > int.MaxValue) return "Craft refuelling stock exceeds the supported range.";
            fuelStocks![fuelItem] = (int)quantity;
        }
        state = result.State;
        if (result.MissingFuel || state.Status != "STR_READY" || !state.IsAutoPatrolling || !rule.AutoPatrol) return null;
        if (!owner.IsPlaced || !new WorldPosition(state.Longitude, state.Latitude).IsNormalized ||
            !new WorldPosition(state.AutoPatrolLongitude, state.AutoPatrolLatitude).IsNormalized)
            return "Auto-patrol hunting coordinates require a placed base and normalized positions.";
        if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons, rules, out var maximum)) return SpeedRangeReason;
        // The temporary destination supplies coordinates only; preflight allocates no waypoint identity.
        state = SetDestination(state, maximum, WorldTargetReference.ForWaypoint(0,
            new(state.AutoPatrolLongitude, state.AutoPatrolLatitude)));
        return null;
    }
}
