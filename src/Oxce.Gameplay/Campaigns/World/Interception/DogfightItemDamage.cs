using Oxce.Core.Random;
using Oxce.Mods.Rulesets.CampaignStart;

namespace Oxce.Gameplay.Campaigns.World.Interception;

/// <summary>
/// The damage type of a unified-formula craft weapon's clip or launcher. Reference:
/// <c>RuleDamageType::getRandomDamage(int power, FuncRef randFunc)</c> at 4df3a5e, with
/// <c>Mod::DAMAGE_RANGE</c>, <c>EXPLOSIVE_DAMAGE_RANGE</c> and <c>FIRE_DAMAGE_RANGE</c>.
/// </summary>
internal sealed class DogfightItemDamage(int randomType, int resistType, CampaignInterceptionSettings settings) : IDogfightDamageType
{
    // ItemDamageRandomType and the ItemDamageType values the default random type depends on.
    private const int Default = 0, Ufo = 1, Tftd = 2, Flat = 3, Fire = 4, None = 5, UfoTwoDice = 6, Easy = 7,
        Standard = 8, Explosion = 9, UfoFourDice = 10;
    private const int ResistNone = 0, ResistIncendiary = 2, ResistHighExplosive = 3, ResistSmoke = 9;

    public int RandomDamage(int power, IRandomSource random)
    {
        var type = randomType;
        if (type == UfoFourDice)
        {
            var first = random.NextInclusive(0, power);
            var second = random.NextInclusive(0, power);
            var third = random.NextInclusive(0, power);
            var fourth = random.NextInclusive(0, power);
            return (first + second + third + fourth) / 2;
        }
        if (type == UfoTwoDice)
        {
            var first = random.NextInclusive(0, power);
            var second = random.NextInclusive(0, power);
            return first + second;
        }
        if (type == Easy) return random.NextInclusive(power / 2, power * 2);

        if (type == Default)
        {
            type = resistType switch
            {
                ResistNone or ResistSmoke => None,
                ResistIncendiary => Fire,
                ResistHighExplosive => Explosion,
                _ => Standard,
            };
        }

        int range;
        switch (type)
        {
            case Ufo: range = 100; break;
            case Tftd: range = 50; break;
            case Standard: range = settings.DamageRange; break;
            case Explosion: range = settings.ExplosiveDamageRange; break;
            case Flat: range = 0; break;
            case Fire: return random.NextInclusive(settings.FireDamageRange[0], settings.FireDamageRange[1]);
            default: return 0;
        }
        return random.NextInclusive(power * (100 - range) / 100, power * (100 + range) / 100);
    }
}
