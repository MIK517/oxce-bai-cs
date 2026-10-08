using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld : ICampaignMissionResearchInterruption
{
    string? ICampaignMissionResearchInterruption.ValidationReason(ref long work)
    {
        // Each real grant checks links, then visits missions again to interrupt
        // its explicit topics. Ordered forecasts reserve both passes per grant.
        work += 2L * _missions.Count;
        if (work > 1_000_000) return "Mission research exceeds the supported discovery work limit.";
        var rules = campaign.Content.RuntimeRules;
        foreach (var mission in _missions)
        {
            var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (rule.InterruptResearchId.Length != 0 && rule.InterruptResearch is null)
                return "Alien mission interruption references undefined research.";
        }
        return null;
    }

    void ICampaignMissionResearchInterruption.Interrupt(IReadOnlyList<string> explicitTopics)
    {
        var topics = explicitTopics.ToHashSet(StringComparer.Ordinal);
        var rules = campaign.Content.RuntimeRules;
        for (var index = 0; index < _missions.Count; index++)
        {
            var mission = _missions[index];
            var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (topics.Contains(rule.InterruptResearchId)) _missions[index] = mission with { Interrupted = true };
        }
    }

    private AlienMissionSnapshot ForecastMission(int id, CampaignTimeTrigger highest)
    {
        var mission = FindMission(id) ?? throw new InvalidDataException("UFO forecast requires a mission link.");
        if (highest >= CampaignTimeTrigger.ThirtyMinutes && _pass.MissionCounterForecast?.TryGetValue(id, out var counters) == true) return counters;
        return highest >= CampaignTimeTrigger.OneDay ? MissionAfterDailyResearch(mission, _pass.DailyResearchForecast) : mission;
    }

    private string? PrepareMissionCounterForecasts(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes) return null;
        var rules = campaign.Content.RuntimeRules;
        var work = 0;
        try
        {
            foreach (var saved in _missions)
            {
                var mission = MissionAfterDailyResearch(saved, _pass.DailyResearchForecast);
                var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
                if (mission.Interrupted || mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra ||
                    mission.NextWave >= rule.Waves.Count || mission.SpawnCountdown > 30) continue;
                if (rule.Objective == RuntimeMissionObjective.Infiltration && rule.EndlessInfiltration &&
                    rule.Waves.All(wave => wave.SpawnTimer == 0)) return WaveWorkReason;
                AlienMissionSnapshot projected = mission;
                foreach (var step in DueWaveSteps(mission, rule))
                {
                    if (++work > MaximumImmediateWaveSteps) return WaveWorkReason;
                    var counter = (ulong)step.NextUfoCounter + 1;
                    var nextWave = step.NextWave;
                    if (counter >= rule.Waves[nextWave].UfoCount) { counter = 0; nextWave++; }
                    if (nextWave == rule.Waves.Count && rule.Objective == RuntimeMissionObjective.Infiltration && rule.EndlessInfiltration)
                        nextWave = 0;
                    projected = mission with
                    {
                        NextWave = nextWave,
                        NextUfoCounter = checked((int)counter),
                        Interrupted = InstantRetaliationWithoutTarget(step, rule),
                    };
                }
                (_pass.MissionCounterForecast ??= []).Add(saved.Id, projected);
            }
        }
        catch (InvalidDataException error) when (error.Message != WaveWorkReason) { return error.Message; }
        catch (OverflowException) { return "Mission counter forecasts exceed the supported range."; }
        return null;
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
            guaranteedResearch ??= GuaranteedDailyResearch();
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
