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
    [Fact]
    public void DailyGeneratedAirborneWavesJoinCreationTickScoringAndSurviveReload()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        foreach (var timer in new[] { 0, 90 })
        {
            var content = GeneratedWaveContent(timer);
            var born = 0;
            var delayed = 0;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var initial = GeneratedWaveSnapshot(content, seed);
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)),
                    seed, "daily-wave-before.sav");
                AdvanceOne(campaign);
                var after = campaign.Capture();
                var mission = Assert.Single(after.World.Missions);
                Assert.True(mission.Id > 0);
                Assert.Equal(1, after.World.AlienBases[0].GenMissionCount);
                Assert.Equal(2, mission.AlienBase!.Id);
                Assert.False(mission.Interrupted);
                Assert.Equal("RACE_A", mission.Race);
                if (after.World.Ufos.Count == 0)
                {
                    delayed++;
                    Assert.Equal((0, 0), (mission.NextWave, mission.LiveUfos));
                    Assert.InRange(mission.SpawnCountdown, 30, 90);
                    Assert.Equal(0, mission.SpawnCountdown % 30);
                    Assert.Equal(initial.Regions[0].ActivityAlien, after.Regions[0].ActivityAlien);
                }
                else
                {
                    born++;
                    var ufo = Assert.Single(after.World.Ufos);
                    Assert.Equal((1, 1), (mission.NextWave, mission.LiveUfos));
                    Assert.Equal(mission.Id, ufo.MissionId);
                    Assert.True(ufo.UniqueId > 0);
                    Assert.Equal(UfoStatus.Flying, ufo.Status);
                    Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 3, after.Regions[0].ActivityAlien[^1]);
                }
                Reload(content, campaign, seed, "daily-wave-after.sav");
            }
            Assert.True(born > 0);
            Assert.Equal(timer == 0, delayed == 0);
        }
    }

    [Fact]
    public void GeneratedWaveIdentityExhaustionStopsBeforeCreationAndDailyMutation()
    {
        var content = GeneratedWaveContent(0);
        var initial = GeneratedWaveSnapshot(content, 71).WithNextId("STR_UFO_UNIQUE", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(71)),
            "UFO unique IDs are exhausted or collide with a saved UFO.");
    }

    [Fact]
    public void HuntingGeneratedAirborneWavesReceiveOneThinkAfterHalfHourScoring()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var content = GeneratedWaveContent(0, hunting: true);
        foreach (var time in new[] { new CampaignTime(1, 1, 1, 1999, 0, 9, 55), new(1, 1, 1, 1999, 0, 29, 55) })
        {
            var initial = HuntingGeneratedWaveSnapshot(content, time);
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
                19, "hunting-wave-before.sav");
            AdvanceOne(campaign);
            var after = campaign.Capture();
            var mission = Assert.Single(after.World.Missions);
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal((1, 1), (mission.NextWave, mission.LiveUfos));
            Assert.Equal(mission.Id, ufo.MissionId);
            Assert.False(ufo.Detected);
            Assert.False(ufo.HunterKiller);
            Assert.Equal(initial.Regions[0].ActivityAlien, after.Regions[0].ActivityAlien);
            Assert.Equal(0, after.World.AlienBases[0].MinutesSinceLastHuntMissionGeneration);
            Assert.Equal(0, after.World.AlienBases[0].GenMissionCount);
            Reload(content, campaign, 19, "hunting-wave-after.sav");
        }
        var exhausted = HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 0, 9, 55))
            .WithNextId("STR_UFO_UNIQUE", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(exhausted, content, new SplitMix64RandomSource(19)),
            "UFO unique IDs are exhausted or collide with a saved UFO.");
    }

    private static CampaignSnapshot HuntingGeneratedWaveSnapshot(RuntimeContent content, CampaignTime time) =>
        (GeneratedWaveSnapshot(content, 19) with { Time = time }).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 100,
                Damage = 0,
                Longitude = 0.2,
                Latitude = 0.1,
                Speed = 0,
                SpeedRadian = 0,
                Destination = null,
                LowFuel = false,
                MissionComplete = false,
            },
        }) with
        {
            World = new()
            {
                AlienBases = [new(2, "GENERATED_WAVE_BASE", "RACE_A", 0.2, 0.1)
                { MinutesSinceLastHuntMissionGeneration = 50 }]
            },
        };

    [Fact]
    public void DailyAndHuntingZeroTimerChainsPreserveCallerOrderAndNewbornInterruptionSemantics()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-wave-chain");
        var expected = TestFixtures.Rows(oracle.RootElement, "cases").Single(row => row.GetProperty("name").GetString() == "zero")
            .GetProperty("rows")[0].EnumerateArray().Take(4).Select(value => value.GetInt32()).ToArray();
        var content = GeneratedWaveContent(0, hunting: true, combined: true, chain: true);
        var races = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 23, 59, 55)) with
            { RandomState = seed, CompletedResearch = ["GEN_STOP"] };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)),
                seed, "composed-wave-before.sav");
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            Assert.Equal(1, Assert.Single(result.Events.OfType<CampaignTimeAdvanced>()).Summary.TickCount);
            var after = campaign.Capture();
            Assert.Equal(2, after.World.Missions.Count);
            var daily = after.World.Missions[0];
            var hunt = after.World.Missions[1];
            Assert.True(daily.Id > 0 && hunt.Id == daily.Id + 1);
            Assert.Equal(10, after.World.Ufos.Count);
            foreach (var mission in after.World.Missions)
            {
                var ufoCount = after.World.Ufos.Count(ufo => ufo.MissionId == mission.Id);
                var actual = new[] { mission.NextWave, mission.NextUfoCounter, mission.SpawnCountdown, ufoCount };
                Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)),
                    CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(actual)));
                Assert.Equal(ufoCount, mission.LiveUfos);
                Assert.False(mission.Interrupted); // The daily interruption handler ran before either birth.
                Assert.Equal("REGION", mission.RegionId); // One missionRegion remap.
                races.Add(mission.Race);
                Assert.All(after.World.Ufos.Where(ufo => ufo.MissionId == mission.Id), ufo => Assert.Equal(UfoStatus.Flying, ufo.Status));
            }
            Assert.All(after.World.Ufos.Take(5), ufo => Assert.Equal(daily.Id, ufo.MissionId));
            Assert.All(after.World.Ufos.Skip(5), ufo => Assert.Equal(hunt.Id, ufo.MissionId));
            Assert.All(after.World.Ufos.Skip(5), ufo => Assert.False(ufo.Detected));
            Assert.All(result.Events.OfType<UfoContactDetected>(), contact => Assert.Contains(after.World.Ufos.Take(5), ufo => ufo.UniqueId == contact.UniqueId));
            Assert.Equal(10, after.World.Ufos.Select(ufo => ufo.UniqueId).Distinct().Count());
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 15, after.Regions[0].ActivityAlien[^1]);
            campaign = Reload(content, campaign, seed, "composed-wave-after.sav");
            AdvanceOne(campaign);
            Assert.Equal(10, campaign.Capture().World.Ufos.Count);
            Reload(content, campaign, seed, "composed-wave-next.sav");
        }
        Assert.True(races.SetEquals(["RACE_A", "RACE_B"]));
    }

    [Fact]
    public void ComposedGeneratedWavesReserveAllIdentitiesAndActivityBeforeMutation()
    {
        var content = GeneratedWaveContent(0, hunting: true, combined: true, chain: true);
        var initial = HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 23, 59, 55));
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO_UNIQUE", int.MaxValue - 9), content,
            new SplitMix64RandomSource(19)), "UFO unique IDs are exhausted or collide with a saved UFO.");
        initial = initial with
        { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 14] }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
            "UFO alien activity exceeds the supported range.");
    }

    [Fact]
    public void HuntingBirthWaveClassificationChargesTheSharedWorkLimit()
    {
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            alienDeployments:
              - type: GUARDED_BASE
                markerName: STR_ALIEN_BASE
                baseDetectionRange: 100
                baseDetectionChance: 100
                huntMissionMaxFrequency: 60
                huntMissionWeights: {0: {GUARDED_WAVES: 1}}
            alienMissions:
              - type: GUARDED_WAVES
                waves:
                  - {count: 1, trajectory: TRAJ_PATROL, timer: 0}
            """ + "\n" + string.Concat(Enumerable.Repeat("      - {count: 1, trajectory: TRAJ_PATROL, timer: 1}\n", 9000)));
        var initial = HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 0, 9, 55));
        var owner = initial.Bases[0];
        var craft = owner.Crafts.Single(value => value.RuleId == "SHIP");
        initial = initial with
        {
            Bases = [owner with { Soldiers = [], Crafts = [.. Enumerable.Range(1, 120).Select(id => craft with { Id = id })] }],
            World = initial.World with { AlienBases = [initial.World.AlienBases[0] with { DeploymentId = "GUARDED_BASE" }] },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
            "Alien base activity and sight checks exceed the supported work limit.");
    }

    [Fact]
    public void DailyGeneratedWaveCannotResolveASharedSourceMarkerIdentity()
    {
        var content = GeneratedWaveContent(0);
        var initial = GeneratedWaveSnapshot(content, 19);
        initial = initial with
        {
            World = initial.World with
            {
                AlienBases = [new(2, "GEN_BASE_exhausted", "RACE_A", 0.4, 0.1) { GenMissionCount = 3 },
                    initial.World.AlienBases[0]],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
            "Immediate generated mission source identity is ambiguous.");
    }

    [Fact]
    public void DailyGeneratedGroundUfoUsesNativeTimerScoringAndLaterTakeoff()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var row = TestFixtures.Rows(oracle.RootElement, "spawnPathRows").Single(value =>
            value[0].GetInt32() == 0 && value[1].GetInt32() == 1 && value[2].GetInt32() == 0);
        var content = GeneratedWaveContent(0, groundTimer: 2);
        var initial = GeneratedWaveSnapshot(content, 19);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
            19, "daily-ground-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        var actual = new[] { (int)ufo.Status, ufo.SecondsRemaining + 5, ufo.Speed };
        var expected = row.EnumerateArray().Skip(3).Take(3).Select(value => value.GetInt32()).ToArray();
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(expected)),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(actual)));
        Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 6, after.Regions[0].ActivityAlien[^1]);
        Assert.Equal(1, Assert.Single(after.World.Missions).LiveUfos);
        campaign = Reload(content, campaign, 19, "daily-ground-after.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(UfoStatus.Flying, Assert.Single(campaign.Capture().World.Ufos).Status);
        Reload(content, campaign, 19, "daily-ground-takeoff.sav");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void GroundHuntingBirthsStayInactiveUntilTheirLaterTakeoff(int percentage)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        foreach (var hunting in new[] { false, true })
        {
            var content = GeneratedWaveContent(0, hunting, groundTimer: 2, hunterPercentage: percentage);
            var initial = hunting ? HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 0, 29, 55))
                : GeneratedWaveSnapshot(content, 19);
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
                19, "ground-hunter-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal(UfoStatus.Landed, ufo.Status);
            Assert.Equal(5, ufo.SecondsRemaining);
            Assert.Equal(percentage > 0, ufo.HunterKiller);
            Assert.False(ufo.Hunting);
            Assert.False(ufo.Escorting);
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + (hunting ? 0 : 6), after.Regions[0].ActivityAlien[^1]);
            if (hunting) Assert.False(ufo.Detected);
            campaign = Reload(content, campaign, 19, "ground-hunter-after.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(UfoStatus.Flying, Assert.Single(campaign.Capture().World.Ufos).Status);
            Reload(content, campaign, 19, "ground-hunter-takeoff.sav");
        }
    }

    [Fact]
    public void GeneratedNonObjectiveSiteWavesComposeForSeventyTwoHoursWithReloads()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        foreach (var ground in new[] { 0, 2 })
        {
            var content = GeneratedWaveContent(0, hunting: true, combined: true, groundTimer: ground, site: true);
            var initial = HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 23, 59, 55));
            initial = initial with
            {
                World = initial.World with
                {
                    AlienBases = [initial.World.AlienBases[0] with { MinutesSinceLastHuntMissionGeneration = 5390 }],
                },
            };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
                19, "ordinary-site-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(2, after.World.Missions.Count);
            Assert.Equal(2, after.World.Ufos.Count);
            Assert.Empty(after.World.MissionSites);
            Assert.All(after.World.Missions, mission =>
            {
                Assert.InRange(mission.MissionSiteZoneArea, 0, 1);
                Assert.Equal((1, 1), (mission.NextWave, mission.LiveUfos));
                Assert.InRange(mission.SpawnCountdown, 4500, 13500);
            });
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + (ground == 0 ? 3 : 6), after.Regions[0].ActivityAlien[^1]);
            Assert.False(after.World.Ufos[1].Detected);
            campaign = Reload(content, campaign, 19, "ordinary-site-birth.sav");
            for (var day = 0; day < 3; day++)
            {
                AdvanceUnblocked(campaign, 17_280);
                campaign = Reload(content, campaign, 19, $"ordinary-site-day-{day}.sav");
            }
            var end = campaign.Capture();
            Assert.Equal(5, end.Time.Day);
            Assert.Empty(end.World.MissionSites);
            Assert.Equal(2, end.World.Missions.Count);
            Assert.All(end.World.Missions, mission => Assert.Equal(1, mission.NextWave));
            Assert.Equal(1, end.World.AlienBases[0].GenMissionCount);
        }
    }

    [Fact]
    public void GeneratedSiteObjectiveFollowUpRetainsAnAtomicCreationBoundary()
    {
        foreach (var hunting in new[] { false, true })
        {
            var content = GeneratedWaveContent(0, hunting, site: true, siteObjectiveTimer: 0, siteConversion: true);
            var initial = hunting ? HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 0, 29, 55))
                : GeneratedWaveSnapshot(content, 19);
            if (hunting) initial = initial with
            {
                World = initial.World with { AlienBases = [initial.World.AlienBases[0] with { MinutesSinceLastHuntMissionGeneration = 5390 }] },
            };
            AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
                "Immediate generated mission waves require creation-tick lifecycle forecasts.");
        }
    }

    private static RuntimeContent GeneratedWaveContent(int timer, bool hunting = false, bool combined = false, bool chain = false, int groundTimer = 0, int hunterPercentage = 0, bool site = false, int siteObjectiveTimer = 9000, int missionPoints = 0, bool siteConversion = false) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-generation.rul"], $$$"""
            alienDeployments:
              - type: GENERATED_WAVE_BASE
                alienBase: true
                markerName: STR_ALIEN_BASE
                genMission: {GENERATED_WAVE: 1}
                genMissionFreq: {{{(hunting && !combined ? 0 : 100)}}}
                genMissionLimit: 1
                genMissionRaceFromAlienBase: {{{(!chain).ToString().ToLowerInvariant()}}}
                baseDetectionRange: {{{(hunting ? 100 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: {{{(site ? 5400 : 60)}}}
                huntMissionRaceFromAlienBase: {{{(!chain).ToString().ToLowerInvariant()}}}
                huntMissionWeights: {0: {GENERATED_WAVE: 1}}
            alienMissions:
              - type: GENERATED_WAVE
                objective: {{{(site ? 3 : 0)}}}
                points: {{{missionPoints}}}
                spawnZone: 1
                interruptResearch: GEN_STOP
                {{{(chain ? "regionWeights: {0: {REGION_SECOND: 1}}\n    raceWeights: {0: {RACE_A: 1, RACE_B: 1}}" : "")}}}
                waves: {{{GeneratedWaves(timer, chain, groundTimer, hunterPercentage, site, siteObjectiveTimer, siteConversion)}}}
            ufoTrajectories:
              - {id: GENERATED_SITE_CONVERSION, groundTimer: 6, waypoints: [[1, 3, 100], [1, 0, 60], [0, 4, 100]]}
              - id: GENERATED_GROUND_PATH
                groundTimer: {{{groundTimer}}}
                waypoints: [[4, 0, 100], [5, 2, 80], [0, 3, 50]]
            crafts:
              - type: SHIP
                sightRange: 0
            """);

    private static string GeneratedWaves(int timer, bool chain, int groundTimer, int hunterPercentage, bool site, int siteObjectiveTimer, bool siteConversion) => site
        ? $"[{{ufo: UFO_SCOUT, count: 1, trajectory: {(groundTimer != 0 ? "GENERATED_GROUND_PATH" : "TRAJ_SPAWN_FIXED")}, timer: {timer}}}, {{ufo: UFO_SCOUT, count: 1, trajectory: {(siteConversion ? "GENERATED_SITE_CONVERSION" : "TRAJ_SITE")}, timer: {siteObjectiveTimer}, objective: true}}]"
        : chain
        ? "[{ufo: UFO_SCOUT, count: 2, trajectory: TRAJ_SPAWN_FIXED, timer: 0}, {ufo: UFO_SCOUT, count: 3, trajectory: TRAJ_SPAWN_FIXED, timer: 0}]"
        : $"[{{ufo: UFO_SCOUT, count: 1, trajectory: {(groundTimer != 0 ? "GENERATED_GROUND_PATH" : "TRAJ_SPAWN_FIXED")}, timer: {timer}, hunterKillerPercentage: {hunterPercentage}}}]";

    private static CampaignSnapshot GeneratedWaveSnapshot(RuntimeContent content, ulong seed) =>
        Snapshot(content, "local") with
        {
            RandomState = seed,
            World = new() { AlienBases = [new(2, "GENERATED_WAVE_BASE", "RACE_A", 0.2, 0.1)] },
        };
}
