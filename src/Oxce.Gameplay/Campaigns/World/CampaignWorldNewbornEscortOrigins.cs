using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string NewbornEscortOriginWorkReason = "New UFO escort origin forecasts exceed the supported work limit.";

    private string? NewbornEscortOriginsReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule missionRule,
        IReadOnlyList<SpawnArea> nominal, ref long work, out IReadOnlyList<SpawnArea> positions,
        out bool canInitialize, out bool canRemainIdle, AlienMissionSnapshot? creationMission = null)
    {
        positions = nominal;
        canInitialize = false;
        canRemainIdle = true;
        work += _ufos.Count + (long)_missions.Count;
        if (work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
        if (WorldHunting.EscortLeader(mission.Id, _ufos) is { } leader)
        {
            positions = [new(leader.Position, 0)];
            canInitialize = true;
            canRemainIdle = false;
            return null;
        }
        var saved = creationMission ?? FindMission(mission.Id);
        if (saved is null) return "New UFO escorts require craft retarget forecasts.";
        var rules = campaign.Content.RuntimeRules;
        List<SpawnArea>? possible = null;
        foreach (var earlier in DueWaveSteps(saved, missionRule))
        {
            if (++work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
            if (earlier.NextWave == mission.NextWave && earlier.NextUfoCounter == mission.NextUfoCounter)
            {
                if (possible is not null)
                {
                    work += nominal.Count;
                    if (work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
                    possible.AddRange(nominal);
                    positions = possible;
                }
                return null;
            }
            // These earlier steps have already passed mission preflight. A failed
            // hunter constructor produces an ordinary UFO at its nominal origin,
            // even when its wave requested escort initialization.
            var region = rules.Regions[rules.Regions.GetRequired(earlier.RegionId)].Value;
            work += 3L * _alienBases.Count + 1;
            if (missionRule.Objective is RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation)
                work += 8L * campaign.BaseStates.Count * (region.Areas.Count + 1L);
            if (work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
            var wave = SpawnWave(earlier, missionRule);
            if (wave.Ufo is not { } handle || CanAdvanceNoObjectWave(earlier, missionRule)) continue;
            var percentage = missionRule.Objective == RuntimeMissionObjective.Supply && wave.Objective ? 0 :
                wave.HunterKillerPercentage == -1 ? rules.Ufos[handle].Value.HunterKillerPercentage : wave.HunterKillerPercentage;
            if (percentage >= 100) continue;
            if (wave.Trajectory is not { } path || !TryOrdinarySpawnPositions(earlier, missionRule,
                    rules.UfoTrajectories[path].Value, region, out var origins))
                return "New UFO escorts require craft retarget forecasts.";
            work += origins.Count + (long)_alienBases.Count;
            if (work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
            (possible ??= []).AddRange(origins);
            canInitialize = true;
            // Losing a Space source can omit an objective supply constructor.
            // Keep its possible origin, but retain nominal idle flight until an
            // independent ordinary birth establishes attachment in every branch.
            if (percentage <= 0 && !(missionRule.Objective == RuntimeMissionObjective.Supply && wave.Objective &&
                _pass.PossibleDailySupplySourceLoss?.Contains(mission.Id) == true))
            {
                // Native selection stops at the first ordinary leader in list
                // order. Earlier stochastic leaders remain possible first choices.
                positions = possible;
                canRemainIdle = false;
                return null;
            }
        }
        return "New UFO escorts require craft retarget forecasts.";
    }
}
