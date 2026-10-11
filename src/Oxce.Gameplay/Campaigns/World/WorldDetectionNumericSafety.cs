using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Proves consumed divisors and wave periods safe at their actual instruction.</summary>
internal static class WorldDetectionNumericSafety
{
    private static int ConsumedOperand(CoreScriptOperation operation) => operation switch
    {
        CoreScriptOperation.Divide or CoreScriptOperation.Modulo => 1,
        CoreScriptOperation.MultiplyDivide => 2,
        CoreScriptOperation.OffsetModulo => 3,
        CoreScriptOperation.WaveRectangle or CoreScriptOperation.WaveSaw or CoreScriptOperation.WaveTriangle or
            CoreScriptOperation.WaveSine or CoreScriptOperation.WaveCosine => 1,
        _ => -1,
    };

    internal static string? UnavailableReason(ScriptProgram program, RuntimeRuleCatalog rules, int[] visits, long maximumWork)
    {
        if (!program.Instructions.Any(instruction => ConsumedOperand((CoreScriptOperation)instruction.Operation.Value) >= 0))
            return null;
        var references = WorldDetectionReferenceForecast.Build(program, rules, visits, maximumWork);
        if (references is null) return "Detection numeric safety forecast exceeds the supported work limit.";
        var values = WorldDetectionScalarForecast.Build(program, rules, references, visits, maximumWork);
        if (values is null) return "Detection numeric safety forecast exceeds the supported work limit.";
        for (var index = 0; index < program.Instructions.Count; index++)
        {
            if (visits[index] == 0 || !references.Reachable(index) || !values.Reachable(index)) continue;
            var instruction = program.Instructions[index];
            var operation = (CoreScriptOperation)instruction.Operation.Value;
            var operand = ConsumedOperand(operation);
            if (operand < 0) continue;
            var range = values.At(index, instruction.Operands[operand]);
            var wave = operation is CoreScriptOperation.WaveRectangle or CoreScriptOperation.WaveSaw or
                CoreScriptOperation.WaveTriangle or CoreScriptOperation.WaveSine or CoreScriptOperation.WaveCosine;
            if (wave ? range.Min <= 0 : range.Min <= 0 && range.Max >= 0)
                return wave ? "Detection script requires a positive wave-period forecast." :
                    "Detection script requires a nonzero divisor forecast.";
        }
        return null;
    }
}
