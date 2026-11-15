using System.Text;
using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;
using static Oxce.CompatibilityTests.BattleMovementGroundworkTests;
using static Oxce.CompatibilityTests.BattleStartTests;
using static Oxce.CompatibilityTests.BattleTestSupport;
using static Oxce.CompatibilityTests.BattleTestTerrain;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S1: the combat groundwork. The item, armor and unit rules' combat fields in the runtime rules (<c>RuleItem.cpp</c>,
/// <c>RuleDamageType.cpp</c>, <c>RuleStatBonus.cpp</c>, <c>Armor.cpp</c>, <c>Unit.cpp</c>, <c>RuleSoldier.cpp</c>), the combat constants of <c>Mod.cpp</c>,
/// and the script refusals (<c>battle.combat-script-unsupported</c>, <c>battle.stat-bonus-script-unsupported</c>)
/// (<c>src/Mod</c> at 4df3a5e). The pure rules are tested in <c>RuleStatBonusTests</c> and <c>RuntimeItemDamageRuleTests</c>.
/// </summary>
public sealed class BattleCombatGroundworkTests
{
    private const string GunText = "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1";
    private const string GrenadeText = "- type: GRENADE\n    battleType: 4\n    weight: 1";
    private const string SuitText = "- type: SUIT\n    movementType: 1";
    private const string VehicleArmorText = "- type: SMALL_VEHICLE_ARMOR\n    size: 1";

    private static string WithGun(string extra) => Replaced(Rules, GunText, GunText + "\n    " + extra);

    private static string WithSuit(string extra) => Replaced(Rules, SuitText, SuitText + "\n    " + extra);

    private static RuntimeRuleCatalog Open(string rules) => BattleMovementGroundworkTests.Open(rules).Rules;

    private static RuntimeItemRule Strategic(RuntimeRuleCatalog rules, string item) =>
        rules.Items.TryGet(item, out var handle) ? rules.Items[handle].Value : throw new InvalidOperationException(item);

    private static RuntimeArmorRule StrategicArmor(RuntimeRuleCatalog rules, string armor) =>
        rules.Armors.TryGet(armor, out var handle) ? rules.Armors[handle].Value : throw new InvalidOperationException(armor);

    // ---- the constants ----

    [Fact]
    public void TheCombatConstantsDefaultAndAreReadFromTheGlobalRules()
    {
        var defaults = Open(Rules).Campaign.Combat;
        Assert.Equal((115, 80, -1, 0), (defaults.KneelBonusGlobal, defaults.OneHandedPenaltyGlobal, defaults.NoLosAccuracyPenaltyGlobal, defaults.ExplodeInventoryGlobal));
        Assert.Equal((0, 100, 12, 8, 0), (defaults.EnableCloseQuartersCombat, defaults.CloseQuartersAccuracyGlobal, defaults.CloseQuartersTuCostGlobal,
            defaults.CloseQuartersEnergyCostGlobal, defaults.CloseQuartersSneakUpGlobal));
        Assert.Equal((0, 0), (defaults.ReactionFireThreshold, defaults.ReactionFireThresholdCivilians));
        Assert.Equal((3, 0, 3, 999, 0, 0, 999), (defaults.AiUseDelayBlaster, defaults.AiUseDelayFirearm, defaults.AiUseDelayGrenade, defaults.AiUseDelayProxy,
            defaults.AiUseDelayMelee, defaults.AiUseDelayPsionic, defaults.AiUseDelayMedikit));
        Assert.Equal((100, false, false, false, 0, 0, 0), (defaults.TuRecoveryWakeUpNewTurn, defaults.StunningImprovesMorale, defaults.StatisticalBulletConservation,
            defaults.ExtendedExperienceAwardSystem, defaults.ExtendedSpotOnHitForSniping, defaults.ExtendedTerrainMelee, defaults.ExtendedUnderwaterThrowFactor));
        Assert.Empty(defaults.HitChancesTable);
        Assert.Equal((false, ""), (defaults.ManaEnabled, defaults.ManaUnlockResearch));
        var ranges = Open(Rules).Campaign.Interception;
        Assert.Equal((100, 50, 5, 10), (ranges.DamageRange, ranges.ExplosiveDamageRange, ranges.FireDamageRange[0], ranges.FireDamageRange[1]));

        var rules = Rules + """

            kneelBonusGlobal: 120
            oneHandedPenaltyGlobal: 70
            noLOSAccuracyPenaltyGlobal: 30
            explodeInventoryGlobal: 2
            enableCloseQuartersCombat: 1
            closeQuartersAccuracyGlobal: 90
            closeQuartersTuCostGlobal: 10
            closeQuartersEnergyCostGlobal: 6
            closeQuartersSneakUpGlobal: 1
            stunningImprovesMorale: true
            statisticalBulletConservation: true
            tuRecoveryWakeUpNewTurn: 50
            turnAIUseGrenade: 5
            turnAIUseBlaster: 6
            ai:
              useDelayFirearm: 1
              useDelayGrenade: 8
              aiUseDelayProxy: 9
              useDelayMelee: 2
              useDelayPsionic: 4
              useDelayMedikit: 7
              reactionFireThreshold: 40
              reactionFireThresholdCiv: 20
            constants:
              damageRange: 80
              explosiveDamageRange: 40
              fireDamageRange: [3, 4]
              extendedExperienceAwardSystem: true
              extendedSpotOnHitForSniping: 2
              extendedTerrainMelee: 3
              extendedUnderwaterThrowFactor: 80
            """;
        var set = Open(rules).Campaign;
        Assert.Equal((120, 70, 30, 2), (set.Combat.KneelBonusGlobal, set.Combat.OneHandedPenaltyGlobal, set.Combat.NoLosAccuracyPenaltyGlobal, set.Combat.ExplodeInventoryGlobal));
        Assert.Equal((1, 90, 10, 6, 1), (set.Combat.EnableCloseQuartersCombat, set.Combat.CloseQuartersAccuracyGlobal, set.Combat.CloseQuartersTuCostGlobal,
            set.Combat.CloseQuartersEnergyCostGlobal, set.Combat.CloseQuartersSneakUpGlobal));
        Assert.Equal((40, 20), (set.Combat.ReactionFireThreshold, set.Combat.ReactionFireThresholdCivilians));
        // The legacy turnAIUseBlaster stays when the ai section does not set the blaster; the ai section wins for the grenade.
        Assert.Equal((6, 1, 8, 9, 2, 4, 7), (set.Combat.AiUseDelayBlaster, set.Combat.AiUseDelayFirearm, set.Combat.AiUseDelayGrenade, set.Combat.AiUseDelayProxy,
            set.Combat.AiUseDelayMelee, set.Combat.AiUseDelayPsionic, set.Combat.AiUseDelayMedikit));
        Assert.Equal((50, true, true, true, 2, 3, 80), (set.Combat.TuRecoveryWakeUpNewTurn, set.Combat.StunningImprovesMorale, set.Combat.StatisticalBulletConservation,
            set.Combat.ExtendedExperienceAwardSystem, set.Combat.ExtendedSpotOnHitForSniping, set.Combat.ExtendedTerrainMelee, set.Combat.ExtendedUnderwaterThrowFactor));
        Assert.Equal((80, 40, 3, 4), (set.Interception.DamageRange, set.Interception.ExplosiveDamageRange, set.Interception.FireDamageRange[0], set.Interception.FireDamageRange[1]));
    }

