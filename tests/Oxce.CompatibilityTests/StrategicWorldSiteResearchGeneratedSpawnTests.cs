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

    internal static RuntimeContent Content(bool cache, bool hunting, bool final = false, string interrupt = "CORE", string trajectory = "PATH_0_0") =>
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
              - {ufo: UFO_SCOUT, count: 1, trajectory: {{{trajectory}}}, timer: 0}
        {{{(final ? "" : "      - {count: 1, trajectory: TRAJ_PATROL, timer: 9000}")}}}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment + $$$"""

          - type: GENERATED_RESEARCH_BIRTH_BASE
            markerName: RESEARCH_BIRTH_BASE
            genMission: {GENERATED_RESEARCH_BIRTH: 1}
            genMissionFreq: {{{(hunting ? 0 : 100)}}}
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
