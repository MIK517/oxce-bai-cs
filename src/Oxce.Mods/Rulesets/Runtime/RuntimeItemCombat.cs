using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Items;

namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>
/// RuleItemAction as the battle reads it (<c>getConfigAimed</c> and the others): the action's range, the shots it fires and the ammunition each
/// spends, whether the camera follows the projectiles, the ammunition slot (-1 for none, 0 to 3), the ammunition's chance overrides (-1 for
/// the ammunition's own), whether the shots arc, and its names. Costs and accuracy are <see cref="RuntimeItemActionCosts"/> and
/// <see cref="RuntimeItemAccuracy"/>.
/// </summary>
public sealed record RuntimeItemAction(int Range, int Shots, int SpendPerShot, bool FollowProjectiles, int AmmoSlot, int AmmoZombieUnitChanceOverride,
    int AmmoSpawnUnitChanceOverride, int AmmoSpawnItemChanceOverride, bool Arcing, string Name, string ShortName)
{
    public static RuntimeItemAction Default { get; } = new(0, 1, 1, true, 0, -1, -1, -1, false, "", "");
}

/// <summary>
/// are those of an item under the default mod, apart from the melee action's ammo slot and the action names, which the reference sets when it reads the item.
/// are those of a freshly created RuleItem under the default mod.
/// </summary>
public sealed record RuntimeItemCombat
{
    public static RuntimeItemCombat Default { get; } = new();

    /// <summary>RuleItem::getConfigAimed, getConfigAuto, getConfigSnap and getConfigMelee.</summary>
    public RuntimeItemAction Aimed { get; init; } = RuntimeItemAction.Default with { Range = 200 };
    public RuntimeItemAction Auto { get; init; } = RuntimeItemAction.Default with { Range = 7, Shots = 3 };
    public RuntimeItemAction Snap { get; init; } = RuntimeItemAction.Default with { Range = 15 };
    public RuntimeItemAction Melee { get; init; } = RuntimeItemAction.Default with { AmmoSlot = -1 };

