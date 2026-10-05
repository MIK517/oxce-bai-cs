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
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows").Where(row => row[0].GetString() != "destroyed"))
        {
            var name = row[0].GetString()!;
            var snapshot = Snapshot(content, name);
            var random = new MinimumChoices();
            var campaign = CampaignState.Restore(snapshot, content, random);
            AdvanceOne(campaign);
            rows.Add([name, Assert.Single(campaign.Capture().World.AlienBases).MinutesSinceLastHuntMissionGeneration, random.Calls]);
            Assert.Empty(campaign.Capture().World.Missions);
            Reload(content, campaign, 17, "base-hunt-timer.sav");
        }
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(TestFixtures.Rows(oracle.RootElement, "rows")
                .Where(row => row[0].GetString() != "destroyed").ToArray())),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(rows)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DestroyedCraftIsIgnoredByHuntingButWholeTickKeepsItsRemovalGuard(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-hunt-timer");
        var row = TestFixtures.Rows(oracle.RootElement, "rows").Single(row => row[0].GetString() == "destroyed");
        Assert.Equal(0, row[2].GetArrayLength());
        var content = Load(cache);
        var snapshot = Snapshot(content).WithCraft("SHIP", craft => craft with
        { Logistics = craft.Logistics! with { Damage = 100 } });
        var state = snapshot.Bases[0].Crafts.Single(c => c.RuleId == "SHIP").Logistics!;
        Assert.False(WorldAlienBaseHunting.Eligible(state, false, 100, new(0.2, 0.1), 100));
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new MinimumChoices()),
            "Destroyed craft removal requires world simulation.");
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
        var snapshot = Snapshot(content, deployment: kind == "future" ? "HUNT_FUTURE" : "HUNT_IMMEDIATE");
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
        var snapshot = Snapshot(content, "grounded", "HUNT_IMMEDIATE") with { Time = new(1, 1, 1, 1999, 0, 29, 55) };
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DelayedHuntingCreationMatchesReferenceAndPersists(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-hunt-generation");
        var content = Load(cache);
        var rows = new List<object[]>();
        var selected = TestFixtures.Rows(oracle.RootElement, "rows").Where(row => row[3].GetInt32() != 0).ToArray();
        Assert.NotEmpty(selected);
        foreach (var row in selected)
        {
            var name = row[0].GetString()!;
            var deployment = name switch
            {
                "weightedRace" => "HUNT_GENERATE_RACE",
                "site" => "HUNT_GENERATE_SITE",
                "short" => "HUNT_GENERATE_SHORT",
                _ => "HUNT_GENERATE"
            };
            var snapshot = Snapshot(content, name == "twoCrafts" ? "multiple" : "emptyChoice", deployment)
                .WithNextId("ALIEN_MISSIONS", 1);
            var random = new MinimumChoices();
            var campaign = CampaignState.Restore(snapshot, content, random);
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var source = Assert.Single(after.World.AlienBases);
            var mission = Assert.Single(after.World.Missions);
            rows.Add([name, source.MinutesSinceLastHuntMissionGeneration, random.Calls, after.World.Missions.Count,
                new object[] { mission.Id, mission.Race, mission.MissionSiteZoneArea, mission.SpawnCountdown,
                    mission.AlienBase?.Id == source.Id ? 1 : 0 }]);
            Assert.Equal(snapshot.World.AlienBases[0].GenMissionCount, source.GenMissionCount);
            Assert.Equal((0, 0, 0, false), (mission.NextWave, mission.NextUfoCounter, mission.LiveUfos, mission.Interrupted));
            Assert.Equivalent(snapshot.World.Strategy, after.World.Strategy, strict: true);
            Reload(content, campaign, 17, "hunting-generated.sav");
        }
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(selected)),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(rows)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HuntingAfterHalfHourGetsOneImmediateCountdownAndReservesMissionIds(bool cache)
    {
        var content = Load(cache);
        var snapshot = Snapshot(content, deployment: "HUNT_GENERATE_SHORT") with { Time = new(1, 1, 1, 1999, 0, 29, 55) };
        var campaign = CampaignState.Restore(snapshot, content, new MinimumChoices());
        AdvanceOne(campaign);
        Assert.Equal(30, Assert.Single(campaign.Capture().World.Missions).SpawnCountdown);
        Reload(content, campaign, 17, "hunting-half-hour.sav");
        AssertTimeBlocked(CampaignState.Restore(snapshot.WithNextId("ALIEN_MISSIONS", int.MaxValue), content, new MinimumChoices()),
            "Alien mission IDs or population exceed the supported limit.");
        var occupied = snapshot with
        {
            World = snapshot.World with
            { Missions = [new AlienMissionSnapshot(1, "MISSION_SCOUT", "REGION", "RACE_A", 0, 0, 9000, 0, -1)] }
        };
        AssertTimeBlocked(CampaignState.Restore(occupied.WithNextId("ALIEN_MISSIONS", 1), content, new MinimumChoices()),
            "Alien mission IDs or population exceed the supported limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateNoObjectHuntingPreservesOneThinkAndDefersCleanupUntilHalfHour(bool cache)
    {
        var content = Load(cache);
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-hunt-generation");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "immediateRows"))
        {
            var timer = row[0].GetString() == "immediate" ? 90 : 0;
            var snapshot = Snapshot(content, deployment: $"HUNT_NO_OBJECT_{timer}").WithNextId("ALIEN_MISSIONS", 1);
            var random = new MinimumChoices();
            var campaign = CampaignState.Restore(snapshot, content, random);
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var mission = Assert.Single(after.World.Missions);
            var source = Assert.Single(after.World.AlienBases);
            Assert.Equal(row[1].GetInt32(), source.MinutesSinceLastHuntMissionGeneration);
            Assert.Equal(row[4][3].GetInt32(), mission.SpawnCountdown);
            Assert.Equal(row[4][5].GetInt32(), mission.NextWave);
            Assert.Equal(row[4][6].GetInt32(), mission.NextUfoCounter);
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row[2])),
                CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(random.Calls)));
            Assert.Empty(after.World.Ufos);
            campaign = Reload(content, campaign, 17, "immediate-hunt.sav");
            var due = campaign.Capture() with { Time = new(1, 1, 1, 1999, 0, 29, 55) };
            campaign = CampaignState.Restore(due, content, new MinimumChoices());
            AdvanceOne(campaign);
            Assert.Empty(campaign.Capture().World.Missions);
            Reload(content, campaign, 17, "completed-hunt.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundedAutoPatrolUsesActualServicingBeforeBaseHunting(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-hunt-timer.rul", "strategic-alien-base-hunt-generation.rul"], cache,
            "crafts: [{type: BASE_REFUEL, speedMax: 6000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "maintenanceRows").Where(row => !row[0].GetBoolean() && !row[3].GetBoolean() && !row[4].GetBoolean()))
        {
            var initial = Snapshot(content, "grounded", "HUNT_IMMEDIATE") with { Time = new(1, 1, 1, 1999, 0, 29, 55) };
            initial = initial.WithCraft("SHIP", craft => craft with
            {
                RuleId = "BASE_REFUEL",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = row[1].GetInt32(),
                    IsAutoPatrolling = true,
                    Weapons = [],
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3
                }
            });
            // Refuelling launches at the player base, so place the hunting base there.
            initial = initial with
            {
                World = initial.World with
                {
                    AlienBases = [initial.World.AlienBases[0] with
                { Longitude = initial.Bases[0].Longitude, Latitude = initial.Bases[0].Latitude }]
                }
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "base-refuel-before.sav");
            if (row[6].GetString() == "STR_OUT")
            {
                AssertTimeBlocked(campaign, "Alien base hunting can create a mission; immediate hunting mission simulation is unavailable.");
                continue;
            }
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var state = after.Bases[0].Crafts.Single(craft => craft.RuleId == "BASE_REFUEL").Logistics!;
            Assert.Equal(row[5].GetInt32(), state.Fuel);
            Assert.Equal(row[6].GetString(), state.Status);
            Assert.Empty(after.World.Missions);
            Assert.Equal(60, Assert.Single(after.World.AlienBases).MinutesSinceLastHuntMissionGeneration);
            Reload(content, campaign, 17, "base-refuel-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncomingAutoPatrolHuntingFollowsArrivalCheckupAndRefuel(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.NotEmpty(TestFixtures.Rows(oracle.RootElement, "incomingCraftRows"));
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-hunt-timer.rul", "strategic-alien-base-hunt-generation.rul"], cache,
            "crafts: [{type: BASE_INCOMING, speedMax: 6000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        foreach (var fuel in new[] { 0, 60, 100 })
            foreach (var hours in new[] { 1, 2 })
            {
                var initial = Snapshot(content, "grounded", "HUNT_IMMEDIATE") with { Time = new(1, 1, 1, 1999, 0, 59, 55) };
                var owner = initial.Bases[0];
                var craft = owner.Crafts.Single(craft => craft.RuleId == "SHIP") with { RuleId = "BASE_INCOMING" };
                craft = craft with
                {
                    Logistics = craft.Logistics! with
                    {
                        Fuel = fuel,
                        Damage = 0,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    }
                };
                initial = initial with
                {
                    Bases = [owner with { Crafts = [], Soldiers = [],
                    Transfers = [new(1, hours, CampaignTransferKind.Craft, "BASE_INCOMING", 1, Craft: craft)] }],
                    World = initial.World with
                    {
                        AlienBases = [initial.World.AlienBases[0] with
                        { Longitude = owner.Longitude, Latitude = owner.Latitude }]
                    }
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "base-incoming-before.sav");
                if (hours == 1 && fuel == 60)
                {
                    AssertTimeBlocked(campaign, "Alien base hunting can create a mission; immediate hunting mission simulation is unavailable.");
                    continue;
                }
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                Assert.Empty(after.World.Missions);
                Assert.Equal(60, Assert.Single(after.World.AlienBases).MinutesSinceLastHuntMissionGeneration);
                if (hours == 2) Assert.Equal(1, Assert.Single(after.Bases[0].Transfers).Hours);
                else
                {
                    var state = Assert.Single(after.Bases[0].Crafts).Logistics!;
                    Assert.Equal(fuel == 0 ? "STR_REFUELLING" : "STR_READY", state.Status);
                    Assert.Equal(fuel == 0 ? 40 : 100, state.Fuel);
                }
                Reload(content, campaign, 17, "base-incoming-after.sav");
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenRelaunchLeaderSpeedCannotBypassBaseHuntingPreflight(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "escortFuelRows"), row => row[0].GetBoolean() && row[2].GetInt32() == 20 && !row[3].GetBoolean());
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-hunt-timer.rul", "strategic-alien-base-hunt-generation.rul"], cache,
            "crafts: [{type: BASE_ESCORT, speedMax: 6000, damageMax: 100, fuelMax: 100}, {type: BASE_HIDDEN_LEADER, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, undetectable: true}]\n");
        var initial = Snapshot(content, "grounded", "HUNT_IMMEDIATE") with { Time = new(1, 1, 1, 1999, 0, 29, 55) };
        var owner = initial.Bases[0];
        var template = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
        var leader = template with
        {
            RuleId = "BASE_HIDDEN_LEADER",
            Id = 2,
            Logistics = template.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            }
        };
        var escort = template with
        {
            RuleId = "BASE_ESCORT",
            Logistics = template.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 50,
                Speed = 6000,
                Weapons = [],
                Destination = new(WorldTargetKind.Craft, "BASE_HIDDEN_LEADER", 2, 0.2, 0.1)
            }
        };
        initial = initial with { Bases = [owner with { Crafts = [leader, escort], Soldiers = [] }] };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "base-escort-before.sav");
        AssertTimeBlocked(campaign, "Alien base hunting can create a mission; immediate hunting mission simulation is unavailable.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroChanceHuntingDoesNotRequireStochasticServicingForecasts(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-hunt-timer");
        var row = TestFixtures.Rows(oracle.RootElement, "rows").Single(row => row[0].GetString() == "zeroChance");
        Assert.Equal(1, row[2].GetArrayLength());
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-hunt-timer.rul", "strategic-alien-base-hunt-generation.rul"], cache,
            "crafts: [{type: ZERO_CHANCE_CRAFT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nmanufacture: [{name: ZERO_CHANCE_PRODUCT, time: 1, producedItems: {SUPPLY: 1}, randomProducedItems: [[1, {SUPPLY: 1}], [1, {BULKY: 1}]]}]\nfacilities: [{type: ZERO_CHANCE_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n");
        var initial = Snapshot(content, "grounded", "HUNT_ZERO_CHANCE") with { Time = new(1, 1, 1, 1999, 0, 59, 55) };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            RuleId = "ZERO_CHANCE_CRAFT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            }
        });
        var owner = initial.Bases[0];
        initial = initial with
        {
            Bases = [owner with
            { Facilities = [.. owner.Facilities, new("ZERO_CHANCE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("ZERO_CHANCE_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())] }]
        };
        var control = CampaignState.Restore(initial with { World = WorldSnapshot.Empty }, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(control, 1);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "zero-chance-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(row[1].GetInt32(), Assert.Single(after.World.AlienBases).MinutesSinceLastHuntMissionGeneration);
        Assert.Empty(after.World.Missions);
        var random = new SplitMix64RandomSource(control.Capture().RandomState);
        random.NextInclusive(0, 99); // Native percent(0) still draws for each eligible craft.
        Assert.Equal(random.State, after.RandomState);
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(control.Capture() with { World = WorldSnapshot.Empty })),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(after with { World = WorldSnapshot.Empty, RandomState = control.Capture().RandomState })));
        Reload(content, campaign, 17, "zero-chance-after.sav");
    }

    private static RuntimeContent Load(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-timer");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-hunt-timer.rul", "strategic-alien-base-hunt-generation.rul"], cache);
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
