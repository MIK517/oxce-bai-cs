using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    string? ICampaignProductionPreflight.ScoreBounds(CampaignTimeTrigger highest, ref long work, out long minimum, out long maximum)
    {
        minimum = maximum = 0;
        if (highest < CampaignTimeTrigger.OneHour) return null;
        foreach (var owner in _bases)
        {
            if (++work > ForecastLimits.Work) return ForecastLimits.MissionResearchDiscoveryWork;
            if (ForecastProductionAssignments(owner, highest, out var assignments, ref work) is { } staffingReason) return staffingReason;
            for (var index = 0; index < owner.Productions.Count; index++)
            {
                if (++work > ForecastLimits.Work) return ForecastLimits.MissionResearchDiscoveryWork;
                var project = owner.Productions[index];
                var rule = _content.RuntimeRules.Manufacture[project.Rule].Value;
                if (rule.Points == 0) continue;
                work += 2L * rule.RequiredMaterials.Count + 2L * rule.ProducedMaterials.Count + rule.RandomProducedItems.Count + (long)rule.Events.Count;
                if (work > ForecastLimits.Work) return ForecastLimits.MissionResearchDiscoveryWork;
                foreach (var output in rule.RandomProducedItems)
                {
                    work += output.Items.Count;
                    if (work > ForecastLimits.Work) return ForecastLimits.MissionResearchDiscoveryWork;
                }
                if (ProductionRuleInvalid(rule) is { } invalidReason) return invalidReason;
                var units = ProductionProgress.Requested(rule.Time, project.Spent, assignments[index], project.Amount, project.Infinite);
                var change = (long)rule.Points * units;
                // Production may stop before its requested completions. Its
                // interval includes zero and never credits an optional offset.
                var lower = (Int128)minimum + Math.Min(0, change);
                var upper = (Int128)maximum + Math.Max(0, change);
                if (lower < long.MinValue || upper > long.MaxValue) return ForecastLimits.MissionResearchScoreRange;
                minimum = (long)lower;
                maximum = (long)upper;
            }
        }
        return null;
    }

    private string? ReserveDailyResearchScores(ref ResearchScoreCapacity capacity, ref long work)
    {
        const string rangeReason = "Research and production scores exceed the supported range.";
        var previous = capacity;
        var reason = ReserveDailyResearchScoreBounds(ref capacity, ref work);
        if (reason != rangeReason) return reason;
        capacity = previous;
        return ReserveExactDailyResearchScores(ref capacity, ref work, advanceGuaranteed: false);
    }

    private string? ReserveExactDailyResearchScores(ref ResearchScoreCapacity capacity, ref long work, bool advanceGuaranteed)
    {
        const string rangeReason = "Research and production scores exceed the supported range.";
        const string workReason = "Research score forecasts exceed the supported work limit.";
        if (!ResearchPreflight.TryForecastDailyWorldStocks(ref work, out var forecast))
            return work > ForecastLimits.Work ? workReason : rangeReason;
        var prefix = capacity;
        if (!prefix.Reserve(forecast.MinimumScorePrefix!.Value, forecast.MaximumScorePrefix!.Value)) return rangeReason;
        var minimum = forecast.ScoreChange ?? long.MaxValue;
        var maximum = forecast.ScoreChange ?? long.MinValue;
        foreach (var branch in forecast.DiscoveryAlternatives)
        {
            if (++work > ForecastLimits.Work) return workReason;
            minimum = Math.Min(minimum, branch.ScoreChange!.Value);
            maximum = Math.Max(maximum, branch.ScoreChange!.Value);
        }
        var supported = advanceGuaranteed ? capacity.AdvanceGuaranteed(forecast.MinimumScorePrefix.Value,
            forecast.MaximumScorePrefix.Value, minimum, maximum) : capacity.Reserve(minimum, maximum);
        return supported ? null : rangeReason;
    }

    private string? ReserveDailyResearchScoreBounds(ref ResearchScoreCapacity capacity, ref long work)
    {
        if (!_bases.Any(owner => owner.Research.Any(project => (long)project.Spent + project.Assigned >= project.Cost))) return null;
        var rules = _content.RuntimeRules.Research;
        long automaticPositive = 0;
        long automaticNegative = 0;
        foreach (var entry in rules.Rules)
        {
            if (++work > ForecastLimits.Work) return ForecastLimits.ResearchScoreForecastsWork;
            if (entry.Value.Cost != 0) continue;
            automaticPositive += Math.Max(0, (long)entry.Value.Points);
            automaticNegative += Math.Min(0, (long)entry.Value.Points);
        }
        foreach (var owner in _bases)
            foreach (var project in owner.Research)
            {
                if (++work > ForecastLimits.Work) return ForecastLimits.ResearchScoreForecastsWork;
                if ((long)project.Spent + project.Assigned < project.Cost) continue;
                var rule = rules[project.Rule].Value;
                var primary = Bounds(rule);
                long bonusPositive = 0;
                long bonusNegative = 0;
                // Earlier completions can expose protected/free choices, or re-enable them.
                // Reserve each possible reward without selecting it or consuming RNG.
                work += rule.GetOneFreeProtected.Count;
                if (work > ForecastLimits.Work) return ForecastLimits.ResearchScoreForecastsWork;
                foreach (var id in rule.GetOneFree.Concat(rule.GetOneFreeProtected.SelectMany(group => group.Topics)))
                {
                    if (++work > ForecastLimits.Work) return ForecastLimits.ResearchScoreForecastsWork;
                    if (!rules.TryGet(id, out var reward)) continue;
                    var bonus = Bounds(rules[reward].Value);
                    bonusPositive = Math.Max(bonusPositive, bonus.Positive);
                    bonusNegative = Math.Min(bonusNegative, bonus.Negative);
                }
                if (!capacity.Reserve(primary.Negative + bonusNegative, primary.Positive + bonusPositive))
                    return ForecastLimits.ResearchAndProductionScoresRange;
            }
        return null;

        (long Negative, long Positive) Bounds(RuntimeResearchRule rule)
        {
            var negative = Math.Min(0, (long)rule.Points) + automaticNegative;
            var positive = Math.Max(0, (long)rule.Points) + automaticPositive;
            if (rule.Lookup.Length != 0 && rules.TryGet(rule.Lookup, out var lookup))
            {
                var points = rules[lookup].Value.Points;
                negative += Math.Min(0, (long)points) + automaticNegative;
                positive += Math.Max(0, (long)points) + automaticPositive;
            }
            return (negative, positive);
        }
    }
}
