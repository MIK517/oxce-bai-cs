using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Types;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Finite text-name sets at each instruction; unknown host outputs remain unknown.</summary>
internal sealed class WorldDetectionTextForecast
{
    private readonly Dictionary<int, HashSet<string>?>?[] _states;
    private WorldDetectionTextForecast(Dictionary<int, HashSet<string>?>?[] states) => _states = states;

    internal bool Reachable(int index) => _states[index] is not null;

    internal HashSet<string>? At(int index, ScriptOperand operand) => Read(operand, _states[index]);

    private static HashSet<string>? Read(ScriptOperand operand, Dictionary<int, HashSet<string>?>? state) =>
        operand.Kind == ScriptOperandKind.Text ? new(StringComparer.Ordinal) { operand.Text! } :
        operand.Kind == ScriptOperandKind.Register && state is not null && state.TryGetValue(operand.Scalar, out var value)
            ? value : null;

    internal static WorldDetectionTextForecast? Build(ScriptProgram program, int[] visits, long maximumWork, out long work, RuntimeRuleCatalog? rules = null, WorldDetectionReferenceForecast? references = null)
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
            if (visits[index] == 0 || references is not null && !references.Reachable(index)) continue;
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
                    var candidates = references?.At(index, operands[1]);
                    usedWork += candidates?.Count ?? WorldDetectionTextQueries.CandidateCount(binding.Name, rules) + 1;
                    if (usedWork > maximumWork) break;
                    values = candidates is null ? new(StringComparer.Ordinal) { "" } : new(StringComparer.Ordinal);
                    foreach (var candidate in candidates ?? WorldDetectionTextQueries.Candidates(binding.Name, rules))
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
                Join(index + 1, operation == CoreScriptOperation.BranchCondition ? true : null);
                if (operation == CoreScriptOperation.BranchCondition) Join(operands[^1].Scalar, false);
            }
            if (usedWork > maximumWork) break;

            void Write(ScriptOperand destination, HashSet<string>? value)
            {
                if (destination.Kind == ScriptOperandKind.Register && state.ContainsKey(destination.Scalar))
                    state[destination.Scalar] = value;
            }

            void Join(int target, bool? branch = null)
            {
                if (target >= states.Length || references is not null && !references.Reachable(target)) return;
                usedWork += Size(state);
                if (usedWork > maximumWork) return;
                var outgoing = state;
                if (branch is { } selected)
                {
                    var all = (ScriptConditionKind)operands[0].Scalar == ScriptConditionKind.All;
                    var canBeTrue = all;
                    var canBeFalse = !all;
                    for (var clause = 1; clause < operands.Count - 1; clause += 3)
                    {
                        var condition = (ScriptConditionKind)operands[clause].Scalar;
                        var left = Read(operands[clause + 1], state);
                        var right = Read(operands[clause + 2], state);
                        var clauseTrue = true;
                        var clauseFalse = true;
                        if (left is not null && right is not null && condition is ScriptConditionKind.Equal or ScriptConditionKind.NotEqual)
                        {
                            usedWork += TextSize(left) + TextSize(right);
                            if (usedWork > maximumWork) return;
                            if (!left.Any(static value => value.Contains('\0')) && !right.Any(static value => value.Contains('\0')))
                            {
                                var equal = left.Overlaps(right);
                                var different = left.Count > 1 || right.Count > 1 || !equal;
                                clauseTrue = condition == ScriptConditionKind.Equal ? equal : different;
                                clauseFalse = condition == ScriptConditionKind.Equal ? different : equal;
                            }
                        }
                        canBeTrue = all ? canBeTrue && clauseTrue : canBeTrue || clauseTrue;
                        canBeFalse = all ? canBeFalse || clauseFalse : canBeFalse && clauseFalse;
                    }
                    if (selected ? !canBeTrue : !canBeFalse) return;
                }
                if (branch is { } matches &&
                    (operands.Count == 5 || ((ScriptConditionKind)operands[0].Scalar == ScriptConditionKind.All) == matches))
                {
                    usedWork += Size(state);
                    if (usedWork > maximumWork) return;
                    outgoing = new(state);
                    for (var clause = 1; clause < operands.Count - 1; clause += 3)
                    {
                        var condition = (ScriptConditionKind)operands[clause].Scalar;
                        if (condition is not (ScriptConditionKind.Equal or ScriptConditionKind.NotEqual)) continue;
                        var leftOperand = operands[clause + 1];
                        var rightOperand = operands[clause + 2];
                        var left = Read(leftOperand, outgoing);
                        var right = Read(rightOperand, outgoing);
                        if (left is null || right is null) continue;
                        usedWork += TextSize(left) + TextSize(right);
                        if (usedWork > maximumWork) return;
                        // Native strcmp observes NUL prefixes. Keep those domains conservative.
                        if (left.Any(static value => value.Contains('\0')) || right.Any(static value => value.Contains('\0'))) continue;
                        var equal = (condition == ScriptConditionKind.Equal) == matches;
                        var filteredLeft = new HashSet<string>(left.Where(value => equal ? right.Contains(value) :
                            right.Count > 1 || !right.Contains(value)), StringComparer.Ordinal);
                        var filteredRight = new HashSet<string>(right.Where(value => equal ? left.Contains(value) :
                            left.Count > 1 || !left.Contains(value)), StringComparer.Ordinal);
                        if (filteredLeft.Count == 0 || filteredRight.Count == 0) return;
                        WriteOutgoing(leftOperand, filteredLeft);
                        WriteOutgoing(rightOperand, filteredRight);
                    }
                }

                void WriteOutgoing(ScriptOperand operand, HashSet<string> values)
                {
                    if (operand.Kind == ScriptOperandKind.Register && outgoing.ContainsKey(operand.Scalar))
                        outgoing[operand.Scalar] = values;
                }
                var changed = false;
                if (states[target] is not { } existing)
                {
                    states[target] = new(outgoing);
                    changed = true;
                }
                else
                {
                    foreach (var (register, value) in outgoing)
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

    internal bool Equivalent(WorldDetectionTextForecast other, ref long work, long maximumWork)
    {
        for (var index = 0; index < _states.Length; index++)
        {
            if (++work > maximumWork) return false;
            var left = _states[index];
            var right = other._states[index];
            if (left is null || right is null)
            {
                if (left != right) return false;
                continue;
            }
            work += Size(left) + Size(right);
            if (work > maximumWork || left.Count != right.Count) return false;
            foreach (var (register, values) in left)
            {
                if (!right.TryGetValue(register, out var otherValues)) return false;
                if (values is null || otherValues is null)
                {
                    if (values != otherValues) return false;
                }
                else if (!values.SetEquals(otherValues)) return false;
            }
        }
        return true;
    }

    private static long Size(Dictionary<int, HashSet<string>?> state) =>
        state.Count + state.Values.Sum(static values => TextSize(values));
    private static long TextSize(HashSet<string>? values) => values?.Sum(static value => 1L + value.Length) ?? 1;
}
