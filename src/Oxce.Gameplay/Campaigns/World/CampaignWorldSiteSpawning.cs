using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private bool IsDirectSiteWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var wave = rule.Waves[mission.NextWave];
        return rule.Objective is not (RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation) &&
            wave.Ufo is null && !SuppressedSupplyWave(mission, rule, wave) &&
            (rule.Objective == RuntimeMissionObjective.Site && wave.Objective || wave.Deployment is { } deployment &&
             campaign.Content.RuntimeRules.AlienDeployments[deployment].Value.MarkerName.Length != 0);
    }

    private IReadOnlyList<MissionArea> DirectSiteAreas(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var wave = rule.Waves[mission.NextWave];
        var rules = campaign.Content.RuntimeRules;
        var trajectory = rules.UfoTrajectories[wave.Trajectory!.Value].Value;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        var zone = rule.SpawnZone == -1 ? trajectory.Zone(0) : rule.SpawnZone;
        var areas = WorldGeometry.MissionAreas(region, zone);
        if (mission.MissionSiteZoneArea == -1) return areas;
        if ((uint)mission.MissionSiteZoneArea >= (uint)areas.Count)
            throw new InvalidDataException("Direct mission site area is outside its spawn zone.");
        return [areas[mission.MissionSiteZoneArea]];
    }

    private string? DirectSiteSpawnReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        Dictionary<string, int> markerReservations, ref long work, ref int population)
    {
        var wave = rule.Waves[mission.NextWave];
        if (wave.Trajectory is null || !CanAdvanceWaveCounters(mission, rule, wave))
            return "Direct mission site wave counters or trajectory are invalid.";
        if (wave.ObjectiveOnXcomBase) return "Direct mission site player-base targeting is unavailable.";
        var areas = DirectSiteAreas(mission, rule);
        work += areas.Count;
        if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
        var markers = new HashSet<string>(StringComparer.Ordinal);
        var rules = campaign.Content.RuntimeRules;
        foreach (var area in areas)
        {
            var candidates = SiteDeploymentCandidates(wave, rule, area);
            work += candidates.Count;
            if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
            if (candidates.Count == 0) continue;
            if (!TrySpawnArea(area, out _)) return "Direct mission site coordinates require an unsupported latitude domain.";
            foreach (var handle in candidates)
            {
                var deployment = rules.AlienDeployments[handle].Value;
                if (deployment.DurationMinimum < 1 || deployment.DurationMaximum < deployment.DurationMinimum ||
                    deployment.DurationMaximum > int.MaxValue / 3600)
                    return "Direct mission site duration is outside the supported range.";
                markers.Add(deployment.MarkerName);
            }
        }
        if (markers.Count == 0) return null;
        if ((long)_sites.Count + ++population > 100_000) return "World mission site population exceeds the supported limit.";
        foreach (var marker in markers)
        {
            work += _sites.Count + (long)_alienBases.Count;
            if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
            if (marker is "STR_UFO_UNIQUE" or "STR_UFO" or "STR_LANDING_SITE" or "STR_CRASH_SITE")
                return "Mission site and UFO creation share an unsupported ID namespace.";
            var count = markerReservations.GetValueOrDefault(marker) + 1;
            markerReservations[marker] = count;
            var first = campaign.PeekNextId(marker);
            var end = (long)first + count;
            if (first <= 0 || end > int.MaxValue ||
                _sites.Any(site => MarkerName(site.DeploymentId) == marker && site.Id >= first && site.Id < end) ||
                _alienBases.Any(alienBase => MarkerName(alienBase.DeploymentId) == marker && alienBase.Id >= first && alienBase.Id < end))
                return "Mission site marker IDs are exhausted or collide with a saved target.";
        }
        return null;
    }

    private void SpawnDirectSite(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var wave = rule.Waves[mission.NextWave];
        var areas = DirectSiteAreas(mission, rule);
        var area = areas[mission.MissionSiteZoneArea == -1 ? campaign.Random.NextInclusive(0, areas.Count - 1) : 0];
        var rules = campaign.Content.RuntimeRules;
        var selected = SelectSiteDeployment(wave, rule, area);
        if (selected is not { } handle) return;
        var deployment = rules.AlienDeployments[handle].Value;
        var longitude = WorldGeometry.RandomDouble(campaign.Random, area.LongitudeMinimum, area.LongitudeMaximum);
        var latitude = WorldGeometry.RandomDouble(campaign.Random, area.LatitudeMinimum, area.LatitudeMaximum);
        var position = WorldPosition.Create(longitude, latitude);
        var id = campaign.NextId(deployment.MarkerName);
        var seconds = checked(campaign.Random.NextInclusive(deployment.DurationMinimum, deployment.DurationMaximum) * 3600);
        _sites.Add(new MissionSiteSnapshot(id, mission.RuleId, rules.AlienDeployments.GetExternalId(handle),
            mission.Race, position.Longitude, position.Latitude, seconds)
        {
            Texture = area.Texture,
            City = area.Name,
        });
    }

    private List<RuleHandle<AlienDeploymentRuleFamily>> SiteDeploymentCandidates(
        RuntimeMissionWave wave, RuntimeAlienMissionRule rule, MissionArea area)
    {
        var rules = campaign.Content.RuntimeRules;
        if (wave.Deployment is { } explicitDeployment) return [explicitDeployment];
        if (rules.AlienDeployments.TryGet(rule.SiteTypeId, out var missionDeployment)) return [missionDeployment];
        if (!rules.Campaign.Globe.Textures.TryGetValue(area.Texture, out var texture))
            throw new InvalidDataException($"Mission site texture {area.Texture} does not exist.");
        var weights = texture.Deployments;
        if (weights.Count == 0) return [];
        if (weights.Count != 1) _ = WorldAlienMissionGeneration.Total(weights);
        var result = new List<RuleHandle<AlienDeploymentRuleFamily>>();
        foreach (var pair in weights)
            if ((weights.Count == 1 || pair.Value != 0) && pair.Key.Length != 0 && pair.Key != "\0")
                result.Add(rules.AlienDeployments.GetRequired(pair.Key));
        return result;
    }

    private RuleHandle<AlienDeploymentRuleFamily>? SelectSiteDeployment(
        RuntimeMissionWave wave, RuntimeAlienMissionRule rule, MissionArea area)
    {
        var rules = campaign.Content.RuntimeRules;
        if (wave.Deployment is { } explicitDeployment) return explicitDeployment;
        if (rules.AlienDeployments.TryGet(rule.SiteTypeId, out var missionDeployment)) return missionDeployment;
        var weights = rules.Campaign.Globe.Textures[area.Texture].Deployments;
        var id = weights.Count == 1 ? weights.First().Key : WorldAlienMissionGeneration.Choose(weights, campaign.Random);
        return id.Length == 0 || id == "\0" ? null : rules.AlienDeployments.GetRequired(id);
    }

    private string? DirectSiteActivityReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        ActivityRange[] regions, ActivityRange[] countries, ref long work)
    {
        var wave = rule.Waves[mission.NextWave];
        var positions = new List<SpawnArea>();
        var minimum = int.MaxValue;
        var maximum = int.MinValue;
        var suppressed = false;
        foreach (var area in DirectSiteAreas(mission, rule))
        {
            var candidates = SiteDeploymentCandidates(wave, rule, area);
            work += candidates.Count;
            if (work > MaximumAlienBaseChecks) return "Mission site activity forecasts exceed the supported work limit.";
            if (candidates.Count == 0) { suppressed = true; continue; }
            _ = TrySpawnArea(area, out var position);
            positions.Add(position);
            foreach (var candidate in candidates)
            {
                var points = campaign.Content.RuntimeRules.AlienDeployments[candidate].Value.Points;
                minimum = Math.Min(minimum, points);
                maximum = Math.Max(maximum, points);
            }
        }
        work += (long)positions.Count * (regions.Length + countries.Length) * 2;
        if (work > MaximumAlienBaseChecks) return "Mission site activity forecasts exceed the supported work limit.";
        if (positions.Count == 0) return null;
        if (!suppressed && minimum == maximum) return CheckPossibleActivity(positions, maximum, regions, countries);
        if (maximum > 0 && CheckPossibleActivity(positions, maximum, regions, countries, false) is { } reason) return reason;
        return minimum < 0 ? CheckPossibleActivity(positions, minimum, regions, countries, false) : null;
    }
}
