using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionRegionRemapTests
{
    [Theory]
    [InlineData(15, "REGION_SECOND", "REGION")]
    [InlineData(16, "REGION_THIRD", "REGION_SECOND")]
    public void MissionRegionRemapsOnceButStrategyRemovalUsesSelectedRegion(int month, string selected, string target)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-alien-base-generation");
        var row = TestFixtures.Rows(oracle.RootElement, "rows").Single(row => row[0].GetString() == "weighted");
        if (month == 15) Assert.Equal(row[3][1].GetString(), target);
        var content = Load();
        var initial = CreateSnapshot(content, "Mission region remapping") with { MonthsPassed = month, RandomState = 17 };
        initial = initial with
        {
            World = initial.World with
            {
                Strategy = new()
                {
                    RegionChances = [new(selected, 1), new(target, 1)],
                    RegionMissions = [new(selected, [new("MISSION_SCOUT", 1)]), new(target, [new("MISSION_SCOUT", 1)])],
                }
            }
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var after = campaign.Capture();
        Assert.Equal(target, Assert.Single(after.World.Missions).RegionId);
        Assert.DoesNotContain(after.World.Strategy.RegionMissions, pair => pair.Key == selected);
        Assert.Equal("MISSION_SCOUT", Assert.Single(after.World.Strategy.RegionMissions.Single(pair => pair.Key == target).Value).Key);
        Reload(content, campaign, 17, "mission-region-remap.sav");
    }

    [Fact]
    public void UndefinedRemapBlocksBeforeRandomnessOrTableChanges()
    {
        var content = Load();
        var initial = CreateSnapshot(content, "Invalid mission remap") with { MonthsPassed = 17, RandomState = 17 };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var before = campaign.Capture();
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    private static Oxce.Mods.Rulesets.Content.RuntimeContent Load()
    {
        TestFixtures.LoadVerifiedManifest("strategic-alien-base-generation");
        return StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-mission-region-remap.rul"]);
    }
}