    [Fact]
    public void TheConstantsSectionIsTheOnlyPlaceTheDamageRangesAndExtendedOptionsAreRead()
    {
        // At the root they are ignored; the section may also be a sequence of mappings (the backward compatible form), later entries over earlier.
        var root = Open(Rules + "\ndamageRange: 80\nextendedTerrainMelee: 3\n");
        Assert.Equal((100, 0), (root.Campaign.Interception.DamageRange, root.Campaign.Combat.ExtendedTerrainMelee));
        var sequence = Open(Rules + "\nconstants:\n  - damageRange: 80\n  - damageRange: 60\n    extendedTerrainMelee: 3\n");
        Assert.Equal((60, 3), (sequence.Campaign.Interception.DamageRange, sequence.Campaign.Combat.ExtendedTerrainMelee));
    }

    private static string Table(int rows, int columns = 61, int fill = 1)
    {
        var text = new StringBuilder();
        for (var row = 0; row < rows; row++) text.Append("        ").Append(row).Append(": [").Append(string.Join(", ", Enumerable.Repeat(fill, columns))).Append("]\n");
        return text.ToString();
    }

    [Fact]
    public void TheHitChancesTableKeepsOnlyTheTablesWithFortyRowsOfSixtyOneValues()
    {
        var rules = Rules + "\nhitChancesTable:\n" +
            "  - unitSize: 1\n    distances:\n" + Table(40, fill: 7) +
            "  - unitSize: 2\n    distances:\n" + Table(39) +
            "  - unitSize: 3\n    distances:\n" + Table(40, columns: 60) +
            "  - distances:\n" + Table(40);
        var table = Open(rules).Campaign.Combat;
        Assert.Equal([1], table.HitChancesTable.Keys);
        Assert.Equal(40 * 61, table.HitChances(1)!.Count);
        Assert.All(table.HitChances(1)!, static value => Assert.Equal(7, value));
        Assert.Null(table.HitChances(2));
    }

    // ---- the item ----

