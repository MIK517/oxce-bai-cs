using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? UfoSiteConversionsReason(CampaignTimeTrigger highest)
    {
        Dictionary<string, int>? reservations = null;
        long work = 0;
        var last = _ufos.Count - 1;
        var craftPassSkipped = false;
        for (var index = 0; index < _ufos.Count; index++)
        {
            var saved = _ufos[index];
            if (ArrivesAtSite(index, highest))
            {
                var ufo = UfoForFlight(saved, highest);
                if (UfoSiteReason(ufo, ForecastMission(ufo.MissionId, highest), highest,
                        reservations ??= new(StringComparer.Ordinal), ref work) is { } reason) return reason;
            }
            if (LandingObjectiveBaseReason(index, highest, ref reservations, ref work) is { } landingReason)
                return landingReason;
            if (!EndsUfoFlightPass(index, highest)) continue;
            last = index;
            craftPassSkipped = true;
            break;
        }
        // Suppressed conversions must be known before forecasting any craft arrivals.
        // A terminal arrival later in the list skips craft movement too, while earlier
        // conversions still transfer followers during the UFO pass.
        for (var index = 0; index <= last; index++)
        {
            var saved = _ufos[index];
            if (!ArrivesAtSite(index, highest) || _pass.SuppressedUfoSites?.Contains(saved.UniqueId) == true) continue;
            var ufo = UfoForFlight(saved, highest);
            var mission = ForecastMission(ufo.MissionId, highest);
            if (UfoSiteFollowersReason(ufo, MissionRule(mission), UfoSiteArea(ufo, mission, ufo.Destination!.Position),
                    highest, craftPassSkipped, ref work) is { } reason) return reason;
        }
        return null;
    }

    private bool ArrivesAtSite(UfoSnapshot ufo, CampaignTimeTrigger highest) =>
        ProjectedArrivesAtSite(ProjectFlightTarget(UfoForFlight(ufo, highest), highest), null, highest);

    /// <summary><see cref="ArrivesAtSite(UfoSnapshot, CampaignTimeTrigger)"/> for the saved UFO at
    /// <paramref name="index"/>, through the shared flight projection.</summary>
    private bool ArrivesAtSite(int index, CampaignTimeTrigger highest)
    {
        var ufo = SharedFlightAt(index, highest, out var distance);
        return ProjectedArrivesAtSite(ufo, distance, highest);
    }

    private bool ProjectedArrivesAtSite(UfoSnapshot ufo, double? knownDistance, CampaignTimeTrigger highest)
    {
        if (ufo.Hunting || ufo.Escorting || ufo.Status != UfoStatus.Flying || ufo.Destination is not { Kind: WorldTargetKind.Waypoint } ||
            (knownDistance ?? UfoDestinationDistance(ufo)) > WorldGeometry.RadianSpeed(ufo.Speed)) return false;
        var rules = campaign.Content.RuntimeRules;
        var trajectory = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
        return IsSiteConversion(ufo, ForecastMission(ufo.MissionId, highest), trajectory);
    }

    private bool IsSiteConversion(UfoSnapshot ufo, AlienMissionSnapshot mission, RuntimeUfoTrajectoryRule trajectory)
    {
        var rule = MissionRule(mission);
        var wave = MissionWaveIndex(ufo, mission, rule);
        return !mission.Interrupted && (uint)wave < (uint)rule.Waves.Count &&
            ufo.TrajectoryPoint + 1 < trajectory.Waypoints.Count && trajectory.Altitude(ufo.TrajectoryPoint + 1) == 0 &&
            mission.MissionSiteZoneArea != -1 && rule.Waves[wave].Objective &&
            trajectory.Zone(ufo.TrajectoryPoint) == rule.SpawnZone;
    }

    private MissionArea UfoSiteArea(UfoSnapshot ufo, AlienMissionSnapshot mission, WorldPosition landing)
    {
        var rules = campaign.Content.RuntimeRules;
        var rule = MissionRule(mission);
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        var area = SelectedMissionArea(region, rule.SpawnZone, mission.MissionSiteZoneArea);
        return rule.Waves[MissionWaveIndex(ufo, mission, rule)].ObjectiveOnTheLandingSite
            ? area with
            {
                LongitudeMinimum = landing.Longitude,
                LongitudeMaximum = landing.Longitude,
                LatitudeMinimum = landing.Latitude,
                LatitudeMaximum = landing.Latitude,
            } : area;
    }

    private string? UfoSiteReason(UfoSnapshot ufo, AlienMissionSnapshot mission, CampaignTimeTrigger highest,
        Dictionary<string, int> reservations, ref long work)
    {
        var rules = campaign.Content.RuntimeRules;
        var rule = MissionRule(mission);
        var wave = rule.Waves[MissionWaveIndex(ufo, mission, rule)] with { Deployment = null };
        var area = UfoSiteArea(ufo, mission, ufo.Destination!.Position);
        work += SiteDeploymentSelectionWork(wave, rule, area);
        if (work > MaximumAlienBaseChecks) return "UFO site conversion exceeds the supported work limit.";
        var candidates = SiteDeploymentCandidates(wave, rule, area);
        if (candidates.Count == 0 && (_pass.SuppressedUfoSites ??= []).Add(ufo.UniqueId))
            _pass.ResetEscortFlight(); // Suppression changes a dormant conversion into a terminal arrival.
        work += (long)candidates.Count * (1L + _sites.Count + _alienBases.Count);
        if (work > MaximumAlienBaseChecks) return "UFO site conversion exceeds the supported work limit.";
        if (candidates.Count != 0 && (long)_sites.Count + _ufos.Count > 100_000)
            return "World mission site population exceeds the supported limit.";
        if (candidates.Count != 0 && !TrySpawnArea(area, out _))
            return "UFO site conversion coordinates require an unsupported latitude domain.";
        var markers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in candidates)
        {
            var deployment = rules.AlienDeployments[handle].Value;
            if (deployment.DurationMinimum < 0 || deployment.DurationMaximum < deployment.DurationMinimum ||
                deployment.DurationMaximum > int.MaxValue / 3600)
                return "UFO mission site duration is outside the supported range.";
            if (string.IsNullOrEmpty(deployment.MarkerName) || IsUfoIdNamespace(deployment.MarkerName))
                return "UFO mission site marker namespace is unsupported.";
            markers.Add(deployment.MarkerName);
        }
        foreach (var marker in markers)
        {
            var count = reservations.GetValueOrDefault(marker) + 1;
            reservations[marker] = count;
            work += _pass.SiteConversionCounterForecast?.Count ?? 0;
            if (highest >= CampaignTimeTrigger.OneDay)
            {
                work += (long)rules.Research.Count * campaign.BaseStates.Count;
                foreach (var research in rules.Research.Rules)
                    work += (research.Value.IncreaseCounters.Count + (long)research.Value.DecreaseCounters.Count) * campaign.BaseStates.Count;
                foreach (var owner in campaign.BaseStates)
                    foreach (var project in owner.Research)
                    {
                        var research = rules.Research[project.Rule].Value;
                        work += 1L + research.GetOneFree.Count;
                        foreach (var group in research.GetOneFreeProtected) work += 1L + group.Topics.Count;
                    }
            }
            if (work > MaximumAlienBaseChecks) return "Mission site marker forecasts exceed the supported work limit.";
            if (_pass.SiteBirthIdChanges?.Any(change => change.Name == marker) == true &&
                _pass.SiteConversionCounterForecast?.Any(change => change.Change.Name == marker && !change.Change.Increase) == true)
                return "UFO site conversion shares decreased marker counters with same-tick direct sites.";
            if (ProductionCounterWorkReason(highest, ref work, "Mission site marker forecasts exceed the supported work limit.") is { } productionWork)
                return productionWork;
            if (_pass.SiteConversionCounterForecast?.Any(change => change.Change.Name == marker && !change.Change.Increase) == true)
            {
                if (ProductionCounterWorkReason(highest, ref work, "Mission site marker forecasts exceed the supported work limit.") is { } decreaseWork)
                    return decreaseWork;
                if (campaign.ProductionPreflight.CraftCounter(marker, highest, (1, 1)).Maximum > 1)
                    return "UFO site conversion shares decreased marker counters with same-tick craft production.";
            }
            var (first, maximum) = campaign.CustomCounters.BeforeWorldCreation(marker, highest, _pass.SiteConversionCounterForecast, includeProduction: true);
            var end = maximum + count;
            if (first <= 0 || end > int.MaxValue ||
                _sites.Any(site => MarkerName(site.DeploymentId) == marker && site.Id >= first && site.Id < end) ||
                _alienBases.Any(alienBase => MarkerName(alienBase.DeploymentId) == marker && alienBase.Id >= first && alienBase.Id < end) ||
                CraftIdCollision(marker, first, end))
                return "Mission site marker IDs are exhausted or collide with a saved target.";
        }
        return null;
    }

    private UfoSnapshot ConvertUfoToSite(UfoSnapshot arrived, AlienMissionSnapshot mission, CampaignState.TimeEffects effects)
    {
        var rules = campaign.Content.RuntimeRules;
        var rule = MissionRule(mission);
        if (rule.Objective != RuntimeMissionObjective.Infiltration) ScoreUfoActivity(arrived.Position, rule.Points, rules);
        var wave = rule.Waves[MissionWaveIndex(arrived, mission, rule)] with { Deployment = null };
        var area = UfoSiteArea(arrived, mission, arrived.Position);
        var selected = SelectSiteDeployment(wave, rule, area);
        if (selected is { } handle)
        {
            var deployment = rules.AlienDeployments[handle].Value;
            var position = WorldPosition.Create(
                WorldGeometry.RandomDouble(campaign.Random, area.LongitudeMinimum, area.LongitudeMaximum),
                WorldGeometry.RandomDouble(campaign.Random, area.LatitudeMinimum, area.LatitudeMaximum));
            var id = campaign.NextId(deployment.MarkerName);
            var seconds = checked(campaign.Random.NextInclusive(deployment.DurationMinimum, deployment.DurationMaximum) * 3600);
            var custom = rules.Ufos[rules.Ufos.GetRequired(arrived.RuleId)].Value.StatsForRace(mission.Race).MissionCustomDeployment;
            _sites.Add(new MissionSiteSnapshot(id, mission.RuleId, rules.AlienDeployments.GetExternalId(handle),
                mission.Race, position.Longitude, position.Latitude, seconds)
            {
                Texture = area.Texture,
                City = area.Name,
                Detected = true,
                CustomDeploymentId = rules.AlienDeployments.TryGet(custom, out _) ? custom : string.Empty,
                UfoUniqueId = rule.RespawnUfoAfterSiteDespawn ? arrived.UniqueId : 0,
            });
            TransferUfoSiteFollowers(arrived, _sites[^1], rule.RespawnUfoAfterSiteDespawn);
            effects.Notify(new MissionSiteDetected(id, deployment.MarkerName));
            if (rule.RespawnUfoAfterSiteDespawn) return arrived with { Status = UfoStatus.IgnoreMe };
        }
        return arrived with { Status = UfoStatus.Destroyed };
    }
}
