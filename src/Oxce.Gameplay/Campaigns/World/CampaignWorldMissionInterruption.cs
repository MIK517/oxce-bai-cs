using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
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
            // Interruption changes waypoint arrival and pursuit in this same tick.
            // Keep those combined live-UFO transitions guarded until forecast ownership expands.
            if (mission.LiveUfos != 0)
                return "Alien mission interruption with live UFOs requires world simulation.";
        }
        return null;
    }

    private AlienMissionSnapshot MissionAfterDailyResearch(AlienMissionSnapshot mission,
        IReadOnlySet<string>? guaranteedResearch)
    {
        if (guaranteedResearch is null || mission.Interrupted) return mission;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
        return rule.InterruptResearchId.Length != 0 && guaranteedResearch.Contains(rule.InterruptResearchId)
            ? mission with { Interrupted = true } : mission;
    }
}
