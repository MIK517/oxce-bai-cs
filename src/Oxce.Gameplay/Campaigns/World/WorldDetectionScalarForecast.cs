using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Bounded scalar intervals at each call, including immutable RuleItem power readers.</summary>
internal sealed class WorldDetectionScalarForecast
{
    private readonly Dictionary<int, (int Min, int Max)>?[] _states;
    private WorldDetectionScalarForecast(Dictionary<int, (int Min, int Max)>?[] states) => _states = states;

    internal (int Min, int Max) At(int index, ScriptOperand operand) => Read(operand, _states[index]);

    private static (int Min, int Max) Read(ScriptOperand operand, Dictionary<int, (int Min, int Max)>? state) =>
        operand.Kind == ScriptOperandKind.Scalar ? (operand.Scalar, operand.Scalar) :
        operand.Kind == ScriptOperandKind.Register && state is not null && state.TryGetValue(operand.Scalar, out var range)
            ? range : (int.MinValue, int.MaxValue);

    internal static WorldDetectionScalarForecast? Build(ScriptProgram program, RuntimeRuleCatalog rules,
        WorldDetectionReferenceForecast references, int[] visits, long maximumWork)
    {
        var states = new Dictionary<int, (int Min, int Max)>?[program.Instructions.Count];
        if (states.Length == 0) return new(states);
        states[0] = program.Registers.Where(static register => !register.Type.IsReference)
            .DistinctBy(static register => register.Offset)
            .ToDictionary(static register => register.Offset,
                static register => register.IsInput || register.IsOutput ? (int.MinValue, int.MaxValue) : (0, 0));
        var bindings = program.Bindings.ToDictionary(static binding => binding.Id.Value);
        var queue = new Queue<int>();
        var queued = new bool[states.Length];
        queue.Enqueue(0);
        queued[0] = true;
        long work = 0;
        while (queue.TryDequeue(out var index))
        {
            queued[index] = false;
            if (++work > maximumWork) return null;
            if (visits[index] == 0 || !references.Reachable(index)) continue;
            var incoming = states[index]!;
            work += incoming.Count;
            if (work > maximumWork) return null;
            var state = new Dictionary<int, (int Min, int Max)>(incoming);
            var instruction = program.Instructions[index];
            var operands = instruction.Operands;
            var operation = (CoreScriptOperation)instruction.Operation.Value;
            if (operation == CoreScriptOperation.Set) Write(operands[0], Read(operands[1], incoming));
            else if (operation == CoreScriptOperation.Clear) Write(operands[0], (0, 0));
            else if (operation == CoreScriptOperation.Swap)
            {
                Write(operands[0], Read(operands[1], incoming));
                Write(operands[1], Read(operands[0], incoming));
            }
            else if (operation == CoreScriptOperation.HostCall)
            {
                var binding = bindings[operands[0].Scalar];
                var range = (Min: int.MinValue, Max: int.MaxValue);
                if (binding.Name is "RuleItem.getPower" or "RuleItem.getMeleePower")
                {
                    var candidates = references.At(index, operands[1]);
                    work += candidates?.Count ?? rules.Items.Count;
                    if (work > maximumWork) return null;
                    range = candidates is null ? (0, 0) : (int.MaxValue, int.MinValue);
                    var items = candidates is null
                        ? rules.Items.Rules.Select(static rule => (object?)rule.Value) : candidates;
                    foreach (var candidate in items)
                    {
                        var value = candidate is RuntimeItemRule item
                            ? binding.Name == "RuleItem.getPower" ? item.Power : item.MeleePower : 0;
                        range = (Math.Min(range.Min, value), Math.Max(range.Max, value));
                    }
                    if (range.Min > range.Max) range = (int.MinValue, int.MaxValue);
                }
                if (binding.Name == "RuleDamageType.getRandomDamage" &&
                    (WorldDetectionReferenceForecast.OnlyNull(references.At(index, operands[1])) ||
                     WorldDetectionReferenceForecast.OnlyNull(references.At(index, operands[4]))))
                    range = (0, 0);
                for (var parameter = 0; parameter < binding.Parameters.Count; parameter++)
                    if (binding.Parameters[parameter].Writable) Write(operands[parameter + 1], range);
            }
            else if (operation is not (CoreScriptOperation.Exit or CoreScriptOperation.Return or
                CoreScriptOperation.Jump or CoreScriptOperation.BranchCondition))
                Write(operands[0], (int.MinValue, int.MaxValue));
            if (operation is CoreScriptOperation.Exit or CoreScriptOperation.Return) continue;
            if (operation == CoreScriptOperation.Jump) Join(operands[0].Scalar);
            else
            {
                Join(index + 1);
                if (operation == CoreScriptOperation.BranchCondition) Join(operands[^1].Scalar);
            }
            if (work > maximumWork) return null;

            void Write(ScriptOperand destination, (int Min, int Max) value)
            {
                if (destination.Kind == ScriptOperandKind.Register && state.ContainsKey(destination.Scalar))
                    state[destination.Scalar] = value;
            }

            void Join(int target)
            {
                if (target >= states.Length || !references.Reachable(target)) return;
                work += state.Count;
                if (work > maximumWork) return;
                bool changed;
                if (states[target] is not { } existing)
                {
                    states[target] = new(state);
                    changed = true;
                }
                else
                {
                    changed = false;
                    foreach (var (register, value) in state)
                    {
                        var current = existing[register];
                        var merged = (Math.Min(current.Min, value.Min), Math.Max(current.Max, value.Max));
                        if (merged == current) continue;
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
        return new(states);
    }
}
