using Oxce.Core.Random;
using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicGeneratedRetaliationTests
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
    public void RetaliationBirthPreservesScoutingAndAssaultSelection(bool targeted, bool replace, bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-retaliation-spawn");
        var row = TestFixtures.Rows(oracle.RootElement, "rows").Single(row =>
            row[0].GetInt32() == 4 && row[1].GetInt32() == 0 && row[2].GetInt32() == 0 &&
            row[3].GetInt32() == (targeted ? 1 : 0) && row[4].GetInt32() == 0 && row[5].GetInt32() == 1 &&
            row[6].GetInt32() == 0 && row[7].GetInt32() == (replace ? 1 : 0));
        var content = Content(hunting, replace);
        var initial = Initial(content, hunting, targeted);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            17, "daily-retaliation-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        Assert.Equal(targeted && replace ? "GENERATED_RETAL_OVERRIDE" : "GENERATED_RETAL_UFO", ufo.RuleId);
        Assert.Equal(targeted ? RuntimeUfoTrajectoryRule.RetaliationAssaultRun : "TRAJ_SPAWN_FIXED", ufo.TrajectoryId);
        Assert.False(ufo.HunterKiller);
        Assert.False(ufo.Escorting);
        Assert.Equal((0, 0), (ufo.HuntMode, ufo.HuntBehavior));
        Assert.Equal(hunting ? 0 : 1, Assert.Single(after.World.AlienBases).GenMissionCount);
        var mission = Assert.Single(after.World.Missions);
        Assert.Equal((1, 1), (mission.NextWave, mission.LiveUfos));
        Assert.Equal(mission.Id, ufo.MissionId);
        if (targeted)
        {
            Assert.Equal(row[17].GetInt32(), ufo.Speed);
            var target = new WorldPosition(initial.Bases[0].Longitude, initial.Bases[0].Latitude);
            Assert.Equal(target, ufo.Destination!.Position);
            var moved = WorldGeometry.Move(initial.World.AlienBases[0].Position, target, WorldGeometry.RadianSpeed(ufo.Speed));
            Assert.Equal(moved.Longitude, ufo.Longitude, 12);
            Assert.Equal(moved.Latitude, ufo.Latitude, 12);
        }
        Assert.Equal(hunting ? 0 : targeted && replace ? 6 : 3, after.Regions[0].ActivityAlien[^1] - initial.Regions[0].ActivityAlien[^1]);
        campaign = Reload(content, campaign, 17, "daily-retaliation-after.sav");
        if (!targeted && !replace)
        {
            for (var day = 0; day < 3; day++)
            {
                AdvanceUnblocked(campaign, 17_280);
                campaign = Reload(content, campaign, 17, $"daily-retaliation-day-{day}.sav");
            }
            Assert.Empty(campaign.Capture().World.Ufos);
            Assert.Empty(campaign.Capture().World.Missions);
        }
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO_UNIQUE", int.MaxValue), content,
            new SplitMix64RandomSource(17)), "UFO unique IDs are exhausted or collide with a saved UFO.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedInstantRetaliationAttacksTheBaseInItsBirthTick(bool hunting)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        TestFixtures.LoadVerifiedManifest("strategic-retaliation-spawn");
        var content = Content(hunting, false, instant: true);
        var campaign = CampaignState.Restore(Initial(content, hunting, true), content, new SplitMix64RandomSource(17));
        var owner = campaign.Capture().Bases[0];
        // AlienMission::spawnUfo places the ship over the base; the same five-second pass reaches it, and the
        // undefended base's garrison is asked to fight.
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        var request = Assert.Single(result.Events.OfType<DeploymentRequested>()).Request;
        Assert.Equal(("STR_BASE_DEFENSE", owner.Id, owner.Longitude, owner.Latitude),
            (request.MissionType, request.BaseId, request.Target.Longitude, request.Target.Latitude));
        var after = campaign.Capture();
        var ufo = Assert.Single(after.World.Ufos);
        Assert.Equal((UfoStatus.Destroyed, 1, "STR_NONE_UC", owner.Longitude, owner.Latitude),
            (ufo.Status, ufo.TrajectoryPoint, ufo.Direction, ufo.Longitude, ufo.Latitude));
        Assert.True(Assert.Single(after.World.Missions).Interrupted);
        AssertTimeBlocked(campaign, WorldCommandReasons.DeploymentRequestPending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverrideOnlyImmediateRetaliationChainReservesEveryActualUfo(bool hunting)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-retaliation-spawn");
        var row = TestFixtures.Rows(oracle.RootElement, "overrideRows").Single(row =>
            row[0].GetInt32() == 4 && row[1].GetInt32() == 1 && row[2].GetInt32() == 0 && row[3].GetInt32() == 1);
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var content = Content(hunting, true, noOriginalUfo: true, immediateChain: true);
        var initial = Initial(content, hunting, true).WithNextId("STR_UFO_UNIQUE", int.MaxValue - 2);
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            17, "override-only-retaliation-before.sav");
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(2, after.World.Ufos.Count);
        Assert.All(after.World.Ufos, ufo => Assert.Equal("GENERATED_RETAL_OVERRIDE", ufo.RuleId));
        foreach (var ufo in after.World.Ufos)
        {
            var expected = JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(4, 4).Select(index => row[index].GetInt32()).ToArray());
            var actual = JsonSerializer.SerializeToUtf8Bytes(new[] { 1, after.World.Missions[0].Interrupted ? 1 : 0, 0, ufo.Speed });
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
        }
        Assert.Equal((2, 2), (Assert.Single(after.World.Missions).NextWave, after.World.Missions[0].LiveUfos));
        Assert.Equal(int.MaxValue, after.NextIds["STR_UFO_UNIQUE"]);
        Assert.Equal(hunting ? 0 : 12, after.Regions[0].ActivityAlien[^1] - initial.Regions[0].ActivityAlien[^1]);
        Reload(content, campaign, 17, "override-only-retaliation-after.sav");
        AssertTimeBlocked(CampaignState.Restore(initial.WithNextId("STR_UFO_UNIQUE", int.MaxValue - 1), content,
            new SplitMix64RandomSource(17)), "UFO unique IDs are exhausted or collide with a saved UFO.");
    }

    internal static RuntimeContent Content(bool hunting, bool replace, bool instant = false,
        bool noOriginalUfo = false, bool immediateChain = false, string interrupt = "") =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], $$$"""
            research:
              - {name: CORE, cost: 1}
            ufos:
              - type: GENERATED_RETAL_UFO
                speedMax: 100
                damageMax: 50
                noAlert: true
                missionScore: 3
              - type: GENERATED_RETAL_OVERRIDE
                speedMax: 200
                damageMax: 50
                noAlert: true
                missionScore: 6
                hunterKillerPercentage: 100
            ufoTrajectories:
              - id: __RETALIATION_ASSAULT_RUN
                waypoints: [[0, 3, 100], [5, 0, 100]]
            alienDeployments:
              - {type: DEPLOY_DIARY, markerName: DIARY_SITE, unlockedResearchOnDespawn: CORE}
              - type: GENERATED_RETAL_BASE
                alienBase: true
                markerName: STR_ALIEN_BASE
                genMission: {GENERATED_RETAL_MISSION: 1}
                genMissionFreq: {{{(hunting ? 0 : 100)}}}
                genMissionLimit: 1
                baseDetectionRange: {{{(hunting ? 100 : 0)}}}
                baseDetectionChance: 100
                huntMissionMaxFrequency: 5400
                huntMissionWeights: {0: {GENERATED_RETAL_MISSION: 1}}
            alienMissions:
              - type: GENERATED_RETAL_MISSION
                objective: {{{(instant ? 6 : 4)}}}
                interruptResearch: {{{interrupt}}}
                operationType: 1
                {{{(replace ? "spawnUfo: GENERATED_RETAL_OVERRIDE" : "")}}}
                regionWeights: {0: {REGION: 1}}
                waves:
                  - {ufo: {{{(noOriginalUfo ? "UNDEFINED" : "GENERATED_RETAL_UFO")}}}, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0}
                  {{{(immediateChain ? "- {ufo: UNDEFINED, count: 1, trajectory: TRAJ_SPAWN_FIXED, timer: 0}" : "")}}}
            """);

    internal static CampaignSnapshot Initial(RuntimeContent content, bool hunting, bool targeted)
    {
        var initial = CreateSnapshot(content, "Generated retaliation completion");
        initial = initial with
        {
            RandomState = 17,
            Time = hunting ? new(1, 1, 1, 1999, 0, 29, 55) : new(1, 1, 1, 1999, 23, 59, 55),
            Bases = initial.Bases.Select(owner => owner with
            { Longitude = 0.4, Latitude = 0.1, RetaliationTarget = targeted }).ToArray(),
            World = new()
            {
                AlienBases = [new(7, "GENERATED_RETAL_BASE", "RACE_A", 0.2, 0.1)
                { MinutesSinceLastHuntMissionGeneration = 5390 }]
            },
        };
        return hunting ? initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Fuel = 100,
                Damage = 0,
                Shield = 0,
                Longitude = 0.2,
                Latitude = 0.1,
                Speed = 0,
                SpeedRadian = 0,
                Destination = null,
            },
        }) : initial;
    }
}
