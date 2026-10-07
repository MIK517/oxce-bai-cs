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

public sealed partial class StrategicWorldAlienBaseGenerationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedGroundEscortsInitializeBeforeScoringAndStayInactiveUntilTakeoff(bool cache, bool hunting)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var leader in new[] { false, true })
        {
            var expected = TestFixtures.Rows(oracle.RootElement, "spawnEscortRows").Single(row =>
                row[0].GetBoolean() && row[1].GetBoolean() && row[2].GetInt32() == (leader ? 3 : 0) &&
                row[3].GetInt32() == 0 && row[12].GetInt32() == 1);
            var content = GeneratedEscortContent(cache, hunting, ground: true, leader: leader);
            var initial = GeneratedEscortSnapshot(content, hunting);
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "generated-ground-escort-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var born = Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "GENERATED_ESCORT");
            Assert.Equal(expected[4].GetBoolean(), born.Escort);
            Assert.Equal(expected[5].GetBoolean(), born.Escorting);
            Assert.Equal(expected[12].GetInt32(), (int)born.Status);
            Assert.False(born.Hunting);
            Assert.Equal(5, born.SecondsRemaining);
            Assert.Equal(leader ? expected[9].GetInt32() : 550, born.Speed);
            Assert.Equal(new WorldPosition(Degrees(leader ? 8 : 10), Degrees(leader ? 2 : 8)), born.Position);
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + (hunting ? 0 : leader ? 9 : 6), after.Regions[0].ActivityAlien[^1]);
            Assert.Equal(leader ? 2 : 1, Assert.Single(after.World.Missions).LiveUfos);
            if (leader)
            {
                var first = after.World.Ufos[0];
                Assert.Equal(first.UniqueId, born.Destination!.UniqueId);
                Assert.Equal(WorldPosition.Create(Degrees(362), Degrees(2)), born.OriginalWaypoint);
            }
            else Assert.Null(born.OriginalWaypoint);
            campaign = Reload(content, campaign, 81, "generated-ground-escort-after.sav");
            AdvanceUnblocked(campaign, 1);
            var lifted = Assert.Single(campaign.Capture().World.Ufos, ufo => ufo.RuleId == "GENERATED_ESCORT");
            Assert.Equal(UfoStatus.Flying, lifted.Status);
            Assert.Equal(550, lifted.Speed);
            Assert.Equal(leader, lifted.Escorting);
            campaign = Reload(content, campaign, 81, "generated-ground-escort-lifted.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 81, "generated-ground-escort-following.sav");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedZeroRadarAirborneEscortsPreserveLeaderOrderAndReload(bool cache, bool hunting)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var leader in new[] { false, true })
        {
            var expected = TestFixtures.Rows(oracle.RootElement, "spawnEscortRows").Single(row =>
                row[0].GetBoolean() && row[1].GetBoolean() && row[2].GetInt32() == (leader ? 3 : 0) &&
                row[3].GetInt32() == 0 && row[12].GetInt32() == 0);
            var content = GeneratedEscortContent(cache, hunting, ground: false, leader: leader);
            var initial = GeneratedEscortSnapshot(content, hunting);
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "generated-air-escort-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var born = Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "GENERATED_ESCORT");
            Assert.Equal(expected[4].GetBoolean(), born.Escort);
            Assert.Equal(expected[5].GetBoolean(), born.Escorting);
            Assert.Equal(expected[12].GetInt32(), (int)born.Status);
            Assert.False(born.Hunting);
            Assert.Equal(leader ? expected[9].GetInt32() : 550, born.Speed);
            if (leader)
            {
                var first = after.World.Ufos[0];
                Assert.Equal(first.UniqueId, born.Destination!.UniqueId);
                // Native move compares the newly moved leader's distance with
                // the step, even when rounding leaves it slightly above one step.
                Assert.Equal(WorldGeometry.Move(new(Degrees(8), Degrees(2)), first.Position,
                    WorldGeometry.RadianSpeed(2200)), born.Position);
                Assert.Equal(WorldPosition.Create(Degrees(362), Degrees(2)), born.OriginalWaypoint);
            }
            else
            {
                Assert.Null(born.OriginalWaypoint);
                Assert.Equal(WorldTargetKind.Waypoint, born.Destination!.Kind);
            }
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + (hunting ? 0 : leader ? 6 : 3), after.Regions[0].ActivityAlien[^1]);
            Assert.Equal(leader ? 2 : 1, Assert.Single(after.World.Missions).LiveUfos);
            campaign = Reload(content, campaign, 81, "generated-air-escort-after.sav");
            AdvanceUnblocked(campaign, 12);
            Reload(content, campaign, 81, "generated-air-escort-following.sav");
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void GeneratedEscortHuntersUseEarlierBirthOriginsForSelectionAndCombatProofs(bool cache, int mode)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var hunting in new[] { false, true })
        {
            var content = GeneratedEscortContent(cache, hunting, ground: false, leader: true, radar: 300, mode: mode);
            var origin = new WorldPosition(Degrees(8), Degrees(2));
            var initial = TargetedGeneratedEscortSnapshot(content, hunting, origin);
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "generated-escort-hunt-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var born = Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "GENERATED_ESCORT");
            var selection = TestFixtures.Rows(oracle.RootElement, "selectionRows").Single(row =>
                row[0].GetInt32() == born.HuntMode && row[1].GetInt32() == 0);
            Assert.True(born.Escort);
            Assert.True(born.Hunting);
            Assert.False(born.Escorting);
            Assert.True(born.Detected);
            Assert.Equal(selection[2].GetInt32(), born.Destination!.Id);
            Assert.Equal(3300, born.Speed);
            Assert.Equal(WorldPosition.Create(Degrees(362), Degrees(2)), born.OriginalWaypoint);
            var target = after.Bases[0].Crafts.Single(craft => craft.Id == born.Destination.Id).Logistics!;
            Assert.Equal(WorldGeometry.Move(origin, new(target.Longitude, target.Latitude), WorldGeometry.RadianSpeed(3300)), born.Position);
            Assert.Single(result.Events.OfType<UfoStartedHunting>());
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + (hunting ? 0 : 6), after.Regions[0].ActivityAlien[^1]);
            campaign = Reload(content, campaign, 81, "generated-escort-hunt-after.sav");
            AdvanceUnblocked(campaign, 12);
            Reload(content, campaign, 81, "generated-escort-hunt-following.sav");
            AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO", int.MaxValue), content,
                new SplitMix64RandomSource(81)), "UFO contact marker IDs are exhausted or collide with a saved UFO.");
            var owner = initial.Bases[0];
            initial = initial with
            {
                Bases = [owner with { Crafts = [owner.Crafts[0] with
            { Logistics = owner.Crafts[0].Logistics! with { Longitude = origin.Longitude, Latitude = origin.Latitude } }] }]
            };
            AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedStochasticSameWaveEscortsKeepEveryConstructorOutcomeSafe(bool cache, bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = GeneratedEscortContent(cache, hunting, ground: false, leader: false, radar: 300,
            mode: 2, percentage: 50, count: 2);
        var initial = TargetedGeneratedEscortSnapshot(content, hunting, new(Degrees(10), Degrees(8)));
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO", int.MaxValue - 1), content,
            new SplitMix64RandomSource(81)), "UFO contact marker IDs are exhausted or collide with a saved UFO.");
        HashSet<(bool First, bool Second)> outcomes = [];
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var campaign = Reload(content, CampaignState.Restore(initial with { RandomState = seed }, content,
                new SplitMix64RandomSource(seed)), seed, "generated-stochastic-escort-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(2, after.World.Ufos.Count);
            outcomes.Add((after.World.Ufos[0].HunterKiller, after.World.Ufos[1].HunterKiller));
            foreach (var born in after.World.Ufos)
            {
                Assert.Equal(born.HunterKiller, born.Hunting);
                Assert.Equal(born.HunterKiller, born.Escort);
                Assert.False(born.Escorting);
                Assert.Equal(born.HunterKiller ? 3300 : 550, born.Speed);
                if (born.HunterKiller) Assert.InRange(born.HuntMode, 0, 1);
            }
            campaign = Reload(content, campaign, seed, "generated-stochastic-escort-after.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, seed, "generated-stochastic-escort-following.sav");
        }
        Assert.Equal(4, outcomes.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedEscortCombatProofRetainsInitializedSpeedWhenHuntDoesNotOverride(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        foreach (var hunting in new[] { false, true })
            foreach (var speed in new[] { 0, -1 })
            {
                Assert.Contains(TestFixtures.Rows(oracle.RootElement, "retargetRows"), row => row[0].GetInt32() == speed && row[1].GetInt32() == 900);
                var content = GeneratedEscortContent(cache, hunting, ground: false, leader: true, radar: 300, huntSpeed: speed);
                var origin = new WorldPosition(Degrees(8), Degrees(2));
                var initial = TargetedGeneratedEscortSnapshot(content, hunting, origin);
                var owner = initial.Bases[0];
                var craft = owner.Crafts[0];
                var close = initial with
                {
                    Bases = [owner with { Crafts = [craft with
                { Logistics = craft.Logistics! with { Longitude = origin.Longitude + WorldGeometry.RadianSpeed(1000) } }] }]
                };
                AssertTimeBlocked(CampaignState.Restore(close, content, new SplitMix64RandomSource(81)),
                    "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
                AdvanceUnblocked(campaign, 1);
                var born = Assert.Single(campaign.Capture().World.Ufos, ufo => ufo.RuleId == "GENERATED_ESCORT");
                Assert.True(born.Hunting);
                Assert.Equal(2200, born.Speed);
                Reload(content, campaign, 81, "generated-no-speed-override.sav");
            }
    }

    private static CampaignSnapshot TargetedGeneratedEscortSnapshot(RuntimeContent content, bool hunting, WorldPosition origin)
    {
        var initial = GeneratedEscortSnapshot(content, hunting);
        var owner = initial.Bases[0];
        var template = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
        CraftSnapshot Craft(int id, string rule) => template with
        {
            Id = id,
            RuleId = rule,
            Logistics = template.Logistics! with
            {
                Fuel = 1000,
                Weapons = [],
                IsAutoPatrolling = false,
                Longitude = origin.Longitude + WorldGeometry.Nautical(100),
                Latitude = origin.Latitude,
            },
        };
        return initial with { Bases = [owner with { Crafts = [Craft(1, "GENERATED_ESCORT_SLOW"), Craft(2, "GENERATED_ESCORT_FAST")], Soldiers = [] }] };
    }

    private static RuntimeContent GeneratedEscortContent(bool cache, bool hunting, bool ground, bool leader,
        int radar = 0, int mode = 0, int percentage = 100, int count = 1, int huntSpeed = 150) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
            crafts:
              - {type: GENERATED_ESCORT_SLOW, speedMax: 2000, damageMax: 100, fuelMax: 1000, soldiers: 0, allowLanding: false, patrolWithoutFuel: true}
              - {type: GENERATED_ESCORT_FAST, speedMax: 6000, damageMax: 100, fuelMax: 1000, soldiers: 0, allowLanding: false, patrolWithoutFuel: true}
            ufos:
              - type: GENERATED_ESCORT
                speedMax: 2200
                damageMax: 50
                radarRange: {{{radar}}}
                huntSpeed: {{{huntSpeed}}}
                huntMode: {{{mode}}}
                hunterKillerPercentage: {{{percentage}}}
                noAlert: true
                missionScore: 3
            ufoTrajectories:
              - id: GENERATED_ESCORT_LEADER_PATH
                waypoints: [[5, 3, 100], [3, 2, 80], [0, 3, 50]]
              - id: GENERATED_ESCORT_PATH
                groundTimer: 2
                waypoints: [[4, {{{(ground ? 0 : 3)}}}, 25], [3, 2, 80], [0, 3, 50]]
            alienDeployments:
              - type: GENERATED_ESCORT_BASE
                markerName: STR_ALIEN_BASE
                genMission: {GENERATED_ESCORT_MISSION: 1}
                genMissionFreq: {{{(hunting ? 0 : 100)}}}
                genMissionLimit: 1
                baseDetectionRange: {{{(hunting ? 500 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: 60
                huntMissionWeights: {0: {GENERATED_ESCORT_MISSION: 1}}
            alienMissions:
              - type: GENERATED_ESCORT_MISSION
                objective: 0
                waves:
                  {{{(leader ? "- {ufo: UFO_SCOUT, count: 1, trajectory: GENERATED_ESCORT_LEADER_PATH, timer: 0}\n      " : "")}}}- {ufo: GENERATED_ESCORT, count: {{{count}}}, trajectory: GENERATED_ESCORT_PATH, timer: 0, escort: true}
            """);

    private static CampaignSnapshot GeneratedEscortSnapshot(RuntimeContent content, bool hunting)
    {
        var initial = HuntingGeneratedWaveSnapshot(content, hunting ? new(1, 1, 1, 1999, 0, 29, 55) : new(1, 1, 1, 1999, 23, 59, 55));
        return initial with
        {
            World = new()
            {
                AlienBases = [new(2, "GENERATED_ESCORT_BASE", "RACE_A", 0.2, 0.1)
                { MinutesSinceLastHuntMissionGeneration = hunting ? 50 : 0 }]
            },
        };
    }
}
