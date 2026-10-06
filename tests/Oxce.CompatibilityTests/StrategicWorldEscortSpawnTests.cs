using System.Text;
using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldEscortSpawnTests
{
    [Theory]
    [InlineData(false, "patrol")]
    [InlineData(true, "patrol")]
    [InlineData(false, "direct")]
    [InlineData(true, "direct")]
    [InlineData(false, "chain")]
    [InlineData(true, "chain")]
    [InlineData(false, "cycle")]
    [InlineData(true, "cycle")]
    [InlineData(false, "direct-far")]
    [InlineData(true, "direct-far")]
    [InlineData(false, "chain-far")]
    [InlineData(true, "chain-far")]
    [InlineData(false, "direct-stationary")]
    [InlineData(true, "direct-stationary")]
    [InlineData(false, "chain-stationary")]
    [InlineData(true, "chain-stationary")]
    public void NewLeaderAttachmentChecksActualCraftPursuitDependencies(bool cache, string kind)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "spawnEscortSequenceRows"), row =>
            !row[0].GetBoolean() && row[1].GetBoolean() && row[2].GetBoolean());
        var content = Load(cache, """
            crafts:
              - type: SHIP
                radarRange: 20000
                radarChance: 100
            """);
        var initial = IdleEscortSnapshot(content);
        var source = initial.World.Ufos[0] with { Detected = true };
        var follower = source with
        {
            UniqueId = 10,
            Id = 10,
            Longitude = 0.7,
            Latitude = 0.3,
            Escorting = true,
            Speed = 2200,
            OriginalWaypoint = new(1, 0.6),
            Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, source.Id,
                source.Longitude, source.Latitude)
            { UniqueId = source.UniqueId },
        };
        if (kind == "cycle") follower = follower with { Destination = follower.Destination! with { UniqueId = 11, Id = 11 } };
        var hasFollower = kind.StartsWith("chain", StringComparison.Ordinal) || kind == "cycle";
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = hasFollower ? [source, follower] : [source],
                Missions = [initial.World.Missions[0] with { LiveUfos = hasFollower ? 2 : 1 }],
            }
        };
        if (kind == "cycle") initial = initial with
        {
            World = initial.World with
            {
                Ufos = [.. initial.World.Ufos, follower with { UniqueId = 11, Id = 11,
                Destination = follower.Destination! with { UniqueId = 10, Id = 10 } }],
                Missions = [initial.World.Missions[0] with { LiveUfos = 3 }],
            }
        };
        var target = kind.StartsWith("direct", StringComparison.Ordinal) ? source : follower;
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 1000,
                Damage = 0,
                Speed = kind is "direct" or "chain" || kind.EndsWith("-far", StringComparison.Ordinal) ? 6000 : 0,
                Takeoff = 0,
                Longitude = kind is "direct" or "chain" || kind.EndsWith("-stationary", StringComparison.Ordinal) ? target.Longitude : 1,
                Latitude = kind is "direct" or "chain" || kind.EndsWith("-stationary", StringComparison.Ordinal) ? target.Latitude : 0.6,
                Destination = kind == "patrol" ? null : new(WorldTargetKind.Ufo, WorldTargetReference.UfoType,
                target.Id, target.Longitude, target.Latitude)
                { UniqueId = target.UniqueId },
            }
        });
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "leader-dependency-before.sav");
        if (kind is "direct" or "chain")
        {
            AssertTimeBlocked(campaign, "Craft interception requires strategic combat (Phase 6 branch 5).");
            return;
        }
        AdvanceUnblocked(campaign, 1);
        var changed = campaign.Capture().World.Ufos.Single(ufo => ufo.UniqueId == source.UniqueId);
        Assert.True(changed.Escorting);
        var leader = Assert.Single(campaign.Capture().World.Ufos, ufo => !ufo.HunterKiller);
        Assert.Equal(leader.UniqueId, changed.Destination!.UniqueId);
        Assert.Equal(WorldGeometry.Move(source.Position, new(Degrees(10), Degrees(8)), WorldGeometry.RadianSpeed(2200)), changed.Position);
        if (kind.EndsWith("-far", StringComparison.Ordinal))
        {
            var pursuing = campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
            Assert.Equal(WorldTargetKind.Ufo, pursuing.Destination!.Kind);
            Assert.Equal(target.UniqueId, pursuing.Destination.UniqueId);
            var movedTarget = campaign.Capture().World.Ufos.Single(ufo => ufo.UniqueId == target.UniqueId);
            var expected = WorldGeometry.Move(new(1, 0.6), movedTarget.Position, WorldGeometry.RadianSpeed(6000));
            Assert.Equal(expected.Longitude, pursuing.Longitude);
            Assert.Equal(expected.Latitude, pursuing.Latitude);
            Assert.Equal(movedTarget.Position, pursuing.Destination.Position);
        }
        if (kind.EndsWith("-stationary", StringComparison.Ordinal))
        {
            var pursuing = campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
            Assert.Equal(target.UniqueId, pursuing.Destination!.UniqueId);
            Assert.Equal(target.Position, new(pursuing.Longitude, pursuing.Latitude));
            Assert.Equal(campaign.Capture().World.Ufos.Single(ufo => ufo.UniqueId == target.UniqueId).Position,
                pursuing.Destination.Position);
        }
        campaign = Reload(content, campaign, 81, "leader-dependency-after.sav");
        AdvanceUnblocked(campaign, 1);
        Reload(content, campaign, 81, "leader-dependency-following.sav");
    }

    [Fact]
    public void ImportedLeaderFlagsDoNotChangeNativeSpawnEscortSelection()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "spawnEscortChainRows"))
        {
            var flags = row[0].GetInt32();
            var source = new UfoSnapshot(10, "UFO", 4, "TRAJ", 0, 0.3, 0.1, (UfoStatus)row[1].GetInt32(), WorldAltitudes.High)
            { HunterKiller = true, Speed = 550, Destination = WorldTargetReference.ForWaypoint(0, new(0.4, 0.2)) };
            var leader = source with
            {
                UniqueId = 9,
                HunterKiller = false,
                Longitude = 0.7,
                Latitude = 0.3,
                Hunting = flags == 1,
                Escorting = flags >= 2,
            };
            var result = WorldHunting.InitializeEscort(source, true, 2200, [leader]);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object?[]
            {
                flags, row[1].GetInt32(), result.Escorting, result.Destination!.UniqueId,
                result.Longitude, result.Latitude, result.Speed, result.OriginalWaypoint?.Longitude, result.OriginalWaypoint?.Latitude,
            });
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NewEscortsAttachToImportedHuntingAndEscortChains(bool cache, bool grounded)
    {
        var content = Load(cache);
        foreach (var flags in new[] { 0, 1, 2, 3 })
        {
            var initial = SpawnSnapshot(content, UfoStatus.Flying);
            var leader = initial.World.Ufos[0] with
            {
                Hunting = flags == 1,
                Escorting = flags >= 2,
                OriginalWaypoint = flags != 0 ? new(1, 0.6) : null,
                Destination = flags == 1 ? new(WorldTargetKind.Craft, "SHIP", 1, 0.45, 0.21)
                    : flags >= 2 ? new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, flags == 3 ? 3 : 4, 0.6, 0.3)
                    { UniqueId = flags == 3 ? 9 : 10 } : initial.World.Ufos[0].Destination,
            };
            var other = initial.World.Ufos[0] with { UniqueId = 10, Id = 4, Longitude = 0.6, Latitude = 0.3 };
            initial = initial with
            {
                World = initial.World with
                {
                    Ufos = [leader, other],
                    Missions = [initial.World.Missions[0] with
                { LiveUfos = 2, RuleId = grounded ? "MISSION_ESCORT_GROUND" : initial.World.Missions[0].RuleId }],
                }
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "new-chain-before.sav");
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var born = Assert.Single(after.World.Ufos, ufo => ufo.UniqueId is not (9 or 10));
            Assert.True(born.Escorting);
            Assert.Equal(9, born.Destination!.UniqueId);
            Assert.NotNull(born.OriginalWaypoint);
            Assert.Equal(2200, born.Speed);
            Assert.Equal(grounded ? UfoStatus.Landed : UfoStatus.Flying, born.Status);
            if (!grounded) Assert.Equal(after.World.Ufos[0].Position, born.Position);
            campaign = Reload(content, campaign, 81, "new-chain-after.sav");
            AdvanceOne(campaign);
            Assert.Equal(9, campaign.Capture().World.Ufos.Single(ufo => ufo.UniqueId == born.UniqueId).Destination!.UniqueId);
            Reload(content, campaign, 81, "new-chain-following-tick.sav");
        }
    }

    [Fact]
    public void EscortInitializationMatchesPinnedSavedOrderOriginBackupAndSpeed()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "spawnEscortRows"))
        {
            var hunter = row[0].GetBoolean();
            var requested = row[1].GetBoolean();
            var variant = row[2].GetInt32();
            var status = row[3].GetInt32();
            var source = new UfoSnapshot(9, "UFO", 4, "TRAJ", 0, 0.3, 0.1, (UfoStatus)row[12].GetInt32(), WorldAltitudes.Low)
            {
                HunterKiller = hunter,
                Speed = 900,
                Destination = WorldTargetReference.ForWaypoint(0, new(0.4, 0.2)),
            };
            var first = source with
            {
                UniqueId = 10,
                Longitude = 0.7,
                Latitude = 0.3,
                HunterKiller = variant == 2,
                MissionId = variant == 1 ? 5 : 4,
                Status = (UfoStatus)status,
            };
            var second = source with { UniqueId = 11, Longitude = 0.9, Latitude = 0.4, HunterKiller = false };
            UfoSnapshot[] targets = variant == 0 ? [] : [first, second];
            var result = WorldHunting.InitializeEscort(source, requested, 2200, targets);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object?[]
            {
                hunter, requested, variant, status, result.Escort, result.Escorting,
                result.Escorting ? result.Destination!.UniqueId : 0,
                result.Longitude, result.Latitude, result.Speed,
                result.OriginalWaypoint?.Longitude, result.OriginalWaypoint?.Latitude, (int)result.Status,
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())),
                CanonicalJson.Normalize(actual));
        }
    }

    [Theory]
    [InlineData(false, UfoStatus.Flying)]
    [InlineData(true, UfoStatus.Flying)]
    [InlineData(false, UfoStatus.Landed)]
    [InlineData(true, UfoStatus.IgnoreMe)]
    [InlineData(false, UfoStatus.Crashed)]
    [InlineData(true, UfoStatus.Destroyed)]
    public void WaveEscortAttachesInSavedOrderAndRepairsDeletedLeader(bool cache, UfoStatus status)
    {
        var content = Load(cache);
        var initial = SpawnSnapshot(content, status);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var world = campaign.Capture().World;
        var escort = world.Ufos.Single(ufo => ufo.UniqueId != 9);
        Assert.True(escort.HunterKiller);
        Assert.True(escort.Escort);
        Assert.Equal(2200, escort.Speed);
        if (status is UfoStatus.Crashed or UfoStatus.Destroyed)
        {
            Assert.Single(world.Ufos);
            Assert.False(escort.Escorting);
            Assert.Null(escort.OriginalWaypoint);
            Assert.Equal(WorldTargetKind.Waypoint, escort.Destination!.Kind);
            Assert.Equal(1, world.Missions[0].LiveUfos);
        }
        else
        {
            Assert.True(escort.Escorting);
            Assert.Equal(9, escort.Destination!.UniqueId);
            Assert.NotNull(escort.OriginalWaypoint);
            Assert.Equal(world.Ufos[0].Position, escort.Position);
            Assert.Equal(2, world.Missions[0].LiveUfos);
        }
        campaign = Reload(content, campaign, 81, "new-escort-spawn.sav");
        AdvanceOne(campaign);
        Reload(content, campaign, 81, "new-escort-next-tick.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WaveEscortWithoutLeaderKeepsFlagAndTrajectorySpeed(bool cache)
    {
        var content = Load(cache);
        var initial = SpawnSnapshot(content, UfoStatus.Flying);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [],
                Missions = [initial.World.Missions[0] with { LiveUfos = 0 }],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var escort = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(escort.HunterKiller);
        Assert.True(escort.Escort);
        Assert.False(escort.Escorting);
        Assert.Null(escort.OriginalWaypoint);
        Assert.Equal(550, escort.Speed);
        Assert.Equal(WorldTargetKind.Waypoint, escort.Destination!.Kind);
        Reload(content, campaign, 81, "new-escort-no-leader.sav");
    }

    [Fact]
    public void EscortSpeedMaximumIsValidatedEvenWhenTrajectoryRoundsItToZero()
    {
        var content = Load(overlay: """
            ufos:
              - type: NEGATIVE_ESCORT
                speedMax: -1
            alienMissions:
              - type: NEGATIVE_ESCORT_MISSION
                objective: 0
                waves:
                  - {ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 9000}
                  - {ufo: NEGATIVE_ESCORT, count: 1, trajectory: TRAJ_ESCORT_SPAWN, timer: 9000, hunterKillerPercentage: 100, escort: true}
            """);
        Assert.Equal(-1, content.RuntimeRules.Ufos[content.RuntimeRules.Ufos.GetRequired("NEGATIVE_ESCORT")].Value.StatsForRace("RACE_A").SpeedMaximum);
        var initial = SpawnSnapshot(content, UfoStatus.Flying);
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RuleId = "NEGATIVE_ESCORT_MISSION" }],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO escort speed exceeds the supported range.");
    }

    [Fact]
    public void EscortSpawnStopsBeforeCreationTimeHuntingCombat()
    {
        var content = Load(overlay: """
            ufos:
              - {type: POSITIVE_RADAR_ESCORT, speedMax: 2200, damageMax: 50, radarRange: 300}
            alienMissions:
              - type: POSITIVE_RADAR_ESCORT_MISSION
                objective: 0
                waves:
                  - {ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 9000}
                  - {ufo: POSITIVE_RADAR_ESCORT, count: 1, trajectory: TRAJ_ESCORT_SPAWN, timer: 9000, hunterKillerPercentage: 100, escort: true}
            """);
        Assert.Equal(300, content.RuntimeRules.Ufos[content.RuntimeRules.Ufos.GetRequired("POSITIVE_RADAR_ESCORT")].Value.StatsForRace("RACE_A").RadarRange);
        var initial = SpawnSnapshot(content, UfoStatus.Flying).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                LowFuel = false,
                MissionComplete = false,
                Fuel = 300,
            },
        });
        initial = initial with
        {
            World = initial.World with
            { Missions = [initial.World.Missions[0] with { RuleId = "POSITIVE_RADAR_ESCORT_MISSION" }] }
        };
        var leader = initial.World.Ufos[0].Position;
        initial = initial.WithCraft("SHIP", craft => craft with
        { Logistics = craft.Logistics! with { Longitude = leader.Longitude, Latitude = leader.Latitude } });
        initial = initial with { Bases = [initial.Bases[0] with { Longitude = leader.Longitude, Latitude = leader.Latitude }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
    }

    [Fact]
    public void ImmediateEscortOrderMatchesPinnedCreationAndRetargetFragments()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "spawnEscortSequenceRows"))
        {
            var ordinaryFirst = row[0].GetBoolean();
            var retarget = row[1].GetBoolean();
            var source = new UfoSnapshot(9, "UFO", 4, "TRAJ", 0, 0.3, 0.1, UfoStatus.Flying, WorldAltitudes.Low)
            {
                HunterKiller = true,
                Speed = 550,
                Destination = WorldTargetReference.ForWaypoint(0, new(0.4, 0.2)),
            };
            var leader = source with { UniqueId = 10, Longitude = 0.7, Latitude = 0.3, HunterKiller = false };
            var result = WorldHunting.InitializeEscort(source, true, 2200, ordinaryFirst ? [leader] : []);
            if (retarget && !result.Escorting)
            {
                var target = new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType,
                    leader.Id, leader.Longitude, leader.Latitude)
                {
                    UniqueId = leader.UniqueId,
                };
                result = WorldHunting.Escort(result, target, 2200);
            }
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object?[]
            {
                ordinaryFirst, retarget, result.Escorting, result.Escorting ? result.Destination!.UniqueId : 0,
                result.Longitude, result.Latitude, result.Speed,
                result.OriginalWaypoint?.Longitude, result.OriginalWaypoint?.Latitude,
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())),
                CanonicalJson.Normalize(actual));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ImmediateWavesKeepLeaderOrderOriginsAndUnmarkedIdentity(bool cache, bool earlyLeader)
    {
        var content = Load(cache);
        var initial = SpawnSnapshot(content, UfoStatus.Flying);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [],
                Missions = [initial.World.Missions[0] with
                {
                    RuleId = earlyLeader ? "MISSION_ESCORT_CHAIN_EARLY_LEADER" : "MISSION_ESCORT_CHAIN_LATE_LEADER",
                    NextWave = 0,
                    LiveUfos = 0,
                }],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var world = campaign.Capture().World;
        Assert.Equal(3, world.Ufos.Count);
        var leader = Assert.Single(world.Ufos, ufo => !ufo.HunterKiller);
        Assert.Equal(0, leader.Id);
        Assert.Equal(earlyLeader ? 0 : 2, world.Ufos.ToList().IndexOf(leader));
        foreach (var escort in world.Ufos.Where(ufo => ufo.HunterKiller))
        {
            Assert.True(escort.Escort);
            Assert.True(escort.Escorting);
            Assert.Equal(leader.UniqueId, escort.Destination!.UniqueId);
            Assert.Equal(0, escort.Destination.Id);
            Assert.Equal(2200, escort.Speed);
            Assert.NotNull(escort.OriginalWaypoint);
            if (earlyLeader) Assert.Equal(leader.Position, escort.Position);
            else Assert.True(WorldGeometry.Distance(leader.Position, escort.Position) > WorldGeometry.RadianSpeed(2200));
        }
        Assert.Equal(3, world.Missions[0].LiveUfos);
        Assert.Equal(2, world.Missions[0].NextWave);
        campaign = Reload(content, campaign, 81, "immediate-escort-chain.sav");
        AdvanceUnblocked(campaign, 120);
        Reload(content, campaign, 81, "immediate-escort-chain-later.sav");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LoadedIdleEscortAttachesToNewbornLeaderWithoutCreationTeleport(bool cache, bool earlyLeader)
    {
        var content = Load(cache);
        var initial = IdleEscortSnapshot(content, earlyLeader
            ? "MISSION_ESCORT_CHAIN_EARLY_LEADER" : "MISSION_ESCORT_CHAIN_LATE_LEADER");
        var source = initial.World.Ufos[0];
        var origin = WorldPosition.Create(10 * Math.PI / 180, 8 * Math.PI / 180);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var world = campaign.Capture().World;
        Assert.Equal(4, world.Ufos.Count);
        var leader = Assert.Single(world.Ufos, ufo => !ufo.HunterKiller);
        var escort = world.Ufos[0];
        Assert.Equal(9, escort.UniqueId);
        Assert.True(escort.Escorting);
        Assert.Equal(leader.UniqueId, escort.Destination!.UniqueId);
        Assert.Equal(0, escort.Destination.Id);
        Assert.Equal(source.Destination!.Position, escort.OriginalWaypoint);
        Assert.Equal(2200, escort.Speed);
        Assert.Equal(WorldGeometry.Move(source.Position, origin, WorldGeometry.RadianSpeed(2200)), escort.Position);
        Assert.True(WorldGeometry.Distance(leader.Position, escort.Position) > WorldGeometry.RadianSpeed(2200));
        Assert.Equal(4, world.Missions[0].LiveUfos);
        campaign = Reload(content, campaign, 81, "loaded-escort-newborn-leader.sav");
        AdvanceUnblocked(campaign, 120);
        Reload(content, campaign, 81, "loaded-escort-newborn-leader-later.sav");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void LoadedIdleEscortDistinguishesIndependentAndPursuingIgnoredOrHiddenCraft(bool hidden, bool pursues)
    {
        var content = Load(overlay: hidden ? "crafts:\n  - type: SHIP\n    undetectable: true\n" : null);
        var initial = IdleEscortSnapshot(content).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 0,
                Longitude = pursues ? 0.4 : 1,
                Latitude = pursues ? 0.2 : 0.6,
                LowFuel = !hidden,
                MissionComplete = !hidden,
                Destination = pursues ? new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 9, 0.4, 0.2) { UniqueId = 9 } : null,
            },
        });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var events = campaign.Execute(new AdvanceCampaignTime(1)).Events;
        Assert.Empty(events.OfType<CampaignActionBlocked>());
        Assert.Equal(1, Assert.Single(events.OfType<CampaignTimeAdvanced>()).Summary.TickCount);
        Assert.Empty(events.OfType<CraftTargetLost>());
        if (pursues) Assert.Equal(9, campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!.Destination!.UniqueId);
        Assert.True(campaign.Capture().World.Ufos[0].Escorting);
        campaign = Reload(content, campaign, 81, "independent-hidden-craft.sav");
        AdvanceUnblocked(campaign, 1);
        Reload(content, campaign, 81, "independent-hidden-craft-following.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewbornLeaderAttachmentPreservesNativeCraftContactLoss(bool cache)
    {
        var content = Load(cache, "crafts: [{type: SHIP, radarRange: 0, radarChance: 0}]\n");
        var initial = IdleEscortSnapshot(content);
        initial = initial with { World = initial.World with { Ufos = [initial.World.Ufos[0] with { Detected = true }] } };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 1000,
                Damage = 0,
                Takeoff = 0,
                Speed = 0,
                Longitude = 1,
                Latitude = 0.6,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 9, 0.4, 0.2) { UniqueId = 9 },
            }
        });
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "leader-loss-before.sav");
        var events = campaign.Execute(new AdvanceCampaignTime(1)).Events;
        Assert.Empty(events.OfType<CampaignActionBlocked>());
        var lost = Assert.Single(events.OfType<CraftTargetLost>());
        var source = campaign.Capture().World.Ufos.Single(ufo => ufo.UniqueId == 9);
        Assert.True(source.Escorting);
        Assert.False(source.Detected);
        Assert.Equal(source.Position, lost.Target.Position);
        Assert.Equal(9, lost.Target.UniqueId);
        Assert.False(lost.ReturningToBase);
        var patrol = campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
        Assert.Null(patrol.Destination);
        Assert.Equal(3000, patrol.Speed);
        campaign = Reload(content, campaign, 81, "leader-loss-after.sav");
        AdvanceUnblocked(campaign, 1);
        Reload(content, campaign, 81, "leader-loss-following.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosePursuitStillRequiresForecastForAProbabilisticLeader(bool cache)
    {
        var content = Load(cache, """
            alienMissions:
              - type: UNCERTAIN_CLOSE_LEADER
                objective: 0
                waves: [{ufo: ESCORT_HUNTER, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 9000, hunterKillerPercentage: 50}]
            """);
        var initial = IdleEscortSnapshot(content, "UNCERTAIN_CLOSE_LEADER").WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 1000,
                Damage = 0,
                Takeoff = 0,
                Speed = 6000,
                Longitude = 0.4,
                Latitude = 0.2,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 9, 0.4, 0.2) { UniqueId = 9 },
            },
        });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Loaded idle escorts require same-tick newborn craft pursuit forecasts.");
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public void BaseOriginLeaderAttachmentUsesNativeOverrideAndRaceSpeed(bool cache, int operation)
    {
        using var originOracle = TestFixtures.ReadVerifiedExpected("strategic-mission-origin");
        var originRow = TestFixtures.Rows(originOracle.RootElement, "rows").Single(row =>
            row[0].GetInt32() == 0 && row[1].GetInt32() == 1 && row[2].GetInt32() == 1);
        var origin = new WorldPosition(originRow[3].GetDouble(), originRow[4].GetDouble());
        var content = Load(cache, $$$"""
            crafts: [{type: SHIP, radarRange: 20000, radarChance: 100}]
            ufos:
              - type: ESCORT_HUNTER
                raceBonus: {RACE_B: {speedMax: 100}}
            alienMissions:
              - type: IDLE_BASE_LEADER
                objective: 0
                operationType: {{{operation}}}
                waves: [{ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 9000}]
            """);
        foreach (var race in new[] { "RACE_A", "RACE_B" })
        {
            var initial = IdleEscortSnapshot(content, "IDLE_BASE_LEADER");
            initial = initial with
            {
                World = initial.World with
                {
                    AlienBases = [new(2, "ALIEN_BASE_DEPLOY", race, origin.Longitude, origin.Latitude)],
                    Missions = [initial.World.Missions[0] with { Race = race,
                AlienBase = new(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 2, origin.Longitude, origin.Latitude) }],
                    Ufos = [initial.World.Ufos[0] with { Detected = true, TrajectoryPoint = 2,
                Destination = WorldTargetReference.ForWaypoint(0, new(0.400001, 0.2)) }],
                }
            };
            initial = initial.WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT",
                    Fuel = 1000,
                    Damage = 0,
                    Takeoff = 0,
                    Speed = 6000,
                    Longitude = 1,
                    Latitude = 0.6,
                    Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 9, 0.4, 0.2) { UniqueId = 9 },
                }
            });
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "base-leader-before.sav");
            AdvanceUnblocked(campaign, 1);
            var source = campaign.Capture().World.Ufos.Single(ufo => ufo.UniqueId == 9);
            var leader = Assert.Single(campaign.Capture().World.Ufos, ufo => !ufo.HunterKiller);
            Assert.Equal(leader.UniqueId, source.Destination!.UniqueId);
            var speed = race == "RACE_A" ? 2200 : 2300;
            Assert.Equal(speed, source.Speed);
            Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, origin, WorldGeometry.RadianSpeed(speed)), source.Position);
            Assert.Equal(2, source.TrajectoryPoint);
            var craft = campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP").Logistics!;
            Assert.Equal(source.Position, craft.Destination!.Position);
            Assert.Equal(WorldGeometry.Move(new(1, 0.6), source.Position, WorldGeometry.RadianSpeed(6000)), new(craft.Longitude, craft.Latitude));
            campaign = Reload(content, campaign, 81, "base-leader-after.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 81, "base-leader-following.sav");
            var daily = initial.WithNextId("STR_UFO_UNIQUE", 41) with
            { Time = new(1, 1, 1, 1999, 23, 59, 55), CompletedResearch = ["UNLOCK"] };
            var deleted = CampaignState.Restore(daily, content, new SplitMix64RandomSource(81));
            AdvanceUnblocked(deleted, 1);
            Assert.Empty(deleted.Capture().World.AlienBases);
            Assert.DoesNotContain(deleted.Capture().World.Ufos, ufo => !ufo.HunterKiller);
            Assert.Equal(41, deleted.Capture().NextIds["STR_UFO_UNIQUE"]);
            Assert.All(deleted.Capture().World.Missions, mission => Assert.True(mission.Interrupted));
            deleted = Reload(content, deleted, 81, "base-leader-deleted.sav");
            AdvanceUnblocked(deleted, 1);
            Reload(content, deleted, 81, "base-leader-deleted-following.sav");
        }
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 2)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    public void LoadedIdleEscortConsidersIncomingPursuersOnlyAtHourlyArrival(bool cache, bool hourly, int hours)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "incomingCraftRows"), row => row[0].GetInt32() == hours);
        foreach (var earlyLeader in new[] { false, true })
        {
            var content = Load(cache, "crafts: [{type: TIMED_PURSUER, speedMax: 6000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
            var initial = IdleEscortSnapshot(content, earlyLeader
                ? "MISSION_ESCORT_CHAIN_EARLY_LEADER" : "MISSION_ESCORT_CHAIN_LATE_LEADER");
            var owner = initial.Bases[0];
            var incoming = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
            incoming = incoming with
            {
                RuleId = "TIMED_PURSUER",
                Logistics = incoming.Logistics! with
                { Damage = 0, Fuel = 60, IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3, Weapons = [] }
            };
            initial = initial with
            {
                Time = new(1, 1, 1, 1999, 1, hourly ? 59 : 29, 55),
                Bases = [owner with { Crafts = [], Soldiers = [],
                    Transfers = [new(1, hours, CampaignTransferKind.Craft, "TIMED_PURSUER", 1, Craft: incoming)] }],
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "idle-pending-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            if (hourly && hours == 1)
            {
                Assert.Empty(after.Bases[0].Transfers);
                var arrived = Assert.Single(after.Bases[0].Crafts).Logistics!;
                Assert.Equal("STR_OUT", arrived.Status);
                Assert.Equal(WorldTargetKind.Waypoint, arrived.Destination!.Kind);
            }
            else
            {
                Assert.Empty(after.Bases[0].Crafts);
                Assert.Equal(hours - (hourly ? 1 : 0), Assert.Single(after.Bases[0].Transfers).Hours);
            }
            var leader = Assert.Single(after.World.Ufos, ufo => !ufo.HunterKiller);
            var source = after.World.Ufos[0];
            Assert.Equal(9, source.UniqueId);
            Assert.True(source.Escorting);
            Assert.Equal(leader.UniqueId, source.Destination!.UniqueId);
            Assert.Equal(2200, source.Speed);
            var origin = new WorldPosition(10 * Math.PI / 180, 8 * Math.PI / 180);
            Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, origin, WorldGeometry.RadianSpeed(2200)), source.Position);
            Assert.Equal(4, Assert.Single(after.World.Missions).LiveUfos);
            campaign = Reload(content, campaign, 81, "idle-pending-after.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 81, "idle-pending-following.sav");
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void LoadedIdleEscortCancelsOrdinaryArrivalWhenPointLeaderIsCertain(bool cache, int arrival)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "spawnEscortSequenceRows"), row =>
            !row[0].GetBoolean() && row[1].GetBoolean() && row[2].GetBoolean());
        var content = Load(cache);
        var initial = IdleEscortSnapshot(content);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with
                {
                    TrajectoryPoint = arrival == 2 ? 0 : arrival == 1 ? 2 : 0,
                    TrajectoryId = arrival == 2 ? "TRAJ_HUNTER_GROUND" : "TRAJ_SPAWN_FIXED",
                    Destination = WorldTargetReference.ForWaypoint(0, new(0.400001, 0.2)),
                }],
            },
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "cancel-arrival-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var source = after.World.Ufos.Single(ufo => ufo.UniqueId == 9);
        var leader = Assert.Single(after.World.Ufos, ufo => !ufo.HunterKiller);
        Assert.True(source.Escorting);
        Assert.Equal(leader.UniqueId, source.Destination!.UniqueId);
        Assert.Equal(UfoStatus.Flying, source.Status);
        Assert.Equal(initial.World.Ufos[0].TrajectoryPoint, source.TrajectoryPoint);
        Assert.Equal(new WorldPosition(0.400001, 0.2), source.OriginalWaypoint);
        Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, new(Degrees(10), Degrees(8)),
            WorldGeometry.RadianSpeed(2200)), source.Position);
        campaign = Reload(content, campaign, 81, "cancel-arrival-after.sav");
        AdvanceUnblocked(campaign, 1);
        Reload(content, campaign, 81, "cancel-arrival-following.sav");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void LoadedIdleEscortRetainsArrivalGuardForUncertainFirstLeader(bool cache, bool regional)
    {
        var content = Load(cache, $$"""
            regions:
              - type: IDLE_LEADER_AREA
                missionZones:
                  - [[7.9, 8.1, 1.9, 2.1]]
                  - []
                  - []
                  - [[362, 362, 2, 2]]
            alienMissions:
              - type: UNCERTAIN_IDLE_LEADER
                objective: 0
                waves:
                  - {ufo: UFO_SCOUT, count: 1, trajectory: {{(regional ? "TRAJ_AIRBORNE" : "TRAJ_SPAWN_FIXED")}}, timer: 9000, hunterKillerPercentage: {{(regional ? 0 : 50)}}}
            """);
        var initial = IdleEscortSnapshot(content, "UNCERTAIN_IDLE_LEADER");
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RegionId = regional ? "IDLE_LEADER_AREA" : "REGION" }],
                Ufos = [initial.World.Ufos[0] with { TrajectoryPoint = 2, Destination = WorldTargetReference.ForWaypoint(0, new(0.400001, 0.2)) }]
            }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Loaded idle escorts require same-tick newborn waypoint forecasts.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledTerminalEscortArrivalCannotHideFollowingCraftInterception(bool cache)
    {
        var content = Load(cache, "crafts: [{type: SHIP, radarRange: 20000, radarChance: 100}]\n");
        var initial = IdleEscortSnapshot(content);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with
        { Detected = true, TrajectoryPoint = 2, Destination = WorldTargetReference.ForWaypoint(0, new(0.400001, 0.2)) }]
            }
        };
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 1000,
                Damage = 0,
                Speed = 6000,
                Longitude = 0.4,
                Latitude = 0.2,
                Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 9, 0.4, 0.2) { UniqueId = 9 },
            }
        });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Craft interception requires strategic combat (Phase 6 branch 5).");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledArrivalForecastDoesNotOverflowAnUnvalidatedPriorBirth(bool cache)
    {
        var content = Load(cache, """
            alienMissions:
              - type: EXHAUSTED_IDLE_LEADER
                objective: 0
                waves:
                  - {ufo: ESCORT_HUNTER, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0, hunterKillerPercentage: 100}
                  - {ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0}
            """);
        var initial = IdleEscortSnapshot(content, "EXHAUSTED_IDLE_LEADER");
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = int.MaxValue }],
                Ufos = [initial.World.Ufos[0] with { TrajectoryPoint = 2, Destination = WorldTargetReference.ForWaypoint(0, new(0.400001, 0.2)) }],
            }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Mission counter forecasts exceed the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadedIdleEscortAllowsAutoPatrolCraftThatRemainGrounded(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, "crafts: [{type: IDLE_REFUEL, speedMax: 6000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "maintenanceRows").Where(row => !row[0].GetBoolean() && !row[3].GetBoolean() && !row[4].GetBoolean()))
        {
            var initial = IdleEscortSnapshot(content).WithCraft("SHIP", craft => craft with
            {
                RuleId = "IDLE_REFUEL",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = row[1].GetInt32(),
                    Damage = 0,
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = []
                },
            });
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "idle-grounded-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var craftState = Assert.Single(after.Bases[0].Crafts, craft => craft.RuleId == "IDLE_REFUEL").Logistics!;
            Assert.Equal(row[5].GetInt32(), craftState.Fuel + (craftState.Status == "STR_OUT" ? 60 : 0));
            Assert.Equal(row[6].GetString(), craftState.Status);
            var source = after.World.Ufos[0];
            var leader = Assert.Single(after.World.Ufos, ufo => !ufo.HunterKiller);
            Assert.True(source.Escorting);
            Assert.Equal(leader.UniqueId, source.Destination!.UniqueId);
            Assert.Equal(2200, source.Speed);
            Assert.Equal(WorldGeometry.Move(initial.World.Ufos[0].Position, new(10 * Math.PI / 180, 8 * Math.PI / 180),
                WorldGeometry.RadianSpeed(2200)), source.Position);
            campaign = Reload(content, campaign, 81, "idle-grounded-after.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 81, "idle-grounded-following.sav");
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void LoadedIdleEscortWithFollowersMatchesNativeNewLeaderMovement(bool cache, bool reverse, bool cancelsArrival)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world");
        using var attachment = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(attachment.RootElement, "spawnEscortSequenceRows"), row =>
            !row[0].GetBoolean() && row[1].GetBoolean() && row[2].GetBoolean());
        var content = Load(cache);
        var initial = IdleEscortSnapshot(content);
        var source = cancelsArrival ? initial.World.Ufos[0] with
        { TrajectoryPoint = 2, Destination = WorldTargetReference.ForWaypoint(0, new(0.400001, 0.2)) } : initial.World.Ufos[0];
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 2 }],
                Ufos = [source, source with
                {
                    UniqueId = 10,
                    Id = 4,
                    Longitude = 0.7,
                    Latitude = 0.3,
                    Speed = 2200,
                    Escorting = true,
                    OriginalWaypoint = new(1, 0.6),
                    Destination = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, source.Id,
                        source.Longitude, source.Latitude) { UniqueId = source.UniqueId },
                }],
            },
        };
        if (reverse) initial = initial with { World = initial.World with { Ufos = initial.World.Ufos.Reverse().ToArray() } };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "loaded-followers-before.sav");
        var rows = TestFixtures.Rows(oracle.RootElement, "idleEscortFollowerMovement").Where(row => row[0].GetBoolean() == reverse).ToArray();
        Assert.Equal(3, rows.Length);
        foreach (var row in rows)
        {
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var leader = Assert.Single(after.World.Ufos, ufo => ufo.UniqueId is not (9 or 10));
            UfoSnapshot[] roles = [after.World.Ufos.Single(ufo => ufo.UniqueId == 9),
                after.World.Ufos.Single(ufo => ufo.UniqueId == 10), leader];
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[] { reverse, row[1].GetInt32() }.Concat(
                roles.SelectMany(ufo => new object[]
                {
                    decimal.Parse(ufo.Longitude.ToString("F12", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture),
                    decimal.Parse(ufo.Latitude.ToString("F12", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture),
                })).ToArray());
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(row)), CanonicalJson.Normalize(actual));
            Assert.Equal(3, Assert.Single(after.World.Missions).LiveUfos);
            Assert.Equal(leader.UniqueId, after.World.Ufos.Single(ufo => ufo.UniqueId == 9).Destination!.UniqueId);
            Assert.Equal(9, after.World.Ufos.Single(ufo => ufo.UniqueId == 10).Destination!.UniqueId);
            campaign = Reload(content, campaign, 81, "loaded-followers-after.sav");
        }
    }

    [Fact]
    public void LoadedEscortNewLeaderScansAreBoundedBeforeAnyBirth()
    {
        var content = Load(overlay: """
            alienMissions:
              - type: MANY_IDLE_ESCORT_LEADERS
                objective: 0
                waves:
                  - {ufo: UFO_SCOUT, count: 400, trajectory: TRAJ_SPAWN_FIXED, timer: 0}
            """);
        var initial = IdleEscortSnapshot(content, "MANY_IDLE_ESCORT_LEADERS");
        var source = initial.World.Ufos[0];
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 800 }],
                Ufos = Enumerable.Range(0, 800).Select(index => source with
                {
                    UniqueId = 10000 + index,
                    Id = 10000 + index,
                }).ToArray(),
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO escort movement exceeds the supported work limit.");
    }

    [Fact]
    public void ImmediateEscortSelectionReservesWorkBeforeAnyBirth()
    {
        var content = Load(overlay: """
            alienMissions:
              - type: MANY_ESCORTS
                objective: 0
                waves:
                  - {ufo: ESCORT_HUNTER, count: 1001, trajectory: TRAJ_ESCORT_SPAWN, timer: 0, hunterKillerPercentage: 100, escort: true}
            """);
        var initial = SpawnSnapshot(content, UfoStatus.Flying);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [],
                Missions = [initial.World.Missions[0] with { RuleId = "MANY_ESCORTS", NextWave = 0, LiveUfos = 0 }],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "New UFO escort selection exceeds the supported work limit.");
    }


    [Theory]
    [InlineData(false, 2, 1)]
    [InlineData(true, 2, 1)]
    [InlineData(false, -2, 1)]
    [InlineData(true, -2, 1)]
    [InlineData(false, 2, 2)]
    [InlineData(true, -2, 2)]
    [InlineData(false, 2, 1, true)]
    [InlineData(true, 2, 1, true)]
    [InlineData(false, -2, 1, true)]
    [InlineData(true, -2, 1, true)]
    [InlineData(false, 2, 2, true)]
    [InlineData(true, -2, 2, true)]
    [InlineData(false, 2, 1, true, 50)]
    [InlineData(true, 2, 1, true, 50)]
    [InlineData(false, -2, 1, true, 50)]
    [InlineData(true, -2, 1, true, 50)]
    public void RelocatedEscortScoresAtLeaderBeforeAnyOverflow(bool country, int score, int room,
        bool newborn = false, int leaderPercentage = 0)
    {
        var content = Load(overlay: $$"""
            ufos:
              - type: SCORING_ESCORT
                speedMax: 2200
                damageMax: 50
                missionScore: {{score}}
                noAlert: true
              - type: SCORING_LEADER
                speedMax: 2200
                damageMax: 50
                missionScore: 0
                noAlert: true
            regions:
              - type: ESCORT_SCORE_REGION
                areas: [[40, 60, -10, 10]]
                missionZones:
                  - [[45, 45, 1, 1]]
                  - [[45, 45, 1, 1]]
                  - [[45, 45, 1, 1]]
                  - [[362, 362, 2, 2]]
                  - [[45, 45, 1, 1]]
                  - [[10, 10, 8, 8]]
            countries:
              - type: ESCORT_SCORE_COUNTRY
                areas: [[40, 60, -10, 10]]
            alienMissions:
              - type: SCORING_ESCORT_MISSION
                objective: 0
                waves:
                  - {ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 9000}
                  - {ufo: SCORING_ESCORT, count: 1, trajectory: TRAJ_ESCORT_SPAWN, timer: 9000, hunterKillerPercentage: 100, escort: true}
              - type: SCORING_ESCORT_IMMEDIATE
                objective: 0
                waves:
                  - {ufo: SCORING_LEADER, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 9000, hunterKillerPercentage: {{leaderPercentage}}}
                  - {ufo: SCORING_ESCORT, count: 1, trajectory: TRAJ_ESCORT_DISTINCT, timer: 0, hunterKillerPercentage: 100, escort: true}
            """);
        var initial = SpawnSnapshot(content, UfoStatus.IgnoreMe);
        var value = score > 0 ? int.MaxValue - room : int.MinValue + room;
        initial = initial with
        {
            Regions = initial.Regions.Select(region => !country && region.RuleId == "ESCORT_SCORE_REGION"
                ? region with { ActivityAlien = [value] } : region).ToArray(),
            Countries = initial.Countries.Select(owner => country && owner.RuleId == "ESCORT_SCORE_COUNTRY"
                ? owner with { ActivityAlien = [value] } : owner).ToArray(),
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RuleId = "SCORING_ESCORT_MISSION" }],
                Ufos = [initial.World.Ufos[0] with { Longitude = Degrees(45), Latitude = Degrees(1) }],
            },
        };
        if (newborn) initial = initial with
        {
            World = initial.World with
            {
                Ufos = [],
                Missions = [initial.World.Missions[0] with
                {
                    RuleId = "SCORING_ESCORT_IMMEDIATE",
                    RegionId = "ESCORT_SCORE_REGION",
                    NextWave = 0,
                    LiveUfos = 0,
                }],
            },
        };
        Assert.Contains(initial.Regions, region => region.RuleId == "ESCORT_SCORE_REGION");
        Assert.Contains(initial.Countries, owner => owner.RuleId == "ESCORT_SCORE_COUNTRY");
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        if (room < Math.Abs(score))
            AssertTimeBlocked(campaign, "UFO alien activity exceeds the supported range.");
        else
        {
            AdvanceOne(campaign);
            var after = campaign.Capture();
            Assert.Equal(value + score, country
                ? after.Countries.Single(owner => owner.RuleId == "ESCORT_SCORE_COUNTRY").ActivityAlien[^1]
                : after.Regions.Single(region => region.RuleId == "ESCORT_SCORE_REGION").ActivityAlien[^1]);
            Reload(content, campaign, 81, "relocated-escort-score.sav");
        }
    }

    [Fact]
    public void NewEscortSurvivesTerminalLeaderFallthroughAndRepairsOnFollowingTick()
    {
        var content = Load();
        var initial = SpawnSnapshot(content, UfoStatus.Flying);
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with
                {
                    TrajectoryPoint = 2,
                    Destination = WorldTargetReference.ForWaypoint(0, new(0.400001, 0.2)),
                }],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var after = campaign.Capture();
        Assert.Equal(UfoStatus.Destroyed, after.World.Ufos[0].Status);
        var escort = after.World.Ufos[1];
        Assert.Equal(initial.World.Ufos[0].Position, escort.Position);
        Assert.True(escort.Escorting);
        Assert.NotNull(escort.OriginalWaypoint);
        Assert.Equal(2, after.World.Missions[0].LiveUfos);
        campaign = Reload(content, campaign, 81, "escort-terminal-leader.sav");
        AdvanceOne(campaign);
        var restored = Assert.Single(campaign.Capture().World.Ufos);
        Assert.False(restored.Escorting);
        Assert.Equal(escort.OriginalWaypoint, restored.Destination!.Position);
        Assert.Equal(2200, restored.Speed);
        Assert.Equal(1, campaign.Capture().World.Missions[0].LiveUfos);
        Reload(content, campaign, 81, "escort-terminal-leader-deleted.sav");
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GroundedEscortSpawnsWithActiveCraftAndRetainsBackupThroughTakeoff(bool cache, bool leader)
    {
        var content = Load(cache);
        var initial = SpawnSnapshot(content, UfoStatus.Flying).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Longitude = 0.45,
                Latitude = 0.21,
                Destination = null,
            },
        });
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RuleId = "MISSION_ESCORT_GROUND", LiveUfos = leader ? 1 : 0 }],
                Ufos = leader ? initial.World.Ufos : [],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        int[] expectedContacts = leader ? [9, 1] : [];
        Assert.Equal(expectedContacts, result.Events.OfType<UfoContactDetected>().Select(item => item.UniqueId).ToArray());
        Assert.Equal(leader ? 3 : 1, result.Events.Count);
        var spawned = campaign.Capture().World.Ufos.Single(ufo => ufo.RuleId == "ESCORT_HUNTER");
        Assert.Equal(UfoStatus.Landed, spawned.Status);
        Assert.Equal(WorldAltitudes.Ground, spawned.Altitude);
        Assert.Equal(55, spawned.SecondsRemaining);
        Assert.True(spawned.HunterKiller);
        Assert.True(spawned.Escort);
        Assert.Equal(leader, spawned.Escorting);
        Assert.Equal(leader ? 2200 : 550, spawned.Speed);
        Assert.Equal(-1, spawned.Shield);
        var start = spawned.Position;
        var original = spawned.OriginalWaypoint;
        campaign = Reload(content, campaign, 81, "ground-escort-birth.sav");
        AdvanceUnblocked(campaign, 11);
        var lifted = campaign.Capture().World.Ufos.Single(ufo => ufo.RuleId == "ESCORT_HUNTER");
        Assert.Equal(UfoStatus.Flying, lifted.Status);
        Assert.Equal(WorldAltitudes.VeryLow, lifted.Altitude);
        Assert.Equal(start, lifted.Position);
        Assert.Equal(550, lifted.Speed);
        Assert.Equal(original, lifted.OriginalWaypoint);
        Assert.Equal(-1, lifted.Shield);
        campaign = Reload(content, campaign, 81, "ground-escort-takeoff.sav");
        AdvanceOne(campaign);
        Assert.Equal(leader, campaign.Capture().World.Ufos.Single(ufo => ufo.RuleId == "ESCORT_HUNTER").Escorting);
        Reload(content, campaign, 81, "ground-escort-first-flight.sav");
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationTickGroundEscortLiftsBeforeItsNextCraftHunt(bool cache)
    {
        var content = Load(cache);
        var initial = SpawnSnapshot(content, UfoStatus.Flying).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 0,
                Longitude = 0.401,
                Latitude = 0.2,
                Destination = null,
            },
        });
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RuleId = "MISSION_ESCORT_GROUND_IMMEDIATE" }],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked or UfoStartedHunting);
        var escort = campaign.Capture().World.Ufos.Single(ufo => ufo.RuleId == "HUNTER");
        Assert.Equal(UfoStatus.Flying, escort.Status);
        Assert.Equal(WorldAltitudes.VeryLow, escort.Altitude);
        Assert.Equal(0, escort.SecondsRemaining);
        Assert.Equal(initial.World.Ufos[0].Position, escort.Position);
        Assert.Equal(550, escort.Speed);
        Assert.True(escort.Escorting);
        Assert.NotNull(escort.OriginalWaypoint);
        Assert.Equal(-1, escort.Shield);
        var rules = content.RuntimeRules.Ufos;
        var expectedScore = rules[rules.GetRequired("UFO_SCOUT")].Value.MissionScore +
            2 * rules[rules.GetRequired("HUNTER")].Value.MissionScore + 7;
        Assert.Equal(initial.Regions[0].ActivityAlien[^1] + expectedScore, campaign.Capture().Regions[0].ActivityAlien[^1]);
        campaign = Reload(content, campaign, 81, "creation-tick-escort-takeoff.sav");
        AssertTimeBlocked(campaign, "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
    }

    [Fact]
    public void NewbornTakeoffCannotProvideHeadroomForEarlierSiteUnderflow()
    {
        var content = Load(overlay: """
            alienDeployments:
              - type: NEGATIVE_TAKEOFF_SITE
                markerName: STR_TERROR_SITE
                points: -4
            """);
        Assert.Equal(-4, content.RuntimeRules.AlienDeployments[content.RuntimeRules.AlienDeployments.GetRequired("NEGATIVE_TAKEOFF_SITE")].Value.Points);
        var initial = SpawnSnapshot(content, UfoStatus.IgnoreMe);
        initial = initial with
        {
            Regions = [initial.Regions[0] with { ActivityAlien = [int.MinValue + 1] }],
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RuleId = "MISSION_ESCORT_GROUND_IMMEDIATE" }],
                MissionSites = [new(1, "MISSION_SCOUT", "NEGATIVE_TAKEOFF_SITE", "RACE_A", 0.4, 0.2, 3600)],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO alien activity exceeds the supported range.");
    }

    [Fact]
    public void TerminalLeaderSkipsNewbornGroundTakeoffAndItsScoreReservation()
    {
        var content = Load();
        var initial = SpawnSnapshot(content, UfoStatus.Flying);
        var scoutScore = content.RuntimeRules.Ufos[content.RuntimeRules.Ufos.GetRequired("UFO_SCOUT")].Value.MissionScore;
        var birthScore = 2 * content.RuntimeRules.Ufos[content.RuntimeRules.Ufos.GetRequired("HUNTER")].Value.MissionScore;
        Assert.Equal(3, content.RuntimeRules.UfoTrajectories[content.RuntimeRules.UfoTrajectories.GetRequired("TRAJ_SPAWN_FIXED")].Value.Waypoints.Count);
        initial = initial with
        {
            Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - scoutScore - birthScore] }],
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RuleId = "MISSION_ESCORT_GROUND_IMMEDIATE" }],
                Ufos = [initial.World.Ufos[0] with
                {
                    TrajectoryPoint = 2,
                    Destination = WorldTargetReference.ForWaypoint(0, new(0.400001, 0.2)),
                }],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceOne(campaign);
        var after = campaign.Capture();
        Assert.Equal(int.MaxValue, after.Regions[0].ActivityAlien[^1]);
        Assert.Equal(UfoStatus.Destroyed, after.World.Ufos[0].Status);
        Assert.Equal(UfoStatus.Landed, after.World.Ufos[1].Status);
        Assert.Equal(5, after.World.Ufos[1].SecondsRemaining);
        campaign = Reload(content, campaign, 81, "terminal-ground-escort-before-lift.sav");
        AssertTimeBlocked(campaign, "UFO alien activity exceeds the supported range.");
    }

    private static CampaignSnapshot IdleEscortSnapshot(RuntimeContent content, string missionRule = "MISSION_ESCORT_SPAWN")
    {
        var initial = SpawnSnapshot(content, UfoStatus.Flying);
        return initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RuleId = missionRule, NextWave = 0 }],
                Ufos = [initial.World.Ufos[0] with
                {
                    Id = 9,
                    RuleId = "ESCORT_HUNTER",
                    HunterKiller = true,
                    Escort = true,
                    Speed = 550,
                }],
            },
        };
    }

    private static RuntimeContent Load(bool cache = false, string? overlay = null) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache, extraRules: overlay);

    private static CampaignSnapshot SpawnSnapshot(RuntimeContent content, UfoStatus status)
    {
        var initial = CreateSnapshot(content, "New UFO escorts");
        return initial with
        {
            RandomState = 81,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = new WorldSnapshot
            {
                Missions = [new(4, "MISSION_ESCORT_SPAWN", "REGION", "RACE_A", 1, 0, 0, 1, -1)],
                Ufos = [new(9, "UFO_SCOUT", 4, "TRAJ_SPAWN_FIXED", 0, 0.4, 0.2, status,
                    status == UfoStatus.Landed ? WorldAltitudes.Ground : WorldAltitudes.High)
                {
                    Id = 3,
                    Speed = status == UfoStatus.Flying ? 900 : 0,
                    MissionWaveNumber = 0,
                    SecondsRemaining = status == UfoStatus.Landed ? 30 : 60,
                    Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6)),
                }],
            },
        };
    }
}
