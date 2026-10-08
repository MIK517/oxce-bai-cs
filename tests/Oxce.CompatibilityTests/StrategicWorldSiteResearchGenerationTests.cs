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

    internal static RuntimeContent Content(bool cache, string interrupt = "CORE") => StrategicReadinessTestContent.Load(
        ["strategic-world.rul", "strategic-alien-base-generation.rul"], cache, $$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: CORE, cost: 1}
        alienMissions:
          - {type: GEN_local, interruptResearch: {{interrupt}}}
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
