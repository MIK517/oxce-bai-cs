using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// Phase 7 branch 5 S9: <c>AIModule::brutalScoreFiringMode</c> for the reaction check (<c>checkLOF</c> and <c>reactionCheck</c> on), the score with which the AI
/// judges a shot or a strike at a target: the damage after the target's armor and damage type, the chance to hit, the shots the time units left allow, and
/// the blast's worth. A score of 0 or less is a reaction the unit would not make.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>
    /// brutalScoreFiringMode(action, target, true, true): the action's actor scores the action at the target. Only the actions a reaction can be (a hit or a shot)
    /// are scored; a throw (the grenade from the belt and its priming) and the no-sight form (<c>checkLOF</c> off) belong to the AI's turn, branch 6.
    /// </summary>
    internal float ReactionScore(BattleAimAction action, BattleUnit target)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(target);
        if (action.Type == BattleActionType.Throw) throw new ArgumentOutOfRangeException(nameof(action), "The throw's score belongs to the AI's turn.");
        // Sanity check first, if the passed action has no type, return 0.
        if (action.Type == BattleActionType.None) return 0;
        var unit = action.Actor;
        var weapon = action.Weapon;
        var type = action.Type;
        var originPosition = unit.RequiredPosition;
        var targetPosition = target.RequiredPosition;
        var targetTile = Map.TileIndex(targetPosition);
        var combat = RequiredRules.Battle.Equipment.Items[weapon.ItemType].Combat;
        var campaign = RequiredRules.Campaign;

        // Get base accuracy for the action
        float accuracy = FiringAccuracy(unit, weapon, type);
        var distanceSquared = DistanceSquaredBetween(originPosition, targetPosition);
        var distance = MathF.Sqrt(distanceSquared);
        int tuTotal = unit.TimeUnits, energyTotal = unit.Energy;
        float explosionMod = 1;

        // battleUFOExtenderAccuracy is off: the range dropoff is the player's, not the AI's.
        if (combat.NoLosAccuracyPenalty != -1)
        {
            // Whether the AI thinks the target is in its sight: within the distance it sees a unit on that tile (by day, or at dark on a dark tile that does not burn), less the smoke.
            var targetArmor = ArmorOf(target);
            var ownArmor = ArmorOf(unit);
            var viewDistance = BattleSight.MaxViewDistance(unit.Vision.MaxViewDistanceAtDay, targetArmor.CamouflageAtDay, ownArmor.AntiCamouflageAtDay);
            if (Map.Shade(targetTile) > campaign.MaximumDarknessToSeeUnits && Map.Fire(targetTile) == 0)
                viewDistance = BattleSight.MaxViewDistance(unit.Vision.MaxViewDistanceAtDark, targetArmor.CamouflageAtDark, ownArmor.AntiCamouflageAtDark);
            var minViewDistance = campaign.MaximumViewDistance / (1.0 + Map.Smoke(targetTile) / 3.0);
            viewDistance = Math.Min(viewDistance, (int)minViewDistance);
            if (distance > viewDistance) accuracy = (float)(accuracy * (combat.NoLosAccuracyPenalty / 100.0));
        }

        if (IsOutOfRange(weapon, distanceSquared)) accuracy = 0;
        if (type == BattleActionType.Hit)
        {
            var attackVoxel = new BattlePosition(originPosition.X * BattleGeometry.VoxelsPerTile + 8 * unit.ArmorSize, originPosition.Y * BattleGeometry.VoxelsPerTile + 8 * unit.ArmorSize,
                originPosition.Z * BattleGeometry.VoxelsPerLevel);
            var arc = ArcDirection(BattleGeometry.DirectionTo(CentreVoxel(target), attackVoxel), target.Direction);
            var penalty = 1.0f - arc * ArmorOf(target).MeleeDodgeBackPenalty / 4.0f;
            var dodge = MeleeDodge(target);
            if (dodge * penalty < accuracy) accuracy -= dodge * penalty;
            else accuracy = 0.01f;
            // We can definitely assume we'll be facing the target
            var directionToLook = BattleGeometry.DirectionTo(originPosition, targetPosition);
            if (!ValidMeleeRange(originPosition, directionToLook, unit, target, out _)) accuracy = 0;
        }
        else if (ShouldAvoidMeleeRange(unit, target) && distance < 2) accuracy = 0;

        float numberOfShots = type switch
        {
            BattleActionType.AimedShot => combat.Aimed.Shots,
            BattleActionType.SnapShot => combat.Snap.Shots,
            BattleActionType.AutoShot => combat.Auto.Shots,
            BattleActionType.Hit => combat.Melee.Shots,
            _ => 1,
        };

        var cost = BattleActionCost.For(RequiredRules, type, unit, weapon);
        // Return a score of zero if this firing mode doesn't exist for this weapon
        if (cost.Time == 0) return 0;
        // action->weapon->getAmmoForAction(type): the non-const form, which also needs one use's full spendPerShot.
        if (ActionIndex(type) is not { } index || AmmoForActionWithMessage(weapon, index, out _, out _) is not { } ammo) return 0;
        float damage = ItemRuleOf(ammo.ItemType).Power;
        var radius = ExplosionRadius(ammo, new BattleAttack(type, unit, null, ammo));
        if (radius > 0) explosionMod *= BrutalExplosiveEfficacy(targetPosition, unit, radius, false);
        var ammoCombat = RequiredRules.Battle.Equipment.Items[ammo.ItemType].Combat;
        if (ammoCombat.ShotgunPellets > 0) numberOfShots *= ammoCombat.ShotgunPellets;
        var damageType = ItemRuleOf(ammo.ItemType).Damage;

        // The power bonus is mutually exclusive with the ammunition's damage, so that a laser's power does not count twice.
        var powerBonus = PowerBonus(weapon, new BattleAttack(type, unit, weapon, ammo));
        if (powerBonus != 0) damage = powerBonus;

        var armor = ArmorOf(target);
        var sides = armor.Sides;
        float relevantArmor = SideFacingToPosition(target, originPosition) switch
        {
            -1 => sides[BattleUnit.SideUnder],
            1 => (sides[BattleUnit.SideLeft] + sides[BattleUnit.SideFront]) / 2.0f,
            3 => (sides[BattleUnit.SideLeft] + sides[BattleUnit.SideRear]) / 2.0f,
            5 => (sides[BattleUnit.SideRight] + sides[BattleUnit.SideRear]) / 2.0f,
            7 => (sides[BattleUnit.SideRight] + sides[BattleUnit.SideFront]) / 2.0f,
            // the four sides, as the reference's side order (front, left, rear, right) of the direction
            0 => sides[BattleUnit.SideFront],
            2 => sides[BattleUnit.SideLeft],
            4 => sides[BattleUnit.SideRear],
            _ => sides[BattleUnit.SideRight],
        };
        var damageRange = (float)(1.0 + campaign.Interception.DamageRange / 100.0);
        damage *= (uint)damageType.ResistType < (uint)armor.DamageModifiers.Count ? armor.DamageModifiers[damageType.ResistType] : 1f;
        float armorPreDamage = damageType.ArmorPreFinalDamage((int)damage, Random);
        damage = (damage * damageRange - relevantArmor) / 2.0f;
        // a reaction is worth at least one point of damage
        damage = Math.Max(1.0f, damage);
        float damageTypeMod = 0;
        if (damage > 0)
        {
            var shot = (int)damage;
            damageTypeMod += damageType.HealthFinalDamage(shot, Random) / damage;
            damageTypeMod += damageType.WoundFinalDamage(shot, Random) / damage;
            damageTypeMod += damageType.StunFinalDamage(shot, Random) / (2 * damage);
            if (damageType.ArmorFinalDamage(shot, Random) > 0) damageTypeMod += damageType.ArmorFinalDamage(shot, Random) / (3 * damage);
            if (damageType.MoraleFinalDamage(shot, Random) > 0) damageTypeMod += damageType.MoraleFinalDamage(shot, Random) / (5 * damage);
            if (damageType.EnergyFinalDamage(shot, Random) > 0) damageTypeMod += damageType.EnergyFinalDamage(shot, Random) / (10 * damage);
            if (damageType.ManaFinalDamage(shot, Random) > 0) damageTypeMod += damageType.ManaFinalDamage(shot, Random) / (10 * damage);
            if (damageType.TimeFinalDamage(shot, Random) > 0) damageTypeMod += damageType.TimeFinalDamage(shot, Random) / (10 * damage);
            // a target already caught in a grenade's blast this turn is worth half
            if (target.Position is not null && Map.Dangerous(targetTile)) damage /= 2.0f;
        }

        var attacks = (float)tuTotal / cost.Time;
        if (cost.Energy > 0) attacks = Math.Min(attacks, (float)energyTotal / cost.Energy);
        numberOfShots *= attacks;
        if (numberOfShots < 1) return 0;

        accuracy /= 100.0f;
        // Apply a modifier for higher/lower hit-chance when closer/further from the target. But not for melee-attacks.
        if (type != BattleActionType.Hit)
        {
            if (accuracy > 0) accuracy += Math.Max(1 - accuracy, 0.0f) / distance;
            accuracy = Math.Min(1.0f, accuracy);
        }

        // Melee-attacks have their own validity check (above).
        if (type != BattleActionType.Hit)
        {
            if (ArcingShot(weapon, type))
            {
                if (!ValidateArcingShot(new BattleAimAction(unit, type, weapon, targetPosition))) return 0;
            }
            else
            {
                var origin = Sight().OriginVoxel(action);
                if (!Sight().CanTargetUnit(origin, targetTile, out _, unit)) return 0;
            }
        }
        return (damage + armorPreDamage) * accuracy * numberOfShots * explosionMod * damageTypeMod;
    }
}
