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
        Assert.Equal(19, work);
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
    public void ProtectedDeliveryOrderCannotBeInferredFromEqualQuantities()
    {
        long work = 0;
        var first = Choice(1, ("A", 0), ("B", 2));
        Assert.False(ProductionOutputEnvelope.TryCreate([first, Choice(7, ("B", 2), ("A", 0))], _ => false, ref work, out _));
        work = 0;
        Assert.True(ProductionOutputEnvelope.TryCreate([first, Choice(7, ("B", 2), ("A", 0))], _ => true, ref work, out var items));
        Assert.Same(first.Items, items);
    }

    [Fact]
    public void SharedWorkExhaustionFailsIntentionally()
    {
        long work = 1_000_000;
        Assert.False(ProductionOutputEnvelope.TryCreate([Choice(1)], _ => true, ref work, out _));
    }

    [Fact]
    public void QuantityRangesKeepPresentEffectsAndIgnoreZeroWeightExtremes()
    {
        RandomProducedItems[] choices = [Choice(1, ("FUEL", 0), ("A", 1)), Choice(3, ("FUEL", 2), ("B", 2)), Choice(0, ("FUEL", int.MaxValue))];
        long work = 0;
        Assert.True(ProductionOutputEnvelope.TryCreateQuantityRanges(choices, id => id != "FUEL", _ => true, ref work, out var upper, out var minimum));
        Assert.Equal(2, upper!["FUEL"]);
        Assert.Equal(1, upper["A"]);
        Assert.Equal(2, upper["B"]);
        Assert.Equal(3, minimum!.Count);
        Assert.Equal(0, minimum["FUEL"]);
        Assert.Equal(0, minimum["A"]);
        Assert.Equal(0, minimum["B"]);
        Assert.Equal(0, choices[0].Items["FUEL"]);
        Assert.Equal(int.MaxValue, choices[2].Items["FUEL"]);
    }

    [Fact]
    public void QuantityPermissionCannotWaivePresenceOrDeliveryOrder()
    {
        foreach (var quantity in new[] { 0, 1 })
        {
            long work = 0;
            Assert.False(ProductionOutputEnvelope.TryCreateQuantityRanges([Choice(1, ("A", quantity)), Choice(1)], _ => false, _ => true, ref work, out _, out _));
        }
        long orderWork = 0;
        Assert.False(ProductionOutputEnvelope.TryCreateQuantityRanges([Choice(1, ("A", 0), ("B", 2)), Choice(1, ("B", 1), ("A", 2))],
            _ => false, _ => true, ref orderWork, out _, out _));
    }

    [Fact]
    public void QuantityRangeAllocationAndOrderChecksShareTheWorkBound()
    {
        RandomProducedItems[] choices = [Choice(1, ("A", 1)), Choice(1, ("A", 2))];
        long work = 999_994;
        Assert.False(ProductionOutputEnvelope.TryCreateQuantityRanges(choices, _ => false, _ => true, ref work, out _, out _));
        Assert.True(work > 1_000_000);
        Assert.Equal(1, choices[0].Items["A"]);
        Assert.Equal(2, choices[1].Items["A"]);
    }

    private static RandomProducedItems Choice(int weight, params (string Id, int Quantity)[] items) =>
        new(weight, items.ToDictionary(item => item.Id, item => item.Quantity, StringComparer.Ordinal));
}
