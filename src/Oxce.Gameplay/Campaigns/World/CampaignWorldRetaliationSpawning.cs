using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private List<CampaignState.BaseState> RetaliationCandidates(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        return WorldRetaliation.Candidates(campaign.BaseStates, region, rule.Objective,
            rule.SkipScoutingPhase, rule.MultiUfoRetaliation,
            static owner => new(owner.Longitude, owner.Latitude), static owner => owner.RetaliationTarget);
    }

    private string? RetaliationWaveReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, ref long work)
    {
        if (rule.Objective is not (RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation)) return null;
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        // Classification is also used by score/spawn forecasts and the executor.
        work += 8L * campaign.BaseStates.Count * (region.Areas.Count + 1L);
        if (work > MaximumAlienBaseChecks) return "Retaliation target selection exceeds the supported work limit.";
        if (RetaliationCandidates(mission, rule).Count != 0)
            return "Retaliation assault spawning requires world forecasts.";
        if (rule.Objective == RuntimeMissionObjective.InstantRetaliation)
            return "Instant retaliation termination requires interruption forecasts.";
        var wave = rule.Waves[mission.NextWave];
        return wave.Ufo is null && wave.Deployment is { } deployment &&
            rules.AlienDeployments[deployment].Value.MarkerName.Length != 0
            ? "Retaliation direct-site waves require deployment forecasts." : null;
    }
}
