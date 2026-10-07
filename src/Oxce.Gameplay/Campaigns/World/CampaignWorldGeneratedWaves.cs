using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private const string GeneratedWaveBoundary = "Immediate generated mission waves require creation-tick lifecycle forecasts.";

    private string? PrepareHuntingGeneratedWaveForecasts(CampaignTimeTrigger highest, IReadOnlySet<string>? research)
    {
        if (highest < CampaignTimeTrigger.TenMinutes) return null;
        long work = 0;
        _pass.CollectHuntingGeneratedWaves = true;
        try
        {
            foreach (var source in _alienBases)
            {
                if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (highest >= CampaignTimeTrigger.OneDay && BaseDestroyedAfterDailyResearch(source, research)) continue;
                if (AlienBaseHuntingReason(source, AlienBaseRule(source), highest, ref work, out _) is { } reason) return reason;
            }
            return null;
        }
        finally { _pass.CollectHuntingGeneratedWaves = false; }
    }

    private string? PrepareDailyGeneratedWaveForecasts(CampaignTimeTrigger highest, IReadOnlySet<string>? research)
    {
        if (highest < CampaignTimeTrigger.OneDay) return null;
        long work = 0;
        foreach (var source in _alienBases)
        {
            if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            if (BaseDestroyedAfterDailyResearch(source, research)) continue;
            var deployment = AlienBaseRule(source);
            if (source.GenMissionCount >= deployment.GenMissionLimit || deployment.GenMissionFrequency <= 0) continue;
            foreach (var choice in deployment.GenMission)
            {
                if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (choice.Value == 0 || choice.Key.Length == 0 ||
                    !campaign.Content.RuntimeRules.AlienMissions.TryGet(choice.Key, out var handle)) continue;
                var rule = campaign.Content.RuntimeRules.AlienMissions[handle].Value;
                if (rule.Waves.Count == 0 || rule.Waves[0].SpawnTimer / 30 / 2 > 1 ||
                    rule.Objective is not (RuntimeMissionObjective.Score or RuntimeMissionObjective.Supply or RuntimeMissionObjective.Site)) continue;
                work += rule.Waves.Count;
                if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (!rule.Waves.Any(wave => wave.Ufo is not null)) continue;
                try
                {
                    if (AlienBaseGenerationReason(source, deployment, rule, ref work, choice.Key,
                        forecastImmediate: true) is { } reason) return reason;
                }
                catch (InvalidDataException error) { return error.Message; }
                catch (OverflowException) { return "Immediate generated mission fields exceed the supported range."; }
            }
        }
        return null;
    }

    private string? CollectGeneratedWaveForecasts(AlienBaseSnapshot source, RuntimeAlienDeploymentRule deployment,
        RuntimeAlienMissionRule rule, string missionId, string region, bool hunting, ref long work)
    {
        work += _alienBases.Count;
        if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        var matches = 0;
        foreach (var other in _alienBases)
            if (other.Id == source.Id && MarkerName(other.DeploymentId) == deployment.MarkerName) matches++;
        if (matches != 1) return "Immediate generated mission source identity is ambiguous.";
        var fromBase = hunting ? deployment.HuntMissionRaceFromAlienBase : deployment.GenMissionRaceFromAlienBase;
        var races = new List<string>();
        if (fromBase || !rule.HasRaceWeights) races.Add(source.Race);
        else
        {
            var weights = WorldAlienMissionGeneration.WeightsAt(rule.RaceWeights, unchecked((ulong)campaign.MonthsPassed), race: true);
            work += weights.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            foreach (var choice in weights)
                if (choice.Value != 0) races.Add(choice.Key);
        }
        // generateRace returns the empty string before its first timeline entry
        // (or for an empty weighted set). The UFO uses unmodified base stats;
        // AlienMission::load applies the reference interruption/race repair later.
        if (races.Count == 0) races.Add(string.Empty);
        foreach (var race in races)
        {
            if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            // Zero is a private candidate identity, never allocated or persisted.
            // Generated missions have not existed during daily interruption.
            var candidate = new AlienMissionSnapshot(0, missionId, region, race, 0, 0, 30, 0, -1)
            { AlienBase = new(WorldTargetKind.AlienBase, deployment.MarkerName, source.Id, source.Longitude, source.Latitude) };
            foreach (var step in DueWaveSteps(candidate, rule))
            {
                if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                var wave = rule.Waves[step.NextWave];
                // Ordinary site-mission waves do not consume the chosen site area.
                // Immediate objective waves need their site-effect forecasts first.
                if (rule.Objective == RuntimeMissionObjective.Site && wave.Objective) return GeneratedWaveBoundary;
                if (CanAdvanceNoObjectWave(step, rule)) continue;
                if (rule.Objective is not (RuntimeMissionObjective.Score or RuntimeMissionObjective.Supply or RuntimeMissionObjective.Site) ||
                    wave.Ufo is not { } ufo || wave.Trajectory is not { } trajectory) return GeneratedWaveBoundary;
                var ufoRule = campaign.Content.RuntimeRules.Ufos[ufo].Value;
                var path = campaign.Content.RuntimeRules.UfoTrajectories[trajectory].Value;
                var hunter = !(rule.Objective == RuntimeMissionObjective.Supply && wave.Objective) &&
                    (wave.HunterKillerPercentage == -1 ? ufoRule.HunterKillerPercentage : wave.HunterKillerPercentage) > 0;
                if (path.Waypoints.Count == 0 || path.Altitude(0) == 0 && path.GroundTimer <= 1 ||
                    hunter && (path.Altitude(0) != 0 || wave.Escort) ||
                    !CanSpawnOrdinaryUfoWave(step, rule)) return GeneratedWaveBoundary;
            }
            (_pass.GeneratedWaveMissions ??= []).Add((candidate, hunting));
        }
        return null;
    }

    private MissionWaveForecastEnumerable MissionWaveForecasts(CampaignTimeTrigger highest,
        IReadOnlySet<string>? research, bool includeHunting = true) => new(this, highest, research, includeHunting);

    // This caller is reached by empty-world five-second ticks too. Keep the
    // composite traversal on the stack rather than allocating a yield iterator.
    private readonly struct MissionWaveForecastEnumerable(CampaignWorld world, CampaignTimeTrigger highest,
        IReadOnlySet<string>? research, bool includeHunting)
    {
        public Enumerator GetEnumerator() => new(world, highest, research, includeHunting);

        public struct Enumerator(CampaignWorld world, CampaignTimeTrigger highest,
            IReadOnlySet<string>? research, bool includeHunting)
        {
            private int _savedIndex = -1;
            private int _birthIndex = -1;
            public (AlienMissionSnapshot Mission, bool Generated) Current { get; private set; }

            public bool MoveNext()
            {
                if (highest >= CampaignTimeTrigger.ThirtyMinutes && ++_savedIndex < world._missions.Count)
                {
                    Current = (world.MissionAfterDailyResearch(world._missions[_savedIndex], research), false);
                    return true;
                }
                if (world._pass.GeneratedWaveMissions is not { } births) return false;
                while (++_birthIndex < births.Count)
                {
                    var birth = births[_birthIndex];
                    if (!includeHunting && birth.Hunting) continue;
                    Current = (birth.Mission, true);
                    return true;
                }
                return false;
            }
        }
    }
}
