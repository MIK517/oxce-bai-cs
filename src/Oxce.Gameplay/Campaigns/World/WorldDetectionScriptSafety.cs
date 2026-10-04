using Oxce.Scripting.Compilation;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Proves bounded detection programs cannot fail after the world tick starts.</summary>
internal static class WorldDetectionScriptSafety
{
    public static string? UnavailableReason(ScriptProgram program, Func<string, bool> bindingSupported)
    {
        foreach (var binding in program.Bindings)
            if (!bindingSupported(binding.Name)) return $"Detection script requires unsupported binding {binding.Name}.";
        if (program.Instructions.Count > ScriptExecutionOptions.Default.MaximumSteps)
            return "Detection script exceeds the supported instruction limit.";
        for (var index = 0; index < program.Instructions.Count; index++)
        {
            var instruction = program.Instructions[index];
            var operation = (CoreScriptOperation)instruction.Operation.Value;
            var operands = instruction.Operands;
            // A forward-only graph visits each instruction at most once, regardless of
            // radar RNG, saved values or earlier handlers changing the caller's inputs.
            foreach (var operand in operands)
                if (operand.Kind == ScriptOperandKind.Label && operand.Scalar <= index)
                    return "Detection script requires a bounded loop forecast.";
            var divisor = operation switch
            {
                CoreScriptOperation.Divide or CoreScriptOperation.Modulo => 1,
                CoreScriptOperation.MultiplyDivide => 2,
                CoreScriptOperation.OffsetModulo => 3,
                _ => -1,
            };
            if (divisor >= 0 && (operands[divisor].Kind != ScriptOperandKind.Scalar || operands[divisor].Scalar == 0))
                return "Detection script requires a nonzero divisor forecast.";
            if (operation is CoreScriptOperation.WaveRectangle or CoreScriptOperation.WaveSaw or
                CoreScriptOperation.WaveTriangle or CoreScriptOperation.WaveSine or CoreScriptOperation.WaveCosine &&
                (operands[1].Kind != ScriptOperandKind.Scalar || operands[1].Scalar <= 0))
                return "Detection script requires a positive wave-period forecast.";
            if (!Enum.IsDefined(operation)) return "Detection script contains an unsupported operation.";
        }
        return null;
    }
}
