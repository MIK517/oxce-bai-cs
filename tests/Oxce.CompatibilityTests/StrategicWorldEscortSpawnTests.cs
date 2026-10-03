using System.Text;
using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldEscortSpawnTests
{
    [Fact]
    public void EscortInitializationMatchesPinnedSavedOrderOriginBackupAndSpeed()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "spawnEscortRows"))
        {
            var hunter = row[0].GetBoolean();
            var requested = row[1].GetBoolean();
            var variant = row[2].GetInt32();
            var status = row[3].GetInt32();
            var source = new UfoSnapshot(9, "UFO", 4, "TRAJ", 0, 0.3, 0.1, UfoStatus.Flying, WorldAltitudes.Low)
            {
                HunterKiller = hunter, Speed = 900,
                Destination = WorldTargetReference.ForWaypoint(0, new(0.4, 0.2)),
            };
            var first = source with
            {
                UniqueId = 10, Longitude = 0.7, Latitude = 0.3, HunterKiller = variant == 2,
                MissionId = variant == 1 ? 5 : 4, Status = (UfoStatus)status,
            };
            var second = source with { UniqueId = 11, Longitude = 0.9, Latitude = 0.4, HunterKiller = false };
            UfoSnapshot[] targets = variant == 0 ? [] : [first, second];
            var result = WorldHunting.InitializeEscort(source, requested, 2200, targets);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object?[]
            {
                hunter, requested, variant, status, result.Escort, result.Escorting,
                result.Escorting ? result.Destination!.UniqueId : 0,
                result.Longitude, result.Latitude, result.Speed,
                result.OriginalWaypoint?.Longitude, result.OriginalWaypoint?.Latitude,
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())),
                CanonicalJson.Normalize(actual));
        }
    }

    [Theory]
    [InlineData(false, UfoStatus.Flying)]
    [InlineData(true, UfoStatus.Flying)]
    [InlineData(false, UfoStatus.Landed)]
    [InlineData(true, UfoStatus.IgnoreMe)]
    [InlineData(false, UfoStatus.Crashed)]
    [InlineData(true, UfoStatus.Destroyed)]
    public void WaveEscortAttachesInSavedOrderAndRepairsDeletedLeader(bool cache, UfoStatus status)
    {
        var content = Load(cache);
        var initial = SpawnSnapshot(content, status);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var world = campaign.Capture().World;
        var escort = world.Ufos.Single(ufo => ufo.UniqueId != 9);
        Assert.True(escort.HunterKiller);
        Assert.True(escort.Escort);
        Assert.Equal(2200, escort.Speed);
        if (status is UfoStatus.Crashed or UfoStatus.Destroyed)
        {
            Assert.Single(world.Ufos);
            Assert.False(escort.Escorting);
            Assert.Null(escort.OriginalWaypoint);
            Assert.Equal(WorldTargetKind.Waypoint, escort.Destination!.Kind);
            Assert.Equal(1, world.Missions[0].LiveUfos);
        }
        else
        {
            Assert.True(escort.Escorting);
            Assert.Equal(9, escort.Destination!.UniqueId);
            Assert.NotNull(escort.OriginalWaypoint);
            Assert.Equal(world.Ufos[0].Position, escort.Position);
            Assert.Equal(2, world.Missions[0].LiveUfos);
        }
        campaign = Reload(content, campaign, 81, "new-escort-spawn.sav");
        AdvanceOne(campaign);
        Reload(content, campaign, 81, "new-escort-next-tick.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WaveEscortWithoutLeaderKeepsFlagAndTrajectorySpeed(bool cache)
    {
        var content = Load(cache);
        var initial = SpawnSnapshot(content, UfoStatus.Flying);
        initial = initial with { World = initial.World with
        {
            Ufos = [], Missions = [initial.World.Missions[0] with { LiveUfos = 0 }],
        } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var escort = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(escort.HunterKiller);
        Assert.True(escort.Escort);
        Assert.False(escort.Escorting);
        Assert.Null(escort.OriginalWaypoint);
        Assert.Equal(550, escort.Speed);
        Assert.Equal(WorldTargetKind.Waypoint, escort.Destination!.Kind);
        Reload(content, campaign, 81, "new-escort-no-leader.sav");
    }

    [Fact]
    public void EscortSpeedMaximumIsValidatedEvenWhenTrajectoryRoundsItToZero()
    {
        var content = Load(overlay: """
            ufos:
              - type: NEGATIVE_ESCORT
                speedMax: -1
            alienMissions:
              - type: NEGATIVE_ESCORT_MISSION
                objective: 0
                waves:
                  - {ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 9000}
                  - {ufo: NEGATIVE_ESCORT, count: 1, trajectory: TRAJ_ESCORT_SPAWN, timer: 9000, hunterKillerPercentage: 100, escort: true}
            """);
        Assert.Equal(-1, content.RuntimeRules.Ufos[content.RuntimeRules.Ufos.GetRequired("NEGATIVE_ESCORT")].Value.StatsForRace("RACE_A").SpeedMaximum);
        var initial = SpawnSnapshot(content, UfoStatus.Flying);
        initial = initial with { World = initial.World with
        {
            Missions = [initial.World.Missions[0] with { RuleId = "NEGATIVE_ESCORT_MISSION" }],
        } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO escort speed exceeds the supported range.");
    }

    [Fact]
    public void EscortSpawnStopsBeforeCraftRetargetIsNeeded()
    {
        var content = Load();
        var initial = SpawnSnapshot(content, UfoStatus.Flying).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Status = "STR_OUT", LowFuel = false, MissionComplete = false },
        });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "New UFO escorts require craft retarget forecasts.");
    }

    private static RuntimeContent Load(bool cache = false, string? overlay = null) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache, extraRules: overlay);

    private static CampaignSnapshot SpawnSnapshot(RuntimeContent content, UfoStatus status)
    {
        var initial = CreateSnapshot(content, "New UFO escorts");
        return initial with
        {
            RandomState = 81,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_ESCORT_SPAWN", "REGION", "RACE_A", 1, 0, 0, 1, -1)],
                Ufos = [new(9, "UFO_SCOUT", 4, "TRAJ_SPAWN_FIXED", 0, 0.4, 0.2, status,
                    status == UfoStatus.Landed ? WorldAltitudes.Ground : WorldAltitudes.High)
                {
                    Id = 3, Speed = status == UfoStatus.Flying ? 900 : 0, MissionWaveNumber = 0,
                    SecondsRemaining = status == UfoStatus.Landed ? 30 : 60,
                    Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6)),
                }],
            },
        };
    }
}
