using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? ForecastHuntingCrafts(CampaignTimeTrigger highest, ref long work)
    {
        _pass.HuntingCraftForecast = [];
        _pass.HuntingCraftSources = [];
        var rules = campaign.Content.RuntimeRules;
        var researchMayChangeStocks = false;
        if (highest >= CampaignTimeTrigger.OneDay)
            foreach (var baseState in campaign.BaseStates)
            {
                work += baseState.Research.Count;
                if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                researchMayChangeStocks |= baseState.Research.Any(project => (long)project.Spent + project.Assigned >= project.Cost);
            }
        if (highest >= CampaignTimeTrigger.OneDay && campaign.ResearchPreflight.HasDisabledDailyProjects(ref work))
            researchMayChangeStocks = true;
        DailyResearchStockForecast? researchStocks = null;
        if (researchMayChangeStocks)
        {
            var supported = campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast);
            if (forecast.UnavailableReason is { } researchReason) return researchReason;
            if (supported)
            {
                researchStocks = forecast;
                researchMayChangeStocks = false;
            }
        }
        if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
        Dictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> baseStocks = [];
        Dictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> baseUncertainty = [];
        foreach (var owner in campaign.BaseStates)
        {
            Dictionary<RuleHandle<ItemRuleFamily>, int>? stocks = null;
            work += 1L + owner.Crafts.Count + owner.Items.Count + owner.Transfers.Count;
            if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
            if (highest >= CampaignTimeTrigger.ThirtyMinutes) stocks = new(owner.Items);
            if (researchStocks?.StockChanges.GetValueOrDefault(owner.Id) is { } changes)
                foreach (var change in changes)
                {
                    var item = rules.Items.GetRequired(change.Key);
                    var quantity = (long)stocks!.GetValueOrDefault(item) + change.Value;
                    if (quantity is < 0 or > int.MaxValue) return "Research refunds exceed the supported stock range.";
                    stocks![item] = (int)quantity;
                }
            var sources = new List<CampaignState.CraftState>(owner.Crafts);
            _pass.HuntingCraftSources.Add(owner.Id, sources);
            foreach (var craft in owner.Crafts)
                if (craft.Logistics is null) return "Craft servicing requires resolved craft state.";
            Dictionary<RuleHandle<ItemRuleFamily>, int>? uncertainty = highest >= CampaignTimeTrigger.OneHour ? [] : null;
            if (highest >= CampaignTimeTrigger.OneHour &&
                ForecastHuntingHourlyInputs(owner, sources, stocks!, highest, researchMayChangeStocks,
                    researchStocks?.Arrivals.GetValueOrDefault(owner.Id), uncertainty!, ref work) is { } inputReason)
                return inputReason;
            if (stocks is not null) baseStocks.Add(owner.Id, stocks);
            if (uncertainty?.Count > 0) baseUncertainty.Add(owner.Id, uncertainty);
        }
        foreach (var owner in campaign.BaseStates)
        {
            if (!baseUncertainty.TryGetValue(owner.Id, out var uncertainty)) continue;
            foreach (var project in owner.Productions)
            {
                var product = rules.Manufacture[project.Rule].Value;
                work += 1L + product.RequiredMaterials.Count;
                if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                if (product.RequiredMaterials.Any(material => material.Quantity > 0 && material.Item is { } item && uncertainty.ContainsKey(item)))
                    return "UFO retargeting requires stochastic production material forecasts.";
            }
        }
        if (highest >= CampaignTimeTrigger.OneHour &&
            campaign.ProductionPreflight.ForecastStocks(_pass.HuntingCraftSources, baseStocks,
                researchStocks?.NextTransferId, researchMayChangeStocks, researchStocks?.CounterChanges, highest, ref work) is { } productionReason)
            return productionReason;
        foreach (var owner in campaign.BaseStates)
        {
            var sources = _pass.HuntingCraftSources[owner.Id];
            var stocks = baseStocks.GetValueOrDefault(owner.Id);
            Dictionary<RuleHandle<ItemRuleFamily>, int>? minimumStocks = null;
            if (baseUncertainty.TryGetValue(owner.Id, out var uncertainty))
            {
                work += stocks!.Count + (long)uncertainty.Count;
                if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                minimumStocks = new(stocks);
                foreach (var item in uncertainty) minimumStocks[item.Key] = stocks.GetValueOrDefault(item.Key) - item.Value;
            }
            foreach (var craft in sources)
            {
                if (ForecastHuntingCraftState(owner, craft, highest, stocks, out var state) is { } reason) return reason;
                if (minimumStocks is not null)
                {
                    work += 1L + craft.Logistics!.Weapons.Count * 3L;
                    if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                    if (ForecastHuntingCraftState(owner, craft, highest, minimumStocks, out var lower) is { } lowerReason) return lowerReason;
                    if (!lower.Weapons.SequenceEqual(state.Weapons) || (lower with { Weapons = state.Weapons }) != state)
                        return "UFO retargeting requires stochastic craft ammunition stock forecasts.";
                }
                _pass.HuntingCraftForecast.Add((rules.Crafts.GetExternalId(craft.Rule), craft.Id), state);
            }
        }
        if (highest < CampaignTimeTrigger.TenMinutes) return null;
        // Every relaunch precedes fuel use. Fuel itself runs in saved craft order;
        // an earlier leader's return can change its speed before a later escort burns fuel.
        foreach (var owner in campaign.BaseStates)
            foreach (var craft in _pass.HuntingCraftSources[owner.Id])
            {
                if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                var key = (rules.Crafts.GetExternalId(craft.Rule), craft.Id);
                var state = _pass.HuntingCraftForecast[key];
                if (state.Status != "STR_OUT") continue;
                if (!owner.IsPlaced || !new WorldPosition(state.Longitude, state.Latitude).IsNormalized)
                    return "Airborne craft coordinates require a placed base and normalized positions.";
                if (!CraftLogistics.TryEffectiveSpeedMaximum(rules.Crafts[craft.Rule].Value, state.Weapons, rules, out _))
                    return SpeedRangeReason;
                _pass.HuntingCraftForecast[key] = AdvanceCraftFuel(owner, craft, state, _pass.HuntingCraftForecast);
            }
        return null;
    }

    private CampaignState.CraftState? ForecastHuntingTarget(WorldTargetReference target)
    {
        if (_pass.HuntingCraftForecast is null) return TargetCraft(target);
        if (_pass.HuntingCraftForecast.GetValueOrDefault((target.TypeId, target.Id)) is not { } state) return null;
        return new(campaign.Content.RuntimeRules.Crafts.GetRequired(target.TypeId), target.Id) { Logistics = state };
    }

    /// <summary>Pure craft state at the native retarget caller, before five-second movement.</summary>
    private string? ForecastHuntingCraftState(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CampaignTimeTrigger highest, Dictionary<RuleHandle<ItemRuleFamily>, int>? fuelStocks, out CraftLogisticsState state)
    {
        state = craft.Logistics!;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Crafts[craft.Rule].Value;
        if (highest < CampaignTimeTrigger.ThirtyMinutes || state.Status == "STR_OUT") return null;
        if (state.Status != "STR_REFUELLING") return null;
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
