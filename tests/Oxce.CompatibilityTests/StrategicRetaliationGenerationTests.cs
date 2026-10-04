using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicRetaliationGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedNoObjectRetaliationUsesMappedRegionsAndCallerCleanupOrder(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-retaliation-spawn");
        foreach (var objective in new[] { 4, 6 })
            foreach (var hunting in new[] { false, true })
                foreach (var targeted in new[] { false, true })
                {
                    Assert.Contains(TestFixtures.Rows(oracle.RootElement, "rows"), row => row[0].GetInt32() == objective && row[4].GetInt32() == 1 && row[8].GetInt32() == 0);
                    var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
                        regions:
                          - type: REGION
                            missionRegion: {{{(targeted ? "RETAL_GEN_TARGET" : "RETAL_GEN_EMPTY")}}}
                          - type: RETAL_GEN_TARGET
                            areas: [[0, 60, -30, 30]]
                          - type: RETAL_GEN_EMPTY
                            areas: [[100, 110, -10, 10]]
                        crafts:
                          - type: SHIP
                            sightRange: 0
                        alienDeployments:
                          - type: RETAL_GEN_BASE
                            alienBase: true
                            markerName: STR_ALIEN_BASE
                            genMission: {RETAL_GEN: 1}
                            genMissionFreq: {{{(hunting ? 0 : 100)}}}
                            genMissionLimit: 1
                            baseDetectionRange: {{{(hunting ? 100 : 0)}}}
                            baseDetectionChance: 100
                            huntMissionMaxFrequency: 10
                            huntMissionWeights: {0: {RETAL_GEN: 1}}
                        alienMissions:
                          - type: RETAL_GEN
                            objective: {{{objective}}}
                            operationType: 1
                            skipScoutingPhase: true
                            waves: [{ufo: UNDEFINED, count: 2, trajectory: TRAJ_PATROL, timer: 0}, {ufo: UNDEFINED, count: 1, trajectory: TRAJ_PATROL, timer: 0}]
                        """);
                    var initial = CreateSnapshot(content, "Generated retaliation") with
                    {
                        RandomState = 17,
                        Time = hunting ? new(1, 1, 1, 1999, 0, 9, 55) : new(1, 1, 1, 1999, 23, 59, 55),
                        World = new() { AlienBases = [new(7, "RETAL_GEN_BASE", "RACE_A", 0.2, 0.1)] },
                    };
                    if (hunting) initial = initial.WithCraft("SHIP", craft => craft with
                    {
                        Logistics = craft.Logistics! with { Status = "STR_OUT", Fuel = 100, Damage = 0, Shield = 0,
                            Longitude = 0.2, Latitude = 0.1, Speed = 0, SpeedRadian = 0, Destination = null },
                    });
                    var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
                    if (targeted)
                    {
                        AssertTimeBlocked(campaign, hunting
                            ? "Alien base hunting can create a mission; immediate hunting mission simulation is unavailable."
                            : "Alien base mission can spawn during its creation tick; immediate generated waves require world simulation.");
                        continue;
                    }
                    AdvanceUnblocked(campaign, 1);
                    var after = campaign.Capture();
                    Assert.Empty(after.World.Ufos);
                    Assert.Equal(hunting ? 0 : 1, Assert.Single(after.World.AlienBases).GenMissionCount);
                    if (hunting)
                    {
                        var mission = Assert.Single(after.World.Missions);
                        Assert.Equal("RETAL_GEN_EMPTY", mission.RegionId);
                        Assert.Equal(objective == 6, mission.Interrupted);
                        Assert.Equal(objective == 6 ? (0, 1) : (2, 0), (mission.NextWave, mission.NextUfoCounter));
                        campaign = Reload(content, campaign, 17, "generated-retaliation-hunt.sav");
                        var due = campaign.Capture() with { Time = new(1, 1, 1, 1999, 0, 29, 55),
                            Bases = campaign.Capture().Bases.Select(owner => owner with { Crafts = owner.Crafts.Select(craft => craft with
                            { Logistics = craft.Logistics is { } state ? state with { LowFuel = true } : null }).ToArray() }).ToArray() };
                        campaign = CampaignState.Restore(due, content, new SplitMix64RandomSource(17));
                        AdvanceUnblocked(campaign, 1);
                    }
                    Assert.Empty(campaign.Capture().World.Missions);
                    Reload(content, campaign, 17, "generated-retaliation-cleanup.sav");
                }
    }
}
