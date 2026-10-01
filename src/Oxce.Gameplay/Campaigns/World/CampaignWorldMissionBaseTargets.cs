using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const long MaximumMissionScriptChecks = 1_000_000;
    private const string MissionScriptWorkReason = "Mission script base targeting exceeds the supported work limit.";

    /// <summary>GeoscapeState::processCommand: uniform mission-list entry, circular retry, duplicate base regions.</summary>
    private (string Region, string Mission)? SelectBaseTargetMission(RuntimeMissionScriptRule script, int month)
    {
        var types = WorldScriptSelection.Available(script.Missions, month);
        var master = new List<string>(campaign.BaseStates.Count);
        foreach (var owner in campaign.BaseStates)
            master.Add(LocateGenerationRegion(new(owner.Longitude, owner.Latitude))!);
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
