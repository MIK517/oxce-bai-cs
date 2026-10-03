using System.Text;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Oxce.FixtureSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicRetaliationSpawnTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetaliationTargetSelectionMatchesPinnedReference(bool cache)
    {
        var content = Load(cache);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value with { Areas = [new(0, 1, -0.5, 0.5)] };
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-retaliation-target");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var objective = row[0].GetInt32();
            var multi = row[1].GetInt32();
            var skip = row[2].GetInt32();
            var mask = row[3].GetInt32();
            var owners = Enumerable.Range(1, 3).Select(id => (Id: id, Point: new WorldPosition(id == 3 ? 2 : id * 0.2, 0.1), Known: (mask & (1 << (id - 1))) != 0)).ToArray();
            var candidates = WorldRetaliation.Candidates(owners, region, (RuntimeMissionObjective)objective,
                skip != 0, multi != 0, static owner => owner.Point, static owner => owner.Known);
            var random = new TestFixtures.CountingMinimumRandomSource();
            var index = WorldRetaliation.SelectIndex(candidates.Count, random);
            var selected = index is { } value ? candidates[value].Id : 0;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new[] { objective, multi, skip, mask, selected, random.IntegerCalls });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }

    private static RuntimeContent Load(bool cache, string extra = "") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, extra);
}
