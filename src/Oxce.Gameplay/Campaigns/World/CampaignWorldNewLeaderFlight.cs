using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private Dictionary<int, UfoSnapshot>? _newLeaderFlightForecast;

    private string? ForecastNewLeaderFlight(UfoSnapshot escort, CampaignTimeTrigger highest, ref long work)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes || escort.Status != UfoStatus.Flying ||
            !escort.HunterKiller || !escort.Escort || escort.Hunting || escort.Escorting ||
            escort.Destination is not { Kind: WorldTargetKind.Waypoint })
            return null;
        work += _missions.Count + 1L;
        if (work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
        if (FindMission(escort.MissionId) is not { } saved) return null;
        var mission = MissionAfterDailyResearch(saved, _dailyResearchForecast);
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
        if (rule.Objective is not (RuntimeMissionObjective.Score or RuntimeMissionObjective.Infiltration or
                RuntimeMissionObjective.Base or RuntimeMissionObjective.Site or RuntimeMissionObjective.Supply) ||
            IsOver(mission, rule) || mission.Interrupted || mission.NextWave >= rule.Waves.Count ||
            mission.SpawnCountdown > 30 || mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra)
            return null;
        WorldPosition? firstPossibleOrigin = null;
        foreach (var step in DueWaveSteps(mission, rule))
        {
            work += 3L * _alienBases.Count + 1;
            if (work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
            var wave = SpawnWave(step, rule);
            if (!CanAdvanceWaveCounters(step, rule, wave)) return null;
            if (CanAdvanceNoObjectWave(step, rule)) continue;
            if (wave.Ufo is not { } handle) continue;
            var percentage = rule.Objective == RuntimeMissionObjective.Supply && wave.Objective ? 0 :
                wave.HunterKillerPercentage == -1 ? rules.Ufos[handle].Value.HunterKillerPercentage : wave.HunterKillerPercentage;
            if (percentage >= 100) continue;
            if (wave.Trajectory is not { } trajectory) return null;
            var region = rules.Regions[rules.Regions.GetRequired(step.RegionId)].Value;
            foreach (var zone in region.MissionZones)
            {
                work += zone.Areas.Count + 1L;
                if (work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
            }
            work += _alienBases.Count;
            if (work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
            if (!TryOrdinarySpawnPositions(step, rule, rules.UfoTrajectories[trajectory].Value, region, out var origins) ||
                origins.Count != 1 || origins[0].Radius != 0) return null;
            // The first ordinary identity may vary while its movement target does not.
            // Require every possible earlier leader to share the same point, and a
            // guaranteed later ordinary birth before publishing any flight proof.
            if (firstPossibleOrigin is { } earlier && earlier != origins[0].Center) return null;
            firstPossibleOrigin = origins[0].Center;
            if (percentage > 0) continue;
            var ufoRule = rules.Ufos[rules.Ufos.GetRequired(escort.RuleId)].Value;
            var maximum = (long)ufoRule.Stats.SpeedMaximum + ufoRule.RaceBonus(mission.Race).SpeedMaximum;
            if (maximum is < 0 or > int.MaxValue) return "UFO hunting stats exceed the supported range.";
            // Flight-only stand-in: the real leader is appended after saved UFOs,
            // so its point origin is the exact target for this movement pass.
            // Never allocate a future actor identity or change detection inputs.
            (_newLeaderFlightForecast ??= []).Add(escort.UniqueId,
                WorldHunting.Escort(escort, WorldTargetReference.ForWaypoint(0, origins[0].Center), (int)maximum));
            return null;
        }
        return null;
    }
}
