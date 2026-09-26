using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// The ordinary flying/landed UFO portion of GeoscapeState::time30Minutes: alien activity
/// and Base::detect/Craft::detect. Detection scripts remain a preflight gate.
/// </summary>
internal sealed partial class CampaignWorld
{
    private void RegisterUfoDetection(CampaignCapabilityRegistry registry) =>
        registry.Timed(CampaignTimeTrigger.ThirtyMinutes, CampaignTimeOrder.ThirtyMinutesWorldUfoDetection,
            "UFO activity and base detection", ProcessUfoHalfHour);

    private string? UfoHalfHourReason(CampaignTimeTrigger highest)
    {
        var halfHour = highest >= CampaignTimeTrigger.ThirtyMinutes;
        var hasUfoWork = false;
        foreach (var ufo in _ufos)
        {
            if (halfHour && ufo.Status is UfoStatus.Flying or UfoStatus.Landed ||
                ufo.Status == UfoStatus.Landed && ufo.SecondsRemaining == 5 ||
                ufo.Detected && ufo.LandId == 0 && ArrivesOnGround(ufo))
            {
                hasUfoWork = true;
                break;
            }
        }
        var hasDueWave = false;
        foreach (var mission in _missions)
        {
            if (!halfHour) break;
            if (mission.Interrupted || mission.SpawnCountdown > 30) continue;
            hasDueWave = true;
            break;
        }
        if (!hasUfoWork && !hasDueWave) return null;
        var regionTotals = campaign.RegionStates.Select(static state => (long)state.ActivityAlien[^1]).ToArray();
        var countryTotals = campaign.CountryStates.Select(static state => (long)state.ActivityAlien[^1]).ToArray();
        var possibleMarkerIds = 0;
        var possibleLandingIds = 0;
        List<WorldCraftRadar> craftRadars = [];
        var craftRadarsProjected = false;
        foreach (var ufo in _ufos)
        {
            if (ufo.Status is not (UfoStatus.Flying or UfoStatus.Landed)) continue;
            var mission = FindMission(ufo.MissionId);
            if (mission is null)
                return "UFO mission link requires world simulation.";
            // GeoscapeState::time30Minutes skips instant retaliation UFOs before
            // activity, detection, and hidden-contact accounting.
            var canDetect = false;
            var rule = campaign.Content.RuntimeRules.Ufos[
                campaign.Content.RuntimeRules.Ufos.GetRequired(ufo.RuleId)].Value;
            if (halfHour && !IsInstantRetaliation(mission))
            {
                if (!craftRadarsProjected)
                {
                    var craftReason = CollectCraftRadars(craftRadars, forecastRefuelling: true, highest);
                    if (craftReason is not null) return craftReason;
                    craftRadarsProjected = true;
                }
                var reason = CheckHalfHourUfo(ufo.RuleId, ufo.Position, ufo.Altitude,
                    regionTotals, countryTotals, craftRadars, highest, out canDetect,
                    ufo.Status == UfoStatus.Landed ? 2 : 1);
                if (reason is not null) return reason;
                if (canDetect && !ufo.Detected && !rule.NoAlert)
                {
                    if (ufo.Id == 0) possibleMarkerIds++;
                    if (ufo.Status == UfoStatus.Landed && ufo.LandId == 0) possibleLandingIds++;
                }
            }
            // Half-hour detection precedes landing in this tick. Even a noAlert UFO
            // can become detected here and receive a landing marker on arrival.
            if (ufo.LandId == 0 && (ufo.Detected || canDetect) && ArrivesOnGround(ufo) &&
                LandingAllowed(ufo.Destination!.Position, rule)) possibleLandingIds++;
        }
        // AlienMission::think runs before the scoring and detection pass. Check its
        // possible UFOs using their fixed first point without drawing RNG in preflight.
        foreach (var mission in _missions)
        {
            if (!halfHour) break;
            var rules = campaign.Content.RuntimeRules;
            var missionRule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (IsOver(mission, missionRule) || mission.Interrupted ||
                mission.MultiUfoRetaliationInProgress && !missionRule.MultiUfoRetaliationExtra ||
                mission.NextWave >= missionRule.Waves.Count || mission.SpawnCountdown > 30 ||
                !CanSpawnOrdinaryUfoWave(mission, missionRule))
                continue;
            var wave = missionRule.Waves[mission.NextWave];
            var trajectory = rules.UfoTrajectories[wave.Trajectory!.Value].Value;
            var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
            if (!TryGetFixedWaypoint(region, trajectory.Zone(0), out var position))
                return "Alien mission wave spawning requires world simulation.";
            if (!craftRadarsProjected)
            {
                var craftReason = CollectCraftRadars(craftRadars, forecastRefuelling: true, highest);
                if (craftReason is not null) return craftReason;
                craftRadarsProjected = true;
            }
            var reason = CheckHalfHourUfo(wave.UfoType, position,
                WorldAltitudes.All[trajectory.Altitude(0)], regionTotals, countryTotals, craftRadars, highest,
                out var canDetect);
            if (reason is not null) return reason;
            if (canDetect && !rules.Ufos[wave.Ufo!.Value].Value.NoAlert) possibleMarkerIds++;
        }
        // time5Seconds applies takeoff mission points AFTER all half-hour scoring.
        // Share these totals so individually safe awards cannot overflow together.
        foreach (var ufo in _ufos)
        {
            if (ufo.Status != UfoStatus.Landed || ufo.SecondsRemaining != 5) continue;
            var mission = FindMission(ufo.MissionId)!;
            var rules = campaign.Content.RuntimeRules;
            var points = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value.Points;
            if (points > 0 && CheckActivityScore(ufo.Position, points, regionTotals, countryTotals) is { } reason)
                return reason;
        }
        if (!CanAllocateUfoMarkerIds(possibleMarkerIds))
            return "UFO contact marker IDs are exhausted or collide with a saved UFO.";
        return CanAllocateUfoMarkerIds(possibleLandingIds, landing: true)
            ? null : "UFO landing marker IDs are exhausted or collide with a saved UFO.";
    }

