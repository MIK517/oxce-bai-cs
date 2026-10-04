using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Bounded reference states at each instruction; joins retain both branch/loop possibilities.</summary>
internal sealed class WorldDetectionReferenceForecast
{
    private readonly Dictionary<int, HashSet<object?>?>?[] _states;

    private WorldDetectionReferenceForecast(Dictionary<int, HashSet<object?>?>?[] states) => _states = states;

    internal bool Reachable(int index) => _states[index] is not null;

    internal HashSet<object?>? At(int index, ScriptOperand operand) =>
        Read(operand, _states[index]);

    internal static bool OnlyNull(HashSet<object?>? values) => values is { Count: 1 } && values.Contains(null);

    private static HashSet<object?>? Read(ScriptOperand operand, Dictionary<int, HashSet<object?>?>? state) =>
        operand == ScriptOperand.IntegerValue(0) ? [null] :
        operand.Kind == ScriptOperandKind.Register && state is not null && state.TryGetValue(operand.Scalar, out var value) ? value : null;

    internal static WorldDetectionReferenceForecast? Build(ScriptProgram program, RuntimeRuleCatalog rules, int[] visits, long maximumWork)
    {
        var states = new Dictionary<int, HashSet<object?>?>?[program.Instructions.Count];
        if (states.Length == 0) return new(states);
        states[0] = program.Registers.Where(static register => register.Type.IsReference)
            .ToDictionary(static register => register.Offset, register => register.IsInput && register.Name == "rules"
                ? new HashSet<object?>(ReferenceEqualityComparer.Instance) { rules }
                : register.IsInput || register.IsOutput ? null : new HashSet<object?>(ReferenceEqualityComparer.Instance) { null });
        var queue = new Queue<int>();
        var queued = new bool[states.Length];
        queue.Enqueue(0);
        queued[0] = true;
        long work = 0;
        var bindings = program.Bindings.ToDictionary(static binding => binding.Id.Value);
        while (queue.TryDequeue(out var index))
        {
            queued[index] = false;
            if (++work > maximumWork) return null;
            if (visits[index] == 0) continue;
            var incoming = states[index]!;
            work += Size(incoming);
            if (work > maximumWork) return null;
            var state = new Dictionary<int, HashSet<object?>?>(incoming);
            var instruction = program.Instructions[index];
            var operands = instruction.Operands;
            var operation = (CoreScriptOperation)instruction.Operation.Value;
            if (operation == CoreScriptOperation.Set) Write(operands[0], Read(operands[1], incoming));
            else if (operation == CoreScriptOperation.Clear) Write(operands[0], [null]);
            else if (operation == CoreScriptOperation.Swap)
            {
                Write(operands[0], Read(operands[1], incoming));
                Write(operands[1], Read(operands[0], incoming));
            }
            else if (operation == CoreScriptOperation.HostCall)
            {
                var binding = bindings[operands[0].Scalar];
                for (var parameter = 0; parameter < binding.Parameters.Count; parameter++)
                {
                    if (!binding.Parameters[parameter].Writable || !binding.Parameters[parameter].Type.IsReference) continue;
                    HashSet<object?>? values = null;
                    var source = Read(operands[1], incoming);
                    if (binding.Name == "set" && binding.Parameters.Count == 2)
                        values = Read(operands[2], incoming);
                    else if (binding.Name is "RuleMod.getRuleItem" or "RuleMod.getRuleInventory")
                    {
                        if (OnlyNull(source) || binding.Name == "RuleMod.getRuleItem" && operands[3] == ScriptOperand.TextValue("STR_NONE"))
                            values = [null];
                        else if (operands[3].Kind == ScriptOperandKind.Text)
                        {
                            object? found = binding.Name == "RuleMod.getRuleItem"
                                ? rules.Items.TryGet(operands[3].Text!, out var item) ? rules.Items[item].Value : null
                                : rules.Inventories.TryGet(operands[3].Text!, out var inventory) ? rules.Inventories[inventory].Value : null;
                            values = new(ReferenceEqualityComparer.Instance) { found };
                            if (source is null || source.Contains(null)) values.Add(null);
                        }
                    }
                    else if (InventoryRole(binding.Name) is { } role)
                    {
                        values = new(ReferenceEqualityComparer.Instance);
                        if (OnlyNull(source)) values.Add(null);
                        else
                        {
                            if (rules.Inventories.TryGet(role, out var inventory)) values.Add(rules.Inventories[inventory].Value);
                            else values.Add(null); // Existing required-role preflight rejects the native throw.
                            if (source is null || source.Contains(null)) values.Add(null);
                        }
                    }
                    else if (binding.Name is "RuleItem.getDamageType" or "RuleItem.getMeleeDamageType" && source is not null)
                    {
                        work += source.Count;
                        if (work > maximumWork) return null;
                        values = new(ReferenceEqualityComparer.Instance);
                        foreach (var item in source)
                            values.Add(item is RuntimeItemRule rule
                                ? binding.Name == "RuleItem.getDamageType" ? rule.Damage : rule.MeleeDamage : null);
                    }
                    Write(operands[parameter + 1], values);
                }
            }
            if (operation is CoreScriptOperation.Exit or CoreScriptOperation.Return) continue;
            if (operation == CoreScriptOperation.Jump) Join(operands[0].Scalar);
            else
            {
                Join(index + 1);
                if (operation == CoreScriptOperation.BranchCondition) Join(operands[^1].Scalar);
            }
            if (work > maximumWork) return null;

            void Write(ScriptOperand target, HashSet<object?>? value)
            {
                if (target.Kind == ScriptOperandKind.Register && state.ContainsKey(target.Scalar))
                    state[target.Scalar] = value;
            }

            void Join(int target)
            {
                if (target >= states.Length) return;
                work += Size(state);
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
                        if (current is null) continue;
                        if (value is null)
                        {
                            existing[register] = null;
                            changed = true;
                        }
                        else
                        {
                            work += current.Count + value.Count;
                            if (work > maximumWork) return;
                            if (value.IsSubsetOf(current)) continue;
                            var merged = new HashSet<object?>(current, ReferenceEqualityComparer.Instance);
                            merged.UnionWith(value);
                            existing[register] = merged;
                            changed = true;
                        }
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

    private static long Size(Dictionary<int, HashSet<object?>?> state) =>
        state.Count + state.Values.Sum(static value => (long)(value?.Count ?? 1));

    internal static string? InventoryRole(string name) => name switch
    {
        "RuleMod.getRuleInventoryRightHand" => "STR_RIGHT_HAND",
        "RuleMod.getRuleInventoryLeftHand" => "STR_LEFT_HAND",
        "RuleMod.getRuleInventoryBackpack" => "STR_BACK_PACK",
        "RuleMod.getRuleInventoryBelt" => "STR_BELT",
        "RuleMod.getRuleInventoryGround" => "STR_GROUND",
        _ => null,
    };
}
