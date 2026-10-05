using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Gameplay.Campaigns;
using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Content;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldHuntingTests
{
    [Fact]
    public void HunterInitializationMatchesPinnedConditionalDraws()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "constructorRows"))
        {
            var random = new ConstructorRandom(row[3].GetInt32());
            var result = WorldHunting.Initialize(row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32(), random);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32(), row[3].GetInt32(),
                result.HunterKiller, result.Mode, result.Behavior, random.Calls,
            });
            Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(row.GetRawText())),
                CanonicalJson.Normalize(actual));
        }
    }

    private sealed class ConstructorRandom(int roll) : IRandomSource
    {
        public int Calls { get; private set; }
        public int NextInclusive(int minimum, int maximum)
        {
            Assert.Equal(0, minimum);
            Assert.True(maximum is 1 or 99);
            Calls++;
            return maximum == 99 ? roll : Calls % 2 == 0 ? 1 : 0;
        }
        public int NextExclusive(int exclusiveMaximum) => throw new InvalidOperationException();
        public double NextUnit() => throw new InvalidOperationException();
    }

    [Fact]
    public void AttractionMatchesPinnedMethods()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var actual = new List<int>();
        var expected = new List<int>();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "attractionRows"))
        {
            Assert.True(WorldHunting.TryAttraction(row[0].GetInt32(), row[1].GetBoolean(), row[2].GetInt32(),
                row[3].GetInt32(), row[4].GetInt32(), 100, out var attraction));
            actual.Add(attraction);
            expected.Add(row[5].GetInt32());
        }
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(actual)));
    }

    [Fact]
    public void SelectionMatchesPinnedSavedOrderAndOriginalTargetRules()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "selectionRows"))
        {
            var mode = row[0].GetInt32();
            var variant = row[1].GetInt32();
            var a = new WorldTargetReference(WorldTargetKind.Craft, "A", 1, 0, WorldGeometry.Nautical(100));
            var b = new WorldTargetReference(WorldTargetKind.Craft, "B", 2, 0, WorldGeometry.Nautical(100));
            if (variant == 4)
            {
                a = a with { Latitude = WorldGeometry.Nautical(301) };
                b = b with { Latitude = WorldGeometry.Nautical(300) };
            }
            Assert.True(WorldHunting.TryAttraction(mode, false, 0, 2000, 0, 100, out var attractionA));
            Assert.True(WorldHunting.TryAttraction(mode, false, 0, variant == 1 ? 2000 : 6000, 0, 100, out var attractionB));
            var ufo = new UfoSnapshot(9, "UFO", 4, "TRAJ", 0, 0, 0, UfoStatus.Flying, WorldAltitudes.Low)
            { Hunting = variant is 1 or 2 or 6, Destination = a };
            WorldHuntCraft[] crafts =
            [
                new(a, a.Position, attractionA, variant is 3 or 6, variant == 2, true),
                new(b, b.Position, attractionB, variant == 2, variant == 3, true),
            ];
            Assert.Equal(row[2].GetInt32(), WorldHunting.Select(ufo, variant == 5 ? 0 : 300, crafts)?.Id ?? 0);
        }
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0, 100, 2147483647)]
    [InlineData(1, 100, 2147483647)]
    public void MalformedAttractionArithmeticIsRejected(int mode, int damageMaximum, int units) =>
        Assert.False(WorldHunting.TryAttraction(mode, true, units, 6000, 50, damageMaximum, out _));
    [Fact]
    public void RetargetTransitionsMatchPinnedBackupSpeedAndRestore()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "retargetRows"))
        {
            var ufo = new UfoSnapshot(9, "UFO", 4, "TRAJ", 0, 0, 0, UfoStatus.Flying, WorldAltitudes.Low)
            { Speed = 900, Destination = WorldTargetReference.ForWaypoint(0, new(0.4, 0.2)) };
            var target = new WorldTargetReference(WorldTargetKind.Craft, "SHIP", 1, 0.8, 0.3);
            Assert.True(WorldHunting.TryHunt(ufo, target, 2200, row[0].GetInt32(), out var hunted));
            var escort = WorldHunting.Escort(hunted, new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 4, 1, 0.5) { UniqueId = 10 }, 2200);
            var restored = WorldHunting.Restore(escort);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                row[0].GetInt32(), hunted.Speed, escort.Speed, restored.Destination!.Longitude,
                restored.Destination.Latitude, restored.Hunting, restored.Escorting,
            });
            Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
            Assert.Null(restored.OriginalWaypoint);
            Assert.Equal(2200, restored.Speed);
        }
    }

    [Fact]
    public void ExplicitSpeedContextMatchesPinnedFastCondition()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "fastRows"))
        {
            var speed = (CampaignTimeSpeed)row[1].GetInt32();
            var fast = row[0].GetBoolean();
            Assert.Equal(row[2].GetBoolean(), WorldHunting.RetargetDue(CampaignTimeTrigger.FiveSeconds, speed, fast));
            Assert.True(WorldHunting.RetargetDue(CampaignTimeTrigger.TenMinutes, speed, fast));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RetargetOptionsKeepReferenceDefaultsCacheAndReload(bool cache, bool disabled)
    {
        var overlay = disabled ? "hunterKillerFastRetarget: false\nfixedUserOptions:\n  oxceGeoSuppressRedundantHKAlert: 'false'\n" : null;
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache, extraRules: overlay);
        Assert.Equal(!disabled, content.RuntimeRules.Campaign.HunterKillerFastRetarget);
        var campaign = CampaignState.Restore(CreateSnapshot(content, "Hunt settings"), content, new SplitMix64RandomSource(81));
        Assert.Equal(!disabled, campaign.Options.SuppressRedundantHunterAlert);
        Reload(content, campaign, 81, "hunting-settings.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundedHunterWavesInitializeBeforeGeometryAndReload(bool cache)
    {
        var content = Load(cache);
        var snapshot = CreateSnapshot(content, "Ground hunter") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_HUNTER_GROUND", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        var random = new SplitMix64RandomSource(snapshot.RandomState);
        var expected = WorldHunting.Initialize(100, 2, 2, random);
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(snapshot.RandomState));
        AdvanceOne(campaign);
        var spawned = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(spawned.HunterKiller);
        Assert.Equal(expected.Mode, spawned.HuntMode);
        Assert.Equal(expected.Behavior, spawned.HuntBehavior);
        Assert.Equal(UfoStatus.Landed, spawned.Status);
        Assert.Equal(55, spawned.SecondsRemaining);
        Reload(content, campaign, 81, "ground-hunter-spawn.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(12));
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Equal(UfoStatus.Flying, Assert.Single(campaign.Capture().World.Ufos).Status);
        Reload(content, campaign, 81, "ground-hunter-lift.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AirborneHunterSpawnsWithoutCandidatesAndFindsLaterAirborneCraft(bool cache)
    {
        var content = Load(cache);
        var snapshot = CreateSnapshot(content, "Air hunter") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_HUNTER_AIR", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(snapshot.RandomState));
        AdvanceOne(campaign);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(ufo.HunterKiller);
        Assert.Equal(1, ufo.HuntMode);
        Assert.Equal(1, ufo.HuntBehavior);
        Assert.False(ufo.Hunting);
        Assert.False(ufo.Escort);
        Reload(content, campaign, 81, "air-hunter-no-target.sav");
        var craft = snapshot.Bases[0].Crafts.Single(item => item.RuleId == "SHIP");
        var active = campaign.Capture().WithCraft("SHIP", item => item with
        {
            Logistics = item.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 0,
                Longitude = ufo.Longitude + 0.02,
                Latitude = ufo.Latitude,
                Destination = null,
            },
        });
        campaign = CampaignState.Restore(active, content, new SplitMix64RandomSource(active.RandomState));
        var hunt = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(hunt.Events[0]).Summary.TickCount);
        Assert.Single(hunt.Events.OfType<UfoStartedHunting>());
        Assert.DoesNotContain(hunt.Events, item => item is CampaignActionBlocked);
        Assert.Equal(craft.Id, Assert.Single(campaign.Capture().World.Ufos).Destination!.Id);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Hunting);
        Reload(content, campaign, 81, "air-hunter-later-target.sav");
    }

    [Fact]
    public void AirborneHunterSpawnStopsBeforeCandidateRetargetForecastIsRequired()
    {
        var content = Load();
        var snapshot = HuntingSnapshot(content);
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = snapshot.World with
            {
                Ufos = [],
                Missions = [snapshot.World.Missions[0] with
                    { RuleId = "MISSION_HUNTER_AIR", NextWave = 0, LiveUfos = 0, SpawnCountdown = 0 }],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81)),
            "Alien mission wave spawning requires world simulation.");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[[4, 0, 100]]")]
    public void HunterSpawnRejectsIncompleteTrajectoriesBeforeIndexing(string waypoints)
    {
        var content = Load(overlay: $"ufoTrajectories:\n  - id: HUNTER_INCOMPLETE\n    waypoints: {waypoints}\nalienMissions:\n  - type: HUNTER_INCOMPLETE_MISSION\n    objective: 0\n    waves:\n      - {{ufo: SPAWN_HUNTER_DEFAULTS, count: 1, trajectory: HUNTER_INCOMPLETE, timer: 9000}}\n");
        var snapshot = CreateSnapshot(content, "Incomplete hunter trajectory") with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "HUNTER_INCOMPLETE_MISSION", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81)),
            "Alien mission wave spawning requires world simulation.");
    }

    [Fact]
    public void AirborneHunterSpawnIncludesPossibleAutoPatrolTargetsInItsBoundary()
    {
        var content = Load();
        var snapshot = CreateSnapshot(content, "Auto patrol hunter boundary");
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Status = "STR_REFUELLING", IsAutoPatrolling = true },
        }) with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_HUNTER_AIR", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81)),
            "Alien mission wave spawning requires world simulation.");
    }

    [Fact]
    public void IgnoredHunterTargetsMatchThePinnedCraftMethod()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "ignoredRows"))
        {
            var actual = JsonSerializer.SerializeToUtf8Bytes(new[]
                { row[0].GetBoolean(), row[1].GetBoolean(), WorldHunting.IsIgnored(row[0].GetBoolean(), row[1].GetBoolean()) });
            Assert.Equal(CanonicalJson.Normalize(System.Text.Encoding.UTF8.GetBytes(row.GetRawText())),
                CanonicalJson.Normalize(actual));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AirborneHunterSpawnsWhileExistingCraftAreIgnored(bool cache, bool complete)
    {
        var content = Load(cache);
        var snapshot = HuntingSnapshot(content);
        snapshot = snapshot.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { LowFuel = !complete, MissionComplete = complete },
        }) with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_HUNTER_AIR", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(ufo.HunterKiller);
        Assert.False(ufo.Hunting);
        Reload(content, campaign, 81, "ignored-target-hunter-spawn.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndetectableAirborneCraftDoNotBlockNewHunterWaves(bool cache)
    {
        var content = Load(cache, "crafts:\n  - type: SHIP\n    undetectable: true\n");
        Assert.True(content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SHIP")].Value.Undetectable);
        var snapshot = HuntingSnapshot(content) with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_HUNTER_AIR", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(ufo.HunterKiller);
        Assert.False(ufo.Hunting);
        Reload(content, campaign, 81, "undetectable-target-hunter-spawn.sav");
    }

    [Fact]
    public void NewHunterRetargetWorkIsReservedBeforeTheCreationTickMutates()
    {
        var content = Load(overlay: "alienMissions:\n  - type: MASS_HUNTER_AIR\n    objective: 0\n    waves:\n      - {ufo: SPAWN_HUNTER_DEFAULTS, count: 1001, trajectory: TRAJ_SPAWN_FIXED, timer: 0}\n");
        var snapshot = CreateSnapshot(content, "Hunter work reservation");
        var craft = snapshot.Bases[0].Crafts.Single(item => item.RuleId == "SHIP");
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            Bases = [snapshot.Bases[0] with
            {
                Soldiers = [],
                Crafts = Enumerable.Range(1, 1001).Select(id => craft with { Id = id }).ToArray(),
            }],
            World = new WorldSnapshot
            {
                Missions = [new(4, "MASS_HUNTER_AIR", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81)),
            "New UFO retargeting exceeds the supported work limit.");
    }

    [Fact]
    public void NewHunterWorkReservesFiniteCraftProductionBeforeSpawning()
    {
        var content = Load(overlay: $"alienMissions:\n  - type: PRODUCTION_HUNTER_WAVE\n    objective: 0\n    waves:\n      - {{ufo: SPAWN_HUNTER_DEFAULTS, count: 1001, trajectory: TRAJ_SPAWN_FIXED, timer: 0}}\nmanufacture:\n  - name: HUNTER_CRAFT_PRODUCT\n    category: STR_CRAFT\n    time: 1\n    producedItems: {{SHIP: 1}}\nfacilities:\n  - type: HUNTER_WORKSHOP\n    mapName: LOGISTICS\n    size: 1\n    workshops: 2000\n");
        var snapshot = CreateSnapshot(content, "Produced craft hunter work");
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 59, 55),
            Bases = [snapshot.Bases[0] with
            {
                Facilities = [.. snapshot.Bases[0].Facilities, new("HUNTER_WORKSHOP", 5, 5, 0, 0, false, false, false)],
                Productions = [new("HUNTER_CRAFT_PRODUCT", 1001, 0, 1001, false, false, false,
                    new Dictionary<string, int>())],
            }],
            World = new WorldSnapshot
            {
                Missions = [new(4, "PRODUCTION_HUNTER_WAVE", "REGION", "RACE_A", 0, 0, 0, 0, -1)],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(81)),
            "New UFO retargeting exceeds the supported work limit.");
    }

    private static RuntimeContent Load(bool cache = false, string? overlay = null) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache, extraRules: overlay);

    private static CampaignSnapshot HuntingSnapshot(RuntimeContent content, bool escort = false)
    {
        var initial = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 0, 0),
            ufoRuleId: escort ? "ESCORT_HUNTER" : "HUNTER", speed: 2200).Capture();
        initial = initial with
        {
            World = initial.World with { Ufos = [initial.World.Ufos[0] with { HunterKiller = true, Escort = escort }] },
        };
        return initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 0,
                Longitude = 0.45,
                Latitude = 0.21,
                Destination = null,
            },
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HalfHourRefuellingAndRelaunchPrecedeHunterSelection(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, "crafts: [{type: REFUEL_HUNT, speedMax: 6000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "maintenanceRows").Where(row => !row[0].GetBoolean() && !row[4].GetBoolean()))
        {
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "REFUEL_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = row[1].GetInt32(),
                    LowFuel = row[3].GetBoolean(),
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = [],
                },
            }) with
            { Time = new(1, 1, 1, 1999, 1, 29, 55) };
            initial = initial with { Bases = [initial.Bases[0] with { Longitude = 0.45, Latitude = 0.21 }] };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
            campaign = Reload(content, campaign, 81, "refuel-hunting-before.sav");
            Assert.Empty(campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "REFUEL_HUNT").Logistics!.Weapons);
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var ship = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "REFUEL_HUNT").Logistics!;
            Assert.Equal(row[6].GetString(), ship.Status);
            Assert.Equal(row[5].GetInt32() - (ship.Status == "STR_OUT" ? 60 : 0), ship.Fuel);
            Assert.Equal(Math.Max(0, row[10].GetInt32() - 1), ship.Takeoff);
            Assert.Equal(row[11].GetInt32() != 0, Assert.Single(after.World.Ufos).Hunting);
            Assert.Equal(row[11].GetInt32() != 0 ? 1 : 0, result.Events.OfType<UfoStartedHunting>().Count());
            Reload(content, campaign, 81, "refuel-hunting-after.sav");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HalfHourItemRefuellingPrecedesHunterSelection(bool cache, bool hourly)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, "crafts: [{type: ITEM_REFUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "maintenanceRows").Where(row => row[0].GetBoolean() && !row[4].GetBoolean()))
        {
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "ITEM_REFUEL_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = row[1].GetInt32(),
                    LowFuel = row[3].GetBoolean(),
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = [],
                },
            }) with
            { Time = new(1, 1, 1, 1999, 1, hourly ? 59 : 29, 55) };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            { Longitude = 0.45, Latitude = 0.21, Items = row[2].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[2].GetInt32() } }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "item-refuel-hunt-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "ITEM_REFUEL_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                craft.Fuel + (craft.Status == "STR_OUT" ? 1 : 0), craft.Status,
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.LowFuel,
                result.Events.OfType<CraftArrivalServiceMessage>().Any(message => message.Message == "STR_NOT_ENOUGH_ITEM_TO_REFUEL_CRAFT_AT_BASE"),
                craft.Takeoff + (craft.Status == "STR_OUT" ? 1 : 0),
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "item-refuel-hunt-after.sav");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SharedFuelStocksPreserveCraftOrderBeforeHunting(bool cache, bool hourly)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, "crafts: [{type: SHARED_REFUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "sharedFuelRows"))
        {
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, hourly ? 59 : 29, 55) };
            var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
            CraftSnapshot Craft(int id, int fuel, bool auto) => template with
            {
                RuleId = "SHARED_REFUEL_HUNT",
                Id = id,
                Logistics = template.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = fuel,
                    IsAutoPatrolling = auto,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = [],
                },
            };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21,
                Items = row[1].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[1].GetInt32() },
                Crafts = [Craft(1, row[0].GetInt32(), row[2].GetBoolean()), Craft(2, 60, true)],
                Soldiers = [],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "shared-refuel-hunt-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var first = after.Bases[0].Crafts[0].Logistics!;
            var second = after.Bases[0].Crafts[1].Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                first.Fuel + (first.Status == "STR_OUT" ? 1 : 0), first.Status,
                second.Fuel + (second.Status == "STR_OUT" ? 1 : 0), second.Status,
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(3).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "shared-refuel-hunt-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HourlyMaintenancePreservesRepairAndRearmDelayBeforeHunting(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, "crafts: [{type: HOURLY_REFUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, repairRate: 10, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "hourlyMaintenanceRows"))
        {
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "HOURLY_REFUEL_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = row[0].GetString()!,
                    Fuel = row[1].GetInt32(),
                    Damage = row[2].GetInt32(),
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = [],
                },
            }) with
            { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            initial = initial with { Bases = [initial.Bases[0] with { Longitude = 0.45, Latitude = 0.21 }] };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "hourly-hunt-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "HOURLY_REFUEL_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                craft.Fuel + (craft.Status == "STR_OUT" ? 20 : 0), craft.Status, craft.Damage,
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(3).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "hourly-hunt-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundedIncomingAutoPatrolRelaunchesBeforeHunting(bool cache)
    {
        var content = Load(cache, "crafts: [{type: INCOMING_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var incoming = template with
        {
            RuleId = "INCOMING_HUNT",
            Id = 2,
            Logistics = template.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
                Weapons = [],
            },
        };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21,
            Crafts = initial.Bases[0].Crafts.Where(craft => craft.RuleId != "SHIP").ToArray(),
            Transfers = [new(1, 1, CampaignTransferKind.Craft, "INCOMING_HUNT", 1, Craft: incoming)],
        }]
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "INCOMING_HUNT").Logistics!;
        Assert.Equal("STR_OUT", craft.Status);
        Assert.Equal(80, craft.Fuel);
        Assert.Equal(59, craft.Takeoff);
        Assert.True(after.World.Ufos[0].Hunting);
        Assert.Equal("INCOMING_HUNT", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "incoming-hunt.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncomingCraftCheckupAndRelaunchMatchNative(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, "crafts: [{type: NATIVE_INCOMING_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "incomingCraftRows"))
        {
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
            var incoming = template with
            {
                RuleId = "NATIVE_INCOMING_HUNT",
                Id = 2,
                Logistics = template.Logistics! with
                {
                    Status = row[1].GetString()!,
                    Fuel = row[2].GetInt32(),
                    Damage = row[3].GetInt32(),
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Longitude = 0.9,
                    Latitude = 0.3,
                    Weapons = [],
                },
            };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Crafts = [], Soldiers = [],
                Transfers = [new(1, row[0].GetInt32(), CampaignTransferKind.Craft, "NATIVE_INCOMING_HUNT", 1, Craft: incoming)],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "native-incoming-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var delivered = after.Bases[0].Transfers.Count == 0;
            var craft = delivered ? Assert.Single(after.Bases[0].Crafts).Logistics! : Assert.Single(after.Bases[0].Transfers).Craft!.Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                delivered, craft.Status, craft.Fuel, craft.Damage, craft.Longitude, craft.Latitude,
                craft.Takeoff + (delivered && craft.Status == "STR_OUT" ? 1 : 0),
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(4).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "native-incoming-after.sav");
        }
    }

    [Fact]
    public void HunterCombatWithIncomingCraftIsGuardedBeforeDelivery()
    {
        var content = Load(overlay: "crafts: [{type: CLOSE_INCOMING_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var incoming = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP") with
        {
            RuleId = "CLOSE_INCOMING_HUNT",
            Id = 2,
            Logistics = new(60, 0, "STR_REFUELLING", [], new Dictionary<string, int>(), [])
            { IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 },
        };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.4, Latitude = 0.2, Crafts = [], Soldiers = [],
            Transfers = [new(1, 1, CampaignTransferKind.Craft, "CLOSE_INCOMING_HUNT", 1, Craft: incoming)],
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuppliedFuelAndAmmunitionPrecedeHalfHourHunterSelection(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, "crafts: [{type: SUPPLIED_FUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true, weapons: 1}]\ncraftWeapons: [{type: HUNT_TRANSFER_GUN, ammoMax: 10, launcher: SUPPLY, clip: SUPPLY}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "suppliedFuelRows"))
        {
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "SUPPLIED_FUEL_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = row[1].GetString()!,
                    Fuel = row[2].GetInt32(),
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = row[4].GetBoolean() ? [new("HUNT_TRANSFER_GUN", 0)] : [],
                },
            }) with
            { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21,
                Items = row[3].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[3].GetInt32() },
                Transfers = [new(1, row[0].GetInt32(), CampaignTransferKind.Item, "SUPPLY", 1)],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "supplied-fuel-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SUPPLIED_FUEL_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                after.Bases[0].Transfers.Count == 0, craft.Status, craft.Fuel,
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "supplied-fuel-after.sav");
        }
    }

    [Fact]
    public void ArrivalItemReuseWorkIsBoundedWithoutUfos()
    {
        var content = Load();
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55), World = new() };
        var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Soldiers = [],
            Crafts = Enumerable.Range(1, 1001).Select(id => template with
            { Id = id, Logistics = template.Logistics! with { Status = "STR_READY", Weapons = [] } }).ToArray(),
            Transfers = Enumerable.Range(1, 1001).Select(id =>
                new TransferSnapshot(id, 1, CampaignTransferKind.Item, "SUPPLY", 1)).ToArray(),
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Arrival item reuse exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CombinedCraftAndItemTransfersReuseAfterAllArrivals(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, "crafts: [{type: COMBINED_TRANSFER_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true, weapons: 1}]\ncraftWeapons: [{type: HUNT_COMBINED_GUN, ammoMax: 10, launcher: SUPPLY, clip: SUPPLY}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "combinedTransferRows"))
        {
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            var incoming = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP") with
            {
                RuleId = "COMBINED_TRANSFER_HUNT",
                Id = 2,
                Logistics = new(row[2].GetInt32(), 0, "STR_REFUELLING",
                    row[1].GetBoolean() ? [new("HUNT_COMBINED_GUN", 0)] : [], new Dictionary<string, int>(), [])
                { IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 },
            };
            var itemTransfer = new TransferSnapshot(1, 1, CampaignTransferKind.Item, "SUPPLY", 1);
            var craftTransfer = new TransferSnapshot(2, 1, CampaignTransferKind.Craft, "COMBINED_TRANSFER_HUNT", 1, Craft: incoming);
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(), Crafts = [], Soldiers = [],
                Transfers = row[0].GetBoolean() ? [itemTransfer, craftTransfer] : [craftTransfer, itemTransfer],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "combined-transfer-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            Assert.Empty(after.Bases[0].Transfers);
            var craft = Assert.Single(after.Bases[0].Crafts).Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                craft.Status, craft.Fuel, after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                craft.Weapons.Any(weapon => weapon is { Rearming: true }),
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(3).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "combined-transfer-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CraftAmmunitionSharesFuelStocksWithoutForecastRandomDraws(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "ammunitionFuelRows")
            .GroupBy(row => (Clip: row[0].GetInt32(), Saving: row[1].GetBoolean())))
        {
            var content = Load(cache, $"items: [{{type: HUNT_SHARED_AMMO, clipSize: {group.Key.Clip}}}]\ncraftWeapons: [{{type: HUNT_AMMO_GUN, ammoMax: 10, rearmRate: 4, bulletSaving: {group.Key.Saving.ToString().ToLowerInvariant()}, launcher: SUPPLY, clip: HUNT_SHARED_AMMO}}]\ncrafts: [{{type: HUNT_AMMO_CONSUMER, speedMax: 2000, damageMax: 100, fuelMax: 100, weapons: 1}}, {{type: HUNT_AMMO_FUEL, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: HUNT_SHARED_AMMO, autoPatrol: true}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
                var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
                var gun = template with
                {
                    RuleId = "HUNT_AMMO_CONSUMER",
                    Id = 2,
                    Logistics = template.Logistics! with
                    {
                        Status = "STR_REARMING",
                        Fuel = 100,
                        Weapons = [new("HUNT_AMMO_GUN", row[2].GetInt32(), Rearming: true)],
                    },
                };
                var fuel = template with
                {
                    RuleId = "HUNT_AMMO_FUEL",
                    Id = 1,
                    Logistics = template.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3,
                    },
                };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Crafts = [gun, fuel], Soldiers = [],
                    Items = row[3].GetInt32() == 0 ? new Dictionary<string, int>() :
                        new Dictionary<string, int> { ["HUNT_SHARED_AMMO"] = row[3].GetInt32() },
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "ammo-fuel-before.sav");
                if (row[11].GetInt32() != 0)
                {
                    AssertTimeBlocked(campaign, "UFO retargeting requires stochastic craft ammunition stock forecasts.");
                    continue;
                }
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var weapon = after.Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_AMMO_CONSUMER").Logistics!.Weapons[0]!;
                var refuelled = after.Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_AMMO_FUEL").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                {
                    weapon.Ammo, weapon.Rearming, after.Bases[0].Items.GetValueOrDefault("HUNT_SHARED_AMMO"),
                    refuelled.Status, refuelled.Fuel,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
                });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).Take(6).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(content, campaign, 81, "ammo-fuel-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FacilityAmmunitionAndCraftFuelShareNativeStockOrder(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, "crafts: [{type: FACILITY_FUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nfacilities: [{type: SHARED_HUNT_FACILITY, mapName: LOGISTICS, ammoMax: 2, rearmRate: 1, ammoItem: SUPPLY}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "facilityFuelRows"))
        {
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "FACILITY_FUEL_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = 60,
                    Weapons = [],
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                },
            }) with
            { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21,
                Items = row[0].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[0].GetInt32() },
                Facilities = [.. initial.Bases[0].Facilities, new("SHARED_HUNT_FACILITY", 1, 1,
                    row[2].GetInt32(), row[1].GetInt32(), row[3].GetBoolean(), true, false)],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "facility-fuel-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var facility = after.Bases[0].Facilities.Single(facility => facility.RuleId == "SHARED_HUNT_FACILITY");
            var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "FACILITY_FUEL_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                facility.Ammo, facility.AmmoMissingReported, result.Events.OfType<FacilityServiceMessage>().Any(),
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(4).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "facility-fuel-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverdueUndeliveredTransfersAreRefusedBeforeUnsafeNativeArrival(bool hunter)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "invalidArrivalRows"))
        {
            Assert.True(row[2].GetBoolean());
            Assert.False(row[3].GetBoolean());
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
            initial = initial with
            {
                World = hunter ? initial.World : new(),
                Bases = [initial.Bases[0] with
                { Transfers = [new(1, row[0].GetInt32(), CampaignTransferKind.Item, "SUPPLY", 1)] }],
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "overdue-arrival.sav");
            AssertTimeBlocked(campaign, "Undelivered transfer arrival requires positive remaining hours.");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void StockFreeDailyResearchAllowsFuelAndAmmunitionHunterTargets(bool cache, bool ammunition)
    {
        var fuelItem = ammunition ? "" : ", refuelItem: SUPPLY";
        var content = Load(cache, $"crafts: [{{type: STOCK_FREE_RESEARCH_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1{fuelItem}}}]\ncraftWeapons: [{{type: STOCK_FREE_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}}]\nresearch: [{{name: STOCK_FREE_TOPIC, cost: 1, getOneFree: [STOCK_FREE_BONUS]}}, {{name: STOCK_FREE_BONUS, cost: 1}}]\nfacilities: [{{type: STOCK_FREE_LAB, mapName: LOGISTICS, labs: 1}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "STOCK_FREE_RESEARCH_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = ammunition ? [new("STOCK_FREE_GUN", 0)] : [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21,
            Items = new Dictionary<string, int> { ["SUPPLY"] = 1 },
            Facilities = [.. initial.Bases[0].Facilities, new("STOCK_FREE_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("STOCK_FREE_TOPIC", 1, 0, 1)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "stock-free-research-before.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Contains("STOCK_FREE_TOPIC", after.CompletedResearch);
        Assert.Contains("STOCK_FREE_BONUS", after.CompletedResearch);
        Assert.Equal(ammunition ? 1 : 0, after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
        Assert.True(after.World.Ufos[0].Hunting);
        Assert.Equal("STOCK_FREE_RESEARCH_HUNT", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "stock-free-research-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyResearchAmmunitionReusePrecedesHuntingWithoutItemFuel(bool cache)
    {
        var content = Load(cache, "crafts: [{type: RESEARCH_AMMO_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1}]\ncraftWeapons: [{type: RESEARCH_AMMO_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}]\nresearch: [{name: RESEARCH_AMMO_DELIVERY, cost: 1, spawnedItem: SUPPLY}]\nfacilities: [{type: RESEARCH_AMMO_LAB, mapName: LOGISTICS, labs: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "RESEARCH_AMMO_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [new("RESEARCH_AMMO_GUN", 0)],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("RESEARCH_AMMO_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("RESEARCH_AMMO_DELIVERY", 1, 0, 1)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "research-ammo-hunt.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "RESEARCH_AMMO_HUNT").Logistics!;
        Assert.Equal("STR_REARMING", craft.Status);
        Assert.Equal(60, craft.Fuel);
        Assert.True(craft.Weapons[0]!.Rearming);
        Assert.False(after.World.Ufos[0].Hunting);
        Assert.Equal(1, after.Bases[0].Items["SUPPLY"]);
        Reload(content, campaign, 81, "research-ammo-hunt-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OtherBaseResearchCompletionCanRefundFuelBeforeHunting(bool cache)
    {
        var content = Load(cache, "crafts: [{type: RESEARCH_FUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nresearch: [{name: RESEARCH_FUEL_RETURN, cost: 100, needItem: true, destroyItem: true, neededItem: SUPPLY}]\nfacilities: [{type: RESEARCH_FUEL_LAB, mapName: LOGISTICS, labs: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "RESEARCH_FUEL_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        var owner = initial.Bases[0] with
        {
            Longitude = 0.45,
            Latitude = 0.21,
            Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("RESEARCH_FUEL_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("RESEARCH_FUEL_RETURN", 1, 0, 100)],
        };
        initial = initial with
        {
            Bases = [owner, owner with
        {
            Id = 1, Name = "Research headquarters", Crafts = [], Soldiers = [], Transfers = [],
            Research = [new("RESEARCH_FUEL_RETURN", 1, 0, 1)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "cross-base-research-fuel.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var predicted = campaign.Capture();
        Assert.True(predicted.World.Ufos[0].Hunting);
        Assert.Equal("RESEARCH_FUEL_HUNT", predicted.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "cross-base-research-fuel-after.sav");
        var control = initial with { World = initial.World with { Ufos = [initial.World.Ufos[0] with { HunterKiller = false }] } };
        campaign = CampaignState.Restore(control, content, new SplitMix64RandomSource(81));
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Empty(after.Bases[0].Research);
        Assert.Equal("STR_OUT", after.Bases[0].Crafts.Single(craft => craft.RuleId == "RESEARCH_FUEL_HUNT").Logistics!.Status);
        Assert.Equal(99, after.Bases[0].Crafts.Single(craft => craft.RuleId == "RESEARCH_FUEL_HUNT").Logistics!.Fuel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResearchCancellationRefundMatchesNativeFinishedAndHeldState(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchRefundRows")
            .GroupBy(row => (Holding: row[0].GetBoolean(), Returns: row[1].GetBoolean())))
        {
            var content = Load(cache, $"research: [{{name: NATIVE_HELD_RETURN, cost: 1, needItem: {group.Key.Holding.ToString().ToLowerInvariant()}, destroyItem: {(group.Key.Holding && !group.Key.Returns).ToString().ToLowerInvariant()}, returnsItem: {group.Key.Returns.ToString().ToLowerInvariant()}, neededItem: SUPPLY}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content) with { World = new() };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Items = row[3].GetInt32() == 0 ? new Dictionary<string, int>() :
                        new Dictionary<string, int> { ["SUPPLY"] = row[3].GetInt32() },
                    Research = [new("NATIVE_HELD_RETURN", 0, row[2].GetBoolean() ? 1 : 0, 1)],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "native-held-before.sav");
                Assert.IsType<CampaignResearchChanged>(Assert.Single(campaign.Execute(
                    new ConfigureResearchProject(0, "NATIVE_HELD_RETURN", 0, Cancel: true)).Events));
                var after = campaign.Capture();
                Assert.Empty(after.Bases[0].Research);
                Assert.Equal(row[4].GetInt32(), after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
                Reload(content, campaign, 81, "native-held-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, int.MaxValue)]
    [InlineData(true, int.MaxValue)]
    public void CompletingHeldResearchRefundFeedsHalfHourHunterSelection(bool cache, int stock)
    {
        var content = Load(cache, "crafts: [{type: DIRECT_REFUND_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nresearch: [{name: DIRECT_REFUND_TOPIC, cost: 1, needItem: true, returnsItem: true, neededItem: SUPPLY}]\nfacilities: [{type: DIRECT_REFUND_LAB, mapName: LOGISTICS, labs: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "DIRECT_REFUND_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Items = stock == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = stock },
            Facilities = [.. initial.Bases[0].Facilities, new("DIRECT_REFUND_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("DIRECT_REFUND_TOPIC", 1, 0, 1)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "direct-refund-before.sav");
        if (stock == int.MaxValue)
        {
            AssertTimeBlocked(campaign, "Research refunds exceed the supported stock range.");
            return;
        }
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Contains("DIRECT_REFUND_TOPIC", after.CompletedResearch);
        Assert.Equal(0, after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
        Assert.Equal(99, after.Bases[0].Crafts.Single(craft => craft.RuleId == "DIRECT_REFUND_HUNT").Logistics!.Fuel);
        Assert.Equal("DIRECT_REFUND_HUNT", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "direct-refund-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedResearchDeliveriesMatchNativeStockAndReuseOrder(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchSpawnRows")
            .GroupBy(row => (Fuel: row[0].GetBoolean(), Main: row[1].GetBoolean(), Count: row[2].GetInt32(), Duplicates: row[3].GetInt32())))
        {
            var fuel = group.Key.Fuel ? ", refuelItem: SUPPLY" : "";
            var items = "MISSING" + string.Concat(Enumerable.Repeat(", SUPPLY", group.Key.Duplicates));
            var content = Load(cache, $"crafts: [{{type: FIXED_RESEARCH_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1{fuel}}}]\ncraftWeapons: [{{type: FIXED_RESEARCH_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}}]\nresearch: [{{name: FIXED_RESEARCH_DELIVERY, cost: 1, spawnedItem: {(group.Key.Main ? "SUPPLY" : "MISSING")}, spawnedItemCount: {group.Key.Count}, spawnedItemList: [{items}]}}]\nfacilities: [{{type: FIXED_RESEARCH_LAB, mapName: LOGISTICS, labs: 1}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "FIXED_RESEARCH_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = group.Key.Fuel ? [] : [new("FIXED_RESEARCH_GUN", 0)],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3,
                    },
                }) with
                { Time = new(1, 1, 1, 1999, 23, 59, 55) };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21,
                    Items = row[4].GetInt32() == 0 ? new Dictionary<string, int>() :
                        new Dictionary<string, int> { ["SUPPLY"] = row[4].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("FIXED_RESEARCH_LAB", 1, 1, 0, 0, false, false, false)],
                    Research = [new("FIXED_RESEARCH_DELIVERY", 1, 0, 1)],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "fixed-research-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "FIXED_RESEARCH_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                {
                    result.Events.OfType<SuppliesArrived>().Sum(arrived => arrived.TransferIds.Count),
                    after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    craft.Weapons.Count == 0 ? false : craft.Weapons[0]?.Rearming ?? false,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
                });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Assert.Empty(after.Bases[0].Transfers);
                Reload(content, campaign, 81, "fixed-research-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ResearchDeliveryLimitsStopBeforeAnyTickMutation(bool cache, bool identities)
    {
        var content = Load(cache, "crafts: [{type: LIMITED_RESEARCH_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nresearch: [{name: LIMITED_RESEARCH_DELIVERY, cost: 1, spawnedItem: SUPPLY}]\nfacilities: [{type: LIMITED_RESEARCH_LAB, mapName: LOGISTICS, labs: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "LIMITED_RESEARCH_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21,
            Items = identities ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue },
            Facilities = [.. initial.Bases[0].Facilities, new("LIMITED_RESEARCH_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("LIMITED_RESEARCH_DELIVERY", 1, 0, 1)],
        }]
        };
        if (identities) initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["oxcePortTransfer"] = int.MaxValue },
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "research-delivery-limit.sav");
        AssertTimeBlocked(campaign, identities ? "Research arrivals exhaust the transfer identity range." :
            "Research arrivals exceed the supported stock range.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DisabledHeldProjectCannotFundAnUnforecastHunterInterception(bool cache, bool close)
    {
        var content = Load(cache, "crafts: [{type: DISABLED_REFUND_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nresearch: [{name: DISABLED_REFUND_TOPIC, cost: 100, needItem: true, destroyItem: true, neededItem: SUPPLY}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "DISABLED_REFUND_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            ResearchRuleStatus = new Dictionary<string, int> { ["DISABLED_REFUND_TOPIC"] = 2 },
            Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
                Research = [new("DISABLED_REFUND_TOPIC", 0, 0, 100)],
            }],
            World = initial.World with { Ufos = [initial.World.Ufos[0] with { Longitude = close ? 0.45 : 0.4, Latitude = close ? 0.21 : 0.2 }] },
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "disabled-refund-hunt.sav");
        if (close)
        {
            AssertTimeBlocked(campaign, "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
            return;
        }
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Empty(after.Bases[0].Research);
        Assert.DoesNotContain("DISABLED_REFUND_TOPIC", after.CompletedResearch);
        Assert.Equal(99, after.Bases[0].Crafts.Single(craft => craft.RuleId == "DISABLED_REFUND_HUNT").Logistics!.Fuel);
        Assert.True(after.World.Ufos[0].Hunting);
        Assert.Equal("DISABLED_REFUND_HUNT", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "disabled-refund-hunt-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LookupDiscoveryDoesNotSpawnOrCancelPrimaryStockEffects(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "researchPrimaryRows").Where(row => !row[1].GetBoolean()))
        {
            var direct = row[0].GetBoolean();
            var content = Load(cache, $"crafts: [{{type: LOOKUP_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nresearch: [{{name: LOOKUP_SOURCE, cost: 1, lookup: LOOKUP_TOPIC, spawnedItem: {(direct ? "SUPPLY" : "")}}}, {{name: LOOKUP_TOPIC, cost: 100, spawnedItem: SUPPLY, spawnedItemCount: 100, needItem: true, returnsItem: true, neededItem: SUPPLY}}]\nfacilities: [{{type: LOOKUP_LAB, mapName: LOGISTICS, labs: 2}}]\n");
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "LOOKUP_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = 60,
                    Weapons = [],
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3
                },
            }) with
            { Time = new(1, 1, 1, 1999, 23, 59, 55) };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
                Facilities = [.. initial.Bases[0].Facilities, new("LOOKUP_LAB", 1, 1, 0, 0, false, false, false)],
                Research = [new("LOOKUP_SOURCE", 1, 0, 1), new("LOOKUP_TOPIC", 1, 0, 100)],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "lookup-stocks-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            Assert.Contains("LOOKUP_TOPIC", after.CompletedResearch);
            Assert.Equal("LOOKUP_TOPIC", Assert.Single(after.Bases[0].Research).RuleId);
            Assert.Equal(row[2].GetInt32(), result.Events.OfType<SuppliesArrived>().Sum(arrived => arrived.TransferIds.Count));
            Assert.Equal(0, after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
            Assert.Equal(direct, after.World.Ufos[0].Hunting);
            Assert.Equal(direct ? 99 : 60, after.Bases[0].Crafts.Single(craft => craft.RuleId == "LOOKUP_HUNT").Logistics!.Fuel);
            Reload(content, campaign, 81, "lookup-stocks-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResearchDiscoveryAndDisabledRefundsMatchNativeBeforeHunting(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchDiscoveryRows").GroupBy(row => row[0].GetInt32()))
        {
            var mode = group.Key;
            var source = mode is 0 or 3 ? ", disables: [DISCOVERY_HELD]" : mode == 1 ? ", lookup: DISCOVERY_LOOKUP" : "";
            var lookup = mode == 1 ? ", disables: [DISCOVERY_HELD]" : "";
            var automatic = mode == 2 ? ", disables: [DISCOVERY_HELD]" : mode == 3 ? ", reenables: [DISCOVERY_HELD]" : "";
            var content = Load(cache, $"crafts: [{{type: DISCOVERY_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nresearch: [{{name: DISCOVERY_SOURCE, cost: 1{source}}}, {{name: DISCOVERY_LOOKUP, cost: 1{lookup}}}, {{name: DISCOVERY_AUTO, cost: 0, dependencies: [DISCOVERY_SOURCE]{automatic}}}, {{name: DISCOVERY_HELD, cost: 100, needItem: true, destroyItem: true, neededItem: SUPPLY}}]\nfacilities: [{{type: DISCOVERY_LAB, mapName: LOGISTICS, labs: 1}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "DISCOVERY_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    },
                }) with
                { Time = new(1, 1, 1, 1999, 23, 59, 55), CompletedResearch = row[1].GetBoolean() ? ["DISCOVERY_SOURCE"] : [] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
                    Facilities = [.. initial.Bases[0].Facilities, new("DISCOVERY_LAB", 1, 1, 0, 0, false, false, false)],
                    Research = [new("DISCOVERY_SOURCE", 1, 0, 1), new("DISCOVERY_HELD", 0, row[2].GetBoolean() ? 100 : 0, 100)],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "discovery-refund-before.sav");
                Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var fuel = after.Bases[0].Crafts.Single(craft => craft.RuleId == "DISCOVERY_HUNT").Logistics!.Fuel;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                {
                    fuel == 99 ? 1 : 0, after.ResearchRuleStatus.GetValueOrDefault("DISCOVERY_HELD"),
                    after.CompletedResearch.Contains("DISCOVERY_AUTO"),
                });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(3).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Assert.Equal(row[3].GetInt32() != 0, after.World.Ufos[0].Hunting);
                Reload(content, campaign, 81, "discovery-refund-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeterministicFreeResearchStocksAndCancellationMatchNative(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "researchRewardRows")
            .GroupBy(row => (Sequential: row[0].GetBoolean(), Duplicate: row[1].GetBoolean())))
        {
            var content = Load(cache, $"crafts: [{{type: REWARD_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nresearch: [{{name: REWARD_SOURCE, cost: 100, needItem: true, destroyItem: true, neededItem: SUPPLY, sequentialGetOneFree: {group.Key.Sequential.ToString().ToLowerInvariant()}, getOneFree: [{(group.Key.Duplicate ? "REWARD_A, REWARD_A" : "REWARD_A")}], getOneFreeProtected: {{REWARD_GATE: [REWARD_B]}}}}, {{name: REWARD_A, cost: 1, spawnedItem: SUPPLY}}, {{name: REWARD_B, cost: 1, spawnedItem: SUPPLY, spawnedItemCount: 2}}, {{name: REWARD_GATE, cost: 1}}]\nfacilities: [{{type: REWARD_LAB, mapName: LOGISTICS, labs: 1}}]\n");
            foreach (var row in group)
            {
                List<string> completed = [];
                if (row[2].GetInt32() == 1) completed.Add("REWARD_A");
                if (row[3].GetInt32() == 1) completed.Add("REWARD_B");
                if (row[4].GetBoolean()) completed.Add("REWARD_GATE");
                Dictionary<string, int> statuses = new(StringComparer.Ordinal);
                if (row[2].GetInt32() == 2) statuses.Add("REWARD_A", 2);
                if (row[3].GetInt32() == 2) statuses.Add("REWARD_B", 2);
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "REWARD_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    },
                }) with
                { Time = new(1, 1, 1, 1999, 23, 59, 55), CompletedResearch = completed, ResearchRuleStatus = statuses };
                var owner = initial.Bases[0] with
                {
                    Longitude = 0.45,
                    Latitude = 0.21,
                    Items = new Dictionary<string, int>(),
                    Facilities = [.. initial.Bases[0].Facilities, new("REWARD_LAB", 1, 1, 0, 0, false, false, false)],
                    Research = [new("REWARD_SOURCE", 1, 99, 100)],
                };
                initial = initial with
                {
                    Bases = [owner, owner with { Id = 1, Crafts = [], Soldiers = [],
                    Research = [new("REWARD_SOURCE", 0, 0, 100)] }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "reward-stocks-before.sav");
                if (!group.Key.Sequential && row[2].GetInt32() == 0 && row[3].GetInt32() == 0 && row[4].GetBoolean())
                {
                    AssertTimeBlocked(campaign, "UFO retargeting requires daily research fuel-stock forecasts.");
                    continue;
                }
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var discoveries = Assert.Single(result.Events.OfType<CampaignResearchCompleted>()).Discoveries;
                var bonus = row[5].GetInt32();
                Assert.Equal(bonus != 0, after.World.Ufos[0].Hunting);
                if (bonus != 0) Assert.Contains(bonus == 1 ? "REWARD_A" : "REWARD_B", discoveries);
                Assert.Equal(row[6].GetInt32() - (bonus == 0 ? 0 : 1), after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
                Assert.Equal(row[7].GetBoolean(), after.Bases[1].Research.Count != 0);
                Assert.Equal(row[7].GetBoolean() ? 0 : 1, after.Bases[1].Items.GetValueOrDefault("SUPPLY"));
                Assert.Equal(bonus == 0 ? 60 : 99, after.Bases[0].Crafts.Single(craft => craft.RuleId == "REWARD_HUNT").Logistics!.Fuel);
                Reload(content, campaign, 81, "reward-stocks-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void QueuedAutomaticDiscoveryKeepsNativeOrderAndAtomicHuntBoundary(bool cache, bool close)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "researchQueuedRows"))
        {
            var lateId = row[0].GetBoolean() ? "QUEUED_A_LATE" : "QUEUED_Z_LATE";
            var content = Load(cache, $"crafts: [{{type: QUEUED_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nresearch: [{{name: QUEUED_SOURCE, cost: 1}}, {{name: QUEUED_DISABLER, cost: 0, dependencies: [QUEUED_SOURCE], disables: [{lateId}], listOrder: 901}}, {{name: {lateId}, cost: 0, dependencies: [QUEUED_SOURCE], disables: [QUEUED_HELD], listOrder: 900}}, {{name: QUEUED_HELD, cost: 100, needItem: true, destroyItem: true, neededItem: SUPPLY}}]\nfacilities: [{{type: QUEUED_LAB, mapName: LOGISTICS, labs: 1}}]\n");
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "QUEUED_HUNT",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = 60,
                    Weapons = [],
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3
                },
            }) with
            { Time = new(1, 1, 1, 1999, 23, 59, 55) };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
                    Facilities = [.. initial.Bases[0].Facilities, new("QUEUED_LAB", 1, 1, 0, 0, false, false, false)],
                    Research = [new("QUEUED_SOURCE", 1, 0, 1), new("QUEUED_HELD", 0, 0, 100)],
                }],
                World = initial.World with { Ufos = [initial.World.Ufos[0] with { Longitude = close ? 0.45 : 0.4, Latitude = close ? 0.21 : 0.2 }] },
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "queued-stocks-before.sav");
            if (close)
            {
                AssertTimeBlocked(campaign, "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
                continue;
            }
            Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            Assert.Equal(row[1].GetBoolean(), after.CompletedResearch.Contains(lateId));
            Assert.Equal(row[2].GetInt32(), after.ResearchRuleStatus.GetValueOrDefault(lateId));
            Assert.Equal(row[3].GetInt32() != 0, after.World.Ufos[0].Hunting);
            Assert.Equal(99, after.Bases[0].Crafts.Single(craft => craft.RuleId == "QUEUED_HUNT").Logistics!.Fuel);
            Assert.Empty(after.Bases[0].Research);
            Reload(content, campaign, 81, "queued-stocks-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticDiscoveryUsesNativeUtf8MapOrderIndependentOfListOrder(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        const string first = "\uE000";
        const string second = "\U00010000";
        var content = Load(cache, $"research: [{{name: UNICODE_SOURCE, cost: 1}}, {{name: '{second}', cost: 0, dependencies: [UNICODE_SOURCE], listOrder: 1}}, {{name: '{first}', cost: 0, dependencies: [UNICODE_SOURCE], disables: ['{second}'], listOrder: 999}}]\nfacilities: [{{type: UNICODE_LAB, mapName: LOGISTICS, labs: 1}}]\n");
        var initial = HuntingSnapshot(content) with { World = new(), Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Facilities = [.. initial.Bases[0].Facilities, new("UNICODE_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("UNICODE_SOURCE", 1, 0, 1)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "unicode-research-before.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "researchUnicodeRows"))
        {
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { after.CompletedResearch.Contains(second), after.ResearchRuleStatus.GetValueOrDefault(second) });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
        }
        Reload(content, campaign, 81, "unicode-research-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectedDiscoveryWorkLimitStopsBeforeHunterTickMutation(bool cache)
    {
        var topics = Enumerable.Range(0, 1100).Select(index => $"BOUNDED_CHILD_{index}").ToArray();
        var automatic = string.Join(", ", topics.Select(id => $"{{name: {id}, cost: 0, requires: [BOUNDED_GATE]}}"));
        var content = Load(cache, $"crafts: [{{type: BOUNDED_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nresearch: [{{name: BOUNDED_SOURCE, cost: 1, spawnedItem: SUPPLY, unlocks: [{string.Join(", ", topics)}]}}, {{name: BOUNDED_GATE, cost: 1}}, {automatic}]\nfacilities: [{{type: BOUNDED_LAB, mapName: LOGISTICS, labs: 1}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "BOUNDED_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            },
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("BOUNDED_LAB", 1, 1, 0, 0, false, false, false)],
            Research = [new("BOUNDED_SOURCE", 1, 0, 1)],
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO retargeting exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroQuantityProductionStillReusesCraftItemsAndPersistsTransfers(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionDeliveryRows").Where(row => row[3].GetInt32() == 0)
            .GroupBy(row => (Fuel: row[0].GetBoolean(), Armed: row[1].GetBoolean(), Delay: row[2].GetInt32())))
        {
            var fuel = group.Key.Fuel ? ", refuelItem: SUPPLY" : "";
            var content = Load(cache, $"crafts: [{{type: ZERO_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1{fuel}}}]\ncraftWeapons: [{{type: ZERO_PRODUCT_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}}]\nmanufacture: [{{name: ZERO_PRODUCT, time: 1, producedItems: {{SUPPLY: 0}}, transferTimes: [{group.Key.Delay}]}}]\nfacilities: [{{type: ZERO_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "ZERO_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = group.Key.Armed ? [new("ZERO_PRODUCT_GUN", 0)] : [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    },
                }) with
                { World = new(), Time = new(1, 1, 1, 1999, 1, 59, 55) };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21,
                    Items = row[4].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[4].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("ZERO_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("ZERO_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "zero-product-before.sav");
                Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "ZERO_PRODUCT_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                    craft.Status, craft.Fuel, group.Key.Armed && craft.Weapons[0]!.Rearming, after.Bases[0].Transfers.Count });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).Take(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Assert.All(after.Bases[0].Transfers, transfer => Assert.Equal(0, transfer.Quantity));
                Reload(content, campaign, 81, "zero-product-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadedZeroItemTransferArrivesOnceAndReusesCraftAmmunition(bool cache)
    {
        var content = Load(cache, "craftWeapons: [{type: ZERO_ARRIVAL_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_READY",
                Fuel = 300,
                Weapons = [new("ZERO_ARRIVAL_GUN", 0)],
                IsAutoPatrolling = false
            },
        }) with
        { World = new(), Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = new Dictionary<string, int>(), Transfers = [new(17, 1, CampaignTransferKind.Item, "SUPPLY", 0)],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "zero-arrival-before.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(17, Assert.Single(Assert.Single(result.Events.OfType<SuppliesArrived>()).TransferIds));
        var after = campaign.Capture();
        Assert.Empty(after.Bases[0].Transfers);
        var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal("STR_REARMING", craft.Status);
        Assert.True(craft.Weapons[0]!.Rearming);
        campaign = Reload(content, campaign, 81, "zero-arrival-after.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<SuppliesArrived>());
        Assert.Empty(campaign.Capture().Bases[0].Transfers);
    }

    [Theory]
    [InlineData(CampaignTransferKind.Item, -1)]
    [InlineData(CampaignTransferKind.Engineer, 0)]
    [InlineData(CampaignTransferKind.Scientist, 0)]
    public void ZeroItemTransferCompatibilityDoesNotPermitNegativeOrEmptyStaff(CampaignTransferKind kind, int quantity)
    {
        var content = Load();
        var initial = HuntingSnapshot(content) with { World = new() };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        { Transfers = [new(17, 1, kind, kind == CampaignTransferKind.Item ? "SUPPLY" : "", quantity)] }]
        };
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalPrepaidProductionOutputsMatchNativeBeforeHunting(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionDeliveryRows")
            .GroupBy(row => (Fuel: row[0].GetBoolean(), Armed: row[1].GetBoolean(), Delay: row[2].GetInt32(), Quantity: row[3].GetInt32())))
        {
            var fuel = group.Key.Fuel ? ", refuelItem: SUPPLY" : "";
            var content = Load(cache, $"crafts: [{{type: FINAL_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1{fuel}}}]\ncraftWeapons: [{{type: FINAL_PRODUCT_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}}]\nmanufacture: [{{name: FINAL_PRODUCT, time: 1, cost: 777, requiredItems: {{SUPPLY: 10, FINAL_PRODUCT_HUNT: 1}}, producedItems: {{SUPPLY: {group.Key.Quantity}}}, transferTimes: [{group.Key.Delay}]}}]\nfacilities: [{{type: FINAL_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "FINAL_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = group.Key.Armed ? [new("FINAL_PRODUCT_GUN", 0)] : [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    },
                }) with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [0] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21,
                    Items = row[4].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[4].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("FINAL_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("FINAL_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "final-product-before.sav");
                Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "FINAL_PRODUCT_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { after.Bases[0].Items.GetValueOrDefault("SUPPLY"),
                    craft.Status, craft.Fuel, group.Key.Armed && craft.Weapons[0]!.Rearming, after.Bases[0].Transfers.Count,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Assert.Equal(0, after.Funds[^1]);
                Assert.Empty(after.Bases[0].Productions);
                Reload(content, campaign, 81, "final-product-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FinalProductionStockAndTransferLimitsStopBeforeMutation(bool cache, bool transfers)
    {
        var content = Load(cache, $"crafts: [{{type: FINAL_LIMIT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nmanufacture: [{{name: FINAL_LIMIT_PRODUCT, time: 1, producedItems: {{SUPPLY: 1}}, transferTimes: [{(transfers ? 1 : 0)}]}}]\nfacilities: [{{type: FINAL_LIMIT_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "FINAL_LIMIT_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            },
        }) with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = transfers ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue },
            Facilities = [.. initial.Bases[0].Facilities, new("FINAL_LIMIT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("FINAL_LIMIT_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
        }]
        };
        if (transfers) initial = initial with { NextIds = new Dictionary<string, int>(initial.NextIds) { ["oxcePortTransfer"] = int.MaxValue } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), transfers ?
            "Production arrivals exhaust the transfer identity range." : "Production outputs exceed the supported stock range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreeRecurringProductionStocksMatchNativeUnitCapsAndReuse(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionFreeRows")
            .GroupBy(row => (Armed: row[0].GetBoolean(), Quantity: row[4].GetInt32(), Delay: row[5].GetInt32())))
        {
            var content = Load(cache, $"crafts: [{{type: FREE_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1, refuelItem: SUPPLY}}]\ncraftWeapons: [{{type: FREE_PRODUCT_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, clip: SUPPLY}}]\nmanufacture: [{{name: FREE_PRODUCT, time: 2, cost: 0, producedItems: {{SUPPLY: {group.Key.Quantity}}}, transferTimes: [{group.Key.Delay}]}}]\nfacilities: [{{type: FREE_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: 6}}]\n");
            foreach (var row in group)
            {
                var units = row[1].GetInt32();
                var amount = row[2].GetInt32();
                var infinite = row[3].GetBoolean();
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "FREE_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = group.Key.Armed ? [new("FREE_PRODUCT_GUN", 0)] : [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    },
                }) with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [-1] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Engineers = 0,
                    Items = row[6].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[6].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("FREE_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("FREE_PRODUCT", units * 2, 1, amount, infinite, false, false, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "free-product-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "FREE_PRODUCT_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { result.Events.OfType<CampaignProductionProgress>().Sum(progress => progress.Produced),
                    after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    group.Key.Armed && craft.Weapons[0]!.Rearming, after.Bases[0].Transfers.Count,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(7).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Assert.Equal(-1, after.Funds[^1]);
                Assert.Equal(infinite || units < amount ? 1 : 0, after.Bases[0].Productions.Count);
                Reload(content, campaign, 81, "free-product-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcessiveFreeProductionWorkStopsBeforeClockInventoryAndRandomChanges(bool cache)
    {
        var content = Load(cache, "crafts: [{type: FREE_BOUND_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nmanufacture: [{name: FREE_BOUND_PRODUCT, time: 1, producedItems: {SUPPLY: 1}}]\nfacilities: [{type: FREE_BOUND_WORKSHOP, mapName: LOGISTICS, workshops: 500001}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        { RuleId = "FREE_BOUND_HUNT", Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [] } })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Engineers = 0, Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("FREE_BOUND_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("FREE_BOUND_PRODUCT", 500001, 0, 1, true, false, false, new Dictionary<string, int>())],
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO retargeting exceeds the supported work limit.");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void FreeProductionFutureQuantitiesAreBoundedBeforeMutation(bool cache, int units)
    {
        var content = Load(cache, $"crafts: [{{type: FUTURE_BOUND_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nmanufacture: [{{name: FUTURE_BOUND_PRODUCT, time: 1, producedItems: {{SUPPLY: {int.MaxValue}}}, transferTimes: [1]}}]\nfacilities: [{{type: FUTURE_BOUND_WORKSHOP, mapName: LOGISTICS, workshops: 2}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        { RuleId = "FUTURE_BOUND_HUNT", Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [] } })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = new Dictionary<string, int>(),
            Transfers = units == 1 ? [new(17, 2, CampaignTransferKind.Item, "SUPPLY", 1)] : [],
            Facilities = [.. initial.Bases[0].Facilities, new("FUTURE_BOUND_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("FUTURE_BOUND_PRODUCT", units, 0, units, false, false, false, new Dictionary<string, int>())],
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Production arrivals exceed the supported quantity range.");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void ProductionCaptivePopulationLimitsIncludeStocksAndFutureArrivals(bool cache, int delay)
    {
        var content = Load(cache, $"items: [{{type: CAPTIVE_PRODUCT_A, liveAlien: true, prisonType: 0}}, {{type: CAPTIVE_PRODUCT_B, liveAlien: true, prisonType: 0}}]\ncrafts: [{{type: CAPTIVE_BOUND_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}}]\nmanufacture: [{{name: CAPTIVE_BOUND_PRODUCT, time: 1, producedItems: {{CAPTIVE_PRODUCT_B: 1}}, transferTimes: [{delay}]}}]\nfacilities: [{{type: CAPTIVE_BOUND_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        { RuleId = "CAPTIVE_BOUND_HUNT", Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [] } })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = new Dictionary<string, int> { ["CAPTIVE_PRODUCT_A"] = int.MaxValue },
            Facilities = [.. initial.Bases[0].Facilities, new("CAPTIVE_BOUND_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("CAPTIVE_BOUND_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
        }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Production captive populations exceed the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedFutureProductionCannotUseIncompleteIdentityForecasts(bool cache)
    {
        var content = Load(cache, "crafts: [{type: MIXED_ID_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\nmanufacture: [{name: MIXED_UNFORECAST_PRODUCT, time: 1, producedItems: {SUPPLY: 0}, randomProducedItems: [[1, {SUPPLY: 1}]], transferTimes: [1]}, {name: MIXED_FIXED_PRODUCT, time: 1, producedItems: {SUPPLY: 0}, transferTimes: [1]}]\nfacilities: [{type: MIXED_ID_WORKSHOP, mapName: LOGISTICS, workshops: 2}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "MIXED_ID_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            }
        })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["oxcePortTransfer"] = int.MaxValue - 1 },
            Bases = [initial.Bases[0] with
            {
                Items = new Dictionary<string, int>(),
                Facilities = [.. initial.Bases[0].Facilities, new("MIXED_ID_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("MIXED_UNFORECAST_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>()),
                    new("MIXED_FIXED_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
            }],
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Production arrivals require other production effect forecasts.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ProductionFutureIdentitiesFollowDailyResearchAndHourlyCleanup(bool cache, bool research)
    {
        var content = Load(cache, "crafts: [{type: ORDERED_ID_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\nresearch: [{name: ORDERED_ID_RESEARCH, cost: 1, spawnedItem: SUPPLY}]\nmanufacture: [{name: ORDERED_ID_PRODUCT, time: 1, producedItems: {SUPPLY: 0}, transferTimes: [1]}]\nfacilities: [{type: ORDERED_ID_WORKSHOP, mapName: LOGISTICS, workshops: 1, labs: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "ORDERED_ID_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            }
        })
            with
        { Time = new(1, 1, 1, 1999, research ? 23 : 1, 59, 55) };
        initial = initial with
        {
            NextIds = new Dictionary<string, int>(initial.NextIds) { ["oxcePortTransfer"] = research ? int.MaxValue - 1 : 1 },
            Bases = [initial.Bases[0] with
            {
                Items = new Dictionary<string, int>(), Transfers = research ? [] : [new(int.MaxValue, 1, CampaignTransferKind.Item, "SUPPLY", 0)],
                Facilities = [.. initial.Bases[0].Facilities, new("ORDERED_ID_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Research = research ? [new("ORDERED_ID_RESEARCH", 1, 0, 1)] : [],
                Productions = [new("ORDERED_ID_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())],
            }],
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "ordered-id-before.sav");
        if (research)
        {
            AssertTimeBlocked(campaign, "Production arrivals exhaust the transfer identity range.");
            return;
        }
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Equal(1, Assert.Single(after.Bases[0].Transfers).Id);
        Assert.Equal(2, after.NextIds["oxcePortTransfer"]);
        Assert.True(after.World.Ufos[0].Hunting);
        Reload(content, campaign, 81, "ordered-id-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionFundedRelaunchStopsBeforeSameTickHunterInterception(bool cache)
    {
        var content = Load(cache, "crafts: [{type: PRODUCTION_COMBAT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\nmanufacture: [{name: PRODUCTION_COMBAT_PRODUCT, time: 1, producedItems: {SUPPLY: 1}}]\nfacilities: [{type: PRODUCTION_COMBAT_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "PRODUCTION_COMBAT_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                Weapons = [],
                Longitude = 0.45,
                Latitude = 0.21,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3
            }
        })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Items = new Dictionary<string, int>(),
                Facilities = [.. initial.Bases[0].Facilities, new("PRODUCTION_COMBAT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("PRODUCTION_COMBAT_PRODUCT", 1, 0, 2, false, false, false, new Dictionary<string, int>())],
            }],
            World = initial.World with { Ufos = [initial.World.Ufos[0] with { Longitude = 0.45, Latitude = 0.21 }] },
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "production-combat-before.sav");
        AssertTimeBlocked(campaign, "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionCraftUnloadStockOverflowStopsBeforeConsumption(bool cache)
    {
        var content = Load(cache, "manufacture: [{name: UNLOAD_LIMIT, time: 1, requiredItems: {SHIP: 1}, producedItems: {SUPPLY: 0}}]\nfacilities: [{type: UNLOAD_LIMIT_WORKSHOP, mapName: LOGISTICS, workshops: 1}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            { Status = "STR_READY", Weapons = [], Items = new Dictionary<string, int> { ["SUPPLY"] = 1 }, Vehicles = [], IsAutoPatrolling = false }
        });
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Items = new Dictionary<string, int> { ["SUPPLY"] = int.MaxValue },
            Facilities = [.. initial.Bases[0].Facilities, new("UNLOAD_LIMIT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("UNLOAD_LIMIT", 1, 0, 2, false, false, false, new Dictionary<string, int>())]
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "unload-limit-before.sav");
        AssertTimeBlocked(campaign, "Production craft unload stocks exceed the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionCraftUnloadFeedsNativeRefuellingAndHunterSelection(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionCraftUnloadRows").GroupBy(row => row[1].GetInt32()))
        {
            var content = Load(cache, $"crafts: [{{type: UNLOAD_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: UNLOAD_PRODUCT, time: 1, requiredItems: {{SHIP: 1, SUPPLY: {group.Key}}}, producedItems: {{SUPPLY: 0}}}}]\nfacilities: [{{type: UNLOAD_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 59, 55) };
                var source = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
                var hunterTarget = source with
                {
                    RuleId = "UNLOAD_HUNT",
                    Logistics = source.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        Items = new Dictionary<string, int>(),
                        Vehicles = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    }
                };
                var consumed = source with
                {
                    Id = 2,
                    Logistics = source.Logistics! with
                    {
                        Status = "STR_READY",
                        Weapons = [],
                        Items = row[2].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[2].GetInt32() },
                        Vehicles = [],
                        IsAutoPatrolling = false
                    }
                };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Crafts = [hunterTarget, consumed], Soldiers = [], Engineers = 0,
                    Items = row[0].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[0].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("UNLOAD_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("UNLOAD_PRODUCT", 1, 0, 2, false, false, false, new Dictionary<string, int>())]
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "craft-unload-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                Assert.Equal(row[3].GetBoolean(), Assert.Single(result.Events.OfType<CampaignProductionProgress>()).StopReason is null);
                var after = campaign.Capture(); var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "UNLOAD_HUNT").Logistics!;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    after.Bases[0].Crafts.Count, after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(4).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(content, campaign, 81, "craft-unload-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionCraftInputSelectionMatchesNativeQuantityAndFirstCraft(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionCraftInputRows").GroupBy(row => row[0].GetInt32()))
        {
            var content = Load(cache, $"manufacture: [{{name: CRAFT_INPUT, time: 1, requiredItems: {{SHIP: {group.Key}}}, producedItems: {{SUPPLY: 0}}}}]\nfacilities: [{{type: CRAFT_INPUT_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content) with { World = new(), Time = new(1, 1, 1, 1999, 1, 59, 55) };
                var source = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
                var craftIds = row[2].GetBoolean() ? new List<int> { 1 } : [];
                craftIds.AddRange(Enumerable.Range(2, row[1].GetInt32()));
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Soldiers = [], Engineers = 0, Items = new Dictionary<string, int>(),
                    Crafts = craftIds.Select(id => source with { Id = id, Logistics = source.Logistics! with
                    { Status = id == 1 ? "STR_OUT" : "STR_READY", Weapons = [], Items = new Dictionary<string, int>(), Vehicles = [], IsAutoPatrolling = false } }).ToArray(),
                    Facilities = [.. initial.Bases[0].Facilities, new("CRAFT_INPUT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("CRAFT_INPUT", 1, 0, 2, false, false, false, new Dictionary<string, int>())]
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "craft-input-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                Assert.Equal(row[3].GetBoolean(), progress.StopReason is null);
                var actual = JsonSerializer.SerializeToUtf8Bytes(campaign.Capture().Bases[0].Crafts.Select(craft => craft.Id).ToArray());
                var expected = JsonSerializer.SerializeToUtf8Bytes(row[4]);
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(content, campaign, 81, "craft-input-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackProgressOverflowBlocksWithoutAnyHunter(bool cache)
    {
        var content = Load(cache, "manufacture: [{name: FALLBACK_OVERFLOW, time: 100, producedItems: {SUPPLY: 1}}]\nfacilities: [{type: FALLBACK_OVERFLOW_WORKSHOP, mapName: LOGISTICS, workshops: 2}]\n");
        var initial = HuntingSnapshot(content) with { World = new(), Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Engineers = 2,
            Facilities = [.. initial.Bases[0].Facilities, new("FALLBACK_OVERFLOW_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("FALLBACK_OVERFLOW", 0, int.MaxValue - 1, 1, true, false, true, new Dictionary<string, int>())]
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "fallback-overflow-before.sav");
        AssertTimeBlocked(campaign, "Production progress exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionStaffReleaseWaitsForTheWholeNativeBasePass(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "productionStaffReleaseRows"))
        {
            var fails = row[0].GetBoolean();
            var content = Load(cache, $"crafts: [{{type: RELEASE_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: RELEASE_FIRST, time: 1, cost: {(fails ? 1000 : 0)}, producedItems: {{SUPPLY: 0}}}}, {{name: RELEASE_FALLBACK, time: 1, producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: RELEASE_WORKSHOP, mapName: LOGISTICS, workshops: 3}}]\n");
            var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "RELEASE_HUNT",
                Logistics = craft.Logistics! with
                { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 }
            }) with
            { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [100] };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Engineers = row[1].GetInt32(), Items = new Dictionary<string, int>(),
                Facilities = [.. initial.Bases[0].Facilities, new("RELEASE_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                Productions = [new("RELEASE_FIRST", 2, 0, fails ? 3 : 1, false, false, false, new Dictionary<string, int>()),
                    new("RELEASE_FALLBACK", 0, 0, 4, false, false, true, new Dictionary<string, int>())]
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "release-before.sav");
            Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture(); var project = Assert.Single(after.Bases[0].Productions);
            var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "RELEASE_HUNT").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { project.Spent, project.Assigned, after.Bases[0].Engineers,
                after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(2).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Reload(content, campaign, 81, "release-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackUsesWorkshopConstructionCompletedEarlierInTheDailyTick(bool cache)
    {
        var content = Load(cache, "crafts: [{type: BUILD_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}]\nmanufacture: [{name: BUILD_PRODUCT, time: 2, space: 2, producedItems: {SUPPLY: 1}}]\nfacilities: [{type: BUILD_WORKSHOP, mapName: LOGISTICS, workshops: 4}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "BUILD_HUNT",
            Logistics = craft.Logistics! with
            { Status = "STR_REFUELLING", Fuel = 60, Weapons = [], IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3 }
        }) with
        { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Engineers = 4, Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("BUILD_WORKSHOP", 1, 1, 1, 0, false, false, false)],
            Productions = [new("BUILD_PRODUCT", 0, 0, 4, false, false, true, new Dictionary<string, int>())]
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "build-before.sav");
        Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture(); var project = Assert.Single(after.Bases[0].Productions);
        Assert.Equal(2, project.Assigned); Assert.Equal(2, project.Spent); Assert.Equal(2, after.Bases[0].Engineers);
        Assert.Equal("STR_OUT", after.Bases[0].Crafts.Single(craft => craft.RuleId == "BUILD_HUNT").Logistics!.Status);
        Assert.True(after.World.Ufos[0].Hunting);
        Reload(content, campaign, 81, "build-after.sav");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FallbackProductionStaffingMatchesNativeBeforeHunting(bool cache, bool arrivingEngineers)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionFallbackRows").GroupBy(row => row[2].GetInt32()))
        {
            foreach (var row in group)
            {
                var capacity = row[1].GetInt32() + row[4].GetInt32() + (row[3].GetInt32() != 0 || row[4].GetInt32() != 0 ? group.Key : 0);
                var rowContent = Load(cache, $"crafts: [{{type: MONEY_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: MONEY_PRODUCT, time: 2, space: {group.Key}, producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: MONEY_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: {capacity}}}]\n");
                var initial = HuntingSnapshot(rowContent).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "MONEY_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    }
                })
                    with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [12] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Engineers = arrivingEngineers ? 0 : row[0].GetInt32(), Items = new Dictionary<string, int>(),
                    Transfers = arrivingEngineers && row[0].GetInt32() > 0 ? [new(17, 1, CampaignTransferKind.Engineer, "", row[0].GetInt32())] : [],
                    Facilities = [.. initial.Bases[0].Facilities, new("MONEY_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("MONEY_PRODUCT", row[4].GetInt32(), row[3].GetInt32(), 4, false, false, true, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(rowContent, CampaignState.Restore(initial, rowContent, new SplitMix64RandomSource(81)), 81, "money-product-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = result.Events.OfType<CampaignProductionProgress>().SingleOrDefault();
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "MONEY_PRODUCT_HUNT").Logistics!;
                var project = Assert.Single(after.Bases[0].Productions);
                Assert.Equal(row[5].GetInt32() - row[4].GetInt32(), ProductionProgress.FallbackAdditional(row[3].GetInt32(), row[4].GetInt32(), group.Key, row[0].GetInt32(), row[1].GetInt32()));
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { project.Assigned, after.Bases[0].Engineers,
                    project.Spent, progress?.Produced ?? 0, 0, after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(rowContent, campaign, 81, "money-product-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionMaterialContinuationMatchesNativeOutputThenInputOrder(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionMaterialRows").GroupBy(row => (Input: row[0].GetInt32(), Output: row[1].GetInt32(), Delay: row[3].GetInt32())))
        {
            var content = Load(cache, $"crafts: [{{type: MONEY_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: MONEY_PRODUCT, time: 2, cost: 3, requiredItems: {{SUPPLY: {group.Key.Input}}}, producedItems: {{SUPPLY: {group.Key.Output}}}, transferTimes: [{group.Key.Delay}]}}]\nfacilities: [{{type: MONEY_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: 6}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "MONEY_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    }
                })
                    with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [12] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Engineers = 0, Items = row[2].GetInt32() == 0 ? new Dictionary<string, int>() : new Dictionary<string, int> { ["SUPPLY"] = row[2].GetInt32() },
                    Facilities = [.. initial.Bases[0].Facilities, new("MONEY_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("MONEY_PRODUCT", row[4].GetInt32() * 2, 1, 4, false, false, false, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "money-product-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "MONEY_PRODUCT_HUNT").Logistics!;
                var stop = progress.StopReason == "STR_PRODUCTION_COMPLETE" ? 1 : progress.StopReason == "STR_NOT_ENOUGH_MONEY" ? 2 : progress.StopReason == "STR_NOT_ENOUGH_MATERIALS" ? 3 : 0;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { progress.Produced, after.Funds[^1], stop,
                    after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0, after.Bases[0].Transfers.Count });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(content, campaign, 81, "money-product-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionCostContinuationMatchesNativePaidUnitAndStopOrder(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var group in TestFixtures.Rows(oracle.RootElement, "productionMoneyRows").GroupBy(row => row[0].GetInt32()))
        {
            var content = Load(cache, $"crafts: [{{type: MONEY_PRODUCT_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}}]\nmanufacture: [{{name: MONEY_PRODUCT, time: 2, cost: {group.Key}, producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: MONEY_PRODUCT_WORKSHOP, mapName: LOGISTICS, workshops: 6}}]\n");
            foreach (var row in group)
            {
                var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "MONEY_PRODUCT_HUNT",
                    Logistics = craft.Logistics! with
                    {
                        Status = "STR_REFUELLING",
                        Fuel = 60,
                        Weapons = [],
                        IsAutoPatrolling = true,
                        AutoPatrolLongitude = 0.8,
                        AutoPatrolLatitude = 0.3
                    }
                })
                    with
                { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [row[1].GetInt32()] };
                initial = initial with
                {
                    Bases = [initial.Bases[0] with
                {
                    Longitude = 0.45, Latitude = 0.21, Engineers = 0, Items = new Dictionary<string, int>(),
                    Facilities = [.. initial.Bases[0].Facilities, new("MONEY_PRODUCT_WORKSHOP", 1, 1, 0, 0, false, false, false)],
                    Productions = [new("MONEY_PRODUCT", row[2].GetInt32() * 2, 1, row[3].GetInt32(), row[4].GetBoolean(), false, false, new Dictionary<string, int>())],
                }]
                };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "money-product-before.sav");
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var progress = Assert.Single(result.Events.OfType<CampaignProductionProgress>());
                var after = campaign.Capture();
                var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "MONEY_PRODUCT_HUNT").Logistics!;
                var stop = progress.StopReason == "STR_PRODUCTION_COMPLETE" ? 1 : progress.StopReason == "STR_NOT_ENOUGH_MONEY" ? 2 : 0;
                var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { progress.Produced, after.Funds[^1], stop,
                    after.Bases[0].Items.GetValueOrDefault("SUPPLY"), craft.Status, craft.Fuel,
                    after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0 });
                var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(5).ToArray());
                Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
                Reload(content, campaign, 81, "money-product-after.sav");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FundedProductionRequiresUnknownEarlierSaleAccountingBeforeHunting(bool cache)
    {
        var content = Load(cache, "items: [{type: SUPPLY, costSell: 10}]\ncrafts: [{type: SHARED_MONEY_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, refuelItem: SUPPLY}]\nmanufacture: [{name: EARLIER_SALE, time: 1, producedItems: {SUPPLY: 1}}, {name: LATER_MONEY_PRODUCT, time: 1, cost: 3, producedItems: {SUPPLY: 1}}]\nfacilities: [{type: SHARED_MONEY_WORKSHOP, mapName: LOGISTICS, workshops: 2}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        { RuleId = "SHARED_MONEY_HUNT", Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 60, Weapons = [] } })
            with
        { Time = new(1, 1, 1, 1999, 1, 59, 55), Funds = [0] };
        var owner = initial.Bases[0] with
        {
            Items = new Dictionary<string, int>(),
            Facilities = [.. initial.Bases[0].Facilities, new("SHARED_MONEY_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("LATER_MONEY_PRODUCT", 2, 0, 3, false, false, false, new Dictionary<string, int>())],
        };
        initial = initial with
        {
            Bases = [owner with { Crafts = [], Soldiers = [],
            Productions = [new("EARLIER_SALE", 1, 0, 1, false, true, false, new Dictionary<string, int>())] },
            owner with { Id = 1 }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO retargeting requires shared production accounting forecasts.");
    }

    [Fact]
    public void ProductionCompletionCountMatchesPinnedCaller()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "productionProgressRows"))
            Assert.Equal(row[4].GetInt32(), ProductionProgress.Requested(row[0].GetInt32(),
                row[1].GetInt32(), row[2].GetInt32(), 20, row[3].GetBoolean()));
    }

    [Theory]
    [InlineData(false, "fuel")]
    [InlineData(true, "fuel")]
    [InlineData(false, "craft")]
    [InlineData(true, "craft")]
    [InlineData(false, "ammunition")]
    [InlineData(true, "ammunition")]
    [InlineData(false, "overflow")]
    [InlineData(true, "overflow")]
    [InlineData(false, "fallback")]
    [InlineData(true, "fallback")]
    public void IncompleteProductionDoesNotChangeHunterTargetsOrStocks(bool cache, string source)
    {
        var clip = source == "ammunition" ? "clip: SUPPLY" : "clip: ''";
        var fuel = source == "ammunition" ? "" : ", refuelItem: SUPPLY";
        var required = source == "craft" ? "requiredItems: {UNFINISHED_HUNT: 1}, " : "";
        var content = Load(cache, $"crafts: [{{type: UNFINISHED_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, weapons: 1{fuel}}}]\ncraftWeapons: [{{type: UNFINISHED_GUN, ammoMax: 10, rearmRate: 4, launcher: SUPPLY, {clip}}}]\nmanufacture: [{{name: UNFINISHED_PRODUCT, time: 100, {required}producedItems: {{SUPPLY: 1}}}}]\nfacilities: [{{type: UNFINISHED_WORKSHOP, mapName: LOGISTICS, workshops: 1}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "UNFINISHED_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
                Weapons = source == "ammunition" ? [new("UNFINISHED_GUN", 0, Rearming: false)] : [],
            },
        }) with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21,
            Items = new Dictionary<string, int> { ["SUPPLY"] = 1 },
            Facilities = [.. initial.Bases[0].Facilities, new("UNFINISHED_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("UNFINISHED_PRODUCT", 1, source == "overflow" ? int.MaxValue : 0, 1, false, false, source == "fallback", new Dictionary<string, int>())],
        }]
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "unfinished-production-before.sav");
        if (source == "overflow")
        {
            AssertTimeBlocked(campaign, "Production progress exceeds the supported range.");
            return;
        }
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Equal(1, after.Bases[0].Productions.Single().Spent);
        Assert.Equal(source == "ammunition" ? 1 : 0, after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
        Assert.True(after.World.Ufos[0].Hunting);
        Assert.Equal("UNFINISHED_HUNT", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "unfinished-production-after.sav");
    }

    [Theory]
    [InlineData("production", "UFO retargeting requires production fuel-stock forecasts.")]
    [InlineData("craft", "UFO retargeting requires production craft-consumption forecasts.")]
    public void UnforecastHourlyStockInputsStopBeforeMutation(string source, string reason)
    {
        var overlay = "crafts: [{type: STOCK_GUARD_HUNT, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true, weapons: 1}]\n";
        overlay += source switch
        {
            "production" => "manufacture: [{name: HUNT_STOCK_PRODUCT, time: 1, producedItems: {SUPPLY: 1}, randomProducedItems: [[1, {SUPPLY: 1}], [1, {BULKY: 1}]]}]\n",
            _ => "manufacture: [{name: HUNT_STOCK_PRODUCT, time: 1, requiredItems: {STOCK_GUARD_HUNT: 1}, producedItems: {SUPPLY: 1}}]\n",
        };
        var content = Load(overlay: overlay);
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "STOCK_GUARD_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
                Weapons = [],
            },
        }) with
        { Time = new(1, 1, 1, 1999, 1, 59, 55) };
        var owner = initial.Bases[0];
        if (source is "production" or "craft") owner = owner with
        {
            Facilities = [.. owner.Facilities, new("FUEL_WORKSHOP", 1, 1, 0, 0, false, false, false)],
            Productions = [new("HUNT_STOCK_PRODUCT", 1, 0, source == "craft" ? 2 : 1, false, false, false, new Dictionary<string, int>())],
        };
        initial = initial with { Bases = [owner] };
        if (source == "craft")
        {
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
            Assert.Empty(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>());
            Assert.DoesNotContain(campaign.Capture().Bases[0].Crafts, craft => craft.RuleId == "STOCK_GUARD_HUNT");
            Assert.False(campaign.Capture().World.Ufos[0].Hunting);
            Reload(content, campaign, 81, "consumption-forecast-after.sav");
            return;
        }
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), reason);
    }

    [Fact]
    public void RelaunchingLeaderSpeedFeedsEscortFuelBeforeHunting()
    {
        var content = Load(overlay: "crafts: [{type: RELAUNCH_LEADER, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 29, 55) };
        var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var leader = template with
        {
            RuleId = "RELAUNCH_LEADER",
            Id = 2,
            Logistics = template.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
                Weapons = [],
            },
        };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Fuel = 50,
                Speed = 6000,
                Weapons = [],
                Destination = new(WorldTargetKind.Craft, "RELAUNCH_LEADER", 2, 0.45, 0.21),
            },
        });
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Crafts = [.. initial.Bases[0].Crafts, leader],
        }]
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var after = campaign.Capture();
        var escort = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal(20, escort.Fuel);
        Assert.False(escort.LowFuel);
        Assert.True(after.World.Ufos[0].Hunting);
        Assert.Equal("SHIP", after.World.Ufos[0].Destination!.TypeId);
        Reload(content, campaign, 81, "relaunched-leader-escort.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlierLeaderFuelReturnChangesLaterEscortConsumption(bool leaderFirst)
    {
        var content = Load(overlay: "crafts: [{type: FUEL_RETURN_LEADER, speedMax: 2000, damageMax: 100, fuelMax: 100}]\n");
        var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, 9, 55) };
        var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
        var leader = template with
        {
            RuleId = "FUEL_RETURN_LEADER",
            Id = 2,
            Logistics = template.Logistics! with { Fuel = 0, Speed = 0, Weapons = [] },
        };
        var escort = template with
        {
            Logistics = template.Logistics! with
            {
                Fuel = 50,
                Speed = 6000,
                Weapons = [],
                Destination = new(WorldTargetKind.Craft, "FUEL_RETURN_LEADER", 2, 0.45, 0.21),
            }
        };
        initial = initial with
        {
            Bases = [initial.Bases[0] with
        {
            Longitude = 0.45, Latitude = 0.21, Soldiers = [],
            Crafts = leaderFirst ? [leader, escort] : [escort, leader],
        }]
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var after = campaign.Capture();
        var state = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Equal(leaderFirst ? 20 : 0, state.Fuel);
        // At the base, the return arrival clears low-fuel after the hunt caller.
        Assert.Equal(leaderFirst, after.World.Ufos[0].Hunting);
        Reload(content, campaign, 81, "ordered-escort-fuel.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposedEscortFuelAndHuntingMatchNativeOrder(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, "crafts:\n  - {type: HUNT_FUEL_ESCORT, speedMax: 6000, damageMax: 100, fuelMax: 100, allowLanding: false}\n  - {type: HUNT_FUEL_LEADER, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true, allowLanding: false}\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "escortFuelRows"))
        {
            var relaunch = row[0].GetBoolean();
            var initial = HuntingSnapshot(content) with { Time = new(1, 1, 1, 1999, 1, relaunch ? 29 : 9, 55) };
            var template = initial.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP");
            var leader = template with
            {
                RuleId = "HUNT_FUEL_LEADER",
                Id = 2,
                Logistics = template.Logistics! with
                {
                    Status = relaunch ? "STR_REFUELLING" : "STR_OUT",
                    Fuel = relaunch ? 60 : 0,
                    IsAutoPatrolling = relaunch,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = [],
                },
            };
            var escort = template with
            {
                RuleId = "HUNT_FUEL_ESCORT",
                Logistics = template.Logistics! with
                {
                    Fuel = 50,
                    Speed = 6000,
                    Weapons = [],
                    Destination = new(WorldTargetKind.Craft, "HUNT_FUEL_LEADER", 2, 0.45, 0.21),
                },
            };
            initial = initial with
            {
                Bases = [initial.Bases[0] with
            {
                Longitude = 0.45, Latitude = 0.21, Soldiers = [],
                Crafts = row[1].GetBoolean() ? [leader, escort] : [escort, leader],
            }]
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "ordered-fuel-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var state = after.Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_FUEL_ESCORT").Logistics!;
            var leaderState = after.Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_FUEL_LEADER").Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                state.Fuel, leaderState.Fuel,
                after.World.Ufos[0].Hunting ? after.World.Ufos[0].Destination!.Id : 0,
            });
            var expected = JsonSerializer.SerializeToUtf8Bytes(new object[]
            { row[2].GetInt32(), row[4].GetInt32(), row[5].GetInt32() });
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Assert.Equal(row[3].GetBoolean(), result.Events.OfType<CraftLowFuel>().Any(message => message.CraftTypeId == "HUNT_FUEL_ESCORT"));
            Reload(content, campaign, 81, "ordered-fuel-after.sav");
        }
    }

    [Theory]
    [InlineData(-1, 40)]
    [InlineData(100, int.MaxValue)]
    public void MalformedRefuellingForecastIsRefusedBeforeMutation(int maximum, int rate)
    {
        var content = Load(overlay: $"crafts: [{{type: INVALID_REFUEL_HUNT, speedMax: 2000, damageMax: 100, fuelMax: {maximum}, refuelRate: {rate}, autoPatrol: true}}]\n");
        var initial = HuntingSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            RuleId = "INVALID_REFUEL_HUNT",
            Logistics = craft.Logistics! with
            {
                Status = "STR_REFUELLING",
                Fuel = 60,
                IsAutoPatrolling = true,
                AutoPatrolLongitude = 0.8,
                AutoPatrolLatitude = 0.3,
                Weapons = [],
            },
        }) with
        { Time = new(1, 1, 1, 1999, 1, 29, 55) };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Craft service capacity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveHuntersSelectRevealMoveAndReload(bool cache)
    {
        var content = Load(cache);
        var initial = HuntingSnapshot(content);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(10));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var alert = Assert.Single(result.Events.OfType<UfoStartedHunting>());
        Assert.Equal(9, alert.UniqueId);
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        Assert.True(ufo.Hunting);
        Assert.True(ufo.Detected);
        Assert.False(ufo.Escorting);
        Assert.Equal(new WorldPosition(1, 0.6), ufo.OriginalWaypoint);
        Assert.Equal(WorldTargetKind.Craft, ufo.Destination!.Kind);
        Assert.Equal(3300, ufo.Speed);
        Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, new(0.45, 0.21), WorldGeometry.RadianSpeed(3300)), ufo.Position);
        var restored = Reload(content, campaign, 81, "live-hunter.sav");
        AdvanceOne(restored);
        Assert.True(Assert.Single(restored.Capture().World.Ufos).Hunting);
    }

    [Theory]
    [InlineData(CampaignTimeSpeed.FiveSeconds, true, false, true)]
    [InlineData(CampaignTimeSpeed.OneMinute, true, false, true)]
    [InlineData(CampaignTimeSpeed.FiveMinutes, true, false, false)]
    [InlineData(CampaignTimeSpeed.FiveSeconds, false, false, false)]
    [InlineData(CampaignTimeSpeed.OneDay, false, true, true)]
    public void LiveRetargetingUsesSelectedSpeedAndTenMinuteCaller(CampaignTimeSpeed speed, bool fast, bool tenMinutes, bool hunts)
    {
        var content = Load(overlay: fast ? null : "hunterKillerFastRetarget: false\n");
        var initial = HuntingSnapshot(content);
        if (tenMinutes) initial = initial with { Time = new(1, 1, 1, 1999, 1, 9, 55) };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(hunts ? 10 : 3) { SelectedSpeed = speed });
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Equal(hunts ? 1 : 3, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal(hunts, Assert.Single(campaign.Capture().World.Ufos).Hunting);
        Assert.Equal(hunts ? 1 : 0, result.Events.OfType<UfoStartedHunting>().Count());
        Reload(content, campaign, 81, "speed-selected-hunter.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscortsFollowUfoListOrderAndRestoreAfterDeletion(bool targetFirst)
    {
        var content = Load();
        var initial = HuntingSnapshot(content, escort: true);
        var hunter = initial.World.Ufos[0];
        var target = hunter with
        { UniqueId = 10, Id = 4, RuleId = "UFO_SCOUT", HunterKiller = false, Escort = false, Longitude = 0.5, Latitude = 0.25 };
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 2 }],
                Ufos = targetFirst ? [target, hunter] : [hunter, target],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var after = campaign.Capture();
        var escorted = after.World.Ufos.Single(ufo => ufo.UniqueId == 9);
        var targetPosition = targetFirst ? after.World.Ufos.Single(ufo => ufo.UniqueId == 10).Position : target.Position;
        Assert.True(escorted.Escorting);
        Assert.False(escorted.Hunting);
        Assert.Equal(10, escorted.Destination!.UniqueId);
        Assert.Equal(WorldGeometry.Move(hunter.Position, targetPosition, WorldGeometry.RadianSpeed(2200)), escorted.Position);
        var restored = Reload(content, campaign, 81, "live-escort.sav");
        var deleted = restored.Capture();
        deleted = deleted with
        {
            World = deleted.World with
            {
                Ufos =
            deleted.World.Ufos.Select(ufo => ufo.UniqueId == 10 ? ufo with { Status = UfoStatus.Destroyed } : ufo).ToArray()
            }
        };
        var cleanup = CampaignState.Restore(deleted, content, new SplitMix64RandomSource(81));
        AdvanceOne(cleanup);
        var survivor = Assert.Single(cleanup.Capture().World.Ufos);
        Assert.False(survivor.Escorting);
        Assert.Equal(new WorldPosition(1, 0.6), survivor.Destination!.Position);
        Assert.Null(survivor.OriginalWaypoint);
        Reload(content, cleanup, 81, "deleted-escort-target.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HunterInterceptionAndMarkerExhaustionAreAtomic(bool marker)
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with { Id = marker ? 0 : 3, Longitude = marker ? 0.4 : 0.45, Latitude = marker ? 0.2 : 0.21 }]
            }
        };
        if (marker) initial = initial.WithNextId("STR_UFO", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), marker
            ? "UFO contact marker IDs are exhausted or collide with a saved UFO."
            : "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RedundantHunterAlertsFollowTheOption(bool suppress)
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial with { Options = initial.Options with { SuppressRedundantHunterAlert = suppress } };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, 0.4, 0.2) { UniqueId = 9 }
            }
        });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Equal(suppress ? 0 : 1, result.Events.OfType<UfoStartedHunting>().Count());
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Hunting);
        Reload(content, campaign, 81, "redundant-hunt-alert.sav");
    }

    [Fact]
    public void EscortCyclesStopBeforeRecursiveMovementForecasts()
    {
        var content = Load();
        var initial = HuntingSnapshot(content, escort: true);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with
                {
                    Escorting = true,
                    OriginalWaypoint = new(1, 0.6),
                    Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, 0.4, 0.2) { UniqueId = 9 },
                }],
            }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO escort chains require additional movement forecasts.");
    }

    [Fact]
    public void IgnoredOriginalCraftRestoresThePreparedWaypoint()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial.WithCraft("SHIP", craft => craft with { Logistics = craft.Logistics! with { LowFuel = true } });
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with
                {
                    Hunting = true,
                    OriginalWaypoint = new(1, 0.6),
                    Destination = new(WorldTargetKind.Craft, "SHIP", 1, 0.45, 0.21),
                }],
            }
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.False(ufo.Hunting);
        Assert.Null(ufo.OriginalWaypoint);
        Assert.Equal(new WorldPosition(1, 0.6), ufo.Destination!.Position);
        Reload(content, campaign, 81, "ignored-hunt-target.sav");
    }

    [Fact]
    public void DestroyedFollowerCleanupIsBoundedBeforeItsNestedScan()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 1001 }],
                Ufos = Enumerable.Range(0, 1001).Select(index => initial.World.Ufos[0] with
                { UniqueId = 9 + index, Id = 3 + index, HunterKiller = false, Status = UfoStatus.Destroyed }).ToArray(),
            }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Destroyed UFO follower cleanup exceeds the supported work limit.");
    }

    [Fact]
    public void HunterRevelationCannotBypassAnImportedCraftInterceptionGuard()
    {
        var content = Load();
        var initial = HuntingSnapshot(content);
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Longitude = 0.403,
                Latitude = 0.2,
                Speed = 6000,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 3, 0.4, 0.2) { UniqueId = 9 },
            }
        });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Craft interception requires strategic combat (Phase 6 branch 5).");
    }

    [Theory]
    [InlineData(2147000, 2000000000, 0, 100)]
    [InlineData(0, 2000, 21474837, 2147483647)]
    public void UndefinedIntermediateAttractionOverflowIsRejected(int units, int speed, int damage, int damageMaximum) =>
        Assert.False(WorldHunting.TryAttraction(0, true, units, speed, damage, damageMaximum, out _));

}
