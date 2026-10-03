using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private bool RequiresOperationBaseCreation(RuntimeAlienMissionRule rule, string selectedRegion) =>
        rule.OperationType == RuntimeMissionOperationType.RegionNewBase ||
        (rule.OperationType is RuntimeMissionOperationType.RegionNewBaseIfNecessary or
            RuntimeMissionOperationType.EarthNewBaseIfNecessary or RuntimeMissionOperationType.EarthNewBase) &&
        !MissionOperationBases(rule, selectedRegion).Any();

    private string? ReserveOperationBaseCreation(
        IReadOnlyCollection<(RuleHandle<AlienMissionRuleFamily> Mission, string Region)> choices,
        Dictionary<string, int> markerReservations, ActivityRange[] regionScores, ActivityRange[] countryScores,
        ref int population, ref long work)
    {
        if (ulong.MaxValue - _alienBaseBirthSequence < (ulong)(population - _alienBases.Count + 1))
            return "Mission operation base birth sequence is exhausted.";
        if (++population > 100_000) return "Mission operation base population exceeds the supported limit.";
        var rules = campaign.Content.RuntimeRules;
        var globe = rules.Campaign.Globe;
        long geometryWork = 1;
        foreach (var polygon in globe.Polygons) geometryWork += polygon.Points.Count;
        long scoreWork = regionScores.Length + (long)countryScores.Length;
        foreach (var state in campaign.RegionStates) scoreWork += rules.Regions[state.Rule].Value.Areas.Count;
        foreach (var state in campaign.CountryStates) scoreWork += rules.Countries[state.Rule].Value.Areas.Count;
        var markers = new HashSet<string>(StringComparer.Ordinal);
        var minimumRegions = new int[regionScores.Length];
        var maximumRegions = new int[regionScores.Length];
        var minimumCountries = new int[countryScores.Length];
        var maximumCountries = new int[countryScores.Length];
        foreach (var choice in choices.OrderBy(choice => rules.AlienMissions[choice.Mission].Id, StringComparer.Ordinal)
                     .ThenBy(choice => choice.Region, StringComparer.Ordinal))
        {
            var rule = rules.AlienMissions[choice.Mission].Value;
            if (rule.OperationBaseType is not { } deploymentHandle)
                return "Mission operation references an unavailable alien base deployment.";
            var deployment = rules.AlienDeployments[deploymentHandle].Value;
            if (deployment.HuntMissionMaxFrequency < 0)
                return "Mission operation base hunting timer cannot be negative.";
            var marker = deployment.MarkerName;
            if (string.IsNullOrWhiteSpace(marker) || marker == "ALIEN_MISSIONS" || IsUfoIdNamespace(marker) ||
                marker is WorldTargetReference.WaypointType or WorldTargetReference.BaseType or "STR_ALIEN_TERROR")
                return "Mission operation base uses an unsupported marker namespace.";
            markers.Add(marker);
            var region = rules.Regions[rules.Regions.GetRequired(choice.Region)].Value;
            if (region.MissionRegion is { } remap) region = rules.Regions[remap].Value;
            var areas = WorldGeometry.MissionAreas(region, rule.OperationSpawnZone);
            work += 100L * (2 * geometryWork + region.Areas.Count + 1) + areas.Count +
                areas.Count * scoreWork;
            if (work > MaximumMissionScriptChecks) return "Mission operation base creation exceeds the supported work limit.";
            foreach (var area in areas)
            {
                if (!TrySpawnArea(area, out var position))
                    return "Mission operation base coordinates require an unsupported latitude domain.";
                var score = rule.Objective == RuntimeMissionObjective.Infiltration ? 0 : rule.Points;
                // Each command creates at most one base. Reserve its worst possible
                // award per owner; no conditional or failed search supplies headroom.
                RecordPossibleScore(position, score, minimumRegions, maximumRegions, minimumCountries, maximumCountries);
            }
        }
        foreach (var marker in markers)
        {
            var count = markerReservations.GetValueOrDefault(marker) + 1;
            markerReservations[marker] = count;
            var first = campaign.PeekNextId(marker);
            work += _alienBases.Count + (long)_sites.Count;
            foreach (var owner in campaign.BaseStates) work += 1L + owner.Crafts.Count + owner.Transfers.Count;
            if (work > MaximumMissionScriptChecks) return "Mission operation base creation exceeds the supported work limit.";
            if (first <= 0 || (long)first + count > int.MaxValue ||
                _alienBases.Any(existing => MarkerName(existing.DeploymentId) == marker &&
                    existing.Id >= first && existing.Id < (long)first + count) ||
                _sites.Any(existing => MarkerName(existing.DeploymentId) == marker &&
                    existing.Id >= first && existing.Id < (long)first + count) ||
                campaign.BaseStates.Any(owner => owner.Crafts.Any(craft =>
                    rules.Crafts.GetExternalId(craft.Rule) == marker &&
                    craft.Id >= first && craft.Id < (long)first + count) ||
                    owner.Transfers.Any(transfer => !transfer.Delivered && transfer.Craft is { } incoming &&
                        incoming.RuleId == marker && incoming.Id >= first && incoming.Id < (long)first + count)))
                return "Mission operation base IDs are exhausted or collide.";
        }
        for (var index = 0; index < regionScores.Length; index++)
            if (!regionScores[index].Add(minimumRegions[index], certain: false) ||
                !regionScores[index].Add(maximumRegions[index], certain: false))
                return "Mission operation base activity exceeds the supported range.";
        for (var index = 0; index < countryScores.Length; index++)
            if (!countryScores[index].Add(minimumCountries[index], certain: false) ||
                !countryScores[index].Add(maximumCountries[index], certain: false))
                return "Mission operation base activity exceeds the supported range.";
        return null;

        void RecordPossibleScore(SpawnArea position, int score, int[] minimumRegions, int[] maximumRegions,
            int[] minimumCountries, int[] maximumCountries)
        {
            for (var index = 0; index < regionScores.Length; index++)
                if (position.Radius != 0 ||
                    WorldGeometry.InsideRegion(rules.Regions[campaign.RegionStates[index].Rule].Value, position.Center))
                {
                    minimumRegions[index] = Math.Min(minimumRegions[index], score);
                    maximumRegions[index] = Math.Max(maximumRegions[index], score);
                    if (position.Radius == 0) break;
                }
            for (var index = 0; index < countryScores.Length; index++)
                if (position.Radius != 0 ||
                    WorldGeometry.InsideCountry(rules.Countries[campaign.CountryStates[index].Rule].Value, position.Center))
                {
                    minimumCountries[index] = Math.Min(minimumCountries[index], score);
                    maximumCountries[index] = Math.Max(maximumCountries[index], score);
                    if (position.Radius == 0) break;
                }
        }
    }

    private AlienMissionSnapshot CreateMissionOperationBase(
        AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, RuntimeRegionRule region)
    {
        var rules = campaign.Content.RuntimeRules;
        var deployment = rules.AlienDeployments[rule.OperationBaseType!.Value].Value;
        var raw = WorldOperationBaseSpawn.Select(region, rules.Campaign.Globe, rule.OperationSpawnZone,
            deployment.FakeUnderwaterSpawnChance, rules.Campaign.AllowAlienBasesOnWrongTextures, campaign.Random);
        if (raw is not { } point) return mission with { Interrupted = true };
        var alienBase = SpawnMissionAlienBase(mission, rule, rule.OperationBaseType!.Value, point);
        var position = alienBase.Position;
        return mission with
        {
            AlienBase = new(WorldTargetKind.AlienBase, deployment.MarkerName, alienBase.Id,
                position.Longitude, position.Latitude),
        };
    }
}
