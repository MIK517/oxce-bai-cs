using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Native RuleItem.cpp float query semantics, with pre-tick integer-conversion bounds.</summary>
internal static class WorldDetectionDamageQueries
{
    internal static bool Supports(string name) => name is
        "RuleDamageType.getArmorEffectiveness" or
        "RuleDamageType.getDamageToArmorPre" or "RuleDamageType.getDamageToArmor" or
        "RuleDamageType.getDamageToEnergy" or "RuleDamageType.getDamageToHealth" or
        "RuleDamageType.getDamageToItem" or "RuleDamageType.getDamageToMana" or
        "RuleDamageType.getDamageToMorale" or "RuleDamageType.getDamageToStun" or
        "RuleDamageType.getDamageToTile" or "RuleDamageType.getDamageToTime" or "RuleDamageType.getDamageToWound";

    internal static float Result(string name, RuntimeItemDamageRule rule, int power) => name switch
    {
        "RuleDamageType.getArmorEffectiveness" => MathF.Round(rule.ArmorEffectiveness * 100f, MidpointRounding.AwayFromZero),
        _ => MathF.Truncate(Multiplier(name, rule.Conversions) * power),
    };

    internal static float Multiplier(string name, RuntimeDamageConversions values) => name switch
    {
        "RuleDamageType.getDamageToArmorPre" => values.ArmorPre,
        "RuleDamageType.getDamageToArmor" => values.Armor,
        "RuleDamageType.getDamageToEnergy" => values.Energy,
        "RuleDamageType.getDamageToHealth" => values.Health,
        "RuleDamageType.getDamageToItem" => values.Item,
        "RuleDamageType.getDamageToMana" => values.Mana,
        "RuleDamageType.getDamageToMorale" => values.Morale,
        "RuleDamageType.getDamageToStun" => values.Stun,
        "RuleDamageType.getDamageToTile" => values.Tile,
        "RuleDamageType.getDamageToTime" => values.Time,
        "RuleDamageType.getDamageToWound" => values.Wound,
        _ => throw new InvalidOperationException("Unknown damage conversion query."),
    };

    internal static bool InIntegerRange(float result) =>
        float.IsFinite(result) && (double)result >= int.MinValue && (double)result <= int.MaxValue;

    internal static string? UnavailableReason(ScriptProgram program, RuntimeRuleCatalog rules, int[] visits, long maximumWork)
    {
        if (!program.Bindings.Any(static binding => Supports(binding.Name))) return null;
        long work = 0;
        var references = ReferenceCandidates(program, rules, ref work, maximumWork);
        if (references is null) return "Detection damage query forecast exceeds the supported work limit.";
        var powers = ScalarRanges(program, ref work, maximumWork);
        if (powers is null) return "Detection damage query forecast exceeds the supported work limit.";
        for (var index = 0; index < program.Instructions.Count; index++)
        {
            if (visits[index] == 0) continue;
            var instruction = program.Instructions[index];
            if ((CoreScriptOperation)instruction.Operation.Value != CoreScriptOperation.HostCall) continue;
            var binding = program.Bindings.First(binding => binding.Id.Value == instruction.Operands[0].Scalar);
            if (!Supports(binding.Name)) continue;
            var receiver = instruction.Operands[1];
            var candidates = Candidates(receiver, references);
            if (candidates is { Count: 1 } && candidates.Contains(null)) continue;
            var armor = binding.Name == "RuleDamageType.getArmorEffectiveness";
            var power = armor ? (0, 0) : ScalarRange(instruction.Operands[3], powers);
            work += candidates?.Count ?? 2L * rules.Items.Count;
            if (work > maximumWork) return "Detection damage query forecast exceeds the supported work limit.";
            var profiles = candidates is null
                ? rules.Items.Rules.SelectMany(static item => new[] { item.Value.Damage, item.Value.MeleeDamage })
                : candidates.OfType<RuntimeItemDamageRule>();
            foreach (var profile in profiles)
                if (!InIntegerRange(Result(binding.Name, profile, power.Item1)) ||
                    !InIntegerRange(Result(binding.Name, profile, power.Item2)))
                    return power == (int.MinValue, int.MaxValue) && !armor
                        ? "Detection damage conversion requires a bounded power forecast."
                        : "Detection damage query exceeds the supported integer range.";
        }
        return null;
    }

    private static (int Min, int Max) ScalarRange(ScriptOperand operand, Dictionary<int, (int Min, int Max)> ranges) =>
        operand.Kind == ScriptOperandKind.Scalar ? (operand.Scalar, operand.Scalar) :
        operand.Kind == ScriptOperandKind.Register && ranges.TryGetValue(operand.Scalar, out var range)
            ? range : (int.MinValue, int.MaxValue);

