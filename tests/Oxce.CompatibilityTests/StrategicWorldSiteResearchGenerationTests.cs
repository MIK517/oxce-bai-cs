using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyDelayedMissionExistsBeforeSiteResearchAndReceivesItsInterruption(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var row = TestFixtures.Rows(oracle.RootElement, "rows").Single(row => row[0].GetString() == "local");
        var content = Content(cache);
        var initial = Snapshot(content);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.MissionSites);
        var mission = Assert.Single(after.World.Missions);
        Assert.Equal("GEN_local", mission.RuleId);
        Assert.True(mission.Interrupted);
        Assert.Equal((0, 0, 0), (mission.NextWave, mission.NextUfoCounter, mission.LiveUfos));
        Assert.Equal(row[3][1].GetString(), mission.RegionId);
        Assert.Equal(row[3][2].GetString(), mission.Race);
        Assert.Equal(1, Assert.Single(after.World.AlienBases).GenMissionCount);
        Assert.Equal(2, mission.AlienBase!.Id);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        var random = new SplitMix64RandomSource(initial.RandomState);
        foreach (var call in row[4].EnumerateArray()) random.NextInclusive(call[0].GetInt32(), call[1].GetInt32());
        Assert.Equal(random.State, after.RandomState);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-research-generated-delayed.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
        AdvanceUnblocked(campaign, 360);
        Assert.Empty(campaign.Capture().World.Missions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidGeneratedInterruptionStopsBeforeDailyCreation(bool cache)
    {
        var content = Content(cache, "MISSING_RESEARCH");
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content), content, new SplitMix64RandomSource(17)),
            "Alien mission interruption references undefined research.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedImmediateMissionDoesNotResolveAnUnusedInterruptionRule(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = Content(cache, "MISSING_RESEARCH");
        var initial = Snapshot(content);
        initial = initial with
        {
            World = initial.World with
            { AlienBases = [initial.World.AlienBases[0] with { DeploymentId = "GEN_BASE_immediateZero" }] }
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.Missions);
        Assert.Empty(after.World.MissionSites);
        Assert.Contains("CORE", after.CompletedResearch);
        Assert.Equivalent(after, Reload(content, campaign, 17, "removed-generated-interruption.sav").Capture(), strict: true);
    }

    [Theory]
    [InlineData(false, 29, 2)]
    [InlineData(true, 29, 2)]
    [InlineData(false, 90, 1)]
    [InlineData(true, 90, 1)]
    public void PotentiallySurvivingImmediateMissionStillValidatesItsInterruption(bool cache, int timer, int count)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$"""
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1}
            alienMissions:
              - type: SURVIVING_COUNTER
                interruptResearch: MISSING_RESEARCH
                waves: [{count: {{count}}, trajectory: TRAJ_PATROL, timer: {{timer}}}]
            """ + "\n" + StrategicWorldSiteResearchTests.Deployment + """

              - type: SURVIVING_COUNTER_BASE
                markerName: SURVIVING
                genMission: {SURVIVING_COUNTER: 1}
                genMissionFreq: 100
                genMissionLimit: 1
            """);
        var missionRule = content.RuntimeRules.AlienMissions[content.RuntimeRules.AlienMissions.GetRequired("SURVIVING_COUNTER")].Value;
        Assert.Equal((ulong)timer, missionRule.Waves[0].SpawnTimer);
        Assert.Equal((ulong)count, missionRule.Waves[0].UfoCount);
        Assert.Equal("MISSING_RESEARCH", missionRule.InterruptResearchId);
        var initial = Snapshot(content);
        initial = initial with
        {
            World = initial.World with
            { AlienBases = [initial.World.AlienBases[0] with { DeploymentId = "SURVIVING_COUNTER_BASE" }] }
        };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)),
            "Alien mission interruption references undefined research.");
    }

    [Fact]
    public void DailyMissionBirthsConsumeResearchValidationWorkBeforeMutation()
    {
        var content = Content(false);
        var campaign = CampaignState.Restore(Snapshot(content), content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        var forecast = campaign.MissionResearch.CreateForecast(precedingWork: 800_000, precedingMissionBirths: 100_000);
        Assert.Equal("Mission research exceeds the supported discovery work limit.", forecast.Advance("CORE"));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyCounterOnlyCreationRunsBeforeSiteResearchWithoutActorBirths(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "immediateRows"))
        {
            var name = row[0].GetString()!;
            var content = Content(cache);
            var initial = Snapshot(content);
            initial = initial with
            {
                World = initial.World with
                { AlienBases = [initial.World.AlienBases[0] with { DeploymentId = "GEN_BASE_" + name }] }
            };
            var random = new SplitMix64RandomSource(initial.RandomState);
            foreach (var call in row[4].EnumerateArray()) random.NextInclusive(call[0].GetInt32(), call[1].GetInt32());
            // The real initial timer draw is the last captured call, not an
            // additional draw. Replay it from the preceding generator state.
            var timerRandom = new SplitMix64RandomSource(initial.RandomState);
            foreach (var call in row[4].EnumerateArray().Take(row[4].GetArrayLength() - 1))
                timerRandom.NextInclusive(call[0].GetInt32(), call[1].GetInt32());
            var countdown = WorldTrajectory.SpawnCountdown(name == "immediate" ? 90 : 0, timerRandom);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Empty(after.World.Ufos);
            Assert.Empty(after.World.MissionSites);
            Assert.Equal(random.State, after.RandomState);
            Assert.Equal(1, Assert.Single(after.World.AlienBases).GenMissionCount);
            Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
            if (countdown <= 30) Assert.Empty(after.World.Missions);
            else
            {
                var mission = Assert.Single(after.World.Missions);
                Assert.True(mission.Interrupted);
                Assert.Equal((0, countdown - 30), (mission.NextWave, mission.SpawnCountdown));
            }
            Assert.Equivalent(after, Reload(content, campaign, 17, "site-research-generated-counter.sav").Capture(), strict: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void GeneratedZeroTimerChainsComposeWithinTheirExistingWorkLimit(bool cache, bool excessive)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-wave-chain");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], cache,
            "research:\n  - {name: UNLOCK, cost: 1}\n  - {name: CORE, cost: 1}\n" +
            StrategicWorldSiteResearchTests.Deployment + $$"""

              - type: GENERATED_CHAIN_BASE
                markerName: CHAIN_BASE
                genMission: {CHAIN_{{(excessive ? "excessive" : "zero")}}: 1}
                genMissionFreq: 100
                genMissionLimit: 1
            """);
        var initial = Snapshot(content);
        initial = initial with
        {
            World = initial.World with
            { AlienBases = [initial.World.AlienBases[0] with { DeploymentId = "GENERATED_CHAIN_BASE" }] }
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        if (excessive)
            AssertTimeBlocked(campaign, "Immediate mission waves exceed the supported work limit.");
        else
        {
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Empty(after.World.Missions);
            Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.Ufos);
            Assert.Equal(1, Assert.Single(after.World.AlienBases).GenMissionCount);
            var random = new SplitMix64RandomSource(initial.RandomState);
            random.NextInclusive(1, 1);
            random.NextInclusive(0, 99);
            random.NextInclusive(0, 99);
            random.NextInclusive(0, 0); // Initial start timer.
            var row = TestFixtures.Rows(oracle.RootElement, "cases").Single(row => row.GetProperty("name").GetString() == "zero");
            var draws = TestFixtures.Rows(row, "rows").First()[4].GetInt32();
            for (var draw = 0; draw < draws; draw++) random.NextInclusive(0, 0);
            Assert.Equal(random.State, after.RandomState);
            Assert.Equivalent(after, Reload(content, campaign, 17, "generated-research-chain.sav").Capture(), strict: true);
        }
    }

    internal static RuntimeContent Content(bool cache, string interrupt = "CORE") => StrategicReadinessTestContent.Load(
        ["strategic-world.rul", "strategic-alien-base-generation.rul"], cache, $$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
        alienMissions:
          - {type: GEN_local, interruptResearch: {{interrupt}}}
          - {type: GEN_immediate, interruptResearch: {{interrupt}}}
          - {type: GEN_immediateZero, interruptResearch: {{interrupt}}}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment);

    internal static CampaignSnapshot Snapshot(RuntimeContent content)
    {
        var initial = StrategicWorldSiteResearchTests.Snapshot(content);
        return initial.WithNextId("ALIEN_MISSIONS", 1) with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            Bases = [initial.Bases[0] with { Research = [] }],
            World = initial.World with
            {
                Missions = [],
                AlienBases = [new(2, "GEN_BASE_local", "RACE_A", 0.2, 0.1) { Discovered = true }],
            },
        };
    }
}
