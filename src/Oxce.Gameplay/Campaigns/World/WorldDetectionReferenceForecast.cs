using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Types;

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
        var bindings = program.Bindings.ToDictionary(static binding => binding.Id.Value);
        var needsNames = program.Instructions.Any(instruction =>
            (CoreScriptOperation)instruction.Operation.Value == CoreScriptOperation.HostCall &&
            NamedLookup(bindings[instruction.Operands[0].Scalar].Name) &&
            instruction.Operands[3].Kind == ScriptOperandKind.Register);
        var needsTextConditions = program.Registers.Any(static register => register.Type.Id == ScriptPrimitiveTypes.Text) &&
            program.Instructions.Any(static instruction => (CoreScriptOperation)instruction.Operation.Value == CoreScriptOperation.BranchCondition);
        var needsText = needsNames || needsTextConditions;
        var metadata = program.Bindings.Any(static binding => WorldDetectionTextQueries.Supports(binding.Name));
        long work = 0;
        var names = needsText ? WorldDetectionTextForecast.Build(program, visits, maximumWork, out work, metadata ? null : rules) : null;
        if (needsText && names is null) return null;
        while (true)
        {
            var built = BuildCore(program, rules, visits, maximumWork - work, names);
            work += built.Work;
            if (built.Forecast is null) return null;
            if (!metadata || names is null) return built.Forecast;
            // Alternate conservative text/reference proofs until immutable identity chains stabilize.
            // Every pass and comparison consumes the same budget; no VM, RNG or campaign mutation runs.
            var refined = WorldDetectionTextForecast.Build(program, visits, maximumWork - work, out var textWork, rules, built.Forecast);
            work += textWork;
            if (refined is null) return null;
            var stable = names.Equivalent(refined, ref work, maximumWork);
            if (work > maximumWork) return null;
            if (stable) return built.Forecast;
            names = refined;
        }
    }

    private static (WorldDetectionReferenceForecast? Forecast, long Work) BuildCore(ScriptProgram program,
        RuntimeRuleCatalog rules, int[] visits, long maximumWork, WorldDetectionTextForecast? names)
    {
        long work = 0;
        var states = new Dictionary<int, HashSet<object?>?>?[program.Instructions.Count];
        if (states.Length == 0) return (new(states), work);
        states[0] = program.Registers.Where(static register => register.Type.IsReference)
            .DistinctBy(static register => register.Offset)
            .ToDictionary(static register => register.Offset, register => register.IsInput && register.Name == "rules"
                ? new HashSet<object?>(ReferenceEqualityComparer.Instance) { rules }
                : register.IsInput || register.IsOutput ? null : new HashSet<object?>(ReferenceEqualityComparer.Instance) { null });
        var queue = new Queue<int>();
        var queued = new bool[states.Length];
        queue.Enqueue(0);
        queued[0] = true;
        var bindings = program.Bindings.ToDictionary(static binding => binding.Id.Value);
        while (queue.TryDequeue(out var index))
        {
            queued[index] = false;
            if (++work > maximumWork) return (null, work);
            if (visits[index] == 0 || names is not null && !names.Reachable(index)) continue;
            var incoming = states[index]!;
            work += Size(incoming);
            if (work > maximumWork) return (null, work);
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
                var runtimeOperand = 0;
                foreach (var parameter in binding.Parameters)
                {
                    if (parameter.Type.Id == ScriptPrimitiveTypes.Separator) continue;
                    var destination = operands[++runtimeOperand];
                    if (!parameter.Writable || !parameter.Type.IsReference) continue;
                    HashSet<object?>? values = null;
                    var source = Read(operands[1], incoming);
                    if (binding.Name == "set" && binding.Parameters.Count == 2)
                        values = Read(operands[2], incoming);
                    else if (NamedLookup(binding.Name))
                    {
                        var possibleNames = operands[3].Kind == ScriptOperandKind.Text
                            ? new HashSet<string>(StringComparer.Ordinal) { operands[3].Text! } : names?.At(index, operands[3]);
                        if (OnlyNull(source)) values = [null];
                        else if (possibleNames is not null)
                        {
                            work += possibleNames.Sum(static name => 1L + name.Length);
                            if (work > maximumWork) return (null, work);
                            values = new(ReferenceEqualityComparer.Instance);
                            foreach (var name in possibleNames)
                            {
                                var found = Lookup(rules, binding.Name, name);
                                values.Add(found);
                            }
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
                    else if (binding.Name is "Ufo.getRules" or "Ufo.getRuleUfo" or "Craft.getRuleCraft" or "GeoscapeGame.getTime" && OnlyNull(source))
                        values = [null];
                    else if (binding.Name == "Country.getRuleCountry")
                    {
                        if (OnlyNull(source)) values = [null];
                        else
                        {
                            work += rules.Countries.Count + 1L;
                            if (work > maximumWork) return (null, work);
                            values = new(rules.Countries.Rules.Select(static rule => (object?)rule.Value), ReferenceEqualityComparer.Instance) { null };
                        }
                    }
                    else if (binding.Name is "RuleItem.getDamageType" or "RuleItem.getMeleeDamageType" && source is not null)
                    {
                        work += source.Count;
                        if (work > maximumWork) return (null, work);
                        values = new(ReferenceEqualityComparer.Instance);
                        foreach (var item in source)
                            values.Add(item is RuntimeItemRule rule
                                ? binding.Name == "RuleItem.getDamageType" ? rule.Damage : rule.MeleeDamage : null);
                    }
                    else if (binding.Name is "RuleUnit.getSpawnUnit" or "RuleUnit.getLiveAlienGeoscape" or
                        "RuleUnit.getCivilianRecoveryItemType" or "RuleResearch.getLookup" or "RuleResearch.getNeededItem" && source is not null)
                    {
                        work += source.Count + source.OfType<string>().Sum(static name => (long)name.Length);
                        if (work > maximumWork) return (null, work);
                        values = new(ReferenceEqualityComparer.Instance);
                        foreach (var candidate in source) values.Add(LinkedValue(rules, binding.Name, candidate));
                    }
                    Write(destination, values);
                }
            }
            if (operation is CoreScriptOperation.Exit or CoreScriptOperation.Return) continue;
            if (operation == CoreScriptOperation.Jump) Join(operands[0].Scalar);
            else
            {
                Join(index + 1, operation == CoreScriptOperation.BranchCondition ? true : null);
                if (operation == CoreScriptOperation.BranchCondition) Join(operands[^1].Scalar, false);
            }
            if (work > maximumWork) return (null, work);

            void Write(ScriptOperand target, HashSet<object?>? value)
            {
                if (target.Kind == ScriptOperandKind.Register && state.ContainsKey(target.Scalar))
                    state[target.Scalar] = value;
            }

            void Join(int target, bool? branch = null)
            {
                if (target >= states.Length || names is not null && !names.Reachable(target)) return;
                work += Size(state);
                if (work > maximumWork) return;
                var outgoing = state;
                if (branch is { } matches && operands.Count == 5 &&
                    (ScriptConditionKind)operands[1].Scalar is ScriptConditionKind.Equal or ScriptConditionKind.NotEqual)
                {
                    var equal = (ScriptConditionKind)operands[1].Scalar == ScriptConditionKind.Equal ? matches : !matches;
                    var tested = operands[2] == ScriptOperand.IntegerValue(0) ? operands[3] :
                        operands[3] == ScriptOperand.IntegerValue(0) ? operands[2] : ScriptOperand.IntegerValue(0);
                    if (tested.Kind == ScriptOperandKind.Register && state.TryGetValue(tested.Scalar, out var possible) && possible is not null)
                    {
                        work += possible.Count;
                        if (work > maximumWork) return;
                        var filtered = new HashSet<object?>(possible.Where(value => (value is null) == equal), ReferenceEqualityComparer.Instance);
                        if (filtered.Count == 0) return;
                        outgoing = new(state) { [tested.Scalar] = filtered };
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
        return (new(states), work);
    }

    private static bool NamedLookup(string name) => name is
        "RuleMod.getRuleItem" or "RuleMod.getRuleInventory" or "RuleMod.getRuleArmor" or
        "RuleMod.getRuleSoldier" or "RuleMod.getRuleSkill" or "RuleMod.getRuleUnit" or "RuleMod.getRuleResearch";

    private static object? Lookup(RuntimeRuleCatalog rules, string binding, string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "\0") return null;
        return binding switch
        {
            "RuleMod.getRuleItem" => name != "STR_NONE" && rules.Items.TryGet(name, out var item) ? rules.Items[item].Value : null,
            "RuleMod.getRuleInventory" => rules.Inventories.TryGet(name, out var inventory) ? rules.Inventories[inventory].Value : null,
            "RuleMod.getRuleArmor" => rules.Armors.TryGet(name, out var armor) ? rules.Armors[armor].Value : null,
            "RuleMod.getRuleSoldier" => rules.Soldiers.TryGet(name, out var soldier) ? rules.Soldiers[soldier].Value : null,
            "RuleMod.getRuleSkill" => rules.Skills.TryGet(name, out var skill) ? rules.Skills[skill].Value : null,
            "RuleMod.getRuleUnit" => rules.Units.TryGet(name, out var unit) ? rules.Units[unit].Value : null,
            "RuleMod.getRuleResearch" => rules.Research.TryGet(name, out var research) ? rules.Research.GetExternalId(research) : null,
            _ => throw new InvalidOperationException("Unknown detection rule lookup."),
        };
    }

    private static object? LinkedValue(RuntimeRuleCatalog rules, string binding, object? source)
    {
        if (source is RuntimeUnitRule unit)
            return binding switch
            {
                "RuleUnit.getSpawnUnit" => unit.SpawnUnit is { } spawn ? rules.Units[spawn].Value : null,
                "RuleUnit.getLiveAlienGeoscape" => unit.LiveAlien is { } live ? rules.Items[live].Value : null,
                "RuleUnit.getCivilianRecoveryItemType" => unit.CivilianRecoveryItem is { } recovery ? rules.Items[recovery].Value : null,
                _ => null,
            };
        if (source is string id && rules.Research.TryGet(id, out var handle))
        {
            var topic = rules.Research[handle].Value;
            return binding == "RuleResearch.getLookup" ? topic.Lookup is { } lookup ? Lookup(rules, "RuleMod.getRuleResearch", lookup) : null
                : topic.NeedItem && topic.NeededItem != "STR_NONE" ? Lookup(rules, "RuleMod.getRuleItem", topic.NeededItem) : null;
        }
        return null;
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
