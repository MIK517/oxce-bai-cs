using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// <c>BattleActionAttack</c>: the kind of action, the unit that attacks, the weapon that causes the damage and the item whose damage it is (the
/// ammunition of a firearm, or the weapon or grenade itself). The skill rule of the reference belongs to the skills, which branch 6 ports.
/// </summary>
internal readonly record struct BattleAttack(BattleActionType Type, BattleUnit? Attacker = null, BattleItem? Weapon = null, BattleItem? DamageItem = null);

/// <summary>
/// What <see cref="BattleState.DamageUnit"/> did: the damage to the unit's health, and the refusal when the hit needs work of a later slice (a
/// self-destruct explosion, S5; a unit taken out or set to respawn, S4). The unit has taken the hit either way: a command runs on a trial copy first.
/// </summary>
internal readonly record struct BattleDamageResult(int Damage, CampaignReason? Refusal = null);

/// <summary>
/// Damage to units (Phase 7 branch 5 S3): <c>BattleUnit::damage</c> (the side and body part of the hit, the armor, every conversion to health, stun,
/// morale, time, energy, mana, wounds and armor, pain immunity, overkill, fire and the extinguisher, infection, the attacker's spotting and the AI's
/// <c>wasHitBy</c>, the self-destruct), <c>TileEngine::hitUnit</c> and <c>awardExperience</c>. The combat script hooks (<c>hitUnit</c>,
/// <c>damageUnit</c>, <c>damageSpecialUnit</c> and the experience hook) are refused at the briefing (branch 8), so none runs. The hit log
/// (<c>appendToHitLog</c>) is runtime text for the next turn's screen and the statistics counters are <c>ProjectileFlyBState</c>'s (S7).
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>Unit.h's UnitStats limits: <c>BaseStatLimit</c>, <c>StunMultipler</c> and <c>OverkillMultipler</c>.</summary>
    private const int BaseStatLimit = 8000;

    private const int StunMultiplier = 4;
    private const int OverkillMultiplier = 4;

    /// <summary>The side roll's pick between two sides: <c>RNG::generate(0, 2) &lt; 2</c> takes the first.</summary>
    private static int RollSide(int ifRoll, int otherwise, int roll) => roll < 2 ? ifRoll : otherwise;

    /// <summary>fmath.h's Clamp: <c>min(max(x, lo), hi)</c>, which does not throw when the bounds cross.</summary>
    private static int Bound(int value, int low, int high) => Math.Min(Math.Max(value, low), high);

    /// <summary>BattleUnit::reduceByResistance: the power times the armor's modifier for the damage type, rounded down.</summary>
    private static int ReduceByResistance(RuntimeBattleArmor armor, int power, int resistType) =>
        (int)MathF.Floor(power * ((uint)resistType < (uint)armor.DamageModifiers.Count ? armor.DamageModifiers[resistType] : 1f));

    /// <summary>BattleUnit::reduceByBravery.</summary>
    private static int ReduceByBravery(BattleUnit unit, int change) => (110 - unit.Stats.Bravery) * change / 100;

    /// <summary>BattleUnit::isFearable: the armor is not immune to fear.</summary>
    private static bool IsFearable(RuntimeBattleArmor armor) => !(armor.FearImmune ?? false);

    /// <summary>BattleUnit::moraleChange.</summary>
    private static void MoraleChange(RuntimeBattleArmor armor, BattleUnit unit, int change)
    {
        if (!IsFearable(armor)) return;
        unit.Morale = Bound(unit.Morale + change, 0, 100);
    }

    /// <summary>BattleUnit::isWoundable: a bleed immune armor does not wound; the default is immune unless the unit is a soldier (aliens bleed only under <c>alienBleeding</c>, off).</summary>
    private static bool IsWoundable(RuntimeBattleArmor armor, BattleUnit unit) => !(armor.BleedImmune ?? unit.SoldierId is null);

    /// <summary>BattleUnit::isOutThresholdExceed: no health, or the stun reaches the health.</summary>
    internal static bool IsOutThresholdExceeded(BattleUnit unit) => unit.Health <= 0 || unit.Health <= unit.StunLevel;

    /// <summary>
    /// What <c>checkForCasualties</c> would act on: a unit that is not dead and has no health, or that is not unconscious and whose stun reaches its
    /// health. An unconscious body that keeps its health stays as it is.
    /// </summary>
    internal static bool NeedsCasualtyCheck(BattleUnit unit) =>
        unit.Status != BattleUnitStatus.Dead && unit.Status != BattleUnitStatus.IgnoreMe &&
        (unit.Health <= 0 || unit.StunLevel >= unit.Health && unit.Status != BattleUnitStatus.Unconscious);

    /// <summary>BattleUnit::getOverKillDamage: the damage past the health and the armor's overkill share of the maximum health.</summary>
    internal static int OverKillDamage(RuntimeRuleCatalog rules, BattleUnit unit) =>
        Math.Max(-unit.Health - (int)(unit.Stats.Health * ArmorOf(rules, unit).OverKill), 0);

    /// <summary>BattleUnit::setFire: a unit that burns floors cannot catch fire.</summary>
    private static void SetUnitFire(RuntimeRuleCatalog rules, BattleUnit unit, int fire)
    {
        if (SpecialAbilityOf(rules, unit) < 2) unit.Fire = fire;
    }

    /// <summary>
    /// SavedBattleGame::getFactionMoraleModifier for the player: 100, plus the highest ranked X-COM unit that is not out counts up from a sergeant
    /// (+10 sergeant, +5 more for a captain, +10 for a colonel, +25 for a commander).
    /// </summary>
    internal int PlayerMoraleModifier()
    {
        BattleUnit? leader = null;
        foreach (var unit in _units)
            if (unit.OriginalFaction == BattleFaction.Player && !unit.IsOut && (leader is null || unit.Rank > leader.Rank)) leader = unit;
        var result = 100;
        switch (leader?.Rank ?? 0)
        {
            case 5: result += 25; goto case 4;
            case 4: result += 10; goto case 3;
            case 3: result += 5; goto case 2;
            case 2: result += 10; break;
        }
        return result;
    }

    /// <summary>
    /// BattleUnit::damage. <paramref name="relative"/> is where the hit landed relative to the unit's bottom centre (a straight hit from below is
    /// <c>(0, 0, z &lt;= 0)</c>, the side under); <paramref name="sideOverride"/> and <paramref name="bodyPartOverride"/> are the environment's. Returns
    /// the damage to the health, with the refusal when the hit would push a self-destruct explosion (S5).
    /// </summary>
    internal BattleDamageResult DamageUnit(RuntimeRuleCatalog rules, BattleUnit unit, BattlePosition relative, int damage, RuntimeItemDamageRule type,
        in BattleAttack attack, int sideOverride = -1, int bodyPartOverride = -1)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(type);
        unit.HitState = true;
        if (unit.Health <= 0) return new(0);
        var armor = ArmorOf(rules, unit);
        var side = BattleUnit.SideFront;
        var bodyPart = BattleUnit.BodyPartTorso;
        damage = ReduceByResistance(armor, damage, type.ResistType);

        if (!type.IgnoreDirection)
        {
            if (relative.X == 0 && relative.Y == 0 && relative.Z <= 0)
            {
                side = BattleUnit.SideUnder;
            }
            else
            {
                int relativeDirection;
                var absX = Math.Abs(relative.X);
                var absY = Math.Abs(relative.Y);
                if (absY > absX * 2) relativeDirection = 8 + 4 * (relative.Y > 0 ? 1 : 0);
                else if (absX > absY * 2) relativeDirection = 10 + 4 * (relative.X < 0 ? 1 : 0);
                else if (relative.X < 0) relativeDirection = relative.Y > 0 ? 13 : 15;
                else relativeDirection = relative.Y > 0 ? 11 : 9;

                side = ((relativeDirection - unit.Direction) % 8) switch
                {
                    0 => BattleUnit.SideFront,
                    1 => RollSide(BattleUnit.SideFront, BattleUnit.SideRight, Random.NextInclusive(0, 2)),
                    2 => BattleUnit.SideRight,
                    3 => RollSide(BattleUnit.SideRear, BattleUnit.SideRight, Random.NextInclusive(0, 2)),
                    4 => BattleUnit.SideRear,
                    5 => RollSide(BattleUnit.SideRear, BattleUnit.SideLeft, Random.NextInclusive(0, 2)),
                    6 => BattleUnit.SideLeft,
                    _ => RollSide(BattleUnit.SideFront, BattleUnit.SideLeft, Random.NextInclusive(0, 2)),
                };
                if (relative.Z >= unit.HeightOf(rules))
                {
                    bodyPart = BattleUnit.BodyPartHead;
                }
                else if (relative.Z > 4)
                {
                    bodyPart = side switch
                    {
                        BattleUnit.SideLeft => BattleUnit.BodyPartLeftArm,
                        BattleUnit.SideRight => BattleUnit.BodyPartRightArm,
                        _ => BattleUnit.BodyPartTorso,
                    };
                }
                else
                {
                    bodyPart = side switch
                    {
                        BattleUnit.SideLeft => BattleUnit.BodyPartLeftLeg,
                        BattleUnit.SideRight => BattleUnit.BodyPartRightLeg,
                        _ => Random.NextInclusive(BattleUnit.BodyPartRightLeg, BattleUnit.BodyPartLeftLeg),
                    };
                }
            }
        }

        // The environment's overrides (the hit scripts, which would run here, are refused at the briefing).
        if (sideOverride != -1) side = sideOverride;
        if (bodyPartOverride != -1) bodyPart = bodyPartOverride;
        var overKillMinimum = type.IgnoreOverKill ? 0 : -OverkillMultiplier * unit.Stats.Health;

        // The ammunition that turns its victims into others, if this victim can be.
        var damageRule = attack.DamageItem is { } damageItem ? rules.Battle.Equipment.Items[damageItem.ItemType].Combat : null;
        var transformUnit = damageRule is null ? "" : ZombieUnitFor(damageRule, unit);
        var transformChance = 0;
        var transforms = damageRule is not null && transformUnit.Length != 0 && !(armor.ZombieImmune ?? false);
        if (transforms)
        {
            transformChance = damageRule!.ZombieUnitChance;
            if (attack.Weapon is { } weapon && ActionIndex(attack.Type) is { } index)
            {
                var conf = rules.Battle.Equipment.Items[weapon.ItemType].Combat;
                var overrideChance = (index switch { 0 => conf.Aimed, 1 => conf.Auto, 2 => conf.Snap, _ => conf.Melee }).AmmoZombieUnitChanceOverride;
                if (overrideChance != -1) transformChance = overrideChance;
            }
            // A hostile victim is converted by an X-COM or neutral attacker only ((mind-controlled) chryssalid on snakeman is not allowed).
            if (unit.OriginalFaction == BattleFaction.Hostile && (attack.Attacker is null || attack.Attacker.OriginalFaction == BattleFaction.Hostile))
                transformChance = 0;
        }

        // update state of unit stats
        int toHealth = 0, toArmor = 0, toStun = 0, toTime = 0, toEnergy = 0, toMorale = 0, toWound = 0, toMana = 0;
        var toTransform = 0;
        if (damage > 0)
        {
            toTransform += transformChance;
            toArmor += type.ArmorPreFinalDamage(damage, Random);
            if (type.ArmorEffectiveness > 0f)
            {
                var armorValue = unit.CurrentArmor[side];
                if (type.ArmorIgnore != 0) armorValue = Bound(armorValue - type.ArmorIgnore, 0, armorValue);
                damage = (int)(damage - armorValue * type.ArmorEffectiveness);
            }
            if (damage > 0)
            {
                toStun += type.StunFinalDamage(damage, Random);
                toMorale += type.MoraleFinalDamage(damage, Random);
                toTime += type.TimeFinalDamage(damage, Random);
                toHealth += type.HealthFinalDamage(damage, Random);
                toMana += type.ManaFinalDamage(damage, Random);
                toEnergy += type.EnergyFinalDamage(damage, Random);
                toWound += type.WoundFinalDamage(damage, Random);
                toArmor += type.ArmorFinalDamage(damage, Random);
            }

            if (!(armor.PainImmune ?? false) || type.IgnorePainImmunity) unit.StunLevel = Bound(unit.StunLevel + toStun, 0, StunMultiplier * unit.Stats.Health);
            MoraleChange(armor, unit, -ReduceByBravery(unit, toMorale));
            unit.TimeUnits = Bound(unit.TimeUnits - toTime, 0, unit.Stats.TimeUnits);
            unit.Health = Bound(unit.Health - toHealth, Math.Min(overKillMinimum, unit.Health), unit.Stats.Health);
            unit.Mana = Bound(unit.Mana - toMana, 0, unit.Stats.Mana);
            unit.Energy = Bound(unit.Energy - toEnergy, 0, unit.Stats.Stamina);
            if (IsWoundable(armor, unit))
            {
                unit.FatalWounds[bodyPart] = Bound(unit.FatalWounds[bodyPart] + toWound, 0, BaseStatLimit);
                MoraleChange(armor, unit, -toWound);
            }
            unit.CurrentArmor[side] = Bound(unit.CurrentArmor[side] - toArmor, 0, unit.MaxArmor[side]);
            unit.FatalShotSide = side;
            unit.FatalShotBodyPart = bodyPart;
            damage = toHealth;
            transformChance = toTransform;
        }

        // special effects
        var moraleLoss = 0;
        if (!type.IgnoreNormalMoraleLose)
        {
            var bravery = ReduceByBravery(unit, 10);
            var modifier = unit.Faction == BattleFaction.Player ? PlayerMoraleModifier() : 100;
            moraleLoss = 100 * (damage * bravery / 10) / modifier;
        }
        var specialAbility = SpecialAbilityOf(rules, unit);
        var selfDestruct = specialAbility is 1 or 3;
        var selfDestructChance = 0;
        if (selfDestruct && !unit.IsOut && IsOutThresholdExceeded(unit) && !type.IgnoreSelfDestruct) selfDestructChance = 100;

        // normal fire
        var fire = unit.Fire;
        if (damage >= type.FireThreshold)
        {
            var resistance = (uint)type.ResistType < (uint)armor.DamageModifiers.Count ? armor.DamageModifiers[type.ResistType] : 1f;
            if (resistance > 0f)
            {
                var burnTime = Random.NextInclusive(0, (int)(5.0f * resistance));
                if (fire < burnTime) fire = burnTime; // catch fire and burn
            }
        }
        // fire extinguisher
        if (fire > 0)
        {
            if (attack.Weapon is { } extinguisher && rules.Battle.Equipment.Items[extinguisher.ItemType].Combat.IsFireExtinguisher) fire = 0;
            else if (attack.DamageItem is { } ammo && rules.Battle.Equipment.Items[ammo.ItemType].Combat.IsFireExtinguisher) fire = 0;
        }

        // AI direct hit tracking
        var attackerTurnsSinceSpotted = 255;
        var attackerTurnsLeftSpottedForSnipers = 0;
        if (attack.Attacker is { } attacker)
        {
            var faction = (int)unit.Faction;
            attackerTurnsSinceSpotted = attacker.TurnsSinceSpotted[faction];
            attackerTurnsLeftSpottedForSnipers = attacker.TurnsLeftSpottedForSnipers[faction];
            if (unit.Faction != attacker.Faction && attack.Type is BattleActionType.AimedShot or BattleActionType.SnapShot or BattleActionType.AutoShot &&
                attack.DamageItem is { } shell && (relative == new BattlePosition(0, 0, 0) || ExplosionRadiusIsZero(rules, shell)))
            {
                if (unit.Ai is { } ai && attacker.Faction != unit.Faction && !ai.WasHitBy.Contains(attacker.Id)) ai.WasHitBy.Add(attacker.Id);
                attackerTurnsSinceSpotted = 0;
                var spotting = rules.Campaign.Combat.ExtendedSpotOnHitForSniping;
                // 0 = don't spot, 1 = spot only if the victim doesn't die or pass out, 2 = always spot
                if (spotting > 0 && (spotting > 1 || !IsOutThresholdExceeded(unit)))
                    attackerTurnsLeftSpottedForSnipers = Math.Max(attackerTurnsLeftSpottedForSnipers, unit.SoldierId is null && rules.Battle.Equipment.Units.TryGetValue(unit.UnitType, out var unitRule) ? unitRule.SpotterDuration : 0);
            }
        }

        // update state
        MoraleChange(armor, unit, -moraleLoss);
        SetUnitFire(rules, unit, fire);

        // check if this unit turns others into zombies
        var respawnSet = false;
        if (RandomPercent(transformChance) && transforms && unit.SpawnUnit.Length == 0)
        {
            if (SpawnArmorSize(rules, transformUnit) is { } spawnSize && spawnSize <= unit.ArmorSize)
            {
                var spawnFaction = damageRule!.ZombieUnitFaction;
                if (spawnFaction == -1) spawnFaction = attack.Attacker is { } maker ? (int)maker.Faction : (int)BattleFaction.Hostile;
                // converts the victim to a zombie on death
                unit.Respawn = true;
                unit.SpawnUnitFaction = spawnFaction;
                unit.SpawnUnit = transformUnit;
                respawnSet = true;
            }
            // else: the reference logs that the bigger armor does not fit; nothing changes.
        }

        var selfDestructItem = armor.SelfDestructItem.Length == 0 ? null : SpecialWeaponNamed(unit, armor.SelfDestructItem);
        var explodes = RandomPercent(selfDestructChance) && !unit.AlreadyExploded && selfDestructItem is not null;
        if (explodes) unit.AlreadyExploded = true;

        if (attack.Attacker is { } spotter)
        {
            spotter.TurnsSinceSpotted[(int)unit.Faction] = (byte)Bound(attackerTurnsSinceSpotted, 0, 255);
            spotter.TurnsLeftSpottedForSnipers[(int)unit.Faction] = (byte)Bound(attackerTurnsLeftSpottedForSnipers, 0, 255);
        }

        // The explosion of a unit that blows up with its death, and the casualty checks, are later slices of this branch: refused by name.
        CampaignReason? refusal = null;
        if (explodes) refusal = BattleFireReasons.TerrainExplosionUnsupported;
        else if (respawnSet || NeedsCasualtyCheck(unit)) refusal = BattleFlowReasons.CasualtiesUnsupported;
        return new(damage, refusal);
    }

    private bool RandomPercent(int chance) => RandomChance.Percent(Random, chance);

    /// <summary>RuleItem::getExplosionRadius(attack) == 0: a damage type of a single target (a radius from the power is 1 or more).</summary>
    private static bool ExplosionRadiusIsZero(RuntimeRuleCatalog rules, BattleItem item) => ItemRuleOf(rules, item.ItemType).Damage.FixedRadius == 0;

    /// <summary>BattleUnit::getSpecialWeapon(const RuleItem*): the unit's built-in item of the type.</summary>
    private BattleItem? SpecialWeaponNamed(BattleUnit unit, string itemType)
    {
        foreach (var id in unit.SpecialWeaponIds)
            if (_items.Find(item => item.Id == id) is { } item && item.ItemType == itemType) return item;
        return null;
    }

    /// <summary>The armor size of the unit rule a victim turns into, or null for a unit that does not exist (the reference would fail).</summary>
    private static int? SpawnArmorSize(RuntimeRuleCatalog rules, string unitType) =>
        rules.Units.TryGet(unitType, out var handle) && rules.Units[handle].Value.Armor is { } armor ? rules.Armors[armor].Value.Size : null;

    /// <summary>
    /// RuleItem::getZombieUnit(victim): the unit the item turns the victim into, by the victim's armor and gender, then by its type, else the item's own.
    /// </summary>
    private static string ZombieUnitFor(RuntimeItemCombat combat, BattleUnit victim)
    {
        var byArmor = victim.Gender == 0 ? combat.ZombieUnitsByMaleArmor : combat.ZombieUnitsByFemaleArmor;
        if (byArmor.TryGetValue(victim.ArmorId, out var named)) return named;
        return combat.ZombieUnitsByType.TryGetValue(victim.UnitType, out var byType) ? byType : combat.ZombieUnit;
    }

    /// <summary>
    /// TileEngine::hitUnit: the target takes the damage (<see cref="DamageUnit"/>), an X-COM attacker earns its experience, and the last unit to hit
    /// the victim is remembered as its murderer unless this hit did no harm to a victim that is already dying (it burns, bleeds or loses health by
    /// itself). Returns whether the unit was hit (a unit with no health is not), and the refusal of <see cref="DamageUnit"/>.
    /// </summary>
    internal (bool Hit, CampaignReason? Refusal) HitUnit(RuntimeRuleCatalog rules, in BattleAttack attack, BattleUnit? target, BattlePosition relative,
        int damage, RuntimeItemDamageRule type, bool rangeAttack)
    {
        if (target is null || target.Health <= 0) return (false, null);
        var healthOrig = target.Health;
        var result = DamageUnit(rules, target, relative, damage, type, attack);
        var healthDamage = healthOrig - target.Health;

        // single place for firing/throwing/melee experience training
        if (attack.Attacker is { OriginalFaction: BattleFaction.Player }) AwardExperience(rules, attack, target, rangeAttack);

        // Use case: an xcom soldier throwing a smoke grenade on a dying unit should not override the previously remembered murderer.
        // (Armor recovery bonuses are not ported yet, so a unit never regenerates negatively here: branch 6.)
        var relevant = !(attack.Attacker is not null && healthDamage <= 0 && target.MurdererId > 0 && (target.Fire > 0 || target.FatalWoundCount > 0));
        if (relevant && attack.Attacker is { } murderer)
        {
            // Record the last unit to hit our victim. If a victim dies without warning, this unit gets the credit.
            target.MurdererId = murderer.Id;
            target.MurdererWeapon = attack.Weapon?.ItemType ?? "STR_WEAPON_UNKNOWN";
            target.MurdererWeaponAmmo = attack.DamageItem?.ItemType ?? "STR_WEAPON_UNKNOWN";
        }
        return (true, result.Refusal);
    }

    // ---- experience ----

    private const int ExperienceDefault = 0;

    /// <summary>
    /// TileEngine::awardExperience: what an attack with the weapon trains in the attacker, by the weapon's experience mode or by what it is and how it
    /// is used. Only hits on hostile units (by birth, and for the extended system also by allegiance now) count; the mana experience of the weapon
    /// is added first. Returns whether experience was awarded.
    /// </summary>
    internal bool AwardExperience(RuntimeRuleCatalog rules, in BattleAttack attack, BattleUnit target, bool rangeAttack)
    {
        if (attack.Attacker is not { } unit || attack.Weapon is not { } weapon) return false;
        var equipment = rules.Battle.Equipment.Items[weapon.ItemType];
        var weaponRule = ItemRuleOf(rules, weapon.ItemType);
        var extended = rules.Campaign.Combat.ExtendedExperienceAwardSystem;
        AddManaExperience(unit, equipment.Combat.ManaExperience);

        var mode = equipment.Combat.ExperienceTrainingMode;
        TrainingKind first = TrainingKind.None, second = TrainingKind.None;
        var multiply = 100;
        if (mode > ExperienceDefault)
        {
            // can train psi strength and psi skill only if the soldier's own psi skill is already above 0
            if (mode is >= 24 and <= 31 && unit.SoldierId is not null && unit.SoldierPsiSkill <= 0) return false;
            switch (mode)
            {
                case 1: first = TrainingKind.Melee; break;
                case 2: multiply = 50; first = TrainingKind.Melee; break;
                case 3: multiply = 33; first = TrainingKind.Melee; break;
                case 4: first = TrainingKind.Firing; break;
                case 5: multiply = 50; first = TrainingKind.Firing; break;
                case 6: multiply = 33; first = TrainingKind.Firing; break;
                case 7: first = TrainingKind.Throwing; break;
                case 8: multiply = 50; first = TrainingKind.Throwing; break;
                case 9: multiply = 33; first = TrainingKind.Throwing; break;
                case 10: first = TrainingKind.Firing; second = TrainingKind.Throwing; break;
                case 11: first = Either(TrainingKind.Firing, TrainingKind.Throwing); break;
                case 12: first = TrainingKind.Reactions; break;
                case 13: first = TrainingKind.Reactions; second = TrainingKind.Melee; break;
                case 14: first = TrainingKind.Reactions; second = TrainingKind.Firing; break;
                case 15: first = TrainingKind.Reactions; second = TrainingKind.Throwing; break;
                case 16: first = Either(TrainingKind.Reactions, TrainingKind.Melee); break;
                case 17: first = Either(TrainingKind.Reactions, TrainingKind.Firing); break;
                case 18: first = Either(TrainingKind.Reactions, TrainingKind.Throwing); break;
                case 19: first = TrainingKind.Bravery; break;
                case 20: multiply = 200; first = TrainingKind.Bravery; break;
                case 21: first = TrainingKind.Bravery; second = TrainingKind.Reactions; break;
                case 22: first = Either(TrainingKind.Bravery, TrainingKind.Reactions); break;
                case 23: multiply = 200; first = Either(TrainingKind.Bravery, TrainingKind.Reactions); break;
                case 24: first = TrainingKind.PsiStrength; break;
                case 25: multiply = 200; first = TrainingKind.PsiStrength; break;
                case 26: first = TrainingKind.PsiSkill; break;
                case 27: multiply = 200; first = TrainingKind.PsiSkill; break;
                case 28: first = TrainingKind.PsiStrength; second = TrainingKind.PsiSkill; break;
                case 29: multiply = 200; first = TrainingKind.PsiStrength; second = TrainingKind.PsiSkill; break;
                case 30: first = Either(TrainingKind.PsiStrength, TrainingKind.PsiSkill); break;
                case 31: multiply = 200; first = Either(TrainingKind.PsiStrength, TrainingKind.PsiSkill); break;
                default: return false; // ETM_NOTHING and anything unknown
            }
        }
        else if (weaponRule.BattleType is RuntimeBattleItem.GrenadeBattleType or RuntimeBattleItem.ProximityGrenadeBattleType)
        {
            // grenades and proxies: throwing for the extended system, firing for vanilla
            first = extended ? TrainingKind.Throwing : TrainingKind.Firing;
        }
        else if (weaponRule.BattleType == RuntimeBattleItem.MeleeBattleType)
        {
            first = TrainingKind.Melee;
        }
        else if (weaponRule.BattleType == RuntimeBattleItem.MedikitBattleType)
        {
            return false; // medikits don't train anything by default
        }
        else if (!rangeAttack)
        {
            first = TrainingKind.Melee; // a gun butt
        }
        else if (!extended)
        {
            first = TrainingKind.Firing;
        }
        else if (ArcingShot(rules, weapon, attack.Type))
        {
            first = TrainingKind.Throwing;
        }
        else
        {
            var maxRange = equipment.Combat.MaxRange;
            if (maxRange > 10) first = TrainingKind.Firing;
            else if (maxRange > 1) first = TrainingKind.Throwing;
            else if (maxRange == 1) first = TrainingKind.Melee;
            else return false; // what is this? no training!
        }

        if (weaponRule.BattleType != RuntimeBattleItem.MedikitBattleType)
        {
            // only enemies count, not friends or neutrals
            if (target.OriginalFaction != BattleFaction.Hostile) multiply = 0;
            // mind-controlled enemies don't count though!
            if (extended && target.Faction != BattleFaction.Hostile) multiply = 0;
        }

        for (var i = multiply / 100; i > 0; i--)
        {
            AddExperience(unit, first);
            AddExperience(unit, second);
        }
        if (RandomPercent(multiply % 100))
        {
            AddExperience(unit, first);
            AddExperience(unit, second);
        }
        return true;
    }

    private enum TrainingKind { None, Bravery, Reactions, Firing, Throwing, PsiSkill, PsiStrength, Melee }

    /// <summary>The coin flip of the "or" modes: <c>RNG::percent(50)</c> picks the first.</summary>
    private TrainingKind Either(TrainingKind first, TrainingKind second) => RandomPercent(50) ? first : second;

    private static void AddExperience(BattleUnit unit, TrainingKind kind)
    {
        var stats = unit.Experience;
        unit.Experience = unchecked(kind switch
        {
            TrainingKind.Bravery => stats with { Bravery = (short)(stats.Bravery + 1) },
            TrainingKind.Reactions => stats with { Reactions = (short)(stats.Reactions + 1) },
            TrainingKind.Firing => stats with { Firing = (short)(stats.Firing + 1) },
            TrainingKind.Throwing => stats with { Throwing = (short)(stats.Throwing + 1) },
            TrainingKind.PsiSkill => stats with { PsiSkill = (short)(stats.PsiSkill + 1) },
            TrainingKind.PsiStrength => stats with { PsiStrength = (short)(stats.PsiStrength + 1) },
            TrainingKind.Melee => stats with { Melee = (short)(stats.Melee + 1) },
            _ => stats,
        });
    }

    /// <summary>BattleUnit::addManaExp: a hundredth of the weapon's mana experience whole, the rest as a chance of one more.</summary>
    private void AddManaExperience(BattleUnit unit, int weaponStat)
    {
        if (weaponStat <= 0) return;
        var mana = unit.Experience.Mana + weaponStat / 100;
        if (RandomPercent(weaponStat % 100)) mana++;
        unit.Experience = unit.Experience with { Mana = unchecked((short)mana) };
    }

    // ---- TileEngine::hit ----

    /// <summary>
    /// The part of <c>TileEngine::hit</c> that concerns units: what stands in the hit voxel. A floor or an object hit passes through to the bodies
    /// lying on the tile (unconscious units' items) first, the first one still alive takes the hit, and the terrain is spared; a unit in the voxel is
    /// hit at where the voxel lies against its bottom centre. Returns the unit part's outcome: the effect it made (2 for a fire or a glowing body),
    /// whether a unit took the hit, and the refusal of the hit (a later slice's work).
    /// </summary>
    private (bool Hit, int Effect, CampaignReason? Refusal) HitUnitsInVoxel(RuntimeRuleCatalog rules, in BattleAttack attack, BattlePosition voxel, BattleVoxelType part,
        int tile, BattlePosition tilePosition, int damage, RuntimeItemDamageRule type, bool rangeAttack)
    {
        if (part is BattleVoxelType.Floor or BattleVoxelType.Object)
        {
            foreach (var item in ItemsOnTile(tilePosition).ToList())
            {
                if (item.CorpseUnitId is not { } corpse) continue;
                var (hit, refusal) = HitUnit(rules, attack, UnitById(corpse), new BattlePosition(0, 0, 0), damage, type, rangeAttack);
                if (!hit) continue;
                return (true, GlowRange(rules, item) is not null ? 2 : 0, refusal);
            }
            return (false, 0, null);
        }
        if (OverlappingUnit(rules, tile) is { Health: > 0 } body)
        {
            // A unit that stands on the tile below is hit 24 voxels lower.
            var verticalOffset = body != UnitOnTile(tile) ? 24 : 0;
            var half = body.ArmorSize * 8;
            var position = body.Position!.Value;
            var targetVoxel = new BattlePosition(position.X * 16 + half, position.Y * 16 + half,
                position.Z * 24 + body.FloatHeightOf(rules) - Map.TerrainLevel(tile));
            var relative = new BattlePosition(voxel.X - targetVoxel.X, voxel.Y - targetVoxel.Y, voxel.Z - targetVoxel.Z - verticalOffset);
            var (_, refusal) = HitUnit(rules, attack, body, relative, damage, type, rangeAttack);
            return (true, body.Fire != 0 ? 2 : 0, refusal);
        }
        return (false, 0, null);
    }
}
