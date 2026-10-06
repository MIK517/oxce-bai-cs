using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private static bool PriceRuleQuerySupported(string name) =>
        name is "RuleMod.getRuleArmor" or "RuleMod.getRuleSkill" or "RuleArmor.getType" or "RuleSkill.getType" or "RuleSoldier.getType" ||
        name.StartsWith("RuleArmor.", StringComparison.Ordinal) && WorldDetectionRuleQueries.Supports(name);

    private ScriptBindingResult InvokePriceRuleQuery(string name, Span<ScriptRuntimeValue> args)
    {
        var rules = _content.RuntimeRules;
        var receiver = args[0].Reference;
        if (name == "RuleMod.getRuleArmor")
            args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : PriceRule(rules.Armors, args[2].Text));
        else if (name == "RuleMod.getRuleSkill")
            args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : PriceRule(rules.Skills, args[2].Text));
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
