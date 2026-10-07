using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldAlienBaseGenerationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyGeneratedGroundReleaseScoresBeforeAndAfterTakeoffAndReloads(bool cache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var native = TestFixtures.Rows(oracle.RootElement, "spawnPathRows").Single(row =>
            row[0].GetInt32() == 0 && row[1].GetInt32() == 1 && row[2].GetInt32() == 0);
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        foreach (var percentage in new[] { 0, 100 })
        {
            var content = GeneratedWaveContent(cache, 0, groundTimer: 1, hunterPercentage: percentage, missionPoints: 7);
            var initial = GeneratedWaveSnapshot(content, 19);
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
                19, "daily-release-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var ufo = Assert.Single(after.World.Ufos);
            Assert.Equal(UfoStatus.Flying, ufo.Status);
            Assert.Equal(WorldAltitudes.VeryLow, ufo.Altitude);
            Assert.Equal(0, ufo.SecondsRemaining);
            Assert.Equal(native[5].GetInt32(), ufo.Speed);
            Assert.Equal(percentage > 0, ufo.HunterKiller);
            Assert.False(ufo.Hunting);
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 13, after.Regions[0].ActivityAlien[^1]);
            Assert.Equal((1, 1), (Assert.Single(after.World.Missions).NextWave, after.World.Missions[0].LiveUfos));
            campaign = Reload(content, campaign, 19, "daily-release-after.sav");
            AdvanceUnblocked(campaign, 1);
            Reload(content, campaign, 19, "daily-release-next.sav");

            var limited = initial with
            { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 6] }, .. initial.Regions.Skip(1)] };
            AssertTimeBlocked(CampaignState.Restore(limited, content, new SplitMix64RandomSource(19)),
                "UFO alien activity exceeds the supported range.");
        }
    }
}
