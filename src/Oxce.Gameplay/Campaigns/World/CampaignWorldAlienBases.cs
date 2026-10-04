using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Alien-base activity and discovery, after the daily player-base loop.</summary>
internal sealed partial class CampaignWorld
{
    private const long MaximumAlienBaseChecks = 1_000_000;
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
        if (_alienBases.Count == 0 && _objectiveBaseHuntingMissions == 0 || highest < CampaignTimeTrigger.TenMinutes) return null;
        var daily = highest >= CampaignTimeTrigger.OneDay;
        var work = daily ? _alienBases.Count * (1L + campaign.RegionStates.Count + campaign.CountryStates.Count) : 0;
        if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        var undiscovered = 0;
        var possibleMissions = _objectiveBaseHuntingMissions;
        foreach (var alienBase in _alienBases)
        {
            if (daily && BaseDestroyedAfterDailyResearch(alienBase, guaranteedResearch)) continue;
            if (!alienBase.Discovered) undiscovered++;
            var rule = AlienBaseRule(alienBase);
            if (AlienBaseHuntingReason(alienBase, rule, highest, ref work, out var canHunt) is { } huntingReason) return huntingReason;
            if (canHunt) possibleMissions++;
            if (highest < CampaignTimeTrigger.OneDay) continue;
            work += rule.GenMission.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            ulong total = 0;
            var canGenerate = false;
            foreach (var choice in rule.GenMission)
            {
                if (choice.Value > (ulong)int.MaxValue - total)
                    return "Alien base mission weights exceed the supported range.";
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
                                campaign.Content.RuntimeRules.AlienMissions[mission].Value, ref work) is { } reason)
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
                return "Alien mission IDs or population exceed the supported limit.";
            foreach (var mission in _missions)
                if (mission.Id >= next && mission.Id < (long)next + possibleMissions)
                    return "Alien mission IDs or population exceed the supported limit.";
        }
        foreach (var owner in campaign.BaseStates)
        {
            if (daily)
            {
                work += (long)undiscovered * owner.Facilities.Count;
                if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            }
            foreach (var craft in owner.Crafts)
                if (craft.Logistics is { Status: "STR_OUT", Destination: null } state)
                {
                    if (!CraftLogistics.TryEffectiveSightRange(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
                            state.Weapons, campaign.Content.RuntimeRules, out var range))
                        return "Craft weapon sight bonuses exceed the supported range.";
                    if (range <= 0) continue;
                    work += _alienBases.Count;
                    if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
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
