using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Xunit;
using static Oxce.CompatibilityTests.StrategicResearchDiaryExecutionTests;
using static Oxce.CompatibilityTests.StrategicWorldSiteResearchTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchPopulationTests
{
    [Fact]
    public void HeldExpiryProofIsClearedBeforeTheSameTriggerAfterPatrolRemovesFollower()
    {
        var content = Content(false, 0, Deployment);
        var snapshot = StrategicWorldSiteResearchReactivationTests.Linked(content, false);
        var site = snapshot.World.MissionSites[0];
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 100,
                Speed = 0,
                Longitude = 0.2,
                Latitude = 0.1,
                Destination = new(WorldTargetKind.MissionSite, "DIARY_SITE", site.Id, site.Longitude, site.Latitude),
            }
        }) with
        { Time = new(1, 1, 1, 1999, 1, 29, 55), ResearchDiarySequence = ulong.MaxValue };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 361); // Hold through both half-hour and hour proofs.
        Assert.Empty(campaign.Capture().ResearchDiary);
        var patrol = campaign.Execute(new PatrolCraft(snapshot.Bases[0].Id, "SHIP", 1));
        Assert.Empty(patrol.Events.OfType<CampaignActionBlocked>());
        AdvanceUnblocked(campaign, 359);
        AssertTimeBlocked(campaign, "Mission research diary exceeds its supported population or identity range.");
    }

    [Fact]
    public void BlockedExpiryProofIsClearedWhenDispatchAddsFollowerBeforeRetry()
    {
        var content = Content(false, 0, Deployment);
        var snapshot = StrategicWorldSiteResearchReactivationTests.Linked(content, false);
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 100,
                Speed = 0,
                Longitude = 0.2,
                Latitude = 0.1,
                Destination = null,
            }
        }) with
        { ResearchDiarySequence = ulong.MaxValue };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AssertTimeBlocked(campaign, "Mission research diary exceeds its supported population or identity range.");
        var site = snapshot.World.MissionSites[0];
        var dispatch = campaign.Execute(new DispatchCraftToTarget(snapshot.Bases[0].Id, "SHIP", 1,
            new(WorldTargetKind.MissionSite, "DIARY_SITE", site.Id, site.Longitude, site.Latitude)));
        Assert.Empty(dispatch.Events.OfType<CampaignActionBlocked>());
        AdvanceUnblocked(campaign, 1);
        Assert.Single(campaign.Capture().World.MissionSites);
        Assert.Empty(campaign.Capture().ResearchDiary);
    }

    [Fact]
    public void PopulatedDormantSiteProofsDoNotAllocateCubicallyAcrossEightThirtyTwoAndOneHundredTwentyEightUfos()
    {
        var content = Content(false, 0, Deployment);
        _ = Sample(content, 8); // Warm the shared paths before measuring population growth.
        var eight = Sample(content, 8);
        var thirtyTwo = Sample(content, 32);
        var oneHundredTwentyEight = Sample(content, 128);
        Assert.True(oneHundredTwentyEight <= thirtyTwo * 24 + 16_384,
            $"Dormant site proof allocations: 8={eight}, 32={thirtyTwo}, 128={oneHundredTwentyEight} bytes.");
    }

    private static long Sample(RuntimeContent content, int population)
    {
        var snapshot = StrategicWorldSiteResearchReactivationTests.Linked(content, false);
        var ufo = snapshot.World.Ufos[0];
        var site = snapshot.World.MissionSites[0];
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Ufos = Enumerable.Range(0, population).Select(index => ufo with { UniqueId = 9 + index, Id = 3 + index, LandId = 40 + index }).ToArray(),
                MissionSites = Enumerable.Range(0, population).Select(index => site with { Id = 1 + index, UfoUniqueId = 9 + index }).ToArray(),
                Missions = [snapshot.World.Missions[0] with { LiveUfos = population }],
            }
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        CampaignCommandResult? result = null;
        var command = new AdvanceCampaignTime(1);
        var bytes = AllocatedBytes(() => result = campaign.Execute(command));
        Assert.Empty(result!.Events.OfType<CampaignActionBlocked>());
        Assert.Empty(campaign.Capture().World.MissionSites);
        Assert.Equal(population, campaign.Capture().World.Ufos.Count);
        Assert.Equal(4, campaign.Capture().ResearchDiary.Count);
        return bytes;
    }
}
