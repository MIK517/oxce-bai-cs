using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? ForecastNewLeaderFlight(UfoSnapshot escort, CampaignTimeTrigger highest, ref long work)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes || escort.Status != UfoStatus.Flying ||
            !escort.HunterKiller || !escort.Escort || escort.Hunting || escort.Escorting ||
            escort.Destination is not { Kind: WorldTargetKind.Waypoint })
            return null;
        work += _missions.Count + 1L;
        if (work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
        if (FindMission(escort.MissionId) is not { } saved) return null;
        var mission = MissionAfterDailyResearch(saved, _pass.DailyResearchForecast);
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
        if (rule.Objective is not (RuntimeMissionObjective.Score or RuntimeMissionObjective.Infiltration or
                RuntimeMissionObjective.Base or RuntimeMissionObjective.Site or RuntimeMissionObjective.Supply) ||
            IsOver(mission, rule) || mission.Interrupted || mission.NextWave >= rule.Waves.Count ||
            mission.SpawnCountdown > 30 || mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra)
            return null;
        // A daily random reward can omit every birth even when a wave's
        // hunter percentage guarantees an ordinary constructor if it executes.
        if (_pass.PossibleDailyMissionInterruptions?.Contains(mission.Id) == true ||
            rule.Objective == RuntimeMissionObjective.Supply &&
            _pass.PossibleDailySupplySourceLoss?.Contains(mission.Id) == true) return null;
        WorldPosition? firstPossibleOrigin = null;
        var exactOrigin = true;
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
                origins.Count == 0) return null;
            // Every earlier possible leader contributes to the target domain. A
            // guaranteed ordinary birth establishes attachment; only identical
            // point origins establish an exact movement target.
            if (origins.Count != 1 || origins[0].Radius != 0 ||
                firstPossibleOrigin is { } earlier && earlier != origins[0].Center) exactOrigin = false;
            firstPossibleOrigin ??= origins[0].Center;
            if (percentage > 0) continue;
            if (!exactOrigin)
            {
                if (RegionalNewLeaderDependencies(escort, ref work, out var dependentIds) is { } workReason)
                    return workReason;
                var inexact = _pass.InexactNewLeaderFlights ??= [];
                inexact.Add(escort.UniqueId);
                foreach (var id in dependentIds) inexact.Add(id);
            }
            var ufoRule = rules.Ufos[rules.Ufos.GetRequired(escort.RuleId)].Value;
            var maximum = (long)ufoRule.Stats.SpeedMaximum + ufoRule.RaceBonus(mission.Race).SpeedMaximum;
            if (maximum is < 0 or > int.MaxValue) return "UFO hunting stats exceed the supported range.";
            // Flight-only stand-in: the real leader is appended after saved UFOs,
            // so a point origin is exact. A regional proof supplies status/speed only;
            // its unchanged position must never serve as an exact flight target.
            // Never allocate a future actor identity or change detection inputs.
            (_pass.NewLeaderFlightForecast ??= []).Add(escort.UniqueId,
                WorldHunting.Escort(escort, WorldTargetReference.ForWaypoint(0, exactOrigin ? firstPossibleOrigin!.Value : escort.Position), (int)maximum));
            return null;
        }
        return null;
    }

    private string? RegionalNewLeaderDependencies(UfoSnapshot source, ref long work, out IReadOnlyList<int> dependentIds)
    {
        List<int> ids = [];
        dependentIds = ids;
        foreach (var saved in _ufos)
        {
            if (++work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
            if (saved.UniqueId == source.UniqueId) continue;
            var projected = UfoForFlight(saved, _pass.NewbornHighestTrigger);
            if (projected.Destination is not { Kind: WorldTargetKind.Ufo } target) continue;
            if (NewLeaderPursuitDependency(target, source, ref work, out var depends) is { } reason) return reason;
            if (!depends) continue;
            // Flying escorts never execute ordinary waypoint arrival effects.
            // Keep their position inexact through every ordered/transitive follower;
            // grounded or other target states need a distinct owning proof.
            if (projected.Status != UfoStatus.Flying || !projected.Escorting || projected.Hunting ||
                projected.OriginalWaypoint is not { IsNormalized: true })
                return "Regional newborn leader followers require flying escort forecasts.";
            ids.Add(saved.UniqueId);
        }
        // Craft pursuit consumes the whole movement envelope, not an invented
        // regional coordinate. Near arrivals remain guarded before any birth.
        if (NewLeaderCraftPursuitReason(source, ref work) is { } pursuitReason) return pursuitReason;
        return null;
    }
}
