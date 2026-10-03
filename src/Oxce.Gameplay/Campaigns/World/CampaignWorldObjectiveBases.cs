using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private int _objectiveBaseBirths;
    private List<(IReadOnlyList<SpawnArea> Positions, int Points)>? _objectiveBaseScores;

    private static bool CompletesBaseObjective(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, RuntimeMissionWave wave) =>
        rule.Objective == RuntimeMissionObjective.Base && !wave.ObjectiveOnTheLandingSite &&
        mission.NextWave + 1 == rule.Waves.Count && (ulong)mission.NextUfoCounter + 1 >= wave.UfoCount;

    private string? ObjectiveBaseSpawnReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        CampaignTimeTrigger highest, Dictionary<string, int> markers, ref long work)
    {
        try
        {
            var rules = campaign.Content.RuntimeRules;
            var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
            var areas = WorldGeometry.MissionAreas(region, rule.SpawnZone);
            long geometry = 1;
            foreach (var polygon in rules.Campaign.Globe.Polygons) geometry += polygon.Points.Count;
            work += 100L * (2 * geometry + region.Areas.Count + 1);
            return ReserveObjectiveBase(rule, areas, highest, markers, ref work);
        }
        catch (InvalidDataException error) { return error.Message; }
    }

    private string? ReserveObjectiveBase(RuntimeAlienMissionRule rule, IReadOnlyList<MissionArea> areas,
        CampaignTimeTrigger highest, Dictionary<string, int> reservations, ref long work)
    {
        if (++_objectiveBaseBirths > 100_000 - _alienBases.Count)
            return "Base objective population exceeds the supported limit.";
        if (ulong.MaxValue - _alienBaseBirthSequence < (ulong)_objectiveBaseBirths)
            return "Base objective birth sequence is exhausted.";
        var rules = campaign.Content.RuntimeRules;
        var markers = new HashSet<string>(StringComparer.Ordinal);
        var positions = new List<SpawnArea>();
        foreach (var area in areas)
        {
            work += 100L * (1 + (rule.SiteTypeId.Length != 0 ? 1 :
                rules.Campaign.Globe.Textures.TryGetValue(area.Texture, out var texture) ? 3L * texture.Deployments.Count : 1));
            if (work > MaximumAlienBaseChecks) return "Base objective creation exceeds the supported work limit.";
            if (!TrySpawnArea(area, out var position))
                return "Base objective coordinates require an unsupported latitude domain.";
            positions.Add(position);
            foreach (var candidate in ObjectiveBaseDeployments(rule, area))
            {
                var deployment = rules.AlienDeployments[candidate].Value;
                var marker = deployment.MarkerName;
                if (marker.Length == 0 || marker == "ALIEN_MISSIONS" || IsUfoIdNamespace(marker) ||
                    marker is WorldTargetReference.WaypointType or WorldTargetReference.BaseType or "STR_ALIEN_TERROR")
                    return "Base objective uses an unsupported marker namespace.";
                markers.Add(marker);
            }
        }
        foreach (var marker in markers)
        {
            var count = reservations.GetValueOrDefault(marker) + 1;
            reservations[marker] = count;
            work += _alienBases.Count + (long)_sites.Count;
            foreach (var owner in campaign.BaseStates) work += owner.Crafts.Count + (long)owner.Transfers.Count;
            // Counter forecasts include daily research; charge its scans too.
            if (highest >= CampaignTimeTrigger.OneDay)
            {
                foreach (var research in rules.Research.Rules)
                    work += (1L + research.Value.IncreaseCounters.Count + research.Value.DecreaseCounters.Count) * campaign.BaseStates.Count;
                foreach (var owner in campaign.BaseStates)
                    foreach (var project in owner.Research)
                    {
                        var research = rules.Research[project.Rule].Value;
                        work += 1L + research.GetOneFree.Count;
                        foreach (var group in research.GetOneFreeProtected) work += 1L + group.Topics.Count;
                    }
            }
            if (work > MaximumAlienBaseChecks) return "Base objective creation exceeds the supported work limit.";
            var range = campaign.CustomCounters.BeforeWorldCreation(marker, highest);
            var end = range.Maximum + count;
            if (range.Minimum <= 0 || end > int.MaxValue ||
                _alienBases.Any(existing => MarkerName(existing.DeploymentId) == marker && existing.Id >= range.Minimum && existing.Id < end) ||
                _sites.Any(existing => MarkerName(existing.DeploymentId) == marker && existing.Id >= range.Minimum && existing.Id < end) ||
                campaign.BaseStates.Any(owner => owner.Crafts.Any(craft => rules.Crafts.GetExternalId(craft.Rule) == marker &&
                    craft.Id >= range.Minimum && craft.Id < end) ||
                    owner.Transfers.Any(transfer => !transfer.Delivered && transfer.Craft is { } craft &&
                        craft.RuleId == marker && craft.Id >= range.Minimum && craft.Id < end)))
                return "Base objective IDs are exhausted or collide.";
            (_siteBirthIdChanges ??= []).Add(new(marker, true));
        }
        long scoreGeometry = campaign.RegionStates.Count + (long)campaign.CountryStates.Count;
        foreach (var state in campaign.RegionStates) scoreGeometry += rules.Regions[state.Rule].Value.Areas.Count;
        foreach (var state in campaign.CountryStates) scoreGeometry += rules.Countries[state.Rule].Value.Areas.Count;
        work += areas.Count * scoreGeometry;
        if (work > MaximumAlienBaseChecks) return "Base objective creation exceeds the supported work limit.";
        (_objectiveBaseScores ??= []).Add((positions, rule.Points));
        return null;
    }

    private RuleHandle<AlienDeploymentRuleFamily>[] ObjectiveBaseDeployments(RuntimeAlienMissionRule rule, MissionArea area)
    {
        var rules = campaign.Content.RuntimeRules;
        try
        {
            if (rule.SiteTypeId.Length != 0) return [rules.AlienDeployments.GetRequired(rule.SiteTypeId)];
            if (rules.Campaign.Globe.Textures.TryGetValue(area.Texture, out var texture) && texture.Deployments.Count != 0)
            {
                if (texture.Deployments.Count == 1) return [rules.AlienDeployments.GetRequired(texture.Deployments.First().Key)];
                if (WorldAlienMissionGeneration.Total(texture.Deployments) == 0)
                    throw new InvalidDataException("Base objective texture weights select an unavailable deployment.");
                return texture.Deployments.Where(pair => pair.Value != 0)
                    .Select(pair => rules.AlienDeployments.GetRequired(pair.Key)).ToArray();
            }
            return [rules.AlienDeployments.GetRequired("STR_ALIEN_BASE_ASSAULT")];
        }
        catch (KeyNotFoundException error) { throw new InvalidDataException("Base objective references an unavailable deployment.", error); }
    }

    private void CompleteBaseObjective(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        if (WorldObjectiveBaseSpawn.Select(rules, rule, region, campaign.Random) is { } selected)
            SpawnMissionAlienBase(mission, rule, selected.Deployment, selected.Position);
    }

    private AlienBaseSnapshot SpawnMissionAlienBase(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        RuleHandle<AlienDeploymentRuleFamily> handle, WorldPosition raw)
    {
        var rules = campaign.Content.RuntimeRules;
        var deployment = rules.AlienDeployments[handle].Value;
        var position = WorldPosition.Create(raw.Longitude, raw.Latitude);
        var alienBase = new AlienBaseSnapshot(campaign.NextId(deployment.MarkerName),
            rules.AlienDeployments.GetExternalId(handle), mission.Race, position.Longitude, position.Latitude)
        {
            PreservationKey = "alien-base:generated:" + checked(++_alienBaseBirthSequence)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            StartMonth = campaign.MonthsPassed,
            MinutesSinceLastHuntMissionGeneration = deployment.HuntMissionMaxFrequency,
        };
        _alienBases.Add(alienBase);
        if (rule.Objective != RuntimeMissionObjective.Infiltration) ScoreUfoActivity(position, rule.Points, rules);
        return alienBase;
    }
}
