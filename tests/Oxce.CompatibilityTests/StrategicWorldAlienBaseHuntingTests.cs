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

public sealed class StrategicWorldAlienBaseHuntingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HuntingTimersAndSafeChoicesMatchExtractedReference(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-hunt-timer");
        var content = Load(cache);
        Assert.False(content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value.Undetectable);
        Assert.True(content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIELD_CRAFT")].Value.Undetectable);
        Assert.Equal(0, content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIELD_CRAFT")].Value.SightRange);
        var rows = new List<object[]>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var name = row[0].GetString()!;
            if (name == "destroyed")
            {
                // Destroyed craft cleanup stays guarded. Verify its hunting predicate directly.
                var craft = Snapshot(content).Bases[0].Crafts.Single(c => c.RuleId == "SHIP");
                Assert.False(WorldAlienBaseHunting.Eligible(craft.Logistics! with { Status = "STR_OUT", Damage = 100 },
                    false, 100, new(0.2, 0.1), 100));
                rows.Add([name, 60, Array.Empty<int[]>()]);
                continue;
            }
            var snapshot = Snapshot(content, name);
            var random = new MinimumChoices();
            var campaign = CampaignState.Restore(snapshot, content, random);
            AdvanceOne(campaign);
            rows.Add([name, Assert.Single(campaign.Capture().World.AlienBases).MinutesSinceLastHuntMissionGeneration, random.Calls]);
            Assert.Empty(campaign.Capture().World.Missions);
            Reload(content, campaign, 17, "base-hunt-timer.sav");
        }
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(oracle.RootElement.GetProperty("rows"))),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(rows)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FuelReturnPrecedesHuntingEligibilityAndNotificationsKeepTickFallthrough(bool cache)
    {
        var content = Load(cache);
        var snapshot = Snapshot(content, deployment: "HUNT_LIVE").WithCraft("SHIP", craft => craft with
        { Logistics = craft.Logistics! with { Fuel = 0 } });
        var campaign = CampaignState.Restore(snapshot, content, new MinimumChoices());
        var events = campaign.Execute(new AdvanceCampaignTime(1)).Events;
        Assert.Single(events.OfType<CraftLowFuel>());
        Assert.Equal(1, Assert.Single(events.OfType<CampaignTimeAdvanced>()).Summary.TickCount);
        Assert.Equal(60, Assert.Single(campaign.Capture().World.AlienBases).MinutesSinceLastHuntMissionGeneration);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Equal(0UL, campaign.Capture().RandomState);
        Reload(content, campaign, 17, "base-hunt-low-fuel.sav");
    }

    [Theory]
    [InlineData("live", false)]
    [InlineData("live", true)]
    [InlineData("overflow", false)]
    [InlineData("overflow", true)]
    [InlineData("future", false)]
    [InlineData("future", true)]
    public void UnsafeHuntingBlocksBeforeTickMutation(string kind, bool cache)
    {
        var content = Load(cache);
        var snapshot = Snapshot(content, deployment: kind == "future" ? "HUNT_FUTURE" : "HUNT_LIVE");
        if (kind == "overflow") snapshot = snapshot with
        {
            World = snapshot.World with
            { AlienBases = [snapshot.World.AlienBases[0] with { MinutesSinceLastHuntMissionGeneration = int.MaxValue - 5 }] }
        };
        var reason = kind switch
        {
            "overflow" => "Alien base hunting timer exceeds the supported range.",
            "future" => "Alien base hunting timeline has no entry for the current month.",
            _ => "Alien base hunting can create a mission; immediate hunting mission simulation is unavailable.",
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new MinimumChoices()), reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoPatrolRelaunchCannotEnterHuntingAfterMutatingEarlierHandlers(bool cache)
    {
        var content = Load(cache);
        var snapshot = Snapshot(content, "grounded", "HUNT_LIVE") with { Time = new(1, 1, 1, 1999, 0, 29, 55) };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            { Status = "STR_REFUELLING", Fuel = 99, IsAutoPatrolling = true }
        });
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new MinimumChoices()),
            "Alien base hunting can create a mission; immediate hunting mission simulation is unavailable.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InactiveFutureTimelineAndDailyDeletedBaseDoNotBlockOwnedTicks(bool cache)
    {
        var content = Load(cache);
        var campaign = CampaignState.Restore(Snapshot(content, "grounded", "HUNT_FUTURE"), content, new MinimumChoices());
        AdvanceOne(campaign);
        Assert.Equal(60, Assert.Single(campaign.Capture().World.AlienBases).MinutesSinceLastHuntMissionGeneration);
        var snapshot = Snapshot(content, deployment: "HUNT_DELETE") with
        { Time = new(1, 1, 1, 1999, 23, 59, 55), CompletedResearch = ["UNLOCK"] };
        campaign = CampaignState.Restore(snapshot, content, new MinimumChoices());
        AdvanceOne(campaign);
        Assert.Empty(campaign.Capture().World.AlienBases);
        Reload(content, campaign, 17, "deleted-hunting-base.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndetectableCraftAllowsSeventyTwoHoursOfPersistentHuntingTimers(bool cache)
    {
        var content = Load(cache);
        var campaign = CampaignState.Restore(Snapshot(content, "undetectable", "HUNT_LIVE"), content, new MinimumChoices());
        AdvanceUnblocked(campaign, 25_920);
        campaign = Reload(content, campaign, 17, "hunting-36-hours.sav");
        AdvanceUnblocked(campaign, 25_920);
        Assert.Equal(4370, Assert.Single(campaign.Capture().World.AlienBases).MinutesSinceLastHuntMissionGeneration);
        Assert.Empty(campaign.Capture().World.Missions);
        Reload(content, campaign, 17, "hunting-72-hours.sav");
    }

    [Fact]
    public void HuntingCraftChecksAreBoundedBeforeMutation()
    {
        var content = Load(false);
        var snapshot = Snapshot(content, "grounded");
        var owner = snapshot.Bases[0];
        var craft = owner.Crafts.Single(c => c.RuleId == "SHIP");
        snapshot = snapshot with
        {
            Bases = [owner with { Crafts = [.. owner.Crafts.Where(c => c.RuleId != "SHIP"),
                .. Enumerable.Range(1, 1000).Select(id => craft with { Id = id })] }],
            World = snapshot.World with
            {
                AlienBases = Enumerable.Range(1, 1001).Select(id => new AlienBaseSnapshot(id,
                "HUNT_TIMER", "RACE_A", 0.2, 0.1)
                { MinutesSinceLastHuntMissionGeneration = 50 }).ToArray()
            },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new MinimumChoices()),
            "Alien base activity and sight checks exceed the supported work limit.");
    }

    private static RuntimeContent Load(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-timer");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-hunt-timer.rul"], cache);
    }

    private static CampaignSnapshot Snapshot(RuntimeContent content, string name = "emptyChoice", string? deployment = null)
    {
        var snapshot = CreateSnapshot(content, "Hunting timers") with { RandomState = 0, Time = new(1, 1, 1, 1999, 0, 9, 55) };
        deployment ??= name switch
        {
            "disabled" => "HUNT_DISABLED",
            "zeroChance" => "HUNT_ZERO_CHANCE",
            "zeroTotal" => "HUNT_ZERO_TOTAL",
            _ => "HUNT_TIMER"
        };
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                AlienBases = [new(2, deployment, "RACE_A", 0.2, 0.1)
            { Discovered = true, MinutesSinceLastHuntMissionGeneration = name == "waiting" ? 40 : 50 }]
            }
        };
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            RuleId = name == "undetectable" ? "SHIELD_CRAFT" : "SHIP",
            Logistics = craft.Logistics! with
            {
                Status = name == "grounded" ? "STR_READY" : "STR_OUT",
                Fuel = 100,
                Damage = 0,
                Shield = name == "undetectable" ? 10 : 0,
                Longitude = 0.2,
                Latitude = name == "outside" ? 0.1 + WorldGeometry.Nautical(200) : 0.1,
                Speed = 0,
                SpeedRadian = 0,
                Destination = null,
                LowFuel = name == "lowFuel",
                MissionComplete = name == "complete"
            },
        });
        if (name == "multiple")
        {
            var owner = snapshot.Bases[0];
            snapshot = snapshot with
            {
                Bases = [owner with { Crafts = [.. owner.Crafts,
                owner.Crafts.Single(c => c.RuleId == "SHIP") with { Id = 99 }] }]
            };
        }
        return snapshot;
    }

    private sealed class MinimumChoices : IStatefulRandomSource
    {
        public List<int[]> Calls { get; } = [];
        public ulong State { get; private set; }
        public void Restore(ulong state) => State = state;
        public int NextInclusive(int minimum, int maximum) { Calls.Add([minimum, maximum]); State++; return minimum; }
        public int NextExclusive(int maximum) => NextInclusive(0, maximum - 1);
        public double NextUnit() => throw new InvalidOperationException();
    }
}
