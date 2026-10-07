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
    [InlineData(false)]
    [InlineData(true)]
    public void DailyGeneratedAirborneWavesJoinCreationTickScoringAndSurviveReload(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        foreach (var timer in new[] { 0, 90 })
        {
            var content = GeneratedWaveContent(cache, timer);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedWaveIdentityExhaustionStopsBeforeCreationAndDailyMutation(bool cache)
    {
        var content = GeneratedWaveContent(cache, 0);
        var initial = GeneratedWaveSnapshot(content, 71).WithNextId("STR_UFO_UNIQUE", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(71)),
            "UFO unique IDs are exhausted or collide with a saved UFO.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HuntingGeneratedAirborneWavesReceiveOneThinkAfterHalfHourScoring(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world");
        var content = GeneratedWaveContent(cache, 0, hunting: true);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyAndHuntingZeroTimerChainsPreserveCallerOrderAndNewbornInterruptionSemantics(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-wave-chain");
        var expected = TestFixtures.Rows(oracle.RootElement, "cases").Single(row => row.GetProperty("name").GetString() == "zero")
            .GetProperty("rows")[0].EnumerateArray().Take(4).Select(value => value.GetInt32()).ToArray();
        var content = GeneratedWaveContent(cache, 0, hunting: true, combined: true, chain: true);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposedGeneratedWavesReserveAllIdentitiesAndActivityBeforeMutation(bool cache)
    {
        var content = GeneratedWaveContent(cache, 0, hunting: true, combined: true, chain: true);
        var initial = HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 23, 59, 55));
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO_UNIQUE", int.MaxValue - 9), content,
            new SplitMix64RandomSource(19)), "UFO unique IDs are exhausted or collide with a saved UFO.");
        initial = initial with
        { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 14] }] };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
            "UFO alien activity exceeds the supported range.");
    }

    private static RuntimeContent GeneratedWaveContent(bool cache, int timer, bool hunting = false, bool combined = false, bool chain = false) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-generation.rul"], cache,
            $$$"""
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
                huntMissionMaxFrequency: 60
                huntMissionRaceFromAlienBase: {{{(!chain).ToString().ToLowerInvariant()}}}
                huntMissionWeights: {0: {GENERATED_WAVE: 1}}
            alienMissions:
              - type: GENERATED_WAVE
                objective: 0
                interruptResearch: GEN_STOP
                {{{(chain ? "regionWeights: {0: {REGION_SECOND: 1}}\n    raceWeights: {0: {RACE_A: 1, RACE_B: 1}}" : "")}}}
                waves: {{{(chain ? "[{ufo: UFO_SCOUT, count: 2, trajectory: TRAJ_SPAWN_FIXED, timer: 0}, {ufo: UFO_SCOUT, count: 3, trajectory: TRAJ_SPAWN_FIXED, timer: 0}]" : $"[{{ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: {timer}}}]")}}}
            crafts:
              - type: SHIP
                sightRange: 0
            """);

    private static CampaignSnapshot GeneratedWaveSnapshot(RuntimeContent content, ulong seed) =>
        Snapshot(content, "local") with
        {
            RandomState = seed,
            World = new() { AlienBases = [new(2, "GENERATED_WAVE_BASE", "RACE_A", 0.2, 0.1)] },
        };
}
