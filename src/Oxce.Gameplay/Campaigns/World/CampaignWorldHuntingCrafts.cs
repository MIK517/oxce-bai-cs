using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? ForecastHuntingCrafts(CampaignTimeTrigger highest, ref long work)
    {
        _huntingCraftForecast = [];
        _huntingCraftSources = [];
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
        {
            Dictionary<RuleHandle<ItemRuleFamily>, int>? stocks = null;
            work += owner.Crafts.Count + (long)owner.Items.Count + owner.Transfers.Count;
            if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
            if (highest >= CampaignTimeTrigger.ThirtyMinutes) stocks = new(owner.Items);
            var sources = new List<CampaignState.CraftState>(owner.Crafts);
            _huntingCraftSources.Add(owner.Id, sources);
            foreach (var craft in owner.Crafts)
            {
                if (craft.Logistics is null) return "Craft servicing requires resolved craft state.";
                if (ForecastHuntingCraftState(owner, craft, highest, stocks, out var state) is { } reason) return reason;
                _huntingCraftForecast.Add((rules.Crafts.GetExternalId(craft.Rule), craft.Id), state);
            }
            if (highest < CampaignTimeTrigger.OneHour) continue;
            foreach (var transfer in owner.Transfers)
            {
                if (transfer.Delivered || transfer.Hours > 1 || transfer.Craft is not { Logistics: { } logistics } incoming) continue;
                var handle = rules.Crafts.GetRequired(incoming.RuleId);
                var rule = rules.Crafts[handle].Value;
                if (!CraftLogistics.TryEffectiveServiceCapacities(rule, logistics.Weapons, rules, out var capacities) ||
                    capacities.FuelMaximum < 0) return "Craft service capacity exceeds the supported range.";
                var craft = new CampaignState.CraftState(handle, incoming.Id)
                {
                    Logistics = CraftLogistics.Arrive(logistics, rule, rules, owner.Longitude, owner.Latitude),
                };
                if (ForecastHuntingCraftState(owner, craft, highest, stocks, out var state, arriving: true) is { } reason) return reason;
                _huntingCraftForecast.Add((incoming.RuleId, incoming.Id), state);
                sources.Add(craft);
            }
        }
        if (highest < CampaignTimeTrigger.TenMinutes) return null;
        // Every relaunch precedes fuel use. Fuel itself runs in saved craft order;
        // an earlier leader's return can change its speed before a later escort burns fuel.
        foreach (var owner in campaign.BaseStates)
            foreach (var craft in _huntingCraftSources[owner.Id])
            {
                if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                var key = (rules.Crafts.GetExternalId(craft.Rule), craft.Id);
                var state = _huntingCraftForecast[key];
                if (state.Status != "STR_OUT") continue;
                if (!owner.IsPlaced || !new WorldPosition(state.Longitude, state.Latitude).IsNormalized)
                    return "Airborne craft coordinates require a placed base and normalized positions.";
                if (!CraftLogistics.TryEffectiveSpeedMaximum(rules.Crafts[craft.Rule].Value, state.Weapons, rules, out _))
                    return SpeedRangeReason;
                _huntingCraftForecast[key] = AdvanceCraftFuel(owner, craft, state, _huntingCraftForecast);
            }
        return null;
    }

    private CampaignState.CraftState? ForecastHuntingTarget(WorldTargetReference target)
    {
        if (_huntingCraftForecast?.GetValueOrDefault((target.TypeId, target.Id)) is not { } state)
            return TargetCraft(target);
        return new(campaign.Content.RuntimeRules.Crafts.GetRequired(target.TypeId), target.Id) { Logistics = state };
    }

    /// <summary>Pure craft state at the native retarget caller, before five-second movement.</summary>
    private string? ForecastHuntingCraftState(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CampaignTimeTrigger highest, Dictionary<RuleHandle<ItemRuleFamily>, int>? fuelStocks, out CraftLogisticsState state,
        bool arriving = false)
    {
        state = craft.Logistics!;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Crafts[craft.Rule].Value;
        if (highest < CampaignTimeTrigger.ThirtyMinutes || state.Status == "STR_OUT") return null;
        if (highest >= CampaignTimeTrigger.OneHour && !arriving)
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
