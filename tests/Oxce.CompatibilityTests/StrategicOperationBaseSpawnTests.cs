using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;
using System.Text;
using Oxce.TestSupport;
using Oxce.FixtureSupport;
using System.Text.Json;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicOperationBaseSpawnTests
{
    [Fact]
    public void OperationBaseSearchMatchesPinnedBoundedFallbackAndGeometryRows()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-operation-base-spawn");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var successAt = row[0].GetInt32();
            var geometry = row[1].GetInt32();
            var fakeChance = row[2].GetInt32();
            var fake = row[3].GetInt32() != 0;
            var allow = row[4].GetInt32() != 0;
            var x = geometry == 0 ? 0.2 : 2.0;
            var region = new RuntimeRegionRule(0, geometry == 2 ? [] : [new(0, 1, -0.5, 0.5)],
                [new([new(x, x, 0.1, 0.1, 0, "")])], new Dictionary<string, ulong>(), 0, "", null, [], []);
            var random = new SplitMix64RandomSource(17);
            var expected = new SplitMix64RandomSource(17);
            var timer = WorldTrajectory.SpawnCountdown(9000, random);
            WorldTrajectory.SpawnCountdown(9000, expected);
            var landCalls = 0;
            var selected = WorldOperationBaseSpawn.Select(region, 0, fakeChance, allow, random,
                _ => ++landCalls >= successAt && successAt != 0, _ => fake);
            var attempts = row[8].GetInt32() / 2;
            expected.NextInclusive(0, 99);
            for (var index = 0; index < attempts; index++)
            {
                expected.NextInclusive(0, 0);
                expected.NextUnit();
                expected.NextUnit();
            }
            Assert.Equal(expected.State, random.State);
            Assert.Equal(4500, row[10].GetInt32()); // minimum captured start timer
            Assert.InRange(timer, 4500, 13500);
            if (selected is { } position) Assert.Equal(new WorldPosition(x, 0.1), position);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                successAt, geometry, fakeChance, fake ? 1 : 0, allow ? 1 : 0,
                selected is null ? 0 : 1, selected is null ? 1 : 0,
                2 + attempts, 2 * attempts, landCalls, row[10].GetInt32(),
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WrongTextureFallbackDefaultsTrueAndExplicitFalseSurvivesCache(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-operation-base-spawn");
        var defaults = StrategicReadinessTestContent.Load("strategic-world.rul", cache);
        Assert.True(defaults.RuntimeRules.Campaign.AllowAlienBasesOnWrongTextures);
        var configured = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache,
            "allowAlienBasesOnWrongTextures: false\n");
        Assert.False(configured.RuntimeRules.Campaign.AllowAlienBasesOnWrongTextures);
    }
}
