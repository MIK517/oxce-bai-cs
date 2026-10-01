using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Isolated monthly mission phase; the future campaign caller supplies score/funds after arcs.</summary>
internal interface ICampaignWorldMissionScripts
{
    CampaignCommandResult Schedule(int score, long funds);
}

/// <summary>Internal fixture entry point; campaign month rollover remains guarded.</summary>
internal sealed record ScheduleMonthlyWorldMissions(int Score, long Funds) : ICampaignCommand;

internal sealed partial class CampaignWorld : ICampaignWorldMissionScripts
{
    private const string MissionScriptBoundary =
        "Mission script requires site, strategy fallback or another unavailable world transition.";

    private void RegisterMissionScripts(CampaignCapabilityRegistry registry)
    {
        registry.Operation<ICampaignWorldMissionScripts>(this);
        registry.Command<ScheduleMonthlyWorldMissions>(command =>
            ((ICampaignWorldMissionScripts)this).Schedule(command.Score, command.Funds));
    }

    CampaignCommandResult ICampaignWorldMissionScripts.Schedule(int score, long funds)
    {
        var state = campaign.WorldScriptContext(score, funds, _strategy.MissionRuns);
        var available = campaign.Content.RuntimeRules.MissionScripts.Rules.Where(entry =>
            WorldScriptEligibility.Matches(entry.Value.Criteria, state) &&
            (entry.Value.MaximumRuns == -1 ||
             entry.Value.MaximumRuns > _strategy.MissionsRun(entry.Value.VariableName))).ToArray();
        return ScheduleMissionScriptCommands(available, state.Month);
    }

