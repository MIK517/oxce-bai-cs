using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns.World;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class WorldUfoShieldTests
{
    [Theory]
    [InlineData(-2, 10, 100)]
    [InlineData(0, -1, 100)]
    [InlineData(int.MaxValue - 1, int.MaxValue, 200)]
    public void InvalidShieldArithmeticDoesNotConsumeRandomness(int shield, int capacity, int recharge)
    {
        var random = new SplitMix64RandomSource(17);
        Assert.Throws<InvalidDataException>(() => WorldUfoShields.Recharge(shield, capacity, recharge, random));
        Assert.Equal(17UL, random.State);
    }
}
