namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string SiteResearchGenerationReason = "Mission site research requires alien-base generation phase forecasts.";
    private const string SiteResearchWaveReason = "Mission site research requires mission-wave composition forecasts.";

    private string? PrepareSiteResearch(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes) return null;
        var rules = campaign.Content.RuntimeRules;
        var found = false;
        foreach (var site in _sites)
        {
            if (!SiteExpiresThisTick(site, highest)) continue;
            var deployment = rules.AlienDeployments[rules.AlienDeployments.GetRequired(site.DeploymentId)].Value;
            if (!rules.Research.TryGet(deployment.UnlockedResearchOnDespawnId, out _)) continue;
            found = true;
            // An unconditional unavailable payload cannot be made safe by any
            // research outcome. Keep that boundary ahead of composition guards.
            foreach (var pair in deployment.DespawnEvents)
                if (pair.Value != 0 && rules.Events.TryGet(pair.Key, out var handle) &&
                    rules.Events[handle].Value.InterruptResearch.Length == 0) return SiteDespawnBoundary;
        }
        if (!found) return null;
        // These boundaries name the still-unprojected caller phases. Research-only
        // worlds can execute without pretending that future actors already changed.
        if (SiteResearchBaseGenerationReason(highest) is { } baseReason) return baseReason;
        foreach (var owner in campaign.BaseStates)
        {
            if (highest >= CampaignTimeTrigger.OneDay && owner.Research.Count != 0)
                return "Mission site research requires daily project phase forecasts.";
        }
        foreach (var saved in _missions)
        {
            var mission = MissionAfterDailyResearch(saved, _pass.DailyResearchForecast);
            var rule = MissionRule(mission);
            if (!mission.Interrupted && !IsOver(mission, rule) && mission.NextWave < rule.Waves.Count && mission.SpawnCountdown <= 30)
                return SiteResearchWaveReason;
        }
        // Half-hour refuelling and higher-trigger arrivals/production precede sites.
        // Their item-dependent automatic discoveries need a separate stock projection.
        if (rules.Research.Rules.Any(entry => entry.Value.Cost == 0 && entry.Value.NeedItem))
        {
            if (highest >= CampaignTimeTrigger.OneHour || campaign.BaseStates.Any(owner => owner.Crafts.Any(craft =>
                    craft.Logistics is { Status: "STR_REFUELLING" })))
                return "Mission site research requires timed item eligibility forecasts.";
        }
        var forecast = campaign.MissionResearch.CreateForecast(highest);
        var afterSites = new Dictionary<(string Deployment, int Id), IReadOnlySet<string>>();
        foreach (var site in _sites)
        {
            if (!SiteExpiresThisTick(site, highest)) continue;
            var deployment = rules.AlienDeployments[rules.AlienDeployments.GetRequired(site.DeploymentId)].Value;
            if (forecast.Advance(deployment.UnlockedResearchOnDespawnId) is { } reason) return reason;
            afterSites.Add((site.DeploymentId, site.Id), forecast.GuaranteedResearch);
            if (SiteDespawnReason(deployment, highest, forecast.GuaranteedResearch) is { } eventReason) return eventReason;
        }
        _pass.SiteResearchAfterSite = afterSites;
        _pass.SiteResearchForecast = forecast;
        return null;
    }

    private string? SiteResearchFlightReason(CampaignTimeTrigger highest)
    {
        if (_pass.SiteResearchForecast is not { } forecast) return null;
        if ((long)_ufos.Count * (_missions.Count + (highest >= CampaignTimeTrigger.OneDay ? (long)_alienBases.Count : 0)) > MaximumAlienBaseChecks)
            return "Mission site research UFO link checks exceed the supported work limit.";
        foreach (var ufo in _ufos)
        {
            // Earlier daily interruption/deletion is permanent for this tick even
            // if a later site disables or reenables its research prerequisite.
            var mission = ForecastMission(ufo.MissionId, highest);
            var rule = MissionRule(mission);
            if (!mission.Interrupted && forecast.PossibleExplicitTopics.Contains(rule.InterruptResearchId) &&
                !forecast.GuaranteedExplicitTopics.Contains(rule.InterruptResearchId))
            {
                // Interruption is consulted at an ordinary waypoint arrival, not
                // during transit, landed countdown or takeoff. A safe transit can
                // commit its selected outcome before a later tick forecasts arrival.
                var projected = ProjectFlightTarget(UfoForFlight(ufo, highest), highest);
                if (!projected.Hunting && !projected.Escorting && projected.Status == UfoStatus.Flying && projected.Destination is not null &&
                    UfoDestinationDistance(projected) <= WorldGeometry.RadianSpeed(projected.Speed))
                    return "UFO mission interruption depends on uncertain site research outcomes.";
            }
        }
        return null;
    }

    private IReadOnlySet<string>? SiteResearchAfter(MissionSiteSnapshot site) =>
        _pass.SiteResearchAfterSite?.GetValueOrDefault((site.DeploymentId, site.Id));

    private string? SiteResearchBaseGenerationReason(CampaignTimeTrigger highest)
    {
        long work = _alienBases.Count;
        foreach (var alienBase in _alienBases)
        {
            // Daily cleanup has already run when site research executes. A grant
            // here cannot destroy today's base or cancel its earlier generation.
            if (highest >= CampaignTimeTrigger.OneDay && BaseDestroyedAfterDailyResearch(alienBase, _pass.DailyResearchForecast)) continue;
            var rule = AlienBaseRule(alienBase);
            work += rule.GenMission.Count + (long)rule.HuntMissions.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            if (highest >= CampaignTimeTrigger.OneDay && rule.GenMissionFrequency > 0 && alienBase.GenMissionCount < rule.GenMissionLimit &&
                rule.GenMission.Any(choice => choice.Key.Length != 0 && choice.Value != 0)) return SiteResearchGenerationReason;
            if (rule.BaseDetectionRange <= 0 || rule.BaseDetectionChance <= 0 ||
                (long)alienBase.MinutesSinceLastHuntMissionGeneration + 10 < rule.HuntMissionMaxFrequency) continue;
            IReadOnlyDictionary<string, ulong> choices;
            try { choices = WorldAlienBaseHunting.Weights(rule.HuntMissions, unchecked((ulong)campaign.MonthsPassed)); }
            catch (InvalidDataException error) { return error.Message; }
            work += choices.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            if (choices.Any(choice => choice.Key.Length != 0 && choice.Value != 0)) return SiteResearchGenerationReason;
        }
        return null;
    }
}
