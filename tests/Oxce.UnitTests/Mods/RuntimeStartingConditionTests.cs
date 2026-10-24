using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.UnitTests.Mods;

/// <summary>RuleStartingCondition::isVehiclePermitted and isItemPermitted.</summary>
public sealed class RuntimeStartingConditionTests
{
    private static RuntimeStartingCondition Condition() => new([], [], [], [],
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>()), false, false);

    [Fact]
    public void TheForbiddenVehicleListWinsOverTheAllowedOne()
    {
        var condition = Condition() with { AllowedVehicles = ["TANK"], ForbiddenVehicles = ["HOVER"] };
        Assert.True(condition.IsVehiclePermitted("TANK"));
        Assert.True(condition.IsVehiclePermitted("OTHER"));
        Assert.False(condition.IsVehiclePermitted("HOVER"));
        Assert.False((Condition() with { AllowedVehicles = ["TANK"] }).IsVehiclePermitted("OTHER"));
    }

    [Fact]
    public void AnExplicitlyAllowedItemSkipsTheCategoriesAndAnUnlistedOneNeedsAnAllowedCategory()
    {
        var condition = Condition() with { AllowedItems = ["RIFLE"], AllowedItemCategories = ["LASER"] };
        Assert.True(condition.IsItemPermitted("RIFLE", ["PLASMA"]));
        Assert.True(condition.IsItemPermitted("PISTOL", ["PLASMA", "LASER"]));
        Assert.False(condition.IsItemPermitted("PISTOL", ["PLASMA"]));
        // An unknown item has no categories to check.
        Assert.False(condition.IsItemPermitted("PISTOL", null));
    }

    [Fact]
    public void AForbiddenItemOrCategoryForbidsAndForbiddenCategoriesHideTheAllowedOnes()
    {
        var condition = Condition() with { ForbiddenItems = ["MINE"], ForbiddenItemCategories = ["HEAVY"], AllowedItemCategories = ["LIGHT"] };
        Assert.False(condition.IsItemPermitted("MINE", ["LIGHT"]));
        Assert.False(condition.IsItemPermitted("CANNON", ["LIGHT", "HEAVY"]));
        Assert.True(condition.IsItemPermitted("KNIFE", ["MEDIUM"]));
        Assert.True(Condition().IsItemPermitted("ANYTHING", null));
    }
}
