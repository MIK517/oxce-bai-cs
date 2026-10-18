using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldPathSlotResearchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedPathsKeepMissingAndLaterGrantSlotsExclusive(bool common)
    {
        TestFixtures.LoadVerifiedManifest("strategic-site-spawn");
        TestFixtures.LoadVerifiedManifest("strategic-site-timer");
        TestFixtures.LoadVerifiedManifest("strategic-wave-chain");
        for (var suppressedIndex = 0; suppressedIndex <= 1; suppressedIndex++)
        {
            var suppressed = suppressedIndex == 1;
            var content = StrategicWorldRegionResearchTests.Content(true, secondDuration: 1, suppressed: suppressed, common: common);
            var firstSeen = false;
            var otherSeen = false;
            for (ulong seed = 1; seed <= 32; seed++)
            {
                var initial = StrategicWorldRegionResearchTests.Snapshot(content) with { RandomState = seed };
                var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed));
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var first = after.NextIds.GetValueOrDefault("PATH_A_MARKER", 1) != 1;
                Assert.Equal(first, after.CompletedResearch.Contains("CORE"));
                Assert.Equal(common, after.CompletedResearch.Contains("BONUS"));
                Assert.Equal((first ? 1 : 0) + (common ? 1 : 0), after.ResearchDiary.Count);
                Assert.Equal(common, after.NextIds.GetValueOrDefault("PATH_COMMON_MARKER", 1) == 2);
                Assert.Equal(first || suppressed ? 0 : common ? 1 : 2, after.World.MissionSites.Count);
                Assert.All(after.World.MissionSites, site =>
                {
                    Assert.Equal("PATH_B_SITE", site.DeploymentId);
                    Assert.Equal(1800, site.SecondsRemaining);
                });
                Assert.Empty(after.World.Missions);
                Assert.Empty(after.World.Events);
                Assert.Equal(1, Assert.Single(after.World.AlienBases).GenMissionCount);
                Assert.Equivalent(after, Reload(content, campaign, seed, "generated-slot-research.sav").Capture(), strict: true);
                firstSeen |= first;
                otherSeen |= !first;
            }
            Assert.True(firstSeen);
            Assert.True(otherSeen);
        }
    }

    [Fact]
    public void MissingPathCannotGuaranteeResearchButACommonLaterSlotCan()
    {
        for (var commonIndex = 0; commonIndex <= 1; commonIndex++)
        {
            var common = commonIndex == 1;
            var content = StrategicWorldRegionResearchTests.Content(true, suppressed: true, common: common);
            var initial = StrategicWorldRegionResearchTests.Snapshot(content);
            AssertEventTickRuns(CampaignState.Restore(initial with
            {
                World = initial.World with { Events = [new GeoscapeEventSnapshot("PATH_A_EVENT", 30)] },
            }, content, new SplitMix64RandomSource(17)), popped: []);
            if (common)
            {
                var campaign = CampaignState.Restore(initial with
                {
                    World = initial.World with { Events = [new GeoscapeEventSnapshot("PATH_B_EVENT", 30)] },
                }, content, new SplitMix64RandomSource(17));
                AdvanceUnblocked(campaign, 1);
                Assert.Contains("BONUS", campaign.Capture().CompletedResearch);
                Assert.Empty(campaign.Capture().World.Events);
            }
        }
    }
}
