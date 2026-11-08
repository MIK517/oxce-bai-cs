using System.Collections.ObjectModel;

namespace Oxce.Mods.Rulesets.CampaignStart;

/// <summary>
/// Global rules the battle's combat reads (<c>Mod::loadFile</c>), with the <c>Mod::Mod</c> defaults: the accuracy globals, close quarters
/// combat, the hit chance tables, the reaction fire thresholds, the AI use delays, and the extended options. Scalars layer per mod; a
/// <c>hitChancesTable</c> replaces the whole table. The damage ranges are in <see cref="CampaignInterceptionSettings"/>.
/// </summary>
public sealed record CampaignCombatSettings
{
    public static CampaignCombatSettings Default { get; } = new();

    /// <summary>Mod::getKneelBonusGlobal and getOneHandedPenaltyGlobal (<c>kneelBonusGlobal</c> 115, <c>oneHandedPenaltyGlobal</c> 80).</summary>
    public int KneelBonusGlobal { get; init; } = 115;
    public int OneHandedPenaltyGlobal { get; init; } = 80;

    /// <summary>Mod::getNoLOSAccuracyPenaltyGlobal (<c>noLOSAccuracyPenaltyGlobal</c>, -1 for none).</summary>
    public int NoLosAccuracyPenaltyGlobal { get; init; } = -1;

    /// <summary>Mod::getExplodeInventoryGlobal (<c>explodeInventoryGlobal</c>): the setting of primed grenades that has no value of its own.</summary>
    public int ExplodeInventoryGlobal { get; init; }

    /// <summary>
    /// Mod::getEnableCloseQuartersCombat (0 off, 1 on, 2 also for aliens), getCloseQuartersAccuracyGlobal, getCloseQuartersTuCostGlobal,
    /// getCloseQuartersEnergyCostGlobal and getCloseQuartersSneakUpGlobal.
    /// </summary>
    public int EnableCloseQuartersCombat { get; init; }
    public int CloseQuartersAccuracyGlobal { get; init; } = 100;
    public int CloseQuartersTuCostGlobal { get; init; } = 12;
    public int CloseQuartersEnergyCostGlobal { get; init; } = 8;
    public int CloseQuartersSneakUpGlobal { get; init; }

    /// <summary>
    /// Mod::getHitChancesTable by unit size: 40 distances of 61 accuracies (0 to 120 percent, step 2) each. A table entry without a
    /// <c>unitSize</c> or with another number of values is skipped (the reference logs it).
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> HitChancesTable { get; init; } = new ReadOnlyDictionary<int, IReadOnlyList<int>>(
        new Dictionary<int, IReadOnlyList<int>>());

    /// <summary>Mod::getAIReactionFireThreshold and getAIReactionFireThresholdCiv (<c>ai.reactionFireThreshold</c>, <c>reactionFireThresholdCiv</c>).</summary>
    public int ReactionFireThreshold { get; init; }
    public int ReactionFireThresholdCivilians { get; init; }

    /// <summary>
    /// Mod::getAIUseDelayBlaster, Firearm, Grenade, Proxy, Melee, Psionic and Medikit (<c>ai.useDelay*</c>, the legacy <c>turnAIUseBlaster</c> and
    /// <c>turnAIUseGrenade</c>, and <c>ai.aiUseDelayProxy</c> as the reference spells it).
    /// </summary>
    public int AiUseDelayBlaster { get; init; } = 3;
    public int AiUseDelayFirearm { get; init; }
    public int AiUseDelayGrenade { get; init; } = 3;
    public int AiUseDelayProxy { get; init; } = 999;
    public int AiUseDelayMelee { get; init; }
    public int AiUseDelayPsionic { get; init; }
    public int AiUseDelayMedikit { get; init; } = 999;

    /// <summary>Mod::getTuRecoveryWakeUpNewTurn (<c>tuRecoveryWakeUpNewTurn</c>): the percentage of time units a unit that wakes up at the new turn recovers.</summary>
    public int TuRecoveryWakeUpNewTurn { get; init; } = 100;

    /// <summary>Mod::getStunningImprovesMorale and getStatisticalBulletConservation.</summary>
    public bool StunningImprovesMorale { get; init; }
    public bool StatisticalBulletConservation { get; init; }

    /// <summary>
    /// Mod::EXTENDED_EXPERIENCE_AWARD_SYSTEM, EXTENDED_SPOT_ON_HIT_FOR_SNIPING (0 off, 1 spot on a hit, 2 also a unit that is out), EXTENDED_TERRAIN_MELEE
    /// (the first of 12 wall and object kinds a melee attack can hit, 0 off) and EXTENDED_UNDERWATER_THROW_FACTOR (percent, 0 off).
    /// </summary>
    public bool ExtendedExperienceAwardSystem { get; init; }
    public int ExtendedSpotOnHitForSniping { get; init; }
    public int ExtendedTerrainMelee { get; init; }
    public int ExtendedUnderwaterThrowFactor { get; init; }

    /// <summary>The hit chance table of a unit size, or null.</summary>
    public IReadOnlyList<int>? HitChances(int unitSize) => HitChancesTable.GetValueOrDefault(unitSize);
}
