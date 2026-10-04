using Oxce.Gameplay.Campaigns.World;
using Oxce.Scripting.Compilation;
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NullDynamicScalarReadersOverwriteUnboundedPowerBeforeConversions(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "nullMemberOutputs"))
        {
            Assert.Equal(2, row.GetArrayLength());
            Assert.All(row.EnumerateArray(), static value => Assert.Equal(0, value.GetInt32()));
        }
        var code = "var ptr Ufo absentUfo null; var ptr Craft absentCraft null; var ptr Country absentCountry null; " +
            "var ptr GeoscapeGame absentGame null; var ptr Time absentTime null; var ptr RuleItem item; " +
            "var ptr RuleDamageType damage; var int power; var int value; rules.getRuleItem item \"NULL_SCALAR_QUERY\"; " +
            "item.getDamageType damage; ";
        foreach (var method in new[] { "getAltitude", "getStatus", "getVisibility", "getDamage", "getDamageMax", "getDamagePercentage",
                     "getShield", "getShieldMax", "getShieldPercentage", "getHuntMode", "getHuntBehavior", "Stats.getSpeedMax" })
            Read("absentUfo", method);
        foreach (var method in new[] { "getCurrentFunding", "getCurrentActivityXcom", "getCurrentActivityAlien" }) Read("absentCountry", method);
        foreach (var method in new[] { "difficultyLevel", "getDaysPassed", "getMonthsPassed" }) Read("absentGame", method);
        Read("absentTime", "getYear");
        if (fromCraft)
        {
            foreach (var method in new[] { "getDamage", "getDamageMax", "getDamagePercentage", "getShield", "getShieldMax", "getShieldPercentage" })
                Read("absentCraft", method);
            foreach (var field in oracle.RootElement.GetProperty("craftStats").EnumerateObject()) Read("absentCraft", field.Name);
        }
        AssertDetectionReadScript(code + "return DETECTION_RADAR 100;", fromCache, fromCraft,
            "items: [{type: NULL_SCALAR_QUERY, damageAlter: {ToHealth: 1}}]");

        void Read(string receiver, string method) => code += $"set power detection_chance; {receiver}.{method} power; " +
            "damage.getDamageToHealth value power; if neq value 0; return DETECTION_NONE 0; end; ";
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableDynamicReceiversRetainUnknownRangesAndResourceLimits(bool fromCache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-scripts");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache);
        foreach (var nullable in new[] { false, true })
        {
            var code = "var ptr Ufo source null; var int power; " +
                (nullable ? "if eq already_tracked 0; set source ufo; end; " : "") + "source.getDamage power; return;";
            var compiled = ScriptCompiler.Compile(code, ScriptParserDefinition.FromCatalog("detectUfoFromBase"));
            Assert.True(compiled.Succeeded);
            var program = compiled.Program!;
            var visits = Enumerable.Repeat(1, program.Instructions.Count).ToArray();
            var references = WorldDetectionReferenceForecast.Build(program, content.RuntimeRules, visits, 10000)!;
            var forecast = WorldDetectionScalarForecast.Build(program, content.RuntimeRules, references, visits, 10000)!;
            var call = program.Instructions.Select((instruction, index) => (instruction, index)).Single(pair =>
                (CoreScriptOperation)pair.instruction.Operation.Value == CoreScriptOperation.HostCall &&
                program.Bindings.Single(binding => binding.Id.Value == pair.instruction.Operands[0].Scalar).Name == "Ufo.getDamage");
            Assert.Equal(nullable ? WorldDetectionScalarRanges.Full : (0, 0), forecast.At(call.index + 1, call.instruction.Operands[2]));
            Assert.Null(WorldDetectionScalarForecast.Build(program, content.RuntimeRules, references, visits, 1));
            Assert.Null(WorldDetectionReferenceForecast.Build(program, content.RuntimeRules, visits, 1));
        }
    }
}
