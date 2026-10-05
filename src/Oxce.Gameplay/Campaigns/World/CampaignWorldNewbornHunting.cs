using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private readonly HashSet<(int Mission, string Ufo, int Mode)> _zeroRadarHunterProofs = [];

    private bool NoCraftCanBecomeHunterTarget(AlienMissionSnapshot mission, RuntimeMissionWave wave) =>
        _zeroRadarHunterProofs.Contains((mission.Id, wave.UfoType, NewbornHuntMode(wave))) ||
        NoCraftCanBecomeHunterTarget();

    private int NewbornHuntMode(RuntimeMissionWave wave) => wave.HuntMode == -1
        ? campaign.Content.RuntimeRules.Ufos[wave.Ufo!.Value].Value.HuntMode : wave.HuntMode;

    private string? ZeroRadarNewbornReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule missionRule,
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
            (long)rule.Stats.RadarRange + rule.RaceBonus(mission.Race).RadarRange != 0 ||
            NoCraftCanBecomeHunterTarget()) return null;
        var mode = NewbornHuntMode(wave);
        var key = (mission.Id, wave.UfoType, mode);
        if (_zeroRadarHunterProofs.Contains(key)) return null;
        if (_huntingCraftForecast is null && ForecastHuntingCrafts(highest, ref _huntingForecastWork) is { } reason)
            return reason;
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
        _zeroRadarHunterProofs.Add(key);
        return null;
    }
}
