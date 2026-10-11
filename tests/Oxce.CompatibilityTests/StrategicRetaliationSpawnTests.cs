using System.Text;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Oxce.FixtureSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicRetaliationSpawnTests
{
    [Fact]
    public void RetaliationTargetSelectionMatchesPinnedReference()
    {
        var content = Load();
        var region = content.RuntimeRules.Regions[content.RuntimeRules.Regions.GetRequired("REGION")].Value with { Areas = [new(0, 1, -0.5, 0.5)] };
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-retaliation-target");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var objective = row[0].GetInt32();
            var multi = row[1].GetInt32();
            var skip = row[2].GetInt32();
            var mask = row[3].GetInt32();
            var owners = Enumerable.Range(1, 3).Select(id => (Id: id, Point: new WorldPosition(id == 3 ? 2 : id * 0.2, 0.1), Known: (mask & (1 << (id - 1))) != 0)).ToArray();
            var candidates = WorldRetaliation.Candidates(owners, region, (RuntimeMissionObjective)objective,
                skip != 0, multi != 0, static owner => owner.Point, static owner => owner.Known);
            var random = new TestFixtures.CountingMinimumRandomSource();
            var index = WorldRetaliation.SelectIndex(candidates.Count, random);
            var selected = index is { } value ? candidates[value].Id : 0;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new[] { objective, multi, skip, mask, selected, random.IntegerCalls });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }

    [Fact]
    public void RetaliationWithoutDiscoveredBasesFallsThroughToOrdinaryScouting()
    {
        var content = Load("""
            alienMissions:
              - type: RETAL_SCOUT
                objective: 4
                waves: [{ufo: UFO_SPAWN, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 60}]
              - type: RETAL_EMPTY
                objective: 4
                waves: [{ufo: UNDEFINED, count: 1, trajectory: TRAJ_PATROL, timer: 60}]
            """);
        var initial = Initial(content, "RETAL_SCOUT");
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        Assert.Equal("UFO_SPAWN", ufo.RuleId);
        Assert.Equal("TRAJ_SPAWN_FIXED", ufo.TrajectoryId);
        Assert.False(ufo.HunterKiller);
        Assert.Equal(1, Assert.Single(campaign.Capture().World.Missions).LiveUfos);
        Reload(content, campaign, 17, "retaliation-scouting.sav");
        initial = Initial(content, "RETAL_EMPTY");
        campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Equal(initial.RandomState, campaign.Capture().RandomState);
        Reload(content, campaign, 17, "retaliation-empty-wave.sav");
    }

    [Fact]
    public void NoRegionalBaseInterruptsInstantRetaliationAfterOneWaveAndPersistsWithLiveUfo()
    {
        var content = Load("""
            regions:
              - type: RETAL_EMPTY_REGION
                areas: [[100, 110, -10, 10]]
                missionZones: [[[8, 8, 2, 2]], [[12, 12, 2, 2]]]
            alienMissions:
              - type: RETAL_INSTANT
                objective: 6
                waves: [{ufo: UFO_SPAWN, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0}, {ufo: UFO_SPAWN, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0}]
            """);
        var initial = Initial(content, "RETAL_INSTANT");
        initial = initial with
        {
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { RegionId = "RETAL_EMPTY_REGION", LiveUfos = 1 }],
                Ufos = [new(9, "UFO_SCOUT", 4, "TRAJ_PATROL", 0, 0.4, 0.2, UfoStatus.Flying, WorldAltitudes.High)
                {
                    Shield = 0, MissionWaveNumber = 0,
                    Destination = WorldTargetReference.ForWaypoint(0, new(1.0, 0.6)),
                }],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var mission = Assert.Single(after.World.Missions);
        Assert.True(mission.Interrupted);
        Assert.Equal((1, 0, 1), (mission.NextWave, mission.NextUfoCounter, mission.LiveUfos));
        Assert.Single(after.World.Ufos);
        var expected = new SplitMix64RandomSource(17);
        expected.NextInclusive(0, 0);
        Assert.Equal(expected.State, after.RandomState);
        Reload(content, campaign, 17, "instant-retaliation-interrupted.sav");
        initial = initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { LiveUfos = 0 }], Ufos = [] } };
        campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Empty(campaign.Capture().World.Ufos);
        Reload(content, campaign, 17, "instant-retaliation-expired.sav");
        initial = initial with { Bases = [initial.Bases[0] with { RetaliationMissionId = 4 }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "A non-retaliation mission cannot expire while a base links it as retaliation.");
    }

    [Fact]
    public void SafeAssaultSpawningMatchesReferenceSourceTargetAndConstructor()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-retaliation-spawn");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows").Where(row =>
            row[0].GetInt32() == 4 && row[1].GetInt32() == 0 && row[4].GetInt32() == 0 &&
            row[8].GetInt32() == 1))
        {
            var skip = row[2].GetInt32() != 0;
            var mask = row[3].GetInt32();
            var linked = row[5].GetInt32() != 0;
            var replace = row[7].GetInt32() != 0;
            var content = Load($$$"""
                regions:
                  - type: RETAL_REGION
                    areas: [[0, 57.29577951308232, -28, 28]]
                    missionZones: [[[114.59155902616465, 114.59155902616465, 5.729577951308233, 5.729577951308233]]]
                ufos:
                  - type: RETAL_SHIP
                    speedMax: 100
                    damageMax: 50
                    noAlert: true
                    missionScore: 0
                    hunterKillerPercentage: 100
                  - type: RETAL_OVERRIDE
                    speedMax: 200
                    damageMax: 50
                    noAlert: true
                    missionScore: 0
                    hunterKillerPercentage: 100
                ufoTrajectories:
                  - id: RETAL_ORIGIN
                    waypoints: [[0, {{{(row[6].GetInt32() == 0 ? 3 : 0)}}}, 100], [0, 2, 100]]
                alienMissions:
                  - type: RETAL_ASSAULT
                    objective: 4
                    operationType: 1
                    skipScoutingPhase: {{{skip.ToString().ToLowerInvariant()}}}
                    spawnUfo: {{{(replace ? "RETAL_OVERRIDE" : "")}}}
                    waves: [{ufo: RETAL_SHIP, count: 1, trajectory: RETAL_ORIGIN, timer: 60, hunterKillerPercentage: 100, huntMode: 1, huntBehavior: 1, escort: true, objective: true}]
                """);
            var initial = Initial(content, "RETAL_ASSAULT");
            initial = initial with
            {
                Bases = [initial.Bases[0] with { RetaliationTarget = (mask & 1) != 0, Facilities = [] },
                    initial.Bases[0] with { Id = 2, Name = "Beta", Longitude = 0.4, RetaliationTarget = (mask & 2) != 0, Crafts = [], Soldiers = [], Facilities = [] }],
                World = initial.World with
                {
                    Missions = [initial.World.Missions[0] with { RegionId = "RETAL_REGION", MissionSiteZoneArea = 99,
                        AlienBase = linked ? new(WorldTargetKind.AlienBase, "STR_ALIEN_BASE", 7, 2.4, 0.1) : null }],
                    AlienBases = linked ? [new(7, "ALIEN_BASE_DEPLOY", "RACE_A", 2.4, 0.1)] : [],
                },
            };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(campaign, 1);
            var ufo = Assert.Single(campaign.Capture().World.Ufos);
            Assert.Equal(replace ? "RETAL_OVERRIDE" : "RETAL_SHIP", ufo.RuleId);
            Assert.Equal(RuntimeUfoTrajectoryRule.RetaliationAssaultRun, ufo.TrajectoryId);
            Assert.False(ufo.HunterKiller);
            Assert.False(ufo.Escorting);
            Assert.Equal((0, 0), (ufo.HuntMode, ufo.HuntBehavior));
            Assert.Equal(row[17].GetInt32(), ufo.Speed);
            var origin = new WorldPosition(row[13].GetDouble(), row[14].GetDouble());
            var target = new WorldPosition(row[15].GetDouble(), row[16].GetDouble());
            Assert.Equal(target, ufo.Destination!.Position);
            var moved = WorldGeometry.Move(origin, target, WorldGeometry.RadianSpeed(ufo.Speed));
            Assert.Equal(moved.Longitude, ufo.Longitude, 12);
            Assert.Equal(moved.Latitude, ufo.Latitude, 12);
            var expected = new SplitMix64RandomSource(17);
            if (!linked) { expected.NextInclusive(0, 0); expected.NextUnit(); expected.NextUnit(); }
            // The following half-hour detection pass rolls even for each zero-chance base.
            expected.NextInclusive(0, 99); expected.NextInclusive(0, 99);
            Assert.Equal(expected.State, campaign.Capture().RandomState);
            Reload(content, campaign, 17, "retaliation-assault.sav");
            AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO_UNIQUE", int.MaxValue), content, new SplitMix64RandomSource(17)),
                "UFO unique IDs are exhausted or collide with a saved UFO.");
        }
    }

    [Fact]
    public void GroundAssaultOriginUsesBoundedLandSearchAndIgnoresSelectedSiteFlags()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-retaliation-spawn");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "rows"), row => row[0].GetInt32() == 4 && row[6].GetInt32() == 1 && row[8].GetInt32() == 1);
        var content = Load("""
            regions:
              - type: RETAL_LAND_REGION
                areas: [[0, 60, -30, 30]]
                missionZones: [[[100, 101, 5, 6]]]
            ufos:
              - type: RETAL_LAND_SHIP
                speedMax: 100
                damageMax: 50
                fakeWaterLandingChance: 100
                noAlert: true
                missionScore: 0
            ufoTrajectories:
              - id: RETAL_LAND_ORIGIN
                waypoints: [[0, 0, 100], [0, 3, 100]]
            alienMissions:
              - type: RETAL_LAND
                objective: 4
                skipScoutingPhase: true
                waves: [{ufo: RETAL_LAND_SHIP, count: 1, trajectory: RETAL_LAND_ORIGIN, timer: 60, objective: true, objectiveOnTheLandingSite: true}]
            """);
        var initial = Initial(content, "RETAL_LAND");
        initial = initial with
        {
            Bases = [initial.Bases[0] with { Facilities = [] }],
            World = initial.World with { Missions = [initial.World.Missions[0] with { RegionId = "RETAL_LAND_REGION", MissionSiteZoneArea = 99 }] },
        };
        var expected = new SplitMix64RandomSource(17);
        expected.NextInclusive(0, 99); // fake-water preference
        WorldPosition origin = default;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            expected.NextInclusive(0, 0);
            origin = new(Degrees(100 + expected.NextUnit()), Degrees(5 + expected.NextUnit()));
        }
        expected.NextInclusive(0, 99); // base radar detection
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var ufo = Assert.Single(campaign.Capture().World.Ufos);
        var moved = WorldGeometry.Move(origin, new(0.2, 0.1), WorldGeometry.RadianSpeed(100));
        Assert.Equal(moved.Longitude, ufo.Longitude, 12);
        Assert.Equal(moved.Latitude, ufo.Latitude, 12);
        Assert.Equal(UfoStatus.Flying, ufo.Status);
        Assert.Equal(expected.State, campaign.Capture().RandomState);
        Reload(content, campaign, 17, "retaliation-ground-origin.sav");
    }

    [Fact]
    public void RetaliationDeploymentFallbackFollowsSpawnAndSiteCallerOrdering()
    {
        using var spawn = TestFixtures.ReadVerifiedExpected("strategic-retaliation-spawn");
        using var sites = TestFixtures.ReadVerifiedExpected("strategic-wave-chain");
        foreach (var objective in new[] { 4, 6 })
            foreach (var targetBase in new[] { false, true })
            {
                Assert.Contains(TestFixtures.Rows(spawn.RootElement, "rows"), row => row[0].GetInt32() == objective && row[4].GetInt32() == 1 && row[8].GetInt32() == 0);
                Assert.Contains(TestFixtures.Rows(sites.RootElement, "siteRows"), row => row[0].GetInt32() == 1 && row[1].GetInt32() == 1 && row[2].GetInt32() == objective && row[4].GetInt32() == 1);
                var content = Load($$$"""
                    regions:
                      - type: RETAL_SITE_REGION
                        areas: [[100, 110, -10, 10]]
                        missionZones: [[[8, 8, 2, 2, 10, RETAL_CITY]]]
                    alienDeployments:
                      - type: RETAL_SITE
                        markerName: RETAL_MARKER
                        duration: [2, 2]
                        points: 0
                    ufoTrajectories:
                      - id: RETAL_SITE_ORIGIN
                        waypoints: [[0, 3, 100], [0, 2, 100]]
                    alienMissions:
                      - type: RETAL_SITE_MISSION
                        objective: {{{objective}}}
                        waves: [{ufo: RETAL_SITE, count: 2, trajectory: RETAL_SITE_ORIGIN, timer: 0, objectiveOnXcomBase: {{{targetBase.ToString().ToLowerInvariant()}}}}]
                    """);
                var initial = Initial(content, "RETAL_SITE_MISSION");
                initial = initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { RegionId = "RETAL_SITE_REGION" }] } };
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                Assert.Empty(after.World.Ufos);
                Assert.Empty(after.World.Missions);
                var count = targetBase ? 0 : objective == 6 ? 1 : 2;
                Assert.Equal(count, after.World.MissionSites.Count);
                Assert.All(after.World.MissionSites, site =>
                {
                    Assert.Equal("RETAL_SITE_MISSION", site.MissionRuleId);
                    Assert.Equal("RETAL_CITY", site.City);
                    Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), site.Position);
                    Assert.Equal(5400, site.SecondsRemaining);
                });
                Reload(content, campaign, 17, "retaliation-direct-sites.sav");
                if (!targetBase)
                    AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("RETAL_MARKER", int.MaxValue), content, new SplitMix64RandomSource(17)),
                        "Mission site marker IDs are exhausted or collide with a saved target.");
            }
    }

    private static CampaignSnapshot Initial(RuntimeContent content, string rule)
    {
        var initial = CreateSnapshot(content, "Retaliation spawning");
        return initial with
        {
            RandomState = 17,
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            Bases = initial.Bases.Select(owner => owner with { RetaliationTarget = false }).ToArray(),
            World = initial.World with { Missions = [new(4, rule, "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
        };
    }

    private static RuntimeContent Load(string extra = "") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], extra);
}