    [Fact]
    public void AnItemsCombatFieldsDefault()
    {
        var rules = Open(Rules);
        var combat = rules.Battle.Equipment.Items["GUN"].Combat;
        Assert.Equal(new RuntimeItemAction(200, 1, 1, true, 0, -1, -1, -1, false, "STR_AIMED_SHOT", ""), combat.Aimed);
        Assert.Equal(new RuntimeItemAction(7, 3, 1, true, 0, -1, -1, -1, false, "STR_AUTO_SHOT", ""), combat.Auto);
        Assert.Equal(new RuntimeItemAction(15, 1, 1, true, 0, -1, -1, -1, false, "STR_SNAP_SHOT", ""), combat.Snap);
        Assert.Equal(new RuntimeItemAction(0, 1, 1, true, -1, -1, -1, -1, false, "", ""), combat.Melee);
        Assert.Equal(combat.Auto, combat.Action("Auto"));
        Assert.Throws<ArgumentOutOfRangeException>(() => combat.Action("Throw"));
        Assert.Equal((false, 0f, 0f), (combat.IgnoreAmmoPower, combat.PowerRangeReduction, combat.PowerRangeThreshold));
        Assert.Equal((200, 0, 2), (combat.MaxRange, combat.MinRange, combat.Dropoff));
        Assert.Equal((200, 200, 99, 99, 5), (combat.ThrowRange, combat.UnderwaterThrowRange, combat.ThrowDropoffRange, combat.UnderwaterThrowDropoffRange, combat.ThrowDropoff));
        // The globals stand in for the -1 of the item: kneel 115, one-handed 80, no penalty out of sight, close quarters 100, no inventory explosion.
        Assert.Equal((100, -1, 115, 80, 0), (combat.AccuracyCloseQuarters, combat.NoLosAccuracyPenalty, combat.KneelBonus, combat.OneHandedPenalty, combat.ExplodeInventory));
        Assert.Equal((false, 0, 0, 0, 100, 100), (combat.ArcingShot, combat.SprayWaypoints, combat.ShotgunPellets, combat.ShotgunBehavior, combat.ShotgunSpread, combat.ShotgunChoke));
        Assert.Equal(["CLIP"], combat.CompatibleAmmo[0]);
        Assert.All(combat.CompatibleAmmo.Skip(1), static slot => Assert.Empty(slot));
        Assert.Equal([15, 15, 15, 15], combat.TimeUnitsToLoad);
        Assert.Equal([8, 8, 8, 8], combat.TimeUnitsToUnload);
        Assert.Equal((false, false, false, false, false, false), (combat.LosRequired, combat.LandOnly, combat.SpecialUseEmptyHand, combat.SpecialUseEmptyHandShow,
            combat.IsFireExtinguisher, combat.ConvertToCivilian));
        Assert.Equal((7, -1, 0, 25, 0, 0), (combat.TargetMatrix, combat.SpecialType, combat.AiUseDelay, combat.AiMeleeHitCount, combat.ExperienceTrainingMode, combat.ManaExperience));
        Assert.Equal(("", 1, 100), (combat.ZombieUnit, combat.ZombieUnitFaction, combat.ZombieUnitChance));
        Assert.Empty(combat.ZombieUnitsByType);
        Assert.False(combat.StatBonuses.AnyScript);
        // Of the strategic projection: the power and the damage types default as the reference's RuleItem and Mod::Mod.
        var gun = Strategic(rules, "GUN");
        Assert.Equal((0, 0, 0, 0), (gun.Power, gun.MeleePower, gun.Damage.ResistType, gun.Accuracy.Aimed));
        Assert.Equal(RuntimeItemDamageRule.RandomStandard, gun.Damage.RandomType);
        Assert.Equal(7, gun.MeleeDamage.ResistType);
    }

