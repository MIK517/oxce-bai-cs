using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDailyGuaranteedResearchTests
{

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ExactDailyRewardsAndAutomaticResearchInterruptRepeatedDueEvents(int mode)
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        TestFixtures.LoadVerifiedManifest("strategic-event");
        var content = Content(mode);
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = Snapshot(content, mode, seed);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var before = campaign.Capture();
            long work = 0;
            Assert.True(campaign.ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast, includeDiscoveries: true));
            Assert.Contains("STOP_GUARANTEED", forecast.CompletedResearch!);
            Assert.Equivalent(before, campaign.Capture(), strict: true);
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Contains("STOP_GUARANTEED", after.CompletedResearch);
            Assert.Empty(after.World.Events);
            Assert.Equal(2, after.World.GeneratedEvents["EVENT_GUARANTEED"]);
            Assert.Equal(forecast.CompletedResearch!.Order(StringComparer.Ordinal), after.CompletedResearch.Order(StringComparer.Ordinal));
            Assert.Equal(forecast.ScoreChange, after.ResearchScores[^1] - before.ResearchScores[^1]);
            Assert.Equal(forecast.DiaryEntries, after.ResearchDiary.Count - before.ResearchDiary.Count);
            var control = CampaignState.Restore(initial with { World = initial.World with { Events = [] } }, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "guaranteed-daily-events.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactDailyDiscoveryDeletesLinkedBaseBeforeActivityAndReturnsCraft(int mode)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        var content = Content(mode);
        var initial = Snapshot(content, mode, 17);
        var target = new WorldTargetReference(WorldTargetKind.AlienBase, "GUARANTEED_MARKER", 6, 0.2, 0.1);
        initial = initial with
        {
            Regions = [.. initial.Regions.Select(region => region with { ActivityAlien = [int.MaxValue] })],
            World = initial.World with
            {
                AlienBases = [new(6, "GUARANTEED_BASE", "RACE_A", 0.2, 0.1)],
                Missions = [new(4, "MISSION_SCOUT", "REGION", "RACE_A", 0, 0, 9000, 0, -1) { AlienBase = target }],
            },
        };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Status = "STR_OUT", Longitude = 0.2, Latitude = 0.1, Speed = 0, Destination = target },
        });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.AlienBases);
        Assert.Empty(after.World.Missions);
        Assert.Empty(after.World.Events);
        Assert.Equal(int.MaxValue, after.Regions[0].ActivityAlien[^1]);
        Assert.Null(Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "SHIP").Logistics!.Destination);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "guaranteed-daily-links.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void UncertainRepeatableDisabledIneligibleAndUnfinishedTopicsCannotAuthorizePayload(int mode)
    {
        var content = Content(mode);
        AssertEventTickRuns(CampaignState.Restore(Snapshot(content, mode, 17), content, new SplitMix64RandomSource(17)));
    }

    [Fact]
    public void LaterSiteDisablerInvalidatesExactDailyEventInterruptionBeforeMutation()
    {
        var content = Content(2);
        var initial = Snapshot(content, 2, 17);
        initial = initial with
        {
            World = initial.World with
            {
                MissionSites = [new(1, "MISSION_SITE", "GUARANTEED_CANCEL_SITE", "RACE_A", 0.2, 0.1, 0) { Detected = true }],
            },
        };
        AssertEventTickRuns(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)));
    }

    private static RuntimeContent Content(int mode)
    {
        var daily = mode switch
        {
            0 or 7 => "getOneFree: [STOP_GUARANTEED]",
            1 => "getOneFree: [REWARD_GUARANTEED]",
            3 => "reenables: [STOP_GUARANTEED]",
            4 => "getOneFree: [STOP_GUARANTEED, OTHER_GUARANTEED], sequentialGetOneFree: true",
            5 => "getOneFreeProtected: {UNLOCK: [STOP_GUARANTEED]}",
            6 => "getOneFree: [STOP_GUARANTEED, OTHER_GUARANTEED]",
            8 => "disables: [STOP_GUARANTEED]",
            _ => "points: 3",
        };
        var stop = mode is 2 or 3 or 9 ? "cost: 0, dependencies: [DAY_GUARANTEED]" : "cost: 1";
        if (mode == 7) stop += ", repeatable: true";
        if (mode == 9) stop += ", needItem: true, neededItem: SUPPLY";
        return StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
            research:
              - {name: UNLOCK, cost: 1}
              - {name: DAY_GUARANTEED, cost: 1, {{{daily}}}}
              - {name: STOP_GUARANTEED, {{{stop}}}}
              - {name: REWARD_GUARANTEED, cost: 1, lookup: STOP_GUARANTEED}
              - {name: OTHER_GUARANTEED, cost: 1}
              - {name: DISABLER_GUARANTEED, cost: 1, disables: [STOP_GUARANTEED]}
            events:
              - {name: EVENT_GUARANTEED, interruptResearch: STOP_GUARANTEED}
            alienDeployments:
              - {type: GUARANTEED_BASE, markerName: GUARANTEED_MARKER, points: 7, baseSelfDestructCode: STOP_GUARANTEED}
              - {type: GUARANTEED_CANCEL_SITE, markerName: GUARANTEED_SITE_MARKER, unlockedResearchOnDespawn: DISABLER_GUARANTEED}
            alienMissions:
              - {type: MISSION_SCOUT, interruptResearch: STOP_GUARANTEED}
            """);
    }

    private static CampaignSnapshot Snapshot(RuntimeContent content, int mode, ulong seed)
    {
        var initial = CreateSnapshot(content, "Exact daily world research");
        return initial with
        {
            RandomState = seed,
            Time = new(2, 2, 1, 1999, 23, 59, 55),
            CompletedResearch = mode == 5 ? ["UNLOCK"] : mode == 8 ? ["STOP_GUARANTEED"] : [],
            ResearchRuleStatus = mode == 3 ? new Dictionary<string, int> { ["STOP_GUARANTEED"] = 2 } : new Dictionary<string, int>(),
            Bases = [initial.Bases[0] with
            {
                Research = [new("DAY_GUARANTEED", 0, 1, mode == 10 ? 10 : 1)],
                Items = mode == 9 ? new Dictionary<string, int>() : initial.Bases[0].Items,
            }],
            World = initial.World with
            {
                Events = [new("EVENT_GUARANTEED", 30) { PreservationKey = "event:generated:EVENT_GUARANTEED:1" },
                    new("EVENT_GUARANTEED", 0) { PreservationKey = "event:generated:EVENT_GUARANTEED:2", Over = true }],
                GeneratedEvents = new Dictionary<string, int> { ["EVENT_GUARANTEED"] = 2 },
            },
        };
    }
}
