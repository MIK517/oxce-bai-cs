using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? ForecastConditionalLeaderFlight(UfoSnapshot source, AlienMissionSnapshot mission,
        CampaignTimeTrigger highest, ref long work)
    {
        var rules = campaign.Content.RuntimeRules;
        var rule = MissionRule(mission);
        // The caller has proved think-time consequences. A remaining idle actor
        // can follow a distant old waypoint, or execute one owned airborne arrival.
        if (source.Destination is not { Kind: WorldTargetKind.Waypoint } destination) return null;
        // The no-leader branch consumes this speed even though attachment would
        // replace it. Do not let the full-speed proxy hide malformed idle input.
        if (source.Speed < 0) return "UFO speed is invalid.";
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(source.RuleId)].Value;
        var maximum = (long)ufoRule.Stats.SpeedMaximum + ufoRule.RaceBonus(mission.Race).SpeedMaximum;
        if (maximum is < 0 or > int.MaxValue) return "UFO hunting stats exceed the supported range.";
        if (!(WorldGeometry.Distance(source.Position, destination.Position) > WorldGeometry.RadianSpeed(source.Speed)))
        {
            if (_pass.SiteResearchForecast?.PossibleExplicitTopics.Contains(rule.InterruptResearchId) == true) return null;
            var trajectory = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(source.TrajectoryId)].Value;
            var next = source.TrajectoryPoint + 1;
            // Ground, conversion and terminal pass interruption need their own proof.
            if (next >= trajectory.Waypoints.Count || !IsAirborne(trajectory.Altitude(next))) return null;
            var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
            var waveIndex = MissionWaveIndex(source, mission, rule);
            if ((uint)waveIndex >= (uint)rule.Waves.Count) return null;
            var wave = rule.Waves[waveIndex];
            var areas = WorldGeometry.MissionAreas(region, trajectory.Zone(next));
            work += 3L * areas.Count + 1;
            if (work > MaximumAlienBaseChecks) return "Conditional UFO escort arrival exceeds the supported work limit.";
            if (areas.Count > 256) return null;
            var replacement = maximum * trajectory.Waypoints[next].SpeedPercentage / 100;
            if (replacement is < 0 or > int.MaxValue) return "UFO hunting stats exceed the supported range.";
            if (!CanAdvanceOrdinaryWaypoint(source, mission, trajectory)) return null;
            _pass.UfoWaypointLandWork += 2 * OrdinaryWaypointLandWork(mission, rule, wave, trajectory, region, next);
            if (_pass.UfoWaypointLandWork > MaximumSpawnLandWork)
                return "UFO waypoint land selection exceeds the supported work limit.";
        }
        if (RegionalNewLeaderDependencies(source, ref work, out var dependentIds) is { } dependentReason) return dependentReason;
        var inexact = _pass.InexactNewLeaderFlights ??= [];
        inexact.Add(source.UniqueId);
        foreach (var id in dependentIds) inexact.Add(id);
        // A flight-only proxy covers old flight or its validated ordinary arrival. Both
        // outcomes stay flying; execution retains the real flags, speed, waypoint
        // and identity. Its coordinate must never become an exact target proof.
        (_pass.NewLeaderFlightForecast ??= []).Add(source.UniqueId,
            WorldHunting.Escort(source, WorldTargetReference.ForWaypoint(0, source.Position), Math.Max(source.Speed, (int)maximum)));
        return null;
    }
}
