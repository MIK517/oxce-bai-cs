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
                return "Targeted mission site despawn requires craft return and lost-site alert.";
            if (transition.Remove && (site.UfoUniqueId != 0 ||
                deployment.UnlockedResearchOnDespawnId.Length != 0 || deployment.DespawnEvents.Count != 0 ||
                deployment.CounterDespawn.Length != 0 || deployment.CounterFailure.Length != 0 ||
                deployment.CounterAll.Length != 0 || deployment.DecreaseCounterDespawn.Length != 0 ||
                deployment.DecreaseCounterFailure.Length != 0 || deployment.DecreaseCounterAll.Length != 0))
                return SiteDespawnBoundary;
        }
        return null;
    }

    private void AdvanceSites(CampaignState.TimeEffects _)
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
                throw new InvalidOperationException("Mission site expiry changed after world preflight.");
            ScoreUfoActivity(site.Position, transition.Score, rules);
            if (transition.Remove) _sites.RemoveAt(index);
            else _sites[index++] = site with { SecondsRemaining = transition.SecondsRemaining };
        }
    }

    private bool SiteHasFollowers(MissionSiteSnapshot site)
    {
        var marker = MarkerName(site.DeploymentId);
        bool Matches(WorldTargetReference? target) => target is
        { Kind: WorldTargetKind.MissionSite } reference && reference.Id == site.Id &&
            reference.TypeId == marker;
        return campaign.BaseStates.Any(owner => owner.Crafts.Any(craft => Matches(craft.Logistics?.Destination))) ||
            _ufos.Any(ufo => Matches(ufo.Destination));
    }
}
