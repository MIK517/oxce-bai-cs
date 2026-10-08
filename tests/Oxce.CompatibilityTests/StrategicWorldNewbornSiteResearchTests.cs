using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornSiteResearchTests
{
    private static readonly string[] SavedThenBornTopics = ["CORE", "BONUS"];
    private static readonly string[] BornTopics = ["BONUS"];

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SavedImmediateSitesGrantAfterSavedSitesAndPersistExactlyOnce(bool cache, bool final)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        for (var savedIndex = 0; savedIndex <= 1; savedIndex++)
        {
            var content = StrategicWorldSiteResearchDirectBirthTests.Content(cache, final, duration: 0,
                interrupt: final ? "MISSING_RESEARCH" : "BONUS");
            var initial = StrategicWorldSiteResearchDirectBirthTests.Snapshot(content);
            if (savedIndex == 0) initial = initial with { World = initial.World with { MissionSites = [] } };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(17)), 17, "immediate-site-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Empty(after.World.MissionSites);
            Assert.Equal(savedIndex == 0 ? BornTopics : SavedThenBornTopics, after.ResearchDiary.Select(entry => entry.RuleId));
            if (final) Assert.Empty(after.World.Missions);
            else Assert.True(Assert.Single(after.World.Missions).Interrupted);
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, 17, "immediate-site-after.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
            AdvanceUnblocked(campaign, 360);
            Assert.Empty(campaign.Capture().World.Missions);
            Assert.Equal(after.ResearchDiary, campaign.Capture().ResearchDiary);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewbornEventChecksItsOwnOrderedGrantAndKeepsUnavailableEffectsAtomic(bool cache)
    {
        var content = StrategicWorldSiteResearchDirectBirthTests.Content(cache, duration: 0, eventInterrupt: "BONUS");
        var initial = StrategicWorldSiteResearchDirectBirthTests.Snapshot(content);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Events);
        Assert.Empty(campaign.Capture().World.MissionSites);
        Assert.Equal(SavedThenBornTopics, campaign.Capture().ResearchDiary.Select(entry => entry.RuleId));

        content = StrategicWorldSiteResearchDirectBirthTests.Content(cache, duration: 0, eventInterrupt: "UNLOCK");
        AssertTimeBlocked(CampaignState.Restore(StrategicWorldSiteResearchDirectBirthTests.Snapshot(content), content, new SplitMix64RandomSource(17)),
            "Mission site despawn requires research or event payloads.");
        content = StrategicWorldSiteResearchDirectBirthTests.Content(cache, duration: 0);
        AssertTimeBlocked(CampaignState.Restore(StrategicWorldSiteResearchDirectBirthTests.Snapshot(content).WithNextId("RESEARCH_BORN_MARKER", int.MaxValue - 1), content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmediateNewbornGrantsRetainDiaryAndActiveMissionValidation(bool cache)
    {
        var content = StrategicWorldSiteResearchDirectBirthTests.Content(cache, duration: 0);
        var initial = StrategicWorldSiteResearchDirectBirthTests.Snapshot(content);
        AssertTimeBlocked(CampaignState.Restore(initial with { ResearchDiarySequence = ulong.MaxValue }, content, new SplitMix64RandomSource(17)),
            "Mission research diary exceeds its supported population or identity range.");
        content = StrategicWorldSiteResearchDirectBirthTests.Content(cache, duration: 0, interrupt: "MISSING_RESEARCH");
        AssertTimeBlocked(CampaignState.Restore(StrategicWorldSiteResearchDirectBirthTests.Snapshot(content), content, new SplitMix64RandomSource(17)),
            "Alien mission interruption references undefined research.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinishedMissionsWithSurvivingUfosDoNotEnterTheImmediateBirthScan(bool cache)
    {
        var content = StrategicWorldSiteResearchGeneratedSpawnTests.Content(cache, hunting: false, final: true);
        var campaign = CampaignState.Restore(StrategicWorldSiteResearchGeneratedSpawnTests.Snapshot(content, hunting: false), content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Equal(1, Assert.Single(campaign.Capture().World.Missions).NextWave);
        Assert.Single(campaign.Capture().World.Ufos);
        var after = campaign.Capture();
        campaign = CampaignState.Restore(after with
        {
            Time = new(2, 2, 1, 1999, 0, 29, 55),
            World = after.World with
            {
                Missions = [after.World.Missions[0] with { Interrupted = false }],
                Ufos = [after.World.Ufos[0] with { Speed = 0 }],
            },
        }, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Single(campaign.Capture().World.Ufos);
    }
}
