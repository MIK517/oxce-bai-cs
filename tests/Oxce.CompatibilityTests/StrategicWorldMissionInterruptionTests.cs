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
    public void SingleRewardOrDirectDisablerSelectsDailyInterruptionAndPersists(string research, bool completed)
    {
        var content = Load();
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var snapshot = Existing(content, "GEN_STOP_TOPIC") with { CompletedResearch = completed ? ["GEN_STOP"] : [], RandomState = seed };
            snapshot = snapshot with { Bases = [snapshot.Bases[0] with { Research = [new(research, 1, 0, 1)] }] };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var interrupted = after.CompletedResearch.Contains("GEN_STOP");
            Assert.Equal(!completed, interrupted);
            Assert.Equal(interrupted, after.World.Missions.Count == 0);
            if (!interrupted) Assert.Equal(8970, Assert.Single(after.World.Missions).SpawnCountdown);
            for (var pass = 0; pass < 3; pass++) campaign = Reload(content, campaign, seed, "daily-reward-interruption.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGuaranteedInterruptionWithLiveUfosContinuesUntilWaypointThenCleansUp(bool sameDay)
    {
        var content = Load();
        var snapshot = Live(content) with { CompletedResearch = sameDay ? [] : ["GEN_STOP"] };
        if (sameDay) snapshot = snapshot with { Bases = [snapshot.Bases[0] with { Research = [new("GEN_STOP", 1, 0, 1)] }] };
        var campaign = Restore(content, snapshot);
        AdvanceUnblocked(campaign, 1);
        Assert.True(Assert.Single(campaign.Capture().World.Missions).Interrupted);
        Assert.Equal(UfoStatus.Flying, Assert.Single(campaign.Capture().World.Ufos).Status);
        var restored = Reload(content, campaign, 40, "live-interruption.sav");
        var beforeArrival = restored.Capture();
        var ufo = Assert.Single(beforeArrival.World.Ufos);
        restored = Restore(content, beforeArrival with
        {
            World = beforeArrival.World with { Ufos = [ufo with { Longitude = ufo.Destination!.Longitude, Latitude = ufo.Destination.Latitude }] },
        });
        AdvanceOne(restored);
        Assert.Equal(UfoStatus.Destroyed, Assert.Single(restored.Capture().World.Ufos).Status);
        Assert.Equal(1, Assert.Single(restored.Capture().World.Missions).LiveUfos);
        restored = Reload(content, restored, 40, "interrupted-arrival.sav");
        AdvanceOne(restored);
        Assert.Empty(restored.Capture().World.Ufos);
        Assert.Equal(0, Assert.Single(restored.Capture().World.Missions).LiveUfos);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveInterruptionMatchesReferenceAndTerminalArrivalSkipsCraftCombat(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        var content = Load(cache);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "liveInterruptionRows"))
        {
            var snapshot = Live(content);
            var ufo = Assert.Single(snapshot.World.Ufos);
            snapshot = snapshot with
            {
                CompletedResearch = row[0].GetInt32() != 0 ? ["GEN_STOP"] : [],
                World = snapshot.World with { Missions = [snapshot.World.Missions[0] with { Interrupted = row[1].GetInt32() != 0 }] },
            };
            var campaign = Restore(content, snapshot);
            AdvanceOne(campaign);
            Assert.Equal(row[2].GetInt32() != 0, Assert.Single(campaign.Capture().World.Missions).Interrupted);
            Assert.Equal(0, row[3].GetInt32());
            Reload(content, campaign, 40, "live-interruption-row.sav");
        }
        var arriving = Live(content) with { CompletedResearch = ["GEN_STOP"] };
        var target = Assert.Single(arriving.World.Ufos) with { Destination = WorldTargetReference.ForWaypoint(0, new(0.2001, 0.1)) };
        arriving = arriving with
        {
            World = arriving.World with { Ufos = [target with { Longitude = target.Destination!.Longitude, Latitude = target.Destination.Latitude, Detected = true }] },
        };
        arriving = arriving.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Speed = 6000,
                Fuel = 100,
                Longitude = target.Destination!.Longitude,
                Latitude = target.Destination.Latitude,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, target.Id, target.Longitude, target.Latitude) { UniqueId = target.UniqueId },
            }
        });
        var terminal = Restore(content, arriving);
        AdvanceUnblocked(terminal, 1);
        Assert.Equal(UfoStatus.Destroyed, Assert.Single(terminal.Capture().World.Ufos).Status);
        var follower = terminal.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal(0.2001, follower.Longitude);
        Assert.Equal(0.1, follower.Latitude);
        Assert.Equal(40, follower.Fuel); // The ten-minute handler still precedes terminal flight.
        Assert.Equal(WorldTargetKind.Ufo, follower.Destination!.Kind); // No craft target-loss pass yet.
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
