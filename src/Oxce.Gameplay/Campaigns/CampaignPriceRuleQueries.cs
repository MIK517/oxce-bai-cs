using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private readonly Dictionary<RuntimeSoldierRule, string> _priceSoldierIds = new(ReferenceEqualityComparer.Instance);

    private static bool PriceRuleQuerySupported(string name) =>
        name is "RuleMod.getRuleArmor" or "RuleMod.getRuleSkill" or "RuleMod.getRuleUnit" or "RuleMod.getRuleSoldier" or
            "RuleArmor.getType" or "RuleSkill.getType" or "RuleSoldier.getType" or "RuleUnit.getType" or
            "RuleUnit.getSpawnUnit" or "RuleUnit.getLiveAlienGeoscape" or "RuleUnit.getCivilianRecoveryItemType" ||
        (name.StartsWith("RuleArmor.", StringComparison.Ordinal) || name.StartsWith("RuleSoldier.", StringComparison.Ordinal)) &&
            WorldDetectionRuleQueries.Supports(name);

    private ScriptBindingResult InvokePriceRuleQuery(string name, Span<ScriptRuntimeValue> args)
    {
        var rules = _content.RuntimeRules;
        var receiver = args[0].Reference;
        if (name == "RuleMod.getRuleArmor")
            args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : PriceRule(rules.Armors, args[2].Text));
        else if (name == "RuleMod.getRuleSkill")
            args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : PriceRule(rules.Skills, args[2].Text));
        else if (name == "RuleMod.getRuleUnit")
            args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : PriceRule(rules.Units, args[2].Text));
        else if (name == "RuleMod.getRuleSoldier")
        {
            var soldier = receiver is null ? null : PriceRule(rules.Soldiers, args[2].Text);
            if (soldier is not null) _priceSoldierIds.TryAdd(soldier, rules.Soldiers.GetExternalId(rules.Soldiers.GetRequired(args[2].Text!)));
            args[1] = ScriptRuntimeValue.FromReference(soldier);
        }
        else if (name == "RuleUnit.getSpawnUnit")
            args[1] = ScriptRuntimeValue.FromReference(receiver is RuntimeUnitRule { SpawnUnit: { } spawn } ? rules.Units[spawn].Value : null);
        else if (name is "RuleUnit.getLiveAlienGeoscape" or "RuleUnit.getCivilianRecoveryItemType")
        {
            var item = receiver is RuntimeUnitRule unit ? name == "RuleUnit.getLiveAlienGeoscape" ? unit.LiveAlien : unit.CivilianRecoveryItem : null;
            args[1] = ScriptRuntimeValue.FromReference(item is { } handle ? PriceItemReferenceFor(rules.Items.GetExternalId(handle), rules.Items[handle].Value) : null);
        }
        else if (WorldDetectionTextQueries.Supports(name))
            args[1] = ScriptRuntimeValue.FromText(WorldDetectionTextQueries.Value(receiver));
        else
            args[1] = ScriptRuntimeValue.FromScalar(WorldDetectionRuleQueries.Value(name, receiver, rules));
        return ScriptBindingResult.Success;
    }

    private static TProjection? PriceRule<TFamily, TProjection>(RuntimeRuleFamily<TFamily, TProjection> rules, string? id)
        where TProjection : class =>
        id is not null && id.Length != 0 && id != "\0" && rules.TryGet(id, out var handle) ? rules[handle].Value : null;
}
