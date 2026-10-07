using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    // A source selects one generated mission per caller. Keep the largest
    // creation chain across its rule/region/race/area alternatives.
    private sealed class GeneratedSiteReservations
    {
        private readonly Dictionary<(int Id, string Type, bool Hunting), int> _populations = [];
        private readonly Dictionary<(int Id, string Type, bool Hunting, string Marker), int> _markers = [];
        private readonly Dictionary<string, int> _candidateMarkers = new(StringComparer.Ordinal);
        private (int Id, string Type, bool Hunting) _source;
        private int _candidatePopulation;

        public void Begin(AlienMissionSnapshot mission, bool hunting)
        {
            _source = (mission.AlienBase!.Id, mission.AlienBase.TypeId, hunting);
            _candidatePopulation = 0;
            _candidateMarkers.Clear();
        }

        public int ReservePopulation()
        {
            var previous = _populations.GetValueOrDefault(_source);
            var maximum = Math.Max(previous, ++_candidatePopulation);
            _populations[_source] = maximum;
            return maximum - previous;
        }

        public int ReserveMarker(string marker)
        {
            var count = _candidateMarkers.GetValueOrDefault(marker) + 1;
            _candidateMarkers[marker] = count;
            var key = (_source.Id, _source.Type, _source.Hunting, marker);
            var previous = _markers.GetValueOrDefault(key);
            var maximum = Math.Max(previous, count);
            _markers[key] = maximum;
            return maximum - previous;
        }
    }

    private bool IsDirectSiteWave(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var wave = rule.Waves[mission.NextWave];
        return !HasRetaliationTarget(mission, rule) &&
            wave.Ufo is null && !SuppressedSupplyWave(mission, rule, wave) &&
            (rule.Objective == RuntimeMissionObjective.Site && wave.Objective || wave.Deployment is { } deployment &&
             campaign.Content.RuntimeRules.AlienDeployments[deployment].Value.MarkerName.Length != 0);
    }

    private IReadOnlyList<MissionArea> DirectSiteAreas(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var wave = rule.Waves[mission.NextWave];
        var rules = campaign.Content.RuntimeRules;
        var trajectory = rules.UfoTrajectories[wave.Trajectory!.Value].Value;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        var zone = rule.SpawnZone == -1 ? trajectory.Zone(0) : rule.SpawnZone;
        var areas = WorldGeometry.MissionAreas(region, zone);
        if (mission.MissionSiteZoneArea == -1) return areas;
        if ((uint)mission.MissionSiteZoneArea >= (uint)areas.Count)
            throw new InvalidDataException("Direct mission site area is outside its spawn zone.");
        return [areas[mission.MissionSiteZoneArea]];
    }

    private string? DirectSiteSpawnReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, CampaignTimeTrigger highest,
        Dictionary<string, int> markerReservations, ref long work, ref int population, bool processThisTick = true,
        GeneratedSiteReservations? generated = null)
    {
        var wave = rule.Waves[mission.NextWave];
        if (wave.Trajectory is null || !CanAdvanceWaveCounters(mission, rule, wave))
            return "Direct mission site wave counters or trajectory are invalid.";
        var areas = DirectSiteAreas(mission, rule);
        work += areas.Count;
        if (wave.ObjectiveOnXcomBase)
        {
            work += DirectSiteBaseWork(mission);
            if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
            if (DirectSiteBases(mission).Count == 0) return null;
        }
        if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
        var markers = new HashSet<string>(StringComparer.Ordinal);
        HashSet<RuleHandle<AlienDeploymentRuleFamily>>? expiryCandidates = null;
        Dictionary<CustomCounterChange, int>? expiryChanges = null;
        var rules = campaign.Content.RuntimeRules;
        foreach (var area in areas)
        {
            work += SiteDeploymentSelectionWork(wave, rule, area);
            if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
            var candidates = SiteDeploymentCandidates(wave, rule, area);
            work += candidates.Count;
            if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
            if (candidates.Count == 0) continue;
            if (!wave.ObjectiveOnXcomBase && !TrySpawnArea(area, out _))
                return "Direct mission site coordinates require an unsupported latitude domain.";
            foreach (var handle in candidates)
            {
                var deployment = rules.AlienDeployments[handle].Value;
                if (deployment.DurationMinimum < 0 || deployment.DurationMaximum < deployment.DurationMinimum ||
                    deployment.DurationMaximum > int.MaxValue / 3600)
                    return "Direct mission site duration is outside the supported range.";
                if (processThisTick && deployment.DurationMinimum == 0)
                {
                    work += 6 + 2L * deployment.DespawnEvents.Count;
                    if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
                    if (SiteDespawnReason(deployment, highest) is { } despawnReason) return despawnReason;
                    if (IsUfoIdNamespace(deployment.CounterDespawn) || IsUfoIdNamespace(deployment.CounterFailure) ||
                        IsUfoIdNamespace(deployment.CounterAll) || IsUfoIdNamespace(deployment.DecreaseCounterDespawn) ||
                        IsUfoIdNamespace(deployment.DecreaseCounterFailure) || IsUfoIdNamespace(deployment.DecreaseCounterAll))
                        return "Direct mission site counters share an unsupported UFO ID namespace.";
                    if ((expiryCandidates ??= []).Add(handle))
                    {
                        work += 42; // At most six fields and their multiplicity comparisons.
                        if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
                        var changes = new List<CustomCounterChange>(6);
                        AddSiteCounterChanges(changes, deployment);
                        expiryChanges ??= [];
                        foreach (var change in changes.Distinct())
                            expiryChanges[change] = Math.Max(expiryChanges.GetValueOrDefault(change), changes.Count(value => value == change));
                    }
                }
                markers.Add(deployment.MarkerName);
            }
        }
        if (markers.Count == 0) return null;
        if (expiryChanges is not null)
            foreach (var (change, count) in expiryChanges)
            {
                var changes = change.Increase ? _pass.SiteBirthExpiryIncreases ??= [] : _pass.SiteBirthExpiryDecreases ??= [];
                for (var index = 0; index < count; index++) changes.Add(change);
            }
        population += generated?.ReservePopulation() ?? 1;
        if ((long)_sites.Count + population > 100_000) return "World mission site population exceeds the supported limit.";
        foreach (var marker in markers)
        {
            if (string.IsNullOrWhiteSpace(marker))
                return "Direct mission site marker names require a nonempty ID namespace.";
            work += _sites.Count + (long)_alienBases.Count;
            if (work > MaximumAlienBaseChecks) return "Direct mission site creation exceeds the supported work limit.";
            if (marker is "STR_UFO_UNIQUE" or "STR_UFO" or "STR_LANDING_SITE" or "STR_CRASH_SITE")
                return "Mission site and UFO creation share an unsupported ID namespace.";
            var additional = generated?.ReserveMarker(marker) ?? 1;
            var count = markerReservations.GetValueOrDefault(marker) + additional;
            markerReservations[marker] = count;
            if (additional != 0) (_pass.SiteBirthIdChanges ??= []).Add(new(marker, true));
            if (highest >= CampaignTimeTrigger.OneDay)
            {
                work += (long)rules.Research.Count * campaign.BaseStates.Count;
                if (work > MaximumAlienBaseChecks) return "Mission site marker forecasts exceed the supported work limit.";
                long counterFields = 0;
                foreach (var research in rules.Research.Rules)
                    counterFields += research.Value.IncreaseCounters.Count + (long)research.Value.DecreaseCounters.Count;
                work += counterFields * campaign.BaseStates.Count;
                foreach (var owner in campaign.BaseStates)
                    foreach (var project in owner.Research)
                    {
                        var research = rules.Research[project.Rule].Value;
                        work += 1L + research.GetOneFree.Count;
                        foreach (var group in research.GetOneFreeProtected) work += 1L + group.Topics.Count;
                    }
                if (work > MaximumAlienBaseChecks) return "Mission site marker forecasts exceed the supported work limit.";
            }
            if (ProductionCounterWorkReason(highest, ref work, "Mission site marker forecasts exceed the supported work limit.") is { } productionWork)
                return productionWork;
            var range = campaign.CustomCounters.BeforeWorldCreation(marker, highest, includeProduction: true);
            var end = range.Maximum + count;
            if (range.Minimum <= 0 || end > int.MaxValue ||
                _sites.Any(site => MarkerName(site.DeploymentId) == marker && site.Id >= range.Minimum && site.Id < end) ||
                _alienBases.Any(alienBase => MarkerName(alienBase.DeploymentId) == marker && alienBase.Id >= range.Minimum && alienBase.Id < end) ||
                CraftIdCollision(marker, range.Minimum, end))
                return "Mission site marker IDs are exhausted or collide with a saved target.";
        }
        return null;
    }

    private static bool IsUfoIdNamespace(string name) =>
        name is "STR_UFO_UNIQUE" or "STR_UFO" or "STR_LANDING_SITE" or "STR_CRASH_SITE";

    private void SpawnDirectSite(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule)
    {
        var wave = rule.Waves[mission.NextWave];
        var areas = DirectSiteAreas(mission, rule);
        var area = areas[mission.MissionSiteZoneArea == -1 ? campaign.Random.NextInclusive(0, areas.Count - 1) : 0];
        if (wave.ObjectiveOnXcomBase)
        {
            var bases = DirectSiteBases(mission);
            if (bases.Count == 0) return;
            var owner = bases[bases.Count == 1 ? 0 : campaign.Random.NextInclusive(0, bases.Count - 1)];
            area = area with
            {
                LongitudeMinimum = owner.Longitude,
                LongitudeMaximum = owner.Longitude,
                LatitudeMinimum = owner.Latitude,
                LatitudeMaximum = owner.Latitude,
                Name = string.Empty,
            };
        }
        var rules = campaign.Content.RuntimeRules;
        var selected = SelectSiteDeployment(wave, rule, area);
        if (selected is not { } handle) return;
        var deployment = rules.AlienDeployments[handle].Value;
        var longitude = WorldGeometry.RandomDouble(campaign.Random, area.LongitudeMinimum, area.LongitudeMaximum);
        var latitude = WorldGeometry.RandomDouble(campaign.Random, area.LatitudeMinimum, area.LatitudeMaximum);
        var position = WorldPosition.Create(longitude, latitude);
        var id = campaign.NextId(deployment.MarkerName);
        var seconds = checked(campaign.Random.NextInclusive(deployment.DurationMinimum, deployment.DurationMaximum) * 3600);
        _sites.Add(new MissionSiteSnapshot(id, mission.RuleId, rules.AlienDeployments.GetExternalId(handle),
            mission.Race, position.Longitude, position.Latitude, seconds)
        {
            Texture = area.Texture,
            City = area.Name,
        });
    }

    private List<RuleHandle<AlienDeploymentRuleFamily>> SiteDeploymentCandidates(
        RuntimeMissionWave wave, RuntimeAlienMissionRule rule, MissionArea area)
    {
        var rules = campaign.Content.RuntimeRules;
        if (wave.Deployment is { } explicitDeployment) return [explicitDeployment];
        if (rules.AlienDeployments.TryGet(rule.SiteTypeId, out var missionDeployment)) return [missionDeployment];
        if (!rules.Campaign.Globe.Textures.TryGetValue(area.Texture, out var texture))
            throw new InvalidDataException($"Mission site texture {area.Texture} does not exist.");
        var weights = texture.Deployments;
        if (weights.Count == 0) return [];
        if (weights.Count != 1) _ = WorldAlienMissionGeneration.Total(weights);
        var result = new List<RuleHandle<AlienDeploymentRuleFamily>>();
        foreach (var pair in weights)
            if ((weights.Count == 1 || pair.Value != 0) && pair.Key.Length != 0 && pair.Key != "\0")
                result.Add(rules.AlienDeployments.GetRequired(pair.Key));
        return result;
    }

    private RuleHandle<AlienDeploymentRuleFamily>? SelectSiteDeployment(
        RuntimeMissionWave wave, RuntimeAlienMissionRule rule, MissionArea area)
    {
        var rules = campaign.Content.RuntimeRules;
        if (wave.Deployment is { } explicitDeployment) return explicitDeployment;
        if (rules.AlienDeployments.TryGet(rule.SiteTypeId, out var missionDeployment)) return missionDeployment;
        var weights = rules.Campaign.Globe.Textures[area.Texture].Deployments;
        var id = weights.Count == 1 ? weights.First().Key : WorldAlienMissionGeneration.Choose(weights, campaign.Random);
        return id.Length == 0 || id == "\0" ? null : rules.AlienDeployments.GetRequired(id);
    }

    private long SiteDeploymentSelectionWork(RuntimeMissionWave wave, RuntimeAlienMissionRule rule, MissionArea area)
    {
        var rules = campaign.Content.RuntimeRules;
        return wave.Deployment is not null || rules.AlienDeployments.TryGet(rule.SiteTypeId, out _) ? 1 :
            rules.Campaign.Globe.Textures.TryGetValue(area.Texture, out var texture) ? 2L * texture.Deployments.Count + 1 : 1;
    }

    private bool SiteDeploymentMayBeSuppressed(RuntimeMissionWave wave, RuntimeAlienMissionRule rule, MissionArea area)
    {
        var rules = campaign.Content.RuntimeRules;
        if (wave.Deployment is not null || rules.AlienDeployments.TryGet(rule.SiteTypeId, out _)) return false;
        var weights = rules.Campaign.Globe.Textures[area.Texture].Deployments;
        return weights.Any(pair => (pair.Key.Length == 0 || pair.Key == "\0") && (weights.Count == 1 || pair.Value != 0));
    }

    private string? DirectSiteActivityReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule,
        ActivityRange[] regions, ActivityRange[] countries, ref long work, bool guaranteedAward = true)
    {
        var wave = rule.Waves[mission.NextWave];
        var positions = new List<SpawnArea>();
        var minimum = int.MaxValue;
        var maximum = int.MinValue;
        var suppressed = false;
        var hasCandidates = false;
        List<CampaignState.BaseState>? bases = null;
        if (wave.ObjectiveOnXcomBase)
        {
            work += DirectSiteBaseWork(mission);
            if (work > MaximumAlienBaseChecks) return "Mission site activity forecasts exceed the supported work limit.";
            bases = DirectSiteBases(mission);
            if (bases.Count == 0) return null;
        }
        foreach (var area in DirectSiteAreas(mission, rule))
        {
            work += SiteDeploymentSelectionWork(wave, rule, area);
            if (work > MaximumAlienBaseChecks) return "Mission site activity forecasts exceed the supported work limit.";
            var candidates = SiteDeploymentCandidates(wave, rule, area);
            work += candidates.Count;
            if (work > MaximumAlienBaseChecks) return "Mission site activity forecasts exceed the supported work limit.";
            if (candidates.Count == 0) { suppressed = true; continue; }
            suppressed |= SiteDeploymentMayBeSuppressed(wave, rule, area);
            hasCandidates = true;
            if (bases is null)
            {
                _ = TrySpawnArea(area, out var position);
                positions.Add(position);
            }
            foreach (var candidate in candidates)
            {
                var points = campaign.Content.RuntimeRules.AlienDeployments[candidate].Value.Points;
                var deployment = campaign.Content.RuntimeRules.AlienDeployments[candidate].Value;
                var lower = deployment.DurationMaximum == 0 ? deployment.DespawnPenalty : points;
                var upper = lower;
                if (deployment.DurationMinimum == 0)
                {
                    lower = Math.Min(lower, deployment.DespawnPenalty);
                    upper = Math.Max(upper, deployment.DespawnPenalty);
                }
                minimum = Math.Min(minimum, lower);
                maximum = Math.Max(maximum, upper);
            }
        }
        if (bases is not null && hasCandidates)
            foreach (var owner in bases) positions.Add(new SpawnArea(new(owner.Longitude, owner.Latitude), 0));
        work += (long)positions.Count * (regions.Length + countries.Length) * 2;
        if (work > MaximumAlienBaseChecks) return "Mission site activity forecasts exceed the supported work limit.";
        if (positions.Count == 0) return null;
        if (!suppressed && minimum == maximum) return CheckPossibleActivity(positions, maximum, regions, countries, guaranteedAward);
        if (maximum > 0 && CheckPossibleActivity(positions, maximum, regions, countries, false) is { } reason) return reason;
        return minimum < 0 ? CheckPossibleActivity(positions, minimum, regions, countries, false) : null;
    }

    private long DirectSiteBaseWork(AlienMissionSnapshot mission)
    {
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        return (long)campaign.BaseStates.Count * (region.Areas.Count + 1L);
    }

    private List<CampaignState.BaseState> DirectSiteBases(AlienMissionSnapshot mission)
    {
        var rules = campaign.Content.RuntimeRules;
        var region = rules.Regions[rules.Regions.GetRequired(mission.RegionId)].Value;
        var rule = MissionRule(mission);
        if (rule.Objective is RuntimeMissionObjective.Retaliation or RuntimeMissionObjective.InstantRetaliation)
            return WorldRetaliation.Candidates(campaign.BaseStates, region, rule.Objective,
                false, rule.MultiUfoRetaliation,
                static owner => new(owner.Longitude, owner.Latitude), static owner => owner.RetaliationTarget);
        var result = new List<CampaignState.BaseState>();
        foreach (var owner in campaign.BaseStates)
            if (WorldGeometry.InsideRegion(region, new(owner.Longitude, owner.Latitude))) result.Add(owner);
        return result;
    }
}
