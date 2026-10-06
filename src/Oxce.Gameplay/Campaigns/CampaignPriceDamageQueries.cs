using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private static bool PriceDamageQuerySupported(string name) =>
        name is "RuleItem.getDamageType" or "RuleItem.getMeleeDamageType" || WorldDetectionDamageQueries.IsClassification(name);

    private static ScriptBindingResult InvokePriceDamageQuery(string name, Span<ScriptRuntimeValue> args)
    {
        if (name is "RuleItem.getDamageType" or "RuleItem.getMeleeDamageType")
            args[1] = ScriptRuntimeValue.FromReference(args[0].Reference is PriceItemReference item
                ? name == "RuleItem.getDamageType" ? item.Rule.Damage : item.Rule.MeleeDamage : null);
        else
            args[1] = ScriptRuntimeValue.FromScalar(args[0].Reference is RuntimeItemDamageRule damage
                ? WorldDetectionDamageQueries.Classification(name, damage) : 0);
        return ScriptBindingResult.Success;
    }
}
