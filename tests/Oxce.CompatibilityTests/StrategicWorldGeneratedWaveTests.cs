using Oxce.Core.Random;
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

    private static RuntimeContent GeneratedWaveContent(bool cache, int timer, string extraRules = "") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-alien-base-generation.rul"], cache,
            $$"""
            alienDeployments:
              - type: GENERATED_WAVE_BASE
                alienBase: true
                markerName: STR_ALIEN_BASE
                genMission: {GENERATED_WAVE: 1}
                genMissionFreq: 100
                genMissionLimit: 1
                genMissionRaceFromAlienBase: true
            alienMissions:
              - type: GENERATED_WAVE
                objective: 0
                waves: [{ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: {{timer}}}]
            {{extraRules}}
            """);

    private static CampaignSnapshot GeneratedWaveSnapshot(RuntimeContent content, ulong seed) =>
        Snapshot(content, "local") with
        {
            RandomState = seed,
            World = new() { AlienBases = [new(2, "GENERATED_WAVE_BASE", "RACE_A", 0.2, 0.1)] },
        };
}
