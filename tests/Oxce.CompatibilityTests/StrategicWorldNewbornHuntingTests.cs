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
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void OrdinaryNewbornHuntersSelectCraftAndPersistPursuit(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = RetargetContent(cache, mode);
        var initial = RetargetSnapshot(content);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            81, "target-birth-before.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        var born = Assert.Single(campaign.Capture().World.Ufos);
        var selection = Assert.Single(TestFixtures.Rows(oracle.RootElement, "selectionRows"),
            row => row[0].GetInt32() == born.HuntMode && row[1].GetInt32() == 0);
        var transition = Assert.Single(TestFixtures.Rows(oracle.RootElement, "retargetRows"), row => row[0].GetInt32() == 150);
        Assert.True(born.Hunting);
        Assert.True(born.Detected);
        Assert.True(born.Id > 0);
        Assert.Equal(selection[2].GetInt32(), born.Destination!.Id);
        Assert.Equal(transition[1].GetInt32(), born.Speed);
        Assert.Equal(WorldPosition.Create(Degrees(362), Degrees(2)), born.OriginalWaypoint);
        var alert = Assert.Single(result.Events.OfType<UfoStartedHunting>());
        Assert.Equal(born.UniqueId, alert.UniqueId);
        Assert.Equal(born.Destination.Id, alert.CraftId);
        var target = campaign.Capture().Bases[0].Crafts.Single(craft => craft.Id == born.Destination.Id).Logistics!;
        var expected = WorldGeometry.Move(new(Degrees(10), Degrees(8)), new(target.Longitude, target.Latitude),
            WorldGeometry.RadianSpeed(born.Speed));
        Assert.Equal(expected.Longitude, born.Longitude, 12);
        Assert.Equal(expected.Latitude, born.Latitude, 12);
        campaign = Reload(content, campaign, 81, "target-birth-after.sav");
        AdvanceUnblocked(campaign, 12);
        Assert.Equal(born.Destination.Id, Assert.Single(campaign.Capture().World.Ufos).Destination!.Id);
        Reload(content, campaign, 81, "target-birth-following.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryNewbornHuntingReservesNoAlertMarkersAndStopsBeforeCombat(bool cache)
    {
        var content = RetargetContent(cache, 0, noAlert: true);
        var initial = RetargetSnapshot(content);
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO", int.MaxValue), content, new SplitMix64RandomSource(81)),
            "UFO contact marker IDs are exhausted or collide with a saved UFO.");
        var owner = initial.Bases[0];
        initial = initial with
        {
            Bases = [owner with { Crafts = [owner.Crafts[0] with
            { Logistics = owner.Crafts[0].Logistics! with { Longitude = Degrees(10), Latitude = Degrees(8) } }] }]
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void NewbornEscortsHuntFromTheirLeaderOriginAndKeepTheBackup(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var content = RetargetContent(cache, mode, escort: true);
        var initial = RetargetSnapshot(content, escort: true);
        var origin = new WorldPosition(0.7, 0.3);
        initial = initial with
        {
            Bases = [initial.Bases[0] with { Crafts = [.. initial.Bases[0].Crafts.Select(craft => craft with
                { Logistics = craft.Logistics! with { Longitude = origin.Longitude + WorldGeometry.Nautical(100), Latitude = origin.Latitude } })] }],
            World = initial.World with { Ufos = [initial.World.Ufos[0] with { Longitude = origin.Longitude, Latitude = origin.Latitude }] },
        };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "escort-hunt-before.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var born = Assert.Single(campaign.Capture().World.Ufos, ufo => ufo.RuleId == "BIRTH_RETARGET");
        Assert.True(born.Escort);
        Assert.True(born.Hunting);
        Assert.False(born.Escorting);
        Assert.Equal(WorldPosition.Create(Degrees(362), Degrees(2)), born.OriginalWaypoint);
        var selection = Assert.Single(TestFixtures.Rows(oracle.RootElement, "selectionRows"),
            row => row[0].GetInt32() == born.HuntMode && row[1].GetInt32() == 0);
        Assert.Equal(selection[2].GetInt32(), born.Destination!.Id);
        var target = campaign.Capture().Bases[0].Crafts.Single(craft => craft.Id == born.Destination.Id).Logistics!;
        var expected = WorldGeometry.Move(origin, new(target.Longitude, target.Latitude), WorldGeometry.RadianSpeed(3300));
        Assert.Equal(expected.Longitude, born.Longitude, 12);
        Assert.Equal(expected.Latitude, born.Latitude, 12);
        Assert.Single(result.Events.OfType<UfoStartedHunting>());
        campaign = Reload(content, campaign, 81, "escort-hunt-after.sav");
        AdvanceUnblocked(campaign, 12);
        Reload(content, campaign, 81, "escort-hunt-following.sav");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, -1)]
    [InlineData(true, -1)]
    public void NewbornEscortCombatReachUsesInitializedSpeedWhenHuntSpeedDoesNotOverride(bool cache, int huntSpeed)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "retargetRows"), row => row[0].GetInt32() == huntSpeed && row[1].GetInt32() == 900);
        var content = RetargetContent(cache, 0, escort: true, huntSpeed: huntSpeed, slowStart: true);
        var initial = RetargetSnapshot(content, escort: true);
        var leader = initial.World.Ufos[0];
        var owner = initial.Bases[0];
        initial = initial with
        {
            Bases = [owner with { Crafts = [owner.Crafts[0] with { Logistics = owner.Crafts[0].Logistics! with
            { Longitude = leader.Longitude + WorldGeometry.RadianSpeed(1000), Latitude = leader.Latitude } }] }]
        };
        // This craft is beyond the trajectory's 550-speed step but inside the
        // native initialized escort's 2200-speed step.
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
        var state = initial.Bases[0].Crafts[0].Logistics!;
        initial = initial with
        {
            Bases = [initial.Bases[0] with { Crafts = [initial.Bases[0].Crafts[0] with
            { Logistics = state with { Longitude = leader.Longitude + WorldGeometry.Nautical(100) } }] }]
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        AdvanceUnblocked(campaign, 1);
        var born = Assert.Single(campaign.Capture().World.Ufos, ufo => ufo.RuleId == "BIRTH_RETARGET");
        Assert.True(born.Hunting);
        Assert.Equal(2200, born.Speed);
        Reload(content, campaign, 81, "escort-initialized-speed.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateSameWaveEscortsCannotReuseAPreLeaderSpeedProof(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "spawnEscortSequenceRows"), row =>
            row[0].GetBoolean() && row[1].GetBoolean() && row[2].GetBoolean());
        var content = RetargetContent(cache, 0, escort: true, huntSpeed: 0, slowStart: true,
            count: 2, immediate: true, hunterPercentage: 50);
        var initial = RetargetSnapshot(content);
        var owner = initial.Bases[0];
        initial = initial with
        {
            Bases = [owner with { Crafts = [owner.Crafts[0] with
        {
            Logistics = owner.Crafts[0].Logistics! with
            { Longitude = Degrees(10) + WorldGeometry.RadianSpeed(1000), Latitude = Degrees(8) },
        }] }]
        };
        // The first ordinary birth can provide the next birth's leader, changing
        // its initial speed from 550 to 2200 within the same immediate wave.
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
            "New UFO escorts require craft retarget forecasts.");
    }

    private static RuntimeContent RetargetContent(bool cache, int mode, bool escort = false, bool noAlert = false,
        int huntSpeed = 150, bool slowStart = false, int count = 1, bool immediate = false, int hunterPercentage = 100) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-hunting.rul"], cache, $$"""
            crafts:
              - {type: BIRTH_SLOW, speedMax: 2000, damageMax: 100, fuelMax: 1000, soldiers: 0, allowLanding: false, patrolWithoutFuel: true}
              - {type: BIRTH_FAST, speedMax: 6000, damageMax: 100, fuelMax: 1000, soldiers: 0, allowLanding: false, patrolWithoutFuel: true}
            ufos:
              - {type: BIRTH_RETARGET, speedMax: 2200, damageMax: 50, radarRange: 300, huntSpeed: {{huntSpeed}}, hunterKillerPercentage: {{hunterPercentage}}, huntMode: {{mode}}, noAlert: {{noAlert.ToString().ToLowerInvariant()}}}
            ufoTrajectories:
              - id: BIRTH_RETARGET_PATH
                waypoints: [[4, 3, {{(slowStart ? 25 : 100)}}], [3, 2, 80], [0, 3, 50]]
            alienMissions:
              - type: BIRTH_RETARGET_MISSION
                objective: 0
                waves: [{ufo: BIRTH_RETARGET, count: {{count}}, trajectory: BIRTH_RETARGET_PATH, timer: {{(immediate ? 0 : 9000)}}, escort: {{escort.ToString().ToLowerInvariant()}}}]
            """);

    private static CampaignSnapshot RetargetSnapshot(RuntimeContent content, bool escort = false)
    {
        var initial = Snapshot(content, escort);
        var owner = initial.Bases[0];
        var template = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
        CraftSnapshot Craft(int id, string rule) => template with
        {
            Id = id,
            RuleId = rule,
            Logistics = template.Logistics! with
            { Fuel = 1000, Damage = 0, Weapons = [], Longitude = Degrees(10) + WorldGeometry.Nautical(100), Latitude = Degrees(8) },
        };
        return initial with
        {
            Bases = [owner with { Crafts = [Craft(1, "BIRTH_SLOW"), Craft(2, "BIRTH_FAST")], Soldiers = [] }],
            World = initial.World with { Missions = [initial.World.Missions[0] with { RuleId = "BIRTH_RETARGET_MISSION" }] },
        };
    }

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
                if (escort) AssertTimeBlocked(campaign, "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
                else
                {
                    AdvanceUnblocked(campaign, 1);
                    Assert.Empty(campaign.Capture().Bases[0].Transfers);
                    Assert.Equal("STR_OUT", Assert.Single(campaign.Capture().Bases[0].Crafts).Logistics!.Status);
                    Assert.True(Assert.Single(campaign.Capture().World.Ufos).Hunting);
                    campaign = Reload(content, campaign, 81, "pending-newborn-arrived.sav");
                    AdvanceUnblocked(campaign, 1);
                    Reload(content, campaign, 81, "pending-newborn-arrived-following.sav");
                }
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

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void PositiveRadarNewbornsUseAllPossibleSpawnOrigins(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "selectionRows"), row => row[0].GetInt32() == mode && row[1].GetInt32() == 7 && row[2].GetInt32() == 0);
        foreach (var regional in new[] { false, true })
            foreach (var near in new[] { false, true })
            {
                var extra = "regions: [{type: SPATIAL_REGION, areas: [[0, 90, -30, 50]], missionZones: [[[2, 28, -18, 18, 10]], [[8, 8, 2, 2, 10]], [[362, 362, 2, 2, 10]], [[20, 20, 8, 8, 10]], " +
                    (regional ? "[[7, 9, 1, 3, 10]]" : "[[10, 10, 8, 8, 10]]") + "]}]\n";
                extra += "crafts: [{type: SPATIAL_CRAFT, speedMax: 6000, damageMax: 100, fuelMax: 1000, patrolWithoutFuel: true}]\n";
                var content = Load(cache, false, mode, extra, radar: 300);
                var spawnAreas = WorldGeometry.MissionAreas(content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("SPATIAL_REGION")].Value, 4);
                Assert.Equal(!regional, spawnAreas[0].IsPoint);
                var initial = Snapshot(content, false).WithCraft("SHIP", craft => craft with
                {
                    RuleId = "SPATIAL_CRAFT",
                    Logistics = craft.Logistics! with
                    {
                        Fuel = 1000,
                        Damage = 0,
                        Weapons = [],
                        Longitude = near ? Degrees(regional ? 8 : 10) : 1.2,
                        Latitude = near ? Degrees(regional ? 2 : 8) : 0.6
                    }
                });
                initial = initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { RegionId = "SPATIAL_REGION" }] } };
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "separated-birth-before.sav");
                Assert.Equal(300, content.RuntimeRules.Ufos[content.RuntimeRules.Ufos.GetRequired("ZERO_RADAR_HUNTER")].Value.Stats.RadarRange);
                var candidate = campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "SPATIAL_CRAFT").Logistics!;
                Assert.False(candidate.LowFuel);
                Assert.False(candidate.MissionComplete);
                Assert.Equal("STR_OUT", candidate.Status);
                Assert.Equal(100, content.RuntimeRules.Crafts[content.RuntimeRules.Crafts.GetRequired("SPATIAL_CRAFT")].Value.DamageMaximum);
                if (near)
                {
                    AssertTimeBlocked(campaign, "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
                    continue;
                }
                AdvanceUnblocked(campaign, 1);
                var born = Assert.Single(campaign.Capture().World.Ufos);
                Assert.True(born.HunterKiller);
                Assert.False(born.Hunting);
                Assert.Equal(mode, born.HuntMode);
                campaign = Reload(content, campaign, 81, "separated-birth-after.sav");
                AdvanceOne(campaign);
                Reload(content, campaign, 81, "separated-birth-following.sav");
            }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void PositiveRadarEscortProofUsesTheLeaderOrigin(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "selectionRows"), row => row[1].GetInt32() == 7 && row[2].GetInt32() == 0);
        foreach (var nearLeader in new[] { false, true })
        {
            var content = Load(cache, true, mode,
                "crafts: [{type: ESCORT_SPATIAL, speedMax: 6000, damageMax: 100, fuelMax: 1000, patrolWithoutFuel: true}]\n", radar: 300);
            var initial = Snapshot(content, true).WithCraft("SHIP", craft => craft with
            {
                RuleId = "ESCORT_SPATIAL",
                Logistics = craft.Logistics! with
                {
                    Damage = 0,
                    Fuel = 1000,
                    Weapons = [],
                    Longitude = nearLeader ? 1.2 : Degrees(10),
                    Latitude = nearLeader ? 0.6 : Degrees(8)
                }
            });
            initial = initial with
            {
                World = initial.World with
                {
                    Ufos = [initial.World.Ufos[0] with
                { Longitude = 1.2, Latitude = 0.6, Speed = 0, SpeedRadian = 0 }]
                }
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)), 81, "escort-spatial-before.sav");
            if (nearLeader)
            {
                AssertTimeBlocked(campaign, "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
                continue;
            }
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var born = Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "ZERO_RADAR_HUNTER");
            Assert.False(born.Hunting);
            Assert.True(born.Escorting);
            Assert.Equal(9, born.Destination!.UniqueId);
            Assert.Equal(1.2, born.Longitude);
            Assert.Equal(0.6, born.Latitude);
            campaign = Reload(content, campaign, 81, "escort-spatial-after.sav");
            AdvanceOne(campaign);
            Reload(content, campaign, 81, "escort-spatial-following.sav");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NegativeRadarNewbornsValidateAttractionAndPersistWithoutTargets(bool cache, bool escort)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "selectionRows"), row => row[1].GetInt32() == 8 && row[2].GetInt32() == 0);
        foreach (var radar in new[] { -1, int.MinValue })
            foreach (var mode in new[] { 0, 1 })
            {
                var content = Load(cache, escort, mode, radar: radar);
                var campaign = Reload(content, CampaignState.Restore(Snapshot(content, escort), content,
                    new SplitMix64RandomSource(81)), 81, "negative-radar-before.sav");
                AdvanceUnblocked(campaign, 1);
                var born = Assert.Single(campaign.Capture().World.Ufos, ufo => ufo.RuleId == "ZERO_RADAR_HUNTER");
                Assert.False(born.Hunting);
                Assert.Equal(escort, born.Escorting);
                Assert.Equal(mode, born.HuntMode);
                campaign = Reload(content, campaign, 81, "negative-radar-after.sav");
                AdvanceOne(campaign);
                Reload(content, campaign, 81, "negative-radar-following.sav");
                content = Load(cache, escort, mode, "crafts: [{type: SHIP, soldiers: 2147483647}]\n", radar);
                AssertTimeBlocked(CampaignState.Restore(Snapshot(content, escort), content, new SplitMix64RandomSource(81)),
                    "Craft hunter attraction exceeds the supported range.");
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
            if (escort && row[6].GetString() == "STR_OUT")
            {
                AssertTimeBlocked(campaign, "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
                continue;
            }
            AdvanceUnblocked(campaign, 1);
            var state = Assert.Single(campaign.Capture().Bases[0].Crafts, craft => craft.RuleId == "BIRTH_REFUEL").Logistics!;
            // The servicing oracle ends before the composed ten-minute fuel pass.
            Assert.Equal(row[5].GetInt32(), state.Fuel + (state.Status == "STR_OUT" ? 60 : 0));
            Assert.Equal(row[6].GetString(), state.Status);
            Assert.Equal(row[6].GetString() == "STR_OUT", Assert.Single(campaign.Capture().World.Ufos, ufo => ufo.RuleId == "ZERO_RADAR_HUNTER").Hunting);
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
                Ufos = escort ? [initial.World.Ufos[0] with { Longitude = 0.2, Latitude = 0.1 }] : [],
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
            var airborne = row[4].GetString() == "STR_OUT" || row[6].GetString() == "STR_OUT";
            if (escort && airborne)
            {
                AssertTimeBlocked(campaign, "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
                continue;
            }
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var first = after.Bases[0].Crafts[0].Logistics!;
            var second = after.Bases[0].Crafts[1].Logistics!;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
                { first.Fuel + (first.Status == "STR_OUT" ? 1 : 0), first.Status,
                    second.Fuel + (second.Status == "STR_OUT" ? 1 : 0), second.Status, after.Bases[0].Items.GetValueOrDefault("SUPPLY") });
            var expected = JsonSerializer.SerializeToUtf8Bytes(row.EnumerateArray().Skip(3).Take(5).ToArray());
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
            Assert.Equal(airborne, Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "ZERO_RADAR_HUNTER").Hunting);
            campaign = Reload(content, campaign, 81, "shared-birth-after.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 81, "shared-birth-following.sav");
        }
    }
}
