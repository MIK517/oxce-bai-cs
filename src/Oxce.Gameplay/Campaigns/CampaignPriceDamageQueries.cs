using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private static bool PriceDamageQuerySupported(string name) =>
        name is "RuleItem.getDamageType" or "RuleItem.getMeleeDamageType" || WorldDetectionDamageQueries.IsClassification(name) ||
        WorldDetectionDamageQueries.Supports(name);

    private static ScriptBindingResult InvokePriceDamageQuery(string name, Span<ScriptRuntimeValue> args)
    {
        if (name is "RuleItem.getDamageType" or "RuleItem.getMeleeDamageType")
            args[1] = ScriptRuntimeValue.FromReference(args[0].Reference is PriceItemReference item
                ? name == "RuleItem.getDamageType" ? item.Rule.Damage : item.Rule.MeleeDamage : null);
        else if (WorldDetectionDamageQueries.IsClassification(name))
            args[1] = ScriptRuntimeValue.FromScalar(args[0].Reference is RuntimeItemDamageRule damage
                ? WorldDetectionDamageQueries.Classification(name, damage) : 0);
        else if (name == "RuleDamageType.getRandomDamage")
        {
            // Native wrapper does not inspect power or consume RNG when either pointer is null.
            if (args[0].Reference is not null && args[3].Reference is not null)
                return ScriptBindingResult.Failure("Price random damage requires a null profile or null state.");
            args[1] = ScriptRuntimeValue.FromScalar(0);
        }
        else
        {
            var value = args[0].Reference is RuntimeItemDamageRule profile
                ? WorldDetectionDamageQueries.Result(name, profile, args.Length > 2 ? args[2].Scalar : 0) : 0;
            if (!WorldDetectionDamageQueries.InIntegerRange(value))
                return ScriptBindingResult.Failure("Price damage query exceeds the supported integer range.");
            args[1] = ScriptRuntimeValue.FromScalar((int)value);
        }
        return ScriptBindingResult.Success;
    }
}
