using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldCompositionTests
{
    [Fact]
    public void DependentEventEligibilityCoversCapturedResearchAndMissionEffects()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-composition");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            if (row.GetProperty("failed").GetBoolean()) continue;
            var month = row.GetProperty("month").GetInt32();
            var zero = row.GetProperty("case").GetString() == "zero-odds";
            var context = new WorldScriptContext(month, 0, 0, 1000, false,
                new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
                new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
                new HashSet<string>(), new Dictionary<string, int>(), new Dictionary<string, int>());
            var criteria = new RuntimeScriptCriteria(0, -1, 100, 0, 4,
                int.MinValue, int.MaxValue, long.MinValue, long.MaxValue, "RUN", "", 2, -1,
                [new(new("R3", null), true)], [], [], [], [], [], []);
            var possible = WorldScriptEligibility.MayMatch(criteria, context,
                new HashSet<string>(zero ? [] : ["R1", "R2", "R3"], StringComparer.Ordinal),
                new Dictionary<string, long> { ["RUN"] = zero ? 0 : 2 }, new Dictionary<string, long>());
            Assert.Equal(row.GetProperty("events").GetInt32() != 0, possible);
        }
    }
}
