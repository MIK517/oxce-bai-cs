using Oxce.Core.Random;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>GeoscapeState::time5Seconds flying-UFO block and Ufo::setShield at 4df3a5e.</summary>
public static class WorldUfoShields
{
    public static bool IsValid(int shield, int capacity, int recharge) => shield >= -1 && capacity >= 0 &&
        (shield == -1 || shield >= capacity || recharge <= 0 ||
            (long)shield + recharge / 100 + (recharge % 100 != 0 ? 1 : 0) <= int.MaxValue);

    public static int Recharge(int shield, int capacity, int recharge, IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!IsValid(shield, capacity, recharge)) throw new InvalidDataException("UFO shield arithmetic exceeds the supported range.");
        if (shield == -1) return capacity;
        if (shield >= capacity) return shield;
        if (recharge == -1) return capacity;
        if (recharge <= 0) return shield;
        var total = recharge / 100;
        if (RandomChance.Percent(random, recharge % 100)) total++;
        return Math.Clamp(checked(shield + total), 0, capacity);
    }
}
