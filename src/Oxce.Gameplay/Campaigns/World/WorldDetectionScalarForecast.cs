using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Bounded scalar intervals at each call, including immutable RuleItem power readers and bounded scalar arithmetic.</summary>
internal sealed class WorldDetectionScalarForecast
{
    private readonly Dictionary<int, (int Min, int Max)>?[] _states;
    private WorldDetectionScalarForecast(Dictionary<int, (int Min, int Max)>?[] states) => _states = states;

    internal bool Reachable(int index) => _states[index] is not null;

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
                if (WorldDetectionItemQueries.Supports(binding.Name))
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
                            ? WorldDetectionItemQueries.Value(binding.Name, item) : 0;
                        range = (Math.Min(range.Min, value), Math.Max(range.Max, value));
                    }
                    if (range.Min > range.Max) range = (int.MinValue, int.MaxValue);
                }
                if (WorldDetectionRuleQueries.Supports(binding.Name))
                {
                    var candidates = references.At(index, operands[1]);
                    work += candidates?.Count ?? WorldDetectionRuleQueries.CandidateCount(binding.Name, rules);
                    if (work > maximumWork) return null;
                    range = candidates is null ? (0, 0) : (int.MaxValue, int.MinValue);
                    foreach (var candidate in candidates ?? WorldDetectionRuleQueries.Candidates(binding.Name, rules))
                    {
                        if (candidate is string id)
                        {
                            work += id.Length;
                            if (work > maximumWork) return null;
                        }
                        var value = WorldDetectionRuleQueries.Value(binding.Name, candidate, rules);
                        range = (Math.Min(range.Min, value), Math.Max(range.Max, value));
                    }
                    if (range.Min > range.Max) range = WorldDetectionScalarRanges.Full;
                }
                if (binding.Name == "RuleInventory.getMoveToCost")
                {
                    var sources = references.At(index, operands[1]);
                    var targets = references.At(index, operands[3]);
                    if (operands[1] == operands[3] || WorldDetectionReferenceForecast.OnlyNull(sources)) range = (0, 0);
                    else
                    {
                        work += (long)(sources?.Count ?? rules.Inventories.Count) * (targets?.Count ?? rules.Inventories.Count);
                        if (work > maximumWork) return null;
                        range = WorldDetectionInventoryCosts.OutputRange(rules, sources, targets);
                    }
                }
                if (WorldDetectionDamageQueries.IsClassification(binding.Name) ||
                    WorldDetectionDamageQueries.Supports(binding.Name) && binding.Name != "RuleDamageType.getRandomDamage")
                {
                    var candidates = references.At(index, operands[1]);
                    work += candidates?.Count ?? 2L * rules.Items.Count;
                    if (work > maximumWork) return null;
                    range = candidates is null ? (0, 0) : (int.MaxValue, int.MinValue);
                    var profiles = candidates is null
                        ? rules.Items.Rules.SelectMany(static item => new object?[] { item.Value.Damage, item.Value.MeleeDamage })
                        : candidates;
                    var power = binding.Name == "RuleDamageType.getArmorEffectiveness"
                        ? (0, 0) : operands.Count > 3 ? Read(operands[3], incoming) : (0, 0);
                    foreach (var candidate in profiles)
                    {
                        var value = candidate is RuntimeItemDamageRule profile
                            ? WorldDetectionDamageQueries.OutputRange(binding.Name, profile, power) : (Min: 0, Max: 0);
                        range = (Math.Min(range.Min, value.Min), Math.Max(range.Max, value.Max));
                    }
                    if (range.Min > range.Max) range = WorldDetectionScalarRanges.Full;
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
                Write(operands[0], WorldDetectionScalarRanges.Apply(operation, Read(operands[0], incoming),
                    operands.Count > 1 ? Read(operands[1], incoming) : WorldDetectionScalarRanges.Full,
                    operands.Count > 2 ? Read(operands[2], incoming) : WorldDetectionScalarRanges.Full,
                    operands.Count > 3 ? Read(operands[3], incoming) : WorldDetectionScalarRanges.Full));
            if (operation is CoreScriptOperation.Exit or CoreScriptOperation.Return) continue;
            if (operation == CoreScriptOperation.Jump) Join(operands[0].Scalar);
            else
            {
                Join(index + 1, operation == CoreScriptOperation.BranchCondition ? true : null);
                if (operation == CoreScriptOperation.BranchCondition) Join(operands[^1].Scalar, false);
            }
            if (work > maximumWork) return null;

            void Write(ScriptOperand destination, (int Min, int Max) value)
            {
                if (destination.Kind == ScriptOperandKind.Register && state.ContainsKey(destination.Scalar))
                    state[destination.Scalar] = value;
            }

            void Join(int target, bool? branch = null)
            {
                if (target >= states.Length || !references.Reachable(target)) return;
                work += state.Count;
                if (work > maximumWork) return;
                var outgoing = state;
                if (branch is { } matches &&
                    (operands.Count == 5 || ((ScriptConditionKind)operands[0].Scalar == ScriptConditionKind.All) == matches))
                {
                    work += state.Count;
                    if (work > maximumWork) return;
                    outgoing = new(state);
                    for (var clause = 1; clause < operands.Count - 1; clause += 3)
                    {
                        var leftOperand = operands[clause + 1];
                        var rightOperand = operands[clause + 2];
                        if (leftOperand.Kind is not (ScriptOperandKind.Scalar or ScriptOperandKind.Register) ||
                            rightOperand.Kind is not (ScriptOperandKind.Scalar or ScriptOperandKind.Register)) continue;
                        var left = Read(leftOperand, outgoing);
                        var right = Read(rightOperand, outgoing);
                        if (!WorldDetectionScalarConditions.Refine((ScriptConditionKind)operands[clause].Scalar,
                            matches, ref left, ref right)) return;
                        if (leftOperand.Kind == ScriptOperandKind.Register && outgoing.ContainsKey(leftOperand.Scalar))
                            outgoing[leftOperand.Scalar] = left;
                        if (rightOperand.Kind == ScriptOperandKind.Register && outgoing.ContainsKey(rightOperand.Scalar))
                            outgoing[rightOperand.Scalar] = right;
                    }
                }
                bool changed;
                if (states[target] is not { } existing)
                {
                    states[target] = new(outgoing);
                    changed = true;
                }
                else
                {
                    changed = false;
                    foreach (var (register, value) in outgoing)
                    {
                        var current = existing[register];
                        var merged = (Math.Min(current.Min, value.Min), Math.Max(current.Max, value.Max));
                        if (merged == current) continue;
                        // Loop values grow independently of the hidden iteration count. Widen back edges
                        // before another transfer so arithmetic cycles reach a bounded fixed point.
                        if (target <= index)
                            merged = (merged.Item1 < current.Min ? int.MinValue : current.Min,
                                merged.Item2 > current.Max ? int.MaxValue : current.Max);
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
