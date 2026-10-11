using System.Globalization;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchSpawnTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AirborneBirthsPrecedeSiteResearchAndKeepCompletedMissionsAlive(bool final)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "spawnPathRows").Where(row => row[0].GetInt32() == 0 && row[1].GetInt32() == 0))
        {
            var second = row[2].GetInt32();
            var content = Content($"PATH_0_{second}", final);
            var initial = Snapshot(content);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            var mission = Assert.Single(after.World.Missions);
            Assert.True(mission.Interrupted);
            Assert.Equal((1, 0, 1), (mission.NextWave, mission.NextUfoCounter, mission.LiveUfos));
            Assert.Empty(after.World.MissionSites);
            Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
            Assert.Equal(UfoStatus.Flying, ufo.Status);
            var origin = Position(row, 6);
            var destination = Position(row, 8);
            var expected = WorldGeometry.Move(origin, destination, WorldGeometry.RadianSpeed(row[5].GetInt32()));
            Assert.InRange(WorldGeometry.Distance(expected, ufo.Position), 0, 1e-8);
            Assert.InRange(WorldGeometry.Distance(destination, ufo.Destination!.Position), 0, 1e-7);
            var random = new SplitMix64RandomSource(initial.RandomState);
            for (var draw = 0; draw < row[10].GetInt32(); draw++) random.NextInclusive(0, 0);
            if (!final) Assert.Equal(WorldTrajectory.SpawnCountdown(9000, random), mission.SpawnCountdown);
            random.NextInclusive(0, 99);
            Assert.Equal(random.State, after.RandomState);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 17, "site-research-airborne-birth.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            Assert.Single(campaign.Capture().World.Missions);
            Assert.Single(campaign.Capture().World.Ufos);
            Assert.Single(campaign.Capture().ResearchDiary);
        }
    }

    [Fact]
    public void AFinalBirthStillValidatesTheSurvivingMissionInterruptionLink()
    {
        var content = Content("PATH_0_0", true, "MISSING_RESEARCH");
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content), content, new SplitMix64RandomSource(17)),
            "Alien mission interruption references undefined research.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundedBirthsKeepTheirTimerAndLandingIdentityAcrossSiteGrants(bool final)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "spawnPathRows").Where(row => row[0].GetInt32() == 0 && row[1].GetInt32() == 1))
        {
            var content = Content($"PATH_1_{row[2].GetInt32()}", final);
            var initial = Snapshot(content);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal((UfoStatus.Landed, 5), (ufo.Status, ufo.SecondsRemaining));
            Assert.InRange(WorldGeometry.Distance(Position(row, 6), ufo.Position), 0, 1e-8);
            Assert.InRange(WorldGeometry.Distance(Position(row, 8), ufo.Destination!.Position), 0, 1e-7);
            // The native half-hour caller assigns a landing marker only after detection.
            Assert.False(ufo.Detected);
            Assert.Equal(0, ufo.LandId);
            Assert.True(Assert.Single(after.World.Missions).Interrupted);
            Assert.Equal(1, after.World.Missions[0].LiveUfos);
            Assert.Equal(6 + 7, after.Regions[0].ActivityAlien[0]); // grounded activity plus site despawn penalty
            Assert.Empty(after.World.MissionSites);
            Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
            var random = new SplitMix64RandomSource(initial.RandomState);
            for (var draw = 0; draw < row[10].GetInt32(); draw++) random.NextInclusive(0, 0);
            if (!final) WorldTrajectory.SpawnCountdown(9000, random);
            random.NextInclusive(0, 99);
            Assert.Equal(random.State, after.RandomState);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 17, "site-research-ground-birth.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 1);
            var lifted = Assert.Single(campaign.Capture().World.Ufos);
            Assert.Equal((UfoStatus.Flying, WorldAltitudes.VeryLow, 0, ufo.LandId),
                (lifted.Status, lifted.Altitude, lifted.SecondsRemaining, lifted.LandId));
            Assert.True(campaign.Capture().World.Missions[0].Interrupted);
            Assert.Equivalent(campaign.Capture(), Reload(content, campaign, 17, "site-research-ground-lift.sav").Capture(), strict: true);
        }
    }

    [Fact]
    public void GroundedBirthsRetainAtomicLandingMarkerLimits()
    {
        var content = Content("PATH_1_0");
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content).WithNextId("STR_LANDING_SITE", int.MaxValue),
            content, new SplitMix64RandomSource(17)), "UFO landing marker IDs are exhausted or collide with a saved UFO.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateNewbornTakeoffPreservesDetectionBeforeResearchAndLiftAfterIt(bool final)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var lift = Assert.Single(TestFixtures.Rows(oracle.RootElement, "normalLiftRows"), row => row[0].GetInt32() == 0 && row[1].GetInt32() == 0);
        var content = Content("PATH_SHORT", final);
        var initial = Snapshot(content).WithFacility("RADAR_HYPER_TEST");
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Single(result.Events.OfType<UfoContactDetected>());
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        Assert.Equal((UfoStatus.Flying, WorldAltitudes.VeryLow, 0), (ufo.Status, ufo.Altitude, ufo.SecondsRemaining));
        Assert.True(ufo.Detected);
        Assert.True(ufo.LandId > 0);
        Assert.True(ufo.Id > 0);
        Assert.True(Assert.Single(after.World.Missions).Interrupted);
        Assert.Equal(1, after.World.Missions[0].LiveUfos);
        Assert.Equal(6 + 7 + lift[6].GetInt32(), after.Regions[0].ActivityAlien[0]);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        Assert.Equivalent(after, Reload(content, campaign, 17, "site-research-immediate-lift.sav").Capture(), strict: true);
    }

    [Fact]
    public void ActualNewbornDetectionScriptReadsResearchBeforeSiteGrantThenAfterReload()
    {
        TestFixtures.LoadVerifiedManifest("strategic-detection-scripts");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        var content = Content("PATH_SHORT", extra: """
            facilities:
              - {type: STORES, radarRange: 5000, radarChance: 100}
            ufos:
              - type: UFO_RESEARCH_BIRTH
                speedMax: 20
                damageMax: 50
                scripts:
                  detectUfoFromBase: |
                    var ptr RuleResearch topic;
                    var int completed;
                    rules.getRuleResearch topic "CORE";
                    geoscape_game.isResearched completed topic;
                    if eq completed 1; return DETECTION_RADAR 100; end;
                    return DETECTION_NONE 0;
            """, ufo: "UFO_RESEARCH_BIRTH");
        var campaign = CampaignState.Restore(Snapshot(content), content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Contains("CORE", after.CompletedResearch);
        Assert.True(Assert.Single(after.World.Missions).Interrupted);
        Assert.False(Assert.Single(after.World.Ufos).Detected);
        Assert.Equal((UfoStatus.Flying, 0), (after.World.Ufos[0].Status, after.World.Ufos[0].LandId));
        campaign = Reload(content, campaign, 17, "newborn-detection-pre-site.sav");
        Assert.Equivalent(after, campaign.Capture(), strict: true);
        AdvanceUnblocked(campaign, 360);
        var detected = Assert.Single(campaign.Capture().World.Ufos);
        Assert.True(detected.Detected);
        Assert.True(detected.Id > 0);
        Assert.Single(campaign.Capture().ResearchDiary);
        Assert.Equivalent(campaign.Capture(), Reload(content, campaign, 17, "newborn-detection-post-site.sav").Capture(), strict: true);
    }

    [Fact]
    public void BirthGeometryWorkIsBoundedBeforeSelectingAnyPointOrGrant()
    {
        var origins = string.Join(", ", Enumerable.Repeat("[10, 10, 8, 8]", 256));
        var destinations = string.Join(", ", Enumerable.Repeat("[100, 100, 2, 2]", 256));
        var content = Content("RESEARCH_GEOMETRY", extra: $$"""
            regions:
              - type: RESEARCH_GEOMETRY_REGION
                areas: [[0, 180, -30, 30]]
                missionZones:
                  - [{{origins}}]
                  - [{{destinations}}]
            ufoTrajectories:
              - id: RESEARCH_GEOMETRY
                waypoints: [[0, 3, 100], [1, 2, 80], [0, 3, 50]]
            """);
        var initial = Snapshot(content);
        initial = initial with
        {
            World = initial.World with
            {
                Missions = Enumerable.Range(4, 16).Select(id => initial.World.Missions[0] with
                { Id = id, RegionId = "RESEARCH_GEOMETRY_REGION" }).ToArray(),
            },
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien mission spawn geometry exceeds the supported work limit.");
    }

    [Fact]
    public void NewHuntersStillStopBeforeCombatWithSiteResearch()
    {
        var content = Content("PATH_0_0", ufo: "UFO_HUNTER");
        var initial = Snapshot(content).WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 300,
                Speed = 0,
                Destination = null,
                Longitude = Degrees(10),
                Latitude = Degrees(8)
            }
        });
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "UFO hunting interception requires strategic combat (Phase 6 branch 5).");
    }

    internal static RuntimeContent Content(string trajectory, bool final = false, string interrupt = "CORE", string extra = "", string ufo = "UFO_SCOUT") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-spawn-paths.rul"], $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
        alienMissions:
          - type: SITE_RESEARCH_BIRTH
            interruptResearch: {{{interrupt}}}
            points: 7
            waves:
              - {ufo: {{{ufo}}}, count: 1, trajectory: {{{trajectory}}}, timer: 0}
        {{{(final ? "" : "      - {count: 1, trajectory: TRAJ_PATROL, timer: 9000}")}}}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + "\n" + extra);

    internal static CampaignSnapshot Snapshot(RuntimeContent content)
    {
        var initial = StrategicWorldSiteResearchTests.Snapshot(content);
        return initial with
        {
            RandomState = 17,
            Bases = [initial.Bases[0] with { Research = [] }],
            World = initial.World with { Missions = [new(4, "SITE_RESEARCH_BIRTH", "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
        };
    }

    internal static WorldPosition Position(System.Text.Json.JsonElement row, int index) =>
        new(double.Parse(row[index].GetString()!, CultureInfo.InvariantCulture),
            double.Parse(row[index + 1].GetString()!, CultureInfo.InvariantCulture));
}
