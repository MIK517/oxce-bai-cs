using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private HashSet<int>? _suppressedUfoSites;
    private List<CustomCounterForecast>? _siteConversionCounterForecast;

    private string? UfoSiteConversionsReason(CampaignTimeTrigger highest)
    {
        Dictionary<string, int>? reservations = null;
        long work = 0;
        foreach (var saved in _ufos)
        {
            if (!ArrivesAtSite(saved, highest)) continue;
            var ufo = UfoAfterTimedTransitions(saved, highest);
            if (UfoSiteReason(ufo, ForecastMission(ufo.MissionId, highest), highest,
                    reservations ??= new(StringComparer.Ordinal), ref work) is { } reason) return reason;
        }
        return null;
    }

    private bool ArrivesAtSite(UfoSnapshot ufo, CampaignTimeTrigger highest)
    {
        ufo = UfoAfterTimedTransitions(ufo, highest);
        if (ufo.Status != UfoStatus.Flying || ufo.Destination is not { Kind: WorldTargetKind.Waypoint } destination ||
            WorldGeometry.Distance(ufo.Position, destination.Position) > WorldGeometry.RadianSpeed(ufo.Speed)) return false;
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
        foreach (var owner in campaign.BaseStates)
            foreach (var craft in owner.Crafts)
            {
                if (++work > MaximumAlienBaseChecks) return "UFO site conversion exceeds the supported work limit.";
                if (craft.Logistics?.Destination is { Kind: WorldTargetKind.Ufo } target &&
                    (target.UniqueId > 0 ? target.UniqueId == ufo.UniqueId : target.Id == ufo.Id))
                    return "UFO site conversion requires craft follower transfer.";
            }
        var wave = rule.Waves[MissionWaveIndex(ufo, mission, rule)] with { Deployment = null };
        var area = UfoSiteArea(ufo, mission, ufo.Destination!.Position);
        work += SiteDeploymentSelectionWork(wave, rule, area);
        if (work > MaximumAlienBaseChecks) return "UFO site conversion exceeds the supported work limit.";
        var candidates = SiteDeploymentCandidates(wave, rule, area);
        if (candidates.Count == 0) (_suppressedUfoSites ??= []).Add(ufo.UniqueId);
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
            if (string.IsNullOrWhiteSpace(deployment.MarkerName) || IsUfoIdNamespace(deployment.MarkerName))
                return "UFO mission site marker namespace is unsupported.";
            markers.Add(deployment.MarkerName);
        }
        foreach (var marker in markers)
        {
            var count = reservations.GetValueOrDefault(marker) + 1;
            reservations[marker] = count;
            work += _siteConversionCounterForecast?.Count ?? 0;
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
            if (_siteBirthIdChanges?.Any(change => change.Name == marker) == true &&
                _siteConversionCounterForecast?.Any(change => change.Change.Name == marker && !change.Change.Increase) == true)
                return "UFO site conversion shares decreased marker counters with same-tick direct sites.";
            var (first, maximum) = campaign.CustomCounters.BeforeWorldCreation(marker, highest, _siteConversionCounterForecast);
            var end = maximum + count;
            if (first <= 0 || end > int.MaxValue ||
                _sites.Any(site => MarkerName(site.DeploymentId) == marker && site.Id >= first && site.Id < end) ||
                _alienBases.Any(alienBase => MarkerName(alienBase.DeploymentId) == marker && alienBase.Id >= first && alienBase.Id < end))
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
            effects.Notify(new MissionSiteDetected(id, deployment.MarkerName));
            if (rule.RespawnUfoAfterSiteDespawn) return arrived with { Status = UfoStatus.IgnoreMe };
        }
        return arrived with { Status = UfoStatus.Destroyed };
    }
}
