using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicResearchDiaryExecutionTests;
using static Oxce.CompatibilityTests.StrategicWorldSiteResearchTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchEventTests
{
    private const string PayloadReason = "Strategic event consequences require campaign event execution (Phase 6 branch 6).";

    [Theory]
    [InlineData(false, 0, "CORE", false)]
    [InlineData(true, 0, "CORE", true)]
    [InlineData(false, 2, "AUTO", false)]
    [InlineData(true, 2, "AUTO", true)]
    public void SiteResearchInterruptsDueRepeatedEventsAfterAllSites(bool cache, int scenario, string interrupt, bool over)
    {
        TestFixtures.LoadVerifiedManifest("strategic-event");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(cache, scenario, Deployment + "\nevents:\n  - {name: EVENT_DESPAWN, interruptResearch: " + interrupt + "}\n");
        var snapshot = Snapshot(content);
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Events = [new("EVENT_DESPAWN", 30) { Over = over }, new("EVENT_DESPAWN", 0)],
                GeneratedEvents = new Dictionary<string, int> { ["EVENT_DESPAWN"] = 2 },
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        campaign = Reload(content, campaign, 17, "site-event-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.Events);
        Assert.Empty(after.World.MissionSites);
        Assert.Contains(interrupt, after.CompletedResearch);
        Assert.Equal(2, after.World.GeneratedEvents["EVENT_DESPAWN"]);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-event-after.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void RepeatableOrDisabledCoreCannotAuthorizeDueEventPayload(int scenario)
    {
        var content = Content(false, scenario, Deployment + "\nevents:\n  - {name: EVENT_DESPAWN, interruptResearch: CORE}\n");
        var snapshot = Snapshot(content);
        snapshot = snapshot with
        {
            ResearchRuleStatus = scenario == 3 ? new Dictionary<string, int> { ["CORE"] = 2 } : new Dictionary<string, int>(),
            World = snapshot.World with { Events = [new("EVENT_DESPAWN", 30)] },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)), PayloadReason);
    }

    [Fact]
    public void SiteResearchRemovingExistingInterruptionStopsBeforeAnySiteOrEventMutation()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, Deployment + "\n" + """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, disables: [STOP_EVENT]}
              - {name: STOP_EVENT, cost: 1}
            events:
              - {name: EVENT_DESPAWN, interruptResearch: STOP_EVENT}
            """);
        var snapshot = Snapshot(content);
        snapshot = snapshot with
        {
            CompletedResearch = ["STOP_EVENT"],
            World = snapshot.World with { Events = [new("EVENT_DESPAWN", 30)] },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)), PayloadReason);
    }

    [Fact]
    public void FutureEventRemainsAllowedWhenSiteDisablesItsInterruption()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, Deployment + "\n" + """
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, disables: [STOP_EVENT]}
              - {name: STOP_EVENT, cost: 1}
            events:
              - {name: EVENT_DESPAWN, interruptResearch: STOP_EVENT}
            """);
        var snapshot = Snapshot(content);
        snapshot = snapshot with
        {
            CompletedResearch = ["STOP_EVENT"],
            World = snapshot.World with { Events = [new("EVENT_DESPAWN", 60)] },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.DoesNotContain("STOP_EVENT", campaign.Capture().CompletedResearch);
        Assert.Empty(campaign.Capture().World.MissionSites);
        Assert.Equal(30, Assert.Single(campaign.Capture().World.Events).SpawnCountdown);
        campaign = Reload(content, campaign, 17, "site-event-future.sav");
        var waiting = campaign.Capture();
        campaign = CampaignState.Restore(waiting with { Time = new(2, 2, 1, 1999, 0, 59, 55) }, content, new SplitMix64RandomSource(waiting.RandomState));
        AssertTimeBlocked(campaign, PayloadReason);
    }
}
