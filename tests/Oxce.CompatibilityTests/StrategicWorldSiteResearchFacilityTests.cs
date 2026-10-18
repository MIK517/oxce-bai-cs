using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldSiteResearchTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchFacilityTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void AutomaticResearchUsesFacilitiesCompletedBeforeItsActualCaller(int buildTime, bool daily)
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-production");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            facilities:
              - {type: STORES, provideBaseFunc: [SCIENCE]}
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, points: 5}
              - {name: AUTO, cost: 0, dependencies: [CORE], requiresBaseFunc: [SCIENCE], points: 13}
            """ + "\n" + Deployment);
        var snapshot = Snapshot(content);
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Time = daily ? new(1, 1, 1, 1999, 23, 59, 55) : snapshot.Time,
            Bases = [owner with { Research = [], Facilities = owner.Facilities.Select(facility => facility with { BuildTime = buildTime, Disabled = true }).ToArray() }],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var forecast = campaign.MissionResearch.CreateForecast(daily ? CampaignTimeTrigger.OneDay : CampaignTimeTrigger.ThirtyMinutes);
        Assert.Null(forecast.Advance("CORE"));
        var completes = buildTime <= (daily ? 1 : 0);
        Assert.Equal(completes, forecast.GuaranteedResearch.Contains("AUTO"));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Equal(daily && buildTime > 0 ? buildTime - 1 : buildTime, after.Bases[0].Facilities[0].BuildTime);
        Assert.Equal(completes, after.CompletedResearch.Contains("AUTO"));
        string[] expectedDiary = completes ? ["CORE", "AUTO"] : ["CORE"];
        Assert.Equal(expectedDiary, after.ResearchDiary.Select(entry => entry.RuleId));
        Assert.True(after.Bases[0].Facilities[0].Disabled);
        Assert.Equal(snapshot.ResearchScores[^1] + (completes ? 18 : 5), after.ResearchScores[^1]);
        Assert.Empty(after.World.MissionSites);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-research-facility.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DueEventRequiresAutomaticResearchFromAnActuallyCompletingFacility(int buildTime)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            facilities:
              - {type: STORES, provideBaseFunc: [SCIENCE]}
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1}
              - {name: AUTO, cost: 0, dependencies: [CORE], requiresBaseFunc: [SCIENCE]}
            events:
              - {name: SITE_EVENT, interruptResearch: AUTO}
            """ + "\n" + Deployment);
        var snapshot = Snapshot(content);
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            Bases = [owner with { Research = [], Facilities = owner.Facilities.Select(facility => facility with { BuildTime = buildTime }).ToArray() }],
            World = snapshot.World with { Events = [new("SITE_EVENT", 0)] },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        if (buildTime == 2) AssertEventTickRuns(campaign);
        else
        {
            AdvanceUnblocked(campaign, 1);
            Assert.Empty(campaign.Capture().World.Events);
            Assert.Contains("AUTO", campaign.Capture().CompletedResearch);
            Reload(content, campaign, 17, "facility-interrupted-event.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletingFacilityCannotExposeScoreOrDiaryOverflowAfterConstructionMutates(bool diary)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            facilities:
              - {type: STORES, provideBaseFunc: [SCIENCE]}
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1, points: 5}
              - {name: AUTO, cost: 0, dependencies: [CORE], requiresBaseFunc: [SCIENCE], points: 13}
            """ + "\n" + Deployment);
        var snapshot = Snapshot(content);
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            ResearchScores = [diary ? 0 : int.MaxValue - 10],
            ResearchDiarySequence = diary ? ulong.MaxValue - 1 : 0,
            Bases = [owner with { Research = [], Facilities = owner.Facilities.Select(facility => facility with { BuildTime = 1 }).ToArray() }],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        // A current-state operation can fit; the world must reserve the automatic
        // discovery that becomes eligible only after today's construction.
        Assert.Null(campaign.MissionResearch.GrantReason("CORE"));
        AssertTimeBlocked(campaign, diary ? "Mission research diary exceeds its supported population or identity range." :
            "Mission research score exceeds the supported range.");
    }
}
