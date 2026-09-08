using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class SoldierPilotingTests
{
    [Fact]
    public void CommendationsAndTransformationCountsApplyEachBonusOnce()
    {
        var rules = CampaignLogisticsTests.LoadFixture().RuntimeRules;
        var soldier = rules.Soldiers[rules.Soldiers.GetRequired("RECRUIT")].Value;
        var craft = rules.Crafts[rules.Crafts.GetRequired("INTERCEPTOR")].Value;
        var personal = SoldierGeneration.Generate(soldier, "ARMOR", 0, new HashSet<string>(StringComparer.Ordinal), new SplitMix64RandomSource(1));
        Assert.False(SoldierPiloting.MeetsRequirements(personal, soldier, craft, rules));
        personal = personal with { Commendations = [new("STR_MEDAL_ORIGINAL8_NAME", "NoNoun", 20)] };
        Assert.True(SoldierPiloting.MeetsRequirements(personal, soldier, craft, rules));
        personal = personal with { TransformationBonuses = new Dictionary<string, int> { ["PILOT_BONUS"] = 20 } };
        Assert.False(SoldierPiloting.MeetsRequirements(personal, soldier, craft with { PilotMinimumStats = new Dictionary<string, short> { ["tu"] = 61 } }, rules));
        Assert.False(SoldierPiloting.MeetsRequirements(personal, soldier with { AllowPiloting = false }, craft, rules));
    }

    [Fact]
    public void StatAdditionWrapsShortBeforeApplyingMinimum()
    {
        var rules = CampaignLogisticsTests.LoadFixture().RuntimeRules;
        var soldier = rules.Soldiers[rules.Soldiers.GetRequired("RECRUIT")].Value;
        var craft = rules.Crafts[rules.Crafts.GetRequired("INTERCEPTOR")].Value;
        var personal = SoldierGeneration.Generate(soldier, "ARMOR", 0, new HashSet<string>(StringComparer.Ordinal), new SplitMix64RandomSource(1)) with
        {
            CurrentStats = new Dictionary<string, short> { ["tu"] = short.MaxValue },
            TransformationBonuses = new Dictionary<string, int> { ["PILOT_BONUS"] = 0 },
        };
        Assert.False(SoldierPiloting.MeetsRequirements(personal, soldier, craft, rules));
        Assert.True(SoldierPiloting.MeetsRequirements(personal, soldier,
            craft with { PilotMinimumStats = new Dictionary<string, short> { ["tu"] = 0, ["health"] = 1 } }, rules));
    }
}
