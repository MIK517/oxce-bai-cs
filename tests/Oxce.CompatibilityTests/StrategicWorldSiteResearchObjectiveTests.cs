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
    [Fact]
    public void CounterOnlyObjectivePrefixesRunBeforeSiteInterruption()
    {
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        for (var objective = 0; objective <= 6; objective++)
        {
            var content = Content(objective);
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
            campaign = ReloadRepeatedly(content, campaign, 66, "site-research-objective-prefix.sav", 3, after);
            AdvanceUnblocked(campaign, 360);
            Assert.Empty(campaign.Capture().World.Missions);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FinalObjectiveTransitionsCompleteBeforeSiteResearchAndCleanup(int objective)
    {
        TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-infiltration-region");
        var content = Content(objective, prefix: false);
        var initial = Snapshot(content, objective);
        var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(66));
        var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(66)), 66, "objective-research-before.sav");
        AdvanceUnblocked(control, 1);
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Single(after.World.AlienBases);
        Assert.Equivalent(control.Capture().World.AlienBases, after.World.AlienBases, strict: true);
        Assert.Equal(control.Capture().Countries.Select(country => (country.Pact, country.NewPact, country.CancelPact)),
            after.Countries.Select(country => (country.Pact, country.NewPact, country.CancelPact)));
        Assert.Equal(control.Capture().RandomState, after.RandomState);
        Assert.Equal(objective == 1, after.Countries.Any(country => country.NewPact));
        Assert.Empty(after.World.Missions);
        Assert.Empty(after.World.Ufos);
        Assert.Empty(after.World.MissionSites);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        campaign = ReloadRepeatedly(content, campaign, 66, "objective-research-after.sav", 3, after);
    }

    [Fact]
    public void InstantRetaliationWithATargetNeedsAUfoRule()
    {
        var content = Content(6);
        var initial = Snapshot(content, 6);
        initial = initial with { World = initial.World with { Missions = [initial.World.Missions[0] with { RegionId = "REGION" }] } };
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(66)),
            "Retaliation assault spawning requires a UFO rule.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void SavedCompletedCounterMissionDoesNotResolveALinkAfterItsRemoval(int objective)
    {
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        var content = Content(objective, prefix: false, interrupt: "MISSING_RESEARCH");
        var initial = Snapshot(content, objective);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(66));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Empty(after.World.Missions);
        Assert.Empty(after.World.MissionSites);
        if (objective is 1 or 2)
        {
            TestFixtures.LoadVerifiedManifest("strategic-objective-base-spawn");
            var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(66));
            AdvanceUnblocked(control, 1);
            Assert.Equal(control.Capture().RandomState, after.RandomState);
            Assert.Single(after.World.AlienBases);
            Assert.Equivalent(control.Capture().World.AlienBases, after.World.AlienBases, strict: true);
        }
        else Assert.Equal(initial.RandomState, after.RandomState);
        Assert.Equal("CORE", Assert.Single(after.ResearchDiary).RuleId);
        Assert.Equivalent(after, Reload(content, campaign, 66, "removed-saved-research-link.sav").Capture(), strict: true);
    }

    [Fact]
    public void SavedSurvivingPrefixStillResolvesItsInterruptionLink()
    {
        var content = Content(3, interrupt: "MISSING_RESEARCH");
        AssertTimeBlocked(CampaignState.Restore(Snapshot(content, 3), content, new SplitMix64RandomSource(66)),
            "Alien mission interruption references undefined research.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FinalObjectiveMarkerExhaustionStopsBeforeBasePactAndSiteGrant(int objective)
    {
        var content = Content(objective, prefix: false);
        var initial = Snapshot(content, objective).WithNextId("STR_ALIEN_BASE", int.MaxValue);
        AssertTimeBlocked(CampaignState.Restore(initial, content, new SplitMix64RandomSource(66)),
            "Base objective IDs are exhausted or collide.");
    }

    internal static RuntimeContent Content(int objective, bool prefix = true, string interrupt = "CORE") => StrategicReadinessTestContent.Load(
        ["strategic-world.rul"], $$"""
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
