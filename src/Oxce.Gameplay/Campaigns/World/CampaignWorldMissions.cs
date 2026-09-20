using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// AlienMission::think's half-hour countdown and waves without a UFO or site. Other wave
/// outcomes remain guarded until their creation and script consequences have handlers.
/// </summary>
internal sealed partial class CampaignWorld
{
    private void RegisterMissionOperations(CampaignCapabilityRegistry registry) =>
        registry.Timed(CampaignTimeTrigger.ThirtyMinutes, CampaignTimeOrder.ThirtyMinutesWorldMissions,
            "alien mission countdown", AdvanceMissionCountdowns);

    private string? MissionSchedulingReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes) return null;
        for (var index = 0; index < _missions.Count; index++)
        {
            var mission = _missions[index];
            var rule = campaign.Content.RuntimeRules.AlienMissions[
                campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (IsOver(mission, rule))
            {
                if (rule.Objective == RuntimeMissionObjective.Retaliation)
                    return "Retaliation mission cleanup requires base linkage.";
                continue;
            }
            if (mission.Interrupted || mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra ||
                mission.NextWave >= rule.Waves.Count)
                continue;
            if (mission.SpawnCountdown <= 30 && !CanAdvanceNoObjectWave(mission, rule))
                return "Alien mission wave spawning requires world simulation.";
        }
        return null;
    }

    private void AdvanceMissionCountdowns(CampaignState.TimeEffects _)
    {
        for (var index = 0; index < _missions.Count; index++)
        {
            var mission = _missions[index];
            var rule = campaign.Content.RuntimeRules.AlienMissions[
                campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (mission.Interrupted || mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra ||
                mission.NextWave >= rule.Waves.Count)
                continue;
            if (mission.SpawnCountdown > 30)
                _missions[index] = mission with { SpawnCountdown = mission.SpawnCountdown - 30 };
            else
            {
                if (!CanAdvanceNoObjectWave(mission, rule))
                    throw new InvalidOperationException("Alien mission wave changed after world preflight.");
                var wave = rule.Waves[mission.NextWave];
                var counter = (ulong)mission.NextUfoCounter + 1;
                var nextWave = mission.NextWave;
                if (counter >= wave.UfoCount)
                {
                    counter = 0;
                    nextWave++;
                }
                var countdown = nextWave < rule.Waves.Count
                    ? WorldTrajectory.SpawnCountdown(checked((int)rule.Waves[nextWave].SpawnTimer), campaign.Random)
                    : mission.SpawnCountdown;
                _missions[index] = mission with
                {
                    NextWave = nextWave,
                    NextUfoCounter = checked((int)counter),
                    SpawnCountdown = countdown,
                };
            }
        }
        for (var index = _missions.Count - 1; index >= 0; index--)
        {
            var mission = _missions[index];
            var rule = campaign.Content.RuntimeRules.AlienMissions[
                campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (IsOver(mission, rule))
                _missions.RemoveAt(index);
        }
    }

    private static bool IsOver(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule) =>
        mission.Interrupted && mission.LiveUfos == 0 ||
        !(rule.Objective == RuntimeMissionObjective.Infiltration && rule.EndlessInfiltration) &&
        mission.NextWave == rule.Waves.Count && mission.LiveUfos == 0;

    private static bool CanAdvanceNoObjectWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        if (rule.Objective != RuntimeMissionObjective.Score ||
            rule.OperationType != RuntimeMissionOperationType.Space)
            return false;
        var wave = rule.Waves[mission.NextWave];
        if (wave.UfoType.Length != 0 || wave.Ufo is not null || wave.Deployment is not null ||
            wave.Trajectory is null || wave.Objective || wave.ObjectiveOnTheLandingSite ||
            wave.ObjectiveOnXcomBase)
            return false;
        var counter = (ulong)mission.NextUfoCounter + 1;
        if (counter > int.MaxValue && counter < wave.UfoCount) return false;
        var nextWave = counter >= wave.UfoCount ? mission.NextWave + 1 : mission.NextWave;
        if (nextWave == rule.Waves.Count) return true;
        var timer = rule.Waves[nextWave].SpawnTimer;
        // AlienMission::think recurses when this next timer is zero. A recursive wave
        // needs a preflight of the entire chain before any state or RNG changes.
        if (timer == 0 || timer > int.MaxValue) return false;
        var steps = (long)(timer / 30);
        return (steps / 2 + steps) * 30 <= int.MaxValue;
    }
}
