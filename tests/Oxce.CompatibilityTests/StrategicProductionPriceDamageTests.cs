using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicProductionPriceMetadataTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DamageProfileIdentityAndClassificationMatchNativePriceQueries(bool cache, bool global)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-damage");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var type = row[0].GetInt32();
            var code = "var ptr RuleItem other; var ptr RuleItem absent null; var ptr RuleDamageType damage; " +
                "var ptr RuleDamageType same; var ptr RuleDamageType melee; var ptr RuleDamageType different; var ptr RuleDamageType missing; " +
                "var int value; rules.getRuleItem other \"PRICE_OTHER_DAMAGE\"; " +
                "item_rule.getDamageType damage; item_rule.getDamageType same; item_rule.getMeleeDamageType melee; " +
                "other.getDamageType different; absent.getDamageType missing; " +
                "if neq damage same; div cost_current 0; end; if eq damage melee; div cost_current 0; end; " +
                "if eq damage different; div cost_current 0; end; if neq missing null; div cost_current 0; end; ";
            foreach (var (method, index) in new[] { ("getRandomType", 2), ("getResistType", 3), ("isDirect", 4), ("isAreaOfEffect", 5), ("getArmorIgnore", 6) })
                code += $"damage.{method} value; if neq value {row[index].GetInt32()}; div cost_current 0; end; " +
                    $"missing.{method} value; if neq value 0; div cost_current 0; end; ";
            code += $"item_rule.isSingleTarget value; if neq value {row[8].GetInt32()}; div cost_current 0; end; return 7;";
            AssertPrice(Load(cache, global, code, type == -1 ? "" : $"damageType: {type}", extraItems: "- {type: PRICE_OTHER_DAMAGE}"));
        }
    }
}
