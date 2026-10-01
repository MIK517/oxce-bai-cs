using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string HuntGenerationReason = "Alien base hunting can create a mission; immediate hunting mission simulation is unavailable.";

    private string? AlienBaseHuntingReason(AlienBaseSnapshot alienBase, RuntimeAlienDeploymentRule deployment, CampaignTimeTrigger highest, ref long work)
    {
        if (deployment.BaseDetectionRange <= 0) return null;
        if (alienBase.MinutesSinceLastHuntMissionGeneration > int.MaxValue - 10)
            return "Alien base hunting timer exceeds the supported range.";
        if (alienBase.MinutesSinceLastHuntMissionGeneration + 10 < deployment.HuntMissionMaxFrequency) return null;
        foreach (var owner in campaign.BaseStates)
        {
            work += owner.Crafts.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            foreach (var craft in owner.Crafts)
            {
                if (craft.Logistics is not { } state) continue;
                var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                if (rule.Undetectable) continue;
                if (state.Status != "STR_OUT")
                {
                    if (highest >= CampaignTimeTrigger.ThirtyMinutes && state.IsAutoPatrolling && rule.AutoPatrol &&
                        (WorldGeometry.Distance(new(state.Longitude, state.Latitude), alienBase.Position) <
                            WorldGeometry.Nautical(deployment.BaseDetectionRange) ||
                         WorldGeometry.Distance(new(owner.Longitude, owner.Latitude), alienBase.Position) <
                            WorldGeometry.Nautical(deployment.BaseDetectionRange)) &&
                        HuntingChoiceReason(deployment, ref work) is { } launchReason) return launchReason;
                    continue;
                }
                if (state.LowFuel || state.MissionComplete) continue;
                var maximum = CraftLogistics.EffectiveDamageMaximum(state, rule, campaign.Content.RuntimeRules);
                if (state.Damage >= maximum) continue;
                if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons, campaign.Content.RuntimeRules, out _))
                    return SpeedRangeReason;
                var projected = AdvanceCraftFuel(owner, craft, state);
                if (!WorldAlienBaseHunting.Eligible(projected, rule.Undetectable, maximum,
                        alienBase.Position, deployment.BaseDetectionRange) || deployment.BaseDetectionChance <= 0) continue;
                if (HuntingChoiceReason(deployment, ref work) is { } reason) return reason;
            }
            if (highest >= CampaignTimeTrigger.OneHour)
            {
                work += owner.Transfers.Count;
                if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                foreach (var transfer in owner.Transfers)
                {
                    if (transfer.Delivered || transfer.Hours > 1 || transfer.Craft?.Logistics is not { IsAutoPatrolling: true }) continue;
                    var rule = campaign.Content.RuntimeRules.Crafts[
                        campaign.Content.RuntimeRules.Crafts.GetRequired(transfer.Craft.RuleId)].Value;
                    if (!rule.Undetectable && rule.AutoPatrol &&
                        WorldGeometry.Distance(new(owner.Longitude, owner.Latitude), alienBase.Position) <
                            WorldGeometry.Nautical(deployment.BaseDetectionRange) &&
                        HuntingChoiceReason(deployment, ref work) is { } arrivalReason) return arrivalReason;
                }
            }
        }
        return null;
    }

    private string? HuntingChoiceReason(RuntimeAlienDeploymentRule deployment, ref long work)
    {
        if (deployment.BaseDetectionChance <= 0) return null;
        try
        {
            var choices = WorldAlienBaseHunting.Weights(deployment.HuntMissions, unchecked((ulong)campaign.MonthsPassed));
            work += choices.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            _ = WorldAlienMissionGeneration.Total(choices);
            foreach (var choice in choices)
                if (choice.Value != 0 && choice.Key.Length != 0) return HuntGenerationReason;
        }
        catch (InvalidDataException error) { return error.Message; }
        return null;
    }

    private void AdvanceAlienBaseHunting(CampaignState.TimeEffects _)
    {
        for (var index = 0; index < _alienBases.Count; index++)
        {
            var alienBase = _alienBases[index];
            var deployment = AlienBaseRule(alienBase);
            if (deployment.BaseDetectionRange <= 0) continue;
            var timer = checked(alienBase.MinutesSinceLastHuntMissionGeneration + 10);
            _alienBases[index] = alienBase with { MinutesSinceLastHuntMissionGeneration = timer };
            if (timer < deployment.HuntMissionMaxFrequency) continue;
            foreach (var owner in campaign.BaseStates)
                foreach (var craft in owner.Crafts)
                {
                    if (craft.Logistics is not { } state) continue;
                    var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                    if (!WorldAlienBaseHunting.Eligible(state, rule.Undetectable,
                            CraftLogistics.EffectiveDamageMaximum(state, rule, campaign.Content.RuntimeRules),
                            alienBase.Position, deployment.BaseDetectionRange) || !Percent(deployment.BaseDetectionChance)) continue;
                    var mission = WorldAlienMissionGeneration.Choose(WorldAlienBaseHunting.Weights(deployment.HuntMissions,
                        unchecked((ulong)campaign.MonthsPassed)), campaign.Random);
                    if (mission.Length != 0) throw new InvalidOperationException("Alien base hunting changed after preflight.");
                    // Empty choices do not reset the timer or break the craft loop.
                }
        }
    }
}

/// <summary>Reference baseHunting craft eligibility and generateHuntMission reverse timeline.</summary>
internal static class WorldAlienBaseHunting
{
    public static bool Eligible(CraftLogisticsState state, bool undetectable, long damageMaximum,
        WorldPosition alienBase, int range) => state.Status == "STR_OUT" && state.Damage < damageMaximum &&
        !undetectable && !state.LowFuel && !state.MissionComplete &&
        WorldGeometry.Distance(new(state.Longitude, state.Latitude), alienBase) < WorldGeometry.Nautical(range);

    public static IReadOnlyDictionary<string, ulong> Weights(IReadOnlyList<RuntimeWeightedTimeline> timeline, ulong month)
    {
        for (var index = timeline.Count - 1; index >= 0; index--)
            if (month >= timeline[index].FirstMonth) return timeline[index].Weights;
        throw new InvalidDataException("Alien base hunting timeline has no entry for the current month.");
    }
}
