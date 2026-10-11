using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldAlienBaseGenerationTests
{
    [Fact]
    public void DailyGeneratedGroundReleaseScoresBeforeAndAfterTakeoffAndReloads()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-lifecycle");
        var native = TestFixtures.Rows(oracle.RootElement, "spawnPathRows").Single(row =>
            row[0].GetInt32() == 0 && row[1].GetInt32() == 1 && row[2].GetInt32() == 0);
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        foreach (var percentage in new[] { 0, 100 })
        {
            var content = GeneratedWaveContent(0, groundTimer: 1, hunterPercentage: percentage, missionPoints: 7);
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

    [Fact]
    public void HuntingGeneratedGroundReleaseMissesHalfHourScoringButReservesTakeoff()
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        foreach (var minute in new[] { 9, 29 })
            foreach (var percentage in new[] { 0, 100 })
            {
                var content = GeneratedWaveContent(0, hunting: true, groundTimer: 1,
                    hunterPercentage: percentage, missionPoints: 7);
                var initial = HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 0, minute, 55));
                var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(19)),
                    19, "hunting-release-before.sav");
                AdvanceUnblocked(campaign, 1);
                var after = campaign.Capture();
                var born = Assert.Single(after.World.Ufos);
                Assert.Equal((UfoStatus.Flying, 0, 2200), (born.Status, born.SecondsRemaining, born.Speed));
                Assert.Equal(percentage > 0, born.HunterKiller);
                Assert.False(born.Hunting);
                Assert.False(born.Detected);
                Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 7, after.Regions[0].ActivityAlien[^1]);
                Assert.Equal(0, after.World.AlienBases[0].MinutesSinceLastHuntMissionGeneration);
                campaign = Reload(content, campaign, 19, "hunting-release-after.sav");
                AdvanceUnblocked(campaign, 1);
                Reload(content, campaign, 19, "hunting-release-next.sav");

                var limited = initial with
                { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 6] }, .. initial.Regions.Skip(1)] };
                AssertTimeBlocked(CampaignState.Restore(limited, content, new SplitMix64RandomSource(19)),
                    "UFO alien activity exceeds the supported range.");
            }
    }

    [Fact]
    public void DailyAndHuntingGroundReleaseShareCreationTickActivityLimits()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-hunt-generation");
        var content = GeneratedWaveContent(0, hunting: true, combined: true, groundTimer: 1, missionPoints: 7);
        var initial = HuntingGeneratedWaveSnapshot(content, new(1, 1, 1, 1999, 23, 59, 55));
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(19));
        AdvanceUnblocked(campaign, 1);
        var after = campaign.Capture();
        Assert.Equal(2, after.World.Ufos.Count);
        Assert.All(after.World.Ufos, ufo => Assert.Equal(UfoStatus.Flying, ufo.Status));
        Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 20, after.Regions[0].ActivityAlien[^1]);
        Reload(content, campaign, 19, "combined-ground-release.sav");
        var limited = initial with
        { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - 19] }, .. initial.Regions.Skip(1)] };
        AssertTimeBlocked(CampaignState.Restore(limited, content, new SplitMix64RandomSource(19)),
            "UFO alien activity exceeds the supported range.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedGroundEscortReleaseRetainsRelocationAndRestoresTrajectorySpeed(bool hunting)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-world-lifecycle");
        foreach (var leader in new[] { false, true })
        {
            var native = TestFixtures.Rows(oracle.RootElement, "spawnEscortRows").Single(row =>
                row[0].GetBoolean() && row[1].GetBoolean() && row[2].GetInt32() == (leader ? 3 : 0) &&
                row[3].GetInt32() == 0 && row[12].GetInt32() == 1);
            var content = GeneratedEscortContent(hunting, ground: true, leader: leader,
                groundTimer: 1, missionPoints: 7);
            var initial = GeneratedEscortSnapshot(content, hunting);
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(81)),
                81, "escort-release-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var born = Assert.Single(after.World.Ufos, ufo => ufo.RuleId == "GENERATED_ESCORT");
            Assert.Equal(native[4].GetBoolean(), born.Escort);
            Assert.Equal(native[5].GetBoolean(), born.Escorting);
            Assert.Equal((UfoStatus.Flying, 0, 550), (born.Status, born.SecondsRemaining, born.Speed));
            Assert.False(born.Hunting);
            Assert.Equal(new WorldPosition(Degrees(leader ? 8 : 10), Degrees(leader ? 2 : 8)), born.Position);
            var score = hunting ? 7 : leader ? 16 : 13;
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + score, after.Regions[0].ActivityAlien[^1]);
            if (leader)
            {
                Assert.Equal(after.World.Ufos[0].UniqueId, born.Destination!.UniqueId);
                Assert.Equal(WorldPosition.Create(Degrees(362), Degrees(2)), born.OriginalWaypoint);
            }
            else Assert.Null(born.OriginalWaypoint);
            campaign = Reload(content, campaign, 81, "escort-release-after.sav");
            AdvanceUnblocked(campaign, 12);
            Reload(content, campaign, 81, "escort-release-following.sav");
            var limited = initial with
            { Regions = [initial.Regions[0] with { ActivityAlien = [int.MaxValue - score + 1] }, .. initial.Regions.Skip(1)] };
            AssertTimeBlocked(CampaignState.Restore(limited, content, new SplitMix64RandomSource(81)),
                "UFO alien activity exceeds the supported range.");
        }
    }
}
