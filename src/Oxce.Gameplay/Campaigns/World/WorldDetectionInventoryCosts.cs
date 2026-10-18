using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Validates native getCost dereferences before the strategic tick.</summary>
internal static class WorldDetectionInventoryCosts
{
    internal static (int Min, int Max) OutputRange(RuntimeRuleCatalog rules,
        HashSet<object?>? sources, HashSet<object?>? targets)
    {
        if (WorldDetectionReferenceForecast.OnlyNull(sources)) return (0, 0);
        if (targets is null || targets.Contains(null)) return WorldDetectionScalarRanges.Full;
        var range = sources is null || sources.Contains(null) ? (Min: 0, Max: 0) : (Min: int.MaxValue, Max: int.MinValue);
        var sourceRules = sources is null ? rules.Inventories.Rules.Select(static rule => rule.Value)
            : sources.OfType<RuntimeInventoryRule>();
        foreach (var source in sourceRules)
            foreach (var target in targets.OfType<RuntimeInventoryRule>())
            {
                var cost = 0;
                if (!ReferenceEquals(source, target) && !source.Costs.TryGetValue(target.Id, out cost))
                    return WorldDetectionScalarRanges.Full;
                range = (Math.Min(range.Min, cost), Math.Max(range.Max, cost));
            }
        return range.Min > range.Max ? WorldDetectionScalarRanges.Full : range;
    }

    internal static string? UnavailableReason(ScriptProgram program, RuntimeRuleCatalog rules, int[] visits, long maximumWork)
    {
        if (!program.Bindings.Any(static binding => binding.Name == "RuleInventory.getMoveToCost")) return null;
        var references = WorldDetectionReferenceForecast.Build(program, rules, visits, maximumWork);
        if (references is null) return ForecastLimits.DetectionInventoryCostForecastWork;
        long work = 0;
        for (var index = 0; index < program.Instructions.Count; index++)
        {
            if (visits[index] == 0 || !references.Reachable(index)) continue;
            var instruction = program.Instructions[index];
            if ((CoreScriptOperation)instruction.Operation.Value != CoreScriptOperation.HostCall) continue;
            var binding = program.Bindings.First(binding => binding.Id.Value == instruction.Operands[0].Scalar);
            if (binding.Name != "RuleInventory.getMoveToCost") continue;
            var sources = references.At(index, instruction.Operands[1]);
            if (WorldDetectionReferenceForecast.OnlyNull(sources) || instruction.Operands[1] == instruction.Operands[3]) continue;
            var targets = references.At(index, instruction.Operands[3]);
            if (targets is null || targets.Contains(null))
                return "Detection inventory movement requires a nonnull destination forecast.";
            work += (long)(sources?.Count ?? rules.Inventories.Count) * targets.Count;
            if (work > maximumWork) return ForecastLimits.DetectionInventoryCostForecastWork;
            var sourceRules = sources is null ? rules.Inventories.Rules.Select(static rule => rule.Value) : sources.OfType<RuntimeInventoryRule>();
            foreach (var source in sourceRules)
                foreach (var target in targets.OfType<RuntimeInventoryRule>())
                    if (!ReferenceEquals(source, target) && !source.Costs.ContainsKey(target.Id))
                        return $"Detection inventory movement requires missing cost {source.Id} -> {target.Id}.";
        }
        return null;
    }
}
