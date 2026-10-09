using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private readonly record struct NewbornHuntingProof(int Mission, string Rule, string Region, string Race,
        WorldTargetReference? Source, int Area, int Wave, int Counter, string Ufo, int Mode);

    private NewbornHuntingProof HuntingProof(AlienMissionSnapshot mission, RuntimeMissionWave wave, bool generated = false) =>
        new(generated ? 0 : mission.Id, mission.RuleId, mission.RegionId, mission.Race, mission.AlienBase,
            wave.Objective ? mission.MissionSiteZoneArea : -1, mission.NextWave, mission.NextUfoCounter,
            wave.UfoType, NewbornHuntMode(wave));

    private bool HasHuntingProof(HashSet<NewbornHuntingProof> proofs, AlienMissionSnapshot mission, RuntimeMissionWave wave) =>
        proofs.Contains(HuntingProof(mission, wave)) || mission.AlienBase is not null &&
        proofs.Contains(HuntingProof(mission, wave, generated: true));

    private bool NewbornHuntingIsForecast(AlienMissionSnapshot mission, RuntimeMissionWave wave) =>
        HasHuntingProof(_pass.NewbornSafeHuntingProofs, mission, wave) ||
        HasHuntingProof(_pass.NewbornNoTargetProofs, mission, wave) ||
        NoCraftCanBecomeHunterTarget();

    private bool NewbornCanRevealHunt(AlienMissionSnapshot mission, RuntimeMissionWave wave) =>
        HasHuntingProof(_pass.NewbornSafeHuntingProofs, mission, wave);

    private int NewbornHuntMode(RuntimeMissionWave wave) => wave.HuntMode == -1
        ? campaign.Content.RuntimeRules.Ufos[wave.Ufo!.Value].Value.HuntMode : wave.HuntMode;

    private string? NewbornHuntingReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule missionRule,
        CampaignTimeTrigger highest, AlienMissionSnapshot? creationMission = null)
    {
        var wave = SpawnWave(mission, missionRule);
        if (wave.Ufo is not { } handle || wave.Trajectory is not { } trajectory ||
            missionRule.Objective == RuntimeMissionObjective.InstantRetaliation ||
            missionRule.Objective == RuntimeMissionObjective.Supply && wave.Objective) return null;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Ufos[handle].Value;
        var path = rules.UfoTrajectories[trajectory].Value;
        if (path.Waypoints.Count == 0 || path.Altitude(0) == 0 ||
            (wave.HunterKillerPercentage == -1 ? rule.HunterKillerPercentage : wave.HunterKillerPercentage) <= 0) return null;
        // Retargeting consumes these stats even with no selectable craft. A zero
        // trajectory percentage must not hide an invalid maximum until after birth.
        var maximum = (long)rule.Stats.SpeedMaximum + rule.RaceBonus(mission.Race).SpeedMaximum;
        var radar = (long)rule.Stats.RadarRange + rule.RaceBonus(mission.Race).RadarRange;
        if (maximum is < 0 or > int.MaxValue)
            return wave.Escort ? "UFO escort speed exceeds the supported range." : "UFO hunting stats exceed the supported range.";
        if (radar is < int.MinValue or > int.MaxValue)
            return "UFO hunting stats exceed the supported range.";
        if (!TryNewbornInitialSpeed(path, (int)maximum, out _))
            return "UFO initial trajectory speed exceeds the supported range.";
        if (NoCraftCanBecomeHunterTarget()) return null;
        var mode = NewbornHuntMode(wave);
        var key = HuntingProof(mission, wave);
        if (_pass.NewbornNoTargetProofs.Contains(key)) return null;
        if (_pass.HuntingCraftForecast is null && ForecastHuntingCrafts(highest, ref _pass.HuntingForecastWork) is { } reason)
            return reason;
        var origin = new WorldPosition(0, 0);
        var possibleHunt = false;
        var initializedEscort = false;
        var canRemainIdle = true;
        if (radar > 0)
        {
            if (ForecastNewbornCraftCandidates(includeIgnored: false, ref _pass.HuntingForecastWork, out var candidate) is { } candidateReason)
                return candidateReason;
            if (candidate)
            {
                var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
                if (!TryOrdinarySpawnPositions(mission, missionRule, path, region, out var positions) || positions.Count == 0)
                    return null;
                if (wave.Escort)
                {
                    if (NewbornEscortOriginsReason(mission, missionRule, positions, ref _pass.HuntingForecastWork,
                            out positions, out initializedEscort, out canRemainIdle, creationMission) is { } escortReason) return escortReason;
                }
                if (NewbornRadarSeparationReason(positions, (int)radar, ref _pass.HuntingForecastWork, out var separated) is { } separationReason)
                    return separationReason;
                if (!separated)
                {
                    if (NewbornHuntReachReason(positions, rule, mission.Race, path, (int)radar,
                            initializedEscort, canRemainIdle, ref _pass.HuntingForecastWork) is { } reachReason)
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
                ref _pass.HuntingForecastWork, out _, out _, missionForecast: mission) is { } candidateReason) return candidateReason;
        }
        if (possibleHunt) _pass.NewbornSafeHuntingProofs.Add(key);
        else _pass.NewbornNoTargetProofs.Add(key);
        return null;
    }

    private string? NewbornHuntReachReason(IReadOnlyList<SpawnArea> positions, RuntimeUfoRule rule,
        string race, RuntimeUfoTrajectoryRule path, int radar, bool initializedEscort, bool canRemainIdle, ref long work)
    {
        var maximum = (long)rule.Stats.SpeedMaximum + rule.RaceBonus(race).SpeedMaximum;
        if (maximum is < 0 or > int.MaxValue) return "UFO hunting stats exceed the supported range.";
        var hypothetical = new UfoSnapshot(0, string.Empty, 0, string.Empty, 0, 0, 0,
            UfoStatus.Flying, WorldAltitudes.High)
        {
            Speed = initializedEscort ? canRemainIdle ? Math.Max((int)maximum, path.Speed(0, (int)maximum)) : (int)maximum
                : path.Speed(0, (int)maximum),
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
            foreach (var craft in _pass.HuntingCraftSources![owner.Id])
            {
                if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                var state = _pass.HuntingCraftForecast![(rules.Crafts.GetExternalId(craft.Rule), craft.Id)];
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
            foreach (var craft in _pass.HuntingCraftSources![owner.Id])
            {
                if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                var state = _pass.HuntingCraftForecast![(rules.Crafts.GetExternalId(craft.Rule), craft.Id)];
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
            foreach (var craft in _pass.HuntingCraftSources![owner.Id])
            {
                if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                var state = _pass.HuntingCraftForecast!.GetValueOrDefault((rules.Crafts.GetExternalId(craft.Rule), craft.Id), craft.Logistics!);
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
