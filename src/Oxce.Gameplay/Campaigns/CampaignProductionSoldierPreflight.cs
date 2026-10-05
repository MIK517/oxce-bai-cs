using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private string? ProducedSoldierReason(RuntimeManufactureRule rule, int births, ref long work)
    {
        var definition = _content.RuntimeRules.Soldiers[_content.RuntimeRules.Soldiers.GetRequired(rule.SpawnedPersonType)].Value;
        long generationWork = 1L + definition.NamePools.Count + definition.MinimumStats.Count;
        foreach (var pool in definition.NamePools)
            generationWork += pool.LookWeights.Count + pool.MaleFirst.Count + pool.FemaleFirst.Count + pool.MaleCallsign.Count + pool.FemaleCallsign.Count;
        generationWork += _bases.Count + _bases.Sum(owner => owner.Soldiers.Count + (long)owner.Transfers.Count);
        work += births * generationWork * 10; // Native duplicate generation retries at most ten times.
        if (work > 1_000_000) return "Soldier production exceeds the supported work limit.";
        if (SoldierGeneration.GenerationRestriction(definition) is { } reason) return reason;
        if (rule.SpawnedSoldierTemplate is not { } template) return null;
        work += births * (long)(template.InitialStats.Count + template.CurrentStats.Count +
            (template.TransformationBonuses?.Count ?? 0) + (template.PreviousTransformations?.Count ?? 0));
        if (work > 1_000_000) return "Soldier production exceeds the supported work limit.";
        if (template.UnsupportedFields.Count != 0)
            return $"Soldier production template requires state for {string.Join(", ", template.UnsupportedFields)}.";
        if (template.RandomTransformationBonuses is { } weights && template.TransformationBonusesCount > 0)
        {
            work += births * (long)weights.Count * (1L + Math.Min(weights.Count, template.TransformationBonusesCount));
            if (work > 1_000_000) return "Soldier production exceeds the supported work limit.";
            if (weights.Where(pair => pair.Value > 0).Sum(pair => (long)pair.Value) > int.MaxValue)
                return "Soldier production template weights exceed the supported range.";
            if (weights.Any(pair => pair.Value > 0 && pair.Key.Length != 0 && pair.Key != "\0" &&
                template.TransformationBonuses?.GetValueOrDefault(pair.Key) == int.MaxValue))
                return "A possible soldier production template bonus exceeds the counter range.";
        }
        return null;
    }

    private string? ProductionSoldierCapacity(long births, IReadOnlyDictionary<RuleHandle<CraftRuleFamily>, long>? craftBirths,
        CampaignTimeTrigger highest, ref long work)
    {
        long population = births;
        foreach (var owner in _bases)
        {
            work += 1L + owner.Soldiers.Count + owner.Transfers.Count;
            if (work > 1_000_000) return "Soldier production exceeds the supported work limit.";
            population += owner.Soldiers.Count + owner.Transfers.Count(transfer => !transfer.Delivered && transfer.Soldier is not null);
        }
        if (population > 100_000) return "Soldier production exceeds the supported population limit.";
        IReadOnlyList<CustomCounterChange>? counters = null;
        if (highest >= CampaignTimeTrigger.OneDay && ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast))
            counters = forecast.CounterChanges;
        if (ProducedCraftCounterRange("STR_SOLDIER", highest, counters, ref work, out var range) is { } reason)
            return reason.Replace("Craft", "Soldier", StringComparison.Ordinal).Replace("craft", "soldier", StringComparison.Ordinal);
        work += _bases.Count + _bases.Sum(owner => owner.Soldiers.Count + (long)owner.Transfers.Count);
        if (work > 1_000_000) return "Soldier production exceeds the supported work limit.";
        long next = range.Maximum;
        foreach (var owner in _bases)
        {
            foreach (var soldier in owner.Soldiers) next = Math.Max(next, (long)soldier.Id + 1);
            foreach (var transfer in owner.Transfers)
                if (transfer.Soldier is { } soldier) next = Math.Max(next, (long)soldier.Id + 1);
        }
        if (_content.RuntimeRules.Crafts.TryGet("STR_SOLDIER", out var craft) && craftBirths?.GetValueOrDefault(craft) is > 0 and var shared)
        {
            work += _bases.Count + _bases.Sum(owner => owner.Crafts.Count + (long)owner.Transfers.Count);
            if (work > 1_000_000) return "Soldier production exceeds the supported work limit.";
            next = Math.Max(next, NextCraftId("STR_SOLDIER", range.Maximum));
            births += shared;
        }
        return births > int.MaxValue - next ? "Soldier production exhausts the soldier identity range." : null;
    }
}
