using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? ForecastConditionalLeaderFlight(UfoSnapshot source, AlienMissionSnapshot mission,
        CampaignTimeTrigger highest, ref long work)
    {
        var rules = campaign.Content.RuntimeRules;
        var rule = MissionRule(mission);
        if (rule.Objective != RuntimeMissionObjective.Score || mission.MissionSiteZoneArea != -1 ||
            _pass.SiteResearchForecast?.PossibleExplicitTopics.Contains(rule.InterruptResearchId) == true ||
            source.Destination is not { Kind: WorldTargetKind.Waypoint } destination ||
            WorldGeometry.Distance(source.Position, destination.Position) > WorldGeometry.RadianSpeed(source.Speed)) return null;
        var trajectory = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(source.TrajectoryId)].Value;
        var next = source.TrajectoryPoint + 1;
        // A remaining idle actor must stay airborne, with no conversion, landing,
        // terminal pass interruption or land-selection lookahead to hide.
        if (next >= trajectory.Waypoints.Count || !IsAirborne(trajectory.Altitude(next)) ||
            next + 1 < trajectory.Waypoints.Count && !IsAirborne(trajectory.Altitude(next + 1))) return null;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        var areas = WorldGeometry.MissionAreas(region, trajectory.Zone(next));
        work += 3L * areas.Count + 1;
        if (work > MaximumAlienBaseChecks) return "Conditional UFO escort arrival exceeds the supported work limit.";
        if (areas.Count > 256) return null;
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(source.RuleId)].Value;
        var maximum = (long)ufoRule.Stats.SpeedMaximum + ufoRule.RaceBonus(mission.Race).SpeedMaximum;
        var replacement = maximum * trajectory.Waypoints[next].SpeedPercentage / 100;
        if (maximum is < 0 or > int.MaxValue || replacement is < 0 or > int.MaxValue)
            return "UFO hunting stats exceed the supported range.";
        if (!CanAdvanceOrdinaryWaypoint(source, mission, trajectory)) return null;
        foreach (var saved in _ufos)
        {
            if (++work > MaximumAlienBaseChecks) return NewbornEscortOriginWorkReason;
            if (FollowsUfo(saved.Destination, source)) return null;
        }
        if (NewLeaderCraftPursuitReason(source, ref work) is { } pursuitReason) return pursuitReason;
        (_pass.InexactNewLeaderFlights ??= []).Add(source.UniqueId);
        // A flight-only proxy skips an already validated ordinary arrival. Both
        // outcomes stay flying; execution retains the real flags, speed, waypoint
        // and identity. Its coordinate must never become an exact target proof.
        (_pass.NewLeaderFlightForecast ??= []).Add(source.UniqueId,
            WorldHunting.Escort(source, WorldTargetReference.ForWaypoint(0, source.Position), Math.Max(source.Speed, (int)maximum)));
        return null;
    }
}
