using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    // A bound on the forecast's own work and on the worst case it admits. Rosigma's monthly scripts need about
    // 1,180,000 checks for site and base targeting and over 10,000,000 for the possible operation-base searches on its
    // 3,253-point globe (each charged as 100 tries scanning every polygon) (branch 6 S3).
    private static long MaximumMissionScriptChecks => FixtureWorkBounds ? 1_000_000 : 100_000_000;
    private const string MissionScriptWorkReason = "Mission script base targeting exceeds the supported work limit.";

    /// <summary>GeoscapeState::processCommand: uniform mission-list entry, circular retry, duplicate base regions.</summary>
    private (string Region, string Mission)? SelectBaseTargetMission(RuntimeMissionScriptRule script, int month)
    {
        var types = script.Missions.Count == 0 ? [] : WorldScriptSelection.Available(script.Missions, month);
        var master = new List<string>(campaign.BaseStates.Count);
        foreach (var owner in campaign.BaseStates)
            master.Add(LocateGenerationRegion(new(owner.Longitude, owner.Latitude))!);
        if (types.Count == 0)
        {
            master.RemoveAll(region => !_strategy.ValidMissionRegion(region));
            if (master.Count == 0) return null;
            var region = master[campaign.Random.NextInclusive(0, master.Count - 1)];
            var mission = _strategy.ChooseRandomMission(region, campaign.Random);
            return mission.Length == 0 ? null : (region, mission);
        }
        var entry = campaign.Random.NextInclusive(0, types.Count - 1);
        for (var attempt = 0; attempt < types.Count; attempt++)
        {
            var type = types[entry].Rule.Id;
            var regions = new List<string>(master);
            foreach (var mission in _missions)
                if (mission.RuleId == type) regions.RemoveAll(region => region == mission.RegionId);
            if (regions.Count != 0)
                return (regions[campaign.Random.NextInclusive(0, regions.Count - 1)], type);
            entry = (entry + 1) % types.Count;
        }
        return null;
    }
}
