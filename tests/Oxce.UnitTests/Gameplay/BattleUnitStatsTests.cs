using Oxce.Gameplay.Battles;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

/// <summary>UnitStats: rule keys, Sint16 sums and obeyFixedMinimum.</summary>
public sealed class BattleUnitStatsTests
{
    [Fact]
    public void RuleKeysFillTheirStatisticsAndMissingKeysAreZero()
    {
        var stats = BattleUnitStats.From(new Dictionary<string, short> { ["tu"] = 50, ["psiSkill"] = 3, ["mana"] = 7 });
        Assert.Equal(new BattleUnitStats(50, 0, 0, 0, 0, 0, 0, 0, 0, 3, 0, 7), stats);
    }

    [Fact]
    public void SumsWrapLikeSixteenBitFields()
    {
        var sum = new BattleUnitStats(short.MaxValue, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11) + new BattleUnitStats(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
        Assert.Equal(new BattleUnitStats(short.MinValue, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12), sum);
    }

    [Fact]
    public void TheFixedMinimumIsOneHealthAndZeroForTheRest()
    {
        var stats = new BattleUnitStats(-1, -2, -3, -4, -5, -6, -7, -8, -9, -10, -11, -12).ObeyFixedMinimum();
        Assert.Equal(new BattleUnitStats(0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0), stats);
    }
}