    /// <summary>RuleItem::getConfigAimed, getConfigAuto, getConfigSnap or getConfigMelee by the action's key (<c>Aimed</c>, <c>Auto</c>, <c>Snap</c>, <c>Melee</c>).</summary>
    public RuntimeItemAction Action(string key) => key switch
    {
        RuntimeItemActionCosts.Aimed => Aimed,
        RuntimeItemActionCosts.Auto => Auto,
        RuntimeItemActionCosts.Snap => Snap,
        RuntimeItemActionCosts.Melee => Melee,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// <summary>RuleItem::getIgnoreAmmoPower: the ammunition's power is not added to the weapon's.</summary>
    public bool IgnoreAmmoPower { get; init; }

    /// <summary>RuleItem's <c>powerRangeReduction</c> and <c>powerRangeThreshold</c> (tiles): <see cref="PowerRangeReductionAt"/> reads them.</summary>
    public float PowerRangeReduction { get; init; }
    public float PowerRangeThreshold { get; init; }

    /// <summary>RuleItem::getMaxRange, getMinRange and getDropoff.</summary>
    public int MaxRange { get; init; } = 200;
    public int MinRange { get; init; }
    public int Dropoff { get; init; } = 2;

    /// <summary>RuleItem's throw ranges (<c>throwRange</c> and the underwater one), the ranges the accuracy starts to drop at, and <c>throwDropoff</c>.</summary>
    public int ThrowRange { get; init; } = 200;
    public int UnderwaterThrowRange { get; init; } = 200;
    public int ThrowDropoffRange { get; init; } = 99;
    public int UnderwaterThrowDropoffRange { get; init; } = 99;
    public int ThrowDropoff { get; init; } = 5;

    /// <summary>
    /// RuleItem::getAccuracyCloseQuarters, getNoLOSAccuracyPenalty, getKneelBonus, getOneHandedPenalty and getExplodeInventory: the item's value, or
    /// the mod's global where the item leaves it at -1 (a non-grenade explodes nothing in the inventory unless it says so).
    /// </summary>
    public int AccuracyCloseQuarters { get; init; } = 100;
    public int NoLosAccuracyPenalty { get; init; } = -1;
    public int KneelBonus { get; init; } = 115;
    public int OneHandedPenalty { get; init; } = 80;
    public int ExplodeInventory { get; init; }

    /// <summary>RuleItem::getArcingShot, getSprayWaypoints and the shotgun fields (pellets, behavior type, spread, choke).</summary>
    public bool ArcingShot { get; init; }
    public int SprayWaypoints { get; init; }
    public int ShotgunPellets { get; init; }
    public int ShotgunBehavior { get; init; }
    public int ShotgunSpread { get; init; } = 100;
    public int ShotgunChoke { get; init; } = 100;

    /// <summary>RuleItem::getCompatibleAmmoForSlot (names) and getTUReload and getTUUnload, by ammunition slot.</summary>
    public IReadOnlyList<IReadOnlyList<string>> CompatibleAmmo { get; init; } = Lists(4, static () => (IReadOnlyList<string>)[]);
    public IReadOnlyList<int> TimeUnitsToLoad { get; init; } = Array.AsReadOnly([15, 15, 15, 15]);
    public IReadOnlyList<int> TimeUnitsToUnload { get; init; } = Array.AsReadOnly([8, 8, 8, 8]);

    /// <summary>RuleItem::isLOSRequired, isLandOnly (isWaterOnly is the strategic projection's <c>WaterOnly</c>), getSpecialUseEmptyHand and isFireExtinguisher.</summary>
    public bool LosRequired { get; init; }
    public bool LandOnly { get; init; }
    public bool SpecialUseEmptyHand { get; init; }
    public bool SpecialUseEmptyHandShow { get; init; }

    /// <summary>RuleItem::getSpecialIconSprite (<c>specialIconSprite</c>, -1 for none): a special weapon with one has the battlescape's special-weapon button.</summary>
    public int SpecialIconSprite { get; init; } = -1;
    public bool IsFireExtinguisher { get; init; }

    /// <summary>RuleItem::isConvertedToCivilian (<c>convertToCivilian</c>) and the target matrix (<c>isTargetAllowed</c>: bit 1 own side, 2 hostile, 4 neutral).</summary>
    public bool ConvertToCivilian { get; init; }
    public int TargetMatrix { get; init; } = 7;

    /// <summary>RuleItem::getSpecialType (-1 for none).</summary>
    public int SpecialType { get; init; } = -1;

    /// <summary>RuleItem::getAIUseDelay (the item's, or the mod's by battle type where the item has none) and getAIMeleeHitCount.</summary>
    public int AiUseDelay { get; init; } = -1;
    public int AiMeleeHitCount { get; init; } = 25;

    /// <summary>RuleItem::getSpecialChance: the chance a self-destruct explosion goes off (<c>specialChance</c>, 100 by default).</summary>
    public int SpecialChance { get; init; } = 100;

    /// <summary>RuleItem::getExperienceTrainingMode and getManaExperience.</summary>
    public int ExperienceTrainingMode { get; init; }
    public int ManaExperience { get; init; }

    /// <summary>
    /// RuleItem::getZombieUnit ("" for none) with the units by the victim's male or female armor and by its type, the faction of the unit
    /// (<c>zombieUnitFaction</c>, 1 hostile) and the chance (<c>zombieUnitChance</c>, or <c>specialChance</c> where unset).
    /// </summary>
    public string ZombieUnit { get; init; } = "";
    public int ZombieUnitFaction { get; init; } = 1;
    public int ZombieUnitChance { get; init; } = 100;
    public IReadOnlyDictionary<string, string> ZombieUnitsByMaleArmor { get; init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    public IReadOnlyDictionary<string, string> ZombieUnitsByFemaleArmor { get; init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    public IReadOnlyDictionary<string, string> ZombieUnitsByType { get; init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    /// <summary>The item's six stat bonuses (RuleItem::getPowerBonus, getMeleeBonus and the multipliers).</summary>
    public ItemStatBonusRules StatBonuses { get; init; } = ItemStatBonusRules.Default;

    /// <summary>
    /// RuleItem::getPowerRangeReduction: the power the shot loses at a range in voxels, <c>powerRangeReduction</c> per tile past the threshold
    /// (single precision, as the reference's).
    /// </summary>
    public float PowerRangeReductionAt(float rangeInVoxels)
    {
        rangeInVoxels -= PowerRangeThreshold * 16.0f;
        return PowerRangeReduction * 0.0625f * (rangeInVoxels > 0 ? rangeInVoxels : 0);
    }

    private static ReadOnlyCollection<T> Lists<T>(int count, Func<T> create) => Array.AsReadOnly(Enumerable.Range(0, count).Select(_ => create()).ToArray());
}
