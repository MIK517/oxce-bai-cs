using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldAlienBaseGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedFutureRaceTimelineUsesNativeEmptyRaceAndReloadRepair(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        var race = TestFixtures.Rows(oracle.RootElement, "rows").Single(row => row[0].GetString() == "futureRace")[3][2].GetString();
        Assert.Equal(string.Empty, race);
        foreach (var hunting in new[] { false, true })
        {
            var content = GeneratedVariantContent(cache, "{2: {RACE_B: 1}}", hunting: hunting);
            var initial = VariantSnapshot(content, hunting);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(19));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var mission = Assert.Single(after.World.Missions);
            Assert.Equal(race, mission.Race);
            Assert.False(mission.Interrupted);
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal(2200, ufo.Speed);
            var loaded = TestFixtures.LoadLogisticsSave(OxceSaveAdapter.EmitNewCampaign(after), content, 19,
                "generated-empty-race.sav").Campaign;
            var repaired = Assert.Single(loaded.Capture().World.Missions);
            Assert.Equal("RACE_A", repaired.Race);
            Assert.True(repaired.Interrupted);
            Assert.Equivalent(after with { World = after.World with { Missions = [mission with { Race = "RACE_A", Interrupted = true }] } },
                loaded.Capture(), strict: true);
            AdvanceUnblocked(loaded, 1);
            Assert.Single(loaded.Capture().World.Ufos);
            Reload(content, loaded, 19, "generated-repaired-race.sav");
        }
    }

    private static RuntimeContent GeneratedVariantContent(bool cache, string races, int targetOdds = 0, bool hunting = false) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache,
            $$$"""
            alienDeployments:
              - type: VARIANT_BASE
                alienBase: true
                markerName: STR_ALIEN_BASE
                genMission: {VARIANT_MISSION: 1}
                genMissionFreq: {{{(hunting ? 0 : 100)}}}
                genMissionLimit: 1
                genMissionRaceFromAlienBase: false
                baseDetectionRange: {{{(hunting ? 100 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: 60
                huntMissionRaceFromAlienBase: false
                huntMissionWeights: {0: {VARIANT_MISSION: 1}}
            alienMissions:
              - type: VARIANT_MISSION
                objective: 0
                targetBaseOdds: {{{targetOdds}}}
                raceWeights: {{{races}}}
                waves: [{ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0}]
            """);

    private static CampaignSnapshot VariantSnapshot(RuntimeContent content, bool hunting) =>
        (hunting ? HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 0, 29, 55)) : GeneratedWaveSnapshot(content, 19)) with
        {
            World = new()
            {
                AlienBases = [new(2, "VARIANT_BASE", "RACE_A", 0.2, 0.1)
                { MinutesSinceLastHuntMissionGeneration = 50 }]
            },
        };
}
