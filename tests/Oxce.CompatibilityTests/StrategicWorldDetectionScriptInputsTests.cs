using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>Pins the scalar caller contract before the detection VM executes.</summary>
public sealed class StrategicWorldDetectionScriptInputsTests
{
    [Fact]
    public void BaseWorkerInputsAndRollOrderingMatchReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "base"))
        {
            List<int> calls = [];
            var succeeds = row.GetProperty("hyperSucceeds").GetBoolean();
            var inputs = WorldDetection.BaseScriptInputs(
                [new(100, 40, false), new(80, 70, false), new(150, 50, true), new(90, 90, true)],
                100, -30, row.GetProperty("tracked").GetBoolean(),
                chance => { calls.Add(chance); return chance >= 100 || chance > 0 && succeeds; });
            var values = new[] { inputs.Distance, inputs.AlreadyTracked ? 1 : 0,
                inputs.RadarTotalStrength, inputs.RadarMaxDistance, inputs.HyperwaveTotalStrength, inputs.HyperwaveMaxDistance };
            Assert.Equal(row.GetProperty("inputs").EnumerateArray().Select(v => v.GetInt32()), values);
            Assert.Equal(row.GetProperty("initial").EnumerateArray().Select(v => v.GetInt32()), [(int)inputs.Type, inputs.Chance]);
            var type = row.GetProperty("override").GetBoolean() ? UfoDetectionResult.Hyperwave : inputs.Type;
            var chance = row.GetProperty("override").GetBoolean() ? 100 : inputs.Chance;
            calls.Add(chance);
            Assert.Equal(row.GetProperty("calls").EnumerateArray().Select(v => v.GetInt32()), calls);
            Assert.Equal(row.GetProperty("result").GetInt32(), chance >= 100 || chance > 0 && succeeds ? (int)type : 0);
        }
    }

    [Fact]
    public void CraftWorkerInputsAndBoundaryMatchReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "craft"))
        {
            var expectedInputs = row.GetProperty("inputs").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var inputs = WorldDetection.CraftScriptInputs(100, 75, expectedInputs[0], -30, row.GetProperty("tracked").GetBoolean());
            Assert.Equal(expectedInputs, new[] { inputs.Distance, inputs.AlreadyTracked ? 1 : 0, inputs.RadarTotalStrength, inputs.RadarMaxDistance });
            Assert.Equal(row.GetProperty("initial").EnumerateArray().Select(v => v.GetInt32()), [(int)inputs.Type, inputs.Chance]);
            var type = row.GetProperty("override").GetBoolean() ? UfoDetectionResult.Hyperwave : inputs.Type;
            var chance = row.GetProperty("override").GetBoolean() ? 100 : inputs.Chance;
            Assert.Equal(row.GetProperty("calls").EnumerateArray().Select(v => v.GetInt32()), [chance]);
            Assert.Equal(row.GetProperty("result").GetInt32(), chance >= 100 ? (int)type : 0);
        }
    }
}
