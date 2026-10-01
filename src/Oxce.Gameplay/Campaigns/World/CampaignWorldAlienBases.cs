using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Alien-base activity and discovery, after the daily player-base loop.</summary>
internal sealed partial class CampaignWorld
{
    private const long MaximumAlienBaseChecks = 1_000_000;
    private const string AlienBaseWorkReason = "Alien base activity and sight checks exceed the supported work limit.";

    private void RegisterAlienBaseOperations(CampaignCapabilityRegistry registry) =>
        registry.Timed(CampaignTimeTrigger.OneDay, CampaignTimeOrder.DayWorldAlienBases,
            "alien base activity and facility discovery", ProcessAlienBasesDaily);

    private RuntimeAlienDeploymentRule AlienBaseRule(AlienBaseSnapshot alienBase) =>
        campaign.Content.RuntimeRules.AlienDeployments[
            campaign.Content.RuntimeRules.AlienDeployments.GetRequired(alienBase.DeploymentId)].Value;

    private string? AlienBaseActivityReason(CampaignTimeTrigger highest)
    {
        if (_alienBases.Count == 0 || highest < CampaignTimeTrigger.TenMinutes) return null;
        var daily = highest >= CampaignTimeTrigger.OneDay;
        var work = daily ? _alienBases.Count * (1L + campaign.RegionStates.Count + campaign.CountryStates.Count) : 0;
        if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        var undiscovered = 0;
        foreach (var alienBase in _alienBases)
        {
            if (!alienBase.Discovered) undiscovered++;
            var rule = AlienBaseRule(alienBase);
            if (rule.BaseDetectionRange > 0)
                return "Alien base hunting requires mission generation and hunter-killer simulation.";
            if (highest < CampaignTimeTrigger.OneDay) continue;
            if (rule.BaseSelfDestructCodeId.Length != 0)
                return "Alien base self-destruction requires mission-link and pact cleanup.";
            work += rule.GenMission.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            ulong total = 0;
            foreach (var choice in rule.GenMission)
            {
                if (choice.Value > (ulong)int.MaxValue - total)
                    return "Alien base mission weights exceed the supported range.";
                total += choice.Value;
                if (choice.Value == 0 || choice.Key.Length == 0) continue;
                if (!campaign.Content.RuntimeRules.AlienMissions.TryGet(choice.Key, out _))
                    return "Alien base generation references an undefined alien mission.";
                if (alienBase.GenMissionCount < rule.GenMissionLimit && rule.GenMissionFrequency > 0)
                    return "Alien base mission generation requires world simulation.";
            }
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
        var rules = campaign.Content.RuntimeRules;
        foreach (var alienBase in _alienBases)
            ScoreUfoActivity(alienBase.Position, AlienBaseRule(alienBase).Points, rules);

        // GenerateSupplyMission chooses its mission even when frequency is zero or
        // the limit is exhausted. Only its non-generating paths are owned here.
        foreach (var alienBase in _alienBases)
        {
            var rule = AlienBaseRule(alienBase);
            var total = 0;
            foreach (var choice in rule.GenMission) total = checked(total + (int)choice.Value);
            if (total == 0) continue;
            var roll = (ulong)campaign.Random.NextInclusive(1, total);
            foreach (var choice in rule.GenMission.OrderBy(static choice => choice.Key, StringComparer.Ordinal))
            {
                if (roll > choice.Value) { roll -= choice.Value; continue; }
                // A zero-frequency percent call still consumes a roll. Preflight
                // guarantees that any nonempty selection cannot generate a mission.
                if (choice.Key.Length != 0 && alienBase.GenMissionCount < rule.GenMissionLimit)
                    _ = Percent(rule.GenMissionFrequency);
                break;
            }
        }
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
