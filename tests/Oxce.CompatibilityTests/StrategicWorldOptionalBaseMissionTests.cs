using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldOptionalBaseMissionTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    [InlineData(false, 6)]
    [InlineData(true, 6)]
    public void OptionalSourceDeletionInterruptsEveryOrdinaryFlightPathAndPersists(bool cache, int path)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        var content = FlightContent(cache, path);
        var deletedContent = FlightContent(cache, path, "CORE");
        var survivedContent = FlightContent(cache, path, "");
        var deletedSeen = false;
        var survivedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Source(StrategicWorldDailyMissionResearchTests.Snapshot(content, seed, path));
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            var mission = Assert.Single(after.World.Missions);
            Assert.Equal(deleted, mission.Interrupted);
            Assert.Equal(deleted, mission.AlienBase is null);
            Assert.Equal(!deleted, after.World.AlienBases.Any(alienBase => alienBase.Id == 6));
            var terminal = path == 6 || deleted && path != 0 || path == 3 && !deleted;
            Assert.Equal(terminal, after.World.Ufos[0].Status == UfoStatus.Destroyed);
            Assert.Equal(terminal, after.World.Ufos[1].Position == initial.World.Ufos[1].Position);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++) campaign = Reload(content, campaign, seed, "optional-base-mission-flight.sav");
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
            deletedSeen |= deleted;
            survivedSeen |= !deleted;
        }
        Assert.True(deletedSeen);
        Assert.True(survivedSeen);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void OptionalSourceDeletionPrecedesDueWaveSchedulingAndZeroLiveCleanup(bool cache, int kind)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var content = WaveContent(cache, kind);
        var deletedContent = WaveContent(cache, kind, "CORE");
        var survivedContent = WaveContent(cache, kind, "");
        var deletedSeen = false;
        var survivedSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = Source(StrategicWorldDailyWaveResearchTests.Snapshot(content, seed, kind));
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(!deleted, after.World.AlienBases.Any(alienBase => alienBase.Id == 6));
            if (deleted) Assert.Empty(after.World.Missions);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++) campaign = Reload(content, campaign, seed, "optional-base-mission-wave.sav");
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), campaign.Capture(), strict: true);
            deletedSeen |= deleted;
            survivedSeen |= !deleted;
        }
        Assert.True(deletedSeen);
        Assert.True(survivedSeen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalSourceCleanupCannotOrphanUfosOrInvalidRetaliationLinks(bool cache)
    {
        var content = FlightContent(cache, 0);
        var initial = Source(StrategicWorldDailyMissionResearchTests.Snapshot(content, 17, 0));
        initial = initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { LiveUfos = 0, NextWave = 0 }] } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), "Alien mission cannot expire while UFOs still reference it.");
        initial = initial with
        {
            Bases = [initial.Bases[0] with { RetaliationMissionId = initial.World.Missions[0].Id }],
            World = initial.World with { Ufos = [] },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "A non-retaliation mission cannot expire while a base links it as retaliation.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalSourceSurvivalStillReservesCreationMarkers(bool cache)
    {
        var content = FlightContent(cache, 5);
        var initial = Source(StrategicWorldDailyMissionResearchTests.Snapshot(content, 17, 5)).WithNextId("ARRIVAL_BASE", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), "Base objective IDs are exhausted or collide.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalSourceSurvivalKeepsInterceptionAtomic(bool cache)
    {
        var content = FlightContent(cache, 0);
        var initial = Source(StrategicWorldDailyMissionResearchTests.Snapshot(content, 17, 0)).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Longitude = Degrees(8), Latitude = Degrees(2), Speed = 6000, Takeoff = 0 },
        });
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with
                {
                    TrajectoryId = "TRAJ_AIRBORNE", TrajectoryPoint = 0, Longitude = Degrees(8), Latitude = Degrees(2),
                    Destination = WorldTargetReference.ForWaypoint(0, new(Degrees(8), Degrees(2))),
                }, initial.World.Ufos[1]],
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Craft interception requires strategic combat (Phase 6 branch 5).");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalSpecialUfoSourceLinksUseEarthMissionInterruption(bool cache)
    {
        var content = FlightContent(cache, 0);
        var initial = Source(StrategicWorldDailyMissionResearchTests.Snapshot(content, 17, 0));
        initial = initial with { World = initial.World with { Ufos = [initial.World.Ufos[0] with { HunterKiller = true }, initial.World.Ufos[1]] } };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(after.CompletedResearch.Contains("BONUS"), Assert.Single(after.World.Missions).Interrupted);
        Assert.True(after.World.Ufos[0].HunterKiller);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalSourceDeletionRepairsRetaliationLinksAtZeroLiveCleanup(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-cleanup");
        var content = RetaliationContent(cache, "BONUS");
        var deletedContent = RetaliationContent(cache, "CORE");
        var survivedContent = RetaliationContent(cache, "");
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var initial = CreateSnapshot(content, "Optional base retaliation cleanup");
            initial = Source(initial with
            {
                Time = new(2, 2, 1, 1999, 23, 59, 55),
                RandomState = seed,
                Bases = [initial.Bases[0] with { Research = [new("CORE", 0, 1, 1)], RetaliationMissionId = 4 }],
                World = initial.World with { Missions = [new(4, "OPTIONAL_RETALIATION", "REGION", "RACE_A", 0, 0, 9000, 0, -1)] },
            });
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var deleted = after.CompletedResearch.Contains("BONUS");
            Assert.Equal(deleted ? null : (int?)4, after.Bases[0].RetaliationMissionId);
            Assert.Equal(deleted ? 0 : 1, after.World.Missions.Count);
            var control = CampaignState.Restore(initial, deleted ? deletedContent : survivedContent, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(control, 1);
            Assert.Equivalent(control.Capture(), after, strict: true);
            for (var pass = 0; pass < 3; pass++) campaign = Reload(content, campaign, seed, "optional-base-retaliation-cleanup.sav");
        }
    }

    private static RuntimeContent RetaliationContent(bool cache, string code) => StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
        research:
          - {name: CORE, cost: 1, getOneFree: [BONUS, OTHER]}
          - {name: BONUS, cost: 1}
          - {name: OTHER, cost: 1}
        alienDeployments:
          - {type: OPTIONAL_OPERATION_BASE, markerName: OPTIONAL_SOURCE, points: 7, baseSelfDestructCode: {{{code}}}}
        alienMissions:
          - type: OPTIONAL_RETALIATION
            objective: 4
            operationType: 1
            operationBaseType: OPTIONAL_OPERATION_BASE
            waves: [{ufo: UFO_SCOUT, count: 1, trajectory: TRAJ_PATROL, timer: 9000}]
        """);

    private static RuntimeContent FlightContent(bool cache, int path, string sourceCode = "BONUS") =>
        StrategicWorldConversionResearchTests.Content(cache, respawn: path == 4, interrupt: "", baseLanding: path == 5,
            operation: path % 6 + 1, sourceCode: sourceCode);

    private static RuntimeContent WaveContent(bool cache, int kind, string sourceCode = "BONUS") =>
        StrategicWorldDailyWaveResearchTests.Content(cache, kind, interrupt: "", operation: kind + 1, sourceCode: sourceCode);

    internal static CampaignSnapshot Source(CampaignSnapshot initial) => initial.WithNextId("OPTIONAL_SOURCE", 70) with
    {
        World = initial.World with
        {
            AlienBaseBirthSequence = 8,
            AlienBases = [new(6, "OPTIONAL_OPERATION_BASE", "RACE_A", 0.2, 0.1) { Discovered = true, PreservationKey = "alien-base:generated:8" }],
            Missions = [.. initial.World.Missions.Select(mission => mission with
            { AlienBase = new(WorldTargetKind.AlienBase, "OPTIONAL_SOURCE", 6, 0.2, 0.1) })],
        },
    };
}
