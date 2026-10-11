using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Native static Mod wrappers dereference their receiver; validate aliases at each call.</summary>
internal static class WorldDetectionModSafety
{
    internal static string? UnavailableReason(ScriptProgram program, RuntimeRuleCatalog rules, int[] visits, long maximumWork)
    {
        if (!program.Bindings.Any(static binding => binding.Name == "RuleMod.getSmokeReduction")) return null;
        var references = WorldDetectionReferenceForecast.Build(program, rules, visits, maximumWork);
        if (references is null) return "Detection Mod receiver forecast exceeds the supported work limit.";
        var bindingIds = program.Bindings.Where(static binding => binding.Name == "RuleMod.getSmokeReduction")
            .Select(static binding => binding.Id.Value).ToHashSet();
        for (var index = 0; index < program.Instructions.Count; index++)
        {
            if (visits[index] == 0 || !references.Reachable(index)) continue;
            var instruction = program.Instructions[index];
            if ((CoreScriptOperation)instruction.Operation.Value != CoreScriptOperation.HostCall ||
                !bindingIds.Contains(instruction.Operands[0].Scalar)) continue;
            var receivers = references.At(index, instruction.Operands[1]);
            if (receivers is not { Count: 1 } || !receivers.Contains(rules))
                return "Detection smoke reduction requires a guaranteed Mod receiver.";
        }
        return null;
    }
}
