using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private string? ReserveDailyResearchScores(ref ResearchScoreCapacity capacity, ref long work)
    {
        if (!_bases.Any(owner => owner.Research.Any(project => (long)project.Spent + project.Assigned >= project.Cost))) return null;
        var rules = _content.RuntimeRules.Research;
        long automaticPositive = 0;
        long automaticNegative = 0;
        foreach (var entry in rules.Rules)
        {
            if (++work > 1_000_000) return "Research score forecasts exceed the supported work limit.";
            if (entry.Value.Cost != 0) continue;
            automaticPositive += Math.Max(0, (long)entry.Value.Points);
            automaticNegative += Math.Min(0, (long)entry.Value.Points);
        }
        foreach (var owner in _bases)
            foreach (var project in owner.Research)
            {
                if (++work > 1_000_000) return "Research score forecasts exceed the supported work limit.";
                if ((long)project.Spent + project.Assigned < project.Cost) continue;
                var rule = rules[project.Rule].Value;
                var primary = Bounds(rule);
                long bonusPositive = 0;
                long bonusNegative = 0;
                // Earlier completions can expose protected/free choices, or re-enable them.
                // Reserve each possible reward without selecting it or consuming RNG.
                foreach (var id in rule.GetOneFree.Concat(rule.GetOneFreeProtected.SelectMany(group => group.Topics)))
                {
                    if (++work > 1_000_000) return "Research score forecasts exceed the supported work limit.";
                    if (!rules.TryGet(id, out var reward)) continue;
                    var bonus = Bounds(rules[reward].Value);
                    bonusPositive = Math.Max(bonusPositive, bonus.Positive);
                    bonusNegative = Math.Min(bonusNegative, bonus.Negative);
                }
                if (!capacity.Reserve(primary.Negative + bonusNegative, primary.Positive + bonusPositive))
                    return "Research and production scores exceed the supported range.";
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
