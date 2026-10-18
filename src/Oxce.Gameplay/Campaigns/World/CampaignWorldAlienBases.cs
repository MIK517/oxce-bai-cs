using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Alien-base activity and discovery, after the daily player-base loop.</summary>
internal sealed partial class CampaignWorld
{
    // The world forecasts' shared work budget (ForecastLimits.Work is the campaign forecasts'): a bound on the
    // forecasts' own work and the worst case they admit. Geometry terms grow with the globe: one base
    // objective on Rosigma's 3,253-point globe is charged about 650,000 (branch 6 S3), so the fixture-calibrated
    // 1,000,000 stopped real campaigns.
    private static long MaximumWorldWork => FixtureWorkBounds ? 1_000_000 : 100_000_000;

    private static readonly AsyncLocal<bool?> ProductionWorkBoundsOnThisFlow = new();

    /// <summary>
    /// Test-only: the forecast work bounds at the sizes the fixture tests were calibrated with (1,000,000 checks, and
    /// 10,000,000 for land selection), so those tests keep small workloads. The test assemblies set it for the process.
    /// </summary>
    internal static bool FixtureWorkBoundsEverywhere { get; set; }

    /// <summary>Test-only: the production bounds on this async flow, for tests that load real mods.</summary>
    internal static bool ProductionWorkBounds
    {
        get => ProductionWorkBoundsOnThisFlow.Value == true;
        set => ProductionWorkBoundsOnThisFlow.Value = value;
    }

    private static bool FixtureWorkBounds => FixtureWorkBoundsEverywhere && !ProductionWorkBounds;
    private const string AlienBaseWorkReason = "Alien base activity and sight checks exceed the supported work limit.";

    private void RegisterAlienBaseOperations(CampaignCapabilityRegistry registry)
    {
        registry.Timed(CampaignTimeTrigger.OneDay, CampaignTimeOrder.DayWorldAlienBases,
            "alien base activity and facility discovery", ProcessAlienBasesDaily);
        registry.Timed(CampaignTimeTrigger.TenMinutes, CampaignTimeOrder.TenMinutesWorldAlienBaseHunting,
            "alien base hunting timers", AdvanceAlienBaseHunting);
    }

    private RuntimeAlienDeploymentRule AlienBaseRule(AlienBaseSnapshot alienBase) =>
        campaign.Content.RuntimeRules.AlienDeployments[
            campaign.Content.RuntimeRules.AlienDeployments.GetRequired(alienBase.DeploymentId)].Value;

