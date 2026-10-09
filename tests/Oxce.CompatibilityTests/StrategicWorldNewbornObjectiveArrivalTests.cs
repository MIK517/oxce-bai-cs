using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornObjectiveArrivalTests
{
    private static readonly int[] BirthWaveNumbers = [0, 1, 2];
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void FirstAirborneArrivalFollowsThinkTimeObjectivesAndPreservesWaveIdentity(bool cache, int objective)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        Assert.Contains(TestFixtures.Rows(oracle.RootElement, "transitionRows"), row =>
            row[0].GetInt32() == objective && row[3].GetInt32() == 0 && row[8].GetInt32() == 1760);
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-infiltration-region");
        TestFixtures.LoadVerifiedManifest("strategic-mission-origin");
        foreach (var earth in new[] { false, true })
        {
            var content = Content(cache, objective, earth);
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var initial = Snapshot(content, seed, objective);
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                var result = campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
                var after = campaign.Capture();
                Assert.Equal(3, after.World.Ufos.Count);
                Assert.All(after.World.Ufos, ufo =>
                {
                    Assert.Equal(1, ufo.TrajectoryPoint);
                    Assert.Equal(UfoStatus.Flying, ufo.Status);
                    Assert.Equal(1760, ufo.Speed);
                    Assert.Equal(new WorldPosition(Degrees(8), Degrees(2)), ufo.Position);
                    Assert.Equal(ufo.Position, ufo.Destination!.Position);
                });
                Assert.Equal(BirthWaveNumbers, after.World.Ufos.Select(ufo => ufo.MissionWaveNumber));
                Assert.Equal(3, Assert.Single(after.World.Missions).NextWave);
                Assert.Equal(3, after.World.Missions[0].LiveUfos);
                Assert.Equal(objective is 1 or 2 ? 2 : 1, after.World.AlienBases.Count);
                Assert.Empty(after.World.MissionSites);
                if (objective is 1 or 2)
                {
                    var born = Assert.Single(after.World.AlienBases, alienBase => alienBase.DeploymentId == "FIRST_OBJECTIVE_BASE");
                    Assert.True(born.Discovered);
                    Assert.Equal(objective == 1 ? "COUNTRY" : "", born.PactCountryId);
                    Assert.Equal(objective == 1, after.Countries[0].NewPact);
                    Assert.Single(result.Events.OfType<AlienBaseDiscovered>());
                }
                Assert.Equal(objective == 2 ? 16 : 9, after.Regions[0].ActivityAlien[^1]);
                for (var pass = 0; pass < 3; pass++)
                {
                    campaign = Reload(content, campaign, seed, "newborn-objective-first.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 1);
                Assert.All(campaign.Capture().World.Ufos, ufo => Assert.Equal(2, ufo.TrajectoryPoint));
            }
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void FirstArrivalCannotHideFinalObjectiveCapacity(bool cache, int objective)
    {
        var content = Content(cache, objective, false);
        var initial = Snapshot(content, 17, objective);
        initial = initial with { World = initial.World with { AlienBaseBirthSequence = ulong.MaxValue } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Base objective birth sequence is exhausted.");
    }

    internal static RuntimeContent Content(bool cache, int objective, bool earth) =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
            alienDeployments:
              - {type: FIRST_OBJECTIVE_SOURCE, markerName: FIRST_SOURCE, points: 0, genMissionFreq: 0, huntMissionMaxFrequency: 0}
              - {type: FIRST_OBJECTIVE_BASE, markerName: FIRST_OBJECTIVE_BIRTH, points: 0, genMissionFreq: 0, huntMissionMaxFrequency: 0}
            ufos:
              - {type: FIRST_OBJECTIVE_UFO, speedMax: 2200, damageMax: 50, missionScore: 3, noAlert: true}
            ufoTrajectories:
              - {id: FIRST_OBJECTIVE_AIR, waypoints: [[5, 3, 100], [5, 2, 80], [4, 3, 100]]}
            alienMissions:
              - type: FIRST_OBJECTIVE_MISSION
                objective: {{{objective}}}
                operationType: {{{(earth ? 1 : 0)}}}
                points: 7
                spawnZone: 5
                siteType: FIRST_OBJECTIVE_BASE
                showAlienBase: true
                endlessInfiltration: false
                waves:
                  - {ufo: FIRST_OBJECTIVE_UFO, count: 1, trajectory: FIRST_OBJECTIVE_AIR, timer: 0, objective: true}
                  - {ufo: FIRST_OBJECTIVE_UFO, count: 1, trajectory: FIRST_OBJECTIVE_AIR, timer: 0, objective: true}
                  - {ufo: FIRST_OBJECTIVE_UFO, count: 1, trajectory: FIRST_OBJECTIVE_AIR, timer: 0, objective: true}
            """);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, ulong seed, int objective)
    {
        var initial = StrategicWorldNewbornFirstArrivalTests.Snapshot(content, seed);
        return initial with
        {
            World = initial.World with
            {
                Missions = [new(4, "FIRST_OBJECTIVE_MISSION", "REGION", "RACE_A", 0, 0, 0, 0, objective == 3 ? 0 : -1)
                { AlienBase = new(WorldTargetKind.AlienBase, "FIRST_SOURCE", 6, Degrees(8), Degrees(2)) }],
                AlienBases = [new(6, "FIRST_OBJECTIVE_SOURCE", "RACE_A", Degrees(8), Degrees(2))],
            },
        };
    }
}
