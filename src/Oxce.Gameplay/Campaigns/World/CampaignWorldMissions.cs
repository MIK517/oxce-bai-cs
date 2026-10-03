using Oxce.Mods.Rulesets.Runtime;
using Oxce.Mods.Rulesets.CampaignStart;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// AlienMission::think's half-hour countdown, ordinary airborne UFO waves, and waves
/// without a UFO or site. Other outcomes remain guarded until their creation and script
/// consequences have handlers.
/// </summary>
internal sealed partial class CampaignWorld
{
    private bool? _newHunterHasNoTarget;
    private bool? _newLeaderHasNoCraftPursuer;

    private void RegisterMissionOperations(CampaignCapabilityRegistry registry)
    {
        registry.Timed(CampaignTimeTrigger.OneDay, CampaignTimeOrder.DayWorldMissionInterruption,
            "alien mission research interruption", InterruptMissionsDaily);
        registry.Timed(CampaignTimeTrigger.ThirtyMinutes, CampaignTimeOrder.ThirtyMinutesWorldMissions,
            "alien mission countdown", AdvanceMissionCountdowns);
    }

    private string? MissionSchedulingReason(CampaignTimeTrigger highest, IReadOnlySet<string>? guaranteedResearch)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes) return null;
        var ufoSpawns = 0;
        var newFlyingHunters = 0;
        var newEscorts = 0;
        HashSet<int>? possibleNewLeaderMissions = null;
        var work = 0;
        long sourceBaseWork = 0;
        long cleanupWork = 0;
        long geometryWork = 0;
        long landWork = 0;
        long escortSelectionWork = 0;
        Dictionary<string, int>? siteMarkerReservations = null;
        var siteSpawns = 0;
        for (var index = 0; index < _missions.Count; index++)
        {
            var mission = MissionAfterDailyResearch(_missions[index], guaranteedResearch);
            var rule = campaign.Content.RuntimeRules.AlienMissions[
                campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (IsOver(mission, rule))
            {
                if (MissionExpiryReason(mission, rule, ref cleanupWork) is { } expiryReason) return expiryReason;
                continue;
            }
            if (mission.Interrupted || mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra ||
                mission.NextWave >= rule.Waves.Count)
                continue;
            if (mission.SpawnCountdown > 30) continue;
            if (rule.Objective == RuntimeMissionObjective.Infiltration && rule.EndlessInfiltration &&
                rule.Waves.All(wave => wave.SpawnTimer == 0)) return WaveWorkReason;
            foreach (var step in DueWaveSteps(mission, rule))
            {
                if (++work > MaximumImmediateWaveSteps) return WaveWorkReason;
                if (step.AlienBase is not null)
                {
                    sourceBaseWork += _alienBases.Count;
                    if (sourceBaseWork > MaximumAlienBaseChecks) return "Alien mission source base lookups exceed the supported work limit.";
                }
                if (CompletesInfiltration(step, rule, rule.Waves[step.NextWave]) &&
                    InfiltrationReason(step, rule, highest, siteMarkerReservations ??= new(StringComparer.Ordinal),
                        ref geometryWork) is { } infiltrationReason)
                    return infiltrationReason;
                if (CompletesBaseObjective(step, rule, rule.Waves[step.NextWave]))
                {
                    siteMarkerReservations ??= new(StringComparer.Ordinal);
                    if (ObjectiveBaseSpawnReason(step, rule, highest, siteMarkerReservations, ref geometryWork) is { } baseReason)
                        return baseReason;
                }
                if (RetaliationWaveReason(step, rule, ref geometryWork) is { } retaliationReason) return retaliationReason;
                if (IsDirectSiteWave(step, rule))
                {
                    siteMarkerReservations ??= new(StringComparer.Ordinal);
                    if (DirectSiteSpawnReason(step, rule, highest, siteMarkerReservations, ref geometryWork, ref siteSpawns) is { } siteReason)
                        return siteReason;
                    var finishes = step.NextWave + 1 == rule.Waves.Count &&
                        (ulong)step.NextUfoCounter + 1 >= rule.Waves[step.NextWave].UfoCount;
                    if (finishes && !(rule.Objective == RuntimeMissionObjective.Infiltration && rule.EndlessInfiltration) &&
                        step.LiveUfos == 0 &&
                        MissionExpiryReason(step, rule, ref cleanupWork) is { } expiryReason) return expiryReason;
                    continue;
                }
                if (CanAdvanceNoObjectWave(step, rule))
                {
                    var finishes = step.NextWave + 1 == rule.Waves.Count &&
                        (ulong)step.NextUfoCounter + 1 >= rule.Waves[step.NextWave].UfoCount;
                    if (finishes && !(rule.Objective == RuntimeMissionObjective.Infiltration && rule.EndlessInfiltration) &&
                        step.LiveUfos == 0 &&
                        MissionExpiryReason(step, rule, ref cleanupWork) is { } expiryReason) return expiryReason;
                    continue;
                }
                if (NewEscortWave(rule, rule.Waves[step.NextWave]))
                {
                    escortSelectionWork += _ufos.Count;
                    if (escortSelectionWork > MaximumAlienBaseChecks)
                        return "New UFO escort selection exceeds the supported work limit.";
                }
                if (NewEscortSpawnReason(step, rule) is { } escortReason) return escortReason;
                if (CanSpawnOrdinaryUfoWave(step, rule))
                {
                    var trajectory = campaign.Content.RuntimeRules.UfoTrajectories[rule.Waves[step.NextWave].Trajectory!.Value].Value;
                    var region = campaign.Content.RuntimeRules.Regions[campaign.Content.RuntimeRules.Regions.GetRequired(step.RegionId)].Value;
                    landWork += SpawnOriginLandWork(step, rule, trajectory, region);
                    if (landWork > MaximumSpawnLandWork) return "Alien mission land selection exceeds the supported work limit.";
                    TryOrdinarySpawnPositions(step, rule, trajectory, region, out var origins);
                    TryOrdinarySpawnDestinations(step, rule, trajectory, region, out var destinations);
                    geometryWork += (long)origins.Count * destinations.Count;
                    if (geometryWork > MaximumAlienBaseChecks) return "Alien mission spawn geometry exceeds the supported work limit.";
                    ufoSpawns++;
                    var wave = rule.Waves[step.NextWave];
                    var ufoRule = campaign.Content.RuntimeRules.Ufos[wave.Ufo!.Value].Value;
                    // Every non-hunter birth is an ordinary leader. A stochastic
                    // hunter constructor may produce either outcome; no RNG is forecast.
                    if (rule.Objective == RuntimeMissionObjective.Supply && wave.Objective ||
                        (wave.HunterKillerPercentage == -1 ? ufoRule.HunterKillerPercentage : wave.HunterKillerPercentage) < 100)
                        (possibleNewLeaderMissions ??= []).Add(step.Id);
                    if (trajectory.Altitude(0) != 0 && !(rule.Objective == RuntimeMissionObjective.Supply && wave.Objective) &&
                        (wave.HunterKillerPercentage == -1 ? ufoRule.HunterKillerPercentage : wave.HunterKillerPercentage) > 0)
                        newFlyingHunters++;
                    if (wave.Escort && !(rule.Objective == RuntimeMissionObjective.Supply && wave.Objective) &&
                        (wave.HunterKillerPercentage == -1 ? ufoRule.HunterKillerPercentage : wave.HunterKillerPercentage) > 0)
                        newEscorts++;
                    continue;
                }
                return "Alien mission wave spawning requires world simulation.";
            }
        }
        long newbornFollowerWork = 0;
        var savedIdleEscorts = 0;
        var savedMovingEscorts = 0;
        foreach (var saved in _ufos)
        {
            var projected = UfoForFlight(saved, highest);
            if (projected.Escorting) savedMovingEscorts++;
            if (projected.Status != UfoStatus.Flying || !projected.HunterKiller || !projected.Escort ||
                projected.Hunting || projected.Escorting) continue;
            savedIdleEscorts++;
            if (possibleNewLeaderMissions?.Contains(saved.MissionId) != true) continue;
            // The live retarget pass sees appended leaders. It does not relocate
            // this saved source, but replaces its waypoint and speed. Prove that
            // no arrival, craft pursuit or UFO follower forecast depends on them.
            if (LoadedEscortNewLeaderReason(projected, ref newbornFollowerWork) is { } leaderReason)
                return leaderReason;
            savedMovingEscorts++;
        }
        var finalUfoCount = (long)_ufos.Count + ufoSpawns;
        var escortWork = 2L * newEscorts * finalUfoCount + (long)savedIdleEscorts * ufoSpawns;
        if (_huntingForecastWork + escortWork + newbornFollowerWork > MaximumAlienBaseChecks)
            return "New UFO escort selection exceeds the supported work limit.";
        if (2L * (savedMovingEscorts + newEscorts) * finalUfoCount > MaximumAlienBaseChecks)
            return "New UFO escort movement exceeds the supported work limit.";
        if (ufoSpawns != 0 && HasDestroyedUfoAfterTransitions(highest))
        {
            var terminal = 0;
            foreach (var ufo in _ufos)
                if (UfoDestroyedAfterTimers(ufo, highest)) terminal++;
            if ((long)_missions.Count * finalUfoCount > MaximumAlienBaseChecks ||
                (long)terminal * finalUfoCount > MaximumAlienBaseChecks)
                return "Destroyed UFO cleanup with new waves exceeds the supported work limit.";
        }
        if (newFlyingHunters != 0)
        {
            long possibleCrafts = 0;
            foreach (var owner in campaign.BaseStates)
            {
                possibleCrafts += owner.Crafts.Count;
                foreach (var transfer in owner.Transfers)
                    if (!transfer.Delivered && transfer.Craft is not null) possibleCrafts++;
                // Production precedes this wave on an hourly tick. Reserve the
                // finite queue as an upper bound without predicting staff or stocks.
                if (highest >= CampaignTimeTrigger.OneHour)
                    foreach (var production in owner.Productions)
                        foreach (var material in campaign.Content.RuntimeRules.Manufacture[production.Rule].Value.ProducedMaterials)
                            if (material.Craft is not null)
                            {
                                if (production.Infinite) return "New UFO retargeting requires finite craft production forecasts.";
                                possibleCrafts += production.Amount;
                                break;
                            }
                if (possibleCrafts > MaximumAlienBaseChecks)
                    return "New UFO retargeting exceeds the supported work limit.";
            }
            if (_huntingForecastWork + possibleCrafts * newFlyingHunters + escortWork + newbornFollowerWork > MaximumAlienBaseChecks)
                return "New UFO retargeting exceeds the supported work limit.";
        }
        if ((long)_ufos.Count + ufoSpawns > 100_000) return "World UFO population exceeds the supported limit.";
        return CanAllocateUfoIds(ufoSpawns) ? null : "UFO unique IDs are exhausted or collide with a saved UFO.";
    }

    private string? MissionExpiryReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, ref long work)
    {
        // Check both existing completion and completion forecast within this tick.
        // Saved live counts alone cannot prove that deleting the mission is safe.
        work += _ufos.Count + (long)campaign.BaseStates.Count;
        if (work > MaximumAlienBaseChecks) return "Retaliation mission cleanup exceeds the supported work limit.";
        foreach (var ufo in _ufos)
            if (ufo.MissionId == mission.Id) return "Alien mission cannot expire while UFOs still reference it.";
        return rule.Objective != RuntimeMissionObjective.Retaliation &&
            campaign.BaseStates.Any(owner => owner.RetaliationMissionId == mission.Id)
            ? "A non-retaliation mission cannot expire while a base links it as retaliation." : null;
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
                for (var work = 0; ; work++)
                {
                    if (work >= MaximumImmediateWaveSteps)
                        throw new InvalidOperationException("Mission wave work changed after preflight.");
                    var spawnsUfo = CanSpawnOrdinaryUfoWave(mission, rule);
                    var spawnsSite = IsDirectSiteWave(mission, rule);
                    if (!spawnsUfo && !spawnsSite && !CanAdvanceNoObjectWave(mission, rule))
                        throw new InvalidOperationException("Alien mission wave changed after world preflight.");
                    var wave = rule.Waves[mission.NextWave];
                    if (spawnsUfo) _ufos.Add(SpawnOrdinaryUfo(mission, wave));
                    if (spawnsSite) SpawnDirectSite(mission, rule);
                    var completesBase = CompletesBaseObjective(mission, rule, wave);
                    var completesInfiltration = CompletesInfiltration(mission, rule, wave);
                    mission = AdvanceWaveCounters(mission, rule, spawnsUfo);
                    if (completesBase) CompleteBaseObjective(mission, rule);
                    if (completesInfiltration)
                    {
                        CompleteInfiltration(mission, rule);
                        if (rule.EndlessInfiltration) mission = WorldInfiltration.Restart(mission, rule, campaign.Random);
                    }
                    _missions[index] = mission;
                    // Recursion tests the original timer, not the rounded countdown.
                    if (mission.NextWave == rule.Waves.Count || rule.Waves[mission.NextWave].SpawnTimer != 0) break;
                }
            }
        }
        for (var index = _missions.Count - 1; index >= 0; index--)
        {
            var mission = _missions[index];
            var rule = campaign.Content.RuntimeRules.AlienMissions[
                campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (IsOver(mission, rule))
            {
                if (rule.Objective == RuntimeMissionObjective.Retaliation)
                    foreach (var owner in campaign.BaseStates)
                        if (owner.RetaliationMissionId == mission.Id) owner.RetaliationMissionId = null;
                _missions.RemoveAt(index);
            }
        }
    }

    private AlienMissionSnapshot AdvanceWaveCounters(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, bool spawnsUfo)
    {
        var counter = (ulong)mission.NextUfoCounter + 1;
        var nextWave = mission.NextWave;
        if (counter >= rule.Waves[nextWave].UfoCount) { counter = 0; nextWave++; }
        var countdown = nextWave < rule.Waves.Count
            ? WorldTrajectory.SpawnCountdown(checked((int)rule.Waves[nextWave].SpawnTimer), campaign.Random)
            : mission.SpawnCountdown;
        return mission with
        {
            NextWave = nextWave,
            NextUfoCounter = checked((int)counter),
            SpawnCountdown = countdown,
            LiveUfos = spawnsUfo ? checked(mission.LiveUfos + 1) : mission.LiveUfos,
        };
    }

    private string? ImmediateNoObjectReason(AlienBaseSnapshot source, RuntimeAlienDeploymentRule deployment,
        RuntimeAlienMissionRule rule, ref long work, string boundary)
    {
        var candidate = new AlienMissionSnapshot(-1, string.Empty, string.Empty, source.Race, 0, 0, 0, 0, -1)
        { AlienBase = new(WorldTargetKind.AlienBase, deployment.MarkerName, source.Id, source.Longitude, source.Latitude) };
        foreach (var step in DueWaveSteps(candidate, rule))
        {
            if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            if (CompletesBaseObjective(step, rule, rule.Waves[step.NextWave]) ||
                CompletesInfiltration(step, rule, rule.Waves[step.NextWave]) || !CanAdvanceNoObjectWave(step, rule)) return boundary;
        }
        return null;
    }

    private AlienMissionSnapshot ThinkNoObjectMission(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        if (mission.SpawnCountdown > 30) return mission with { SpawnCountdown = mission.SpawnCountdown - 30 };
        for (var work = 0; ; work++)
        {
            if (work >= MaximumImmediateWaveSteps || !CanAdvanceNoObjectWave(mission, rule))
                throw new InvalidOperationException("Immediate no-object mission changed after preflight.");
            mission = AdvanceWaveCounters(mission, rule, spawnsUfo: false);
            if (mission.NextWave == rule.Waves.Count || rule.Waves[mission.NextWave].SpawnTimer != 0) return mission;
        }
    }

    private static bool IsOver(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule) =>
        mission.Interrupted && mission.LiveUfos == 0 ||
        !(rule.Objective == RuntimeMissionObjective.Infiltration && rule.EndlessInfiltration) &&
        mission.NextWave == rule.Waves.Count && mission.LiveUfos == 0;

    private bool CanAdvanceNoObjectWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        if (rule.Objective == RuntimeMissionObjective.InstantRetaliation ||
            rule.Objective == RuntimeMissionObjective.Retaliation && RetaliationCandidates(mission, rule).Count != 0)
            return false;
        var wave = rule.Waves[mission.NextWave];
        if (SuppressedSupplyWave(mission, rule, wave))
            return wave.Trajectory is not null && CanAdvanceWaveCounters(mission, rule, wave);
        if (wave.Ufo is not null || wave.Trajectory is null ||
            rule.Objective == RuntimeMissionObjective.Site && wave.Objective ||
            wave.Deployment is { } deployment && campaign.Content.RuntimeRules.AlienDeployments[deployment].Value.MarkerName.Length != 0)
            return false;
        return CanAdvanceWaveCounters(mission, rule, wave);
    }

    private bool SuppressedSupplyWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, RuntimeMissionWave wave) =>
        rule.Objective == RuntimeMissionObjective.Supply &&
        (wave.Ufo is null ? wave.Deployment is null : wave.Objective && MissionSourceBase(mission) is null);

    private bool CanSpawnOrdinaryUfoWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        if (rule.Objective == RuntimeMissionObjective.InstantRetaliation ||
            rule.Objective == RuntimeMissionObjective.Retaliation && RetaliationCandidates(mission, rule).Count != 0 ||
            mission.LiveUfos == int.MaxValue)
            return false;
        var wave = rule.Waves[mission.NextWave];
        if (wave.Ufo is not { } ufoHandle || wave.Trajectory is not { } trajectoryHandle ||
            SuppressedSupplyWave(mission, rule, wave))
            return false;
        var rules = campaign.Content.RuntimeRules;
        var ufoRule = rules.Ufos[ufoHandle].Value;
        var hunterKillerPercentage = wave.HunterKillerPercentage == -1
            ? ufoRule.HunterKillerPercentage : wave.HunterKillerPercentage;
        var trajectory = rules.UfoTrajectories[trajectoryHandle].Value;
        if (trajectory.Waypoints.Count < 2 ||
            trajectory.Altitude(0) != 0 && !IsAirborne(trajectory.Altitude(0)) ||
            trajectory.Altitude(1) != 0 && !IsAirborne(trajectory.Altitude(1)))
            return false;
        if (hunterKillerPercentage > 0 && !(rule.Objective == RuntimeMissionObjective.Supply && wave.Objective) &&
            (trajectory.Altitude(0) != 0 && !NoCraftCanBecomeHunterTarget())) return false;
        if (NewEscortSpawnReason(mission, rule) is not null) return false;
        var stats = ufoRule.StatsForRace(mission.Race);
        var speed = trajectory.Speed(0, stats.SpeedMaximum);
        if (trajectory.Altitude(0) == 0 &&
            (rule.Objective == RuntimeMissionObjective.Supply || stats.DamageMaximum <= 0 ||
             trajectory.GroundTimer <= 0 || trajectory.GroundTimer > int.MaxValue / 5))
            return false;
        // The half-hour UFO handler scores and detects this ship after spawning.
        if (ufoRule.Scripts.Count != 0 ||
            !WorldUfoShields.IsValid(-1, stats.ShieldCapacity, stats.ShieldRechargeInGeoscape) || speed < 0)
            return false;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        if (!TryOrdinarySpawnPositions(mission, rule, trajectory, region, out var positions) ||
            !TryOrdinarySpawnDestinations(mission, rule, trajectory, region, out var destinations))
            return false;
        foreach (var position in positions)
            foreach (var destination in destinations)
                if (!(WorldGeometry.Distance(position.Center, destination.Center) - position.Radius - destination.Radius > WorldGeometry.RadianSpeed(speed) + 1e-12)) return false;
        return CanAdvanceWaveCounters(mission, rule, wave);
    }

    private bool NewEscortWave(RuntimeAlienMissionRule rule, RuntimeMissionWave wave) =>
        wave.Escort && wave.Ufo is { } handle && !(rule.Objective == RuntimeMissionObjective.Supply && wave.Objective) &&
        (wave.HunterKillerPercentage == -1
            ? campaign.Content.RuntimeRules.Ufos[handle].Value.HunterKillerPercentage : wave.HunterKillerPercentage) > 0;

    private string? NewEscortSpawnReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var wave = rule.Waves[mission.NextWave];
        if (!NewEscortWave(rule, wave) || wave.Trajectory is not { } trajectory) return null;
        var ufoRule = campaign.Content.RuntimeRules.Ufos[wave.Ufo!.Value].Value;
        var path = campaign.Content.RuntimeRules.UfoTrajectories[trajectory].Value;
        var maximum = (long)ufoRule.Stats.SpeedMaximum + ufoRule.RaceBonus(mission.Race).SpeedMaximum;
        if (maximum is < 0 or > int.MaxValue) return "UFO escort speed exceeds the supported range.";
        if (path.Waypoints.Count != 0 && path.Altitude(0) != 0 && !NoCraftCanBecomeHunterTarget())
            return "New UFO escorts require craft retarget forecasts.";
        if (WorldHunting.EscortLeader(mission.Id, _ufos) is { } leader && (leader.Hunting || leader.Escorting))
            return "UFO escort chains require additional movement forecasts.";
        return null;
    }

    // A new hunter has no original target. With no current or potential airborne
    // craft, the reference retarget pass cannot select a craft.
    private bool NoCraftCanBecomeHunterTarget() => _newHunterHasNoTarget ??= CalculateNoHunterTarget();

    private bool CalculateNoHunterTarget()
    {
        long work = 0;
        foreach (var owner in campaign.BaseStates)
        {
            work += owner.Crafts.Count + (long)owner.Transfers.Count;
            if (work > MaximumAlienBaseChecks) return false;
            foreach (var craft in owner.Crafts)
                if (!campaign.Content.RuntimeRules.Crafts[craft.Rule].Value.Undetectable &&
                    craft.Logistics is { } state &&
                    (state.Status == "STR_OUT" && !WorldHunting.IsIgnored(state.LowFuel, state.MissionComplete) || state.IsAutoPatrolling &&
                        campaign.Content.RuntimeRules.Crafts[craft.Rule].Value.AutoPatrol)) return false;
            foreach (var transfer in owner.Transfers)
                if (!transfer.Delivered && transfer.Craft?.Logistics is { } state &&
                    (state.Status == "STR_OUT" || state.IsAutoPatrolling &&
                        campaign.Content.RuntimeRules.Crafts[
                            campaign.Content.RuntimeRules.Crafts.GetRequired(transfer.Craft.RuleId)].Value.AutoPatrol)) return false;
        }
        return true;
    }

    private string? LoadedEscortNewLeaderReason(UfoSnapshot escort, ref long work)
    {
        if (!(_newLeaderHasNoCraftPursuer ??= CalculateNoCraftPursuer(ref work)))
            return "Loaded idle escorts require same-tick newborn craft pursuit forecasts.";
        if (escort.Destination is not { Kind: WorldTargetKind.Waypoint } destination ||
            !(WorldGeometry.Distance(escort.Position, destination.Position) > WorldGeometry.RadianSpeed(escort.Speed) + 1e-12))
            return "Loaded idle escorts require same-tick newborn waypoint forecasts.";
        work += _ufos.Count;
        if (work > MaximumAlienBaseChecks) return "New UFO escort selection exceeds the supported work limit.";
        foreach (var follower in _ufos)
            if (follower.Escorting && FollowsUfo(follower.Destination, escort))
                return "Loaded idle escorts require same-tick newborn UFO follower forecasts.";
        return null;
    }

    private bool CalculateNoCraftPursuer(ref long work)
    {
        foreach (var owner in campaign.BaseStates)
        {
            work += owner.Crafts.Count + (long)owner.Transfers.Count;
            if (work > MaximumAlienBaseChecks) return false;
            foreach (var craft in owner.Crafts)
                if (craft.Logistics is { } state &&
                    (state.Status == "STR_OUT" || state.IsAutoPatrolling &&
                        campaign.Content.RuntimeRules.Crafts[craft.Rule].Value.AutoPatrol)) return false;
            foreach (var transfer in owner.Transfers)
                if (!transfer.Delivered && transfer.Craft?.Logistics is { } state &&
                    (state.Status == "STR_OUT" || state.IsAutoPatrolling &&
                        campaign.Content.RuntimeRules.Crafts[
                            campaign.Content.RuntimeRules.Crafts.GetRequired(transfer.Craft.RuleId)].Value.AutoPatrol)) return false;
        }
        return true;
    }

    private AlienBaseSnapshot? MissionSourceBase(AlienMissionSnapshot mission)
    {
        if (mission.AlienBase is null) return null;
        foreach (var candidate in _alienBases)
            if (FollowsAlienBase(mission.AlienBase, candidate)) return candidate;
        return null;
    }

    private bool TryOrdinarySpawnPositions(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        RuntimeUfoTrajectoryRule trajectory, RuntimeRegionRule region, out IReadOnlyList<SpawnArea> positions)
    {
        var source = rule.OperationType == RuntimeMissionOperationType.Space ? null : MissionSourceBase(mission);
        if (source is not null && rule.Objective == RuntimeMissionObjective.Supply)
        {
            positions = [new(source.Position, 0)];
            return source.Position.IsNormalized;
        }
        var wave = rule.Waves[mission.NextWave];
        if (source is not null)
        {
            positions = [];
            if (WorldGeometry.MissionAreas(region, trajectory.Zone(0)).Count > 256) return false;
            ValidateWaypointAreas(region, trajectory.Zone(0));
            if (mission.MissionSiteZoneArea != -1 && wave.Objective && trajectory.Zone(0) == rule.SpawnZone)
                _ = SelectedMissionArea(region, rule.SpawnZone, mission.MissionSiteZoneArea);
            positions = [new(source.Position, 0)];
            return source.Position.IsNormalized;
        }
        if (rule.Objective != RuntimeMissionObjective.Supply && mission.MissionSiteZoneArea != -1 &&
            wave.Objective && trajectory.Zone(0) == rule.SpawnZone)
        {
            var area = SelectedMissionArea(region, rule.SpawnZone, mission.MissionSiteZoneArea);
            positions = [];
            if (wave.ObjectiveOnTheLandingSite)
            {
                if (!TrySpawnArea(area, out var selected)) return false;
                positions = [selected];
            }
            else positions = [new(WorldPosition.Create(area.LongitudeMinimum, area.LatitudeMinimum), 0)];
        }
        else
        {
            var areas = WorldGeometry.MissionAreas(region, trajectory.Zone(0));
            positions = [];
            if (areas.Count > 256) return false;
            var result = new SpawnArea[areas.Count];
            for (var index = 0; index < areas.Count; index++)
                if (!TrySpawnArea(areas[index], out result[index])) return false;
            positions = result;
        }
        return positions.All(static position => position.Center.IsNormalized);
    }

    private bool TryOrdinarySpawnDestinations(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        RuntimeUfoTrajectoryRule trajectory, RuntimeRegionRule region, out IReadOnlyList<SpawnArea> positions)
    {
        if (rule.Objective == RuntimeMissionObjective.Supply && rule.Waves[mission.NextWave].Objective &&
            trajectory.Altitude(1) == 0 && MissionSourceBase(mission) is { } supplyBase)
        {
            positions = [new(supplyBase.Position, 0)];
            return supplyBase.Position.IsNormalized;
        }
        _ = WorldGeometry.MissionAreas(region, trajectory.Zone(1));
        var wave = rule.Waves[mission.NextWave];
        if (rule.Objective != RuntimeMissionObjective.Supply && mission.MissionSiteZoneArea != -1 &&
            wave.Objective && trajectory.Zone(1) == rule.SpawnZone)
        {
            var area = SelectedMissionArea(region, rule.SpawnZone, mission.MissionSiteZoneArea);
            positions = [];
            if (wave.ObjectiveOnTheLandingSite)
            {
                if (!TrySpawnArea(area, out var selected)) return false;
                positions = [selected];
            }
            else positions = [new(WorldPosition.Create(area.LongitudeMinimum, area.LatitudeMinimum), 0)];
            return true;
        }
        if (rule.Objective != RuntimeMissionObjective.Supply && rule.OperationType != RuntimeMissionOperationType.Space &&
            trajectory.Waypoints.Count == 2 && MissionSourceBase(mission) is { } source)
        {
            positions = [new(source.Position, 0)];
            return source.Position.IsNormalized;
        }
        var areas = WorldGeometry.MissionAreas(region, trajectory.Zone(1));
        positions = [];
        if (areas.Count > 256) return false;
        var result = new SpawnArea[areas.Count];
        for (var index = 0; index < areas.Count; index++)
            if (!TrySpawnArea(areas[index], out result[index])) return false;
        positions = result;
        return true;
    }

    private static MissionArea SelectedMissionArea(RuntimeRegionRule region, int zone, int index)
    {
        var areas = WorldGeometry.MissionAreas(region, zone);
        if ((uint)index >= (uint)areas.Count) throw new InvalidDataException($"Mission zone {zone} has no area {index}.");
        var area = areas[index];
        ValidateWaypointPosition(area.LongitudeMinimum, area.LatitudeMinimum);
        ValidateWaypointPosition(area.LongitudeMaximum, area.LatitudeMaximum);
        return area;
    }

    private readonly record struct SpawnArea(WorldPosition Center, double Radius);

    private const long MaximumSpawnLandWork = 10_000_000;

    private long SpawnOriginLandWork(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        RuntimeUfoTrajectoryRule trajectory, RuntimeRegionRule region)
    {
        var wave = rule.Waves[mission.NextWave];
        var selected = rule.Objective != RuntimeMissionObjective.Supply && mission.MissionSiteZoneArea != -1 &&
            wave.Objective && trajectory.Zone(0) == rule.SpawnZone;
        if (selected && !wave.ObjectiveOnTheLandingSite) return 0;
        if (!selected && (rule.Objective == RuntimeMissionObjective.Supply ? trajectory.Altitude(0) != 0 : trajectory.Altitude(1) != 0)) return 0;
        if (rule.Objective == RuntimeMissionObjective.Supply && rule.OperationType != RuntimeMissionOperationType.Space &&
            MissionSourceBase(mission) is not null) return 0;
        if (!selected && WorldGeometry.MissionAreas(region, trajectory.Zone(0))[0].IsPoint) return 0;
        long scan = 1;
        foreach (var polygon in campaign.Content.RuntimeRules.Campaign.Globe.Polygons)
            scan += 1L + 4L * polygon.Points.Count;
        // Up to 100 attempts, with two polygon queries and an ordinary region query.
        return 100 * (2 * scan + (selected ? 0 : region.Areas.Count) + 1);
    }

    private static bool TrySpawnArea(MissionArea area, out SpawnArea result)
    {
        result = default;
        ValidateWaypointPosition(area.LongitudeMinimum, area.LatitudeMinimum);
        ValidateWaypointPosition(area.LongitudeMaximum, area.LatitudeMaximum);
        // A point has no uncertain latitude interval. Preserve Target's pole
        // normalization already supported by the former point-origin projection.
        if (area.IsPoint)
        {
            result = new(WorldPosition.Create(area.LongitudeMinimum, area.LatitudeMinimum), 0);
            return true;
        }
        // Keep the whole raw latitude interval within a single pole domain.
        if (Math.Min(area.LatitudeMinimum, area.LatitudeMaximum) < -Math.PI / 2 ||
            Math.Max(area.LatitudeMinimum, area.LatitudeMaximum) > Math.PI / 2) return false;
        var longitudeWidth = Math.Abs(area.LongitudeMaximum - area.LongitudeMinimum);
        var latitudeWidth = Math.Abs(area.LatitudeMaximum - area.LatitudeMinimum);
        var center = WorldPosition.Create((area.LongitudeMinimum + area.LongitudeMaximum) / 2,
            (area.LatitudeMinimum + area.LatitudeMaximum) / 2);
        // A path along the meridian and parallel bounds every point in the rectangle.
        result = new(center, Math.Min(Math.PI, (longitudeWidth + latitudeWidth) / 2));
        return true;
    }

    private bool CanAllocateUfoIds(int count)
    {
        if (count == 0) return true;
        var first = campaign.PeekNextId("STR_UFO_UNIQUE");
        var afterLast = (long)first + count;
        if (afterLast > int.MaxValue) return false;
        foreach (var ufo in _ufos)
            if (ufo.UniqueId >= first && ufo.UniqueId < afterLast)
                return false;
        return true;
    }

    private UfoSnapshot SpawnOrdinaryUfo(AlienMissionSnapshot mission, RuntimeMissionWave wave)
    {
        var rules = campaign.Content.RuntimeRules;
        var ufoRule = rules.Ufos[wave.Ufo!.Value].Value;
        var trajectory = rules.UfoTrajectories[wave.Trajectory!.Value].Value;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        var missionRule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
        var source = missionRule.OperationType == RuntimeMissionOperationType.Space ? null : MissionSourceBase(mission);
        var hunter = missionRule.Objective == RuntimeMissionObjective.Supply && wave.Objective
            ? (HunterKiller: false, Mode: 0, Behavior: 0)
            : WorldHunting.Initialize(wave.HunterKillerPercentage == -1 ? ufoRule.HunterKillerPercentage : wave.HunterKillerPercentage,
                wave.HuntMode == -1 ? ufoRule.HuntMode : wave.HuntMode,
                wave.HuntBehavior == -1 ? ufoRule.HuntBehavior : wave.HuntBehavior, campaign.Random);
        var position = source is not null && missionRule.Objective == RuntimeMissionObjective.Supply
            ? source.Position
            : NormalizeWaypoint(missionRule.Objective == RuntimeMissionObjective.Supply
                ? WorldGeometry.RandomPoint(region, trajectory.Zone(0), -1, campaign.Random)
                : OrdinaryWaypoint(mission, trajectory, 0, region, ufoRule, wave));
        if (source is not null) position = source.Position;
        var destination = NormalizeWaypoint(missionRule.Objective == RuntimeMissionObjective.Supply
            ? SupplySpawnDestination(mission, wave, trajectory, region, ufoRule)
            : OrdinaryWaypoint(mission, trajectory, 1, region, ufoRule, wave));
        var stats = ufoRule.StatsForRace(mission.Race);
        var speed = trajectory.Speed(0, stats.SpeedMaximum);
        var speedRadian = WorldGeometry.RadianSpeed(speed);
        var vector = WorldGeometry.SpeedVector(position, destination, speedRadian);
        var spawned = new UfoSnapshot(campaign.NextId("STR_UFO_UNIQUE"), wave.UfoType, mission.Id,
            wave.TrajectoryId, 0, position.Longitude, position.Latitude,
            trajectory.Altitude(0) == 0 ? UfoStatus.Landed : UfoStatus.Flying,
            WorldAltitudes.All[trajectory.Altitude(0)])
        {
            HunterKiller = hunter.HunterKiller,
            HuntMode = hunter.Mode,
            HuntBehavior = hunter.Behavior,
            MissionWaveNumber = mission.NextWave,
            SecondsRemaining = trajectory.Altitude(0) == 0 ? checked(trajectory.GroundTimer * 5) : 0,
            Speed = speed,
            SpeedRadian = speedRadian,
            SpeedLongitude = vector.Longitude,
            SpeedLatitude = vector.Latitude,
            Destination = new WorldTargetReference(WorldTargetKind.Waypoint,
                WorldTargetReference.WaypointType, 0, destination.Longitude, destination.Latitude),
        };
        return WorldHunting.InitializeEscort(spawned, wave.Escort, stats.SpeedMaximum, _ufos);
    }

    private WorldPosition SupplySpawnDestination(AlienMissionSnapshot mission, RuntimeMissionWave wave,
        RuntimeUfoTrajectoryRule trajectory, RuntimeRegionRule region, RuntimeUfoRule ufoRule)
    {
        if (trajectory.Altitude(1) != 0)
            return WorldGeometry.RandomPoint(region, trajectory.Zone(1), -1, campaign.Random);
        if (wave.Objective) return MissionSourceBase(mission)!.Position;
        return WorldGeometry.LandPoint(region, campaign.Content.RuntimeRules.Campaign.Globe,
            trajectory.Zone(1), ufoRule.FakeWaterLandingChance, campaign.Random);
    }

    private static WorldPosition NormalizeWaypoint(WorldPosition position) =>
        WorldPosition.Create(position.Longitude, position.Latitude);

    private static bool IsAirborne(int altitude) => altitude > 0 && altitude < WorldAltitudes.All.Count;

    private static bool CanAdvanceWaveCounters(
        AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, RuntimeMissionWave wave)
    {
        var counter = (ulong)mission.NextUfoCounter + 1;
        if (counter > int.MaxValue && counter < wave.UfoCount) return false;
        var nextWave = counter >= wave.UfoCount ? mission.NextWave + 1 : mission.NextWave;
        if (nextWave == rule.Waves.Count)
        {
            if (rule.Objective != RuntimeMissionObjective.Infiltration || !rule.EndlessInfiltration) return true;
            nextWave = 0;
        }
        var timer = rule.Waves[nextWave].SpawnTimer;
        // DueWaveSteps validates the entire immediate chain before any mutation/RNG.
        if (timer > int.MaxValue) return false;
        var steps = (long)(timer / 30);
        return (steps / 2 + steps) * 30 <= int.MaxValue;
    }

    private const int MaximumImmediateWaveSteps = 4096;
    private const string WaveWorkReason = "Immediate mission waves exceed the supported work limit.";

    /// <summary>Forecasts only counters, never object creation, scripts or random choices.</summary>
    private IEnumerable<AlienMissionSnapshot> DueWaveSteps(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        for (var work = 0; ; work++)
        {
            if (work >= MaximumImmediateWaveSteps) throw new InvalidDataException(WaveWorkReason);
            yield return mission;
            var wave = rule.Waves[mission.NextWave];
            var counter = (ulong)mission.NextUfoCounter + 1;
            var nextWave = mission.NextWave;
            if (counter >= wave.UfoCount) { counter = 0; nextWave++; }
            if (nextWave == rule.Waves.Count && rule.Objective == RuntimeMissionObjective.Infiltration && rule.EndlessInfiltration)
                nextWave = 0;
            if (nextWave == rule.Waves.Count || rule.Waves[nextWave].SpawnTimer != 0) yield break;
            mission = mission with
            {
                NextWave = nextWave,
                NextUfoCounter = checked((int)counter),
                SpawnCountdown = 0,
                LiveUfos = wave.Ufo is not null && !SuppressedSupplyWave(mission, rule, wave)
                    ? checked(mission.LiveUfos + 1) : mission.LiveUfos
            };
        }
    }
}
