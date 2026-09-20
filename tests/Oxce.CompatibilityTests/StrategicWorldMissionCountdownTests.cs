using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>AlienMission::think on half-hour boundaries before wave spawning.</summary>
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
