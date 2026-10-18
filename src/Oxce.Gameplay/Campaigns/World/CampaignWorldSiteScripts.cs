using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string SiteScriptRegionBoundary = "Site mission script region listing and selection timelines disagree.";

    private (string Region, string Mission, int Area)? SelectSiteTargetMission(RuntimeMissionScriptRule script, int month)
    {
        var first = WorldScriptSelection.Generate(script.Missions, unchecked((ulong)month), campaign.Random)?.Id ?? "";
        var types = WorldScriptSelection.Available(script.Missions, month)
            .Where(choice => choice.Weight != 0).Select(choice => choice.Rule.Id).ToArray();
        var targetBase = Percent(script.TargetBaseOdds);
        var candidates = WorldMissionSiteSelection.SelectMission(first, types,
            type => SiteScriptCandidates(script, month, type, targetBase), out var mission);
        if (candidates is null) return null;
        var weights = script.Regions.Count == 0 ? null :
            WorldScriptSelection.ForMonth(script.Regions, unchecked((ulong)month), false)
                .Select(choice => new KeyValuePair<string, ulong>(choice.Rule.Id, choice.Weight));
        var region = WorldMissionSiteSelection.SelectRegion(candidates,
            script.Regions.Count == 0 ? SiteScriptRegionIds(script, month) : [], weights, campaign.Random);
        var area = WorldMissionSiteSelection.SelectArea(candidates, region, campaign.Random);
        _strategy.AddMissionLocation(script.VariableName, region, area, script.AvoidRepeats);
        return (region, mission, area);
    }

    private IReadOnlyList<WorldSiteScriptCandidate> SiteScriptCandidates(
        RuntimeMissionScriptRule script, int month, string missionId, bool targetBase)
    {
        var rules = campaign.Content.RuntimeRules;
        var mission = rules.AlienMissions.GetRequiredValue(missionId);
        var regions = new List<WorldSiteScriptRegion>();
        foreach (var regionId in SiteScriptRegionIds(script, month))
        {
            var region = rules.Regions.GetRequiredValue(regionId);
            if (targetBase && !campaign.BaseStates.Any(owner =>
                    LocateGenerationRegion(new(owner.Longitude, owner.Latitude)) == regionId)) continue;
            regions.Add(new(regionId, region.MissionZones,
                _missions.Any(existing => existing.RuleId == missionId && existing.RegionId == regionId)));
        }
        var wave = mission.Waves[0];
        var direct = wave.Ufo is null && wave.Deployment is { } deployment &&
            rules.AlienDeployments[deployment].Value.MarkerName.Length != 0;
        return WorldMissionSiteSelection.Candidates(regions, mission.SpawnZone, direct,
            mission.SiteTypeId.Length != 0, _strategy, script.VariableName);
    }

    private string[] SiteScriptRegionIds(RuntimeMissionScriptRule script, int month) =>
        script.Regions.Count == 0 ? campaign.Content.RuntimeRules.Regions.Rules.Select(entry => entry.Id).ToArray() :
            WorldScriptSelection.Available(script.Regions, month).Where(choice => choice.Weight != 0)
                .Select(choice => choice.Rule.Id).ToArray();

    private string? SiteScriptReason(RuntimeMissionScriptRule script, int month, int batchSize,
        ref long work, out IReadOnlyList<string> regionIds)
    {
        regionIds = SiteScriptRegionIds(script, month);
        if (script.Missions.Count == 0) return "Site mission script has no valid mission distribution.";
        if (script.Regions.Count != 0 && Choices(script.Regions, month, choice => choice.Handle is not null) is { } regionReason)
            return regionReason;
        var selectedRegions = script.Regions.Count == 0 ? regionIds.ToArray() :
            WorldScriptSelection.ForMonth(script.Regions, unchecked((ulong)month), false)
                .Where(choice => choice.Weight != 0).Select(choice => choice.Rule.Id).ToArray();
        if (selectedRegions.Length == 0) return SiteScriptRegionBoundary;
        work += regionIds.Count + (long)selectedRegions.Length;
        if (work > MaximumMissionScriptChecks) return MissionScriptWorkReason;
        // Every listed (candidate) region must have a positive weight in the
        // generation timeline; generation-only regions are rejected by the native
        // retry loop and drop out of conditioning. A listed region without
        // generation weight could leave only unreachable candidates after earlier
        // commands change occupancy, which natively never terminates.
        var selectedRegionSet = selectedRegions.ToHashSet(StringComparer.Ordinal);
        if (!selectedRegionSet.IsSupersetOf(regionIds)) return SiteScriptRegionBoundary;
        var rules = campaign.Content.RuntimeRules;
        foreach (var regionId in regionIds.Concat(selectedRegions).Distinct(StringComparer.Ordinal))
            if (!rules.Regions.TryGet(regionId, out var regionHandle) ||
                rules.Regions[regionHandle].Value is { MissionRegionId.Length: > 0, MissionRegion: null })
                return "Site mission script references an unavailable region.";
        var types = WorldScriptSelection.Available(script.Missions, month).Where(choice => choice.Weight != 0).ToArray();
        if (types.Length == 0 || Choices(script.Missions, month, choice => choice.Handle is not null) is { })
            return "Site mission script has no valid mission distribution.";
        var generatedTypes = WorldScriptSelection.ForMonth(script.Missions, unchecked((ulong)month), false);
        work += types.Length + (long)generatedTypes.Count;
        if (work > MaximumMissionScriptChecks) return MissionScriptWorkReason;
        var listedTypes = types.Select(type => type.Rule.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var type in generatedTypes)
            if (type.Weight != 0 && !listedTypes.Contains(type.Rule.Id))
                return "Site script generated mission is outside its candidate list.";
        long baseRegionWork = 0;
        if (script.TargetBaseOdds > 0)
        {
            foreach (var state in campaign.RegionStates)
                baseRegionWork += 1L + rules.Regions[state.Rule].Value.Areas.Count;
            work += campaign.BaseStates.Count * baseRegionWork;
            if (work > MaximumMissionScriptChecks) return MissionScriptWorkReason;
            foreach (var owner in campaign.BaseStates)
                if (LocateGenerationRegion(new(owner.Longitude, owner.Latitude)) is null)
                    return "Site mission script cannot locate a player base region.";
        }
        var recentCount = _strategy.MissionLocations.GetValueOrDefault(script.VariableName)?.Count ?? 0;
        foreach (var type in types)
        {
            if (type.Rule.Handle is not { } handle) return MissionScriptBoundary;
            var mission = rules.AlienMissions[handle].Value;
            if (mission.Waves.Count == 0) return MissionScriptBoundary;
            if (mission.SpawnZone < 0) return "Cannot spawn mission site, invalid spawn zone.";
            foreach (var regionId in regionIds)
            {
                var region = rules.Regions.GetRequiredValue(regionId);
                // Bound preflight plus runtime scans and every possible repeat lookup.
                work += 3L * (1L + _missions.Count + batchSize + campaign.BaseStates.Count * baseRegionWork);
                if (mission.SpawnZone < region.MissionZones.Count)
                    work += 3L * region.MissionZones[mission.SpawnZone].Areas.Count * (1L + recentCount + batchSize);
                if (work > MaximumMissionScriptChecks) return MissionScriptWorkReason;
            }
        }
        return null;
    }
}
