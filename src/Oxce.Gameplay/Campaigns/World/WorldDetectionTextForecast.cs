using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Types;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Finite text-name sets at each instruction; unknown host outputs remain unknown.</summary>
internal sealed class WorldDetectionTextForecast
{
    private readonly Dictionary<int, HashSet<string>?>?[] _states;
    private WorldDetectionTextForecast(Dictionary<int, HashSet<string>?>?[] states) => _states = states;

    internal HashSet<string>? At(int index, ScriptOperand operand) => Read(operand, _states[index]);

    private static HashSet<string>? Read(ScriptOperand operand, Dictionary<int, HashSet<string>?>? state) =>
        operand.Kind == ScriptOperandKind.Text ? new(StringComparer.Ordinal) { operand.Text! } :
        operand.Kind == ScriptOperandKind.Register && state is not null && state.TryGetValue(operand.Scalar, out var value)
            ? value : null;

    internal static WorldDetectionTextForecast? Build(ScriptProgram program, int[] visits, long maximumWork, out long work, RuntimeRuleCatalog? rules = null)
    {
        work = 0;
        var states = new Dictionary<int, HashSet<string>?>?[program.Instructions.Count];
        if (states.Length == 0) return new(states);
        states[0] = program.Registers.Where(static register => register.Type.Id == ScriptPrimitiveTypes.Text && !register.Type.IsReference)
            .DistinctBy(static register => register.Offset)
            .ToDictionary(static register => register.Offset, static register => register.IsInput || register.IsOutput
                ? null : new HashSet<string>(StringComparer.Ordinal) { "" });
        var queue = new Queue<int>();
        var queued = new bool[states.Length];
        var bindings = program.Bindings.ToDictionary(static binding => binding.Id.Value);
        queue.Enqueue(0);
        queued[0] = true;
        long usedWork = 0;
        while (queue.TryDequeue(out var index))
        {
            queued[index] = false;
            if (++usedWork > maximumWork) break;
            if (visits[index] == 0) continue;
            var incoming = states[index]!;
            usedWork += Size(incoming);
            if (usedWork > maximumWork) break;
            var state = new Dictionary<int, HashSet<string>?>(incoming);
            var instruction = program.Instructions[index];
            var operands = instruction.Operands;
            var operation = (CoreScriptOperation)instruction.Operation.Value;
            if (operation == CoreScriptOperation.Set) Write(operands[0], Read(operands[1], incoming));
            else if (operation == CoreScriptOperation.Clear) Write(operands[0], null);
            else if (operation == CoreScriptOperation.Swap)
            {
                Write(operands[0], Read(operands[1], incoming));
                Write(operands[1], Read(operands[0], incoming));
            }
            else if (operation == CoreScriptOperation.HostCall)
            {
                var binding = bindings[operands[0].Scalar];
                HashSet<string>? values = binding.Name == "set" && binding.Parameters.Count == 2
                    ? Read(operands[2], incoming) : null;
                if (rules is not null && WorldDetectionTextQueries.Supports(binding.Name))
                {
                    usedWork += WorldDetectionTextQueries.CandidateCount(binding.Name, rules) + 1;
                    if (usedWork > maximumWork) break;
                    values = new(StringComparer.Ordinal) { "" };
                    foreach (var candidate in WorldDetectionTextQueries.Candidates(binding.Name, rules))
                    {
                        var text = WorldDetectionTextQueries.Value(candidate);
                        usedWork += 1L + text.Length;
                        if (usedWork > maximumWork) break;
                        values.Add(text);
                    }
                    if (usedWork > maximumWork) break;
                }
                var runtimeOperand = 0;
                foreach (var parameter in binding.Parameters)
                {
                    if (parameter.Type.Id == ScriptPrimitiveTypes.Separator) continue;
                    var destination = operands[++runtimeOperand];
                    if (parameter.Writable)
                        Write(destination, values);
                }
            }
            else if (operation is not (CoreScriptOperation.Exit or CoreScriptOperation.Return or
                CoreScriptOperation.Jump or CoreScriptOperation.BranchCondition)) Write(operands[0], null);
            if (operation is CoreScriptOperation.Exit or CoreScriptOperation.Return) continue;
            if (operation == CoreScriptOperation.Jump) Join(operands[0].Scalar);
            else
            {
                Join(index + 1);
                if (operation == CoreScriptOperation.BranchCondition) Join(operands[^1].Scalar);
            }
            if (usedWork > maximumWork) break;

            void Write(ScriptOperand destination, HashSet<string>? value)
            {
                if (destination.Kind == ScriptOperandKind.Register && state.ContainsKey(destination.Scalar))
                    state[destination.Scalar] = value;
            }

            void Join(int target)
            {
                if (target >= states.Length) return;
                usedWork += Size(state);
                if (usedWork > maximumWork) return;
                var changed = false;
                if (states[target] is not { } existing)
                {
                    states[target] = new(state);
                    changed = true;
                }
                else
                {
                    foreach (var (register, value) in state)
                    {
                        var current = existing[register];
                        if (current is null) continue;
                        if (value is null)
                        {
                            existing[register] = null;
                            changed = true;
                            continue;
                        }
                        usedWork += TextSize(current) + TextSize(value);
                        if (usedWork > maximumWork) return;
                        if (value.IsSubsetOf(current)) continue;
                        var merged = new HashSet<string>(current, StringComparer.Ordinal);
                        merged.UnionWith(value);
                        existing[register] = merged;
                        changed = true;
                    }
                }
                if (changed && !queued[target])
                {
                    queue.Enqueue(target);
                    queued[target] = true;
                }
            }
        }
        work = usedWork;
        return usedWork > maximumWork ? null : new(states);
    }

    private static long Size(Dictionary<int, HashSet<string>?> state) =>
        state.Count + state.Values.Sum(static values => TextSize(values));
    private static long TextSize(HashSet<string>? values) => values?.Sum(static value => 1L + value.Length) ?? 1;
}
