using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Native RuleItem.cpp float query semantics, with pre-tick integer-conversion bounds.</summary>
internal static class WorldDetectionDamageQueries
{
    internal static bool Supports(string name) => name is
        "RuleDamageType.getRandomDamage" or "RuleDamageType.getArmorEffectiveness" or
        "RuleDamageType.getDamageToArmorPre" or "RuleDamageType.getDamageToArmor" or
        "RuleDamageType.getDamageToEnergy" or "RuleDamageType.getDamageToHealth" or
        "RuleDamageType.getDamageToItem" or "RuleDamageType.getDamageToMana" or
        "RuleDamageType.getDamageToMorale" or "RuleDamageType.getDamageToStun" or
        "RuleDamageType.getDamageToTile" or "RuleDamageType.getDamageToTime" or "RuleDamageType.getDamageToWound";

    internal static bool IsClassification(string name) => name is
        "RuleDamageType.getRandomType" or "RuleDamageType.getResistType" or "RuleDamageType.getArmorIgnore" or
        "RuleDamageType.isDirect" or "RuleDamageType.isAreaOfEffect";

    internal static int Classification(string name, RuntimeItemDamageRule rule) => name switch
    {
        "RuleDamageType.getRandomType" => rule.RandomType,
        "RuleDamageType.getResistType" => rule.ResistType,
        "RuleDamageType.getArmorIgnore" => rule.ArmorIgnore,
        "RuleDamageType.isDirect" => rule.FixedRadius == 0 ? 1 : 0,
        "RuleDamageType.isAreaOfEffect" => rule.FixedRadius != 0 ? 1 : 0,
        _ => throw new InvalidOperationException("Unknown damage classification query."),
    };

    // Float multiplication is monotone for a fixed finite multiplier. Check both endpoints
    // before casting; invalid native float-to-int conversions remain guarded by preflight.
    internal static (int Min, int Max) OutputRange(string name, RuntimeItemDamageRule rule, (int Min, int Max) power)
    {
        if (IsClassification(name))
        {
            var value = Classification(name, rule);
            return (value, value);
        }
        var left = Result(name, rule, power.Min);
        var right = Result(name, rule, power.Max);
        return InIntegerRange(left) && InIntegerRange(right)
            ? (Math.Min((int)left, (int)right), Math.Max((int)left, (int)right))
            : WorldDetectionScalarRanges.Full;
    }

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
        var references = WorldDetectionReferenceForecast.Build(program, rules, visits, maximumWork);
        if (references is null) return "Detection damage query forecast exceeds the supported work limit.";
        var powers = WorldDetectionScalarForecast.Build(program, rules, references, visits, maximumWork);
        if (powers is null) return "Detection damage query forecast exceeds the supported work limit.";
        for (var index = 0; index < program.Instructions.Count; index++)
        {
            if (visits[index] == 0 || !references.Reachable(index) || !powers.Reachable(index)) continue;
            var instruction = program.Instructions[index];
            if ((CoreScriptOperation)instruction.Operation.Value != CoreScriptOperation.HostCall) continue;
            var binding = program.Bindings.First(binding => binding.Id.Value == instruction.Operands[0].Scalar);
            if (!Supports(binding.Name)) continue;
            var receiver = instruction.Operands[1];
            var candidates = references.At(index, receiver);
            if (candidates is { Count: 1 } && candidates.Contains(null)) continue;
            if (binding.Name == "RuleDamageType.getRandomDamage")
            {
                if (WorldDetectionReferenceForecast.OnlyNull(references.At(index, instruction.Operands[4]))) continue;
                return "Detection random damage requires a null profile or null state forecast.";
            }
            var armor = binding.Name == "RuleDamageType.getArmorEffectiveness";
            var power = armor ? (0, 0) : powers.At(index, instruction.Operands[3]);
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
}
