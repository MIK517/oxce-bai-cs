using Oxce.Scripting.Api;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Proves bounded detection programs cannot fail after the world tick starts.</summary>
internal static class WorldDetectionScriptSafety
{
    public static string? UnavailableReason(ScriptProgram program, Func<ScriptBindingDeclaration, bool> bindingSupported, out int[] visits)
    {
        visits = Enumerable.Repeat(1, program.Instructions.Count).ToArray();
        foreach (var binding in program.Bindings)
            if (!bindingSupported(binding)) return $"Detection script requires unsupported binding {binding.Name}.";
        if (program.Instructions.Count > ScriptExecutionOptions.Default.MaximumSteps)
            return "Detection script exceeds the supported instruction limit.";
        var loops = new Dictionary<int, (int End, long Factor)>();
        for (var index = 2; index < program.Instructions.Count - 2; index++)
            if (TryLiteralLoop(program, index, out var end, out var limit))
                loops.Add(index, (end, Math.Max(0L, limit) + 1));
        for (var index = 0; index < program.Instructions.Count; index++)
        {
            var instruction = program.Instructions[index];
            var operation = (CoreScriptOperation)instruction.Operation.Value;
            var operands = instruction.Operands;
            // Only compiler-owned counted-loop back edges are accepted. Hidden control
            // registers cannot depend on radar RNG, saved tags or preceding handlers.
            foreach (var operand in operands)
                if (operand.Kind == ScriptOperandKind.Label && operand.Scalar <= index &&
                    (operation != CoreScriptOperation.Jump || !loops.TryGetValue(operand.Scalar, out var loop) ||
                     index >= loop.End))
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
        foreach (var (start, loop) in loops)
            for (var index = start; index < loop.End; index++)
            {
                var bound = visits[index] * loop.Factor;
                if (bound > ScriptExecutionOptions.Default.MaximumSteps)
                    return "Detection script exceeds the supported instruction limit.";
                visits[index] = (int)bound;
            }
        if (visits.Sum(static count => (long)count) > ScriptExecutionOptions.Default.MaximumSteps)
            return "Detection script exceeds the supported instruction limit.";
        return null;
    }

    private static bool TryLiteralLoop(ScriptProgram program, int start, out int end, out int limit)
    {
        end = limit = 0;
        var instructions = program.Instructions;
        var branch = instructions[start];
        if ((CoreScriptOperation)branch.Operation.Value != CoreScriptOperation.BranchCondition) return false;
        var args = branch.Operands;
        if (args.Count != 5 || args[0].Scalar != (int)ScriptConditionKind.All ||
            args[1].Scalar != (int)ScriptConditionKind.LessThan ||
            args[2].Kind != ScriptOperandKind.Register || args[3].Kind != ScriptOperandKind.Register ||
            args[4].Kind != ScriptOperandKind.Label) return false;
        end = args[4].Scalar;
        if (end <= start + 3 || end > instructions.Count) return false;
        var initialize = instructions[start - 2];
        var clear = instructions[start - 1];
        var expose = instructions[start + 1];
        var increment = instructions[start + 2];
        var back = instructions[end - 1];
        if ((CoreScriptOperation)initialize.Operation.Value != CoreScriptOperation.Set ||
            initialize.Operands.Count != 2 || initialize.Operands[0] != args[3] ||
            initialize.Operands[1].Kind != ScriptOperandKind.Scalar ||
            (CoreScriptOperation)clear.Operation.Value != CoreScriptOperation.Clear ||
            clear.Operands.Count != 1 || clear.Operands[0] != args[2] ||
            (CoreScriptOperation)expose.Operation.Value != CoreScriptOperation.Set ||
            expose.Operands.Count != 2 || expose.Operands[1] != args[2] ||
            (CoreScriptOperation)increment.Operation.Value != CoreScriptOperation.Add ||
            increment.Operands.Count != 2 || increment.Operands[0] != args[2] ||
            increment.Operands[1] != ScriptOperand.IntegerValue(1) ||
            (CoreScriptOperation)back.Operation.Value != CoreScriptOperation.Jump ||
            back.Operands.Count != 1 || back.Operands[0] != ScriptOperand.Label(start)) return false;
        // Neither control register may be touched by the user body, including nested
        // host calls. The compiler uses separate registers for the visible loop value.
        for (var index = start + 3; index < end; index++)
            foreach (var operand in instructions[index].Operands)
                if (operand.Kind == ScriptOperandKind.Register &&
                    (operand.Scalar == args[2].Scalar || operand.Scalar == args[3].Scalar)) return false;
        limit = initialize.Operands[1].Scalar;
        return true;
    }
}
