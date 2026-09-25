using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// The ordinary flying-UFO portion of GeoscapeState::time30Minutes: alien activity
/// and Base::detect. Craft radar and detection scripts remain preflight gates.
/// </summary>
internal sealed partial class CampaignWorld
{
    private void RegisterUfoDetection(CampaignCapabilityRegistry registry) =>
        registry.Timed(CampaignTimeTrigger.ThirtyMinutes, CampaignTimeOrder.ThirtyMinutesWorldUfoDetection,
            "UFO activity and base detection", ProcessUfoHalfHour);

    private string? UfoHalfHourReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes) return null;
        var hasFlyingUfo = false;
        foreach (var ufo in _ufos)
        {
            if (ufo.Status != UfoStatus.Flying) continue;
            hasFlyingUfo = true;
            break;
        }
        var hasDueWave = false;
        foreach (var mission in _missions)
        {
            if (mission.Interrupted || mission.SpawnCountdown > 30) continue;
            hasDueWave = true;
            break;
        }
        if (!hasFlyingUfo && !hasDueWave) return null;
        var regionTotals = campaign.RegionStates.Select(static state => (long)state.ActivityAlien[^1]).ToArray();
        var countryTotals = campaign.CountryStates.Select(static state => (long)state.ActivityAlien[^1]).ToArray();
        var possibleMarkerIds = 0;
        foreach (var ufo in _ufos)
        {
            if (ufo.Status != UfoStatus.Flying) continue;
            if (!_missions.Exists(mission => mission.Id == ufo.MissionId))
                return "UFO mission link requires world simulation.";
            var reason = CheckHalfHourUfo(ufo.RuleId, ufo.Position, ufo.Altitude,
                regionTotals, countryTotals, highest, out var canDetect);
            if (reason is not null) return reason;
            var rule = campaign.Content.RuntimeRules.Ufos[
                campaign.Content.RuntimeRules.Ufos.GetRequired(ufo.RuleId)].Value;
            if (canDetect && !ufo.Detected && ufo.Id == 0 && !rule.NoAlert) possibleMarkerIds++;
        }
        // AlienMission::think runs before the scoring and detection pass. Check its
        // possible UFOs using their fixed first point without drawing RNG in preflight.
        foreach (var mission in _missions)
        {
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
            var reason = CheckHalfHourUfo(wave.UfoType, position,
                WorldAltitudes.All[trajectory.Altitude(0)], regionTotals, countryTotals, highest,
                out var canDetect);
            if (reason is not null) return reason;
            if (canDetect && !rules.Ufos[wave.Ufo!.Value].Value.NoAlert) possibleMarkerIds++;
        }
        return CanAllocateUfoMarkerIds(possibleMarkerIds)
            ? null : "UFO contact marker IDs are exhausted or collide with a saved UFO.";
    }

    private bool CanAllocateUfoMarkerIds(int count)
    {
        if (count == 0) return true;
        var first = campaign.PeekNextId("STR_UFO");
        var afterLast = (long)first + count;
        if (afterLast > int.MaxValue) return false;
        foreach (var ufo in _ufos)
            if (ufo.Id >= first && ufo.Id < afterLast)
                return false;
        return true;
    }

    private string? CheckHalfHourUfo(string ruleId, WorldPosition position, string altitude,
        long[] regionTotals, long[] countryTotals, CampaignTimeTrigger highest, out bool canDetect)
    {
        canDetect = false;
        var rules = campaign.Content.RuntimeRules;
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ruleId)].Value;
        if (ufoRule.Scripts.Count != 0)
            return "UFO detection scripts require world simulation.";
        if (!SpawnBoundaryHasNoActiveCrafts())
            return "Craft UFO detection requires world simulation.";
        if (ufoRule.DefaultVisibility is < -100 or > 100)
            return "UFO detection visibility is outside the supported range.";
        var score = ufoRule.MissionScore;
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
            var visibility = WorldAltitudes.Visibility(ufoRule.DefaultVisibility, altitude);
            _ = WorldDetection.DetectionChance(radarChance, visibility);
            _ = WorldDetection.DetectionChance(hyperwaveChance, visibility);
            if (hyperwaveChance > 0 || WorldDetection.DetectionChance(radarChance, visibility) > 0)
                canDetect = true;
        }
        return null;
    }

    private void ProcessUfoHalfHour(CampaignState.TimeEffects effects)
    {
        List<WorldRadarFacility>? radars = null;
        for (var index = 0; index < _ufos.Count; index++)
        {
            var ufo = _ufos[index];
            if (ufo.Status != UfoStatus.Flying) continue;
            var rules = campaign.Content.RuntimeRules;
            var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
            ScoreUfoActivity(ufo.Position, ufoRule.MissionScore, rules);
            var detected = DetectUfoFromBases(ufo, ufoRule, rules,
                radars ??= new List<WorldRadarFacility>());
            if (!ufo.Detected && (detected & UfoDetectionResult.Radar) != 0)
            {
                var hyperwave = (detected & UfoDetectionResult.Hyperwave) == UfoDetectionResult.Hyperwave;
                var markerId = !ufoRule.NoAlert && ufo.Id == 0 ? campaign.NextId("STR_UFO") : ufo.Id;
                _ufos[index] = ufo with { Id = markerId, Detected = true, HyperDetected = hyperwave };
                if (!ufoRule.NoAlert) effects.Notify(new UfoContactDetected(ufo.UniqueId, hyperwave));
            }
            else if (ufo.Detected && detected == UfoDetectionResult.None)
                _ufos[index] = ufo with { Detected = false, HyperDetected = false };
            else if (ufo.Detected && detected == UfoDetectionResult.Hyperwave && !ufo.HyperDetected)
                _ufos[index] = ufo with { HyperDetected = true };
        }
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

    private bool Percent(int chance) => chance >= 100 || chance > 0 && campaign.Random.NextInclusive(0, 99) < chance;
}
