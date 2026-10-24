using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles.Generation;

/// <summary>BattlescapeGenerator::deployAliens: how many aliens of one deployment group the battle gets.</summary>
internal static class AlienQuantities
{
    /// <summary>
    /// The difficulty's base (low; halfway to medium; medium, else halfway to high; halfway from medium to high, else
    /// halfway to high; high), then <c>dQty</c> and <c>extraQty</c> extras: their maximum with <c>difficultyDemigod</c>, else a
    /// random 0..n each, drawn in that order.
    /// </summary>
    public static int Quantity(RuntimeDeploymentData data, CampaignDifficulty difficulty, bool demigod, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(random);
        var quantity = difficulty switch
        {
            CampaignDifficulty.Beginner => data.LowQuantity,
            CampaignDifficulty.Experienced => data.MediumQuantity > 0
                ? data.LowQuantity + (data.MediumQuantity - data.LowQuantity) / 2 : data.LowQuantity,
            CampaignDifficulty.Veteran => data.MediumQuantity > 0
                ? data.MediumQuantity : data.LowQuantity + (data.HighQuantity - data.LowQuantity) / 2,
            CampaignDifficulty.Genius => data.MediumQuantity > 0
                ? data.MediumQuantity + (data.HighQuantity - data.MediumQuantity) / 2 : data.LowQuantity + (data.HighQuantity - data.LowQuantity) / 2,
            _ => data.HighQuantity,
        };
        quantity = checked(quantity + (demigod ? data.DifficultyQuantity : MonthlyCycle.Generate(random, 0, data.DifficultyQuantity)));
        return checked(quantity + (demigod ? data.ExtraQuantity : MonthlyCycle.Generate(random, 0, data.ExtraQuantity)));
    }
}
