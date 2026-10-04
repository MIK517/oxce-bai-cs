namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    /// <summary>Pure craft state at the native retarget caller, before five-second movement.</summary>
    private string? ForecastHuntingCraftState(CampaignState.BaseState owner, CampaignState.CraftState craft,
        CampaignTimeTrigger highest, out CraftLogisticsState state)
    {
        state = craft.Logistics!;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Crafts[craft.Rule].Value;
        if (highest < CampaignTimeTrigger.ThirtyMinutes || state.Status == "STR_OUT" ||
            !state.IsAutoPatrolling || !rule.AutoPatrol) return null;
        if (highest >= CampaignTimeTrigger.OneHour || state.Status == "STR_REFUELLING" && rule.RefuelItem is not null)
            return "UFO retargeting requires same-tick craft relaunch forecasts.";
        if (state.Status != "STR_REFUELLING") return null;
        if (rule.RefuelRate < 0 || (long)state.Fuel + rule.RefuelRate > int.MaxValue ||
            !CraftLogistics.TryEffectiveServiceCapacities(rule, state.Weapons, rules, out var capacities) ||
            capacities.FuelMaximum < 0)
            return "Craft service capacity exceeds the supported range.";
        var result = CraftLogistics.Refuel(state, rule, rules, 0);
        state = result.State;
        if (result.MissingFuel || state.Status != "STR_READY") return null;
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
