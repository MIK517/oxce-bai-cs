using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class ProductionOutputEnvelopeTests
{
    [Fact]
    public void VariantsRetainCommonQuantitiesAndReserveOnlyUpperBounds()
    {
        RandomProducedItems[] choices = [Choice(1, ("FUEL", 2), ("A", 1)), Choice(3, ("FUEL", 2), ("B", 2)), Choice(0, ("FUEL", int.MaxValue))];
        long work = 0;
        Assert.True(ProductionOutputEnvelope.TryCreate(choices, id => id != "FUEL", ref work, out var items));
        Assert.Equal(2, items!["FUEL"]);
        Assert.Equal(1, items["A"]);
        Assert.Equal(2, items["B"]);
        Assert.Equal(11, work);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ChangedPresenceIncludesZeroQuantityEffects(int quantity)
    {
        long work = 0;
        Assert.False(ProductionOutputEnvelope.TryCreate([Choice(1, ("A", quantity)), Choice(1)], _ => false, ref work, out _));
    }

    [Fact]
    public void EquivalentMapsDoNotRequireVariationPermission()
    {
        long work = 0;
        var first = Choice(1, ("A", 0), ("B", 2));
        Assert.True(ProductionOutputEnvelope.TryCreate([first, Choice(7, ("B", 2), ("A", 0))], _ => false, ref work, out var items));
        Assert.Same(first.Items, items);
    }

    [Fact]
    public void SharedWorkExhaustionFailsIntentionally()
    {
        long work = 1_000_000;
        Assert.False(ProductionOutputEnvelope.TryCreate([Choice(1)], _ => true, ref work, out _));
    }

    private static RandomProducedItems Choice(int weight, params (string Id, int Quantity)[] items) =>
        new(weight, items.ToDictionary(item => item.Id, item => item.Quantity, StringComparer.Ordinal));
}