    private static Dictionary<int, (int Min, int Max)>? ScalarRanges(
        ScriptProgram program, ref long work, long maximumWork)
    {
        var ranges = program.Registers.Where(static register => !register.Type.IsReference)
            .ToDictionary(static register => register.Offset,
                static register => register.IsInput || register.IsOutput ? (int.MinValue, int.MaxValue) : (0, 0));
        bool changed;
        do
        {
            changed = false;
            foreach (var instruction in program.Instructions)
            {
                if (++work > maximumWork) return null;
                var operands = instruction.Operands;
                var operation = (CoreScriptOperation)instruction.Operation.Value;
                if (operation == CoreScriptOperation.Set)
                    Merge(operands[0], ScalarRange(operands[1], ranges));
                else if (operation == CoreScriptOperation.Clear) Merge(operands[0], (0, 0));
                else if (operation == CoreScriptOperation.Swap)
                {
                    Merge(operands[0], ScalarRange(operands[1], ranges));
                    Merge(operands[1], ScalarRange(operands[0], ranges));
                }
                else if (operation == CoreScriptOperation.HostCall)
                {
                    var binding = program.Bindings.First(binding => binding.Id.Value == operands[0].Scalar);
                    for (var parameter = 0; parameter < binding.Parameters.Count; parameter++)
                        if (binding.Parameters[parameter].Type.IsRegister)
                            Merge(operands[parameter + 1], (int.MinValue, int.MaxValue));
                }
                else if (operation is not (CoreScriptOperation.Exit or CoreScriptOperation.Jump or
                    CoreScriptOperation.BranchCondition or CoreScriptOperation.Return))
                    Merge(operands[0], (int.MinValue, int.MaxValue));
            }
        } while (changed);
        return ranges;

        void Merge(ScriptOperand destination, (int Min, int Max) value)
        {
            if (destination.Kind != ScriptOperandKind.Register || !ranges.TryGetValue(destination.Scalar, out var current)) return;
            var merged = (Math.Min(current.Item1, value.Min), Math.Max(current.Item2, value.Max));
            if (merged == current) return;
            ranges[destination.Scalar] = merged;
            changed = true;
        }
    }

    private static HashSet<object?>? Candidates(ScriptOperand operand, Dictionary<int, HashSet<object?>?> references) =>
        operand == ScriptOperand.IntegerValue(0) ? [null] :
        operand.Kind == ScriptOperandKind.Register && references.TryGetValue(operand.Scalar, out var values) ? values : null;

    private static Dictionary<int, HashSet<object?>?>? ReferenceCandidates(
        ScriptProgram program, RuntimeRuleCatalog rules, ref long work, long maximumWork)
    {
        var spent = work;
        // Union all possible writes, including skipped branches and loop-carried aliases.
        // Null means unknown; a set containing null is a proven nullable local.
        var references = program.Registers.Where(static register => register.Type.IsReference)
            .ToDictionary(static register => register.Offset,
                static register => register.IsInput || register.IsOutput ? null : new HashSet<object?>(ReferenceEqualityComparer.Instance) { null });
        bool changed;
        do
        {
            changed = false;
            foreach (var instruction in program.Instructions)
            {
                if (++spent > maximumWork) return null;
                var operands = instruction.Operands;
                var operation = (CoreScriptOperation)instruction.Operation.Value;
                if (operation == CoreScriptOperation.Set && references.ContainsKey(operands[0].Scalar))
                    Merge(operands[0], Candidates(operands[1], references));
                else if (operation == CoreScriptOperation.Clear && references.ContainsKey(operands[0].Scalar))
                    Merge(operands[0], [null]);
                else if (operation == CoreScriptOperation.Swap)
                {
                    Merge(operands[0], Candidates(operands[1], references));
                    Merge(operands[1], Candidates(operands[0], references));
                }
                else if (operation == CoreScriptOperation.HostCall)
                {
                    var binding = program.Bindings.First(binding => binding.Id.Value == operands[0].Scalar);
                    for (var parameter = 0; parameter < binding.Parameters.Count; parameter++)
                    {
                        var destination = operands[parameter + 1];
                        if (!binding.Parameters[parameter].Type.IsRegister || !binding.Parameters[parameter].Type.IsReference ||
                            destination.Kind != ScriptOperandKind.Register) continue;
                        HashSet<object?>? values = null;
                        if (binding.Name == "set" && binding.Parameters.Count == 2)
                            values = Candidates(operands[2], references);
                        else if (binding.Name == "RuleMod.getRuleItem" && operands[3].Kind == ScriptOperandKind.Text)
                        {
                            values = new(ReferenceEqualityComparer.Instance) { null };
                            if (rules.Items.TryGet(operands[3].Text!, out var item)) values.Add(rules.Items[item].Value);
                        }
                        else if (binding.Name is "RuleItem.getDamageType" or "RuleItem.getMeleeDamageType" &&
                            Candidates(operands[1], references) is { } items)
                        {
                            spent += items.Count;
                            if (spent > maximumWork) return null;
                            values = new(ReferenceEqualityComparer.Instance) { null };
                            foreach (var item in items.OfType<RuntimeItemRule>())
                                values.Add(binding.Name == "RuleItem.getDamageType" ? item.Damage : item.MeleeDamage);
                        }
                        Merge(destination, values);
                    }
                }
                if (spent > maximumWork) return null;
            }
        } while (changed);
        work = spent;
        return references;

        void Merge(ScriptOperand destination, HashSet<object?>? values)
        {
            if (destination.Kind != ScriptOperandKind.Register || !references.TryGetValue(destination.Scalar, out var current) ||
                current is null) return;
            if (values is null)
            {
                references[destination.Scalar] = null;
                changed = true;
            }
            else
            {
                spent += values.Count;
                if (spent > maximumWork) return;
                var count = current.Count;
                current.UnionWith(values);
                changed |= count != current.Count;
            }
        }
    }
}
