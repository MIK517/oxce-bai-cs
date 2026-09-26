using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>AlienMission::think on half-hour boundaries, including bounded wave spawning.</summary>
public sealed class StrategicWorldMissionCountdownTests
{
    [Fact]
    public void CountdownPersistsAndStopsBeforeAnUnsupportedWave()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateCampaign(content);

        AdvanceOne(campaign);
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

        AdvanceOne(campaign);

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

        AdvanceOne(campaign);

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

        AdvanceOne(reloaded);

        var moved = Assert.Single(reloaded.Capture().World.Ufos);
        Assert.Equal(expectedPosition, moved.Position);
        Assert.Equal(0, moved.Shield);
    }

    [Fact]
    public void ScoredUfoSpawnAddsHalfHourActivity()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_SCORED");
        var before = campaign.Capture();

        AdvanceOne(campaign);

        var after = campaign.Capture();
        Assert.Equal("UFO_SCOUT", Assert.Single(after.World.Ufos).RuleId);
        Assert.False(Assert.Single(after.World.Ufos).Detected);
        Assert.Equal(3, Assert.Single(after.Regions).ActivityAlien[^1] -
            Assert.Single(before.Regions).ActivityAlien[^1]);
        Assert.Equal(3, Assert.Single(after.Countries).ActivityAlien[^1] -
            Assert.Single(before.Countries).ActivityAlien[^1]);
    }

    [Fact]
    public void HyperwaveDetectsNewUfoAndPausesAfterItsFirstMovement()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_AIRBORNE");
        campaign = CampaignState.Restore(campaign.Capture().WithFacility("RADAR_HYPER_TEST"), content,
            new SplitMix64RandomSource(75));

        var result = campaign.Execute(new AdvanceCampaignTime(12));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var contact = Assert.IsType<UfoContactDetected>(result.Events[1]);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Equal(ufo.UniqueId, contact.UniqueId);
        Assert.True(contact.Hyperwave);
        Assert.True(ufo.Detected);
        Assert.True(ufo.HyperDetected);
        Assert.Equal(1, ufo.Id);
        Assert.Equal(2, campaign.Capture().NextIds["STR_UFO"]);
        Assert.NotEqual(new WorldPosition(10 * Math.PI / 180.0, 8 * Math.PI / 180.0), ufo.Position);
    }

    [Fact]
    public void ScoredWaveStopsBeforeActivityOverflowOrSpawn()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_SCORED");
        var snapshot = campaign.Capture();
        var region = Assert.Single(snapshot.Regions);
        campaign = CampaignState.Restore(snapshot with
        {
            Regions = [region with { ActivityAlien = [int.MaxValue] }],
        }, content, new SplitMix64RandomSource(79));

        AssertTimeBlocked(campaign, "UFO alien activity exceeds the supported range.");
    }

    [Fact]
    public void ContactMarkerExhaustionStopsBeforeSpawn()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_AIRBORNE");
        campaign = CampaignState.Restore(campaign.Capture().WithNextId("STR_UFO", int.MaxValue)
            .WithFacility("RADAR_HYPER_TEST"), content, new SplitMix64RandomSource(80));

        AssertTimeBlocked(campaign, "UFO contact marker IDs are exhausted or collide with a saved UFO.");
    }

    [Fact]
    public void SpawnReservesAContactMarkerEvenWithoutRadarCoverage()
    {
        // Preflight does not forecast radar coverage; an alerting spawn always reserves a marker.
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_AIRBORNE");
        campaign = CampaignState.Restore(campaign.Capture().WithNextId("STR_UFO", int.MaxValue), content,
            new SplitMix64RandomSource(82));

        AssertTimeBlocked(campaign, "UFO contact marker IDs are exhausted or collide with a saved UFO.");
    }

    [Fact]
    public void ActiveCraftDetectsNewMissionUfo()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_AIRBORNE");
        var owner = Assert.Single(campaign.Capture().Bases);
        Assert.IsType<CraftDestinationChanged>(Assert.Single(campaign.Execute(
            new DispatchCraftToWaypoint(owner.Id, "SHIP", 1, 0.3, 0.1)).Events));
        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Contains(result.Events, item => item is UfoContactDetected { Hyperwave: false });
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Assert.Equal(1, Assert.Single(campaign.Capture().World.Missions).LiveUfos);
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

        AdvanceOne(campaign);

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
    public void RadarCompletingBeforeSpawnDetectsAtTheDailyBoundary()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_AIRBORNE");
        campaign = CampaignState.Restore(campaign.Capture().WithFacility("RADAR_HYPER_TEST", buildTime: 1) with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 23, 59, 55),
        }, content, new SplitMix64RandomSource(78));
        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Contains(result.Events, item => item is UfoContactDetected { Hyperwave: true });
        Assert.Equal(0, campaign.Capture().Bases[0].Facilities[^1].BuildTime);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
    }

    [Fact]
    public void UfoIdBatchExhaustionStopsBeforeAnyMissionSpawns()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_SPAWN_AIRBORNE");
        var snapshot = campaign.Capture().WithNextId("STR_UFO_UNIQUE", int.MaxValue - 1);
        var mission = Assert.Single(snapshot.World.Missions);
        campaign = CampaignState.Restore(snapshot with
        {
            World = snapshot.World with { Missions = [mission, mission with { Id = 5 }] },
        }, content, new SplitMix64RandomSource(77));

        // One ID remains, but both missions spawn in the same boundary.
        AssertTimeBlocked(campaign, "UFO unique IDs are exhausted or collide with a saved UFO.");
    }

    [Fact]
    public void FinalNoObjectWaveRemovesCompletedMissionWithoutRng()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = CreateWaveCampaign(content, "MISSION_EMPTY_FINAL");
        var before = campaign.Capture();

        AdvanceOne(campaign);

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

        AssertTimeBlocked(campaign, "Alien mission wave spawning requires world simulation.");
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

        AdvanceOne(interrupted);

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

        AssertTimeBlocked(restored, "Retaliation mission cleanup requires base linkage.");
    }

    private static CampaignState CreateCampaign(RuntimeContent content)
    {
        var snapshot = CreateSnapshot(content, "Mission countdown");
        return CampaignState.Restore(snapshot with
        {
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
