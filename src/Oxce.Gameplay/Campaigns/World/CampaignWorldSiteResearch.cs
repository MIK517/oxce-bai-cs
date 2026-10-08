using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string SiteResearchGenerationReason = "Mission site research requires alien-base generation phase forecasts.";
    private const string SiteResearchWaveReason = "Mission site research requires mission-wave composition forecasts.";
    private readonly record struct SiteResearchBirth(AlienMissionSnapshot Mission, IReadOnlyList<RuleHandle<AlienDeploymentRuleFamily>> Deployments,
        bool Optional, bool GenerationOptional = false);

    private string? PrepareSiteResearch(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.ThirtyMinutes) return null;
        var rules = campaign.Content.RuntimeRules;
        var found = false;
        foreach (var site in _sites)
        {
            if (!SiteExpiresThisTick(site, highest)) continue;
            var deployment = rules.AlienDeployments[rules.AlienDeployments.GetRequired(site.DeploymentId)].Value;
            if (!rules.Research.TryGet(deployment.UnlockedResearchOnDespawnId, out _)) continue;
            found = true;
            // An unconditional unavailable payload cannot be made safe by any
            // research outcome. Keep that boundary ahead of composition guards.
            foreach (var pair in deployment.DespawnEvents)
                if (pair.Value != 0 && rules.Events.TryGet(pair.Key, out var handle) &&
                    rules.Events[handle].Value.InterruptResearch.Length == 0) return SiteDespawnBoundary;
        }
        if (!found)
        {
            try { if (!HasImmediateSiteResearch(highest)) return null; }
            catch (InvalidDataException error) when (error.Message is WaveWorkReason or "Direct mission site creation exceeds the supported work limit.")
            { return error.Message; }
        }
        // These boundaries name the still-unprojected caller phases. Research-only
        // worlds can execute without pretending that future actors already changed.
        List<SiteResearchBirth> dailyImmediateBirths = [];
        if (SiteResearchBaseGenerationReason(highest, dailyImmediateBirths, out var missionBirths, out var generationWork) is { } baseReason) return baseReason;
        DailyResearchStockForecast? dailyResearch = null;
        if (highest >= CampaignTimeTrigger.OneDay)
        {
            long work = 0;
            if (!campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var daily, includeDiscoveries: true))
                return daily.UnavailableReason ?? "Mission site research requires deterministic daily research phase forecasts.";
            dailyResearch = daily;
        }
        var waveWork = 0;
        long landWork = 0;
        HashSet<int>? missionRemovals = null;
        List<SiteResearchBirth> immediateBirths = [];
        foreach (var saved in _missions)
        {
            var mission = MissionAfterDailyResearch(saved, _pass.DailyResearchForecast);
            var rule = MissionRule(mission);
            var afterThink = _pass.MissionCounterForecast?.GetValueOrDefault(saved.Id) ?? mission;
            var births = false;
            if (!mission.Interrupted && !(mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra) &&
                !IsOver(mission, rule) && mission.NextWave < rule.Waves.Count && mission.SpawnCountdown <= 30)
            {
                foreach (var step in DueWaveSteps(mission, rule))
                {
                    if (++waveWork > MaximumImmediateWaveSteps) return WaveWorkReason;
                    if (RetaliationWaveReason(step, rule, ref generationWork) is { } retaliationReason) return retaliationReason;
                    // Final objectives execute before sites. Their owning later
                    // preflight reserves base/pact effects before the tick mutates.
                    if (CanAdvanceNoObjectWave(step, rule)) continue;
                    if (IsDirectSiteWave(step, rule))
                    {
                        if (SiteResearchDirectBirthReason(step, rule, ref generationWork, immediateBirths) is { } directReason) return directReason;
                        continue;
                    }
                    if (SiteResearchOrdinaryBirthReason(step, rule, ref generationWork, ref landWork) is { } birthReason) return birthReason;
                    births = true;
                }
            }
            // Counter forecasts intentionally omit actor births. A completed wave
            // with a surviving newborn still participates in the later research scan.
            if (!births && IsOver(afterThink, rule)) (missionRemovals ??= []).Add(saved.Id);
        }
        IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int>? firstBaseItems = null;
        long itemWork = (dailyResearch?.Work ?? 0) + generationWork;
        if (rules.Research.Rules.Any(entry => entry.Value.Cost == 0 && entry.Value.NeedItem))
        {
            var items = new SiteResearchItemForecast(dailyResearch);
            if (ForecastHuntingCrafts(highest, ref itemWork, items) is { } itemReason) return itemReason;
            var first = campaign.BaseStates[0];
            firstBaseItems = items.Stocks[first.Id];
            var uncertainty = items.Uncertainty.GetValueOrDefault(first.Id);
            foreach (var entry in rules.Research.Rules)
            {
                if (++itemWork > MaximumAlienBaseChecks) return "Mission research exceeds the supported discovery work limit.";
                if (entry.Value.Cost != 0 || !entry.Value.NeedItem || entry.Value.NeededItem is not { } needed ||
                    !rules.Items.TryGet(needed, out var item)) continue;
                var upper = firstBaseItems.GetValueOrDefault(item);
                var lower = upper - (uncertainty?.GetValueOrDefault(item) ?? 0);
                if (lower < 0) return "Mission site research item stocks exceed the supported range.";
                if (lower == 0 && upper != 0)
                    return "Mission site research item eligibility depends on stochastic maintenance outcomes.";
            }
        }
        var forecast = campaign.MissionResearch.CreateForecast(highest, dailyResearch, firstBaseItems, itemWork, missionBirths, missionRemovals);
        var afterSites = new Dictionary<(string Deployment, int Id), IReadOnlySet<string>>();
        foreach (var site in _sites)
        {
            if (!SiteExpiresThisTick(site, highest)) continue;
            var deployment = rules.AlienDeployments[rules.AlienDeployments.GetRequired(site.DeploymentId)].Value;
            if (forecast.Advance(deployment.UnlockedResearchOnDespawnId) is { } reason) return reason;
            afterSites.Add((site.DeploymentId, site.Id), forecast.GuaranteedResearch);
            if (SiteDespawnReason(deployment, highest, forecast.GuaranteedResearch) is { } eventReason) return eventReason;
        }
        _pass.SiteResearchAfterSite = afterSites;
        foreach (var birth in immediateBirths)
        {
            if (AdvanceBirthResearch(birth, forecast, highest, out _) is { } reason) return reason;
        }
        if (dailyImmediateBirths.Count != 0 && AdvanceDailyBirthResearch(dailyImmediateBirths, forecast, highest) is { } dailyReason)
            return dailyReason;
        _pass.SiteResearchForecast = forecast;
        return null;
    }

    // Keep captured callbacks outside the per-tick entry point: C# allocates their
    // shared closure before even an early return from the enclosing method.
    private string? AdvanceDailyBirthResearch(IReadOnlyList<SiteResearchBirth> births,
        CampaignState.MissionResearchForecast forecast, CampaignTimeTrigger highest)
    {
        // Saved missions think before the appended daily missions. Alternatives
        // of one generated birth share one grant slot, never execute all branches.
        foreach (var source in births.GroupBy(birth =>
            (birth.Mission.AlienBase!.TypeId, birth.Mission.AlienBase.Id)))
        {
            var choices = source.GroupBy(birth => birth.Mission.RuleId).ToArray();
            string? AdvanceChoice(IGrouping<string, SiteResearchBirth> group)
            {
                string? AdvancePath(IGrouping<(string RegionId, string Race, int MissionSiteZoneArea), SiteResearchBirth> path)
                {
                    Dictionary<(int Wave, int Counter), IReadOnlyDictionary<string, IReadOnlySet<string>>> slots = [];
                    foreach (var birth in path)
                    {
                        var mission = birth.Mission;
                        var key = (mission.NextWave, mission.NextUfoCounter);
                        if (!slots.TryGetValue(key, out var conditional))
                        {
                            if (AdvanceBirthResearch(birth, forecast, highest, out conditional) is { } reason) return reason;
                            slots.Add(key, conditional);
                        }
                        else if (RecordBirthResearch(birth, conditional, highest) is { } reason) return reason;
                    }
                    return null;
                }
                var paths = group.GroupBy(birth => (birth.Mission.RegionId, birth.Mission.Race, birth.Mission.MissionSiteZoneArea)).ToArray();
                string? AdvanceBirths() => paths.Length == 1 ? AdvancePath(paths[0]) :
                    forecast.AdvanceSequenceChoices(paths.Select(path => (Func<string?>)(() => AdvancePath(path))));
                return group.Any(birth => birth.GenerationOptional)
                    ? forecast.AdvanceOptionalSequence(AdvanceBirths) : AdvanceBirths();
            }
            var reason = choices.Length == 1 ? AdvanceChoice(choices[0]) :
                forecast.AdvanceSequenceChoices(choices.Select(group => (Func<string?>)(() => AdvanceChoice(group))));
            if (reason is not null) return reason;
        }
        return null;
    }

    private string? AdvanceBirthResearch(SiteResearchBirth birth, CampaignState.MissionResearchForecast forecast,
        CampaignTimeTrigger highest, out IReadOnlyDictionary<string, IReadOnlySet<string>> conditional)
    {
        var rules = campaign.Content.RuntimeRules;
        if (forecast.AdvanceChoices(birth.Deployments.Select(handle => rules.AlienDeployments[handle].Value.UnlockedResearchOnDespawnId),
                birth.Optional, out conditional) is { } reason) return reason;
        return RecordBirthResearch(birth, conditional, highest);
    }

    private string? RecordBirthResearch(SiteResearchBirth birth, IReadOnlyDictionary<string, IReadOnlySet<string>> conditional,
        CampaignTimeTrigger highest)
    {
        var rules = campaign.Content.RuntimeRules;
        foreach (var handle in birth.Deployments)
        {
            var deployment = rules.AlienDeployments[handle].Value;
            var research = conditional[deployment.UnlockedResearchOnDespawnId];
            (_pass.SiteResearchAfterBirth ??= []).TryAdd((birth.Mission, handle), research);
            if (SiteDespawnReason(deployment, highest, research) is { } reason) return reason;
        }
        return null;
    }

    private string? SiteResearchOrdinaryBirthReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        ref long work, ref long landWork)
    {
        var wave = SpawnWave(mission, rule);
        if (wave.Trajectory is not { } path) return SiteResearchWaveReason;
        var rules = campaign.Content.RuntimeRules;
        var trajectory = rules.UfoTrajectories[path].Value;
        // Ordinary lifting does not inspect mission interruption. A one-tick
        // ground start may therefore lift after sites through the owned handler.
        if (!SiteResearchOrdinaryWave(wave)) return SiteResearchWaveReason;
        // Reserve bounded geometry and source lookups before the spawn predicate's
        // Cartesian distance checks. This proof does not select a point or draw RNG.
        work += 8L * _alienBases.Count;
        if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        if (!TryOrdinarySpawnPositions(mission, rule, trajectory, region, out var origins) ||
            !TryOrdinarySpawnDestinations(mission, rule, trajectory, region, out var destinations)) return SiteResearchWaveReason;
        work += (long)origins.Count * destinations.Count + origins.Count + destinations.Count;
        if (work > MaximumAlienBaseChecks) return "Alien mission spawn geometry exceeds the supported work limit.";
        landWork += SpawnOriginLandWork(mission, rule, trajectory, region);
        if (landWork > MaximumSpawnLandWork) return "Alien mission land selection exceeds the supported work limit.";
        // The owning mission preflight proves retargeting after site research has
        // populated its forecast, still before the transaction mutates anything.
        return CanSpawnOrdinaryUfoWave(mission, rule, requireHuntingForecast: false) ? null : SiteResearchWaveReason;
    }

    private bool HasImmediateSiteResearch(CampaignTimeTrigger highest)
    {
        var rules = campaign.Content.RuntimeRules;
        var work = 0;
        long selectionWork = 0;
        foreach (var birth in MissionWaveForecasts(highest, _pass.DailyResearchForecast, includeHunting: false))
        {
            var mission = birth.Mission;
            var rule = MissionRule(mission);
            if (mission.Interrupted || mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra ||
                IsOver(mission, rule) || mission.NextWave >= rule.Waves.Count || mission.SpawnCountdown > 30) continue;
            foreach (var step in DueWaveSteps(mission, rule))
            {
                if (++work > MaximumImmediateWaveSteps) throw new InvalidDataException(WaveWorkReason);
                if (!IsDirectSiteWave(step, rule)) continue;
                var wave = rule.Waves[step.NextWave];
                if (wave.Trajectory is null) continue;
                if (wave.ObjectiveOnXcomBase)
                {
                    selectionWork += DirectSiteBaseWork(step);
                    if (selectionWork > MaximumAlienBaseChecks) throw new InvalidDataException("Direct mission site creation exceeds the supported work limit.");
                    if (DirectSiteBases(step).Count == 0) continue;
                }
                var areas = DirectSiteAreas(step, rule);
                selectionWork += areas.Count;
                foreach (var area in areas)
                {
                    selectionWork += SiteDeploymentSelectionWork(wave, rule, area);
                    if (selectionWork > MaximumAlienBaseChecks) throw new InvalidDataException("Direct mission site creation exceeds the supported work limit.");
                    var candidates = SiteDeploymentCandidates(wave, rule, area);
                    selectionWork += candidates.Count;
                    if (selectionWork > MaximumAlienBaseChecks) throw new InvalidDataException("Direct mission site creation exceeds the supported work limit.");
                    foreach (var handle in candidates)
                        if (rules.AlienDeployments[handle].Value.DurationMinimum == 0 &&
                            rules.Research.TryGet(rules.AlienDeployments[handle].Value.UnlockedResearchOnDespawnId, out _)) return true;
                }
            }
        }
        return false;
    }

    private string? SiteResearchDirectBirthReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, ref long work,
        List<SiteResearchBirth>? immediateBirths = null)
    {
        var wave = rule.Waves[mission.NextWave];
        if (wave.Trajectory is null) return SiteResearchWaveReason;
        if (wave.ObjectiveOnXcomBase)
        {
            work += DirectSiteBaseWork(mission);
            if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
            if (DirectSiteBases(mission).Count == 0) return null;
        }
        var areas = DirectSiteAreas(mission, rule);
        work += areas.Count;
        List<RuleHandle<AlienDeploymentRuleFamily>> immediate = [];
        HashSet<RuleHandle<AlienDeploymentRuleFamily>> seen = [];
        var deterministic = areas.Count != 0;
        var anyPositive = false;
        var optional = false;
        foreach (var area in areas)
        {
            work += SiteDeploymentSelectionWork(wave, rule, area);
            if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
            var candidates = SiteDeploymentCandidates(wave, rule, area);
            work += candidates.Count;
            if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
            deterministic &= candidates.Count != 0 && !SiteDeploymentMayBeSuppressed(wave, rule, area);
            foreach (var candidate in candidates)
            {
                var deployment = campaign.Content.RuntimeRules.AlienDeployments[candidate].Value;
                if (deployment.DurationMinimum < 0 || deployment.DurationMaximum < deployment.DurationMinimum ||
                    deployment.DurationMaximum > int.MaxValue / 3600)
                    return "Direct mission site duration is outside the supported range.";
                if (deployment.DurationMinimum == 0)
                {
                    optional |= deployment.DurationMaximum != 0;
                    if (seen.Add(candidate)) immediate.Add(candidate);
                }
                else anyPositive = true;
            }
        }
        if (immediate.Count != 0)
        {
            if (immediateBirths is null)
                return "Newborn mission site research requires ordered expiry forecasts.";
            immediateBirths.Add(new(mission, immediate, optional || anyPositive || !deterministic));
        }
        // Positive timers add no grant. Deterministic immediate births append
        // their grants after saved sites; the site owner still validates geometry.
        return null;
    }

    private bool SiteResearchOrdinaryWave(RuntimeMissionWave wave)
    {
        // The later owning preflight validates hunter/escort target alternatives,
        // including relocation to a leader's origin and initialized escort speed.
        return wave.Ufo is not null && wave.Trajectory is { } path &&
            campaign.Content.RuntimeRules.UfoTrajectories[path].Value.Waypoints.Count >= 2;
    }

    private string? SiteResearchFlightReason(CampaignTimeTrigger highest)
    {
        if (_pass.SiteResearchForecast is not { } forecast) return null;
        if ((long)_ufos.Count * (_missions.Count + (highest >= CampaignTimeTrigger.OneDay ? (long)_alienBases.Count : 0)) > MaximumAlienBaseChecks)
            return "Mission site research UFO link checks exceed the supported work limit.";
        foreach (var ufo in _ufos)
        {
            // Earlier daily interruption/deletion is permanent for this tick even
            // if a later site disables or reenables its research prerequisite.
            var mission = ForecastMission(ufo.MissionId, highest);
            var rule = MissionRule(mission);
            if (!mission.Interrupted && forecast.PossibleExplicitTopics.Contains(rule.InterruptResearchId) &&
                !forecast.GuaranteedExplicitTopics.Contains(rule.InterruptResearchId))
            {
                // Interruption is consulted at an ordinary waypoint arrival, not
                // during transit, landed countdown or takeoff. A safe transit can
                // commit its selected outcome before a later tick forecasts arrival.
                var projected = ProjectFlightTarget(UfoForFlight(ufo, highest), highest);
                if (!projected.Hunting && !projected.Escorting && projected.Status == UfoStatus.Flying && projected.Destination is not null &&
                    UfoDestinationDistance(projected) <= WorldGeometry.RadianSpeed(projected.Speed))
                    return "UFO mission interruption depends on uncertain site research outcomes.";
            }
        }
        return null;
    }

    private IReadOnlySet<string>? SiteResearchAfter(MissionSiteSnapshot site) =>
        _pass.SiteResearchAfterSite?.GetValueOrDefault((site.DeploymentId, site.Id));

    private sealed class SiteResearchItemForecast(DailyResearchStockForecast? research)
    {
        public DailyResearchStockForecast? Research { get; } = research;
        public Dictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> Stocks = [];
        public Dictionary<int, Dictionary<RuleHandle<ItemRuleFamily>, int>> Uncertainty = [];
    }

    private string? SiteResearchBaseGenerationReason(CampaignTimeTrigger highest,
        List<SiteResearchBirth> dailyImmediateBirths,
        out int precedingMissionBirths, out long work)
    {
        precedingMissionBirths = 0;
        work = _alienBases.Count;
        foreach (var alienBase in _alienBases)
        {
            // Daily cleanup has already run when site research executes. A grant
            // here cannot destroy today's base or cancel its earlier generation.
            if (highest >= CampaignTimeTrigger.OneDay && BaseDestroyedAfterDailyResearch(alienBase, _pass.DailyResearchForecast)) continue;
            var rule = AlienBaseRule(alienBase);
            work += rule.GenMission.Count + (long)rule.HuntMissions.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            if (highest >= CampaignTimeTrigger.OneDay && rule.GenMissionFrequency > 0 && alienBase.GenMissionCount < rule.GenMissionLimit)
            {
                var canGenerate = false;
                var firstBirth = dailyImmediateBirths.Count;
                var missions = campaign.Content.RuntimeRules.AlienMissions;
                foreach (var choice in rule.GenMission)
                {
                    if (choice.Key.Length == 0 || choice.Value == 0) continue;
                    if (!missions.TryGet(choice.Key, out var handle)) return SiteResearchGenerationReason;
                    var mission = missions[handle].Value;
                    if (SiteResearchGeneratedMissionReason(alienBase, rule, mission, choice.Key, hunting: false, ref work, out var removed,
                            dailyImmediateBirths) is { } reason) return reason;
                    // The native site caller resolves every active mission's
                    // interruption rule, including newly created delayed missions.
                    if (!removed && mission.InterruptResearchId.Length != 0 && mission.InterruptResearch is null)
                        return "Alien mission interruption references undefined research.";
                    canGenerate |= !removed;
                }
                // A selected null, delayed or non-site mission contributes no
                // immediate grant, even when other choices do produce sites.
                if (dailyImmediateBirths.Count != firstBirth)
                {
                    HashSet<string> grantingChoices = new(StringComparer.Ordinal);
                    for (var index = firstBirth; index < dailyImmediateBirths.Count; index++)
                    {
                        if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                        grantingChoices.Add(dailyImmediateBirths[index].Mission.RuleId);
                    }
                    if (rule.GenMission.Any(choice => choice.Value != 0 && !grantingChoices.Contains(choice.Key)))
                        for (var index = firstBirth; index < dailyImmediateBirths.Count; index++)
                        {
                            if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                            dailyImmediateBirths[index] = dailyImmediateBirths[index] with { GenerationOptional = true };
                        }
                }
                if (canGenerate) precedingMissionBirths++;
            }
            if (rule.BaseDetectionRange <= 0 || rule.BaseDetectionChance <= 0 ||
                (long)alienBase.MinutesSinceLastHuntMissionGeneration + 10 < rule.HuntMissionMaxFrequency) continue;
            IReadOnlyDictionary<string, ulong> choices;
            try { choices = WorldAlienBaseHunting.Weights(rule.HuntMissions, unchecked((ulong)campaign.MonthsPassed)); }
            catch (InvalidDataException error) { return error.Message; }
            work += choices.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            foreach (var choice in choices)
            {
                if (choice.Key.Length == 0 || choice.Value == 0) continue;
                var missions = campaign.Content.RuntimeRules.AlienMissions;
                if (!missions.TryGet(choice.Key, out var handle)) return SiteResearchGenerationReason;
                var mission = missions[handle].Value;
                if (SiteResearchGeneratedMissionReason(alienBase, rule, mission, choice.Key, hunting: true, ref work, out _) is { } reason) return reason;
                // Hunting generation follows sites. Its new mission cannot receive
                // an earlier explicit grant or enlarge that grant's validation scan.
            }
        }
        return null;
    }

    private string? SiteResearchGeneratedMissionReason(AlienBaseSnapshot source, RuntimeAlienDeploymentRule deployment,
        RuntimeAlienMissionRule mission, string missionId, bool hunting, ref long work, out bool removedBeforeSiteResearch,
        List<SiteResearchBirth>? dailyImmediateBirths = null)
    {
        removedBeforeSiteResearch = false;
        if (mission.Waves.Count == 0) return SiteResearchGenerationReason;
        if (mission.Waves[0].SpawnTimer / 30 / 2 > 1) return null;
        // Counter prefixes can use a source-only candidate. Actor geometry needs
        // the daily alternatives or the owning later hunting preflight instead.
        if (mission.Objective is RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation)
        {
            // Hunting follows site grants; its owning later preflight proves
            // effective assault/scouting waves before any tick mutation.
            if (hunting) return null;
            var firstBirth = dailyImmediateBirths!.Count;
            if (SiteResearchDailyBirthReason(source, mission, missionId, ref work, out _, out var removed,
                    dailyImmediateBirths) is { } reason) return reason;
            MarkOptionalGeneratedBirths(deployment, mission, dailyImmediateBirths, firstBirth);
            var initialSteps = mission.Waves[0].SpawnTimer / 30;
            removedBeforeSiteResearch = removed && initialSteps / 2 + initialSteps <= 1;
            return null;
        }
        try
        {
            var candidate = new AlienMissionSnapshot(-1, string.Empty, string.Empty, source.Race, 0, 0, 30, 0, -1)
            { AlienBase = new(WorldTargetKind.AlienBase, deployment.MarkerName, source.Id, source.Longitude, source.Latitude) };
            var completes = false;
            var dailyBirthsProved = false;
            var survivingUfos = false;
            foreach (var step in DueWaveSteps(candidate, mission))
            {
                if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (!CanAdvanceNoObjectWave(step, mission))
                {
                    if (!hunting)
                    {
                        var priorBirths = dailyImmediateBirths!.Count;
                        if (!dailyBirthsProved && SiteResearchDailyBirthReason(source, mission, missionId, ref work, out survivingUfos, out _,
                                dailyImmediateBirths) is { } reason)
                            return reason;
                        if (dailyImmediateBirths.Count != priorBirths)
                        {
                            MarkOptionalGeneratedBirths(deployment, mission, dailyImmediateBirths, priorBirths);
                        }
                        dailyBirthsProved = true;
                    }
                    // Hunting creates after the grants. It cannot enlarge an earlier
                    // interruption scan; the later generated-wave owner validates all
                    // region/race alternatives and geometry before any tick mutation.
                    else if (!IsDirectSiteWave(step, mission) && !SiteResearchOrdinaryWave(SpawnWave(step, mission)))
                        return SiteResearchGenerationReason;
                }
                completes = (ulong)step.NextUfoCounter + 1 >= mission.Waves[step.NextWave].UfoCount &&
                    step.NextWave + 1 == mission.Waves.Count;
            }
            // Native half-hour cleanup precedes site grants. Resolve interruption
            // links only for missions that can still exist at that caller.
            var steps = mission.Waves[0].SpawnTimer / 30;
            removedBeforeSiteResearch = completes && !survivingUfos &&
                !(mission.Objective == RuntimeMissionObjective.Infiltration && mission.EndlessInfiltration) &&
                steps / 2 + steps <= 1;
            return null;
        }
        catch (InvalidDataException error) { return error.Message; }
    }

    private static void MarkOptionalGeneratedBirths(RuntimeAlienDeploymentRule deployment, RuntimeAlienMissionRule mission,
        List<SiteResearchBirth> births, int first)
    {
        // Native start chooses (steps / 2 + random[0, steps]) * 30.
        // A delayed initial timer skips this whole birth sequence.
        var steps = mission.Waves[0].SpawnTimer / 30;
        if (deployment.GenMissionFrequency < 100 || steps / 2 + steps > 1)
            for (var index = first; index < births.Count; index++)
                births[index] = births[index] with { GenerationOptional = true };
    }

    private string? SiteResearchDailyBirthReason(AlienBaseSnapshot source, RuntimeAlienMissionRule rule, string missionId, ref long work,
        out bool survivingUfos, out bool allRemoved, List<SiteResearchBirth> immediateBirths)
    {
        survivingUfos = false;
        allRemoved = true;
        if (_pass.GeneratedWaveMissions is not { } candidates) return SiteResearchGenerationReason;
        var found = false;
        long landWork = 0;
        var firstBirth = immediateBirths.Count;
        var skippedPath = false;
        foreach (var birth in candidates)
        {
            if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            if (birth.Hunting || birth.Mission.RuleId != missionId || !FollowsAlienBase(birth.Mission.AlienBase, source)) continue;
            found = true;
            List<SiteResearchBirth> candidateBirths = [];
            var completed = false;
            var surviving = false;
            foreach (var step in DueWaveSteps(birth.Mission, rule))
            {
                if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (RetaliationWaveReason(step, rule, ref work) is { } retaliationReason) return retaliationReason;
                completed = InstantRetaliationWithoutTarget(step, rule) ||
                    (ulong)step.NextUfoCounter + 1 >= rule.Waves[step.NextWave].UfoCount && step.NextWave + 1 == rule.Waves.Count;
                if (CanAdvanceNoObjectWave(step, rule)) continue;
                if (IsDirectSiteWave(step, rule))
                {
                    if (SiteResearchDirectBirthReason(step, rule, ref work, candidateBirths) is { } directReason) return directReason;
                    continue;
                }
                if (SiteResearchOrdinaryBirthReason(step, rule, ref work, ref landWork) is { } reason)
                    return reason == SiteResearchWaveReason ? SiteResearchGenerationReason : reason;
                survivingUfos = true;
                surviving = true;
            }
            allRemoved &= completed && !surviving;
            skippedPath |= candidateBirths.Count == 0;
            immediateBirths.AddRange(candidateBirths);
        }
        if (skippedPath)
            for (var index = firstBirth; index < immediateBirths.Count; index++)
            {
                if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                immediateBirths[index] = immediateBirths[index] with { GenerationOptional = true };
            }
        // Prepared candidates retain every reachable region/race/area alternative.
        // A real ordinary birth survives cleanup and enlarges the later grant scan.
        return found ? null : SiteResearchGenerationReason;
    }
}