    [Fact]
    public void AnItemsCombatFieldsAreReadAndItsActionConfigsOverrideTheActionKeys()
    {
        var rules = Open(WithGun("""
            aimRange: 25
                autoRange: 9
                snapRange: 18
                autoShots: 4
                accuracyAimed: 70
                confAimed: {shots: 2, spendPerShot: 3, followProjectiles: false, ammoSlot: 1, arcing: true, name: STR_X, shortName: XS, ammoZombieUnitChanceOverride: 5}
                confAuto: {shots: 6, ammoSlot: 9}
                maxRange: 30
                minRange: 2
                dropoff: 4
                throwRange: 10
                underwaterThrowRange: 8
                throwDropoffRange: 6
                underwaterThrowDropoffRange: 5
                throwDropoff: 3
                powerRangeReduction: 1.5
                powerRangeThreshold: 2
                ignoreAmmoPower: true
                arcingShot: true
                sprayWaypoints: 3
                shotgunPellets: 5
                shotgunBehavior: 1
                shotgunSpread: 50
                shotgunChoke: 80
                LOSRequired: true
                landOnly: true
                underwaterOnly: true
                specialUseEmptyHand: true
                specialUseEmptyHandShow: true
                isFireExtinguisher: true
                convertToCivilian: true
                targetMatrix: 5
                specialType: 14
                ai: {meleeHitCount: 10}
                experienceTrainingMode: 2
                manaExperience: 3
                ammo:
                  1: {compatibleAmmo: [CLIP, GRENADE], tuLoad: 20, tuUnload: 9}
                accuracyCloseQuarters: 70
                noLOSAccuracyPenalty: 25
                kneelBonus: 130
                oneHandedPenalty: 60
                explodeInventory: 1
                zombieUnit: ALIEN
                zombieUnitFaction: 0
                zombieUnitByArmorMale: {SUIT: VEHICLE}
                zombieUnitByArmorFemale: {SUIT: CIVILIAN}
                zombieUnitByType: {ALIEN: CIVILIAN}
                specialChance: 30
            """));
        var combat = rules.Battle.Equipment.Items["GUN"].Combat;
        // confAimed's keys come after the aimRange key and win; an out-of-range ammo slot (9) is ignored.
        Assert.Equal(new RuntimeItemAction(25, 2, 3, false, 1, 5, -1, -1, true, "STR_X", "XS"), combat.Aimed);
        Assert.Equal(new RuntimeItemAction(9, 6, 1, true, 0, -1, -1, -1, false, "STR_AUTO_SHOT", ""), combat.Auto);
        Assert.Equal(18, combat.Snap.Range);
        Assert.Equal((30, 2, 4), (combat.MaxRange, combat.MinRange, combat.Dropoff));
        Assert.Equal((10, 8, 6, 5, 3), (combat.ThrowRange, combat.UnderwaterThrowRange, combat.ThrowDropoffRange, combat.UnderwaterThrowDropoffRange, combat.ThrowDropoff));
        Assert.Equal((true, 1.5f, 2f), (combat.IgnoreAmmoPower, combat.PowerRangeReduction, combat.PowerRangeThreshold));
        Assert.Equal((true, 3, 5, 1, 50, 80), (combat.ArcingShot, combat.SprayWaypoints, combat.ShotgunPellets, combat.ShotgunBehavior, combat.ShotgunSpread, combat.ShotgunChoke));
        Assert.Equal((true, true, true, true, true, true), (combat.LosRequired, combat.LandOnly, combat.SpecialUseEmptyHand, combat.SpecialUseEmptyHandShow,
            combat.IsFireExtinguisher, combat.ConvertToCivilian));
        Assert.Equal((5, 14, 10, 2, 3), (combat.TargetMatrix, combat.SpecialType, combat.AiMeleeHitCount, combat.ExperienceTrainingMode, combat.ManaExperience));
        Assert.Equal(["CLIP", "GRENADE"], combat.CompatibleAmmo[1]);
        Assert.Equal((20, 9, 15, 8), (combat.TimeUnitsToLoad[1], combat.TimeUnitsToUnload[1], combat.TimeUnitsToLoad[0], combat.TimeUnitsToUnload[0]));
        Assert.Equal((70, 25, 130, 60, 1), (combat.AccuracyCloseQuarters, combat.NoLosAccuracyPenalty, combat.KneelBonus, combat.OneHandedPenalty, combat.ExplodeInventory));
        // The zombie chance follows specialChance until it is set; the units are by armor and gender, and by type.
        Assert.Equal(("ALIEN", 0, 30), (combat.ZombieUnit, combat.ZombieUnitFaction, combat.ZombieUnitChance));
        Assert.Equal(("VEHICLE", "CIVILIAN", "CIVILIAN"), (combat.ZombieUnitsByMaleArmor["SUIT"], combat.ZombieUnitsByFemaleArmor["SUIT"], combat.ZombieUnitsByType["ALIEN"]));
        var gun = Strategic(rules, "GUN");
        Assert.Equal((70, true, true), (gun.Accuracy.Aimed, gun.WaterOnly, gun.TwoHanded == false));
    }

    [Fact]
    public void AnItemsGlobalsGiveWayToItsOwnValuesAndZombieChanceIsItsOwnWhenSet()
    {
        var rules = Open(Rules + "\nkneelBonusGlobal: 120\noneHandedPenaltyGlobal: 70\nnoLOSAccuracyPenaltyGlobal: 30\ncloseQuartersAccuracyGlobal: 90\nexplodeInventoryGlobal: 2\n");
        var gun = rules.Battle.Equipment.Items["GUN"].Combat;
        Assert.Equal((120, 70, 30, 90), (gun.KneelBonus, gun.OneHandedPenalty, gun.NoLosAccuracyPenalty, gun.AccuracyCloseQuarters));
        // Only a grenade explodes in the inventory by the global; anything else by its own setting (or not).
        Assert.Equal((0, 2), (gun.ExplodeInventory, rules.Battle.Equipment.Items["GRENADE"].Combat.ExplodeInventory));
        var own = Open(Replaced(Rules, GrenadeText, GrenadeText + "\n    explodeInventory: 0\n    zombieUnitChance: 40\n    specialChance: 20\n    isExplodingInHands: true"));
        var grenade = own.Battle.Equipment.Items["GRENADE"].Combat;
        // isExplodingInHands is the legacy 2 or 0, and explodeInventory comes after it.
        Assert.Equal((0, 40), (grenade.ExplodeInventory, grenade.ZombieUnitChance));
        var legacy = Open(Replaced(Rules, GrenadeText, GrenadeText + "\n    isExplodingInHands: true"));
        Assert.Equal(2, legacy.Battle.Equipment.Items["GRENADE"].Combat.ExplodeInventory);
    }

