using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private static bool PriceInventoryQuerySupported(string name) =>
        WorldDetectionReferenceForecast.InventoryRole(name) is not null || name is
        "RuleMod.getRuleInventory" or "RuleInventory.getId" or "RuleInventory.getType" or
        "RuleInventory.isRightHand" or "RuleInventory.isLeftHand" or "RuleInventory.getMoveToCost";

    private ScriptBindingResult InvokePriceInventoryQuery(string name, Span<ScriptRuntimeValue> args)
    {
        var rules = _content.RuntimeRules;
        var receiver = args[0].Reference;
        if (WorldDetectionReferenceForecast.InventoryRole(name) is { } role)
        {
            var inventory = receiver is null ? null : PriceRule(rules.Inventories, role);
            if (receiver is not null && inventory is null)
                return ScriptBindingResult.Failure($"Price script requires missing inventory {role}.");
            args[1] = ScriptRuntimeValue.FromReference(inventory);
        }
        else if (name == "RuleMod.getRuleInventory")
            args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : PriceRule(rules.Inventories, args[2].Text));
        else if (name == "RuleInventory.getId")
            args[1] = ScriptRuntimeValue.FromText(WorldDetectionTextQueries.Value(receiver));
        else if (name == "RuleInventory.getMoveToCost")
        {
            var cost = 0;
            if (receiver is RuntimeInventoryRule source && !ReferenceEquals(source, args[2].Reference))
            {
                if (args[2].Reference is not RuntimeInventoryRule target)
                    return ScriptBindingResult.Failure("Price inventory movement requires a nonnull destination.");
                if (!source.Costs.TryGetValue(target.Id, out cost))
                    return ScriptBindingResult.Failure($"Price inventory movement requires missing cost {source.Id} -> {target.Id}.");
            }
            args[1] = ScriptRuntimeValue.FromScalar(cost);
        }
        else
            args[1] = ScriptRuntimeValue.FromScalar(WorldDetectionRuleQueries.Value(name, receiver, rules));
        return ScriptBindingResult.Success;
    }
}
