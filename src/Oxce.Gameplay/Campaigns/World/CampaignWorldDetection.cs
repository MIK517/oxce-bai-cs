using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// The ordinary flying/landed UFO portion of GeoscapeState::time30Minutes: alien activity
/// and Base::detect/Craft::detect. Detection scripts execute through the bounded VM caller.
/// </summary>
internal sealed partial class CampaignWorld
{
    private void RegisterUfoDetection(CampaignCapabilityRegistry registry)
    {
        registry.Command<IgnoreUfoContact>(IgnoreContact);
        registry.Timed(CampaignTimeTrigger.ThirtyMinutes, CampaignTimeOrder.ThirtyMinutesWorldUfoDetection,
            "UFO activity and base detection", ProcessUfoHalfHour);
    }

    private string? UfoHalfHourReason(CampaignTimeTrigger highest, IReadOnlySet<string>? guaranteedResearch)
    {
        var halfHour = highest >= CampaignTimeTrigger.ThirtyMinutes;
        long detectionWork = 0;
        var detectionCraftSources = -1;
        var detectionWeaponSlots = 0;
        var hasUfoWork = false;
        foreach (var ufo in _ufos)
        {
            if (ArrivesAtSite(ufo, highest) || halfHour && ufo.Status is UfoStatus.Flying or UfoStatus.Landed ||
                ufo.Status == UfoStatus.Landed && ufo.SecondsRemaining == 5 ||
                ufo.Detected && ufo.LandId == 0 && ArrivesOnGround(ufo, highest))
            {
                hasUfoWork = true;
                break;
            }
        }
        var hasDueWave = false;
        foreach (var savedMission in _missions)
        {
            if (!halfHour) break;
            var mission = MissionAfterDailyResearch(savedMission, guaranteedResearch);
            if (mission.Interrupted || mission.SpawnCountdown > 30) continue;
            hasDueWave = true;
            break;
        }
        var dailyBases = highest >= CampaignTimeTrigger.OneDay && _alienBases.Count != 0;
        if (!hasUfoWork && !hasDueWave && !(halfHour && _sites.Count != 0) && !dailyBases && _objectiveBaseScores is not { Count: > 0 }) return null;
        var regionTotals = campaign.RegionStates.Select(static state => new ActivityRange(state.ActivityAlien[^1])).ToArray();
        var countryTotals = campaign.CountryStates.Select(static state => new ActivityRange(state.ActivityAlien[^1])).ToArray();
        if (dailyBases)
            foreach (var alienBase in _alienBases)
                if (!BaseDestroyedAfterDailyResearch(alienBase, guaranteedResearch) && CheckActivityScore(alienBase.Position, AlienBaseRule(alienBase).Points,
                        regionTotals, countryTotals) is { } reason)
                    return reason;
        if (_objectiveBaseScores is not null)
            foreach (var birth in _objectiveBaseScores)
                if (CheckPossibleActivity(birth.Positions, birth.Points, regionTotals, countryTotals, guaranteedAward: false) is { } reason)
                    return reason;
        // Marker capacity is reserved for every contact that could be made, whatever the
        // radar coverage: only exhausted ID ranges can block, and they never occur in play.
        var possibleMarkerIds = 0;
        var possibleLandingIds = 0;
        foreach (var ufo in _ufos)
        {
            if (ufo.Status == UfoStatus.IgnoreMe)
            {
                var lifted = UfoAfterTimedTransitions(ufo, highest);
                // This UFO misses the half-hour detection pass, but can land in the
                // later five-second flight pass using its existing detected flag.
                if (lifted.Status == UfoStatus.Flying && lifted.Detected && lifted.LandId == 0 &&
                    ArrivesOnGround(lifted, highest) && LandingAllowed(lifted.Destination!.Position,
                        campaign.Content.RuntimeRules.Ufos[
                            campaign.Content.RuntimeRules.Ufos.GetRequired(lifted.RuleId)].Value))
                    possibleLandingIds++;
                continue;
            }
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
                if (ReserveDetectionScriptWork(ufo.RuleId, highest, ref detectionWork, ref detectionCraftSources,
                        ref detectionWeaponSlots) is { } scriptWorkReason) return scriptWorkReason;
                if (!ufo.Detected && !rule.NoAlert && !_ignoredUfoContacts.Contains(ufo.Id))
                {
                    if (ufo.Id == 0 && !PlannedHuntMarker(ufo)) possibleMarkerIds++;
                    if (ufo.Status == UfoStatus.Landed && ufo.LandId == 0) possibleLandingIds++;
                }
            }
            // Half-hour detection precedes landing in this tick. Even a noAlert UFO
            // can become detected here and receive a landing marker on arrival.
            if (ufo.LandId == 0 && (ufo.Detected || halfHour) && ArrivesOnGround(ufo, highest) &&
                LandingAllowed(ufo.Destination!.Position, rule))
                possibleLandingIds++;
        }
        // AlienMission::think runs before the scoring and detection pass. Check its
        // possible UFOs using every candidate first point without drawing RNG in preflight.
        long activityWork = 0;
        List<(IReadOnlyList<SpawnArea> Positions, int Points)>? newbornTakeoffScores = null;
        foreach (var savedMission in _missions)
        {
            if (!halfHour) break;
            var mission = MissionAfterDailyResearch(savedMission, guaranteedResearch);
            var rules = campaign.Content.RuntimeRules;
            var missionRule = rules.AlienMissions[rules.AlienMissions.GetRequired(mission.RuleId)].Value;
            if (IsOver(mission, missionRule) || mission.Interrupted ||
                mission.MultiUfoRetaliationInProgress && !missionRule.MultiUfoRetaliationExtra ||
                mission.NextWave >= missionRule.Waves.Count || mission.SpawnCountdown > 30)
                continue;
            List<SpawnArea>? leaderOrigins = null;
            var certainLeader = false;
            if (missionRule.Waves.Any(wave => NewEscortWave(missionRule, wave)))
            {
                activityWork += _ufos.Count;
                if (activityWork > MaximumAlienBaseChecks) return "Alien mission activity forecasts exceed the supported work limit.";
                leaderOrigins = [];
                if (WorldHunting.EscortLeader(mission.Id, _ufos) is { } leader)
                {
                    leaderOrigins.Add(new(leader.Position, 0));
                    certainLeader = true;
                }
            }
            foreach (var step in DueWaveSteps(mission, missionRule))
            {
                if (!CanSpawnOrdinaryUfoWave(step, missionRule)) continue;
                var wave = SpawnWave(step, missionRule);
                var trajectory = rules.UfoTrajectories[wave.Trajectory!.Value].Value;
                var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
                if (!TryOrdinarySpawnPositions(step, missionRule, trajectory, region, out var positions))
                    return "Alien mission wave spawning requires world simulation.";
                var ordinaryOrigins = positions;
                var percentage = missionRule.Objective == RuntimeMissionObjective.Supply && wave.Objective
                    ? 0 : wave.HunterKillerPercentage == -1
                        ? rules.Ufos[wave.Ufo!.Value].Value.HunterKillerPercentage : wave.HunterKillerPercentage;
                // spawnUfo relocates a successful escort BEFORE this score pass.
                // Earlier probabilistic births may or may not provide the first
                // ordinary leader; preserve every possible outcome without RNG.
                if (NewEscortWave(missionRule, wave) && leaderOrigins is { Count: > 0 })
                    positions = percentage >= 100 && certainLeader ? leaderOrigins : [.. positions, .. leaderOrigins];
                activityWork += (long)positions.Count * (regionTotals.Length + countryTotals.Length) * 2;
                if (activityWork > MaximumAlienBaseChecks) return "Alien mission activity forecasts exceed the supported work limit.";
                var grounded = trajectory.Altitude(0) == 0;
                var reason = CheckSpawnActivity(wave.UfoType, positions, regionTotals, countryTotals, grounded ? 2 : 1);
                if (reason is not null) return reason;
                if (ReserveDetectionScriptWork(wave.UfoType, highest, ref detectionWork, ref detectionCraftSources,
                        ref detectionWeaponSlots) is { } scriptWorkReason) return scriptWorkReason;
                if (!certainLeader && leaderOrigins is not null && percentage < 100)
                {
                    activityWork += ordinaryOrigins.Count;
                    if (activityWork > MaximumAlienBaseChecks) return "Alien mission activity forecasts exceed the supported work limit.";
                    leaderOrigins.AddRange(ordinaryOrigins);
                    certainLeader = percentage <= 0;
                }
                if (!rules.Ufos[wave.Ufo!.Value].Value.NoAlert)
                {
                    possibleMarkerIds++;
                    if (grounded) possibleLandingIds++;
                }
                if (grounded && trajectory.GroundTimer == 1 && missionRule.Points > 0 &&
                    missionRule.Objective is not (RuntimeMissionObjective.Base or RuntimeMissionObjective.Infiltration))
                    (newbornTakeoffScores ??= []).Add((positions, missionRule.Points));
            }
        }
        // processMissionSite follows UFO detection in the same half-hour handler.
        // Every site scores, including one whose timer remains above zero or whose
        // expiry is held by followers.
        var discoveredSite = FirstSiteDiscoveredThisTick(highest);
        if (halfHour)
            foreach (var site in _sites)
            {
                var rules = campaign.Content.RuntimeRules;
                var mission = rules.AlienMissions[rules.AlienMissions.GetRequired(site.MissionRuleId)].Value;
                var deployment = rules.AlienDeployments[rules.AlienDeployments.GetRequired(site.DeploymentId)].Value;
                var transition = WorldSiteTimer.Advance(SiteSecondsAfterDiscovery(site, discoveredSite), SiteHasFollowers(site),
                    mission.DespawnEvenIfTargeted, deployment.Points, deployment.DespawnPenalty);
                if (CheckActivityScore(site.Position, transition.Score, regionTotals, countryTotals) is { } reason)
                    return reason;
                if (transition.Remove && site.UfoUniqueId != 0 &&
                    _ufos.Find(candidate => candidate.UniqueId == site.UfoUniqueId) is { } siteUfo &&
                    TryLiftUfo(siteUfo, fromSite: true, out _, out var liftScore) && liftScore != 0 &&
                    CheckActivityScore(siteUfo.Position, liftScore, regionTotals, countryTotals) is { } liftReason)
                    return liftReason;
            }
        // Direct sites are appended during think, then processed after every existing
        // site. Their positive durations lose 1800 seconds and award deployment points.
        if (halfHour)
            foreach (var savedMission in _missions)
            {
                var mission = MissionAfterDailyResearch(savedMission, guaranteedResearch);
                var rule = campaign.Content.RuntimeRules.AlienMissions[
                    campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;
                if (mission.Interrupted || IsOver(mission, rule) || mission.NextWave >= rule.Waves.Count ||
                    mission.SpawnCountdown > 30 || mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra) continue;
                foreach (var step in DueWaveSteps(mission, rule))
                {
                    if (!IsDirectSiteWave(step, rule)) continue;
                    if (DirectSiteActivityReason(step, rule, regionTotals, countryTotals, ref activityWork) is { } reason) return reason;
                }
            }
        // time5Seconds applies takeoff mission points AFTER all half-hour scoring.
        // Share these totals so individually safe awards cannot overflow together.
        var terminalArrival = false;
        foreach (var ufo in _ufos)
        {
            if (ufo.Status == UfoStatus.Landed && ufo.SecondsRemaining == 5 &&
                TryLiftUfo(ufo, fromSite: false, out _, out var points) && points != 0 &&
                CheckActivityScore(ufo.Position, points, regionTotals, countryTotals) is { } reason)
                return reason;
            if (ArrivesAtSite(ufo, highest))
            {
                var mission = ForecastMission(ufo.MissionId, highest);
                var rule = MissionRule(mission);
                if (rule.Objective != RuntimeMissionObjective.Infiltration &&
                    CheckActivityScore(UfoAfterTimedTransitions(ufo, highest).Destination!.Position, rule.Points,
                        regionTotals, countryTotals) is { } conversionReason) return conversionReason;
            }
            if (EndsUfoFlightPass(ufo, highest))
            {
                terminalArrival = true;
                break;
            }
        }
        // Newborn UFOs are appended after the saved actors. Their landed
        // takeoffs follow site processing and every earlier UFO's movement.
        if (!terminalArrival && newbornTakeoffScores is not null)
            foreach (var takeoff in newbornTakeoffScores)
                if (CheckPossibleActivity(takeoff.Positions, takeoff.Points, regionTotals, countryTotals) is { } liftReason)
                    return liftReason;
        if (!CanAllocateUfoMarkerIds(possibleMarkerIds + _possibleHuntMarkerIds))
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
        ActivityRange[] regionTotals, ActivityRange[] countryTotals, int scoreMultiplier = 1)
    {
        var rules = campaign.Content.RuntimeRules;
        var ufoRule = rules.Ufos[rules.Ufos.GetRequired(ruleId)].Value;
        if (DetectionScriptsReason(ruleId) is { } scriptReason) return scriptReason;
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
                var alert = !ufoRule.NoAlert && !_ignoredUfoContacts.Contains(ufo.Id);
                var markerId = alert && ufo.Id == 0 ? campaign.NextId("STR_UFO") : ufo.Id;
                var landingId = alert && ufo.Status == UfoStatus.Landed && ufo.LandId == 0
                    ? campaign.NextId("STR_LANDING_SITE") : ufo.LandId;
                _ufos[index] = ufo with { Id = markerId, LandId = landingId, Detected = true, HyperDetected = hyperwave };
                if (alert) effects.Notify(new UfoContactDetected(ufo.UniqueId, hyperwave));
            }
            else if (ufo.Detected && detected == UfoDetectionResult.None)
                _ufos[index] = ufo with { Detected = false, HyperDetected = false };
            else if (ufo.Detected && detected == UfoDetectionResult.Hyperwave && !ufo.HyperDetected)
                _ufos[index] = ufo with { HyperDetected = true };
        }
    }

    private string? CheckActivityScore(WorldPosition position, int score, ActivityRange[] regionTotals, ActivityRange[] countryTotals)
    {
        var rules = campaign.Content.RuntimeRules;
        for (var index = 0; index < campaign.RegionStates.Count; index++)
        {
            var region = campaign.RegionStates[index];
            if (!WorldGeometry.InsideRegion(rules.Regions[region.Rule].Value, position)) continue;
            if (!regionTotals[index].Add(score, certain: true))
                return "UFO alien activity exceeds the supported range.";
            break;
        }
        for (var index = 0; index < campaign.CountryStates.Count; index++)
        {
            var country = campaign.CountryStates[index];
            if (!WorldGeometry.InsideCountry(rules.Countries[country.Rule].Value, position)) continue;
            if (!countryTotals[index].Add(score, certain: true))
                return "UFO alien activity exceeds the supported range.";
            break;
        }
        return null;
    }

    private struct ActivityRange(long initial)
    {
        private long _minimum = initial;
        private long _maximum = initial;

        public bool Add(int score, bool certain)
        {
            _minimum += certain ? score : Math.Min(0, score);
            _maximum += certain ? score : Math.Max(0, score);
            return _minimum >= int.MinValue && _maximum <= int.MaxValue;
        }
    }

    private string? CheckSpawnActivity(string ruleId, IReadOnlyList<SpawnArea> positions,
        ActivityRange[] regions, ActivityRange[] countries, int multiplier)
    {
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Ufos[rules.Ufos.GetRequired(ruleId)].Value;
        var score = (long)rule.MissionScore * multiplier;
        if (DetectionScriptsReason(ruleId) is { } scriptReason) return scriptReason;
        if (score is < int.MinValue or > int.MaxValue) return "UFO alien activity exceeds the supported range.";
        return CheckPossibleActivity(positions, (int)score, regions, countries);
    }

    private string? CheckPossibleActivity(IReadOnlyList<SpawnArea> positions, int score,
        ActivityRange[] regions, ActivityRange[] countries, bool guaranteedAward = true)
    {
        var regionHits = new int[regions.Length];
        var countryHits = new int[countries.Length];
        var rules = campaign.Content.RuntimeRules;
        foreach (var position in positions)
        {
            if (position.Radius != 0)
            {
                // A regional rectangle may cross score boundaries. Reserve a possible
                // award for each owner, never credit an uncertain negative score as
                // overflow headroom. Actual scoring still uses ordered containment.
                for (var index = 0; index < regions.Length; index++) regionHits[index] = -1;
                for (var index = 0; index < countries.Length; index++) countryHits[index] = -1;
                break;
            }
            for (var index = 0; index < regions.Length; index++)
                if (WorldGeometry.InsideRegion(rules.Regions[campaign.RegionStates[index].Rule].Value, position.Center))
                { regionHits[index]++; break; }
            for (var index = 0; index < countries.Length; index++)
                if (WorldGeometry.InsideCountry(rules.Countries[campaign.CountryStates[index].Rule].Value, position.Center))
                { countryHits[index]++; break; }
        }
        for (var index = 0; index < regions.Length; index++)
            if (regionHits[index] != 0 && !regions[index].Add(score, guaranteedAward && regionHits[index] == positions.Count))
                return "UFO alien activity exceeds the supported range.";
        for (var index = 0; index < countries.Length; index++)
            if (countryHits[index] != 0 && !countries[index].Add(score, guaranteedAward && countryHits[index] == positions.Count))
                return "UFO alien activity exceeds the supported range.";
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
            var caller = WorldDetection.BaseScriptInputs(radars[start..end], distance, visibility, ufo.Detected, percent);
            var (type, chance) = RunDetectionScript(ufo, caller);
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
            var caller = WorldDetection.CraftScriptInputs(craft.Range, craft.Chance, distance, visibility, ufo.Detected);
            var (type, chance) = RunDetectionScript(ufo, caller, craft.Craft);
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
                sources.Add(new WorldCraftRadar(new WorldPosition(state.Longitude, state.Latitude), range, chance, craft));
            }
        }
    }

    private readonly record struct WorldCraftRadar(WorldPosition Position, int Range, int Chance, CampaignState.CraftState Craft);

    private bool Percent(int chance) => RandomChance.Percent(campaign.Random, chance);

    private bool IsInstantRetaliation(AlienMissionSnapshot mission)
    {
        var rules = campaign.Content.RuntimeRules.AlienMissions;
        return rules[rules.GetRequired(mission.RuleId)].Value.Objective ==
            RuntimeMissionObjective.InstantRetaliation;
    }
}
