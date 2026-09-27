using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal static class WorldEventScriptSelection
{
    // The caller schedules only AFTER all three choices: a single command can pick the
    // same event through sequential, one-time random and repeatable selection.
    public static IReadOnlyList<RuntimeRuleReference<EventRuleFamily>> Select(RuntimeEventScriptRule rule,
        int month, IReadOnlyDictionary<string, int> history, IRandomSource random)
    {
        var selected = new List<RuntimeRuleReference<EventRuleFamily>>(3);
        foreach (var candidate in rule.Sequential)
            if (!history.ContainsKey(candidate.Id)) { AddRequired(candidate); break; }
        var choices = rule.Random.Where(choice => !history.ContainsKey(choice.Rule.Id)).ToArray();
        if (WorldScriptSelection.Choose(choices, random) is { } oneTime) AddRequired(oneTime);
        if (WorldScriptSelection.Generate(rule.Events, unchecked((ulong)month), random, allowEmptyTimeline: true)
            is { Handle: not null } repeated) selected.Add(repeated);
        return selected;

        void AddRequired(RuntimeRuleReference<EventRuleFamily> candidate)
        {
            if (!candidate.IsResolved) throw new InvalidDataException($"Required event '{candidate.Id}' does not exist.");
            selected.Add(candidate);
        }
    }
}
