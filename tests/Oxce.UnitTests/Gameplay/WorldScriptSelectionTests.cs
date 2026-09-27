using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class WorldScriptSelectionTests
{
    [Fact]
    public void AppendedDuplicateMonthOverridesEarlierEntriesWithoutSorting()
    {
        RuntimeScriptTimeline<EventRuleFamily>[] timeline =
        [new(0, [new(new("A", null), 1)]), new(6, [new(new("B", null), 1)]), new(0, [new(new("C", null), 1)])];
        var random = new SplitMix64RandomSource(17);
        Assert.Equal("C", WorldScriptSelection.Generate(timeline, 12, random)!.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void GenerationRejectsMissingOrFutureOnlyTimelinesBeforeRandomDraw(int count)
    {
        RuntimeScriptTimeline<EventRuleFamily>[] timeline = count == 0 ? [] : [new(2, [new(new("A", null), 1)])];
        var random = new SplitMix64RandomSource(17);
        Assert.Throws<InvalidDataException>(() => WorldScriptSelection.Generate(timeline, 0, random));
        Assert.Equal(17UL, random.State);
        if (count == 0) Assert.Throws<InvalidDataException>(() => WorldScriptSelection.Available(timeline, 0));
        else Assert.Equal("A", Assert.Single(WorldScriptSelection.Available(timeline, -1)).Rule.Id);
    }

    [Theory]
    [InlineData(2147483648UL)]
    [InlineData(18446744073709551615UL)]
    public void UnsafeWeightsFailBeforeRandomDraw(ulong weight)
    {
        RuntimeScriptWeight<EventRuleFamily>[] choices = [new(new("A", null), weight)];
        var random = new SplitMix64RandomSource(17);
        Assert.Throws<InvalidDataException>(() => WorldScriptSelection.Choose(choices, random));
        Assert.Equal(17UL, random.State);
    }

    [Fact]
    public void EmptyWeightedTableDrawsNothing()
    {
        var random = new SplitMix64RandomSource(17);
        Assert.Null(WorldScriptSelection.Choose<EventRuleFamily>([], random));
        Assert.Null(WorldScriptSelection.Generate<EventRuleFamily>([new(0, [])], 0, random));
        Assert.Equal(17UL, random.State);
    }
}
