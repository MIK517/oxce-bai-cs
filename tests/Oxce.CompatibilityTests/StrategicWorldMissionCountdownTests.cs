using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>AlienMission::think on half-hour boundaries, including bounded wave spawning.</summary>
public sealed class StrategicWorldMissionCountdownTests
{
    [Fact]
    public void CountdownPersistsAndStopsBeforeAnUnsupportedWave()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateCampaign(content);

        var first = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(first.Events)).Summary.TickCount);
        Assert.Equal(60, Assert.Single(campaign.Capture().World.Missions).SpawnCountdown);

        var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(campaign.Capture()),
            content, seed: 45, name: "mission-countdown.sav").Campaign;
        var second = reloaded.Execute(new AdvanceCampaignTime(360));
        Assert.Equal(360, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(second.Events)).Summary.TickCount);
        Assert.Equal(30, Assert.Single(reloaded.Capture().World.Missions).SpawnCountdown);

        var beforeSpawn = reloaded.Capture();
        var stopped = reloaded.Execute(new AdvanceCampaignTime(360));
        Assert.Equal(359, Assert.IsType<CampaignTimeAdvanced>(stopped.Events[0]).Summary.TickCount);
        Assert.Equal("Alien mission wave spawning requires world simulation.",
            Assert.IsType<CampaignActionBlocked>(stopped.Events[^1]).Reason);
        Assert.Equal(beforeSpawn.World.Missions, reloaded.Capture().World.Missions);
        Assert.Empty(reloaded.Capture().World.Ufos);
    }

    [Fact]
    public void NoObjectWaveAdvancesCountersAndNextTimerAcrossSave()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_EMPTY_WAVE");
        var random = new SplitMix64RandomSource(campaign.Capture().RandomState);
        var firstCountdown = WorldTrajectory.SpawnCountdown(90, random);

        var first = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(first.Events)).Summary.TickCount);
        var firstState = campaign.Capture();
        var mission = Assert.Single(firstState.World.Missions);
        Assert.Equal(0, mission.NextWave);
        Assert.Equal(1, mission.NextUfoCounter);
        Assert.Equal(firstCountdown, mission.SpawnCountdown);
        Assert.Equal(random.State, firstState.RandomState);
        Assert.Empty(firstState.World.Ufos);
        Assert.Empty(firstState.World.MissionSites);

        var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(firstState),
            content, seed: 65, name: "mission-empty-wave.sav").Campaign;
        for (var boundary = 0; boundary < 3 &&
            Assert.Single(reloaded.Capture().World.Missions).SpawnCountdown > 30; boundary++)
        {
            var countdown = Assert.Single(reloaded.Capture().World.Missions).SpawnCountdown;
            var elapsed = reloaded.Execute(new AdvanceCampaignTime(360));
            Assert.Equal(360, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(elapsed.Events)).Summary.TickCount);
            Assert.Equal(countdown - 30, Assert.Single(reloaded.Capture().World.Missions).SpawnCountdown);
        }
        Assert.InRange(Assert.Single(reloaded.Capture().World.Missions).SpawnCountdown, 0, 30);
        var secondCountdown = WorldTrajectory.SpawnCountdown(3000, random);
        var second = reloaded.Execute(new AdvanceCampaignTime(360));

        Assert.Equal(360, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(second.Events)).Summary.TickCount);
        var after = reloaded.Capture();
        mission = Assert.Single(after.World.Missions);
        Assert.Equal(1, mission.NextWave);
        Assert.Equal(0, mission.NextUfoCounter);
        Assert.Equal(secondCountdown, mission.SpawnCountdown);
        Assert.Equal(random.State, after.RandomState);
        Assert.Empty(after.World.Ufos);
        Assert.Empty(after.World.MissionSites);
    }

    [Fact]
    public void OrdinaryAirborneWaveSpawnsUfoAndTransitContinuesAfterReload()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_AIRBORNE");
        var before = campaign.Capture();
        var random = new SplitMix64RandomSource(before.RandomState);
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value;
        var rawPosition = WorldGeometry.RandomPoint(region, 4, -1, random);
        var position = WorldPosition.Create(rawPosition.Longitude, rawPosition.Latitude);
        var rawDestination = WorldGeometry.RandomPoint(region, 3, -1, random);
        var destination = WorldPosition.Create(rawDestination.Longitude, rawDestination.Latitude);
        var nextCountdown = WorldTrajectory.SpawnCountdown(9000, random);
        var speedRadian = WorldGeometry.RadianSpeed(2200);
        var vector = WorldGeometry.SpeedVector(position, destination, speedRadian);
        var firstPosition = WorldGeometry.Move(position, destination, speedRadian);
        var expectedUniqueId = before.NextIds.GetValueOrDefault("STR_UFO_UNIQUE", 1);

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(result.Events)).Summary.TickCount);
        var spawned = campaign.Capture();
        var mission = Assert.Single(spawned.World.Missions);
        Assert.Equal(0, mission.NextWave);
        Assert.Equal(1, mission.NextUfoCounter);
        Assert.Equal(nextCountdown, mission.SpawnCountdown);
        Assert.Equal(1, mission.LiveUfos);
        Assert.Equal(expectedUniqueId + 1, spawned.NextIds["STR_UFO_UNIQUE"]);
        Assert.Equal(random.State, spawned.RandomState);
        var ufo = Assert.Single(spawned.World.Ufos);
        Assert.Equal(expectedUniqueId, ufo.UniqueId);
        Assert.Equal("UFO_SPAWN", ufo.RuleId);
        Assert.Equal(mission.Id, ufo.MissionId);
        Assert.Equal("TRAJ_SPAWN_FIXED", ufo.TrajectoryId);
        Assert.Equal(0, ufo.TrajectoryPoint);
        Assert.Equal(0, ufo.MissionWaveNumber);
        Assert.Equal(WorldAltitudes.High, ufo.Altitude);
        Assert.Equal(UfoStatus.Flying, ufo.Status);
        Assert.Equal(firstPosition, ufo.Position);
        Assert.Equal(destination, ufo.Destination!.Position);
        Assert.Equal(2200, ufo.Speed);
        Assert.Equal(speedRadian, ufo.SpeedRadian);
        Assert.Equal(vector.Longitude, ufo.SpeedLongitude);
        Assert.Equal(vector.Latitude, ufo.SpeedLatitude);
        Assert.Equal(WorldAltitudes.Direction(vector.Longitude, vector.Latitude), ufo.Direction);
        Assert.Equal(0, ufo.Shield);
        Assert.False(ufo.Detected);

        var reloaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(spawned),
            content, seed: 74, name: "mission-ufo-wave.sav").Campaign;
        var expectedPosition = WorldGeometry.Move(firstPosition, destination, speedRadian);

        var movement = reloaded.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(movement.Events)).Summary.TickCount);
        var moved = Assert.Single(reloaded.Capture().World.Ufos);
        Assert.Equal(expectedPosition, moved.Position);
        Assert.Equal(0, moved.Shield);
    }

    [Theory]
    [InlineData("MISSION_SPAWN_SCORED", false)]
    [InlineData("MISSION_SPAWN_AIRBORNE", true)]
    public void SpawnStopsBeforeUnsupportedScoringOrDetection(string missionRuleId, bool addRadar)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, missionRuleId);
        if (addRadar)
        {
            var snapshot = campaign.Capture();
            var owner = Assert.Single(snapshot.Bases);
            campaign = CampaignState.Restore(snapshot with
            {
                Bases = [owner with
                {
                    Facilities = [.. owner.Facilities,
                        new FacilitySnapshot("RADAR_TEST", 1, 0, 0, 0, false, false, false)],
                }],
            }, content, new SplitMix64RandomSource(75));
        }
        var before = campaign.Capture();

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal("Alien mission wave spawning requires world simulation.",
            Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void MultipleOrdinarySpawnsAllocateDistinctIdsInOneBoundary()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_AIRBORNE");
        var snapshot = campaign.Capture();
        var mission = Assert.Single(snapshot.World.Missions);
        campaign = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with { Missions = [mission, mission with { Id = 5 }] },
        }, content, new SplitMix64RandomSource(76));

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(result.Events)).Summary.TickCount);
        var after = campaign.Capture();
        Assert.Equal<int>([1, 2], [.. after.World.Ufos.Select(static ufo => ufo.UniqueId)]);
        Assert.Equal(3, after.NextIds["STR_UFO_UNIQUE"]);
        Assert.All(after.World.Missions, spawnedMission =>
        {
            Assert.Equal(1, spawnedMission.LiveUfos);
            Assert.Equal(1, spawnedMission.NextUfoCounter);
        });
    }

    [Fact]
    public void RadarCompletingBeforeSpawnDetectionStopsTheDailyBoundaryAtomically()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_AIRBORNE");
        var snapshot = campaign.Capture();
        var owner = Assert.Single(snapshot.Bases);
        campaign = CampaignState.Restore(snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 23, 59, 55),
            Bases = [owner with
            {
                Facilities = [.. owner.Facilities,
                    new FacilitySnapshot("RADAR_TEST", 1, 0, 1, 0, false, false, false)],
            }],
        }, content, new SplitMix64RandomSource(78));
        var before = campaign.Capture();

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal("Alien mission wave spawning requires world simulation.",
            Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void UfoIdBatchExhaustionStopsBeforeAnyMissionSpawns()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_AIRBORNE");
        var snapshot = campaign.Capture();
        var mission = Assert.Single(snapshot.World.Missions);
        var ids = snapshot.NextIds.ToDictionary(static pair => pair.Key, static pair => pair.Value,
            StringComparer.Ordinal);
        ids["STR_UFO_UNIQUE"] = int.MaxValue - 1;
        campaign = CampaignState.Restore(snapshot with
        {
            NextIds = ids,
            World = snapshot.World with { Missions = [mission, mission with { Id = 5 }] },
        }, content, new SplitMix64RandomSource(77));
        var before = campaign.Capture();

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal("Alien mission wave spawning requires world simulation.",
            Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void FinalNoObjectWaveRemovesCompletedMissionWithoutRng()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_EMPTY_FINAL");
        var before = campaign.Capture();

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(result.Events)).Summary.TickCount);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Empty(campaign.Capture().World.MissionSites);
        Assert.Equal(before.RandomState, campaign.Capture().RandomState);
    }

    [Fact]
    public void ZeroTimerFollowUpWaveStopsBeforeTheRecursiveSpawn()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_EMPTY_RECURSIVE");
        var before = campaign.Capture();

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal("Alien mission wave spawning requires world simulation.",
            Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void InterruptedMissionWithNoLiveUfosIsRemoved()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateCampaign(content);
        var snapshot = campaign.Capture();
        var mission = Assert.Single(snapshot.World.Missions);
        var interrupted = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with { Missions = [mission with { SpawnCountdown = 0, Interrupted = true }] },
        }, content, new SplitMix64RandomSource(46));

        var elapsed = interrupted.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(Assert.Single(elapsed.Events)).Summary.TickCount);
        Assert.Empty(interrupted.Capture().World.Missions);
    }

    [Fact]
    public void CompletedRetaliationMissionReportsPermanentCleanupGate()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateCampaign(content);
        var snapshot = campaign.Capture();
        var waves = content.RuntimeRules.AlienMissions[
            content.RuntimeRules.AlienMissions.GetRequired("MISSION_RETALIATION")].Value.Waves.Count;
        var mission = Assert.Single(snapshot.World.Missions) with
        {
            RuleId = "MISSION_RETALIATION",
            NextWave = waves,
            LiveUfos = 0,
        };
        var restored = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with { Missions = [mission] },
        }, content, new SplitMix64RandomSource(54));
        var before = restored.Capture();

        var result = restored.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal("Retaliation mission cleanup requires base linkage.",
            Assert.IsType<CampaignActionBlocked>(result.Events[^1]).Reason);
        Assert.Equivalent(before, restored.Capture(), strict: true);
    }

    private static CampaignState CreateCampaign(RuntimeContent content)
    {
        var campaign = TestFixtures.CreateLogisticsCampaign(content, "Mission countdown", CampaignDifficulty.Veteran);
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 0.2, 0.1));
        var snapshot = campaign.Capture();
        return CampaignState.Restore(snapshot with
        {
            MonthsPassed = 0,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = snapshot.World with
            {
                Missions = [new AlienMissionSnapshot(4, "MISSION_SCOUT", "REGION", "RACE_A",
                    0, 0, 90, 0, -1)],
            },
        }, content, new SplitMix64RandomSource(44));
    }

    private static CampaignState CreateWaveCampaign(RuntimeContent content, string ruleId)
    {
        var campaign = CreateCampaign(content);
        var snapshot = campaign.Capture();
        var mission = Assert.Single(snapshot.World.Missions) with
        {
            RuleId = ruleId,
            SpawnCountdown = 0,
        };
        return CampaignState.Restore(snapshot with
        {
            World = snapshot.World with { Missions = [mission] },
        }, content, new SplitMix64RandomSource(66));
    }
}
