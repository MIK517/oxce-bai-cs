using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private readonly HashSet<(int Mission, string Ufo, int Mode)> _newbornNoTargetProofs = [];

    private bool NoCraftCanBecomeHunterTarget(AlienMissionSnapshot mission, RuntimeMissionWave wave) =>
        _newbornNoTargetProofs.Contains((mission.Id, wave.UfoType, NewbornHuntMode(wave))) ||
        NoCraftCanBecomeHunterTarget();

    private int NewbornHuntMode(RuntimeMissionWave wave) => wave.HuntMode == -1
        ? campaign.Content.RuntimeRules.Ufos[wave.Ufo!.Value].Value.HuntMode : wave.HuntMode;

    private string? NewbornNoTargetReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule missionRule,
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
        var key = (mission.Id, wave.UfoType, mode);
        if (_newbornNoTargetProofs.Contains(key)) return null;
        if (_huntingCraftForecast is null && ForecastHuntingCrafts(highest, ref _huntingForecastWork) is { } reason)
            return reason;
        if ((long)rule.Stats.RadarRange + rule.RaceBonus(mission.Race).RadarRange != 0)
        {
            if (ForecastNewbornCraftCandidates(includeIgnored: false, ref _huntingForecastWork, out var candidate) is { } candidateReason)
                return candidateReason;
            if (candidate) return null;
        }
        // Native candidate attraction is evaluated before the radar test. A zero
        // range proves no target, but cannot hide malformed candidate arithmetic.
        // Constructor modes above one randomly select either zero or one; prove
        // both outcomes without drawing RNG or allocating a campaign identity.
        for (var candidateMode = mode > 1 ? 0 : mode; candidateMode <= (mode > 1 ? 1 : mode); candidateMode++)
        {
            var hypothetical = new UfoSnapshot(0, wave.UfoType, mission.Id, wave.TrajectoryId,
                0, 0, 0, UfoStatus.Flying, WorldAltitudes.High)
            {
                HunterKiller = true,
                HuntMode = candidateMode,
                Destination = new(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0, 1, 0.6),
            };
            if (ProjectHunting(hypothetical, highest, due: true, forecastFuel: true,
                ref _huntingForecastWork, out _, out _) is { } candidateReason) return candidateReason;
        }
        _newbornNoTargetProofs.Add(key);
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
