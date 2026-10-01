using System.Text;
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

public sealed class StrategicWorldMissionInterruptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyInterruptionAndCleanupMatchExtractedReference(bool cache)
    {
        var content = Load(cache);
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        var rows = new List<int[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "interruptionRows"))
        {
            var snapshot = Existing(content) with { CompletedResearch = row[0].GetInt32() != 0 ? ["UNLOCK"] : [] };
            snapshot = snapshot with
            {
                World = snapshot.World with
                {
                    Missions = [snapshot.World.Missions[0] with
                    { Interrupted = row[1].GetInt32() != 0 }]
                },
            };
            var campaign = Restore(content, snapshot);
            AdvanceOne(campaign);
            var after = campaign.Capture();
            rows.Add([row[0].GetInt32(), row[1].GetInt32(),
                after.World.Missions.Count == 0 || after.World.Missions[0].Interrupted ? 1 : 0,
                after.World.Missions.Count == 0 ? 1 : 0]);
            Assert.Equal(snapshot.RandomState, after.RandomState);
            Reload(content, campaign, 40, "interruption-oracle.sav");
        }
        Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(oracle.RootElement.GetProperty("interruptionRows").GetRawText())),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(rows)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedMissionWaitsUntilFollowingDailyCheckAndNeverRestartsAfterReload(bool cache)
    {
        var content = Load(cache);
        var snapshot = Existing(content) with { CompletedResearch = ["UNLOCK"] };
        snapshot = snapshot with
        {
            World = snapshot.World with { Missions = [], AlienBases = [new(2, "GEN_BASE_interrupt", "RACE_A", 0.2, 0.1)] },
        };
        var campaign = Restore(content, snapshot);
        AdvanceOne(campaign);
        Assert.False(Assert.Single(campaign.Capture().World.Missions).Interrupted);
        Assert.Equal(1, Assert.Single(campaign.Capture().World.AlienBases).GenMissionCount);
        campaign = Reload(content, campaign, 40, "before-generated-interruption.sav");
        AdvanceUnblocked(campaign, 17_280);
        Assert.Empty(campaign.Capture().World.Missions);
        campaign = Reload(content, campaign, 40, "after-generated-interruption.sav");
        AdvanceUnblocked(campaign, 17_280);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Equal(1, Assert.Single(campaign.Capture().World.AlienBases).GenMissionCount);
    }

    [Theory]
    [InlineData(false, "GEN_STOP")]
    [InlineData(true, "GEN_STOP")]
    [InlineData(false, "GEN_SOURCE")]
    [InlineData(true, "GEN_SOURCE")]
    public void SameDayPrimaryOrLookupResearchInterruptsBeforeUnsupportedDueWave(bool cache, string research)
    {
        var content = Load(cache);
        var snapshot = Existing(content, "GEN_STOP_TOPIC");
        snapshot = snapshot with
        {
            Bases = [snapshot.Bases[0] with { Research = [new(research, 1, 0, 1)] }],
            World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { SpawnCountdown = 0 }] },
        };
        var campaign = Restore(content, snapshot);
        var result = campaign.Execute(new AdvanceCampaignTime(2));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Single(result.Events.OfType<CampaignResearchCompleted>());
        Assert.Contains("GEN_STOP", campaign.Capture().CompletedResearch);
        Assert.Empty(campaign.Capture().World.Missions);
        Reload(content, campaign, 40, "same-day-mission-interruption.sav");
    }

    [Fact]
    public void InterruptionRemovesTheDueScoredWaveFromCapacityForecasts()
    {
        var content = Load();
        var snapshot = Existing(content, "GEN_SCORED_INTERRUPT");
        snapshot = snapshot with
        {
            CompletedResearch = ["GEN_STOP"],
            Regions = [.. snapshot.Regions.Select(region => region with { ActivityAlien = [int.MaxValue] })],
            World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { SpawnCountdown = 0 }] },
        };
        var campaign = Restore(content, snapshot);
        AdvanceOne(campaign);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Equal(int.MaxValue, campaign.Capture().Regions[0].ActivityAlien[^1]);
    }

    [Theory]
    [InlineData("GEN_REWARD", false)]
    [InlineData("GEN_DISABLE", true)]
    public void UncertainRewardsOrDisablersBlockBeforeResearchAndRandomness(string research, bool completed)
    {
        var content = Load();
        var snapshot = Existing(content, "GEN_STOP_TOPIC") with { CompletedResearch = completed ? ["GEN_STOP"] : [] };
        snapshot = snapshot with { Bases = [snapshot.Bases[0] with { Research = [new(research, 1, 0, 1)] }] };
        AssertTimeBlocked(Restore(content, snapshot),
            "Alien mission interruption depends on uncertain daily research outcomes.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGuaranteedInterruptionWithLiveUfosBlocksAtomically(bool sameDay)
    {
        var content = Load();
        var snapshot = Live(content) with { CompletedResearch = sameDay ? [] : ["GEN_STOP"] };
        if (sameDay) snapshot = snapshot with { Bases = [snapshot.Bases[0] with { Research = [new("GEN_STOP", 1, 0, 1)] }] };
        AssertTimeBlocked(Restore(content, snapshot),
            "Alien mission interruption with live UFOs requires world simulation.");
    }

    [Fact]
    public void UnrelatedResearchAllowsLiveMissionToContinue()
    {
        var content = Load();
        var snapshot = Live(content);
        snapshot = snapshot with { Bases = [snapshot.Bases[0] with { Research = [new("GEN_OTHER", 1, 0, 1)] }] };
        var campaign = Restore(content, snapshot);
        AdvanceUnblocked(campaign, 1);
        Assert.False(Assert.Single(campaign.Capture().World.Missions).Interrupted);
        Assert.Contains("GEN_OTHER", campaign.Capture().CompletedResearch);
    }

    [Theory]
    [InlineData("GEN_MISSING_INTERRUPT", "Alien mission interruption references undefined research.")]
    [InlineData("GEN_RETALIATION_INTERRUPT", "Retaliation mission cleanup requires base linkage.")]
    public void MissingResearchOrDeferredCleanupStopsBeforeDailyMutation(string mission, string reason)
    {
        var content = Load();
        var snapshot = Existing(content, mission) with { CompletedResearch = ["UNLOCK"] };
        AssertTimeBlocked(Restore(content, snapshot), reason);
    }

    [Fact]
    public void ZeroSavedLiveCountCannotOrphanAReferencedUfoOnInterruption()
    {
        var content = Load();
        var snapshot = Live(content) with { CompletedResearch = ["GEN_STOP"] };
        snapshot = snapshot with { World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { LiveUfos = 0 }] } };
        AssertTimeBlocked(Restore(content, snapshot), "Alien mission cannot expire while UFOs still reference it.");
    }

    [Fact]
    public void CompletedResearchDoesNotInterruptBeforeDailyCheck()
    {
        var content = Load();
        var snapshot = Existing(content) with { CompletedResearch = ["UNLOCK"], Time = new(1, 1, 1, 1999, 0, 29, 55) };
        var campaign = Restore(content, snapshot);
        AdvanceOne(campaign);
        var mission = Assert.Single(campaign.Capture().World.Missions);
        Assert.False(mission.Interrupted);
        Assert.Equal(8970, mission.SpawnCountdown);
    }

    private static RuntimeContent Load(bool cache = false)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-generation.rul"], cache);
    }

    private static CampaignSnapshot Existing(RuntimeContent content, string mission = "GEN_INTERRUPT")
    {
        var snapshot = CreateSnapshot(content, "Mission interruption").WithFacility("GEN_LAB");
        return snapshot with
        {
            RandomState = 0,
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            World = snapshot.World with { Missions = [new(4, mission, "REGION", "RACE_A", 0, 0, 9000, 0, -1)] },
        };
    }

    private static CampaignSnapshot Live(RuntimeContent content) =>
        CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 23, 59, 55), "UFO_SCOUT", 2200,
            "GEN_STOP_TOPIC", missionWaveNumber: 0).Capture().WithFacility("GEN_LAB");

    private static CampaignState Restore(RuntimeContent content, CampaignSnapshot snapshot) =>
        CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(40));
}
