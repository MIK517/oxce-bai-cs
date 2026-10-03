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

public sealed class StrategicInfiltrationRepeatTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestartAndCountdownMatchPinnedReference(bool cache)
    {
        var content = Load(cache);
        var template = content.RuntimeRules.AlienMissions[content.RuntimeRules.AlienMissions.GetRequired("INFILTRATE")].Value;
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-infiltration-repeat");
        foreach (var example in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var timer = example.GetProperty("name").GetString() switch { "one" => 1UL, "rounded" => 29UL, "short" => 60UL, _ => 210UL };
            var rule = template with { EndlessInfiltration = true, Waves = [template.Waves[0] with { SpawnTimer = timer }] };
            var random = new TestFixtures.CountingMinimumRandomSource();
            var mission = new AlienMissionSnapshot(4, "INFILTRATE", "INF_REGION", "RACE_B", 0, 0, 0, 0, -1);
            var spawns = 0;
            var actual = new List<int[]>();
            foreach (var row in TestFixtures.Rows(example, "rows"))
            {
                if (mission.SpawnCountdown > 30) mission = mission with { SpawnCountdown = mission.SpawnCountdown - 30 };
                else { spawns++; mission = WorldInfiltration.Restart(mission, rule, random); }
                actual.Add([mission.NextWave, mission.NextUfoCounter, mission.SpawnCountdown, spawns, random.IntegerCalls]);
            }
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(example.GetProperty("rows"))),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(actual)));
        }
    }

    private static RuntimeContent Load(bool cache, string extra = "") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-infiltration.rul"], cache, extra);
}
