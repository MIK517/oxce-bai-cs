namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string NewLeaderPursuitReason = "Loaded idle escorts require same-tick newborn craft pursuit forecasts.";

    private string? NewLeaderCraftPursuitReason(UfoSnapshot source, ref long work)
    {
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
        {
            if (++work > MaximumAlienBaseChecks) return "New UFO escort selection exceeds the supported work limit.";
            foreach (var craft in owner.Crafts)
            {
                if (++work > MaximumAlienBaseChecks) return "New UFO escort selection exceeds the supported work limit.";
                if (craft.Logistics is not { Status: "STR_OUT", Destination.Kind: WorldTargetKind.Ufo } state) continue;
                if (NewLeaderPursuitDependency(state.Destination!, source, ref work, out var depends) is { } dependencyReason)
                    return dependencyReason;
                if (!depends) continue;
                if (_huntingCraftForecast is null && ForecastHuntingCrafts(_newbornHighestTrigger, ref work) is { } forecastReason)
                    return forecastReason;
                if (!_huntingCraftForecast!.TryGetValue((rules.Crafts.GetExternalId(craft.Rule), craft.Id), out state) ||
                    state.Status != "STR_OUT" || state.Destination is not { Kind: WorldTargetKind.Ufo } target) continue;
                if (NewLeaderPursuitDependency(target, source, ref work, out depends) is { } projectedReason) return projectedReason;
                // Hidden, ignored and damaged craft still pursue targets. Servicing,
                // production removal and fuel return can eliminate that dependency.
                if (depends) return NewLeaderPursuitReason;
            }
        }
        // Due transfers arrive grounded; refuel-triggered relaunches receive a
        // player waypoint. Production births likewise cannot acquire a UFO target.
        return null;
    }

    private string? NewLeaderPursuitDependency(WorldTargetReference target, UfoSnapshot source,
        ref long work, out bool depends)
    {
        depends = false;
        // A saved escort chain has at most this many distinct actors. Revisiting
        // a cycle cannot introduce the changed source; never recurse on save data.
        for (var visited = 0; visited < _ufos.Count; visited++)
        {
            work += _ufos.Count + (long)_missions.Count + _sites.Count + _alienBases.Count + 1;
            if (work > MaximumAlienBaseChecks) return "New UFO escort selection exceeds the supported work limit.";
            if (TargetUfo(target) is not { } actor) return null;
            if (actor.UniqueId == source.UniqueId)
            {
                depends = true;
                return null;
            }
            var projected = UfoForFlight(actor, _newbornHighestTrigger);
            if (!projected.Escorting || projected.Destination is not { Kind: WorldTargetKind.Ufo } leader) return null;
            target = leader;
        }
        return null;
    }
}
