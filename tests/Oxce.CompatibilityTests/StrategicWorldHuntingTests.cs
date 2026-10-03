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
