using Oxce.Core.Random;
using System.Text.Json;
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
    [InlineData(false, 1, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 2, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    [InlineData(false, 2, true)]
    [InlineData(true, 2, true)]
    public void GeneratedObjectiveOpeningPreservesPendingCompletion(bool cache, int objective, bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        var content = GeneratedObjectiveOpeningContent(cache, objective, hunting);
        var initial = GeneratedObjectiveOpeningSnapshot(content, hunting);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)),
            41, "generated-objective-prefix-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var mission = Assert.Single(after.World.Missions);
        var ufo = Assert.Single(after.World.Ufos);
        Assert.Equal((1, 1), (mission.NextWave, mission.LiveUfos));
        Assert.Equal(mission.Id, ufo.MissionId);
        Assert.Equal(0, ufo.MissionWaveNumber);
        Assert.Equal(hunting ? 0 : 3, after.Regions[0].ActivityAlien[^1] - initial.Regions[0].ActivityAlien[^1]);
        Assert.Single(after.World.AlienBases);
        Assert.All(after.Countries, country => Assert.False(country.NewPact));
        campaign = Reload(content, campaign, 41, "generated-objective-prefix-born.sav");
        for (var day = 0; day < 3; day++)
        {
            AdvanceUnblocked(campaign, 17_280);
            campaign = Reload(content, campaign, 41, $"generated-objective-prefix-day-{day}.sav");
        }
        after = campaign.Capture();
        Assert.Empty(after.World.Ufos);
        Assert.Equal((1, 0), (Assert.Single(after.World.Missions).NextWave, after.World.Missions[0].LiveUfos));
        Assert.Single(after.World.AlienBases);
        Assert.All(after.Countries, country => Assert.False(country.NewPact));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void DailyGeneratedFinalBaseWaveCreatesBaseBeforeUfoLifecycle(bool cache, int groundTimer)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = GeneratedObjectiveOpeningContent(cache, 2, groundTimer: groundTimer, prefix: false);
        var initial = GeneratedObjectiveOpeningSnapshot(content).WithNextId("STR_ALIEN_BASE", 7);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)),
            41, "generated-final-base-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var born = Assert.Single(after.World.AlienBases, alienBase => alienBase.Id == 7);
        Assert.Equal("OBJECTIVE_OPENING_DESTINATION", born.DeploymentId);
        Assert.Equal("RACE_A", born.Race);
        Assert.Equal(8, after.NextIds["STR_ALIEN_BASE"]);
        Assert.Equal(UfoStatus.Flying, Assert.Single(after.World.Ufos).Status);
        Assert.Equal((1, 1), (Assert.Single(after.World.Missions).NextWave, after.World.Missions[0].LiveUfos));
        var points = groundTimer == 0 ? 10 : 13; // Base completion plus creation-stage UFO activity; no base-mission takeoff award.
        Assert.Equal(points, after.Regions[0].ActivityAlien[^1] - initial.Regions[0].ActivityAlien[^1]);
        campaign = Reload(content, campaign, 41, "generated-final-base-born.sav");
        for (var day = 0; day < 3; day++)
        {
            AdvanceUnblocked(campaign, 17_280);
            campaign = Reload(content, campaign, 41, $"generated-final-base-day-{day}.sav");
        }
        Assert.Equal(2, campaign.Capture().World.AlienBases.Count);
        Assert.Empty(campaign.Capture().World.Ufos);
        Assert.Empty(campaign.Capture().World.Missions);
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_ALIEN_BASE", int.MaxValue), content,
            new SplitMix64RandomSource(41)), "Base objective IDs are exhausted or collide.");
        var atCapacity = initial with
        { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - points] }, .. initial.Regions.Skip(1)] };
        campaign = CampaignState.Restore(atCapacity, content, new SplitMix64RandomSource(41));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(int.MaxValue, campaign.Capture().Regions[0].ActivityAlien[^1]);
        Reload(content, campaign, 41, "generated-final-base-score-capacity.sav");
        AssertTimeBlocked(CampaignState.Restore(atCapacity with
        { Regions = [atCapacity.Regions[0] with { ActivityAlien = [int.MaxValue - points + 1] }, .. atCapacity.Regions.Skip(1)] }, content,
            new SplitMix64RandomSource(41)), "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedFinalBaseRaceAlternativesShareOneBirthReservation(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = GeneratedObjectiveOpeningContent(cache, 2, prefix: false, raceAlternatives: true);
        HashSet<string> races = [];
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = GeneratedObjectiveOpeningSnapshot(content) with { RandomState = seed };
            initial = initial.WithNextId("STR_ALIEN_BASE", int.MaxValue - 1) with
            { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 10] }, .. initial.Regions.Skip(1)] };
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var born = Assert.Single(campaign.Capture().World.AlienBases, alienBase => alienBase.Id == int.MaxValue - 1);
            races.Add(born.Race);
            Assert.Equal(int.MaxValue, campaign.Capture().Regions[0].ActivityAlien[^1]);
            Assert.Equal(int.MaxValue, campaign.Capture().NextIds["STR_ALIEN_BASE"]);
            Reload(content, campaign, seed, "generated-alternative-base-capacity.sav");
            AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_ALIEN_BASE", int.MaxValue), content,
                new SplitMix64RandomSource(seed)), "Base objective IDs are exhausted or collide.");
            var twoSources = initial.WithNextId("STR_ALIEN_BASE", 7) with
            {
                World = initial.World with
                { AlienBases = [initial.World.AlienBases[0], new(3, "OBJECTIVE_OPENING_SOURCE", "RACE_A", 0.2, 0.1)] },
            };
            AssertTimeBlocked(CampaignState.Restore(twoSources, content, new SplitMix64RandomSource(seed)),
                "UFO alien activity exceeds the supported range.");
            twoSources = twoSources with
            { Regions = [twoSources.Regions[0] with { ActivityAlien = [int.MaxValue - 20] }, .. twoSources.Regions.Skip(1)] };
            campaign = CampaignState.Restore(twoSources, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(4, campaign.Capture().World.AlienBases.Count);
            Assert.Equal(int.MaxValue, campaign.Capture().Regions[0].ActivityAlien[^1]);
            Reload(content, campaign, seed, "generated-two-base-sources.sav");
        }
        Assert.Equal(2, races.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedGeneratedFinalObjectivesStopBeforeTheirImmediatePrefixes(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        foreach (var hunting in new[] { false, true })
            foreach (var objective in new[] { 1 })
            {
                var content = GeneratedObjectiveOpeningContent(cache, objective, hunting, finalImmediate: true);
                AssertTimeBlocked(CampaignState.Restore(GeneratedObjectiveOpeningSnapshot(content, hunting), content,
                    new SplitMix64RandomSource(41)), "Immediate generated objective completion requires objective birth forecasts.");
            }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyGeneratedNoObjectFinalBaseCreatesAndCleansItsMission(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = GeneratedObjectiveOpeningContent(cache, 2, prefix: false, noObject: true, raceAlternatives: true);
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var initial = GeneratedObjectiveOpeningSnapshot(content) with { RandomState = seed };
            initial = initial.WithNextId("STR_ALIEN_BASE", int.MaxValue - 1) with
            { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 7] }, .. initial.Regions.Skip(1)] };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)),
                seed, "generated-no-object-base-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(2, after.World.AlienBases.Count);
            Assert.Empty(after.World.Ufos);
            Assert.Empty(after.World.Missions);
            Assert.Equal(int.MaxValue, after.Regions[0].ActivityAlien[^1]);
            Assert.Equal(int.MaxValue, after.NextIds["STR_ALIEN_BASE"]);
            campaign = Reload(content, campaign, seed, "generated-no-object-base-after.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(2, campaign.Capture().World.AlienBases.Count);
            AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_ALIEN_BASE", int.MaxValue), content,
                new SplitMix64RandomSource(seed)), "Base objective IDs are exhausted or collide.");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HuntingFinalBaseBirthMissesCurrentHuntingIteration(bool cache, bool detectable)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        var row = TestFixtures.Rows(oracle.RootElement, "birthRows").Single(row =>
            row[0].GetString() == (detectable ? "appendLive" : "appendDisabled"));
        var content = GeneratedObjectiveOpeningContent(cache, 2, hunting: true, prefix: false, noObject: true,
            detectableDestination: detectable);
        var initial = GeneratedObjectiveOpeningSnapshot(content, hunting: true).WithNextId("STR_ALIEN_BASE", 7);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)),
            41, "hunting-final-base-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var born = Assert.Single(after.World.AlienBases, alienBase => alienBase.Id == 7);
        Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(new[] { row[5].GetInt32(), row[6].GetInt32() })),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(new[] { born.MinutesSinceLastHuntMissionGeneration, after.World.AlienBases.Count })));
        Assert.Equal(0, after.World.AlienBases[0].MinutesSinceLastHuntMissionGeneration);
        Assert.Equal((1, 0), (Assert.Single(after.World.Missions).NextWave, after.World.Missions[0].LiveUfos));
        Assert.Empty(after.World.Ufos);
        Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 7, after.Regions[0].ActivityAlien[^1]);
        campaign = Reload(content, campaign, 41, "hunting-final-base-born.sav");
        AdvanceUnblocked(campaign, 360);
        Assert.Empty(campaign.Capture().World.Missions);
        Assert.Equal(detectable ? 90 : 60, campaign.Capture().World.AlienBases[1].MinutesSinceLastHuntMissionGeneration);
        Reload(content, campaign, 41, "hunting-final-base-next-boundary.sav");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HuntingFinalBaseIdsFollowEarlierSiteExpiryCounters(bool cache, bool decrease)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        var content = GeneratedObjectiveOpeningContent(cache, 2, hunting: true, prefix: false, noObject: true,
            expiryShift: decrease ? -1 : 1);
        var shiftedId = decrease ? 6 : 8;
        var initial = GeneratedObjectiveOpeningSnapshot(content, hunting: true).WithNextId("STR_ALIEN_BASE", 7);
        initial = initial with
        {
            World = initial.World with
            {
                AlienBases = [initial.World.AlienBases[0], new(shiftedId, "OBJECTIVE_OPENING_DESTINATION", "RACE_A", 0.2, 0.1)],
                MissionSites = [new(3, "STR_ALIEN_TERROR", "OBJECTIVE_EXPIRY_SHIFT", "RACE_A", 0.2, 0.1, 0) { Detected = true }],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)),
            "Base objective IDs are exhausted or collide.");
        initial = initial with
        { World = initial.World with { AlienBases = [initial.World.AlienBases[0], initial.World.AlienBases[1] with { Id = 9 }] } };
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(41)),
            41, "hunting-base-expiry-before.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.Single(campaign.Capture().World.AlienBases, alienBase => alienBase.Id == shiftedId);
        Assert.Empty(campaign.Capture().World.MissionSites);
        Reload(content, campaign, 41, "hunting-base-expiry-after.sav");
    }

    private static RuntimeContent GeneratedObjectiveOpeningContent(bool cache, int objective, bool hunting = false,
        int groundTimer = 0, bool prefix = true, bool raceAlternatives = false, bool finalImmediate = false, bool noObject = false,
        bool detectableDestination = false, int expiryShift = 0) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
            alienDeployments:
              - type: OBJECTIVE_EXPIRY_SHIFT
                markerName: OBJECTIVE_EXPIRY_MARKER
                {{{(expiryShift > 0 ? "counterDespawn: STR_ALIEN_BASE" : expiryShift < 0 ? "decreaseCounterDespawn: STR_ALIEN_BASE" : "")}}}
              - type: OBJECTIVE_OPENING_SOURCE
                alienBase: true
                markerName: STR_ALIEN_BASE
                genMission: {OBJECTIVE_OPENING_MISSION: 1}
                genMissionFreq: {{{(hunting ? 0 : 100)}}}
                genMissionLimit: 1
                genMissionRaceFromAlienBase: {{{!raceAlternatives}}}
                baseDetectionRange: {{{(hunting ? 100 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: 5400
                huntMissionWeights: {0: {OBJECTIVE_OPENING_MISSION: 1}}
              - type: OBJECTIVE_OPENING_DESTINATION
                alienBase: true
                markerName: STR_ALIEN_BASE
                baseDetectionRange: {{{(detectableDestination ? 100 : 0)}}}
                baseDetectionChance: 0
                huntMissionMaxFrequency: 60
            alienMissions:
              - type: OBJECTIVE_OPENING_MISSION
                objective: {{{objective}}}
                endlessInfiltration: false
                points: 7
                spawnZone: 1
                siteType: OBJECTIVE_OPENING_DESTINATION
                regionWeights: {0: {REGION: 1}}
                {{{(raceAlternatives ? "raceWeights: {0: {RACE_A: 1, RACE_B: 1}}" : "")}}}
                waves:
                  - {ufo: {{{(noObject ? "UNKNOWN_OBJECTIVE_FINAL" : "UFO_SCOUT")}}}, count: 1, trajectory: {{{(groundTimer == 0 ? "TRAJ_SPAWN_FIXED" : "OBJECTIVE_OPENING_GROUND")}}}, timer: 0}
                  {{{(prefix ? "- {ufo: UNKNOWN_OBJECTIVE_FINAL, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: " + (finalImmediate ? "0}" : "30000}") : "")}}}
            ufoTrajectories:
              - id: OBJECTIVE_OPENING_GROUND
                groundTimer: {{{groundTimer}}}
                waypoints: [[4, 0, 100], [5, 2, 80], [0, 3, 50]]
            """);

    private static CampaignSnapshot GeneratedObjectiveOpeningSnapshot(RuntimeContent content, bool hunting = false) =>
        (hunting ? HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 0, 29, 55)) : GeneratedWaveSnapshot(content, 41)) with
        {
            World = new()
            {
                AlienBases = [new(2, "OBJECTIVE_OPENING_SOURCE", "RACE_A", 0.2, 0.1)
                { MinutesSinceLastHuntMissionGeneration = 5390 }],
            },
        };
}
