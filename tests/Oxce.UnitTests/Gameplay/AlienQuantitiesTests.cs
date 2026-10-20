using Oxce.Core.Random;
using Oxce.Gameplay.Battles.Generation;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

/// <summary>BattlescapeGenerator::deployAliens' quantity per difficulty and its random extras.</summary>
public sealed class AlienQuantitiesTests
{
    private static RuntimeDeploymentData Data(int low, int medium, int high, int difficulty = 0, int extra = 0) =>
        new(0, "", low, medium, high, difficulty, extra, 0, [], []);

    [Theory]
    [InlineData(CampaignDifficulty.Beginner, 2, 2)]
    [InlineData(CampaignDifficulty.Experienced, 4, 2)]
    [InlineData(CampaignDifficulty.Veteran, 6, 6)]
    [InlineData(CampaignDifficulty.Genius, 8, 6)]
    [InlineData(CampaignDifficulty.Superhuman, 10, 10)]
    public void TheBaseFollowsTheDifficultyWithOrWithoutAMediumQuantity(CampaignDifficulty difficulty, int withMedium, int withoutMedium)
    {
        var random = new SplitMix64RandomSource(1);
        Assert.Equal(withMedium, AlienQuantities.Quantity(Data(2, 6, 10), difficulty, false, random));
        Assert.Equal(withoutMedium, AlienQuantities.Quantity(Data(2, 0, 10), difficulty, false, random));
    }

    [Fact]
    public void ExtrasAreRandomUpToTheirMaximumOrTheMaximumForDemigod()
    {
        Assert.Equal(2 + 3 + 4, AlienQuantities.Quantity(Data(2, 0, 2, 3, 4), CampaignDifficulty.Beginner, true, new SplitMix64RandomSource(1)));
        var draws = Enumerable.Range(0, 200).Select(seed =>
            AlienQuantities.Quantity(Data(2, 0, 2, 3, 4), CampaignDifficulty.Beginner, false, new SplitMix64RandomSource((ulong)seed))).ToArray();
        Assert.All(draws, value => Assert.InRange(value, 2, 9));
        Assert.Contains(2, draws);
        Assert.Contains(9, draws);
    }
}