    private CampaignCommandResult ScheduleMissionScriptCommands(
        RuntimeRule<RuntimeMissionScriptRule>[] available, int month)
    {
        if (available.Length > 4096) return Blocked("Mission scripts exceed the supported work limit.");
        if (MissionScriptReason(available, month) is { } reason) return Blocked(reason);

        // GeoscapeState gathers the available list once, then evaluates labels and odds in order.
        var conditions = new Dictionary<int, bool>();
        foreach (var entry in available)
        {
            var script = entry.Value;
            var process = true;
            foreach (var condition in script.Conditionals)
            {
                if (!process) break;
                process = !conditions.TryGetValue(Math.Abs(condition), out var prior) ||
                          (prior && condition > 0) || (!prior && condition < 0);
            }
            var success = false;
            if (process && Percent(script.Criteria.ExecutionOdds))
            {
                var selectionMonth = unchecked((ulong)month);
                string region, mission;
                if (Percent(script.TargetBaseOdds))
                {
                    if (SelectBaseTargetMission(script, month) is not { } selection)
                    {
                        if (script.Label > 0) conditions.Add(script.Label, false);
                        continue;
                    }
                    (region, mission) = selection;
                }
                else
                {
                    region = WorldScriptSelection.Generate(script.Regions, selectionMonth, campaign.Random)!.Id;
                    mission = script.Missions.Count == 0
                        ? _strategy.ChooseRandomMission(region, campaign.Random)
                        : WorldScriptSelection.Generate(script.Missions, selectionMonth, campaign.Random)!.Id;
                    if (mission.Length == 0)
                    {
                        if (script.Label > 0) conditions.Add(script.Label, false);
                        continue;
                    }
                }
                var rule = campaign.Content.RuntimeRules.AlienMissions[
                    campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission)].Value;
                var race = script.Races.Count == 0
                    ? WorldAlienMissionGeneration.Choose(WorldAlienMissionGeneration.WeightsAt(rule.RaceWeights,
                        selectionMonth, race: true), campaign.Random)
                    : WorldScriptSelection.Generate(script.Races, selectionMonth, campaign.Random)!.Id;
                var delay = WorldMissionScriptSelection.Delay(script, campaign.Random);
                var countdown = delay == 0
                    ? WorldTrajectory.SpawnCountdown(checked((int)rule.Waves[0].SpawnTimer), campaign.Random)
                    : delay;
                var id = campaign.NextId("ALIEN_MISSIONS");
                _strategy.AddMissionRun(script.VariableName);
                var regionRule = campaign.Content.RuntimeRules.Regions[
                    campaign.Content.RuntimeRules.Regions.GetRequired(region)].Value;
                var missionRegion = regionRule.MissionRegionId.Length == 0 ? region : regionRule.MissionRegionId;
                _missions.Add(new AlienMissionSnapshot(id, mission, missionRegion, race, 0, 0, countdown, 0, -1));
                if (script.UseTable) _strategy.RemoveMission(region, mission);
                success = true;
            }
            if (script.Label > 0) conditions.Add(script.Label, success);
        }
        return new CampaignCommandResult([]);
    }

    private string? MissionScriptReason(
        RuntimeRule<RuntimeMissionScriptRule>[] available, int month)
    {
        var labels = new HashSet<int>();
        var runs = new Dictionary<string, int>(StringComparer.Ordinal);
        var possible = 0;
        long work = 0;
        var possibleRemoved = new HashSet<(string Region, string Mission)>();
        var removalCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in available)
        {
            var script = entry.Value;
            var creditedRegions = new HashSet<string>(StringComparer.Ordinal);
            if (script.Label > 0 && !labels.Add(script.Label))
                return $"Mission scripts share label {script.Label}.";
            if (script.Conditionals.Contains(int.MinValue))
                return "Mission script condition is outside the supported range.";
            if (script.Criteria.ExecutionOdds <= 0) continue;
            possible++;
            if (script.SiteType || (script.Missions.Count == 0 && script.TargetBaseOdds > 0) ||
                (script.TargetBaseOdds < 100 && script.Regions.Count == 0))
                return MissionScriptBoundary;
            if (script.StartDelay < 0 || script.RandomDelay < 0 ||
                (long)script.StartDelay + script.RandomDelay > int.MaxValue)
                return "Mission script delay exceeds the supported range.";
            if (script.TargetBaseOdds > 0)
            {
                var types = WorldScriptSelection.Available(script.Missions, month);
                work += (long)types.Count * campaign.BaseStates.Count * (1L + _missions.Count + available.Length);
                work += (long)campaign.BaseStates.Count * campaign.RegionStates.Count;
                if (work > MaximumMissionScriptChecks) return MissionScriptWorkReason;
                if (types.Count == 0 || types.Any(choice => choice.Rule.Handle is not { } handle ||
                        !ValidMission(handle, script.Races.Count == 0))) return MissionScriptBoundary;
                foreach (var owner in campaign.BaseStates)
                {
                    if (LocateGenerationRegion(new(owner.Longitude, owner.Latitude)) is not { } region ||
                        !ValidRegion(campaign.Content.RuntimeRules.Regions.GetRequired(region)))
                        return "Mission script cannot locate a valid player base region.";
                    if (script.UseTable)
                        foreach (var type in types) RecordRemoval(region, type.Rule.Id, creditedRegions);
                }
            }
            if (script.TargetBaseOdds < 100)
            {
                if (script.Missions.Count != 0 && Choices(script.Missions, month, choice =>
                        choice.Handle is { } handle && ValidMission(handle, script.Races.Count == 0)) is { } missionReason)
                    return missionReason;
                if (Choices(script.Regions, month, choice => choice.Handle is { } handle && ValidRegion(handle)) is { } regionReason)
                    return regionReason;
                foreach (var region in WorldScriptSelection.ForMonth(script.Regions, unchecked((ulong)month), false))
                {
                    if (script.Missions.Count == 0)
                    {
                        if (!_strategy.RegionMissions.TryGetValue(region.Rule.Id, out var options))
                            return "Mission script strategy has no table for its selected region.";
                        work += options.Names.Count;
                        if (work > MaximumMissionScriptChecks) return MissionScriptWorkReason;
                        if (!options.IsEmpty && removalCounts.GetValueOrDefault(region.Rule.Id) >= options.Names.Count &&
                            options.Names.All(name => possibleRemoved.Contains((region.Rule.Id, name))))
                            return "Earlier mission scripts can exhaust a required strategy region table.";
                        foreach (var choice in options.Entries)
                            if (!campaign.Content.RuntimeRules.AlienMissions.TryGet(choice.Key, out var handle) ||
                                !ValidMission(handle, script.Races.Count == 0)) return MissionScriptBoundary;
                        if (script.UseTable)
                            foreach (var name in options.Names) RecordRemoval(region.Rule.Id, name, creditedRegions);
                    }
                    else if (script.UseTable)
                    {
                        var choices = WorldScriptSelection.ForMonth(script.Missions, unchecked((ulong)month), false);
                        work += choices.Count;
                        if (work > MaximumMissionScriptChecks) return MissionScriptWorkReason;
                        foreach (var choice in choices) RecordRemoval(region.Rule.Id, choice.Rule.Id, creditedRegions);
                    }
                }
            }
            if (script.Races.Count != 0 && Choices(script.Races, month, choice => choice.IsResolved) is { } raceReason)
                return raceReason;
            if (script.VariableName.Length != 0)
            {
                var count = runs.GetValueOrDefault(script.VariableName) + 1;
                runs[script.VariableName] = count;
                if ((long)_strategy.MissionsRun(script.VariableName) + count > int.MaxValue)
                    return "Mission script run counter is exhausted.";
            }
        }
        if (possible == 0) return null;
        var next = campaign.PeekNextId("ALIEN_MISSIONS");
        if ((long)_missions.Count + possible > 100_000 || next <= 0 ||
            (long)next + possible > int.MaxValue ||
            _missions.Any(mission => mission.Id >= next && mission.Id < (long)next + possible))
            return "Alien mission IDs or population exceed the supported limit.";
        return null;

        void RecordRemoval(string region, string mission, HashSet<string> credited)
        {
            possibleRemoved.Add((region, mission));
            if (credited.Add(region)) removalCounts[region] = removalCounts.GetValueOrDefault(region) + 1;
        }

        bool ValidRegion(RuleHandle<RegionRuleFamily> handle)
        {
            var region = campaign.Content.RuntimeRules.Regions[handle].Value;
            return region.MissionRegionId.Length == 0 || region.MissionRegion is not null;
        }

        bool ValidMission(RuleHandle<AlienMissionRuleFamily> handle, bool useMissionRace)
        {
            var rule = campaign.Content.RuntimeRules.AlienMissions[handle].Value;
            if (rule.Objective != RuntimeMissionObjective.Score ||
                rule.OperationType != RuntimeMissionOperationType.Space || rule.Waves.Count == 0)
                return false;
            if (useMissionRace)
            {
                var weights = WorldAlienMissionGeneration.WeightsAt(rule.RaceWeights, unchecked((ulong)month), race: true);
                if (WorldAlienMissionGeneration.Total(weights) == 0) return false;
                foreach (var choice in weights)
                    if (choice.Value != 0 && !campaign.Content.RuntimeRules.AlienRaces.TryGet(choice.Key, out _))
                        return false;
            }
            var timer = rule.Waves[0].SpawnTimer;
            var steps = timer / 30;
            return timer <= int.MaxValue && (steps / 2 + steps) * 30 <= int.MaxValue;
        }
    }

    private static string? Choices<TFamily>(IReadOnlyList<RuntimeScriptTimeline<TFamily>> timeline,
        int month, Func<RuntimeRuleReference<TFamily>, bool> valid)
    {
        var choices = WorldScriptSelection.ForMonth(timeline, unchecked((ulong)month), allowEmptyTimeline: false);
        ulong total = 0;
        foreach (var choice in choices)
        {
            if (choice.Weight > (ulong)int.MaxValue - total)
                throw new InvalidDataException("Strategic script weights exceed the supported range.");
            total += choice.Weight;
            if (choice.Weight != 0 && !valid(choice.Rule)) return MissionScriptBoundary;
        }
        return total == 0 ? MissionScriptBoundary : null;
    }
}
