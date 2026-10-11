using Oxce.Scripting.Api;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Proves bounded detection programs cannot fail after the world tick starts.</summary>
internal static class WorldDetectionScriptSafety
{
    public static string? UnavailableReason(ScriptProgram program, Func<ScriptBindingDeclaration, bool> bindingSupported, out int[] visits, int countryCount = 0)
    {
        visits = Enumerable.Repeat(1, program.Instructions.Count).ToArray();
        foreach (var binding in program.Bindings)
            if (!bindingSupported(binding)) return $"Detection script requires unsupported binding {binding.Name}.";
        if (program.Instructions.Count > ScriptExecutionOptions.Default.MaximumSteps)
            return "Detection script exceeds the supported instruction limit.";
        var loops = new Dictionary<int, (int End, long Iterations)>();
        for (var index = 1; index < program.Instructions.Count - 1; index++)
            if (TryLiteralLoop(program, index, out var end, out var limit) ||
                TryCountryLoop(program, index, countryCount, out end, out limit))
                loops.Add(index, (end, Math.Max(0L, limit)));
        for (var index = 0; index < program.Instructions.Count; index++)
        {
            var instruction = program.Instructions[index];
            var operation = (CoreScriptOperation)instruction.Operation.Value;
            var operands = instruction.Operands;
            if (operation == CoreScriptOperation.HostCall && CountryLoopCall(program, index) &&
                !loops.Keys.Any(start => index == start - 1 || index == start + 1))
                return "Detection country enumeration requires a compiler-owned loop.";
            // Only compiler-owned counted-loop back edges are accepted. Hidden control
            // registers cannot depend on radar RNG, saved tags or preceding handlers.
            foreach (var operand in operands)
                if (operand.Kind == ScriptOperandKind.Label && operand.Scalar <= index &&
                    (operation != CoreScriptOperation.Jump || !loops.TryGetValue(operand.Scalar, out var loop) ||
                     index >= loop.End))
                    return "Detection script requires a bounded loop forecast.";
            if (!Enum.IsDefined(operation)) return "Detection script contains an unsupported operation.";
        }
        // Apply outer loops first explicitly. A skipped outer body must zero every
        // nested header before considering that nested loop's potentially huge limit.
        foreach (var (start, loop) in loops.OrderBy(static pair => pair.Key))
            for (var index = start; index < loop.End; index++)
            {
                var bound = visits[index] * (index == start ? loop.Iterations + 1 : loop.Iterations);
                if (bound > ScriptExecutionOptions.Default.MaximumSteps)
                    return "Detection script exceeds the supported instruction limit.";
                visits[index] = (int)bound;
            }
        if (visits.Sum(static count => (long)count) > ScriptExecutionOptions.Default.MaximumSteps)
            return "Detection script exceeds the supported instruction limit.";
        return null;
    }

    private static bool CountryLoopCall(ScriptProgram program, int index)
    {
        var instruction = program.Instructions[index];
        if ((CoreScriptOperation)instruction.Operation.Value != CoreScriptOperation.HostCall) return false;
        return program.Bindings.First(binding => binding.Id.Value == instruction.Operands[0].Scalar).Name is
            "GeoscapeGame.getCountries.init" or "GeoscapeGame.getCountries.list";
    }

    private static bool TryCountryLoop(ScriptProgram program, int start, int countries, out int end, out int limit)
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
        if (end <= start + 1 || end > instructions.Count || !CountryLoopCall(program, start - 1) ||
            !CountryLoopCall(program, start + 1)) return false;
        var initialize = instructions[start - 1];
        var step = instructions[start + 1];
        var back = instructions[end - 1];
        if (program.Bindings.First(binding => binding.Id.Value == initialize.Operands[0].Scalar).Name != "GeoscapeGame.getCountries.init" ||
            program.Bindings.First(binding => binding.Id.Value == step.Operands[0].Scalar).Name != "GeoscapeGame.getCountries.list" ||
            initialize.Operands.Count != 4 || step.Operands.Count != 5 ||
            initialize.Operands[1] != step.Operands[1] || initialize.Operands[2] != args[2] || initialize.Operands[3] != args[3] ||
            step.Operands[2] != args[2] || step.Operands[3] != args[3] ||
            (CoreScriptOperation)back.Operation.Value != CoreScriptOperation.Jump ||
            back.Operands.Count != 1 || back.Operands[0] != ScriptOperand.Label(start)) return false;
        for (var index = start + 2; index < end; index++)
            foreach (var operand in instructions[index].Operands)
                if (operand.Kind == ScriptOperandKind.Register &&
                    (operand.Scalar == args[2].Scalar || operand.Scalar == args[3].Scalar)) return false;
        limit = countries;
        return true;
    }

    private static bool TryLiteralLoop(ScriptProgram program, int start, out int end, out int limit)
    {
        end = limit = 0;
        var instructions = program.Instructions;
        if (start < 2 || start + 2 >= instructions.Count) return false;
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
            initialize.Operands[1].Kind is not (ScriptOperandKind.Scalar or ScriptOperandKind.Register) ||
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
        if (initialize.Operands[1].Kind == ScriptOperandKind.Scalar)
        {
            limit = initialize.Operands[1].Scalar;
            return true;
        }
        return TryLiteralRegisterBound(program, initialize.Operands[1].Scalar, out limit);
    }

    /// <summary>
    /// Bounds a local limit register (<c>var int count 1; loop var i count;</c>) whose
    /// only writes are literal Set/Clear instructions anywhere in the program. Locals
    /// start cleared, so the loop limit is at most the largest literal or zero.
    /// Inputs and outputs carry caller values and are never bounded this way.
    /// </summary>
    private static bool TryLiteralRegisterBound(ScriptProgram program, int register, out int limit)
    {
        limit = 0;
        foreach (var definition in program.Registers)
            if (definition.Offset == register && (definition.IsInput || definition.IsOutput)) return false;
        foreach (var instruction in program.Instructions)
        {
            var operation = (CoreScriptOperation)instruction.Operation.Value;
            var operands = instruction.Operands;
            for (var position = 0; position < operands.Count; position++)
            {
                if (operands[position] != ScriptOperand.Register(register)) continue;
                if (position == 0 && operation == CoreScriptOperation.Clear && operands.Count == 1) continue;
                if (position == 0 && operation == CoreScriptOperation.Set && operands.Count == 2 &&
                    operands[1].Kind == ScriptOperandKind.Scalar)
                {
                    limit = Math.Max(limit, operands[1].Scalar);
                    continue;
                }
                // Read-only uses: copies, comparisons and returned values.
                if (operation == CoreScriptOperation.Set && position == 1 ||
                    operation == CoreScriptOperation.BranchCondition && position is 2 or 3 ||
                    operation == CoreScriptOperation.Return) continue;
                return false;
            }
        }
        return true;
    }
}
