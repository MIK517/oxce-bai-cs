using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private CraftLogisticsState ProducedCraftLogistics(BaseState owner, RuleHandle<CraftRuleFamily> craft)
    {
        var rules = _content.RuntimeRules;
        var definition = rules.Crafts[craft].Value;
        return CraftLogistics.Arrive(CraftLogistics.Purchase(definition, rules, owner.Longitude, owner.Latitude),
            definition, rules, owner.Longitude, owner.Latitude);
    }

    private string? ProducedCraftServiceReason(BaseState owner, RuleHandle<CraftRuleFamily> craft)
    {
        try
        {
            var rules = _content.RuntimeRules;
            var definition = rules.Crafts[craft].Value;
            var state = ProducedCraftLogistics(owner, craft);
            if (!CraftLogistics.TryEffectiveServiceCapacities(definition, state.Weapons, rules, out var capacities) ||
                capacities.FuelMaximum < 0 || definition.RefuelRate < 0)
                return "Produced craft service values exceed the supported range.";
        }
        catch (Exception error) when (error is OverflowException or InvalidDataException or ArgumentOutOfRangeException)
        { return "Produced craft service values exceed the supported range."; }
        return null;
    }

    private string? ProductionCraftCapacity(IReadOnlyDictionary<RuleHandle<CraftRuleFamily>, long> births,
        long soldierBirths, CampaignTimeTrigger highest, ref long work)
    {
        IReadOnlyList<CustomCounterChange>? researchCounters = null;
        if (highest >= CampaignTimeTrigger.OneDay && ResearchPreflight.TryForecastDailyStocks(ref work, out var researchForecast))
            researchCounters = researchForecast.CounterChanges;
        long population = births.Values.Sum();
        foreach (var owner in _bases)
        {
            work += 1L + owner.Crafts.Count + owner.Transfers.Count;
            if (work > 1_000_000) return "Craft production exceeds the supported work limit.";
            population += owner.Crafts.Count + owner.Transfers.Count(transfer => !transfer.Delivered && transfer.Craft is not null);
        }
        if (population > 100_000) return "Craft production exceeds the supported population limit.";
        foreach (var birth in births)
        {
            work += _bases.Count + _bases.Sum(owner => owner.Crafts.Count + (long)owner.Transfers.Count);
            if (work > 1_000_000) return "Craft production exceeds the supported work limit.";
            var type = _content.RuntimeRules.Crafts.GetExternalId(birth.Key);
            if (ProducedCraftCounterRange(type, highest, researchCounters, ref work, out var range) is { } counterReason) return counterReason;
            var next = NextCraftId(type, range.Maximum);
            var quantity = birth.Value;
            if (type == "STR_SOLDIER" && soldierBirths != 0)
            {
                work += _bases.Count + _bases.Sum(owner => owner.Soldiers.Count + (long)owner.Transfers.Count);
                if (work > 1_000_000) return "Craft production exceeds the supported work limit.";
                next = Math.Max(next, NextSoldierId());
                quantity += soldierBirths;
            }
            if (quantity > int.MaxValue - next)
                return "Craft production exhausts the craft identity range.";
        }
        return null;
    }

    private string? ForecastProducedCraftIdentity(RuleHandle<CraftRuleFamily> craft,
        IReadOnlyDictionary<int, List<CraftState>> sources, Dictionary<string, long> counters,
        IReadOnlyList<CustomCounterChange>? researchCounters, CampaignTimeTrigger highest, ref long work, out int identity)
    {
        identity = 0;
        var type = _content.RuntimeRules.Crafts.GetExternalId(craft);
        if (!counters.TryGetValue(type, out var next))
        {
            if (ProducedCraftCounterRange(type, highest, researchCounters, ref work, out var range) is { } counterReason) return counterReason;
            if (range.Minimum != range.Maximum) return "Craft production requires daily research identity forecasts.";
            next = range.Maximum;
        }
        foreach (var owner in _bases)
        {
            work += 1L + sources[owner.Id].Count + owner.Transfers.Count * 2L;
            if (work > 1_000_000) return "Craft production exceeds the supported work limit.";
            foreach (var existing in sources[owner.Id])
                if (existing.Rule == craft) next = Math.Max(next, (long)existing.Id + 1);
            var removesDelivered = owner.Transfers.Any(transfer => !transfer.Delivered && transfer.Hours <= 1);
            foreach (var transfer in owner.Transfers)
                if (!(removesDelivered && (transfer.Delivered || transfer.Hours <= 1)) && transfer.Craft is { } pending && pending.RuleId == type)
                    next = Math.Max(next, (long)pending.Id + 1);
        }
        if (next >= int.MaxValue) return "Craft production exhausts the craft identity range.";
        identity = (int)next;
        counters[type] = next + 1;
        return null;
    }

    private string? ProducedCraftCounterRange(string type, CampaignTimeTrigger highest,
        IReadOnlyList<CustomCounterChange>? researchCounters, ref long work,
        out (long Minimum, long Maximum) range)
    {
        range = default;
        if (researchCounters is not null)
        {
            long next = _nextIds.GetValueOrDefault(type, 1);
            foreach (var change in researchCounters)
            {
                if (++work > 1_000_000) return "Craft production exceeds the supported work limit.";
                if (change.Name != type) continue;
                next = change.Increase ? next + 1 : Math.Max(1, next - 1);
                if (next > int.MaxValue) return "Craft production exhausts the craft identity range.";
            }
            range = (next, next);
            return null;
        }
        if (highest >= CampaignTimeTrigger.OneDay)
            foreach (var owner in _bases)
            {
                work += 1L + owner.Research.Count;
                if (work > 1_000_000) return "Craft production exceeds the supported work limit.";
                var finishing = false;
                foreach (var project in owner.Research)
                {
                    if ((long)project.Spent + project.Assigned < project.Cost) continue;
                    finishing = true;
                    var rule = _content.RuntimeRules.Research[project.Rule].Value;
                    work += rule.GetOneFree.Count + (long)rule.GetOneFreeProtected.Count;
                    foreach (var group in rule.GetOneFreeProtected) work += group.Topics.Count;
                    if (work > 1_000_000) return "Craft production exceeds the supported work limit.";
                }
                if (!finishing) continue;
                foreach (var entry in _content.RuntimeRules.Research.Rules)
                {
                    work += 1L + entry.Value.IncreaseCounters.Count + entry.Value.DecreaseCounters.Count;
                    if (work > 1_000_000) return "Craft production exceeds the supported work limit.";
                }
            }
        range = CustomCounters.BeforeWorldCreation(type, highest);
        return null;
    }
}
