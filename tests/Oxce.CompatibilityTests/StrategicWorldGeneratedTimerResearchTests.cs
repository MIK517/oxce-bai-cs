using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldGeneratedTimerResearchTests
{
    [Fact]
    public void NativeInitialTimerSelectsTheWholeBirthSequenceWithoutInventingResearch()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        for (var timer = 59; timer <= 120; timer = timer == 59 ? 60 : timer + 30)
        {
            var content = StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0, generated: true,
                interrupt: "BONUS", eventInterrupt: "BONUS", count: 1, firstTimer: timer);
            var immediateSeen = false;
            var delayedSeen = false;
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = StrategicWorldSiteResearchDirectBirthTests.GeneratedSnapshot(content) with { RandomState = seed };
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                var control = CampaignState.Restore(initial with { World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                AdvanceUnblocked(control, 1);
                var after = campaign.Capture();
                var mission = Assert.Single(after.World.Missions);
                var immediate = mission.NextWave == 1;
                Assert.Equal(1, Assert.Single(after.World.AlienBases).GenMissionCount);
                Assert.Equal(immediate, after.CompletedResearch.Contains("BONUS"));
                Assert.Equal(immediate, mission.Interrupted);
                Assert.Equal(immediate ? 2 : 1, after.ResearchDiary.Count);
                Assert.Equal("CORE", after.ResearchDiary[0].RuleId);
                Assert.Empty(after.World.MissionSites);
                Assert.Empty(after.World.Events);
                Assert.Equal(0, mission.NextUfoCounter);
                Assert.Equal(control.Capture().RandomState, after.RandomState);
                if (!immediate) Assert.InRange(mission.SpawnCountdown, 30, timer / 30 * 45);
                immediateSeen |= immediate;
                delayedSeen |= !immediate;
                Assert.Equivalent(after, Reload(content, campaign, seed, "generated-timer-research.sav").Capture(), strict: true);
            }
            Assert.Equal(timer < 120, immediateSeen);
            Assert.Equal(timer >= 60, delayedSeen);
        }
    }

    [Fact]
    public void OnlyAlwaysDueInitialTimersCanGuaranteeResearchForDueEventSuppression()
    {
        for (var timer = 59; timer <= 120; timer = timer == 59 ? 60 : timer + 30)
        {
            var content = StrategicWorldSiteResearchDirectBirthTests.Content(duration: 0, generated: true,
                interrupt: "BONUS", eventInterrupt: "BONUS", count: 1, firstTimer: timer);
            var initial = StrategicWorldSiteResearchDirectBirthTests.GeneratedSnapshot(content);
            var campaign = CampaignState.Restore(initial with
            {
                World = initial.World with { Events = [new GeoscapeEventSnapshot("NEWBORN_EVENT", 30)] },
            }, content, new SplitMix64RandomSource(17));
            if (timer == 59)
            {
                AdvanceUnblocked(campaign, 1);
                Assert.Empty(campaign.Capture().World.Events);
            }
            else AssertTimeBlocked(campaign, "Strategic event consequences require campaign event execution (Phase 6 branch 6).");
        }
    }
}
