using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private List<CampaignState.BaseState> RetaliationCandidates(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        return WorldRetaliation.Candidates(campaign.BaseStates, region, rule.Objective,
            rule.SkipScoutingPhase, rule.MultiUfoRetaliation,
            static owner => new(owner.Longitude, owner.Latitude), static owner => owner.RetaliationTarget);
    }

    private string? RetaliationWaveReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, ref long work)
    {
        if (rule.Objective is not (RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation)) return null;
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        // Reserve repeated classification in counter, geometry, score and executor forecasts.
        work += 32L * campaign.BaseStates.Count * (region.Areas.Count + 1L);
        if (work > MaximumAlienBaseChecks) return "Retaliation target selection exceeds the supported work limit.";
        if (HasRetaliationTarget(mission, rule))
        {
            if (rule.Objective == RuntimeMissionObjective.InstantRetaliation)
                return "Retaliation assault arrival requires base-defense simulation.";
            var original = rule.Waves[mission.NextWave];
            if (original.Trajectory is not { } originalPath || rules.UfoTrajectories[originalPath].Value.Waypoints.Count == 0)
                return "Retaliation assault spawning requires a source trajectory.";
            if (rule.SpawnUfo is null && original.Ufo is null)
                return "Retaliation assault spawning requires a UFO rule.";
            if (!rules.UfoTrajectories.TryGet(RuntimeUfoTrajectoryRule.RetaliationAssaultRun, out var assault))
                return "Retaliation assault spawning requires the assault trajectory.";
            if (rules.UfoTrajectories[assault].Value.Waypoints.Count == 0 || rules.UfoTrajectories[assault].Value.Altitude(0) == 0)
                return "Retaliation assault ground starts require lifecycle forecasts.";
            return null;
        }
        if (rule.Objective == RuntimeMissionObjective.InstantRetaliation && mission.LiveUfos == 0 &&
            MissionExpiryReason(mission, rule, ref work) is { } expiryReason) return expiryReason;
        return null;
    }
    private bool HasRetaliationTarget(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        if (rule.Objective is not (RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation)) return false;
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        foreach (var owner in campaign.BaseStates)
            if ((rule.Objective != RuntimeMissionObjective.Retaliation || rule.SkipScoutingPhase || owner.RetaliationTarget) &&
                WorldGeometry.InsideRegion(region, new(owner.Longitude, owner.Latitude))) return true;
        return false;
    }

    private RuntimeMissionWave SpawnWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var wave = rule.Waves[mission.NextWave];
        if (!HasRetaliationTarget(mission, rule)) return wave;
        return wave with
        {
            UfoType = rule.SpawnUfo is null ? wave.UfoType : rule.SpawnUfoId,
            Ufo = rule.SpawnUfo ?? wave.Ufo,
            TrajectoryId = RuntimeUfoTrajectoryRule.RetaliationAssaultRun,
            Trajectory = campaign.Content.RuntimeRules.UfoTrajectories.GetRequired(RuntimeUfoTrajectoryRule.RetaliationAssaultRun),
            HunterKillerPercentage = 0,
            HuntMode = 0,
            HuntBehavior = 0,
            Escort = false,
        };
    }

    private WorldPosition RetaliationSpawnPosition(AlienMissionSnapshot mission, RuntimeMissionWave original,
        RuntimeRegionRule region, RuntimeUfoRule ufo)
    {
        var trajectory = campaign.Content.RuntimeRules.UfoTrajectories[original.Trajectory!.Value].Value;
        return NormalizeWaypoint(trajectory.Altitude(0) == 0
            ? WorldGeometry.LandPoint(region, campaign.Content.RuntimeRules.Campaign.Globe,
                trajectory.Zone(0), ufo.FakeWaterLandingChance, campaign.Random)
            : WorldGeometry.RandomPoint(region, trajectory.Zone(0), -1, campaign.Random));
    }

    private bool InstantRetaliationWithoutTarget(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule) =>
        rule.Objective == RuntimeMissionObjective.InstantRetaliation && RetaliationCandidates(mission, rule).Count == 0;
}
