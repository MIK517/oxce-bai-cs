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
}
