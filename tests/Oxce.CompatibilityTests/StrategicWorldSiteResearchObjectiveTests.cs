using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchObjectiveTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CounterOnlyObjectivePrefixesRunBeforeSiteInterruption(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        for (var objective = 0; objective <= 6; objective++)
        {
            var content = Content(cache, objective);
            var initial = Snapshot(content, objective);
            var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(66));
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Empty(after.World.Ufos);
            Assert.Empty(after.World.MissionSites);
            Assert.Empty(after.World.AlienBases);
            Assert.All(after.Countries, country => Assert.False(country.NewPact));
            Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
            var expected = new SplitMix64RandomSource(initial.RandomState);
            var countdown = WorldTrajectory.SpawnCountdown(9000, expected);
            Assert.Equal(expected.State, after.RandomState);
            if (objective == 6) Assert.Empty(after.World.Missions);
            else
            {
                var mission = Assert.Single(after.World.Missions);
                Assert.Equal((1, 0, countdown), (mission.NextWave, mission.NextUfoCounter, mission.SpawnCountdown));
                Assert.True(mission.Interrupted);
            }
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 66, "site-research-objective-prefix.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 360);
            Assert.Empty(campaign.Capture().World.Missions);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void FinalObjectiveTransitionsKeepTheirCompositionBoundary(bool cache, int objective)
    {
        var content = Content(cache, objective, prefix: false);
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, objective), content, new SplitMix64RandomSource(66)),
            "Mission site research requires mission-wave composition forecasts.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstantRetaliationWithATargetKeepsTheBaseDefenseBoundary(bool cache)
    {
        var content = Content(cache, 6);
        var initial = Snapshot(content, 6);
        initial = initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { RegionId = "REGION" }] } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(66)),
            "Retaliation assault arrival requires base-defense simulation.");
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    public void SavedCompletedCounterMissionDoesNotResolveALinkAfterItsRemoval(bool cache, int objective)
    {
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var content = Content(cache, objective, prefix: false, interrupt: "MISSING_RESEARCH");
        var initial = Snapshot(content, objective);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(66));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.Missions);
        Assert.Empty(after.World.MissionSites);
        Assert.Equal(initial.RandomState, after.RandomState);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        Assert.Equivalent(after, Reload(content, campaign, 66, "removed-saved-research-link.sav").Capture(), strict: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedSurvivingPrefixStillResolvesItsInterruptionLink(bool cache)
    {
        var content = Content(cache, 3, interrupt: "MISSING_RESEARCH");
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 3), content, new SplitMix64RandomSource(66)),
            "Alien mission interruption references undefined research.");
    }

    internal static RuntimeContent Content(bool cache, int objective, bool prefix = true, string interrupt = "CORE") => StrategicReadinessTestContent.Load(
        ["strategic-world.rul"], cache, $$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
        regions:
          - {type: OUTSIDE_COUNTER_REGION, areas: [[90, 100, -10, 10]]}
        alienMissions:
          - type: OWNED_COUNTER_PREFIX
            objective: {{objective}}
            endlessInfiltration: false
            interruptResearch: {{interrupt}}
            spawnZone: 1
            siteType: ALIEN_BASE_DEPLOY
            waves:
              - {count: 1, trajectory: TRAJ_PATROL, timer: 0}
        {{(prefix ? "      - {count: 1, trajectory: TRAJ_PATROL, timer: 9000}" : "")}}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, int objective)
    {
        var initial = StrategicWorldSiteResearchTests.Snapshot(content);
        return initial with
        {
            RandomState = 66,
            Bases = [initial.Bases[0] with { Research = [] }],
            World = initial.World with
            { Missions = [new(4, "OWNED_COUNTER_PREFIX", objective == 6 ? "OUTSIDE_COUNTER_REGION" : "REGION", "RACE_A", 0, 0, 0, 0, -1)] },
        };
    }
}
