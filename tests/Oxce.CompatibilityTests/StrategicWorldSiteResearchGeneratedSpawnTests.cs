using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchGeneratedSpawnTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void GeneratedAirborneHuntersRetargetAfterSiteGrantAtBothCallers(bool cache, bool hunting, bool final)
    {
        TestFixtures.LoadVerifiedManifest(hunting ? "strategic-alien-base-hunt-generation" : "strategic-alien-base-generation");
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "constructorRows"), row =>
            row[0].GetInt32() == 100 && row[1].GetInt32() == 1 && row[4].GetBoolean());
        var content = Content(cache, hunting, final, ufo: "UFO_HUNTER");
        var initial = Snapshot(content, hunting);
        var source = Assert.Single(initial.World.AlienBases);
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 0,
                Longitude = source.Longitude + WorldGeometry.Nautical(50),
                Latitude = source.Latitude,
                Destination = WorldTargetReference.ForWaypoint(1, new(1, 0.6)),
            },
        }) with
        { World = initial.World with { Waypoints = [new(1, 1, 0.6)] } };
        var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(17));
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "generated-hunter-research-before.sav");
        AdvanceUnblocked(control, 1);
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        var born = Assert.Single(after.World.Ufos);
        Assert.True(born.Hunting);
        Assert.True(born.Detected);
        Assert.Equal(WorldTargetKind.Craft, born.Destination!.Kind);
        Assert.Equal(1, born.Destination.Id);
        Assert.NotNull(born.OriginalWaypoint);
        Assert.Equal(born.UniqueId, Assert.Single(result.Events.OfType<UfoStartedHunting>()).UniqueId);
        Assert.Equivalent(control.Capture().World.Ufos, after.World.Ufos, strict: true);
        Assert.Equal(control.Capture().RandomState, after.RandomState);
        var mission = Assert.Single(after.World.Missions);
        Assert.Equal(!hunting, mission.Interrupted);
        Assert.Equal(1, mission.LiveUfos);
        Assert.Equal(source.Id, mission.AlienBase!.Id);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        Assert.Empty(after.World.MissionSites);
        Assert.Equivalent(after, Reload(content, campaign, 17, "generated-hunter-research-after.sav").Capture(), strict: true);
        AdvanceUnblocked(campaign, 3);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Hunting);
        Reload(content, campaign, 17, "generated-hunter-research-following.sav");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DailyGeneratedBirthPrecedesSiteGrantAndRemainsLinked(bool cache, bool final) =>
        AssertBirth(cache, final, hunting: false);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void HuntingGeneratedBirthFollowsSiteGrantAndMissesEarlierInterruption(bool cache, bool final) =>
        AssertBirth(cache, final, hunting: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalDailyGeneratedUfoKeepsItsInvalidInterruptionLinkReachable(bool cache)
    {
        var content = Content(cache, hunting: false, final: true, interrupt: "MISSING_RESEARCH");
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, hunting: false), content, new SplitMix64RandomSource(17)),
            "Alien mission interruption references undefined research.");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedGroundTimersAndImmediateLiftsPreserveTheirCallerOrder(bool cache, bool hunting)
    {
        using var generation = TestFixtures.ReadVerifiedExpected(hunting ? "strategic-alien-base-hunt-generation" : "strategic-alien-base-generation");
        using var lifecycle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var generationRow = TestFixtures.Rows(generation.RootElement, "rows").Single(row => row[0].GetString() == (hunting ? "delayed" : "local"));
        var spawnRow = Assert.Single(TestFixtures.Rows(lifecycle.RootElement, "spawnPathRows"), row =>
            row[0].GetInt32() == 0 && row[1].GetInt32() == 1 && row[2].GetInt32() == 0);
        var liftRow = Assert.Single(TestFixtures.Rows(lifecycle.RootElement, "normalLiftRows"), row => row[0].GetInt32() == 0 && row[1].GetInt32() == 0);
        foreach (var path in new[] { "PATH_1_0", "PATH_SHORT" })
        {
            var shortTimer = path == "PATH_SHORT";
            var content = Content(cache, hunting, trajectory: path);
            var initial = Snapshot(content, hunting);
            var random = new SplitMix64RandomSource(initial.RandomState);
            var calls = generationRow[hunting ? 2 : 4].EnumerateArray().ToArray();
            foreach (var call in calls.Take(calls.Length - 1)) random.NextInclusive(call[0].GetInt32(), call[1].GetInt32());
            random.NextInclusive(0, 0);
            for (var draw = 0; draw < spawnRow[10].GetInt32(); draw++) random.NextInclusive(0, 0);
            var countdown = WorldTrajectory.SpawnCountdown(9000, random);
            if (!hunting) random.NextInclusive(0, 99);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal(shortTimer ? UfoStatus.Flying : UfoStatus.Landed, ufo.Status);
            Assert.Equal(shortTimer ? WorldAltitudes.VeryLow : WorldAltitudes.Ground, ufo.Altitude);
            Assert.Equal(shortTimer ? 0 : spawnRow[4].GetInt32() - 5, ufo.SecondsRemaining);
            Assert.Equal(initial.World.AlienBases[0].Position, ufo.Position);
            Assert.False(ufo.Detected);
            Assert.Equal(0, ufo.LandId);
            var mission = Assert.Single(after.World.Missions);
            Assert.Equal(!hunting, mission.Interrupted);
            Assert.Equal((1, 0, 1, countdown), (mission.NextWave, mission.NextUfoCounter, mission.LiveUfos, mission.SpawnCountdown));
            Assert.Equal(mission.Id, ufo.MissionId);
            Assert.Equal(7 + (hunting ? 0 : 6) + (shortTimer ? liftRow[6].GetInt32() : 0), after.Regions[0].ActivityAlien[0]);
            Assert.Equal(random.State, after.RandomState);
            Assert.Empty(after.World.MissionSites);
            Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 17, "site-generated-ground-timer.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            Assert.Equal(UfoStatus.Flying, Assert.Single(campaign.Capture().World.Ufos).Status);
            Assert.Equal(0, campaign.Capture().World.Ufos[0].SecondsRemaining);
            Assert.Single(campaign.Capture().ResearchDiary);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameSourceBirthsStraddleSiteResearchAndRemainDistinctAfterReload(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        var content = Content(cache, hunting: true, bothCallers: true);
        var initial = Snapshot(content, hunting: true) with { Time = new(1, 1, 1, 1999, 23, 59, 55) };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(2, after.World.Ufos.Count);
        Assert.Collection(after.World.Missions,
            mission => { Assert.Equal(1, mission.Id); Assert.True(mission.Interrupted); Assert.Equal(1, mission.LiveUfos); },
            mission => { Assert.Equal(2, mission.Id); Assert.False(mission.Interrupted); Assert.Equal(1, mission.LiveUfos); });
        Assert.Equal(2, after.World.Ufos.Select(ufo => ufo.UniqueId).Distinct().Count());
        Assert.All(after.World.Ufos, ufo => Assert.Single(after.World.Missions, mission => mission.Id == ufo.MissionId));
        Assert.Empty(after.World.MissionSites);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-research-both-birth-callers.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeventyTwoHoursPreserveGeneratedUfosThroughDailyInterruptionAndReloads(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = Content(cache, hunting: true);
        var initial = Snapshot(content, hunting: true);
        var batched = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var reloaded = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(batched, 1);
        AdvanceUnblocked(reloaded, 1);
        Assert.Single(batched.Capture().World.Ufos);
        for (var day = 0; day < 3; day++)
        {
            AdvanceUnblocked(batched, 17_280);
            for (var hour = 0; hour < 24; hour++) AdvanceUnblocked(reloaded, 720);
            reloaded = Reload(content, reloaded, 17, "generated-ufo-research-endurance.sav");
            Assert.Equivalent(batched.Capture(), reloaded.Capture(), strict: true);
            var after = batched.Capture();
            Assert.InRange(after.World.Missions.Count, 0, 24);
            Assert.InRange(after.World.Ufos.Count, 0, 24);
            Assert.All(after.World.Ufos, ufo => Assert.Single(after.World.Missions, mission => mission.Id == ufo.MissionId));
            Assert.Empty(after.World.MissionSites);
            Assert.Single(after.World.AlienBases);
            Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedSafeHuntersPreserveTheirCreationCallerAndRetargetPhase(bool cache, bool hunting)
    {
        using var native = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        Assert.Contains(TestFixtures.Rows(native.RootElement, "constructorRows"), row =>
            row[0].GetInt32() == 100 && row[1].GetInt32() == 1 && row[2].GetInt32() == 0 && row[4].GetBoolean() && row[5].GetInt32() == 1);
        TestFixtures.LoadVerifiedManifest(hunting ? "strategic-alien-base-hunt-generation" : "strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        foreach (var path in hunting ? new[] { "PATH_1_0", "PATH_SHORT" } : new[] { "PATH_0_0", "PATH_1_0", "PATH_SHORT" })
        {
            var content = Content(cache, hunting, trajectory: path, ufo: "UFO_HUNTER");
            var initial = Snapshot(content, hunting);
            var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(17));
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(control, 1);
            var result = campaign.Execute(new AdvanceCampaignTime(1));
            Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
            Assert.Empty(result.Events.OfType<UfoStartedHunting>());
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            Assert.True(ufo.HunterKiller);
            Assert.False(ufo.Hunting);
            Assert.False(ufo.Escorting);
            Assert.Equal(1, ufo.HuntMode);
            Assert.Equal(path == "PATH_1_0" ? UfoStatus.Landed : UfoStatus.Flying, ufo.Status);
            Assert.Equal(path == "PATH_1_0" ? 5 : 0, ufo.SecondsRemaining);
            Assert.Equal(WorldTargetKind.Waypoint, ufo.Destination!.Kind);
            Assert.Equivalent(control.Capture().World.Ufos, after.World.Ufos, strict: true);
            Assert.Equal(control.Capture().RandomState, after.RandomState);
            var mission = Assert.Single(after.World.Missions);
            Assert.Equal(!hunting, mission.Interrupted);
            Assert.False(Assert.Single(control.Capture().World.Missions).Interrupted);
            Assert.Equal(1, mission.LiveUfos);
            Assert.Equal(mission.Id, ufo.MissionId);
            Assert.Equal(2, mission.AlienBase!.Id);
            Assert.Empty(after.World.MissionSites);
            Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
            Assert.Equivalent(after, Reload(content, campaign, 17, "site-research-generated-safe-hunter.sav").Capture(), strict: true);
        }
    }

    private static void AssertBirth(bool cache, bool final, bool hunting)
    {
        using var generation = TestFixtures.ReadVerifiedExpected(hunting ? "strategic-alien-base-hunt-generation" : "strategic-alien-base-generation");
        using var lifecycle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var generationRow = TestFixtures.Rows(generation.RootElement, "rows").Single(row => row[0].GetString() == (hunting ? "delayed" : "local"));
        var spawnRow = Assert.Single(TestFixtures.Rows(lifecycle.RootElement, "spawnPathRows"), row =>
            row[0].GetInt32() == 0 && row[1].GetInt32() == 0 && row[2].GetInt32() == 0);
        var content = Content(cache, hunting, final);
        var initial = Snapshot(content, hunting);
        var random = new SplitMix64RandomSource(initial.RandomState);
        var calls = generationRow[hunting ? 2 : 4].EnumerateArray().ToArray();
        foreach (var call in calls.Take(calls.Length - 1)) random.NextInclusive(call[0].GetInt32(), call[1].GetInt32());
        random.NextInclusive(0, 0); // Native initial zero timer.
        for (var draw = 0; draw < spawnRow[10].GetInt32(); draw++) random.NextInclusive(0, 0);
        var countdown = final ? 0 : WorldTrajectory.SpawnCountdown(9000, random);
        if (!hunting) random.NextInclusive(0, 99); // Birth half-hour detection precedes sites.
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.MissionSites);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        var mission = Assert.Single(after.World.Missions);
        Assert.Equal(!hunting, mission.Interrupted);
        Assert.Equal((1, 0, 1, countdown), (mission.NextWave, mission.NextUfoCounter, mission.LiveUfos, mission.SpawnCountdown));
        Assert.Equal(2, mission.AlienBase!.Id);
        var ufo = Assert.Single(after.World.Ufos);
        Assert.Equal(mission.Id, ufo.MissionId);
        Assert.Equal(UfoStatus.Flying, ufo.Status);
        var destination = StrategicWorldSiteResearchSpawnTests.Position(spawnRow, 8);
        var expected = WorldGeometry.Move(initial.World.AlienBases[0].Position, destination, WorldGeometry.RadianSpeed(spawnRow[5].GetInt32()));
        Assert.InRange(WorldGeometry.Distance(expected, ufo.Position), 0, 1e-8);
        Assert.Equal(random.State, after.RandomState);
        Assert.Equal(hunting ? 0 : 1, Assert.Single(after.World.AlienBases).GenMissionCount);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-research-generated-ufo.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    internal static RuntimeContent Content(bool cache, bool hunting, bool final = false, string interrupt = "CORE", string trajectory = "PATH_0_0", bool bothCallers = false, string ufo = "UFO_SCOUT") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-spawn-paths.rul"], cache, $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
        alienMissions:
          - type: GENERATED_RESEARCH_BIRTH
            operationType: 1
            interruptResearch: {{{interrupt}}}
            points: 7
            waves:
              - {ufo: {{{ufo}}}, count: 1, trajectory: {{{trajectory}}}, timer: 0}
        {{{(final ? "" : "      - {count: 1, trajectory: TRAJ_PATROL, timer: 9000}")}}}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + $$$"""

          - type: GENERATED_RESEARCH_BIRTH_BASE
            markerName: RESEARCH_BIRTH_BASE
            genMission: {GENERATED_RESEARCH_BIRTH: 1}
            genMissionFreq: {{{(hunting && !bothCallers ? 0 : 100)}}}
            genMissionLimit: 1
            baseDetectionRange: {{{(hunting ? 100 : 0)}}}
            baseDetectionChance: 100
            huntMissionMaxFrequency: 60
            huntMissionWeights: {0: {GENERATED_RESEARCH_BIRTH: 1}}
        """);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, bool hunting)
    {
        var initial = StrategicWorldSiteResearchGeneratedObjectiveTests.Snapshot(content, hunting);
        return initial with
        {
            RandomState = 17,
            World = initial.World with
            { AlienBases = [initial.World.AlienBases[0] with { DeploymentId = "GENERATED_RESEARCH_BIRTH_BASE" }] },
        };
    }
}