    [Fact]
    public void AnItemsAiUseDelayIsItsOwnOrTheModsByBattleType()
    {
        var rules = Open(Rules + "\nai: {useDelayBlaster: 4, useDelayFirearm: 1, useDelayGrenade: 6, aiUseDelayProxy: 7, useDelayMelee: 2, useDelayPsionic: 3, useDelayMedikit: 5}\n");
        int Delay(string item) => rules.Battle.Equipment.Items[item].Combat.AiUseDelay;
        Assert.Equal((1, 6, 3, -1, -1), (Delay("GUN"), Delay("GRENADE"), Delay("AMP"), Delay("CLIP"), Delay("VEHICLE_B")));
        var more = Open(Replaced(Rules + "\nai: {useDelayBlaster: 4, useDelayMelee: 2, useDelayMedikit: 5, aiUseDelayProxy: 7}\n", GunText, GunText + "\n    waypoints: 3\n" +
            "  - type: STAB\n    battleType: 3\n    clipSize: -1\n  - type: MEDI\n    battleType: 6\n  - type: MINE\n    battleType: 5\n  - type: OWN\n    battleType: 4\n    ai: {useDelay: 11}"));
        Assert.Equal((4, 2, 5, 7, 11), (more.Battle.Equipment.Items["GUN"].Combat.AiUseDelay, more.Battle.Equipment.Items["STAB"].Combat.AiUseDelay,
            more.Battle.Equipment.Items["MEDI"].Combat.AiUseDelay, more.Battle.Equipment.Items["MINE"].Combat.AiUseDelay, more.Battle.Equipment.Items["OWN"].Combat.AiUseDelay));
    }

    [Fact]
    public void ThePowerLosesWhatTheRangeReducesPastTheThreshold()
    {
        var combat = Open(WithGun("powerRangeReduction: 2\n    powerRangeThreshold: 3")).Battle.Equipment.Items["GUN"].Combat;
        // 2 power per tile (16 voxels) beyond 3 tiles (48 voxels): at 80 voxels, 32 over, 4.
        Assert.Equal(4f, combat.PowerRangeReductionAt(80));
        Assert.Equal(0f, combat.PowerRangeReductionAt(48));
        Assert.Equal(0f, combat.PowerRangeReductionAt(10));
        Assert.Equal(0f, Open(Rules).Battle.Equipment.Items["GUN"].Combat.PowerRangeReductionAt(500));
    }

    // ---- the damage types ----

    [Fact]
    public void AnItemsDamageTypeIsTheBaseTypeWithTheBlastRadiusAndTheAlterKeysOverIt()
    {
        var rules = Open(WithGun("damageType: 3\n    blastRadius: 5\n    damageAlter: {ToTile: 0.7, RandomType: 7, IgnoreDirection: true, RadiusReduction: 4, FireThreshold: 20, RandomWoundType: 1, TileDamageLimit: 9}\n" +
            "    meleeType: 6\n    meleeAlter: {ToStun: 0.5, ArmorIgnore: 3}\n    power: 55\n    meleePower: 12"));
        var gun = Strategic(rules, "GUN");
        // DT_HE under the keys: the blast radius replaces the -1 and the keys the fields; what they leave stays the base type's.
        Assert.Equal((5, RuntimeItemDamageRule.RandomEasy, 3, true, 4f, 20, 1, 9), (gun.Damage.FixedRadius, gun.Damage.RandomType, gun.Damage.ResistType,
            gun.Damage.IgnoreDirection, gun.Damage.RadiusReduction, gun.Damage.FireThreshold, gun.Damage.RandomWoundType, gun.Damage.TileDamageLimit));
        Assert.Equal((0.7f, 1f, true, 0.05f), (gun.Damage.Conversions.Tile, gun.Damage.Conversions.Item, gun.Damage.IgnoreSelfDestruct, gun.Damage.RadiusEffectiveness));
        // meleeType 6 is the stun base type with its stun share changed.
        Assert.Equal((-1, 6, 0.5f, 3, false), (gun.MeleeDamage.FixedRadius, gun.MeleeDamage.ResistType, gun.MeleeDamage.Conversions.Stun, gun.MeleeDamage.ArmorIgnore,
            gun.MeleeDamage.RandomStun));
        Assert.Equal((55, 12), (gun.Power, gun.MeleePower));

        // Without keys a firearm's melee is DT_MELEE; a corpse's damage type is DT_HE (the compatibility hack for corpse explosions), a grenade's none.
        var plain = Open(Rules);
        Assert.Equal((7, true, true), (Strategic(plain, "GUN").MeleeDamage.ResistType, Strategic(plain, "GUN").MeleeDamage.IgnoreSelfDestruct, Strategic(plain, "GUN").MeleeDamage.IgnoreOverKill));
        var corpse = Strategic(Open(Replaced(Rules, GrenadeText, GrenadeText + "\n  - type: BODY\n    battleType: 11")), "BODY");
        Assert.Equal((3, -1, RuntimeItemDamageRule.RandomExplosion), (corpse.Damage.ResistType, corpse.Damage.FixedRadius, corpse.Damage.RandomType));
        Assert.Equal(0, Strategic(plain, "GRENADE").Damage.FixedRadius);
    }

