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
    [InlineData(false, 50)]
    [InlineData(true, 50)]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    public void DailyGeneratedAirborneHuntersWithNoPossibleCraftKeepTheirNativeWaypoint(bool cache, int percentage)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-hunting");
        var constructor = TestFixtures.Rows(oracle.RootElement, "constructorRows").Where(row => row[0].GetInt32() == percentage).ToArray();
        Assert.NotEmpty(constructor);
        var content = GeneratedWaveContent(cache, 0, hunterPercentage: percentage);
        HashSet<bool> flags = [];
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var initial = GeneratedWaveSnapshot(content, seed);
            var owner = initial.Bases[0];
            initial = initial with { Bases = [owner with { Crafts = [], Soldiers = [] }] };
            var campaign = Reload(content, CampaignState.Restore(initial, content, new SplitMix64RandomSource(seed)),
                seed, "idle-generated-hunter-before.sav");
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            var born = Assert.Single(after.World.Ufos);
            flags.Add(born.HunterKiller);
            Assert.Contains(constructor, row => row[4].GetBoolean() == born.HunterKiller);
            Assert.Equal(UfoStatus.Flying, born.Status);
            Assert.False(born.Hunting);
            Assert.False(born.Escorting);
            Assert.Null(born.OriginalWaypoint);
            Assert.Equal(WorldTargetKind.Waypoint, born.Destination!.Kind);
            Assert.Equal(2200, born.Speed);
            Assert.Equal(initial.Regions[0].ActivityAlien[^1] + 3, after.Regions[0].ActivityAlien[^1]);
            Assert.Equal(1, Assert.Single(after.World.Missions).LiveUfos);
            campaign = Reload(content, campaign, seed, "idle-generated-hunter-after.sav");
            AdvanceUnblocked(campaign, 12);
            Reload(content, campaign, seed, "idle-generated-hunter-following.sav");
        }
        Assert.True(flags.SetEquals(percentage == 100 ? [true] : [false, true]));
        var exhausted = GeneratedWaveSnapshot(content, 19);
        exhausted = exhausted with { Bases = [exhausted.Bases[0] with { Crafts = [], Soldiers = [] }] };
        AssertTimeBlocked(CampaignState.Restore(exhausted.WithNextId("STR_UFO_UNIQUE", int.MaxValue), content,
            new SplitMix64RandomSource(19)), "UFO unique IDs are exhausted or collide with a saved UFO.");
    }
}
