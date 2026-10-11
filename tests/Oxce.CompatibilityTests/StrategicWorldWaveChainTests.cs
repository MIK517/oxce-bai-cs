using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldWaveChainTests
{
    [Fact]
    public void CountersAndRandomDrawsMatchExtractedRecursionAcrossReloads()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-wave-chain");
        foreach (var example in TestFixtures.Rows(oracle.RootElement, "cases"))
        {
            var name = example.GetProperty("name").GetString()!;
            var snapshot = WaveSnapshot(content, "CHAIN_" + name);
            snapshot = snapshot with
            {
                World = snapshot.World with
                {
                    Missions = [snapshot.World.Missions[0] with
            {
                NextUfoCounter = name == "mixed" ? 1 : 0,
                SpawnCountdown = name == "pending" ? 90 : 0,
                LiveUfos = 1, // Keep the completed mission so every counter remains observable.
            }]
                }
            };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(66));
            var random = new SplitMix64RandomSource(snapshot.RandomState);
            var calls = 0;
            var expected = new List<int[]>();
            var actual = new List<int[]>();
            foreach (var row in TestFixtures.Rows(example, "rows"))
            {
                AdvanceOne(campaign);
                var current = campaign.Capture();
                var mission = Assert.Single(current.World.Missions);
                expected.Add([row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32()]);
                actual.Add([mission.NextWave, mission.NextUfoCounter, mission.SpawnCountdown]);
                while (calls < row[4].GetInt32()) { random.NextInclusive(0, 0); calls++; }
                Assert.Equal(random.State, current.RandomState);
                Assert.Empty(current.World.Ufos);
                campaign = Reload(content, campaign, 66, "immediate-waves.sav");
                // Each reference row is one half-hour handler invocation.
                campaign = CampaignState.Restore(campaign.Capture() with { Time = snapshot.Time }, content,
                    new SplitMix64RandomSource(66));
            }
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(actual)));
        }
    }

    [Fact]
    public void ImmediateUfosSpawnInOrderBeforeDetectionAndMoveTogether()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = WaveSnapshot(content, "CHAIN_ufos");
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(66));
        var random = new SplitMix64RandomSource(snapshot.RandomState);
        random.NextInclusive(0, 0); // No-object wave schedules the first immediate UFO.
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value;
        for (var index = 0; index < 3; index++)
        {
            WorldGeometry.RandomPoint(region, 4, -1, random);
            WorldGeometry.RandomPoint(region, 3, -1, random);
            if (index != 2) random.NextInclusive(0, 0);
        }
        for (var index = 0; index < 3; index++) random.NextInclusive(0, 99); // Base detection after the entire chain.
        AdvanceOne(campaign);
        var current = campaign.Capture();
        Assert.Equal(random.State, current.RandomState);
        Assert.Equal(3, current.World.Ufos.Count);
        Assert.Equal([1, 2, 3], current.World.Ufos.Select(ufo => ufo.UniqueId));
        Assert.All(current.World.Ufos, ufo => Assert.Equal(1, ufo.MissionWaveNumber));
        Assert.Single(current.World.Ufos.Select(ufo => ufo.Position).Distinct());
        Assert.Equal(3, Assert.Single(current.World.Missions).LiveUfos);
        Assert.Equal(2, Assert.Single(current.World.Missions).NextWave);
        Reload(content, campaign, 66, "immediate-ufo-chain.sav");
    }

    [Theory]
    [InlineData("STR_UFO_UNIQUE", "UFO unique IDs are exhausted or collide with a saved UFO.")]
    [InlineData("STR_UFO", "UFO contact marker IDs are exhausted or collide with a saved UFO.")]
    public void WholeChainReservesIdsBeforeAnyMutation(string counter, string reason)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = WaveSnapshot(content, "CHAIN_ufos").WithNextId(counter, int.MaxValue - 2);
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(66));
        AssertTimeBlocked(campaign, reason);
    }

    [Fact]
    public void ExcessiveImmediateWorkFailsWithoutMutationOrRandomDraws()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CampaignState.Restore(WaveSnapshot(content, "CHAIN_excessive"), content, new SplitMix64RandomSource(66));
        var before = campaign.Capture();
        var error = Assert.Throws<InvalidDataException>(() => campaign.Execute(new AdvanceCampaignTime(1)));
        Assert.Equal("Immediate mission waves exceed the supported work limit.", error.Message);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static CampaignSnapshot WaveSnapshot(RuntimeContent content, string ruleId)
    {
        var snapshot = CreateSnapshot(content, "Immediate mission waves");
        return snapshot with
        {
            Time = new(1, 1, 1, 1999, 1, 29, 55),
            RandomState = 66,
            World = snapshot.World with { Missions = [new AlienMissionSnapshot(4, ruleId, "REGION", "RACE_A", 0, 0, 0, 0, -1)] }
        };
    }
}