    // ---- the armor ----

    [Fact]
    public void AnArmorsCombatFieldsDefault()
    {
        var rules = Open(Rules);
        var armor = rules.Battle.Equipment.Armors["SUIT"];
        Assert.Equal(Enumerable.Repeat(1f, 20), armor.DamageModifiers);
        Assert.Equal((null, null, null, null, null), (armor.PainImmune, armor.BleedImmune, armor.ZombieImmune, armor.IgnoresMeleeThreat, armor.CreatesMeleeThreat));
        Assert.Equal((0.5f, 0f, 0, false), (armor.OverKill, armor.MeleeDodgeBackPenalty, armor.MeleeOriginVoxelVerticalOffset, armor.InstantWoundRecovery));
        Assert.Equal([("psiStrength", 1f), ("psiSkill", 0.2f)], armor.StatBonuses.PsiDefence.Terms.Select(static term => (term.Stat, term.Coefficients.Single())));
        Assert.Empty(armor.StatBonuses.MeleeDodge.Terms);
        // The side armor and the corpses are the strategic projection's.
        Assert.Single(StrategicArmor(rules, "SUIT").Script.CorpseBattle);
        Assert.Equal(4, StrategicArmor(rules, "VEHICLE_ARMOR").Script.CorpseBattle.Count);
        // Unit rules: capturable, and the morale lost when it dies.
        Assert.Equal((true, 100), (rules.Battle.Equipment.Units["ALIEN"].Capturable, rules.Battle.Equipment.Units["ALIEN"].MoraleLossWhenKilled));
        Assert.All(rules.Battle.Equipment.SoldierMoraleLossWhenKilled.Values, static value => Assert.Equal(100, value));
    }

    [Fact]
    public void AnArmorsCombatFieldsAreRead()
    {
        var rules = Open(WithSuit("""
            damageModifier: [0.5, 0.25, 2]
                overKill: 1.5
                meleeDodgeBackPenalty: 0.25
                meleeOriginVoxelVerticalOffset: 3
                instantWoundRecovery: true
                painImmune: true
                bleedImmune: false
                zombiImmune: true
                ignoresMeleeThreat: true
                createsMeleeThreat: false
                psiDefence: {psiStrength: [2, 0.1]}
                meleeDodge: {melee: 0.5}
            """));
        var armor = rules.Battle.Equipment.Armors["SUIT"];
        // A short list sets the first types and leaves the rest at 1.
        Assert.Equal(new float[] { 0.5f, 0.25f, 2 }.Concat(Enumerable.Repeat(1f, 17)), armor.DamageModifiers);
        Assert.Equal((true, false, true, true, false), (armor.PainImmune, armor.BleedImmune, armor.ZombieImmune, armor.IgnoresMeleeThreat, armor.CreatesMeleeThreat));
        Assert.Equal((1.5f, 0.25f, 3, true), (armor.OverKill, armor.MeleeDodgeBackPenalty, armor.MeleeOriginVoxelVerticalOffset, armor.InstantWoundRecovery));
        var defence = Assert.Single(armor.StatBonuses.PsiDefence.Terms);
        Assert.Equal("psiStrength", defence.Stat);
        Assert.Equal([2f, 0.1f], defence.Coefficients);
        Assert.Equal([("melee", 0.5f)], armor.StatBonuses.MeleeDodge.Terms.Select(static term => (term.Stat, term.Coefficients.Single())));

        // A big armor is immune to fear, bleeding and pain and creates no melee threat; it cannot be zombified however the rule says.
        var big = rules.Battle.Equipment.Armors["VEHICLE_ARMOR"];
        Assert.Equal((true, true, true, true, false), (big.FearImmune, big.BleedImmune, big.PainImmune, big.IgnoresMeleeThreat, big.CreatesMeleeThreat));
        Assert.True(big.ZombieImmune);
    }

    [Fact]
    public void AUnitAndASoldierRuleReadTheirMoraleLossAndCapturability()
    {
        var rules = Replaced(Rules, "- type: ALIEN\n    armor: SMALL_VEHICLE_ARMOR", "- type: ALIEN\n    armor: SMALL_VEHICLE_ARMOR\n    capturable: false\n    moraleLossWhenKilled: 150") +
            "\nsoldiers:\n  - type: RECRUIT\n    moraleLossWhenKilled: 130\n";
        var equipment = Open(rules).Battle.Equipment;
        Assert.Equal((false, 150), (equipment.Units["ALIEN"].Capturable, equipment.Units["ALIEN"].MoraleLossWhenKilled));
        Assert.Equal((true, 100), (equipment.Units["CIVILIAN"].Capturable, equipment.Units["CIVILIAN"].MoraleLossWhenKilled));
        Assert.Equal(130, equipment.SoldierMoraleLossWhenKilled["RECRUIT"]);
    }

