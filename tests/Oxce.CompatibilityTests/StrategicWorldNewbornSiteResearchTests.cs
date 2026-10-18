using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornSiteResearchTests
{
    private static readonly string[] SavedThenBornTopics = ["CORE", "BONUS"];
    private static readonly string[] BornTopics = ["BONUS"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedImmediateSitesGrantAfterSavedSitesAndPersistExactlyOnce(bool final)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        for (var savedIndex = 0; savedIndex <= 1; savedIndex++)
        {
            var content = StrategicWorldSiteResearchDirectBirthTests.Content(final, duration: 0,
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

    [Fact]
    public void NewbornEventChecksItsOwnOrderedGrantAndKeepsUnavailableEffectsAtomic()
    {
        var content = StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0, eventInterrupt: "BONUS");
        var initial = StrategicWorldSiteResearchDirectBirthTests.Snapshot(content);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Events);
        Assert.Empty(campaign.Capture().World.MissionSites);
        Assert.Equal(SavedThenBornTopics, campaign.Capture().ResearchDiary.Select(entry => entry.RuleId));

        content = StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0, eventInterrupt: "UNLOCK");
        AssertEventTickRuns(CampaignState.Restore(StrategicWorldSiteResearchDirectBirthTests.Snapshot(content), content, new SplitMix64RandomSource(17)), popped: ["NEWBORN_EVENT", "NEWBORN_EVENT"]);
        content = StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0);
        AssertTimeBlocked(CampaignState.Restore(StrategicWorldSiteResearchDirectBirthTests.Snapshot(content).WithNextId("RESEARCH_BORN_MARKER", int.MaxValue - 1), content, new SplitMix64RandomSource(17)),
            "Mission site marker IDs are exhausted or collide with a saved target.");
    }

    [Fact]
    public void ImmediateNewbornGrantsRetainDiaryAndActiveMissionValidation()
    {
        var content = StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0);
        var initial = StrategicWorldSiteResearchDirectBirthTests.Snapshot(content);
        AssertTimeBlocked(CampaignState.Restore(initial with { ResearchDiarySequence = ulong.MaxValue }, content, new SplitMix64RandomSource(17)),
            "Mission research diary exceeds its supported population or identity range.");
        content = StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0, interrupt: "MISSING_RESEARCH");
        AssertTimeBlocked(CampaignState.Restore(StrategicWorldSiteResearchDirectBirthTests.Snapshot(content), content, new SplitMix64RandomSource(17)),
            "Alien mission interruption references undefined research.");
    }

    [Fact]
    public void FinishedMissionsWithSurvivingUfosDoNotEnterTheImmediateBirthScan()
    {
        var content = StrategicWorldSiteResearchGeneratedSpawnTests.Content(hunting: false, final: true);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyImmediateBirthsGrantAfterSavedSitesAndPreserveRemovedMissionLinks(bool final)
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        for (var objective = 0; objective <= 3; objective += 3)
        {
            var content = StrategicWorldSiteResearchDirectBirthTests.Content(final, duration: 0, generated: true,
                objective: objective, interrupt: final ? "MISSING_RESEARCH" : "BONUS");
            var initial = StrategicWorldSiteResearchDirectBirthTests.GeneratedSnapshot(content);
            for (var savedIndex = 0; savedIndex <= 1; savedIndex++)
            {
                var snapshot = initial with { World = initial.World with { MissionSites = savedIndex == 0 ? [] : initial.World.MissionSites } };
                var campaign = Reload(content, CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)), 17, "daily-immediate-before.sav");
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                Assert.Empty(after.World.MissionSites);
                Assert.Equal(savedIndex == 0 ? BornTopics : SavedThenBornTopics, after.ResearchDiary.Select(entry => entry.RuleId));
                Assert.Equal(1, Assert.Single(after.World.AlienBases).GenMissionCount);
                if (final) Assert.Empty(after.World.Missions);
                else Assert.True(Assert.Single(after.World.Missions).Interrupted);
                for (var pass = 0; pass < 3; pass++)
                {
                    campaign = Reload(content, campaign, 17, "daily-immediate-after.sav");
                    Assert.Equivalent(after, campaign.Capture(), strict: true);
                }
                AdvanceUnblocked(campaign, 360);
                Assert.Empty(campaign.Capture().World.Missions);
                Assert.Equal(after.ResearchDiary, campaign.Capture().ResearchDiary);
            }
        }
    }

    [Fact]
    public void DailyOptionalGenerationCannotGuaranteeInterruptionOfDueEvents()
    {
        var content = StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0, generated: true, frequency: 50, eventInterrupt: "BONUS");
        var initial = StrategicWorldSiteResearchDirectBirthTests.GeneratedSnapshot(content);
        AssertEventTickRuns(CampaignState.Restore(initial with
        {
            World = initial.World with
            { Events = [new GeoscapeEventSnapshot("NEWBORN_EVENT", 30)] }
        }, content, new SplitMix64RandomSource(17)), popped: ["NEWBORN_EVENT"]);
        content = StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0, generated: true, eventInterrupt: "BONUS");
        var campaign = CampaignState.Restore(StrategicWorldSiteResearchDirectBirthTests.GeneratedSnapshot(content), content, new SplitMix64RandomSource(17));
        AdvanceUnblocked(campaign, 1);
        Assert.Empty(campaign.Capture().World.Events);
        content = StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0, generated: true, eventInterrupt: "UNLOCK");
        AssertEventTickRuns(CampaignState.Restore(StrategicWorldSiteResearchDirectBirthTests.GeneratedSnapshot(content), content, new SplitMix64RandomSource(17)), popped: ["NEWBORN_EVENT", "NEWBORN_EVENT"]);
    }

    [Fact]
    public void SuppressedMissionBirthCannotInventResearchToBypassDueEventBoundary()
    {
        var content = StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0, eventInterrupt: "BONUS");
        var initial = StrategicWorldSiteResearchDirectBirthTests.Snapshot(content);
        for (var savedIndex = 0; savedIndex <= 1; savedIndex++)
        {
            var snapshot = initial with
            {
                World = initial.World with
                {
                    Missions = [initial.World.Missions[0] with { MultiUfoRetaliationInProgress = true }],
                    MissionSites = savedIndex == 0 ? [] : initial.World.MissionSites,
                    Events = [new GeoscapeEventSnapshot("NEWBORN_EVENT", 30)],
                },
            };
            AssertEventTickRuns(CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17)), popped: ["NEWBORN_EVENT"]);
        }
    }
}
