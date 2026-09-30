using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>GeoscapeState::processMissionSite's half-hour timer and follower branch.</summary>
internal readonly record struct SiteTimerTransition(bool Remove, int SecondsRemaining, int Score,
    bool ReturnFollowers, bool LostAlert);

internal static class WorldSiteTimer
{
    internal static SiteTimerTransition Advance(int secondsRemaining, bool followed, bool despawnEvenIfTargeted,
        int points, int despawnPenalty)
    {
        if (secondsRemaining < 0) throw new InvalidDataException("Mission site timer cannot be negative.");
        var remove = secondsRemaining < 30 * 60;
        var remaining = remove ? secondsRemaining : secondsRemaining - 30 * 60;
        var returnFollowers = remove && followed && despawnEvenIfTargeted;
        if (remove && followed && !despawnEvenIfTargeted) remove = false;
        return new SiteTimerTransition(remove, remaining, remove ? despawnPenalty : points,
            returnFollowers, returnFollowers);
    }
}

internal sealed partial class CampaignWorld
{
    private const string SiteDespawnBoundary =
        "Mission site despawn requires research, counters, event payloads or UFO reactivation.";
    private const string SiteUfoFollowerBoundary =
        "Targeted mission site despawn requires UFO follower destination repair.";

    private void RegisterSiteOperations(CampaignCapabilityRegistry registry) =>
        registry.Timed(CampaignTimeTrigger.ThirtyMinutes, CampaignTimeOrder.ThirtyMinutesWorldSites,
            "mission site activity and expiry", AdvanceSites);

    private string? SiteTimingReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes) return null;
        var rules = campaign.Content.RuntimeRules;
        foreach (var site in _sites)
        {
            var mission = rules.AlienMissions[rules.AlienMissions.GetRequired(site.MissionRuleId)].Value;
            var deployment = rules.AlienDeployments[rules.AlienDeployments.GetRequired(site.DeploymentId)].Value;
            var transition = WorldSiteTimer.Advance(site.SecondsRemaining, SiteHasFollowers(site),
                mission.DespawnEvenIfTargeted, deployment.Points, deployment.DespawnPenalty);
            if (transition.ReturnFollowers)
            {
                if (_ufos.Any(ufo => FollowsSite(ufo.Destination, site)))
                    return SiteUfoFollowerBoundary;
                foreach (var owner in campaign.BaseStates)
                    foreach (var craft in owner.Crafts)
                    {
                        if (!FollowsSite(craft.Logistics?.Destination, site)) continue;
                        if (craft.Logistics is not { Status: "STR_OUT" } state || !owner.IsPlaced)
                            return "A mission site follower craft cannot return to its base.";
                        if (!CraftLogistics.TryEffectiveSpeedMaximum(
                                rules.Crafts[craft.Rule].Value, state.Weapons, rules, out _))
                            return SpeedRangeReason;
                    }
            }
            if (transition.Remove && (site.UfoUniqueId != 0 ||
                deployment.UnlockedResearchOnDespawnId.Length != 0 || deployment.DespawnEvents.Count != 0 ||
                deployment.CounterDespawn.Length != 0 || deployment.CounterFailure.Length != 0 ||
                deployment.CounterAll.Length != 0 || deployment.DecreaseCounterDespawn.Length != 0 ||
                deployment.DecreaseCounterFailure.Length != 0 || deployment.DecreaseCounterAll.Length != 0))
                return SiteDespawnBoundary;
        }
        return null;
    }

    private void AdvanceSites(CampaignState.TimeEffects effects)
    {
        var rules = campaign.Content.RuntimeRules;
        for (var index = 0; index < _sites.Count;)
        {
            var site = _sites[index];
            var mission = rules.AlienMissions[rules.AlienMissions.GetRequired(site.MissionRuleId)].Value;
            var deployment = rules.AlienDeployments[rules.AlienDeployments.GetRequired(site.DeploymentId)].Value;
            var transition = WorldSiteTimer.Advance(site.SecondsRemaining, SiteHasFollowers(site),
                mission.DespawnEvenIfTargeted, deployment.Points, deployment.DespawnPenalty);
            if (transition.ReturnFollowers)
            {
                ReturnSiteCraftFollowers(site);
                effects.Notify(new MissionSiteLost(site.Id, site.DeploymentId, site.Name));
            }
            ScoreUfoActivity(site.Position, transition.Score, rules);
            if (transition.Remove) _sites.RemoveAt(index);
            else _sites[index++] = site with { SecondsRemaining = transition.SecondsRemaining };
        }
    }

    private bool SiteHasFollowers(MissionSiteSnapshot site)
    {
        return campaign.BaseStates.Any(owner => owner.Crafts.Any(craft => FollowsSite(craft.Logistics?.Destination, site))) ||
            _ufos.Any(ufo => FollowsSite(ufo.Destination, site));
    }

    private bool FollowsSite(WorldTargetReference? target, MissionSiteSnapshot site) =>
        target is { Kind: WorldTargetKind.MissionSite } reference && reference.Id == site.Id &&
        reference.TypeId == MarkerName(site.DeploymentId);

    private bool ReturnsFromSiteBeforeMovement(WorldTargetReference destination) =>
        _sites.Any(site => FollowsSite(destination, site) && site.SecondsRemaining < 30 * 60 &&
            campaign.Content.RuntimeRules.AlienMissions[
                campaign.Content.RuntimeRules.AlienMissions.GetRequired(site.MissionRuleId)].Value.DespawnEvenIfTargeted);

    private void ReturnSiteCraftFollowers(MissionSiteSnapshot site)
    {
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
            for (var index = 0; index < owner.Crafts.Count; index++)
            {
                var craft = owner.Crafts[index];
                if (craft.Logistics is not { } state || !FollowsSite(state.Destination, site)) continue;
                if (!CraftLogistics.TryEffectiveSpeedMaximum(rules.Crafts[craft.Rule].Value,
                        state.Weapons, rules, out var speed))
                    throw new InvalidOperationException("Mission site follower speed changed after world preflight.");
                owner.Crafts[index] = craft with { Logistics = SetDestination(state, speed, BaseReference(owner)) };
            }
    }
}
