using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Immutable native rule identity readers shared by execution and text forecasts.</summary>
internal static class WorldDetectionTextQueries
{
    internal static bool Supports(string name) => name is "RuleItem.getType" or "RuleArmor.getType" or
        "RuleCraft.getType" or "RuleSkill.getType" or "RuleSoldier.getType" or "RuleUnit.getType" or "RuleInventory.getId";

    internal static string Value(object? receiver) => receiver switch
    {
        null => "",
        RuntimeItemRule item => item.Type,
        RuntimeArmorRule armor => armor.Type,
        RuntimeCraftRule craft => craft.Type,
        RuntimeIdentityRule skill => skill.Id, // Native RuleSoldier.getType also receives RuleSkill.
        RuntimeUnitRule unit => unit.Type,
        RuntimeInventoryRule inventory => inventory.Id,
        _ => throw new InvalidOperationException("Detection text receiver was not preflighted."),
    };

    internal static long CandidateCount(string name, RuntimeRuleCatalog rules) => name switch
    {
        "RuleItem.getType" => rules.Items.Count,
        "RuleArmor.getType" => rules.Armors.Count,
        "RuleCraft.getType" => rules.Crafts.Count,
        "RuleSkill.getType" or "RuleSoldier.getType" => rules.Skills.Count,
        "RuleUnit.getType" => rules.Units.Count,
        "RuleInventory.getId" => rules.Inventories.Count,
        _ => throw new InvalidOperationException("Unknown detection text query."),
    };

    internal static IEnumerable<object?> Candidates(string name, RuntimeRuleCatalog rules) => name switch
    {
        "RuleItem.getType" => rules.Items.Rules.Select(static rule => (object?)rule.Value),
        "RuleArmor.getType" => rules.Armors.Rules.Select(static rule => (object?)rule.Value),
        "RuleCraft.getType" => rules.Crafts.Rules.Select(static rule => (object?)rule.Value),
        "RuleSkill.getType" or "RuleSoldier.getType" => rules.Skills.Rules.Select(static rule => (object?)rule.Value),
        "RuleUnit.getType" => rules.Units.Rules.Select(static rule => (object?)rule.Value),
        "RuleInventory.getId" => rules.Inventories.Rules.Select(static rule => (object?)rule.Value),
        _ => throw new InvalidOperationException("Unknown detection text query."),
    };
}
