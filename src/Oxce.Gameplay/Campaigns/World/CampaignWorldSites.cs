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
        "Mission site despawn requires research or event payloads.";
    private const string SiteUfoFollowerBoundary =
        "Targeted mission site despawn requires UFO follower destination repair.";
    private IReadOnlySet<string>? _siteDespawnResearchForecast;

    private void RegisterSiteOperations(CampaignCapabilityRegistry registry)
    {
        registry.Timed(CampaignTimeTrigger.OneHour, CampaignTimeOrder.HourWorldSiteDiscovery,
            "mission site discovery", DiscoverSiteHourly);
        registry.Timed(CampaignTimeTrigger.ThirtyMinutes, CampaignTimeOrder.ThirtyMinutesWorldSites,
            "mission site activity and expiry", AdvanceSites);
    }

    private string? SiteDiscoveryReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.OneHour) return null;
        var first = true;
        foreach (var site in _sites)
        {
            if (site.Detected) continue;
            if (first) { first = false; continue; }
            if (site.SecondsRemaining > int.MaxValue - 3600)
                return "Mission site discovery postponement exceeds the supported timer range.";
        }
        return null;
    }

    private MissionSiteSnapshot? FirstSiteDiscoveredThisTick(CampaignTimeTrigger highest) =>
        highest >= CampaignTimeTrigger.OneHour ? _sites.Find(site => !site.Detected) : null;

    private static int SiteSecondsAfterDiscovery(MissionSiteSnapshot site, MissionSiteSnapshot? first) =>
        first is null || site.Detected ? site.SecondsRemaining :
        ReferenceEquals(site, first) ? Math.Max(3600, site.SecondsRemaining) : checked(site.SecondsRemaining + 3600);

    private void DiscoverSiteHourly(CampaignState.TimeEffects effects)
    {
        var first = FirstSiteDiscoveredThisTick(CampaignTimeTrigger.OneHour);
        if (first is null) return;
        for (var index = 0; index < _sites.Count; index++)
        {
            var site = _sites[index];
            if (site.Detected) continue;
            _sites[index] = site with
            {
                Detected = ReferenceEquals(site, first),
                SecondsRemaining = SiteSecondsAfterDiscovery(site, first),
            };
        }
        effects.Notify(new MissionSiteDetected(first.Id, MarkerName(first.DeploymentId)));
    }

    private string? SiteTimingReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes || _sites.Count == 0 && _siteBirthExpiryIncreases is not { Count: > 0 }) return null;
        var rules = campaign.Content.RuntimeRules;
        var counterChanges = new List<CustomCounterChange>();
        if (_siteBirthIdChanges is not null) counterChanges.AddRange(_siteBirthIdChanges);
        var discovered = FirstSiteDiscoveredThisTick(highest);
        long despawnWork = 0;
        foreach (var site in _sites)
        {
            var mission = rules.AlienMissions[rules.AlienMissions.GetRequired(site.MissionRuleId)].Value;
            var deployment = rules.AlienDeployments[rules.AlienDeployments.GetRequired(site.DeploymentId)].Value;
            var transition = WorldSiteTimer.Advance(SiteSecondsAfterDiscovery(site, discovered), SiteHasFollowers(site),
                mission.DespawnEvenIfTargeted, deployment.Points, deployment.DespawnPenalty);
            if (transition.ReturnFollowers)
            {
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
            if (transition.Remove && site.UfoUniqueId != 0)
            {
                var ufo = _ufos.Find(candidate => candidate.UniqueId == site.UfoUniqueId);
                if (ufo is not { Status: UfoStatus.IgnoreMe })
                    return "Mission site UFO reactivation requires a dormant UFO.";
                if (!TryLiftUfo(ufo, fromSite: true, out _, out _))
                    return "Mission site UFO reactivation speed exceeds the supported range.";
            }
            if (transition.Remove)
            {
                despawnWork += 6 + 2L * deployment.DespawnEvents.Count;
                if (despawnWork > MaximumAlienBaseChecks) return "Mission site despawn checks exceed the supported work limit.";
                if (SiteDespawnReason(deployment, highest) is { } despawnReason) return despawnReason;
            }
            if (transition.Remove) AddSiteCounterChanges(counterChanges, deployment);
        }
        if (_siteBirthExpiryIncreases is not null) counterChanges.AddRange(_siteBirthExpiryIncreases);
        return campaign.CustomCounters.CapacityReason(counterChanges, highest);
    }

    private void AdvanceSites(CampaignState.TimeEffects effects)
    {
        if (_sites.Count == 0) return;
        var rules = campaign.Content.RuntimeRules;
        var counterChanges = new List<CustomCounterChange>(6);
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
                effects.Notify(new MissionSiteLost(site.Id, MarkerName(site.DeploymentId), site.Name));
            }
            if (transition.Remove)
            {
                counterChanges.Clear();
                AddSiteCounterChanges(counterChanges, deployment);
                foreach (var change in counterChanges) campaign.CustomCounters.Apply(change);
                ConsumeSuppressedSiteEvent(deployment);
            }
            ScoreUfoActivity(site.Position, transition.Score, rules);
            if (transition.Remove && site.UfoUniqueId != 0)
            {
                var ufoIndex = _ufos.FindIndex(candidate => candidate.UniqueId == site.UfoUniqueId);
                _ufos[ufoIndex] = LiftUfo(_ufos[ufoIndex], fromSite: true);
            }
            if (transition.Remove)
            {
                for (var ufoIndex = 0; ufoIndex < _ufos.Count; ufoIndex++)
                    if (FollowsSite(_ufos[ufoIndex].Destination, site))
                        _ufos[ufoIndex] = RestoreUfoOriginalWaypoint(_ufos[ufoIndex]);
                _sites.RemoveAt(index);
            }
            else _sites[index++] = site with { SecondsRemaining = transition.SecondsRemaining };
        }
    }

    private bool SiteExpiresThisTick(MissionSiteSnapshot site, CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes || site.SecondsRemaining >= 30 * 60 ||
            highest >= CampaignTimeTrigger.OneHour && !site.Detected) return false;
        var rule = campaign.Content.RuntimeRules.AlienMissions[
            campaign.Content.RuntimeRules.AlienMissions.GetRequired(site.MissionRuleId)].Value;
        return rule.DespawnEvenIfTargeted || !SiteHasFollowers(site);
    }

    private string? SiteDespawnReason(RuntimeAlienDeploymentRule deployment, CampaignTimeTrigger highest)
    {
        var rules = campaign.Content.RuntimeRules;
        if (campaign.BaseStates.Count != 0 && rules.Research.TryGet(deployment.UnlockedResearchOnDespawnId, out _))
            return SiteDespawnBoundary;
        _ = WorldAlienMissionGeneration.Total(deployment.DespawnEvents);
        foreach (var pair in deployment.DespawnEvents)
        {
            if (pair.Value == 0 || !rules.Events.TryGet(pair.Key, out var handle)) continue;
            var rule = rules.Events[handle].Value;
            if (rule.InterruptResearch.Length == 0) return SiteDespawnBoundary;
            if (highest >= CampaignTimeTrigger.OneDay)
            {
                _siteDespawnResearchForecast ??= campaign.ResearchPreflight.GuaranteedResearchAfterDailyProgress();
                if (!_siteDespawnResearchForecast.Contains(rule.InterruptResearch)) return SiteDespawnBoundary;
            }
            else if (!campaign.HasCompletedResearch(rule.InterruptResearch)) return SiteDespawnBoundary;
        }
        return null;
    }

    private void ConsumeSuppressedSiteEvent(RuntimeAlienDeploymentRule deployment)
    {
        var id = WorldAlienMissionGeneration.Choose(deployment.DespawnEvents, campaign.Random);
        if (campaign.Content.RuntimeRules.Events.TryGet(id, out var handle) &&
            !IsEventInterrupted(campaign.Content.RuntimeRules.Events[handle].Value))
            throw new InvalidOperationException(SiteDespawnBoundary);
    }

    private string? SiteUfoRepairReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes || _ufos.Count == 0) return null;
        long work = 0;
        foreach (var site in _sites)
        {
            if (!SiteExpiresThisTick(site, highest)) continue;
            work += _ufos.Count;
            if (work > MaximumAlienBaseChecks) return "Mission site UFO follower repair exceeds the supported work limit.";
            foreach (var ufo in _ufos)
                if (FollowsSite(ufo.Destination, site) && ufo.OriginalWaypoint is null)
                    return SiteUfoFollowerBoundary;
        }
        return null;
    }

    private string? SiteFollowerWorkReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes || _sites.Count == 0) return null;
        long followers = _ufos.Count + 1L;
        foreach (var owner in campaign.BaseStates) followers += owner.Crafts.Count;
        return followers * _sites.Count > MaximumAlienBaseChecks
            ? "Mission site follower lookups exceed the supported work limit." : null;
    }

    internal static UfoSnapshot RestoreUfoOriginalWaypoint(UfoSnapshot ufo) => ufo.OriginalWaypoint is { } waypoint ?
        ufo with
        {
            Destination = WorldTargetReference.ForWaypoint(0, waypoint),
            OriginalWaypoint = null,
            Hunting = false,
            Escorting = false,
        } : ufo;

    // Pure forecast for handlers that run after the half-hour site loop: ten-minute
    // base detection, five-second flight and craft interception/deployment guards.
    private UfoSnapshot UfoAfterTimedTransitions(UfoSnapshot ufo, CampaignTimeTrigger highest)
    {
        ufo = WorldCrashedUfo.FiveSeconds(highest >= CampaignTimeTrigger.ThirtyMinutes ? WorldCrashedUfo.HalfHour(ufo) : ufo);
        if (highest < CampaignTimeTrigger.ThirtyMinutes) return ufo;
        if (ufo.Status != UfoStatus.IgnoreMe && ufo.Destination?.Kind != WorldTargetKind.MissionSite) return ufo;
        foreach (var site in _sites)
        {
            if (!SiteExpiresThisTick(site, highest)) continue;
            if (ufo.Status == UfoStatus.IgnoreMe && site.UfoUniqueId == ufo.UniqueId &&
                TryLiftUfo(ufo, fromSite: true, out var lifted, out _)) ufo = lifted;
            if (FollowsSite(ufo.Destination, site)) ufo = RestoreUfoOriginalWaypoint(ufo);
        }
        return ufo;
    }

    private bool SiteHasFollowers(MissionSiteSnapshot site)
    {
        return campaign.BaseStates.Any(owner => owner.Crafts.Any(craft => FollowsSite(craft.Logistics?.Destination, site))) ||
            _ufos.Any(ufo => FollowsSite(ufo.Destination, site));
    }

    private bool FollowsSite(WorldTargetReference? target, MissionSiteSnapshot site) =>
        target is { Kind: WorldTargetKind.MissionSite } reference && reference.Id == site.Id &&
        reference.TypeId == MarkerName(site.DeploymentId);

    private static void AddSiteCounterChanges(List<CustomCounterChange> changes, RuntimeAlienDeploymentRule deployment)
    {
        if (deployment.CounterDespawn.Length != 0) changes.Add(new(deployment.CounterDespawn, true));
        if (deployment.CounterFailure.Length != 0) changes.Add(new(deployment.CounterFailure, true));
        if (deployment.CounterAll.Length != 0) changes.Add(new(deployment.CounterAll, true));
        if (deployment.DecreaseCounterDespawn.Length != 0) changes.Add(new(deployment.DecreaseCounterDespawn, false));
        if (deployment.DecreaseCounterFailure.Length != 0) changes.Add(new(deployment.DecreaseCounterFailure, false));
        if (deployment.DecreaseCounterAll.Length != 0) changes.Add(new(deployment.DecreaseCounterAll, false));
    }

    private bool ReturnsFromSiteBeforeMovement(WorldTargetReference destination, CampaignTimeTrigger highest) =>
        _sites.Any(site => FollowsSite(destination, site) && site.SecondsRemaining < 30 * 60 &&
            !(highest >= CampaignTimeTrigger.OneHour && !site.Detected) &&
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
