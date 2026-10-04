using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionBoundedQueriesTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void BooleanAndCalendarResultsComposeThroughBothCallersAndReload(bool fromCache, bool fromCraft)
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-scripts");
        var code = "var ptr RuleItem item; var ptr RuleDamageType damage; var ptr Time time; var ptr Time absent null; " +
            "var int power; var int value; var int quotient; rules.getRuleItem item \"BOUNDED_READ\"; " +
            "item.getDamageType damage; geoscape_game.getTime time; ";
        foreach (var method in new[] { "getDetected", "getHyperDetected", "isHunterKiller", "isHunting", "isEscorting" })
            code += $"ufo.{method} power; damage.getDamageToHealth value power; if neq value power; return DETECTION_NONE 0; end; ";
        foreach (var method in new[] { "getDay", "getMonth", "getHour", "getMinute", "getSecond", "getSecondsPastMidnight" })
            code += $"time.{method} power; damage.getDamageToHealth value power; if neq value power; return DETECTION_NONE 0; end; " +
                $"absent.{method} power; damage.getDamageToHealth value power; if neq value 0; return DETECTION_NONE 0; end; ";
        code += "item.hasCategory power \"BOUND\"; damage.getDamageToHealth value power; " +
            "if neq value power; return DETECTION_NONE 0; end; if eq power 1; set quotient 39; div quotient power; " +
            "if neq quotient 39; return DETECTION_NONE 0; end; else; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;";
        AssertDetectionReadScript(code, fromCache, fromCraft,
            "items: [{type: BOUNDED_READ, categories: [BOUND], damageAlter: {ToHealth: 1}}]");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NativeSmokeResultsFeedDamageQueriesWithSignedWrap(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "smokeReduction"))
        {
            var expected = row[2].GetInt32();
            AssertDetectionReadScript("var ptr RuleMod alias rules; var ptr RuleItem item; var ptr RuleDamageType damage; " +
                $"var int power {row[1].GetInt32()}; var int value; var int quotient; " +
                "rules.getRuleItem item \"SMOKE_QUERY\"; item.getDamageType damage; alias.getSmokeReduction power; " +
                "damage.getDamageToHealth value power; " +
                $"if neq value {(int)(0.5f * expected)}; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
                fromCache, fromCraft,
                $"maxViewDistance: {row[0].GetInt32()}\nitems: [{{type: SMOKE_QUERY, damageAlter: {{ToHealth: 0.5}}}}]");
        }
        AssertDetectionReadScript("var ptr RuleItem item; var ptr RuleDamageType damage; var int power distance; var int value; " +
            "rules.getRuleItem item \"SMOKE_QUERY\"; item.getDamageType damage; rules.getSmokeReduction power; " +
            "damage.getDamageToHealth value power; return DETECTION_RADAR 100;", fromCache, fromCraft,
            "maxViewDistance: 2147483647\nitems: [{type: SMOKE_QUERY, damageAlter: {ToHealth: 0.5}}]");
    }
}
