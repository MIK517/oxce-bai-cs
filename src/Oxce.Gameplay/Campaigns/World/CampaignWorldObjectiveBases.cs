using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private int _objectiveBaseBirths;
    private int _objectiveBasesBeforeHunting;
    private int _objectiveBaseHuntingMissions;
    private List<(IReadOnlyList<SpawnArea> Positions, int Points)>? _objectiveBaseScores;

    private static bool CompletesBaseObjective(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, RuntimeMissionWave wave) =>
        rule.Objective == RuntimeMissionObjective.Base && !wave.ObjectiveOnTheLandingSite &&
        mission.NextWave + 1 == rule.Waves.Count && (ulong)mission.NextUfoCounter + 1 >= wave.UfoCount;

    private string? ObjectiveBaseSpawnReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        CampaignTimeTrigger highest, Dictionary<string, int> markers, ref long work)
    {
        try
        {
            var rules = campaign.Content.RuntimeRules;
            var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
            var areas = WorldGeometry.MissionAreas(region, rule.SpawnZone);
            long geometry = 1;
            foreach (var polygon in rules.Campaign.Globe.Polygons) geometry += polygon.Points.Count;
            work += 100L * (2 * geometry + region.Areas.Count + 1);
            return ReserveObjectiveBase(rule, areas, highest, markers, ref work);
        }
        catch (InvalidDataException error) { return error.Message; }
    }

    private string? ReserveObjectiveBase(RuntimeAlienMissionRule rule, IReadOnlyList<MissionArea> areas,
        CampaignTimeTrigger highest, Dictionary<string, int> reservations, ref long work, bool landing = false)
    {
        if (++_objectiveBaseBirths > 100_000 - _alienBases.Count)
            return "Base objective population exceeds the supported limit.";
        if (ulong.MaxValue - _alienBaseBirthSequence < (ulong)_objectiveBaseBirths)
            return "Base objective birth sequence is exhausted.";
        if (!landing) _objectiveBasesBeforeHunting++;
        var rules = campaign.Content.RuntimeRules;
        var markers = new HashSet<string>(StringComparer.Ordinal);
        var positions = new List<SpawnArea>();
        var canHunt = false;
        foreach (var area in areas)
        {
            work += 100L * (1 + (rule.SiteTypeId.Length != 0 ? 1 :
                rules.Campaign.Globe.Textures.TryGetValue(area.Texture, out var texture) ? 3L * texture.Deployments.Count : 1));
            if (work > MaximumAlienBaseChecks) return "Base objective creation exceeds the supported work limit.";
            if (!TrySpawnArea(area, out var position))
                return "Base objective coordinates require an unsupported latitude domain.";
            positions.Add(position);
            foreach (var candidate in ObjectiveBaseDeployments(rule, area))
            {
                var deployment = rules.AlienDeployments[candidate].Value;
                if (deployment.HuntMissionMaxFrequency < 0)
                    return "Base objective hunting timer cannot be negative.";
                if (!landing && NewObjectiveBaseHuntingReason(rules.AlienDeployments.GetExternalId(candidate), deployment, position, highest, ref work, ref canHunt) is { } huntingReason)
                    return huntingReason;
                var marker = deployment.MarkerName;
                if (string.IsNullOrWhiteSpace(marker) || marker == "ALIEN_MISSIONS" || IsUfoIdNamespace(marker) ||
                    marker is WorldTargetReference.WaypointType or WorldTargetReference.BaseType or "STR_ALIEN_TERROR")
                    return "Base objective uses an unsupported marker namespace.";
                markers.Add(marker);
            }
        }
        if (canHunt) _objectiveBaseHuntingMissions++;
        foreach (var marker in markers)
        {
            // Charge the production-owned projection for each shared marker.
            if (highest >= CampaignTimeTrigger.OneHour)
                foreach (var owner in campaign.BaseStates)
                    foreach (var production in owner.Productions)
                    {
                        var manufacture = rules.Manufacture[production.Rule].Value;
                        work += 1L + manufacture.ProducedMaterials.Count + manufacture.RequiredMaterials.Count;
                        if (work > MaximumAlienBaseChecks)
                            return "Base objective creation exceeds the supported work limit.";
                    }
            var count = reservations.GetValueOrDefault(marker) + 1;
            reservations[marker] = count;
            work += _alienBases.Count + (long)_sites.Count;
            foreach (var owner in campaign.BaseStates) work += owner.Crafts.Count + (long)owner.Transfers.Count;
            if (landing)
                work += (_siteBirthIdChanges?.Count ?? 0) + 2L * (_siteConversionCounterForecast?.Count ?? 0);
            // Counter forecasts include daily research; charge its scans too.
            if (highest >= CampaignTimeTrigger.OneDay)
            {
                foreach (var research in rules.Research.Rules)
                    work += (1L + research.Value.IncreaseCounters.Count + research.Value.DecreaseCounters.Count) * campaign.BaseStates.Count;
                foreach (var owner in campaign.BaseStates)
                    foreach (var project in owner.Research)
                    {
                        var research = rules.Research[project.Rule].Value;
                        work += 1L + research.GetOneFree.Count;
                        foreach (var group in research.GetOneFreeProtected) work += 1L + group.Topics.Count;
                    }
            }
            if (work > MaximumAlienBaseChecks) return "Base objective creation exceeds the supported work limit.";
            if (landing && _siteBirthIdChanges?.Any(change => change.Name == marker) == true &&
                _siteConversionCounterForecast?.Any(change => change.Change.Name == marker && !change.Change.Increase) == true)
                return "Landing base creation shares decreased marker counters with same-tick world births.";
            var range = campaign.CustomCounters.BeforeWorldCreation(marker, highest,
                landing ? _siteConversionCounterForecast : null, includeProduction: true);
            if (landing && _siteConversionCounterForecast?.Any(change => change.Change.Name == marker && !change.Change.Increase) == true)
            {
                // A later decrease may reuse the ID of a craft born this hour.
                // The snapshot craft list cannot detect that future collision.
                work += 1L;
                foreach (var owner in campaign.BaseStates)
                {
                    work += owner.Crafts.Count + (long)owner.Transfers.Count;
                    foreach (var production in owner.Productions)
                    {
                        var manufacture = rules.Manufacture[production.Rule].Value;
                        work += 1L + manufacture.ProducedMaterials.Count + manufacture.RequiredMaterials.Count;
                    }
                }
                if (work > MaximumAlienBaseChecks) return "Base objective creation exceeds the supported work limit.";
                if (campaign.ProductionPreflight.CraftCounter(marker, highest, (1, 1)).Maximum > 1)
                    return "Landing base creation shares decreased marker counters with same-tick craft production.";
            }
            var end = range.Maximum + count;
            if (range.Minimum <= 0 || end > int.MaxValue ||
                _alienBases.Any(existing => MarkerName(existing.DeploymentId) == marker && existing.Id >= range.Minimum && existing.Id < end) ||
                _sites.Any(existing => MarkerName(existing.DeploymentId) == marker && existing.Id >= range.Minimum && existing.Id < end) ||
                campaign.BaseStates.Any(owner => owner.Crafts.Any(craft => rules.Crafts.GetExternalId(craft.Rule) == marker &&
                    craft.Id >= range.Minimum && craft.Id < end) ||
                    owner.Transfers.Any(transfer => !transfer.Delivered && transfer.Craft is { } craft &&
                        craft.RuleId == marker && craft.Id >= range.Minimum && craft.Id < end)))
                return "Base objective IDs are exhausted or collide.";
            if (!landing) (_siteBirthIdChanges ??= []).Add(new(marker, true));
        }
        // Patrol discovery runs after these births, including delivered craft.
        foreach (var owner in campaign.BaseStates) work += owner.Crafts.Count + (long)owner.Transfers.Count;
        long scoreGeometry = campaign.RegionStates.Count + (long)campaign.CountryStates.Count;
        foreach (var state in campaign.RegionStates) scoreGeometry += rules.Regions[state.Rule].Value.Areas.Count;
        foreach (var state in campaign.CountryStates) scoreGeometry += rules.Countries[state.Rule].Value.Areas.Count;
        work += areas.Count * scoreGeometry;
        if (work > MaximumAlienBaseChecks) return "Base objective creation exceeds the supported work limit.";
        (_objectiveBaseScores ??= []).Add((positions, rule.Points));
        return null;
    }

    private RuleHandle<AlienDeploymentRuleFamily>[] ObjectiveBaseDeployments(RuntimeAlienMissionRule rule, MissionArea area)
    {
        var rules = campaign.Content.RuntimeRules;
        try
        {
            if (rule.SiteTypeId.Length != 0) return [rules.AlienDeployments.GetRequired(rule.SiteTypeId)];
            if (rules.Campaign.Globe.Textures.TryGetValue(area.Texture, out var texture) && texture.Deployments.Count != 0)
            {
                if (texture.Deployments.Count == 1) return [rules.AlienDeployments.GetRequired(texture.Deployments.First().Key)];
                if (WorldAlienMissionGeneration.Total(texture.Deployments) == 0)
                    throw new InvalidDataException("Base objective texture weights select an unavailable deployment.");
                return texture.Deployments.Where(pair => pair.Value != 0)
                    .Select(pair => rules.AlienDeployments.GetRequired(pair.Key)).ToArray();
            }
            return [rules.AlienDeployments.GetRequired("STR_ALIEN_BASE_ASSAULT")];
        }
        catch (KeyNotFoundException error) { throw new InvalidDataException("Base objective references an unavailable deployment.", error); }
    }

    private string? NewObjectiveBaseHuntingReason(string deploymentId, RuntimeAlienDeploymentRule deployment, SpawnArea area,
        CampaignTimeTrigger highest, ref long work, ref bool canGenerate)
    {
        if (highest < CampaignTimeTrigger.TenMinutes || deployment.BaseDetectionRange <= 0) return null;
        if (deployment.HuntMissionMaxFrequency > int.MaxValue - 10)
            return "New base objective hunting timer exceeds the supported range.";
        var rules = campaign.Content.RuntimeRules;
        var range = WorldGeometry.Nautical(deployment.BaseDetectionRange);
        var possibleGeneration = false;
        foreach (var owner in campaign.BaseStates)
        {
            work += owner.Crafts.Count + (highest >= CampaignTimeTrigger.OneHour ? (long)owner.Transfers.Count : 0);
            if (work > MaximumAlienBaseChecks) return "Base objective creation exceeds the supported work limit.";
            foreach (var craft in owner.Crafts)
            {
                if (craft.Logistics is not { } state || rules.Crafts[craft.Rule].Value.Undetectable) continue;
                var craftRule = rules.Crafts[craft.Rule].Value;
                var possible = false;
                if (state.Status == "STR_OUT")
                {
                    var projected = AdvanceCraftFuel(owner, craft, state);
                    possible = !projected.LowFuel && !projected.MissionComplete &&
                        projected.Damage < CraftLogistics.EffectiveDamageMaximum(projected, craftRule, rules) &&
                        Near(new(projected.Longitude, projected.Latitude));
                }
                else if (state.IsAutoPatrolling && craftRule.AutoPatrol)
                    possible = Near(new(state.Longitude, state.Latitude)) || Near(new(owner.Longitude, owner.Latitude));
                if (possible && ChoiceReason(ref work) is { } reason) return reason;
            }
            if (highest < CampaignTimeTrigger.OneHour) continue;
            foreach (var transfer in owner.Transfers)
            {
                if (transfer.Delivered || transfer.Hours > 1 ||
                    transfer.Craft?.Logistics is not { IsAutoPatrolling: true }) continue;
                var craft = rules.Crafts[rules.Crafts.GetRequired(transfer.Craft.RuleId)].Value;
                if (!craft.Undetectable && craft.AutoPatrol && Near(new(owner.Longitude, owner.Latitude)) &&
                    ChoiceReason(ref work) is { } reason) return reason;
            }
        }
        canGenerate |= possibleGeneration;
        return null;

        bool Near(WorldPosition position) => WorldGeometry.Distance(position, area.Center) - area.Radius < range;

        string? ChoiceReason(ref long checks)
        {
            if (deployment.BaseDetectionChance <= 0) return null;
            var choices = WorldAlienBaseHunting.Weights(deployment.HuntMissions, unchecked((ulong)campaign.MonthsPassed));
            checks += 2L * choices.Count;
            if (checks > MaximumAlienBaseChecks) return "Base objective creation exceeds the supported work limit.";
            _ = WorldAlienMissionGeneration.Total(choices);
            foreach (var choice in choices)
            {
                if (choice.Value == 0 || choice.Key.Length == 0) continue;
                if (!rules.AlienMissions.TryGet(choice.Key, out var handle))
                    return "Alien base hunting references an undefined mission.";
                var mission = rules.AlienMissions[handle].Value;
                if (area.Radius != 0 || mission.Waves.Count == 0 || mission.Waves[0].SpawnTimer / 30 / 2 <= 1)
                    return "New base objective hunting requires creation-tick mission forecasts.";
                var source = new AlienBaseSnapshot(0, deploymentId, "", area.Center.Longitude, area.Center.Latitude);
                if (HuntingMissionReason(source, deployment, mission, highest, ref checks, newborn: true) is { } reason)
                    return reason;
                possibleGeneration = true;
            }
            return null;
        }
    }

    private string? ObjectiveBasePatrolReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.TenMinutes || _objectiveBasesBeforeHunting == 0) return null;
        long possibleCrafts = 0;
        foreach (var owner in campaign.BaseStates) possibleCrafts += owner.Crafts.Count + (long)owner.Transfers.Count;
        return possibleCrafts * (_alienBases.Count + (long)_objectiveBasesBeforeHunting) > MaximumAlienBaseChecks
            ? "Base objective patrol discovery exceeds the supported work limit." : null;
    }

    private bool CreatesLandingBase(UfoSnapshot ufo, AlienMissionSnapshot mission, RuntimeUfoTrajectoryRule trajectory)
    {
        var rules = campaign.Content.RuntimeRules;
        var rule = MissionRule(mission);
        var wave = MissionWaveIndex(ufo, mission, rule);
        return (uint)wave < (uint)rule.Waves.Count && ufo.TrajectoryPoint + 1 < trajectory.Waypoints.Count &&
            trajectory.Altitude(ufo.TrajectoryPoint + 1) == 0 &&
            WorldObjectiveBaseSpawn.CreatesOnLanding(rule, rule.Waves[wave], trajectory.Zone(ufo.TrajectoryPoint),
                LandingAllowed(ufo.Destination!.Position, rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value),
                IsSiteConversion(ufo, mission, trajectory));
    }

    private string? LandingObjectiveBaseReason(UfoSnapshot saved, CampaignTimeTrigger highest,
        ref Dictionary<string, int>? reservations, ref long work)
    {
        var ufo = ProjectFlightTarget(UfoForFlight(saved, highest), highest);
        if (ufo.Hunting || ufo.Escorting || ufo.Status != UfoStatus.Flying || ufo.Destination is null ||
            WorldGeometry.Distance(ufo.Position, ufo.Destination.Position) > WorldGeometry.RadianSpeed(ufo.Speed)) return null;
        var mission = ForecastMission(ufo.MissionId, highest);
        if (mission.Interrupted) return null;
        var rules = campaign.Content.RuntimeRules;
        var trajectory = rules.UfoTrajectories[rules.UfoTrajectories.GetRequired(ufo.TrajectoryId)].Value;
        if (!CreatesLandingBase(ufo, mission, trajectory)) return null;
        try
        {
            var position = ufo.Destination.Position;
            // MissionArea's default texture is zero, rather than the arrival texture.
            return ReserveObjectiveBase(MissionRule(mission),
                [new(position.Longitude, position.Longitude, position.Latitude, position.Latitude, 0, "")],
                highest, reservations ??= new(StringComparer.Ordinal), ref work, landing: true);
        }
        catch (InvalidDataException error) { return error.Message; }
    }

    private void CompleteBaseObjective(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        if (WorldObjectiveBaseSpawn.Select(rules, rule, region, campaign.Random) is { } selected)
            SpawnMissionAlienBase(mission, rule, selected.Deployment, selected.Position);
    }

    private AlienBaseSnapshot SpawnMissionAlienBase(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        RuleHandle<AlienDeploymentRuleFamily> handle, WorldPosition raw, string pactCountry = "")
    {
        var rules = campaign.Content.RuntimeRules;
        var deployment = rules.AlienDeployments[handle].Value;
        var position = WorldPosition.Create(raw.Longitude, raw.Latitude);
        var alienBase = new AlienBaseSnapshot(campaign.NextId(deployment.MarkerName),
            rules.AlienDeployments.GetExternalId(handle), mission.Race, position.Longitude, position.Latitude)
        {
            PreservationKey = "alien-base:generated:" + checked(++_alienBaseBirthSequence)
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            StartMonth = campaign.MonthsPassed,
            PactCountryId = pactCountry,
            MinutesSinceLastHuntMissionGeneration = deployment.HuntMissionMaxFrequency,
        };
        _alienBases.Add(alienBase);
        if (rule.Objective != RuntimeMissionObjective.Infiltration) ScoreUfoActivity(position, rule.Points, rules);
        return alienBase;
    }
}