    // ---- the script refusals ----

    private static CampaignActionBlocked Confirmed(string rules)
    {
        var (campaign, _) = Requested(rules, "UFO_SUITED");
        Started(campaign);
        campaign.Execute(new BeginBriefing("T", "C"));
        return Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new ConfirmBriefing()).Events));
    }

    private static string WithArmorScript(string parser) => Replaced(Rules, VehicleArmorText, VehicleArmorText + "\n    scripts:\n      " + parser + ": |\n        return;\n");

    [Theory]
    [InlineData("hitUnit")]
    [InlineData("damageUnit")]
    [InlineData("damageSpecialUnit")]
    [InlineData("tryMeleeAttackUnit")]
    [InlineData("reactionUnitAction")]
    [InlineData("reactionUnitReaction")]
    [InlineData("aiCalculateTargetWeight")]
    [InlineData("awardExperience")]
    public void AnArmorInTheBattleWithACombatHookIsRefusedAtTheBriefing(string parser) =>
        Assert.Equal("battle.combat-script-unsupported", Confirmed(WithArmorScript(parser)).Code);

    [Theory]
    [InlineData("hitUnitAmmo")]
    [InlineData("damageUnitAmmo")]
    [InlineData("damageSpecialUnitAmmo")]
    [InlineData("tryMeleeAttackItem")]
    [InlineData("reactionWeaponAction")]
    public void AnItemInTheBattleWithACombatHookIsRefusedAtTheBriefing(string parser) =>
        Assert.Equal("battle.combat-script-unsupported", Confirmed(WithGun("scripts:\n      " + parser + ": |\n        return;\n")).Code);

    [Fact]
    public void AGlobalEventForACombatHookOrAStatBonusIsRefused()
    {
        const string Event = "\nextended:\n  scripts:\n    PARSER:\n      - new: global-PARSER\n        offset: 1\n        code: |\n          return;\n";
        Assert.Equal("battle.combat-script-unsupported", Confirmed(Rules + Event.Replace("PARSER", "hitUnit", StringComparison.Ordinal)).Code);
        Assert.Equal("battle.stat-bonus-script-unsupported", Confirmed(Rules + Event.Replace("PARSER", "damageBonusBonusStats", StringComparison.Ordinal)).Code);
    }

    [Fact]
    public void AStatBonusWrittenAsCodeIsRefusedForTheItemsAndArmorsInTheBattleOnly()
    {
        Assert.Equal("battle.stat-bonus-script-unsupported", Confirmed(WithGun("accuracyMultiplier: return bonus;")).Code);
        Assert.Equal("battle.stat-bonus-script-unsupported", Confirmed(WithSuit("meleeDodge: return bonus;")).Code);
        Assert.Equal("battle.stat-bonus-script-unsupported", Confirmed(Replaced(Rules, VehicleArmorText, VehicleArmorText + "\n    psiDefence: return bonus;")).Code);
        // An item that is nowhere in the battle brings nothing: AMP is carried by the suit (specialWeapon) but VEHICLE_B is not.
        var (campaign, _) = Requested(Replaced(Rules, "- type: VEHICLE_B\n    fixedWeapon: true", "- type: VEHICLE_B\n    fixedWeapon: true\n    damageBonus: return bonus;"), "UFO_SUITED");
        Started(campaign);
        campaign.Execute(new BeginBriefing("T", "C"));
        Assert.IsType<BriefingConfirmed>(Assert.Single(campaign.Execute(new ConfirmBriefing()).Events));
    }

    [Fact]
    public void AnArmorOrItemOnlyACommandCanCreateIsCheckedForScriptsLikeOneInTheBattle()
    {
        // FLOAT_SUIT is worn by no unit of the battle; VEHICLE_B is a unit type that no unit has and whose fixed weapon is no item in it.
        const string FloatSuitText = "- type: FLOAT_SUIT\n    movementType: 3";
        const string TankText = "- type: VEHICLE_B\n    armor: SMALL_VEHICLE_ARMOR";
        var hook = Replaced(Replaced(Rules, TankText, "- type: VEHICLE_B\n    armor: FLOAT_SUIT"), FloatSuitText,
            FloatSuitText + "\n    scripts:\n      hitUnit: |\n        return;\n");
        var visible = Replaced(Replaced(Rules, TankText, "- type: VEHICLE_B\n    armor: FLOAT_SUIT"), FloatSuitText,
            FloatSuitText + "\n    scripts:\n      visibilityUnit: |\n        return;\n");
        var (campaign, _) = Requested(hook, "UFO_SUITED");
        Started(campaign);
        campaign.Execute(new BeginBriefing("T", "C"));
        // Nothing can bring the scripted armor in: the briefing is confirmed.
        Assert.IsType<BriefingConfirmed>(Assert.Single(campaign.Execute(new ConfirmBriefing()).Events));

        // An item of the battle that spawns that unit, or infects into it, or an ammunition the weapon takes, brings the armor's hooks in: refused at the briefing.
        Assert.Equal("battle.combat-script-unsupported", Confirmed(Replaced(hook, GunText, GunText + "\n    spawnUnit: VEHICLE_B")).Code);
        Assert.Equal("battle.combat-script-unsupported", Confirmed(Replaced(hook, GunText, GunText + "\n    zombieUnit: VEHICLE_B")).Code);
        Assert.Equal("battle.visibility-script-unsupported", Confirmed(Replaced(visible, GunText, GunText + "\n    spawnUnit: VEHICLE_B")).Code);
        // A unit type another spawns, and the items a spawn item or a creatable unit's weapon brings: the reach is transitive.
        var chained = Replaced(Replaced(hook, GunText, GunText + "\n    spawnUnit: ALIEN"), "- type: ALIEN\n    armor: SMALL_VEHICLE_ARMOR", "- type: ALIEN\n    armor: SMALL_VEHICLE_ARMOR\n    spawnUnit: VEHICLE_B");
        Assert.Equal("battle.combat-script-unsupported", Confirmed(chained).Code);
        var scriptedItem = Replaced(Replaced(Rules, "- type: VEHICLE_B\n    fixedWeapon: true", "- type: VEHICLE_B\n    fixedWeapon: true\n    scripts:\n      hitUnitAmmo: |\n        return;\n"),
            GunText, GunText + "\n    spawnItem: VEHICLE_B");
        Assert.Equal("battle.combat-script-unsupported", Confirmed(scriptedItem).Code);
    }

    [Fact]
    public void AFirearmThatTakesAPsiAmplifierAsAmmunitionIsRefusedBeforeItsShotCanFaultTheBattle()
    {
        // ExplosionBState's psi attack is branch 6's; the refusal comes at the briefing and for every command, not in the middle of a shot.
        var rules = Replaced(Rules, GunText, "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP, AMP]\n    weight: 1");
        Assert.Equal("battle.psi-unsupported", Confirmed(rules).Code);
        var (campaign, _) = Requested(Rules, "UFO_SUITED");
        Started(campaign);
        Assert.Null(Battle(campaign).CombatScriptRefusal(campaign.Content));
    }

    [Fact]
    public void ARemovedUnitsArmorHooksAndAnyOfTheItemKeysCodeAreHandledAsTheyAre()
    {
        // A code bonus on any of the item keys is refused when the item is in the battle.
        foreach (var key in new[] { "meleeBonus", "meleeMultiplier", "throwMultiplier", "closeQuartersMultiplier" })
            Assert.Equal("battle.stat-bonus-script-unsupported", Confirmed(WithGun(key + ": return bonus;")).Code);
        // A unit that is removed from the game (IgnoreMe) no longer brings its armor's hooks.
        var (campaign, _) = Requested(WithArmorScript("hitUnit"), "UFO_SUITED");
        Started(campaign);
        var battle = Battle(campaign);
        Assert.NotNull(battle.CombatScriptRefusal(campaign.Content));
        foreach (var unit in battle.Units.Where(static unit => unit.Faction != BattleFaction.Player)) unit.Status = BattleUnitStatus.IgnoreMe;
        Assert.Null(battle.CombatScriptRefusal(campaign.Content));
    }

    [Fact]
    public void AStatBonusWrittenAsCoefficientsIsRunNotRefused()
    {
        var rules = Open(Replaced(WithGun("damageBonus: {strength: 1}\n    accuracyMultiplier: {firing: 0.5}"), SuitText, SuitText + "\n    meleeDodge: {melee: 1}\n    psiDefence: {psiStrength: 1}")).Battle.Equipment;
        Assert.False(rules.Items["GUN"].Combat.StatBonuses.AnyScript);
        Assert.False(rules.Armors["SUIT"].StatBonuses.MeleeDodge.IsScript);
        Assert.True(rules.Items["GUN"].Combat.StatBonuses.Damage.Modded);
    }

    [Fact]
    public void ARunningBattleWithACombatScriptRefusesItsCommandsAndStillLoads()
    {
        foreach (var (rules, code) in new[]
        {
            (WithArmorScript("hitUnit"), "battle.combat-script-unsupported"),
            (WithGun("damageBonus: return bonus;"), "battle.stat-bonus-script-unsupported"),
        })
        {
            var (campaign, _) = Requested(rules, "UFO_SUITED");
            Started(campaign);
            var battle = Battle(campaign);
            battle.Phase = BattlePhase.InProgress;
            battle.Turn = 1;
            Assert.Equal(code, Refusal(campaign.Execute(new SelectNextUnit(battle.Units[0].Id, false, false))));
            // The battle is kept as it was when a save with it is loaded; it is only its commands that stop.
            var restored = CampaignState.Restore(campaign.Capture(), campaign.Content, new SplitMix64RandomSource(3), TerrainFiles());
            Assert.NotNull(Battle(restored));
            Assert.Equal(code, Refusal(restored.Execute(new SelectNextUnit(Battle(restored).Units[0].Id, false, false))));
        }
    }
}