    private string? AlienBaseActivityReason(CampaignTimeTrigger highest, IReadOnlySet<string>? guaranteedResearch)
    {
        if (_alienBases.Count == 0 && _pass.ObjectiveBaseHuntingMissions == 0 || highest < CampaignTimeTrigger.TenMinutes) return null;
        var daily = highest >= CampaignTimeTrigger.OneDay;
        var work = daily ? _alienBases.Count * (1L + campaign.RegionStates.Count + campaign.CountryStates.Count) : 0;
        if (work > MaximumWorldWork) return AlienBaseWorkReason;
        var undiscovered = 0;
        var possibleMissions = _pass.ObjectiveBaseHuntingMissions;
        foreach (var alienBase in _alienBases)
        {
            if (daily && BaseDestroyedAfterDailyResearch(alienBase, guaranteedResearch)) continue;
            if (!alienBase.Discovered) undiscovered++;
            var rule = AlienBaseRule(alienBase);
            if (AlienBaseHuntingReason(alienBase, rule, highest, ref work, out var canHunt) is { } huntingReason) return huntingReason;
            if (canHunt) possibleMissions++;
            if (highest < CampaignTimeTrigger.OneDay) continue;
            work += rule.GenMission.Count;
            if (work > MaximumWorldWork) return AlienBaseWorkReason;
            ulong total = 0;
            var canGenerate = false;
            foreach (var choice in rule.GenMission)
            {
                if (choice.Value > (ulong)int.MaxValue - total)
                    return ForecastLimits.AlienBaseMissionWeightsRange;
                total += choice.Value;
                if (choice.Value == 0 || choice.Key.Length == 0) continue;
                if (!campaign.Content.RuntimeRules.AlienMissions.TryGet(choice.Key, out var mission))
                    return "Alien base generation references an undefined alien mission.";
                if (alienBase.GenMissionCount < rule.GenMissionLimit && rule.GenMissionFrequency > 0)
                {
                    canGenerate = true;
                    try
                    {
                        if (AlienBaseGenerationReason(alienBase, rule,
                                campaign.Content.RuntimeRules.AlienMissions[mission].Value, ref work, choice.Key) is { } reason)
                            return reason;
                    }
                    catch (InvalidDataException error) { return error.Message; }
                }
            }
            if (canGenerate) possibleMissions++;
        }
        if (possibleMissions != 0)
        {
            var next = campaign.PeekNextId("ALIEN_MISSIONS");
            if ((long)_missions.Count + possibleMissions > 100_000 || next <= 0 ||
                (long)next + possibleMissions > int.MaxValue)
                return ForecastLimits.AlienMissionIdsOrPopulationLimit;
            foreach (var mission in _missions)
                if (mission.Id >= next && mission.Id < (long)next + possibleMissions)
                    return ForecastLimits.AlienMissionIdsOrPopulationLimit;
            // Both generation callers precede the ten-minute retarget pass.
            // Existing hunters then search the enlarged mission list; reserve
            // that work even for delayed/no-object newly created missions.
            work += _ufos.Count;
            if (work > MaximumWorldWork) return AlienBaseWorkReason;
            var retargetWork = Math.Max(_pass.HuntingForecastWork, _pass.HuntingRetargetWorkBound) +
                (long)_pass.NewFlyingHunterBirths * possibleMissions;
            var escortActors = _pass.EscortMissionLookupActors;
            foreach (var saved in _ufos)
            {
                var projected = UfoForFlight(saved, highest);
                if (projected.Status == UfoStatus.Flying && projected.HunterKiller) retargetWork += possibleMissions;
                if (projected.Escorting) escortActors = Math.Max(escortActors, _ufos.Count);
            }
            retargetWork += escortActors * possibleMissions;
            if (retargetWork > MaximumWorldWork)
                return "UFO retargeting with generated missions exceeds the supported work limit.";
        }
        foreach (var owner in campaign.BaseStates)
        {
            if (daily)
            {
                work += (long)undiscovered * owner.Facilities.Count;
                if (work > MaximumWorldWork) return AlienBaseWorkReason;
            }
            foreach (var craft in owner.Crafts)
                if (craft.Logistics is { Status: "STR_OUT", Destination: null } state)
                {
                    if (!CraftLogistics.TryEffectiveSightRange(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
                            state.Weapons, campaign.Content.RuntimeRules, out var range))
                        return "Craft weapon sight bonuses exceed the supported range.";
                    if (range <= 0) continue;
                    work += _alienBases.Count;
                    if (work > MaximumWorldWork) return AlienBaseWorkReason;
                }
        }
        return null;
    }

    private void ProcessAlienBasesDaily(CampaignState.TimeEffects effects)
    {
        DestroyAlienBasesDaily();
        var rules = campaign.Content.RuntimeRules;
        foreach (var alienBase in _alienBases)
            ScoreUfoActivity(alienBase.Position, AlienBaseRule(alienBase).Points, rules);

        for (var index = 0; index < _alienBases.Count; index++) GenerateAlienBaseMission(index);
        for (var index = 0; index < _alienBases.Count; index++)
        {
            var alienBase = _alienBases[index];
            if (alienBase.Discovered) continue;
            foreach (var owner in campaign.BaseStates)
            {
                var distance = WorldGeometry.Distance(new(owner.Longitude, owner.Latitude), alienBase.Position);
                foreach (var facility in owner.Facilities)
                {
                    var rule = rules.Facilities[facility.Rule].Value;
                    if (WorldAlienBaseSight.FacilityChance(distance, rule.SightRange, rule.SightChance,
                            facility.BuildTime) is not { } chance || !Percent(chance)) continue;
                    alienBase = alienBase with { Discovered = true };
                    _alienBases[index] = alienBase;
                    effects.Notify(new AlienBaseDiscovered(alienBase.Id, alienBase.DeploymentId));
                    // Reference intentionally continues through the remaining facilities.
                }
            }
        }
    }

    private void DiscoverAlienBasesFromCraft(RuleHandle<CraftRuleFamily> craftRule, CraftLogisticsState state)
    {
        if (_alienBases.Count == 0 || state.Destination is not null) return;
        var rules = campaign.Content.RuntimeRules;
        if (!CraftLogistics.TryEffectiveSightRange(rules.Crafts[craftRule].Value, state.Weapons, rules, out var range))
            throw new InvalidOperationException("Craft sight range changed after world preflight.");
        if (range <= 0) return;
        for (var index = 0; index < _alienBases.Count; index++)
        {
            var alienBase = _alienBases[index];
            var distance = WorldGeometry.Distance(new(state.Longitude, state.Latitude), alienBase.Position);
            // RNG precedes the discovered flag check in the reference.
            if (WorldAlienBaseSight.CraftChance(distance, range) is { } chance && Percent(chance) && !alienBase.Discovered)
                _alienBases[index] = alienBase with { Discovered = true };
        }
    }
}
