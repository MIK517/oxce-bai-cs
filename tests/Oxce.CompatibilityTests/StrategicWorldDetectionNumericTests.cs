using Oxce.Scripting.Api;
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
    public void SignedDivisionAndModuloIntervalsContainEverySmallDomainResult()
    {
        var domains = new[] { (-6, -2), (-3, 3), (2, 6), (-5, -5), (5, 5) };
        foreach (var a in domains)
            foreach (var b in domains.Where(static range => range.Item1 > 0 || range.Item2 < 0))
                foreach (var c in domains.Where(static range => range.Item1 > 0 || range.Item2 < 0))
                    foreach (var operation in new[] { CoreScriptOperation.Divide, CoreScriptOperation.Modulo,
                                 CoreScriptOperation.MultiplyDivide, CoreScriptOperation.OffsetModulo })
                    {
                        var range = WorldDetectionScalarRanges.Apply(operation, a, b, c, b);
                        for (var x = a.Item1; x <= a.Item2; x++)
                            for (var y = b.Item1; y <= b.Item2; y++)
                                for (var z = c.Item1; z <= c.Item2; z++)
                                {
                                    var value = operation switch
                                    {
                                        CoreScriptOperation.Divide => x / y,
                                        CoreScriptOperation.Modulo => x % y,
                                        CoreScriptOperation.MultiplyDivide => (long)x * y / z,
                                        _ => (((long)x * y + z) % y + y) % y,
                                    };
                                    Assert.InRange(value, range.Min, range.Max);
                                }
                    }
        Assert.Equal(WorldDetectionScalarRanges.Full,
            WorldDetectionScalarRanges.Apply(CoreScriptOperation.Divide, (1, 39), (-1, 1), (0, 0)));
        Assert.Equal((2, 2), WorldDetectionScalarRanges.Apply(CoreScriptOperation.MultiplyDivide,
            (int.MaxValue, int.MaxValue), (2, 2), (int.MaxValue, int.MaxValue)));
    }

    [Fact]
    public void BitwiseIntervalsContainAllSmallSignedDomainResults()
    {
        var domains = new[] { (-9, -2), (-3, 7), (2, 9), (-1, -1), (0, 0), (15, 15) };
        foreach (var a in domains)
            foreach (var b in domains)
                foreach (var operation in new[] { CoreScriptOperation.BitAnd, CoreScriptOperation.BitOr, CoreScriptOperation.BitXor })
                {
                    var range = WorldDetectionScalarRanges.Apply(operation, a, b, (0, 0));
                    for (var x = a.Item1; x <= a.Item2; x++)
                        for (var y = b.Item1; y <= b.Item2; y++)
                            Assert.InRange(operation == CoreScriptOperation.BitAnd ? x & y :
                                operation == CoreScriptOperation.BitOr ? x | y : x ^ y, range.Min, range.Max);
                }
        Assert.Equal((0, 63), WorldDetectionScalarRanges.Apply(CoreScriptOperation.BitAnd,
            WorldDetectionScalarRanges.Full, (63, 63), (0, 0)));
        Assert.Equal((-536870912, 536870911), WorldDetectionScalarRanges.Apply(CoreScriptOperation.ShiftRight,
            WorldDetectionScalarRanges.Full, (2, 2), (0, 0)));
        Assert.Equal(WorldDetectionScalarRanges.Full, WorldDetectionScalarRanges.Apply(CoreScriptOperation.ShiftLeft,
            (1, int.MaxValue), (1, 1), (0, 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaskedCallerInputsProvideBoundedDamagePowers(bool fromCraft) =>
        AssertDetectionReadScript("var ptr RuleItem item; var ptr RuleDamageType damage; var int value; var int power distance; " +
            "rules.getRuleItem item \"BIT_POWER\"; item.getDamageType damage; bit_and power 63; " +
            "damage.getDamageToHealth value power; if neq value power; return DETECTION_NONE 0; end; " +
            "return DETECTION_RADAR 100;", fromCraft, "items: [{type: BIT_POWER, damageAlter: {ToHealth: 1}}]");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeWaveResultsFeedSubsequentDamageQueries(bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-numeric");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases").Where(row =>
            row.GetProperty("name").GetString()!.Contains("wave", StringComparison.Ordinal)))
        {
            var code = "var ptr RuleItem item; var ptr RuleDamageType damage; var int value; " +
                $"var int result {row.GetProperty("seed").GetInt32()}; " +
                row.GetProperty("source").GetString()!.Replace("return result;", "", StringComparison.Ordinal) +
                "rules.getRuleItem item \"WAVE_POWER\"; item.getDamageType damage; damage.getDamageToHealth value result; " +
                $"if neq value {row.GetProperty("result").GetInt32()}; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;";
            AssertDetectionReadScript(code, fromCraft, "items: [{type: WAVE_POWER, damageAlter: {ToHealth: 1}}]");
        }
    }

    [Fact]
    public void WaveIntervalsContainEverySmallIntegerPhaseAndNegativeCap()
    {
        foreach (var operation in new[] { CoreScriptOperation.WaveRectangle, CoreScriptOperation.WaveSaw, CoreScriptOperation.WaveTriangle })
            foreach (var size in new[] { (-5, -1), (-2, 4), (1, 7) })
                foreach (var cap in new[] { (-5, -1), (-2, 4), (1, 7) })
                {
                    var bounds = WorldDetectionWaveRanges.Apply(operation, (-12, 12), (2, 7), size, cap);
                    for (var value = -12; value <= 12; value++)
                        for (var period = 2; period <= 7; period++)
                            for (var width = size.Item1; width <= size.Item2; width++)
                                for (var height = cap.Item1; height <= cap.Item2; height++)
                                {
                                    var phase = (value % period + period) % period;
                                    var result = phase > width ? 0 : operation == CoreScriptOperation.WaveRectangle ? height :
                                        Math.Min(operation == CoreScriptOperation.WaveTriangle && phase > width / 2 ? width - phase : phase, height);
                                    Assert.InRange(result, bounds.Min, bounds.Max);
                                }
                }
        Assert.Equal(WorldDetectionScalarRanges.Full,
            WorldDetectionWaveRanges.Apply(CoreScriptOperation.WaveSine, (-10, 10), (1, 4), (int.MinValue, int.MinValue), (0, 0)));
    }

    [Fact]
    public void NativeArithmeticAndCalculatedOperandCasesExecuteInTheCoreVm()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-numeric");
        var actual = new List<object>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var compiled = ScriptCompiler.Compile(row.GetProperty("source").GetString()!, new("Probe", ["result"], ReferenceScriptApiCatalog.Instance));
            Assert.True(row.GetProperty("compiled").GetBoolean() == compiled.Succeeded,
                $"{row.GetProperty("name").GetString()}: {string.Join("; ", compiled.Diagnostics.Select(static diagnostic => diagnostic.Message))}");
            var executed = ScriptVm.Execute(compiled.Program!, new Dictionary<string, int>
            {
                ["result"] = row.GetProperty("seed").GetInt32(),
            });
            Assert.Equal(ScriptExecutionStatus.Completed, executed.Status);
            Assert.Equal(row.GetProperty("result").GetInt32(), executed.Outputs["result"]);
            actual.Add(new
            {
                compiled = compiled.Succeeded,
                diagnostics = compiled.Diagnostics.Select(static diagnostic => diagnostic.Message).ToArray(),
                name = row.GetProperty("name").GetString(),
                result = executed.Outputs["result"],
                seed = row.GetProperty("seed").GetInt32(),
                source = row.GetProperty("source").GetString(),
            });
        }
        Assert.Equal(Oxce.FixtureSupport.CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(oracle.RootElement.GetRawText())),
            Oxce.FixtureSupport.CanonicalJson.Normalize(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { cases = actual })));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CalculatedPowersAndClampsFeedDamageQueries(bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-numeric");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases").Where(row =>
            !row.GetProperty("name").GetString()!.StartsWith("variable-", StringComparison.Ordinal) &&
            !row.GetProperty("name").GetString()!.StartsWith("condition-", StringComparison.Ordinal)))
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
            AssertDetectionReadScript(code, fromCraft,
                $"items: [{{type: NUMERIC_POWER, damageAlter: {{ToHealth: {(half ? "0.5" : "1")}}}}}]");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CalculatedDivisorsAndWavePeriodsMatchNative(bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-numeric");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases").Where(row =>
            row.GetProperty("name").GetString()!.StartsWith("variable-", StringComparison.Ordinal)))
        {
            var code = $"var int result {row.GetProperty("seed").GetInt32()}; " +
                row.GetProperty("source").GetString()!.Replace("return result;", "", StringComparison.Ordinal) +
                $"if neq result {row.GetProperty("result").GetInt32()}; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;";
            AssertDetectionReadScript(code, fromCraft);
        }
        AssertDetectionReadScript("var int divisor distance; limit divisor 1 10; " +
            "div detection_chance divisor; return DETECTION_RADAR 100;", fromCraft);
    }

    [Theory]
    [InlineData("var int divisor 0; div detection_chance divisor; return;", "nonzero divisor")]
    [InlineData("var int divisor 3; if eq already_tracked 0; set divisor 0; end; div detection_chance divisor; return;", "nonzero divisor")]
    [InlineData("var int period -1; wavegen_sin detection_chance period 100; return;", "positive wave-period")]
    [InlineData("var int divisor 3; loop var i 2; div detection_chance divisor; set divisor 0; end; return;", "nonzero divisor")]
    public void UncertainOrUnsafeCalculatedOperandsStopBeforeMutation(string code, string reason)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], extraRules: $$"""
            ufos:
              - type: UFO_NUMERIC_GUARD
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: '{{code}}'
            """);
        var campaign = CreateUfoTransitCampaign(content,
            new Oxce.Gameplay.Campaigns.CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_NUMERIC_GUARD", 2200);
        AssertTimeBlocked(campaign, $"Detection script requires a {reason} forecast.");
    }

    [Fact]
    public void NumericSafetyRejectsAnExhaustedForecastBudget()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"]);
        var program = ScriptCompiler.Compile("var int divisor 3; div detection_chance divisor; return;",
            ScriptParserDefinition.FromCatalog("detectUfoFromBase")).Program!;
        var visits = Enumerable.Repeat(1, program.Instructions.Count).ToArray();
        Assert.Equal("Detection numeric safety forecast exceeds the supported work limit.",
            WorldDetectionNumericSafety.UnavailableReason(program, content.RuntimeRules, visits, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarConditionEdgesProveSafeDivisorsAndPowerBounds(bool fromCraft)
    {
        AssertDetectionReadScript("var int divisor distance; if gt divisor 0; div detection_chance divisor; end; " +
            "if lt divisor 0; div detection_chance divisor; end; return DETECTION_RADAR 100;", fromCraft);
        AssertDetectionReadScript("var int divisor distance; if eq divisor 3; div detection_chance divisor; end; " +
            "return DETECTION_RADAR 100;", fromCraft);
        AssertDetectionReadScript("var int divisor 0; if neq divisor 0; div detection_chance divisor; end; " +
            "return DETECTION_RADAR 100;", fromCraft);
        AssertDetectionReadScript("var ptr RuleItem item; var ptr RuleDamageType damage; var int power already_tracked; " +
            "var int value -7; rules.getRuleItem item \"CONDITION_POWER\"; item.getDamageType damage; " +
            "if and ge power 0 le power 39; damage.getDamageToHealth value power; " +
            "if neq value power; return DETECTION_NONE 0; end; end; return DETECTION_RADAR 100;",
            fromCraft, "items: [{type: CONDITION_POWER, damageAlter: {ToHealth: 1}}]");
    }

    [Theory]
    [InlineData(ScriptConditionKind.LessThan, true, int.MinValue, int.MaxValue, 0, 0, int.MinValue, -1)]
    [InlineData(ScriptConditionKind.GreaterThan, true, int.MinValue, int.MaxValue, 0, 0, 1, int.MaxValue)]
    [InlineData(ScriptConditionKind.Equal, true, int.MinValue, int.MaxValue, 3, 3, 3, 3)]
    [InlineData(ScriptConditionKind.NotEqual, true, 0, 5, 0, 0, 1, 5)]
    public void ComparisonIntervalsPreserveNativeSignedBoundaries(ScriptConditionKind condition, bool matches,
        int minLeft, int maxLeft, int minRight, int maxRight, int expectedMin, int expectedMax)
    {
        var left = (minLeft, maxLeft);
        var right = (minRight, maxRight);
        Assert.True(WorldDetectionScalarConditions.Refine(condition, matches, ref left, ref right));
        Assert.Equal((expectedMin, expectedMax), left);
    }

    [Fact]
    public void ImpossibleExtremeComparisonsProduceNoSuccessor()
    {
        var left = (int.MaxValue, int.MaxValue);
        var right = (int.MinValue, int.MinValue);
        Assert.False(WorldDetectionScalarConditions.Refine(ScriptConditionKind.LessThan, true, ref left, ref right));
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
