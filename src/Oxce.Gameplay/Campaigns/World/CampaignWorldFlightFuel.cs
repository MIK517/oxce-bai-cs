using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string FlightFuelRangeReason = "Craft flight fuel values exceed the supported range.";

    private string? FlightFuelValuesReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.TenMinutes) return null;
        var rules = campaign.Content.RuntimeRules;
        long work = 0;
        foreach (var owner in campaign.BaseStates)
            foreach (var craft in owner.Crafts)
            {
                if (craft.Logistics is not { Status: "STR_OUT" } state) continue;
                work += 1L + state.Weapons.Count * 2L;
                if (work > MaximumAlienBaseChecks) return "Craft flight fuel checks exceed the supported work limit.";
                var rule = rules.Crafts[craft.Rule].Value;
                if (FlightFuelStateReason(state, rule) is { } reason) return reason;
            }
        return null;
    }

    private string? FlightFuelStateReason(CraftLogisticsState state, RuntimeCraftRule rule)
    {
        if (state.Status != "STR_OUT" || state.Destination is null && rule.PatrolWithoutFuel) return null;
        var rules = campaign.Content.RuntimeRules;
        if (!CraftLogistics.TryEffectiveFuelMaximum(rule, state.Weapons, rules, out var maximum) ||
            maximum < 0 || state.Fuel < 0) return FlightFuelRangeReason;
        if (rule.RefuelItem is null)
        {
            if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons, rules, out var speedMaximum))
                return SpeedRangeReason;
            // Include saved speed, optional returns and positive escort capping.
            // Unsafe signed subtraction must stop before forecasts or execution.
            var minimumBurn = Math.Min(WorldFlight.FuelConsumption(false, speedMaximum, state.Speed, 0),
                Math.Min(speedMaximum / 100, speedMaximum / 200));
            return (long)state.Fuel - minimumBurn > int.MaxValue ? FlightFuelRangeReason : null;
        }
        // Item burn is independent of escort/saved speed. Its overflow
        // arithmetic is consumed only when the post-burn fuel exceeds capacity.
        if ((long)state.Fuel - 1 <= maximum) return null;
        var overflow = (long)state.Fuel - 1 - maximum + state.ExcessFuel;
        if (overflow > int.MaxValue) return FlightFuelRangeReason;
        return overflow > 0 && rule.RefuelRate <= 0
            ? "Item flight fuel refunds require a positive refuel rate." : null;
    }

    private string? FlightFuelRefundReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.TenMinutes) return null;
        var rules = campaign.Content.RuntimeRules;
        var possible = false;
        long work = 0;
        foreach (var owner in campaign.BaseStates)
            foreach (var craft in owner.Crafts)
            {
                if (++work > MaximumAlienBaseChecks) return "Craft flight fuel refunds exceed the supported work limit.";
                if (craft.Logistics is not { } state) continue;
                var rule = rules.Crafts[craft.Rule].Value;
                if (state.Status != "STR_OUT" &&
                    !(highest >= CampaignTimeTrigger.ThirtyMinutes && state.IsAutoPatrolling && rule.AutoPatrol)) continue;
                if (state.Status == "STR_OUT" && state.Destination is null && rule.PatrolWithoutFuel) continue;
                work += state.Weapons.Count * 2L;
                if (work > MaximumAlienBaseChecks) return "Craft flight fuel refunds exceed the supported work limit.";
                if (!CraftLogistics.TryEffectiveFuelMaximum(rule, state.Weapons, rules, out var maximum))
                    return FlightFuelRangeReason;
                if (rule.RefuelItem is null)
                {
                    // A grounded ordinary craft can also acquire a negative
                    // speed on relaunch. Validate its potential subtraction even
                    // when no hunter requests the full craft forecast.
                    if (state.Status == "STR_OUT") continue;
                    if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons, rules, out var speedMaximum))
                        return SpeedRangeReason;
                    possible |= Math.Max((long)state.Fuel, maximum) -
                        WorldFlight.FuelConsumption(false, speedMaximum, speedMaximum, 0) > int.MaxValue;
                }
                else possible |= (long)state.Fuel - 1 > maximum;
            }
        if (!possible) return null;
        if (highest < CampaignTimeTrigger.ThirtyMinutes)
            return Check(campaign.BaseStates.ToDictionary(owner => owner.Id, owner => owner.Crafts),
                campaign.BaseStates.ToDictionary(owner => owner.Id, owner => owner.Items), null);

        // Reuse the pure servicing/delivery/production projection. The temporary
        // before-site forecast stops before fuel, and must not replace the full
        // hunter forecast consumed by later movement checks.
        var originalSources = _pass.HuntingCraftSources;
        var originalForecast = _pass.HuntingCraftForecast;
        try
        {
            var stocks = new SiteResearchItemForecast(null);
            if (ForecastHuntingCrafts(highest, ref work, stocks) is { } reason) return reason;
            return Check(_pass.HuntingCraftSources!, stocks.Stocks, _pass.HuntingCraftForecast);
        }
        finally
        {
            _pass.HuntingCraftSources = originalSources;
            _pass.HuntingCraftForecast = originalForecast;
        }

        string? Check(IReadOnlyDictionary<int, List<CampaignState.CraftState>> sources,
            IReadOnlyDictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> stocks,
            IReadOnlyDictionary<(string TypeId, int Id), CraftLogisticsState>? forecast)
        {
            foreach (var owner in campaign.BaseStates)
            {
                Dictionary<RuleHandle<ItemRuleFamily>, long> refunds = [];
                foreach (var craft in sources[owner.Id])
                {
                    if (++work > MaximumAlienBaseChecks) return "Craft flight fuel refunds exceed the supported work limit.";
                    var state = forecast?.GetValueOrDefault((rules.Crafts.GetExternalId(craft.Rule), craft.Id)) ?? craft.Logistics!;
                    var rule = rules.Crafts[craft.Rule].Value;
                    if (state.Status != "STR_OUT" || state.Destination is null && rule.PatrolWithoutFuel) continue;
                    work += state.Weapons.Count * 3L;
                    if (work > MaximumAlienBaseChecks) return "Craft flight fuel refunds exceed the supported work limit.";
                    if (FlightFuelStateReason(state, rule) is { } fuelReason) return fuelReason;
                    if (rule.RefuelItem is not { } item) continue;
                    try
                    {
                        var fuel = CraftLogistics.SetFuel(state, rule, rules, checked(state.Fuel - 1));
                        var total = refunds.GetValueOrDefault(item) + fuel.FuelItemChange;
                        if ((long)stocks[owner.Id].GetValueOrDefault(item) + total > int.MaxValue)
                            return "Craft flight fuel refunds exceed the supported stock range.";
                        refunds[item] = total;
                    }
                    catch (OverflowException) { return FlightFuelRangeReason; }
                    catch (InvalidDataException error) { return error.Message; }
                }
            }
            return null;
        }
    }
}
