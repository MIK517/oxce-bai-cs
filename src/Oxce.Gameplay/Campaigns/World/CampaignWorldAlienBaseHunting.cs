using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string HuntGenerationReason = "Alien base hunting can create a mission; immediate hunting mission simulation is unavailable.";

    private string? BaseHuntingCraftRemovalReason(CampaignTimeTrigger highest, ref long work)
    {
        if (_pass.BaseHuntingCraftRemovalTypes is not null) return null;
        if (campaign.ProductionPreflight.CraftRemovalWork(_ufos.Count, highest, ref work, out var types) is { } reason)
            return reason;
        _pass.BaseHuntingCraftRemovalTypes = types;
        return null;
    }

    private string? BaseHuntingPopulationReason(CampaignTimeTrigger highest, ref long work, string boundary)
    {
        if (_pass.BaseHuntingCraftPopulationBound is null)
        {
            if (campaign.ProductionPreflight.CraftPopulationBound(highest, ref work, out var population) is { } reason)
                return reason;
            _pass.BaseHuntingCraftPopulationBound = population;
        }
        work += campaign.BaseStates.Count + _pass.BaseHuntingCraftPopulationBound.Value;
        return work > MaximumAlienBaseChecks ? boundary : null;
    }

    private string? AlienBaseHuntingReason(AlienBaseSnapshot alienBase, RuntimeAlienDeploymentRule deployment, CampaignTimeTrigger highest, ref long work, out bool canGenerate)
    {
        canGenerate = false;
        if (deployment.BaseDetectionRange <= 0) return null;
        if (alienBase.MinutesSinceLastHuntMissionGeneration > int.MaxValue - 10)
            return "Alien base hunting timer exceeds the supported range.";
        if (alienBase.MinutesSinceLastHuntMissionGeneration + 10 < deployment.HuntMissionMaxFrequency) return null;
        if (deployment.BaseDetectionChance <= 0) return BaseHuntingPopulationReason(highest, ref work, AlienBaseWorkReason);
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
                            WorldGeometry.Nautical(deployment.BaseDetectionRange)))
                    {
                        if (_pass.HuntingCraftForecast is null && ForecastHuntingCrafts(highest, ref work) is { } forecastReason)
                            return forecastReason;
                        if (!_pass.HuntingCraftForecast!.TryGetValue((campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id), out var forecast))
                            continue;
                        if (WorldAlienBaseHunting.Eligible(forecast, rule.Undetectable,
                                CraftLogistics.EffectiveDamageMaximum(forecast, rule, campaign.Content.RuntimeRules),
                                alienBase.Position, deployment.BaseDetectionRange) &&
                            HuntingChoiceReason(alienBase, deployment, highest, ref work, ref canGenerate) is { } launchReason) return launchReason;
                    }
                    continue;
                }
                if (state.LowFuel || state.MissionComplete) continue;
                if (BaseHuntingCraftRemovalReason(highest, ref work) is { } removalReason) return removalReason;
                var maximum = CraftLogistics.EffectiveDamageMaximum(state, rule, campaign.Content.RuntimeRules);
                if (state.Damage >= maximum) continue;
                if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons, campaign.Content.RuntimeRules, out _))
                    return SpeedRangeReason;
                var projected = state;
                if (state.Destination is { Kind: WorldTargetKind.Craft } ||
                    _pass.BaseHuntingCraftRemovalTypes!.Contains(campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule)))
                {
                    if (_pass.HuntingCraftForecast is null && ForecastHuntingCrafts(highest, ref work) is { } forecastReason)
                        return forecastReason;
                    if (!_pass.HuntingCraftForecast!.TryGetValue((campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id), out projected))
                        continue;
                }
                else projected = AdvanceCraftFuel(owner, craft, state);
                if (!WorldAlienBaseHunting.Eligible(projected, rule.Undetectable, maximum,
                        alienBase.Position, deployment.BaseDetectionRange) || deployment.BaseDetectionChance <= 0) continue;
                if (HuntingChoiceReason(alienBase, deployment, highest, ref work, ref canGenerate) is { } reason) return reason;
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
                            WorldGeometry.Nautical(deployment.BaseDetectionRange))
                    {
                        if (_pass.HuntingCraftForecast is null && ForecastHuntingCrafts(highest, ref work) is { } forecastReason)
                            return forecastReason;
                        var forecast = _pass.HuntingCraftForecast!.GetValueOrDefault((transfer.Craft.RuleId, transfer.Craft.Id));
                        if (forecast is not null && WorldAlienBaseHunting.Eligible(forecast, rule.Undetectable,
                                CraftLogistics.EffectiveDamageMaximum(forecast, rule, campaign.Content.RuntimeRules),
                                alienBase.Position, deployment.BaseDetectionRange) &&
                            HuntingChoiceReason(alienBase, deployment, highest, ref work, ref canGenerate) is { } arrivalReason) return arrivalReason;
                    }
                }
            }
        }
        return null;
    }

    private string? HuntingChoiceReason(AlienBaseSnapshot alienBase, RuntimeAlienDeploymentRule deployment, CampaignTimeTrigger highest, ref long work, ref bool canGenerate)
    {
        if (deployment.BaseDetectionChance <= 0) return null;
        try
        {
            var choices = WorldAlienBaseHunting.Weights(deployment.HuntMissions, unchecked((ulong)campaign.MonthsPassed));
            work += choices.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            _ = WorldAlienMissionGeneration.Total(choices);
            foreach (var choice in choices)
            {
                if (choice.Value == 0 || choice.Key.Length == 0) continue;
                if (!campaign.Content.RuntimeRules.AlienMissions.TryGet(choice.Key, out var handle))
                    return "Alien base hunting references an undefined mission.";
                if (HuntingMissionReason(alienBase, deployment,
                        campaign.Content.RuntimeRules.AlienMissions[handle].Value, highest, ref work, missionId: choice.Key) is { } reason) return reason;
                canGenerate = true;
            }
        }
        catch (InvalidDataException error) { return error.Message; }
        catch (OverflowException) { return "Alien base hunting mission fields exceed the supported range."; }
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
            var started = false;
            foreach (var owner in campaign.BaseStates)
            {
                foreach (var craft in owner.Crafts)
                {
                    if (craft.Logistics is not { } state) continue;
                    var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                    if (!WorldAlienBaseHunting.Eligible(state, rule.Undetectable,
                            CraftLogistics.EffectiveDamageMaximum(state, rule, campaign.Content.RuntimeRules),
                            alienBase.Position, deployment.BaseDetectionRange) || !Percent(deployment.BaseDetectionChance)) continue;
                    var mission = WorldAlienMissionGeneration.Choose(WorldAlienBaseHunting.Weights(deployment.HuntMissions,
                        unchecked((ulong)campaign.MonthsPassed)), campaign.Random);
                    if (mission.Length != 0)
                    {
                        GenerateHuntingMission(alienBase, deployment, mission);
                        _alienBases[index] = _alienBases[index] with { MinutesSinceLastHuntMissionGeneration = 0 };
                        started = true;
                        break;
                    }
                    // Empty choices do not reset the timer or break the craft loop.
                }
                if (started) break;
            }
        }
    }

    private string? HuntingMissionReason(AlienBaseSnapshot source, RuntimeAlienDeploymentRule deployment,
        RuntimeAlienMissionRule mission, CampaignTimeTrigger highest, ref long work, bool newborn = false, string? sourceRegion = null,
        string missionId = "")
    {
        if (!Enum.IsDefined(mission.Objective) || !Enum.IsDefined(mission.OperationType) || mission.Waves.Count == 0)
            return "Alien base hunting requires a valid mission and first wave.";
        var timer = mission.Waves[0].SpawnTimer;
        var steps = timer / 30;
        if (timer > int.MaxValue || (steps / 2 + steps) * 30 > int.MaxValue)
            return "Alien base hunting initial timer exceeds the supported range.";
        var proofKey = (source.Id, source.DeploymentId, missionId);
        var mayCollect = _pass.CollectHuntingGeneratedWaves && !newborn && steps / 2 <= 1 &&
            mission.Objective is (RuntimeMissionObjective.Score or RuntimeMissionObjective.Supply or RuntimeMissionObjective.Site or
                RuntimeMissionObjective.Base or RuntimeMissionObjective.Infiltration) &&
            _pass.HuntingGeneratedWaveProofs?.Contains(proofKey) != true;
        if (mayCollect)
        {
            work += mission.Waves.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        }
        var collect = mayCollect && HasGeneratedObjectWave(mission);
        if (steps / 2 <= 1 && !collect && _pass.HuntingGeneratedWaveProofs?.Contains(proofKey) != true &&
            mission.Objective is not (RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation) && ImmediateNoObjectReason(source, deployment, mission, ref work,
                HuntGenerationReason) is { } immediateReason) return immediateReason;
        if (!deployment.HuntMissionRaceFromAlienBase && mission.HasRaceWeights)
        {
            var weights = WorldAlienMissionGeneration.WeightsAt(mission.RaceWeights,
                unchecked((ulong)campaign.MonthsPassed), race: true);
            work += weights.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            _ = WorldAlienMissionGeneration.Total(weights);
        }
        work += campaign.RegionStates.Count + (long)_alienBases.Count;
        if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        if (!newborn && _alienBases.Count(other => other.Id == source.Id && MarkerName(other.DeploymentId) == MarkerName(source.DeploymentId)) != 1)
            return "Alien base hunting source identity is ambiguous.";
        if ((sourceRegion ?? LocateGenerationRegion(source.Position)) is not { } regionName)
            return "Alien base hunting cannot locate its source region.";
        var regions = campaign.Content.RuntimeRules.Regions;
        var region = regions[regions.GetRequired(regionName)].Value;
        if (region.MissionRegionId.Length != 0)
        {
            if (region.MissionRegion is not { } remap) return "Alien base hunting references an undefined fallback region.";
            region = regions[remap].Value;
            regionName = regions.GetExternalId(remap);
        }
        if (steps / 2 <= 1 && mission.Objective == RuntimeMissionObjective.Retaliation && !mission.SkipScoutingPhase &&
            HuntingRetaliationDiscoveryReason(region, highest, ref work) is { } discoveryReason) return discoveryReason;
        if (steps / 2 <= 1 && mission.Objective is (RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation) &&
            ImmediateNoObjectReason(source, deployment, mission, ref work, HuntGenerationReason, regionName) is { } retaliationReason)
            return retaliationReason;
        if (mission.Objective == RuntimeMissionObjective.Site &&
            (mission.SpawnZone < 0 || mission.SpawnZone >= region.MissionZones.Count))
            return "Alien base hunting site mission has an invalid spawn zone.";
        if (collect)
        {
            if (CollectGeneratedWaveForecasts(source, deployment, mission, missionId, regionName, hunting: true, ref work) is { } waveReason)
                return waveReason;
            (_pass.HuntingGeneratedWaveProofs ??= []).Add(proofKey);
        }
        return null;
    }

    private string? HuntingRetaliationDiscoveryReason(RuntimeRegionRule region, CampaignTimeTrigger highest, ref long work)
    {
        // DetectXCOMBase precedes baseHunting in the ten-minute fallthrough. A
        // previously undiscovered base may therefore become an assault target
        // after this preflight, even for an otherwise no-object wave.
        foreach (var owner in campaign.BaseStates)
        {
            work += region.Areas.Count + 1L;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            if (owner.RetaliationTarget || !WorldGeometry.InsideRegion(region, new(owner.Longitude, owner.Latitude))) continue;
            foreach (var saved in _ufos)
            {
                work += _sites.Count + (long)_missions.Count + _alienBases.Count + 1;
                if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                var scanner = UfoAfterTimedTransitions(saved, highest);
                if (!UfoCanScanBases(scanner, out var range) ||
                    WorldGeometry.Distance(new(owner.Longitude, owner.Latitude), scanner.Position) >= WorldGeometry.Nautical(range)) continue;
                work += owner.Facilities.Count;
                if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (!TryBaseDetectionChance(owner, highest, out var chance)) return "Base detection inputs are outside the supported range.";
                if (chance > 0) return "Immediate hunting retaliation requires base-discovery forecasts.";
            }
        }
        return null;
    }

    private void GenerateHuntingMission(AlienBaseSnapshot source, RuntimeAlienDeploymentRule deployment, string missionName)
    {
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.AlienMissions[rules.AlienMissions.GetRequired(missionName)].Value;
        var regionId = LocateGenerationRegion(source.Position)!;
        var region = rules.Regions[rules.Regions.GetRequired(regionId)].Value;
        if (region.MissionRegionId.Length != 0) regionId = region.MissionRegionId;
        var id = campaign.NextId("ALIEN_MISSIONS");
        var race = !deployment.HuntMissionRaceFromAlienBase && rule.HasRaceWeights
            ? WorldAlienMissionGeneration.Choose(WorldAlienMissionGeneration.WeightsAt(rule.RaceWeights,
                unchecked((ulong)campaign.MonthsPassed), race: true), campaign.Random) : source.Race;
        var area = -1;
        if (rule.Objective == RuntimeMissionObjective.Site)
        {
            var areas = rules.Regions[rules.Regions.GetRequired(regionId)].Value.MissionZones[rule.SpawnZone].Areas;
            if (areas.Count != 0) area = campaign.Random.NextInclusive(0, areas.Count - 1);
        }
        var countdown = WorldTrajectory.SpawnCountdown(checked((int)rule.Waves[0].SpawnTimer), campaign.Random);
        var created = new AlienMissionSnapshot(id, missionName, regionId, race, 0, 0, countdown, 0, area)
        { AlienBase = new(WorldTargetKind.AlienBase, deployment.MarkerName, source.Id, source.Longitude, source.Latitude) };
        var index = _missions.Count;
        _missions.Add(created);
        _missions[index] = ThinkMission(created, rule, index);
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
