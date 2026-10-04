using Oxce.Gameplay.Campaigns.World;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Runtime;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionNumericTests
{
    [Fact]
    public void NativeArithmeticAndCalculatedOperandCasesExecuteInTheCoreVm()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-numeric");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var compiled = ScriptCompiler.Compile(row.GetProperty("source").GetString()!, new("Probe", ["result"]));
            Assert.Equal(row.GetProperty("compiled").GetBoolean(), compiled.Succeeded);
            var executed = ScriptVm.Execute(compiled.Program!, new Dictionary<string, int>
            {
                ["result"] = row.GetProperty("seed").GetInt32(),
            });
            Assert.Equal(ScriptExecutionStatus.Completed, executed.Status);
            Assert.Equal(row.GetProperty("result").GetInt32(), executed.Outputs["result"]);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CalculatedPowersAndClampsFeedDamageQueries(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-numeric");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases").Where(row =>
            !row.GetProperty("name").GetString()!.StartsWith("variable-", StringComparison.Ordinal)))
        {
            var name = row.GetProperty("name").GetString();
            var half = name is "absolute-minimum" or "singleton-wrap" or "bounded-loop";
            var result = row.GetProperty("result").GetInt32();
            var expected = half ? (int)(0.5f * result) : result;
            var code = "var ptr RuleItem item; var ptr RuleDamageType damage; var int converted; " +
                $"var int result {row.GetProperty("seed").GetInt32()}; " +
                row.GetProperty("source").GetString()!.Replace("return result;", "", StringComparison.Ordinal) +
                "rules.getRuleItem item \"NUMERIC_POWER\"; item.getDamageType damage; " +
                "damage.getDamageToHealth converted result; " +
                $"if neq converted {expected}; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;";
            AssertDetectionReadScript(code, fromCache, fromCraft,
                $"items: [{{type: NUMERIC_POWER, damageAlter: {{ToHealth: {(half ? "0.5" : "1")}}}}}]");
        }
    }

    [Theory]
    [InlineData(CoreScriptOperation.Multiply, -4, -1, 2, 5, -20, -2)]
    [InlineData(CoreScriptOperation.Add, 30, 32, 7, 9, 37, 41)]
    [InlineData(CoreScriptOperation.Subtract, 30, 32, 7, 9, 21, 25)]
    [InlineData(CoreScriptOperation.Add, int.MaxValue, int.MaxValue, 1, 1, int.MinValue, int.MinValue)]
    [InlineData(CoreScriptOperation.Add, int.MaxValue - 1, int.MaxValue, 1, 1, int.MinValue, int.MaxValue)]
    public void ScalarIntervalsConserveSignsAndWidenUncertainWrap(CoreScriptOperation operation,
        int minA, int maxA, int minB, int maxB, int expectedMin, int expectedMax) =>
        Assert.Equal((expectedMin, expectedMax), WorldDetectionScalarRanges.Apply(operation,
            (minA, maxA), (minB, maxB), WorldDetectionScalarRanges.Full));

    [Fact]
    public void GrowingLoopIntervalsReachABoundedFixedPoint()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"]);
        var program = ScriptCompiler.Compile("set detection_chance 0; loop var i 4; add detection_chance 1; end; return;",
            ScriptParserDefinition.FromCatalog("detectUfoFromBase")).Program!;
        var visits = Enumerable.Repeat(1, program.Instructions.Count).ToArray();
        var references = WorldDetectionReferenceForecast.Build(program, content.RuntimeRules, visits, 1000)!;
        var forecast = WorldDetectionScalarForecast.Build(program, content.RuntimeRules, references, visits, 1000);
        Assert.NotNull(forecast);
        Assert.Null(WorldDetectionScalarForecast.Build(program, content.RuntimeRules, references, visits, 1));
    }
}
