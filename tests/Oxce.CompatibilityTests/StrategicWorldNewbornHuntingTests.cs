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

public sealed class StrategicWorldNewbornHuntingTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    public void ZeroRadarHuntersAndEscortsSpawnAlongsideActiveCraft(bool cache, bool escort, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "selectionRows"), row =>
            row[0].GetInt32() == mode && row[1].GetInt32() == 5 && row[2].GetInt32() == 0);
        var content = Load(cache, escort, mode);
        var initial = Snapshot(content, escort);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "zero-radar-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var born = Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "ZERO_RADAR_HUNTER");
        Assert.True(born.HunterKiller);
        Assert.False(born.Hunting);
        Assert.Equal(mode, born.HuntMode);
        Assert.Equal(escort, born.Escorting);
        Assert.Equal(escort ? 2 : 1, Assert.Single(after.World.Missions).LiveUfos);
        if (escort)
        {
            var leader = after.World.Ufos.Single(ufo => ufo.UniqueId == 9);
            Assert.Equal(leader.UniqueId, born.Destination!.UniqueId);
            Assert.Equal(leader.Position, born.Position);
            Assert.NotNull(born.OriginalWaypoint);
        }
        else Assert.Equal(WorldTargetKind.Waypoint, born.Destination!.Kind);
        campaign = Reload(content, campaign, 81, "zero-radar-after.sav");
        AdvanceOne(campaign);
        Assert.False(campaign.Capture().World.Ufos.Single(ufo => ufo.UniqueId == born.UniqueId).Hunting);
        Reload(content, campaign, 81, "zero-radar-following.sav");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void ZeroRadarNewbornsRejectMalformedAttractionBeforeBirth(bool cache, int mode)
    {
        var content = Load(cache, false, mode, "crafts: [{type: SHIP, soldiers: 2147483647}]\n");
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, false), content, new SplitMix64RandomSource(81)),
            "Craft hunter attraction exceeds the supported range.");
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 2)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    public void PendingCraftJoinsNewbornCandidatesOnlyAtItsHourlyArrival(bool cache, bool hourly, int hours)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "incomingCraftRows"), row => row[0].GetInt32() == hours);
        foreach (var escort in new[] { false, true })
        {
            var content = Load(cache, escort, 0,
                "crafts: [{type: TIMED_INCOMING, speedMax: 6000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n", radar: 300);
            var initial = Snapshot(content, escort);
            var owner = initial.Bases[0];
            var incoming = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
            incoming = incoming with
            {
                RuleId = "TIMED_INCOMING",
                Logistics = incoming.Logistics! with
                { Damage = 0, Fuel = 60, IsAutoPatrolling = true, AutoPatrolLongitude = 0.8, AutoPatrolLatitude = 0.3, Weapons = [] }
            };
            initial = initial with
            {
                Time = new(1, 1, 1, 1999, 1, hourly ? 59 : 29, 55),
                Bases = [owner with { Crafts = [], Soldiers = [],
                    Transfers = [new(1, hours, CampaignTransferKind.Craft, "TIMED_INCOMING", 1, Craft: incoming)] }],
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "pending-newborn-before.sav");
            if (hourly && hours == 1)
            {
                AssertTimeBlocked(campaign, escort ? "New UFO escorts require craft retarget forecasts." :
                    "Alien mission wave spawning requires world simulation.");
                continue;
            }
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Empty(after.Bases[0].Crafts);
            Assert.Equal(hours - (hourly ? 1 : 0), Assert.Single(after.Bases[0].Transfers).Hours);
            Assert.False(Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "ZERO_RADAR_HUNTER").Hunting);
            campaign = Reload(content, campaign, 81, "pending-newborn-after.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 81, "pending-newborn-following.sav");
        }
    }

    private static RuntimeContent Load(bool cache, bool escort, int mode, string extra = "", int radar = 0) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache, $$"""
            ufos:
              - type: ZERO_RADAR_HUNTER
                speedMax: 2200
                damageMax: 50
                radarRange: {{radar}}
                hunterKillerPercentage: 100
                huntMode: {{mode}}
            alienMissions:
              - type: ZERO_RADAR_MISSION
                objective: 0
                waves:
                  - {ufo: ZERO_RADAR_HUNTER, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 9000, escort: {{escort.ToString().ToLowerInvariant()}}}
            {{extra}}
            """);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GroundedAutoPatrolCraftBlocksNewbornHuntersOnlyWhenRefuellingLaunchesIt(bool cache, bool escort)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, escort, 0,
            "crafts: [{type: BIRTH_REFUEL, speedMax: 6000, damageMax: 100, fuelMax: 100, refuelRate: 40, autoPatrol: true}]\n", radar: 300);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "maintenanceRows").Where(row => !row[0].GetBoolean() && !row[3].GetBoolean() && !row[4].GetBoolean()))
        {
            var initial = Snapshot(content, escort).WithCraft("SHIP", craft => craft with
            {
                RuleId = "BIRTH_REFUEL",
                Logistics = craft.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Fuel = row[1].GetInt32(),
                    LowFuel = row[3].GetBoolean(),
                    IsAutoPatrolling = true,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = []
                },
            });
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "grounded-birth-before.sav");
            if (row[6].GetString() == "STR_OUT")
            {
                AssertTimeBlocked(campaign, escort ? "New UFO escorts require craft retarget forecasts." :
                    "Alien mission wave spawning requires world simulation.");
                continue;
            }
            AdvanceUnblocked(campaign, 1);
            var state = Assert.Single(campaign.Capture().Bases[0].Crafts, craft => craft.RuleId == "BIRTH_REFUEL").Logistics!;
            Assert.Equal(row[5].GetInt32(), state.Fuel);
            Assert.Equal(row[6].GetString(), state.Status);
            Assert.False(Assert.Single(campaign.Capture().World.Ufos, ufo => ufo.RuleId == "ZERO_RADAR_HUNTER").Hunting);
            campaign = Reload(content, campaign, 81, "grounded-birth-after.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 81, "grounded-birth-following.sav");
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, -2)]
    [InlineData(true, -2)]
    public void ZeroRadarProofValidatesEveryPossibleConstructorMode(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var nativeMode = mode < 0 ? -1 : mode;
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "constructorRows"), row =>
            row[0].GetInt32() == 100 && row[1].GetInt32() == nativeMode && row[2].GetInt32() == 0 &&
            row[3].GetInt32() == 0 && row[4].GetBoolean() && row[5].GetInt32() == (nativeMode > 1 ? 1 : nativeMode));
        var content = Load(cache, false, mode,
            "crafts: [{type: MODE_LIMIT_CRAFT, soldiers: 0, speedMax: 2147483647, damageMax: 100, fuelMax: 2147483647}]\n");
        var initial = Snapshot(content, false).WithCraft("SHIP", craft => craft with
        {
            RuleId = "MODE_LIMIT_CRAFT",
            Logistics = craft.Logistics! with { Weapons = [], Fuel = int.MaxValue - 1000 },
        });
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        if (mode != 0)
            AssertTimeBlocked(campaign, "Craft hunter attraction exceeds the supported range.");
        else
        {
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(0, Assert.Single(campaign.Capture().World.Ufos).HuntMode);
            Reload(content, campaign, 81, "constructor-mode-zero.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroRadarProofDoesNotReuseAValidModeForALaterImmediateWave(bool cache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache, """
            ufos: [{type: ZERO_RADAR_HUNTER, speedMax: 2200, damageMax: 50, radarRange: 0, hunterKillerPercentage: 100}]
            crafts: [{type: MODE_LIMIT_CRAFT, soldiers: 0, speedMax: 2147483647, damageMax: 100, fuelMax: 2147483647}]
            alienMissions:
              - type: MODE_CHAIN
                objective: 0
                waves:
                  - {ufo: ZERO_RADAR_HUNTER, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0, huntMode: 0}
                  - {ufo: ZERO_RADAR_HUNTER, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0, huntMode: 1}
            """);
        var initial = Snapshot(content, false).WithCraft("SHIP", craft => craft with
        {
            RuleId = "MODE_LIMIT_CRAFT",
            Logistics = craft.Logistics! with { Weapons = [], Fuel = int.MaxValue - 1000 },
        });
        initial = initial with
        {
            World = initial.World with
            { Missions = [initial.World.Missions[0] with { RuleId = "MODE_CHAIN" }] }
        };
        var waves = content.RuntimeRules.AlienMissions[content.RuntimeRules.AlienMissions.GetRequired("MODE_CHAIN")].Value.Waves;
        Assert.Equal(1, waves[1].HuntMode);
        Assert.Equal(0UL, waves[1].SpawnTimer);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "Craft hunter attraction exceeds the supported range.");
    }

    private static CampaignSnapshot Snapshot(RuntimeContent content, bool escort)
    {
        var initial = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 29, 55), "UFO_SCOUT", 900).Capture();
        return initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            { Status = "STR_OUT", Fuel = 300, Speed = 0, Destination = null }
        }) with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RuleId = "ZERO_RADAR_MISSION", NextWave = 0, SpawnCountdown = 0, LiveUfos = escort ? 1 : 0 }],
                Ufos = escort ? initial.World.Ufos : [],
            },
        };
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SharedFuelStockForecastControlsNewbornCandidateEligibility(bool cache, bool escort)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = Load(cache, escort, 0,
            "crafts: [{type: SHARED_BIRTH, speedMax: 2000, damageMax: 100, fuelMax: 100, refuelRate: 40, refuelItem: SUPPLY, autoPatrol: true}]\n", radar: 300);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "sharedFuelRows"))
        {
            var initial = Snapshot(content, escort);
            var owner = initial.Bases[0];
            var template = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
            CraftSnapshot Craft(int id, int fuel, bool auto) => template with
            {
                RuleId = "SHARED_BIRTH",
                Id = id,
                Logistics = template.Logistics! with
                {
                    Status = "STR_REFUELLING",
                    Damage = 0,
                    Fuel = fuel,
                    IsAutoPatrolling = auto,
                    AutoPatrolLongitude = 0.8,
                    AutoPatrolLatitude = 0.3,
                    Weapons = []
                },
            };
            initial = initial with
            {
                Bases = [owner with { Crafts = [Craft(1, row[0].GetInt32(), row[2].GetBoolean()), Craft(2, 60, true)],
                    Soldiers = [], Items = row[1].GetInt32() == 0 ? new Dictionary<string, int>() :
                        new Dictionary<string, int> { ["SUPPLY"] = row[1].GetInt32() } }],
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "shared-birth-before.sav");
            if (row[4].GetString() == "STR_OUT" || row[6].GetString() == "STR_OUT")
            {
                AssertTimeBlocked(campaign, escort ? "New UFO escorts require craft retarget forecasts." :
                    "Alien mission wave spawning requires world simulation.");
                continue;
            }
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var first = after.Bases[0].Crafts[0].Logistics!;
            var second = after.Bases[0].Crafts[1].Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                { first.Fuel, first.Status, second.Fuel, second.Status, after.Bases[0].Items.GetValueOrDefault("SUPPLY") });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(3).Take(5).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Assert.False(Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "ZERO_RADAR_HUNTER").Hunting);
            campaign = Reload(content, campaign, 81, "shared-birth-after.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 81, "shared-birth-following.sav");
        }
    }
}
