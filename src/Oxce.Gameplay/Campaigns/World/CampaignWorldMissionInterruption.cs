using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    // Pure daily projection, shared by world and subsequent craft preflight.
    // It is reset on each preflight and never appears in campaign persistence.
    private IReadOnlySet<string>? _dailyResearchForecast;

    private AlienMissionSnapshot ForecastMission(int id, CampaignTimeTrigger highest)
    {
        var mission = FindMission(id) ?? throw new InvalidDataException("UFO forecast requires a mission link.");
        return highest >= CampaignTimeTrigger.OneDay ? MissionAfterDailyResearch(mission, _dailyResearchForecast) : mission;
    }

    private void InterruptMissionsDaily(CampaignState.TimeEffects _)
    {
        var rules = campaign.Content.RuntimeRules;
        for (var index = 0; index < _missions.Count; index++)
        {
            var mission = _missions[index];
            var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (!mission.Interrupted && rule.InterruptResearchId.Length != 0 &&
                campaign.HasCompletedResearch(rule.InterruptResearchId))
                _missions[index] = mission with { Interrupted = true };
        }
    }

    private string? MissionInterruptionReason(CampaignTimeTrigger highest,
        out IReadOnlySet<string>? guaranteedResearch)
    {
        guaranteedResearch = null;
        if (highest < CampaignTimeTrigger.OneDay) return null;
        IReadOnlySet<string>? possibleResearch = null;
        var rules = campaign.Content.RuntimeRules;
        foreach (var mission in _missions)
        {
            var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
            var id = rule.InterruptResearchId;
            if (id.Length == 0) continue;
            if (rule.InterruptResearch is null)
                return "Alien mission interruption references undefined research.";
            if (mission.Interrupted) continue;
            guaranteedResearch ??= campaign.ResearchPreflight.GuaranteedResearchAfterDailyProgress();
            possibleResearch ??= campaign.ResearchPreflight.PossibleResearchAfterDailyProgress();
            if (!possibleResearch.Contains(id)) continue;
            if (!guaranteedResearch.Contains(id))
                return "Alien mission interruption depends on uncertain daily research outcomes.";

        }
        return null;
    }

    private AlienMissionSnapshot MissionAfterDailyResearch(AlienMissionSnapshot mission,
        IReadOnlySet<string>? guaranteedResearch)
    {
        if (guaranteedResearch is null || mission.Interrupted) return mission;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
        var projected = rule.InterruptResearchId.Length != 0 && guaranteedResearch.Contains(rule.InterruptResearchId)
            ? mission with { Interrupted = true } : mission;
        if (projected.AlienBase is null) return projected;
        foreach (var alienBase in _alienBases)
            if (BaseDestroyedAfterDailyResearch(alienBase, guaranteedResearch) && FollowsAlienBase(projected.AlienBase, alienBase))
                projected = projected with
                {
                    AlienBase = null,
                    Interrupted = projected.Interrupted ||
                    rule.OperationType != RuntimeMissionOperationType.Space
                };
        return projected;
    }
}