    private bool CanAllocateUfoMarkerIds(int count, bool landing = false)
    {
        if (count == 0) return true;
        var first = campaign.PeekNextId(landing ? "STR_LANDING_SITE" : "STR_UFO");
        var afterLast = (long)first + count;
        if (afterLast > int.MaxValue) return false;
        foreach (var ufo in _ufos)
            if ((landing ? ufo.LandId : ufo.Id) >= first && (landing ? ufo.LandId : ufo.Id) < afterLast)
                return false;
        return true;
    }

    private string? CheckHalfHourUfo(string ruleId, WorldPosition position, string altitude,
        long[] regionTotals, long[] countryTotals, IReadOnlyList<WorldCraftRadar> craftRadars,
        CampaignTimeTrigger highest, out bool canDetect, int scoreMultiplier = 1)
    {
        canDetect = false;
        var rules = campaign.Content.RuntimeRules;
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ruleId)].Value;
        if (ufoRule.Scripts.Count != 0)
            return "UFO detection scripts require world simulation.";
        if (ufoRule.DefaultVisibility is < -100 or > 100)
            return "UFO detection visibility is outside the supported range.";
        var score = (long)ufoRule.MissionScore * scoreMultiplier;
        if (score is < int.MinValue or > int.MaxValue)
            return "UFO alien activity exceeds the supported range.";
        if (CheckActivityScore(position, (int)score, regionTotals, countryTotals) is { } scoreReason)
            return scoreReason;
        var visibility = WorldAltitudes.Visibility(ufoRule.DefaultVisibility, altitude);
        foreach (var owner in campaign.BaseStates)
        {
            var distance = WorldGeometry.XcomDistance(WorldGeometry.Distance(
                new WorldPosition(owner.Longitude, owner.Latitude), position));
            var radarChance = 0;
            var hyperwaveChance = 0;
            foreach (var facility in owner.Facilities)
            {
                if (facility.BuildTime != 0 &&
                    !(highest >= CampaignTimeTrigger.OneDay && facility.BuildTime == 1)) continue;
                var radar = rules.Facilities[facility.Rule].Value;
                if (radar.RadarChance is < 0 or > 100)
                    return "Base radar chance is outside the supported range.";
                if (radar.RadarRange < distance) continue;
                if (radar.Hyperwave) hyperwaveChance = checked(hyperwaveChance + radar.RadarChance);
                else radarChance = checked(radarChance + radar.RadarChance);
            }
            _ = WorldDetection.DetectionChance(radarChance, visibility);
            _ = WorldDetection.DetectionChance(hyperwaveChance, visibility);
            if (hyperwaveChance > 0 || WorldDetection.DetectionChance(radarChance, visibility) > 0)
                canDetect = true;
        }
        foreach (var craft in craftRadars)
        {
            var distance = WorldGeometry.XcomDistance(WorldGeometry.Distance(craft.Position, position));
            var (_, chance) = WorldDetection.CraftDetection(craft.Range, craft.Chance,
                distance, visibility, false);
            if (chance > 0) canDetect = true;
        }
        return null;
    }

    private void ProcessUfoHalfHour(CampaignState.TimeEffects effects)
    {
        List<WorldRadarFacility>? radars = null;
        List<WorldCraftRadar>? craftRadars = null;
        for (var index = 0; index < _ufos.Count; index++)
        {
            var ufo = _ufos[index];
            if (ufo.Status is not (UfoStatus.Flying or UfoStatus.Landed)) continue;
            var mission = FindMission(ufo.MissionId) ??
                throw new InvalidOperationException("UFO mission link changed after world preflight.");
            if (IsInstantRetaliation(mission)) continue;
            if (craftRadars is null)
            {
                craftRadars = [];
                if (CollectCraftRadars(craftRadars, forecastRefuelling: false,
                        CampaignTimeTrigger.FiveSeconds) is { } reason)
                    throw new InvalidOperationException(reason);
            }
            var rules = campaign.Content.RuntimeRules;
            var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
            ScoreUfoActivity(ufo.Position, checked(ufoRule.MissionScore * (ufo.Status == UfoStatus.Landed ? 2 : 1)), rules);
            var detected = DetectUfoFromBases(ufo, ufoRule, rules,
                radars ??= new List<WorldRadarFacility>());
            detected |= DetectUfoFromCrafts(ufo, ufoRule, craftRadars);
            if (!ufo.Detected && (detected & UfoDetectionResult.Radar) != 0)
            {
                var hyperwave = (detected & UfoDetectionResult.Hyperwave) == UfoDetectionResult.Hyperwave;
                var markerId = !ufoRule.NoAlert && ufo.Id == 0 ? campaign.NextId("STR_UFO") : ufo.Id;
                var landingId = !ufoRule.NoAlert && ufo.Status == UfoStatus.Landed && ufo.LandId == 0
                    ? campaign.NextId("STR_LANDING_SITE") : ufo.LandId;
                _ufos[index] = ufo with { Id = markerId, LandId = landingId, Detected = true, HyperDetected = hyperwave };
                if (!ufoRule.NoAlert) effects.Notify(new UfoContactDetected(ufo.UniqueId, hyperwave));
            }
            else if (ufo.Detected && detected == UfoDetectionResult.None)
                _ufos[index] = ufo with { Detected = false, HyperDetected = false };
            else if (ufo.Detected && detected == UfoDetectionResult.Hyperwave && !ufo.HyperDetected)
                _ufos[index] = ufo with { HyperDetected = true };
        }
    }

    private string? CheckActivityScore(WorldPosition position, int score, long[] regionTotals, long[] countryTotals)
    {
        var rules = campaign.Content.RuntimeRules;
        for (var index = 0; index < campaign.RegionStates.Count; index++)
        {
            var region = campaign.RegionStates[index];
            if (!WorldGeometry.InsideRegion(rules.Regions[region.Rule].Value, position)) continue;
            regionTotals[index] += score;
            if (regionTotals[index] is < int.MinValue or > int.MaxValue)
                return "UFO alien activity exceeds the supported range.";
            break;
        }
        for (var index = 0; index < campaign.CountryStates.Count; index++)
        {
            var country = campaign.CountryStates[index];
            if (!WorldGeometry.InsideCountry(rules.Countries[country.Rule].Value, position)) continue;
            countryTotals[index] += score;
            if (countryTotals[index] is < int.MinValue or > int.MaxValue)
                return "UFO alien activity exceeds the supported range.";
            break;
        }
        return null;
    }

    private void ScoreUfoActivity(WorldPosition position, int score, RuntimeRuleCatalog rules)
    {
        foreach (var region in campaign.RegionStates)
            if (WorldGeometry.InsideRegion(rules.Regions[region.Rule].Value, position))
            {
                region.ActivityAlien[^1] = checked(region.ActivityAlien[^1] + score);
                break;
            }
        foreach (var country in campaign.CountryStates)
            if (WorldGeometry.InsideCountry(rules.Countries[country.Rule].Value, position))
            {
                country.ActivityAlien[^1] = checked(country.ActivityAlien[^1] + score);
                break;
            }
    }

    private UfoDetectionResult DetectUfoFromBases(UfoSnapshot ufo, RuntimeUfoRule ufoRule,
        RuntimeRuleCatalog rules, List<WorldRadarFacility> radars)
    {
        var detected = UfoDetectionResult.None;
        var visibility = WorldAltitudes.Visibility(ufoRule.DefaultVisibility, ufo.Altitude);
        foreach (var owner in campaign.BaseStates)
        {
            var distance = WorldGeometry.XcomDistance(WorldGeometry.Distance(
                new WorldPosition(owner.Longitude, owner.Latitude), ufo.Position));
            radars.Clear();
            foreach (var facility in owner.Facilities)
            {
                if (facility.BuildTime != 0) continue;
                var rule = rules.Facilities[facility.Rule].Value;
                radars.Add(new WorldRadarFacility(rule.RadarRange, rule.RadarChance, rule.Hyperwave));
            }
            var (type, chance) = WorldDetection.BaseDetection(radars, distance, visibility,
                ufo.Detected, Percent);
            if (Percent(chance)) detected |= type;
        }
        return detected;
    }

    private UfoDetectionResult DetectUfoFromCrafts(UfoSnapshot ufo, RuntimeUfoRule ufoRule,
        IReadOnlyList<WorldCraftRadar> crafts)
    {
        var detected = UfoDetectionResult.None;
        var visibility = WorldAltitudes.Visibility(ufoRule.DefaultVisibility, ufo.Altitude);
        foreach (var craft in crafts)
        {
            var distance = WorldGeometry.XcomDistance(WorldGeometry.Distance(craft.Position, ufo.Position));
            var (type, chance) = WorldDetection.CraftDetection(craft.Range, craft.Chance,
                distance, visibility, ufo.Detected);
            if (Percent(chance)) detected |= type;
        }
        return detected;
    }

    private string? CollectCraftRadars(List<WorldCraftRadar> sources, bool forecastRefuelling,
        CampaignTimeTrigger highest)
    {
        foreach (var owner in campaign.BaseStates)
        {
            // The reference refuels in craft order, then relaunches ready auto-patrols
            // before updateActiveCrafts. Project shared item consumption in that order.
            Dictionary<RuleHandle<ItemRuleFamily>, int>? remainingItems = null;
            foreach (var craft in owner.Crafts)
            {
                var state = craft.Logistics;
                if (state is null) continue;
                // Hourly Rearm changes an already rearmed craft to REFUELLING;
                // that refuel can relaunch auto-patrol in the same tick.
                if (forecastRefuelling && highest >= CampaignTimeTrigger.OneHour &&
                    state.Status == "STR_REARMING" && !state.Weapons.Any(static weapon => weapon is { Rearming: true }))
                    state = state with { Status = "STR_REFUELLING" };
                var active = state.Status == "STR_OUT";
                if (forecastRefuelling && state.Status == "STR_REFUELLING")
                {
                    var refuelRule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                    var available = refuelRule.RefuelItem is { } item
                        ? (remainingItems ?? owner.Items).GetValueOrDefault(item) : 0;
                    var refuel = CraftLogistics.Refuel(state, refuelRule,
                        campaign.Content.RuntimeRules, available);
                    if (refuelRule.RefuelItem is { } fuelItem && refuel.FuelItemChange != 0)
                    {
                        remainingItems ??= new Dictionary<RuleHandle<ItemRuleFamily>, int>(owner.Items);
                        var quantity = checked(remainingItems.GetValueOrDefault(fuelItem) + refuel.FuelItemChange);
                        if (quantity == 0) remainingItems.Remove(fuelItem);
                        else remainingItems[fuelItem] = quantity;
                    }
                    active = !refuel.MissingFuel && refuel.State.Status == "STR_READY" &&
                        refuel.State.IsAutoPatrolling && refuelRule.AutoPatrol;
                }
                // Reserve for a possible launch only when an earlier handler can
                // supply this fuel item. A READY craft also needs reuseItem to
                // move it back into REFUELLING.
                if (forecastRefuelling && !active && highest >= CampaignTimeTrigger.OneHour &&
                    state.Status is "STR_READY" or "STR_REFUELLING" && state.IsAutoPatrolling)
                {
                    var possibleRule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                    if (possibleRule.AutoPatrol && possibleRule.RefuelItem is { } fuelItem &&
                        state.Fuel < CraftLogistics.EffectiveFuelMaximum(possibleRule, state.Weapons,
                            campaign.Content.RuntimeRules) &&
                        campaign.MayReceiveRefuelItem(owner, fuelItem, highest,
                            requiresReuse: state.Status == "STR_READY"))
                    {
                        var candidate = CraftLogistics.Refuel(state with { Status = "STR_REFUELLING" },
                            possibleRule, campaign.Content.RuntimeRules, 1);
                        active = !candidate.MissingFuel && candidate.State.Status == "STR_READY";
                    }
                }
                if (!active) continue;
                var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
                var position = new WorldPosition(state.Longitude, state.Latitude);
                if (AddCraftRadar(sources, rule, state, position) is { } reason) return reason;
            }
        }
        return null;
    }

    private string? AddCraftRadar(List<WorldCraftRadar> sources, RuntimeCraftRule rule,
        CraftLogisticsState state, WorldPosition position)
    {
        var supported = CraftLogistics.TryEffectiveDetectionStats(rule, state.Weapons,
            campaign.Content.RuntimeRules, out var damageMaximum, out var range, out var chance);
        if (state.Damage >= damageMaximum) return null; // updateActiveCrafts excludes destroyed crafts.
        if (!supported) return "Craft radar stats are outside the supported range.";
        if (!position.IsNormalized) return "Airborne craft coordinates are invalid.";
        sources.Add(new WorldCraftRadar(position, range, chance));
        return null;
    }

    private readonly record struct WorldCraftRadar(WorldPosition Position, int Range, int Chance);

    private bool Percent(int chance) => chance >= 100 || chance > 0 && campaign.Random.NextInclusive(0, 99) < chance;

    private bool IsInstantRetaliation(AlienMissionSnapshot mission)
    {
        var rules = campaign.Content.RuntimeRules.AlienMissions;
        return rules[rules.GetRequired(mission.RuleId)].Value.Objective ==
            RuntimeMissionObjective.InstantRetaliation;
    }
}
