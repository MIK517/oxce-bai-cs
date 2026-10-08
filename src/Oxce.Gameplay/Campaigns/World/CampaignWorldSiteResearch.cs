using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string SiteResearchGenerationReason = "Mission site research requires alien-base generation phase forecasts.";
    private const string SiteResearchWaveReason = "Mission site research requires mission-wave composition forecasts.";

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
        if (!found) return null;
        // These boundaries name the still-unprojected caller phases. Research-only
        // worlds can execute without pretending that future actors already changed.
        if (SiteResearchBaseGenerationReason(highest, out var missionBirths, out var generationWork) is { } baseReason) return baseReason;
        DailyResearchStockForecast? dailyResearch = null;
        if (highest >= CampaignTimeTrigger.OneDay)
        {
            long work = 0;
            if (!campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var daily, includeDiscoveries: true))
                return daily.UnavailableReason ?? "Mission site research requires deterministic daily research phase forecasts.";
            dailyResearch = daily;
        }
        var waveWork = 0;
        foreach (var saved in _missions)
        {
            var mission = MissionAfterDailyResearch(saved, _pass.DailyResearchForecast);
            var rule = MissionRule(mission);
            if (mission.Interrupted || IsOver(mission, rule) || mission.NextWave >= rule.Waves.Count || mission.SpawnCountdown > 30) continue;
            // Counter-only score waves have no research-dependent actor birth or
            // objective transition. Preserve their native immediate chain before sites.
            if (rule.Objective != RuntimeMissionObjective.Score) return SiteResearchWaveReason;
            foreach (var step in DueWaveSteps(mission, rule))
            {
                if (++waveWork > MaximumImmediateWaveSteps) return WaveWorkReason;
                if (!CanAdvanceNoObjectWave(step, rule)) return SiteResearchWaveReason;
            }
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
        var forecast = campaign.MissionResearch.CreateForecast(highest, dailyResearch, firstBaseItems, itemWork, missionBirths);
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
        _pass.SiteResearchForecast = forecast;
        return null;
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

    private string? SiteResearchBaseGenerationReason(CampaignTimeTrigger highest, out int precedingMissionBirths, out long work)
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
                var missions = campaign.Content.RuntimeRules.AlienMissions;
                foreach (var choice in rule.GenMission)
                {
                    if (choice.Key.Length == 0 || choice.Value == 0) continue;
                    if (!missions.TryGet(choice.Key, out var handle)) return SiteResearchGenerationReason;
                    var mission = missions[handle].Value;
                    if (SiteResearchGeneratedMissionReason(alienBase, rule, mission, ref work, out var removed) is { } reason) return reason;
                    // The native site caller resolves every active mission's
                    // interruption rule, including newly created delayed missions.
                    if (!removed && mission.InterruptResearchId.Length != 0 && mission.InterruptResearch is null)
                        return "Alien mission interruption references undefined research.";
                    canGenerate |= !removed;
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
                if (SiteResearchGeneratedMissionReason(alienBase, rule, missions[handle].Value, ref work, out _) is { } reason) return reason;
                // Hunting generation follows sites. Its new mission cannot receive
                // an earlier explicit grant or enlarge that grant's validation scan.
            }
        }
        return null;
    }

    private string? SiteResearchGeneratedMissionReason(AlienBaseSnapshot source, RuntimeAlienDeploymentRule deployment,
        RuntimeAlienMissionRule mission, ref long work, out bool removedBeforeSiteResearch)
    {
        removedBeforeSiteResearch = false;
        if (mission.Waves.Count == 0) return SiteResearchGenerationReason;
        if (mission.Waves[0].SpawnTimer / 30 / 2 > 1) return null;
        // Only counter transitions can compose without a new actor or objective
        // prefix. The owning generator still validates all other creation fields.
        if (mission.Objective is not (RuntimeMissionObjective.Score or RuntimeMissionObjective.Supply))
            return SiteResearchGenerationReason;
        try
        {
            var candidate = new AlienMissionSnapshot(-1, string.Empty, string.Empty, source.Race, 0, 0, 30, 0, -1)
            { AlienBase = new(WorldTargetKind.AlienBase, deployment.MarkerName, source.Id, source.Longitude, source.Latitude) };
            var completes = false;
            foreach (var step in DueWaveSteps(candidate, mission))
            {
                if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (!CanAdvanceNoObjectWave(step, mission)) return SiteResearchGenerationReason;
                completes = (ulong)step.NextUfoCounter + 1 >= mission.Waves[step.NextWave].UfoCount &&
                    step.NextWave + 1 == mission.Waves.Count;
            }
            // Native half-hour cleanup precedes site grants. Resolve interruption
            // links only for missions that can still exist at that caller.
            var steps = mission.Waves[0].SpawnTimer / 30;
            removedBeforeSiteResearch = completes && steps / 2 + steps <= 1;
            return null;
        }
        catch (InvalidDataException error) { return error.Message; }
    }
}
