using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.UnitTests.Mods;

/// <summary>Texture::getRandomTerrain: a weighted pick among criteria whose area contains the target.</summary>
public sealed class RuntimeTerrainCriterionTests
{
    private static readonly RuntimeTerrainCriterion[] Criteria =
    [
        new("EVERYWHERE", 2, 0, 360, -90, 90),
        new("NEVER", 0, 0, 360, -90, 90),
        new("EAST", 3, 1, 2, -1, 1),
    ];

    [Fact]
    public void ThePickDrawsOneToTheTotalWeightOfTheApplicableCriteria()
    {
        var draws = new List<(int, int)>();
        int Draw(int minimum, int maximum, int value)
        {
            draws.Add((minimum, maximum));
            return value;
        }
        // At longitude 1.5 both weighted criteria apply: 1-2 is EVERYWHERE, 3-5 is EAST; the zero weight never applies.
        Assert.Equal("EVERYWHERE", RuntimeTerrainCriterion.Pick(Criteria, 1.5, 0, (min, max) => Draw(min, max, 2)));
        Assert.Equal("EAST", RuntimeTerrainCriterion.Pick(Criteria, 1.5, 0, (min, max) => Draw(min, max, 3)));
        Assert.Equal("EAST", RuntimeTerrainCriterion.Pick(Criteria, 1.5, 0, (min, max) => Draw(min, max, 5)));
        // Outside EAST's area (its maximum is exclusive) only EVERYWHERE applies.
        Assert.Equal("EVERYWHERE", RuntimeTerrainCriterion.Pick(Criteria, 2, 0, (min, max) => Draw(min, max, 1)));
        Assert.Equal([(1, 5), (1, 5), (1, 5), (1, 2)], draws);
        // Nothing applicable: "" without a draw.
        Assert.Equal("", RuntimeTerrainCriterion.Pick([Criteria[1]], 0, 0, (_, _) => throw new InvalidOperationException()));
    }
}
