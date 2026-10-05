using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private readonly HashSet<(int Mission, int Wave, int Counter, string Ufo, int Mode)> _newbornNoTargetProofs = [];
    private readonly HashSet<(int Mission, int Wave, int Counter, string Ufo, int Mode)> _newbornSafeHuntingProofs = [];

    private bool NewbornHuntingIsForecast(AlienMissionSnapshot mission, RuntimeMissionWave wave) =>
        _newbornSafeHuntingProofs.Contains((mission.Id, mission.NextWave, mission.NextUfoCounter, wave.UfoType, NewbornHuntMode(wave))) ||
        _newbornNoTargetProofs.Contains((mission.Id, mission.NextWave, mission.NextUfoCounter, wave.UfoType, NewbornHuntMode(wave))) ||
        NoCraftCanBecomeHunterTarget();

    private bool NewbornCanRevealHunt(AlienMissionSnapshot mission, RuntimeMissionWave wave) =>
        _newbornSafeHuntingProofs.Contains((mission.Id, mission.NextWave, mission.NextUfoCounter, wave.UfoType, NewbornHuntMode(wave)));

    private int NewbornHuntMode(RuntimeMissionWave wave) => wave.HuntMode == -1
        ? campaign.Content.RuntimeRules.Ufos[wave.Ufo!.Value].Value.HuntMode : wave.HuntMode;

    private string? NewbornHuntingReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule missionRule,
        CampaignTimeTrigger highest)
    {
        var wave = SpawnWave(mission, missionRule);
        if (wave.Ufo is not { } handle || wave.Trajectory is not { } trajectory ||
            missionRule.Objective == RuntimeMissionObjective.InstantRetaliation ||
            missionRule.Objective == RuntimeMissionObjective.Supply && wave.Objective) return null;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Ufos[handle].Value;
        var path = rules.UfoTrajectories[trajectory].Value;
        if (path.Waypoints.Count == 0 || path.Altitude(0) == 0 ||
            (wave.HunterKillerPercentage == -1 ? rule.HunterKillerPercentage : wave.HunterKillerPercentage) <= 0 ||
            NoCraftCanBecomeHunterTarget()) return null;
        var mode = NewbornHuntMode(wave);
        var key = (mission.Id, mission.NextWave, mission.NextUfoCounter, wave.UfoType, mode);
        if (_newbornNoTargetProofs.Contains(key)) return null;
        if (_huntingCraftForecast is null && ForecastHuntingCrafts(highest, ref _huntingForecastWork) is { } reason)
            return reason;
        var origin = new WorldPosition(0, 0);
        var possibleHunt = false;
        var initializedEscort = false;
        var radar = (long)rule.Stats.RadarRange + rule.RaceBonus(mission.Race).RadarRange;
        if (radar > 0)
        {
            if (ForecastNewbornCraftCandidates(includeIgnored: false, ref _huntingForecastWork, out var candidate) is { } candidateReason)
                return candidateReason;
            if (candidate)
            {
                if (radar is < 0 or > int.MaxValue) return null;
                var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
                if (!TryOrdinarySpawnPositions(mission, missionRule, path, region, out var positions) || positions.Count == 0)
                    return null;
                if (wave.Escort)
                {
                    _huntingForecastWork += _ufos.Count;
                    if (_huntingForecastWork > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                    if (WorldHunting.EscortLeader(mission.Id, _ufos) is { } leader)
                    {
                        positions = [new(leader.Position, 0)];
                        initializedEscort = true;
                    }
                    else
                    {
                        _huntingForecastWork += _missions.Count;
                        if (_huntingForecastWork > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                        var saved = FindMission(mission.Id);
                        // Earlier immediate waves may append an ordinary leader at
                        // a different origin. That source needs a separate forecast.
                        if (saved is null || saved.NextWave != mission.NextWave || saved.NextUfoCounter != mission.NextUfoCounter)
                            return null;
                    }
                }
                if (NewbornRadarSeparationReason(positions, (int)radar, ref _huntingForecastWork, out var separated) is { } separationReason)
                    return separationReason;
                if (!separated)
                {
                    if (NewbornHuntReachReason(positions, rule, mission.Race, path, (int)radar,
                            initializedEscort, ref _huntingForecastWork) is { } reachReason)
                        return reachReason;
                    possibleHunt = true;
                }
                origin = positions[0].Center;
            }
        }
        // Native candidate attraction is evaluated before the radar test. A
        // nonpositive range proves no target, but cannot hide malformed arithmetic.
        // Constructor modes above one randomly select either zero or one; prove
        // both outcomes without drawing RNG or allocating a campaign identity.
        for (var candidateMode = mode > 1 ? 0 : mode; candidateMode <= (mode > 1 ? 1 : mode); candidateMode++)
        {
            var hypothetical = new UfoSnapshot(0, wave.UfoType, mission.Id, wave.TrajectoryId,
                0, origin.Longitude, origin.Latitude, UfoStatus.Flying, WorldAltitudes.High)
            {
                HunterKiller = true,
                HuntMode = candidateMode,
                Destination = new(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0, 1, 0.6),
            };
            if (ProjectHunting(hypothetical, highest, due: true, forecastFuel: true,
                ref _huntingForecastWork, out _, out _) is { } candidateReason) return candidateReason;
        }
        if (possibleHunt) _newbornSafeHuntingProofs.Add(key);
        else _newbornNoTargetProofs.Add(key);
        return null;
    }

    private string? NewbornHuntReachReason(IReadOnlyList<SpawnArea> positions, RuntimeUfoRule rule,
        string race, RuntimeUfoTrajectoryRule path, int radar, bool initializedEscort, ref long work)
    {
        var maximum = (long)rule.Stats.SpeedMaximum + rule.RaceBonus(race).SpeedMaximum;
        if (maximum is < 0 or > int.MaxValue) return "UFO hunting stats exceed the supported range.";
        var hypothetical = new UfoSnapshot(0, string.Empty, 0, string.Empty, 0, 0, 0,
            UfoStatus.Flying, WorldAltitudes.High)
        {
            Speed = initializedEscort ? (int)maximum : path.Speed(0, (int)maximum),
            Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6))
        };
        if (!WorldHunting.TryHunt(hypothetical, WorldTargetReference.ForWaypoint(0, new(1, 0.6)),
                (int)maximum, rule.HuntSpeed, out var hunted)) return "UFO hunt speed exceeds the supported range.";
        var reach = WorldGeometry.RadianSpeed(hunted.Speed);
        var rules = campaign.Content.RuntimeRules;
        // Selection may depend on random constructor modes and spawn positions.
        // Prove safety for every eligible target without forecasting RNG or IDs.
        foreach (var owner in campaign.BaseStates)
        {
            if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
            foreach (var craft in _huntingCraftSources![owner.Id])
            {
                if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                var state = _huntingCraftForecast![(rules.Crafts.GetExternalId(craft.Rule), craft.Id)];
                var craftRule = rules.Crafts[craft.Rule].Value;
                if (state.Status != "STR_OUT" || craftRule.Undetectable || WorldHunting.IsIgnored(state.LowFuel, state.MissionComplete) ||
                    state.Damage >= CraftLogistics.EffectiveDamageMaximum(state, craftRule, rules)) continue;
                foreach (var position in positions)
                {
                    if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                    var nearest = WorldGeometry.Distance(position.Center, new(state.Longitude, state.Latitude)) - position.Radius;
                    if (nearest <= WorldGeometry.Nautical(radar) + 1e-12 && nearest <= reach + 1e-12)
                        return HuntCombatReason;
                }
            }
        }
        return null;
    }

    private string? NewbornRadarSeparationReason(IReadOnlyList<SpawnArea> positions, int radar, ref long work,
        out bool separated)
    {
        separated = false;
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
        {
            if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
            foreach (var craft in _huntingCraftSources![owner.Id])
            {
                if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                var state = _huntingCraftForecast![(rules.Crafts.GetExternalId(craft.Rule), craft.Id)];
                var rule = rules.Crafts[craft.Rule].Value;
                if (state.Status != "STR_OUT" || rule.Undetectable || WorldHunting.IsIgnored(state.LowFuel, state.MissionComplete) ||
                    state.Damage >= CraftLogistics.EffectiveDamageMaximum(state, rule, rules)) continue;
                foreach (var position in positions)
                {
                    if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                    if (WorldGeometry.Distance(position.Center, new(state.Longitude, state.Latitude)) - position.Radius <=
                        WorldGeometry.Nautical(radar) + 1e-12) return null;
                }
            }
        }
        separated = true;
        return null;
    }

    private string? ForecastNewbornCraftCandidates(bool includeIgnored, ref long work, out bool candidate)
    {
        candidate = false;
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
        {
            if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
            foreach (var craft in _huntingCraftSources![owner.Id])
            {
                if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                var state = _huntingCraftForecast!.GetValueOrDefault((rules.Crafts.GetExternalId(craft.Rule), craft.Id), craft.Logistics!);
                if (state.Status != "STR_OUT") continue;
                var rule = rules.Crafts[craft.Rule].Value;
                if (includeIgnored || !rule.Undetectable && !WorldHunting.IsIgnored(state.LowFuel, state.MissionComplete) &&
                    state.Damage < CraftLogistics.EffectiveDamageMaximum(state, rule, rules))
                {
                    candidate = true;
                    return null;
                }
            }
        }
        return null;
    }
}
