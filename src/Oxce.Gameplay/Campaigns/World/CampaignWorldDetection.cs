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
        // Marker capacity is reserved for every contact that could be made, whatever the
        // radar coverage: only exhausted ID ranges can block, and they never occur in play.
        var possibleMarkerIds = 0;
        var possibleLandingIds = 0;
        foreach (var ufo in _ufos)
        {
            if (ufo.Status is not (UfoStatus.Flying or UfoStatus.Landed)) continue;
            var mission = FindMission(ufo.MissionId);
            if (mission is null)
                return "UFO mission link requires world simulation.";
            var rule = campaign.Content.RuntimeRules.Ufos[
                campaign.Content.RuntimeRules.Ufos.GetRequired(ufo.RuleId)].Value;
            // GeoscapeState::time30Minutes skips instant retaliation UFOs before
            // activity, detection, and hidden-contact accounting.
            if (halfHour && !IsInstantRetaliation(mission))
            {
                var reason = CheckHalfHourUfo(ufo.RuleId, ufo.Position, regionTotals, countryTotals,
                    ufo.Status == UfoStatus.Landed ? 2 : 1);
                if (reason is not null) return reason;
                if (!ufo.Detected && !rule.NoAlert)
                {
                    if (ufo.Id == 0) possibleMarkerIds++;
                    if (ufo.Status == UfoStatus.Landed && ufo.LandId == 0) possibleLandingIds++;
                }
            }
            // Half-hour detection precedes landing in this tick. Even a noAlert UFO
            // can become detected here and receive a landing marker on arrival.
            if (ufo.LandId == 0 && (ufo.Detected || halfHour) && ArrivesOnGround(ufo) &&
                LandingAllowed(ufo.Destination!.Position, rule))
                possibleLandingIds++;
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
            var reason = CheckHalfHourUfo(wave.UfoType, position, regionTotals, countryTotals);
            if (reason is not null) return reason;
            if (!rules.Ufos[wave.Ufo!.Value].Value.NoAlert) possibleMarkerIds++;
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

    /// <summary>The detection-script gate and activity range for one half-hour UFO.</summary>
    private string? CheckHalfHourUfo(string ruleId, WorldPosition position,
        long[] regionTotals, long[] countryTotals, int scoreMultiplier = 1)
    {
        var rules = campaign.Content.RuntimeRules;
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ruleId)].Value;
        if (ufoRule.Scripts.Count != 0)
            return "UFO detection scripts require world simulation.";
        var score = (long)ufoRule.MissionScore * scoreMultiplier;
        if (score is < int.MinValue or > int.MaxValue)
            return "UFO alien activity exceeds the supported range.";
        return CheckActivityScore(position, (int)score, regionTotals, countryTotals);
    }

    // Completed base radars, projected once per half-hour and reused across boundaries.
    private readonly List<WorldRadarFacility> _baseRadars = [];
    private readonly List<int> _baseRadarEnds = [];
    private Func<int, bool>? _percent;

    private void ProjectBaseRadars()
    {
        _baseRadars.Clear();
        _baseRadarEnds.Clear();
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
        {
            foreach (var facility in owner.Facilities)
            {
                if (facility.BuildTime != 0) continue;
                var rule = rules.Facilities[facility.Rule].Value;
                _baseRadars.Add(new WorldRadarFacility(rule.RadarRange, rule.RadarChance, rule.Hyperwave));
            }
            _baseRadarEnds.Add(_baseRadars.Count);
        }
    }

    private void ProcessUfoHalfHour(CampaignState.TimeEffects effects)
    {
        var radarsProjected = false;
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
                // Refuelling and auto-patrol relaunch have already run in this half-hour.
                craftRadars = [];
                CollectCraftRadars(craftRadars);
            }
            var rules = campaign.Content.RuntimeRules;
            var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
            ScoreUfoActivity(ufo.Position, checked(ufoRule.MissionScore * (ufo.Status == UfoStatus.Landed ? 2 : 1)), rules);
            if (!radarsProjected)
            {
                ProjectBaseRadars();
                radarsProjected = true;
            }
            var detected = DetectUfoFromBases(ufo, ufoRule);
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

    private UfoDetectionResult DetectUfoFromBases(UfoSnapshot ufo, RuntimeUfoRule ufoRule)
    {
        var detected = UfoDetectionResult.None;
        var visibility = WorldAltitudes.Visibility(ufoRule.DefaultVisibility, ufo.Altitude);
        var radars = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_baseRadars);
        var percent = _percent ??= Percent;
        var start = 0;
        for (var index = 0; index < campaign.BaseStates.Count; index++)
        {
            var owner = campaign.BaseStates[index];
            var end = _baseRadarEnds[index];
            var distance = WorldGeometry.XcomDistance(WorldGeometry.Distance(
                new WorldPosition(owner.Longitude, owner.Latitude), ufo.Position));
            var (type, chance) = WorldDetection.BaseDetection(radars[start..end], distance, visibility,
                ufo.Detected, percent);
            if (Percent(chance)) detected |= type;
            start = end;
        }
        return detected;
    }

    private UfoDetectionResult DetectUfoFromCrafts(UfoSnapshot ufo, RuntimeUfoRule ufoRule,
        List<WorldCraftRadar> crafts)
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

    /// <summary>GeoscapeState::updateActiveCrafts: airborne crafts that are not destroyed.</summary>
    private void CollectCraftRadars(List<WorldCraftRadar> sources)
    {
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
        {
            foreach (var craft in owner.Crafts)
            {
                if (craft.Logistics is not { Status: "STR_OUT" } state) continue;
                var rule = rules.Crafts[craft.Rule].Value;
                if (CraftLogistics.IsDestroyed(state, rule, rules)) continue;
                CraftLogistics.EffectiveRadar(rule, state.Weapons, rules, out var range, out var chance);
                sources.Add(new WorldCraftRadar(new WorldPosition(state.Longitude, state.Latitude), range, chance));
            }
        }
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
