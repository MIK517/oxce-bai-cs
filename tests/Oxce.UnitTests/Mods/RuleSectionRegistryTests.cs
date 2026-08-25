using Oxce.Mods.Rulesets;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class RuleSectionRegistryTests
{
    [Fact]
    public void NamedRegistryMatchesReferenceLoadRuleDispatch()
    {
        Assert.Equal(38, RuleSectionRegistry.NamedRuleSections.Count);
        Assert.Equal(
        [
            "countries", "extraGlobeLabels", "regions", "facilities", "crafts", "craftWeapons",
            "itemCategories", "items", "weaponSets", "ufos", "invs", "terrains", "armors",
            "skills", "soldiers", "units", "alienRaces", "enviroEffects", "startingConditions",
            "alienDeployments", "research", "manufacture", "manufactureShortcut", "soldierBonuses",
            "soldierTransformation", "commendations", "ufoTrajectories", "alienMissions", "arcScripts",
            "eventScripts", "events", "missionScripts", "adhocScripts", "soundDefs", "customPalettes",
            "interfaces", "cutscenes", "musics",
        ],
            RuleSectionRegistry.NamedRuleSections.Select(section => section.Name));
        Assert.Equal("id", RuleSectionRegistry.NamedRuleSections.Single(section => section.Name == "invs").IdentityKey);
        Assert.Equal("name", RuleSectionRegistry.NamedRuleSections.Single(section => section.Name == "research").IdentityKey);
    }

    [Fact]
    public void SpecialRegistryMakesNonGenericSemanticsExplicit()
    {
        Assert.Equal(8, RuleSectionRegistry.SpecialRuleSections.Count);
        Assert.True(RuleSectionRegistry.TryGetSpecial("mapScripts", out var mapScripts));
        Assert.Equal(SpecialRuleSectionBehavior.ReplaceByIdentity, mapScripts!.Behavior);
        Assert.Equal(["type", "delete"], mapScripts.IdentityKeys);
        Assert.True(RuleSectionRegistry.TryGetSpecial("statStrings", out var statStrings));
        Assert.Empty(statStrings!.IdentityKeys);
        Assert.True(RuleSectionRegistry.TryGetSpecial("extraSprites", out var extraSprites));
        Assert.Equal(SpecialRuleSectionBehavior.AppendOrDeleteIdentity, extraSprites!.Behavior);
        Assert.False(RuleSectionRegistry.TryGetNamed("mapScripts", out _));
    }
}
