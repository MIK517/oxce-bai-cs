using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionRuleQueriesTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NativeArmorVisionOutputsComposeWithDamageQueries(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "armorVision");
        for (var stage = 0; stage < rows.Length; stage++)
        {
            var fields = stage == 0 ? "" : string.Join("\n", rows[stage].EnumerateObject().Select(field =>
                $"    {char.ToLowerInvariant(field.Name[3])}{field.Name[4..]}: {field.Value.GetInt32()}"));
            var code = "var ptr RuleArmor armor; var ptr RuleItem item; var ptr RuleDamageType damage; " +
                "var int power; var int value; var int quotient; rules.getRuleArmor armor \"VISION_QUERY\"; " +
                "rules.getRuleItem item \"HALF_DAMAGE\"; item.getDamageType damage; ";
            foreach (var field in rows[stage].EnumerateObject())
            {
                var expected = field.Value.GetInt32();
                code += $"armor.{field.Name} power; damage.getDamageToHealth value power; " +
                    $"if neq value {(int)(0.5f * expected)}; return DETECTION_NONE 0; end; ";
                if (expected != 0)
                    code += $"set quotient {expected}; div quotient power; if neq quotient 1; return DETECTION_NONE 0; end; ";
            }
            code += "return DETECTION_RADAR 100;";
            AssertDetectionReadScript(code, fromCache, fromCraft, $$$"""
                armors:
                  - type: VISION_QUERY
                    corpseBattle: [CORPSE]
                    corpseGeo: CORPSE
                    loftempsSet: [0]
                {{{fields}}}
                items: [{type: HALF_DAMAGE, damageAlter: {ToHealth: 0.5}}]
                """);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NativeModSettingsOutputsComposeWithQueriesIncludingNullReceiver(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "modSettings"))
        {
            var code = "var ptr RuleMod alias rules; var ptr RuleMod absent null; var ptr RuleItem item; " +
                "var ptr RuleDamageType damage; var int power; var int value; var int quotient; " +
                "rules.getRuleItem item \"HALF_DAMAGE\"; item.getDamageType damage; ";
            foreach (var (method, index) in new[] { ("getMaxViewDistance", 0), ("getMaxDarknessToSeeUnits", 1) })
            {
                var expected = row[index].GetInt32();
                code += $"alias.{method} power; damage.getDamageToHealth value power; " +
                    $"if neq value {(int)(0.5f * expected)}; return DETECTION_NONE 0; end; ";
                if (expected != 0)
                    code += $"set quotient {expected}; div quotient power; if neq quotient 1; return DETECTION_NONE 0; end; ";
                code += $"absent.{method} power; damage.getDamageToHealth value power; " +
                    "if neq value 0; return DETECTION_NONE 0; end; ";
            }
            code += "return DETECTION_RADAR 100;";
            AssertDetectionReadScript(code, fromCache, fromCraft,
                $"maxViewDistance: {row[0].GetInt32()}\nmaxDarknessToSeeUnits: {row[1].GetInt32()}\n" +
                "items: [{type: HALF_DAMAGE, damageAlter: {ToHealth: 0.5}}]");
        }
    }
}
