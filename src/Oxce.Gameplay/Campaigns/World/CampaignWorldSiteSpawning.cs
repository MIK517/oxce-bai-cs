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
        // Explicit deployments own this first path. Terrain fallback is a separate capability.
        if (wave.Deployment is not { } handle) return "Direct mission site requires terrain deployment selection.";
        var deployment = campaign.Content.RuntimeRules.AlienDeployments[handle].Value;
        if (deployment.DurationMinimum < 1 || deployment.DurationMaximum < deployment.DurationMinimum ||
            deployment.DurationMaximum > int.MaxValue / 3600)
            return "Direct mission site duration is outside the supported range.";
        var areas = DirectSiteAreas(mission, rule);
        work += areas.Count + _sites.Count + (long)_alienBases.Count;
        if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
        foreach (var area in areas)
            if (!TrySpawnArea(area, out _)) return "Direct mission site coordinates require an unsupported latitude domain.";
        if ((long)_sites.Count + ++population > 100_000) return "World mission site population exceeds the supported limit.";
        var marker = deployment.MarkerName;
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
        return null;
    }

    private void SpawnDirectSite(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var wave = rule.Waves[mission.NextWave];
        var areas = DirectSiteAreas(mission, rule);
        var area = areas[mission.MissionSiteZoneArea == -1 ? campaign.Random.NextInclusive(0, areas.Count - 1) : 0];
        var handle = wave.Deployment!.Value;
        var rules = campaign.Content.RuntimeRules;
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
}
