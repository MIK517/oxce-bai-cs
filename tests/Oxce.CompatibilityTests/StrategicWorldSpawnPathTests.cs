using System.Globalization;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSpawnPathTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AirborneSpawnsChooseGroundDestinationsInReferenceOrder(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "spawnPathRows").Where(row => row[1].GetInt32() == 0))
            AssertSpawn(content, row);
    }

    private static void AssertSpawn(RuntimeContent content, System.Text.Json.JsonElement row)
    {
        var supply = row[0].GetInt32() != 0;
        var first = row[1].GetInt32();
        var second = row[2].GetInt32();
        var initial = CreateSnapshot(content, "Spawn paths");
        var snapshot = initial with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55), RandomState = 17,
            World = initial.World with
            {
                Missions = [new AlienMissionSnapshot(4, $"SPAWN_PATH_{(supply ? 1 : 0)}_{first}_{second}",
                    "REGION", "RACE_A", 0, 0, 0, 0, -1)
                { AlienBase = supply ? new(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 7, 0.2, 0.1) : null }],
                AlienBases = supply ? [new AlienBaseSnapshot(7, "ALIEN_BASE_DEPLOY", "RACE_A", 0.2, 0.1)] : [],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceOne(campaign);
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        var origin = new WorldPosition(double.Parse(row[6].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(row[7].GetString()!, CultureInfo.InvariantCulture));
        var destination = new WorldPosition(double.Parse(row[8].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(row[9].GetString()!, CultureInfo.InvariantCulture));
        Assert.InRange(WorldGeometry.Distance(destination, ufo.Destination!.Position), 0, 1e-7);
        var expected = first == 0 ? WorldGeometry.Move(origin, destination, WorldGeometry.RadianSpeed(row[5].GetInt32())) : origin;
        Assert.InRange(Math.Abs(expected.Longitude - ufo.Longitude), 0, 1e-8);
        Assert.InRange(Math.Abs(expected.Latitude - ufo.Latitude), 0, 1e-8);
        Assert.Equal((UfoStatus)row[3].GetInt32(), ufo.Status);
        Assert.Equal(first == 0 ? row[4].GetInt32() : row[4].GetInt32() - 5, ufo.SecondsRemaining);
        var random = new SplitMix64RandomSource(17);
        for (var draw = 0; draw < row[10].GetInt32(); draw++) random.NextInclusive(0, 0);
        WorldTrajectory.SpawnCountdown(9000, random);
        random.NextInclusive(0, 99); // no base radar still consumes the final percent draw.
        Assert.Equal(random.State, after.RandomState);
        Assert.Equal((first == 0 ? 1 : 2) * content.RuntimeRules.Ufos[content.RuntimeRules.Ufos.GetRequired("UFO_SCOUT")].Value.MissionScore, after.Regions[0].ActivityAlien[0]);
        Reload(content, campaign, 17, "spawn-path.sav");
    }

    private static RuntimeContent Load(bool cache) => StrategicReadinessTestContent.Load(
        ["strategic-world.rul", "strategic-spawn-paths.rul"], cache);
}
