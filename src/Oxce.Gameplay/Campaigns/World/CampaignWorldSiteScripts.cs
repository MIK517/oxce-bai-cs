using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string SiteScriptRegionBoundary = "Site mission scripts require bounded multi-region selection.";

    private (string Region, string Mission, int Area)? SelectSiteTargetMission(RuntimeMissionScriptRule script, int month)
    {
        var first = WorldScriptSelection.Generate(script.Missions, unchecked((ulong)month), campaign.Random)!.Id;
        var types = WorldScriptSelection.Available(script.Missions, month)
            .Where(choice => choice.Weight != 0).Select(choice => choice.Rule.Id).ToArray();
        var targetBase = Percent(script.TargetBaseOdds);
        var candidates = WorldMissionSiteSelection.SelectMission(first, types,
            type => SiteScriptCandidates(script, month, type, targetBase), out var mission);
        if (candidates is null) return null;
        var region = WorldScriptSelection.Generate(script.Regions, unchecked((ulong)month), campaign.Random)!.Id;
        var area = WorldMissionSiteSelection.SelectArea(candidates, region, campaign.Random);
        _strategy.AddMissionLocation(script.VariableName, region, area, script.AvoidRepeats);
        return (region, mission, area);
    }

    private IReadOnlyList<WorldSiteScriptCandidate> SiteScriptCandidates(
        RuntimeMissionScriptRule script, int month, string missionId, bool targetBase)
    {
        var rules = campaign.Content.RuntimeRules;
        var mission = rules.AlienMissions[rules.AlienMissions.GetRequired(missionId)].Value;
        var regions = new List<WorldSiteScriptRegion>();
        foreach (var choice in WorldScriptSelection.Available(script.Regions, month))
        {
            if (choice.Weight == 0) continue;
            var region = rules.Regions[rules.Regions.GetRequired(choice.Rule.Id)].Value;
            if (targetBase && !campaign.BaseStates.Any(owner =>
                    LocateGenerationRegion(new(owner.Longitude, owner.Latitude)) == choice.Rule.Id)) continue;
            regions.Add(new(choice.Rule.Id, region.MissionZones,
                _missions.Any(existing => existing.RuleId == missionId && existing.RegionId == choice.Rule.Id)));
        }
        var wave = mission.Waves[0];
        var direct = wave.Ufo is null && wave.Deployment is { } deployment &&
            rules.AlienDeployments[deployment].Value.MarkerName.Length != 0;
        return WorldMissionSiteSelection.Candidates(regions, mission.SpawnZone, direct,
            mission.SiteTypeId.Length != 0, _strategy, script.VariableName);
    }

    private string? SiteScriptReason(RuntimeMissionScriptRule script, int month, ref long work, out string regionId)
    {
        regionId = "";
        if (script.Regions.Count == 0 || script.Missions.Count == 0) return SiteScriptRegionBoundary;
        if (Choices(script.Regions, month, choice => choice.Handle is not null) is { } regionReason) return regionReason;
        var selectedRegions = WorldScriptSelection.ForMonth(script.Regions, unchecked((ulong)month), false)
            .Where(choice => choice.Weight != 0).ToArray();
        var listedRegions = WorldScriptSelection.Available(script.Regions, month).Where(choice => choice.Weight != 0).ToArray();
        if (selectedRegions.Length != 1 || listedRegions.Length != 1 ||
            selectedRegions[0].Rule.Id != listedRegions[0].Rule.Id) return SiteScriptRegionBoundary;
        var rules = campaign.Content.RuntimeRules;
        regionId = selectedRegions[0].Rule.Id;
        if (!rules.Regions.TryGet(regionId, out var regionHandle) ||
            rules.Regions[regionHandle].Value is { MissionRegionId.Length: > 0, MissionRegion: null })
            return "Site mission script references an unavailable region.";
        var region = rules.Regions[regionHandle].Value;
        var types = WorldScriptSelection.Available(script.Missions, month).Where(choice => choice.Weight != 0).ToArray();
        if (types.Length == 0 || Choices(script.Missions, month, choice => choice.Handle is not null) is { })
            return "Site mission script has no valid mission distribution.";
        foreach (var type in WorldScriptSelection.ForMonth(script.Missions, unchecked((ulong)month), false))
            if (type.Weight != 0 && !types.Any(listed => listed.Rule.Id == type.Rule.Id))
                return "Site script generated mission is outside its candidate list.";
        foreach (var type in types)
        {
            if (type.Rule.Handle is not { } handle) return MissionScriptBoundary;
            var mission = rules.AlienMissions[handle].Value;
            if (mission.SpawnZone < 0) return "Cannot spawn mission site, invalid spawn zone.";
            work += 1L + _missions.Count + campaign.BaseStates.Count * (1L + campaign.RegionStates.Count);
            if (mission.SpawnZone < region.MissionZones.Count) work += region.MissionZones[mission.SpawnZone].Areas.Count;
            if (work > MaximumMissionScriptChecks) return MissionScriptWorkReason;
        }
        return null;
    }
}
