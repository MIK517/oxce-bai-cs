using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using static Oxce.Gameplay.Battles.BattleBigWalls;

namespace Oxce.Gameplay.Battles;

/// <summary>The reasons the melee attack command refuses (Phase 7 branch 5 S6).</summary>
internal static class BattleAttackReasons
{
    internal static readonly CampaignReason WeaponUnavailable = new("battle.attack-weapon-unavailable",
        "The weapon is not in the hand of the unit, or in its special weapons.");

    internal static readonly CampaignReason ActionNotOffered = new("battle.attack-action-not-offered",
        "The weapon does not offer this action to this unit (a hit costs no time units, or the weapon needs a psi skill or the mana feature the unit lacks).");

    internal static readonly CampaignReason KindUnsupported = new("battle.attack-kind-unsupported",
        "The attack kind is not one the battle knows (the hit, the throw, the shots and the launch are attacks; priming has its own commands).");

    internal static readonly CampaignReason PrimeIsItsOwnCommand = new("battle.attack-kind-is-prime-command",
        "Priming and unpriming are the PrimeItem and UnprimeItem commands, not attacks.");

    internal static readonly CampaignReason NotPerformed = new("battle.attack-not-performed",
        "The attack could not start: the unit cannot act, or the target tile is not on the map.");

    /// <summary>A refusal the reference words as a message key (<c>STR_THERE_IS_NO_ONE_THERE</c>, <c>STR_NOT_ENOUGH_TIME_UNITS</c>, ...): the key is the reason's text key.</summary>
    internal static CampaignReason ForMessage(string key) => new("battle.attack-refused", $"The attack is refused: {key}.") { TextKey = key };

    internal const string NoOneThere = "STR_THERE_IS_NO_ONE_THERE";
}

/// <summary>
/// Melee (Phase 7 branch 5 S6): <c>MeleeAttackBState</c> (the player's hit in the unit's facing direction, no targeting turn), the melee part of
/// <c>ExplosionBState</c> (in <c>BattleExplosions.cs</c>), <c>TileEngine::meleeAttack</c> and <c>meleeAttackCalculate</c> with the melee dodge and its
/// penalty from behind, <c>validMeleeRange</c> and <c>validTerrainMeleeRange</c>, and the command behind the action menu's hit. The reaction to the attack
/// (S9), the melee reaction of a hostile unit (S9) and the AI's chosen target (branch 6) are not ported. The hit log (<c>appendToHitLog</c>) is runtime
/// text and draws nothing. User options at their defaults: <c>diagTerrainMelee</c> is off, so terrain melee never faces a diagonal.
/// </summary>
internal sealed partial class BattleState
{
    private const int MeleeOriginHeightReduction = 4;
    private const int BurnFloorIgnitePower = 15;

    /// <summary>RuleItem::getMeleeBonus(attack): the item's melee power with the bonus of the attacker (a bonus written as code is refused at the briefing).</summary>
    private int MeleeBonus(BattleItem item, in BattleAttack attack)
    {
        var rule = ItemRuleOf(item.ItemType);
        var bonus = RequiredRules.Battle.Equipment.Items[item.ItemType].Combat.StatBonuses.Melee;
        var power = bonus.IsScript ? rule.MeleePower : bonus.Evaluate(attack.Attacker, rule.MeleePower);
        return Hooks.StatBonus(BattleStatBonusKind.MeleeBonus, attack.Attacker, item, power);
    }

    /// <summary>Armor::getMeleeDodge(unit): the armor's dodge bonus for the unit.</summary>
    private int MeleeDodge(BattleUnit unit)
    {
        var bonus = ArmorOf(unit).StatBonuses.MeleeDodge;
        return Hooks.StatBonus(BattleStatBonusKind.MeleeDodge, unit, null, bonus.Evaluate(unit));
    }

    /// <summary>TileEngine::getArcDirection: the angle between two directions, 0 for the same to 4 for opposite.</summary>
    internal static int ArcDirection(int directionA, int directionB) => Math.Abs((directionA - directionB + 12) % 8 - 4);

    /// <summary>BattleUnit::getPositionVexels: the bottom centre of the unit in voxels.</summary>
    private static BattlePosition CentreVoxel(BattleUnit unit)
    {
        var position = unit.RequiredPosition;
        return new BattlePosition(position.X * BattleGeometry.VoxelsPerTile + 8 * unit.ArmorSize, position.Y * BattleGeometry.VoxelsPerTile + 8 * unit.ArmorSize,
            position.Z * BattleGeometry.VoxelsPerLevel);
    }

    /// <summary>
    /// TileEngine::meleeAttackCalculate: the success of a melee attack on a victim (0 or less misses). The attacker's accuracy for the hit, against the
    /// victim's melee dodge, less the dodge lost to an attack from behind (the armor's penalty times the angle's quarters), less a roll of 0 to 99. The
    /// item's and the victim's armor's scripts may change the result (<see cref="IBattleHooks"/>). No victim is a miss.
    /// </summary>
    internal int MeleeAttackCalculate(in BattleAttack attack, BattleUnit? victim)
    {
        if (victim is null) return 0;
        var attacker = attack.Attacker ?? throw new InvalidOperationException("A melee attack on a unit has an attacker.");
        var weapon = attack.Weapon ?? throw new InvalidOperationException("A melee attack has a weapon.");
        var attackStrength = FiringAccuracy(attacker, weapon, attack.Type);
        var defenseStrength = MeleeDodge(victim);
        var arc = ArcDirection(BattleGeometry.DirectionTo(CentreVoxel(victim), CentreVoxel(attacker)), victim.Direction);
        var defenseStrengthPenalty = Math.Clamp((int)(defenseStrength * (arc * ArmorOf(victim).MeleeDodgeBackPenalty / 4.0f)), 0, Math.Max(0, defenseStrength));

        // The item's default script: a roll of 0 to 99 against the strengths.
        var roll = Random.NextInclusive(0, 99);
        var result = -roll + attackStrength - defenseStrength + defenseStrengthPenalty;
        result = Hooks.TryMeleeAttackItem(attack, victim, attackStrength, defenseStrength, roll, arc, defenseStrengthPenalty, result);
        return Hooks.TryMeleeAttackUnit(attack, victim, attackStrength, defenseStrength, result);
    }

    /// <summary>
    /// TileEngine::meleeAttack: whether the attack hits. A terrain attack never misses. Otherwise the victim learns who attacked it (the reference's
    /// <c>extendedMeleeReactions</c> 2: a melee reaction from any side) unless it was close quarters combat, and <see cref="MeleeAttackCalculate"/> decides.
    /// </summary>
    internal bool MeleeAttack(in BattleAttack attack, BattleUnit? victim, int terrainMeleeTilePart)
    {
        if (terrainMeleeTilePart > 0) return true;
        if (attack.Type != BattleActionType.CloseQuarters && victim is not null && RequiredRules.Campaign.ExtendedMeleeReactions == 2 &&
            attack.Attacker is { } attacker && !victim.MeleeAttackedBy.Contains(attacker.Id))
            victim.MeleeAttackedBy.Add(attacker.Id);
        return MeleeAttackCalculate(attack, victim) > 0;
    }

    private int TileOrNone(BattlePosition position) => Contains(position) ? Map.TileIndex(position) : -1;

    /// <summary>
    /// TileEngine::validMeleeRange from a position and a direction: whether a unit (the <paramref name="target"/>, or any when it is null) stands on a tile the
    /// attacker, a big unit from each of its tiles, can strike in that direction: the origin voxel of each of its tiles must see a part of the unit
    /// (<c>canTargetUnit</c>). The target tile is the one above when the attacker stands on the high part of stairs, and the one below when the tile is
    /// open and a raised floor lies under it. With no target given, the chosen unit is one of an enemy faction when <paramref name="preferEnemy"/>, else the
    /// most wounded friend. <paramref name="dest"/> is the tile of the unit found.
    /// </summary>
    internal bool ValidMeleeRange(BattlePosition position, int direction, BattleUnit attacker, BattleUnit? target, out BattlePosition? dest, bool preferEnemy = true)
    {
        dest = null;
        if (direction < 0 || direction > 7) return false;
        var potential = new List<BattleUnit>();
        var size = attacker.ArmorSize - 1;
        var originOffset = ArmorOf(attacker).MeleeOriginVoxelVerticalOffset;
        var (stepX, stepY) = BattleGeometry.DirectionVector(direction);
        var sight = Sight();
        for (var x = 0; x <= size; x++)
        {
            for (var y = 0; y <= size; y++)
            {
                var originPosition = new BattlePosition(position.X + x, position.Y + y, position.Z);
                var origin = TileOrNone(originPosition);
                var targetTile = TileOrNone(new BattlePosition(originPosition.X + stepX, originPosition.Y + stepY, originPosition.Z));
                if (targetTile < 0 || origin < 0) continue;
                var above = Map.TileAbove(targetTile);
                var below = Map.TileBelow(targetTile);
                if (Map.TerrainLevel(origin) <= -16 && above >= 0 && !Map.HasNoFloor(above)) targetTile = above;
                else if (below >= 0 && Map.HasNoFloor(targetTile) && UnitOnTile(targetTile) is null && Map.TerrainLevel(below) <= -16) targetTile = below;
                if (UnitOnTile(targetTile) is not { } unit || target is not null && unit != target) continue;
                var originVoxel = new BattlePosition(originPosition.X * BattleGeometry.VoxelsPerTile + 8, originPosition.Y * BattleGeometry.VoxelsPerTile + 8,
                    originPosition.Z * BattleGeometry.VoxelsPerLevel + attacker.HeightOf(RequiredRules) + attacker.FloatHeightOf(RequiredRules) -
                    MeleeOriginHeightReduction - Map.TerrainLevel(origin) + originOffset);
                if (!sight.CanTargetUnit(originVoxel, targetTile, out _, attacker)) continue;
                dest = Map.PositionOf(targetTile);
                if (target is not null) return true;
                potential.Add(unit);
            }
        }

        BattleUnit? chosen = null;
        foreach (var candidate in potential)
        {
            // if there's actually something THERE, we'll chalk this up as a success.
            if (chosen is null) chosen = candidate;
            // but if there's a target of a different faction, we'll prioritize them,
            // or, if we're using a medikit, prioritize whichever friend is wounded the most.
            else if (preferEnemy && candidate.Faction != attacker.Faction ||
                !preferEnemy && candidate.Faction == attacker.Faction && candidate.FatalWoundCount > chosen.FatalWoundCount)
                chosen = candidate;
        }
        if (chosen is not null) dest = chosen.Position;
        return chosen is not null;
    }

    /// <summary>
    /// TileEngine::validTerrainMeleeRange: with the mod's <c>extendedTerrainMelee</c> above 0, the part of the terrain a weapon strikes when the unit has no
    /// one to hit: the weapon's damage type must hurt tiles, the direction is a compass one (a diagonal needs the <c>diagTerrainMelee</c> option, off), and
    /// the first part found in the reference's order that the unit can target, that is solid enough (a loft layer from the mod's value up) and, for a wall,
    /// that nobody can walk through, becomes the target. On the high part of stairs the target is one level up unless Alt is held. Returns the tile and
    /// the tile part (a <see cref="BattleTilePart"/> value above 0).
    /// </summary>
    internal bool ValidTerrainMeleeRange(BattleUnit actor, BattleItem? weapon, bool altPressed, out BattlePosition target, out int tilePart)
    {
        target = default;
        tilePart = 0;
        var extended = RequiredRules.Campaign.Combat.ExtendedTerrainMelee;
        if (extended <= 0) return false;
        if (weapon is not null)
        {
            // is the melee weapon able to hit terrain?
            var rule = ItemRuleOf(weapon.ItemType);
            if (rule.BattleType == MeleeBattleTypeOfItem ? rule.Damage.Conversions.Tile == 0 : rule.MeleeDamage.Conversions.Tile == 0) return false;
        }

        var direction = actor.Direction;
        // only horizontal compass directions, and no diagonal ones without diagTerrainMelee
        if (direction < 0 || direction > 7 || direction % 2 != 0) return false;

        var position = actor.RequiredPosition;
        var (stepX, stepY) = BattleGeometry.DirectionVector(direction);
        var originTile = TileOrNone(position);
        if (originTile >= 0 && Map.TerrainLevel(originTile) <= -16 && !altPressed)
        {
            // on the upper part of stairs, target one tile above.
            position = position with { Z = position.Z + 1 };
            originTile = TileOrNone(position);
        }

        var size = actor.ArmorSize - 1;
        int At(int dx, int dy, bool neighbour = false) =>
            TileOrNone(new BattlePosition(position.X + dx + (neighbour ? stepX : 0), position.Y + dy + (neighbour ? stepY : 0), position.Z));
        var originTile2 = originTile;
        var neighbouringTile = At(0, 0, true);
        var neighbouringTile2 = -1;
        switch (direction)
        {
            case 0:
                originTile2 = At(size, 0);
                neighbouringTile2 = At(size, 0, true);
                break;
            case 2:
                originTile = At(size, 0);
                originTile2 = At(size, size);
                neighbouringTile = At(size, 0, true);
                neighbouringTile2 = At(size, size, true);
                break;
            case 4:
                originTile = At(0, size);
                originTile2 = At(size, size);
                neighbouringTile = At(0, size, true);
                neighbouringTile2 = At(size, size, true);
                break;
            default:
                originTile2 = At(0, size);
                neighbouringTile2 = At(0, size, true);
                break;
        }
        if (originTile < 0 || neighbouringTile < 0) return false;

        var chosenTile = -1;
        var chosenPart = 0;
        var sight = Sight();
        bool Set(int tile, BattleTilePart part, int dir = -1)
        {
            if (tile < 0) return false;
            var eye = sight.SightOriginVoxel(actor);
            var origin = eye with { Z = eye.Z - 4 };
            // is the tile reachable (a more predictable targeting queue at diagonals)
            if (!sight.CanTargetTile(origin, tile, (int)part, out _, actor)) return false;
            if (Map.MapData(tile, part) is not { } data) return false;
            if (dir > -1 && part == BattleTilePart.ObjectPart)
            {
                var bigWall = data.BigWall;
                if (dir == 0 && bigWall != BigWallNorth && bigWall != BigWallWestAndNorth) return false;
                if (dir == 2 && bigWall != BigWallEast && bigWall != BigWallEastAndSouth) return false;
                if (dir == 4 && bigWall != BigWallSouth && bigWall != BigWallEastAndSouth) return false;
                if (dir == 6 && bigWall != BigWallWest && bigWall != BigWallWestAndNorth) return false;
            }
            // it is possible to walk through this (rubble) wall... no need to attack it
            if (part != BattleTilePart.ObjectPart && !data.IsDoor && !data.IsUfoDoor && Map.TimeUnitCost(tile, part, 0) != InvalidMoveCost) return false;
            for (var layer = extended; layer < BattleMapData.LoftLayers; layer++)
            {
                if (data.LoftId(layer) <= 0) continue;
                chosenTile = tile;
                chosenPart = (int)part;
                return true;
            }
            return false;
        }

        var found = Set(originTile, BattleTilePart.ObjectPart, direction) || Set(originTile2, BattleTilePart.ObjectPart, direction);
        if (!found)
        {
            // The walls in the reference's order for the direction (an object on the current tile came first).
            found = direction switch
            {
                0 => Set(originTile, BattleTilePart.NorthWall) || Set(originTile2, BattleTilePart.NorthWall) || Set(neighbouringTile2, BattleTilePart.WestWall) && size != 0,
                2 => Set(neighbouringTile, BattleTilePart.WestWall) || Set(neighbouringTile2, BattleTilePart.WestWall) || Set(neighbouringTile2, BattleTilePart.NorthWall) && size != 0,
                4 => Set(neighbouringTile, BattleTilePart.NorthWall) || Set(neighbouringTile2, BattleTilePart.NorthWall) || Set(neighbouringTile2, BattleTilePart.WestWall) && size != 0,
                _ => Set(originTile, BattleTilePart.WestWall) || Set(originTile2, BattleTilePart.WestWall) || Set(neighbouringTile2, BattleTilePart.NorthWall) && size != 0,
            };
        }
        // All directions: the object on the neighbouring tile, and a big unit's second part at a non-diagonal direction.
        if (!found) found = Set(neighbouringTile, BattleTilePart.ObjectPart) || Set(neighbouringTile2, BattleTilePart.ObjectPart) && size != 0;
        if (!found) return false;
        target = Map.PositionOf(chosenTile);
        tilePart = chosenPart;
        return true;
    }

    private const int InvalidMoveCost = 255;

    // ---- the attack command ----

    /// <summary>
    /// BattleItem::getAmmoForAction(BA_HIT, &amp;message, &amp;spendPerShot): the item the hit feeds on (the weapon itself for a self-use slot), or null with the
    /// reason: no ammunition loaded, or fewer rounds than one hit spends.
    /// </summary>
    private BattleItem? AmmoForHit(BattleItem weapon, out string message, out int spendPerShot) => AmmoForActionWithMessage(weapon, 3, out message, out spendPerShot);

    /// <summary>BattleItem::spendAmmoForAction(BA_HIT): a round (or the hit's share of the battery) leaves the ammunition, and a spent clip leaves the battle and the weapon.</summary>
    private void SpendAmmoForHit(BattleItem weapon) => SpendAmmoForAction(weapon, 3);

    /// <summary>The config of an action by its index (0 aimed or launch, 1 auto, 2 snap, 3 melee): <c>getActionConf</c>.</summary>
    internal RuntimeItemAction ActionConfig(BattleItem weapon, int action)
    {
        var combat = RequiredRules.Battle.Equipment.Items[weapon.ItemType].Combat;
        return action switch { 0 => combat.Aimed, 1 => combat.Auto, 2 => combat.Snap, 3 => combat.Melee, _ => throw new ArgumentOutOfRangeException(nameof(action)) };
    }

    /// <summary>BattleItem::getAmmoQuantity: an item with no clip size (-1) never runs dry (255).</summary>
    internal int AmmoQuantityOf(BattleItem item) => ItemRuleOf(item.ItemType).ClipSize == -1 ? 255 : item.AmmoQuantity;

    /// <summary>
    /// BattleItem::getAmmoForAction(action, &amp;message, &amp;spendPerShot) by the action's index: the item the action feeds on (the weapon itself for a self-use slot), or null
    /// with the reason: no ammunition loaded, or fewer rounds than one use spends.
    /// </summary>
    internal BattleItem? AmmoForActionWithMessage(BattleItem weapon, int action, out string message, out int spendPerShot)
    {
        message = "";
        var battleRule = RequiredRules.Battle.Equipment.Items[weapon.ItemType];
        spendPerShot = ActionConfig(weapon, action).SpendPerShot;
        var slot = battleRule.ActionAmmoSlots[action];
        if (slot == -1) return weapon;
        if (slot < 0 || slot >= BattleItem.AmmoSlotCount) throw new InvalidOperationException($"Item {weapon.ItemType} names ammo slot {slot}.");
        if (weapon.AmmoSlots[slot] is not { } id)
        {
            message = "STR_NO_AMMUNITION_LOADED";
            return null;
        }
        var ammo = id == weapon.Id ? weapon : _items.First(candidate => candidate.Id == id);
        if (AmmoQuantityOf(ammo) < spendPerShot)
        {
            // no rounds left (or not enough energy left in the battery)
            message = "STR_NO_ROUNDS_LEFT";
            return null;
        }
        return ammo;
    }

    /// <summary>BattleItem::spendAmmoForAction: a round (or the action's share of the battery) leaves the ammunition, and a spent clip leaves the battle and the weapon.</summary>
    internal void SpendAmmoForAction(BattleItem weapon, int action)
    {
        if (RequiredRules.Battle.Equipment.Items[weapon.ItemType].ActionAmmoSlots[action] == -1) return;
        if (AmmoForActionWithMessage(weapon, action, out _, out var spendPerShot) is not { } ammo) return;
        var rule = ItemRuleOf(ammo.ItemType);
        if (rule.ClipSize <= 0) return;
        // BattleItem::spendBullet: false when nothing is left.
        if (ammo.AmmoQuantity >= spendPerShot) ammo.AmmoQuantity -= spendPerShot;
        if (ammo.AmmoQuantity != 0 || RequiredRules.Battle.Equipment.Items[ammo.ItemType].AmmoRechargeable) return;
        RemoveItem(ammo);
        ammo.IsAmmo = false;
        if (ammo == weapon) return;
        for (var slot = 0; slot < BattleItem.AmmoSlotCount; slot++)
            if (weapon.AmmoSlots[slot] == ammo.Id) weapon.AmmoSlots[slot] = null;
    }

    /// <summary>
    /// <c>MeleeAttackBState</c>: <c>init</c> checks the weapon's ammunition, the target tile and the unit, pays the time units and strikes (the terrain part or the unit's
    /// voxel), pushing the <see cref="ExplosionState"/> of the hit in front; <c>think</c> runs once that popped: the reaction check (<see cref="CheckReactionFire"/>), the floor a burning unit sets alight, and for a hostile unit the further hits of the weapon's AI count.
    /// </summary>
    private sealed class MeleeAttackState(BattleState battle, BattleUnit unit, BattleItem weapon, BattlePosition target, int terrainMeleeTilePart,
        BattleActionCost cost) : QueuedState
    {
        private BattleUnit? _victim;
        private BattleItem? _ammo;
        private BattlePosition _voxel;
        private int _hitNumber;
        private bool _initialized;
        private bool _reaction;
        private int _terrainPart = terrainMeleeTilePart;

        /// <summary>BattleAction::result: why the attack did not start (empty when it did, or when there was nothing to say).</summary>
        public string Result { get; private set; } = "";

        /// <summary>Whether a hit was struck.</summary>
        public bool Performed { get; private set; }

        private bool SpendTimeUnits()
        {
            if (cost.Shortfall() is var shortfall && shortfall != BattleActionShortfall.None)
            {
                Result = ShortfallMessage(shortfall);
                return false;
            }
            unit.SpendCost(cost);
            return true;
        }

        public override void Init()
        {
            if (_initialized) return;
            _initialized = true;

            var part = _terrainPart;
            _terrainPart = 0; // reset!

            // A unit that is not of the side on turn reacts (reactionShoot): no message is shown for it.
            var reactionShoot = unit.Faction != battle.Side;
            _ammo = battle.AmmoForHit(weapon, out var message, out _);
            if (_ammo is null)
            {
                if (!reactionShoot) Result = message;
                battle.PopState();
                return;
            }
            // invalid target position
            if (!battle.Contains(target) || unit.IsOut || unit.IsOutThresholdExceeded)
            {
                battle.PopState();
                return;
            }
            // reaction fire: the unit that acted must still stand there, and the reactor turns to it for free
            if (reactionShoot && !battle.StartReaction(unit, target))
            {
                battle.PopState();
                return;
            }
            // The reference throws when no unit stands on the target tile (after it spent the time units) and when the target voxel lies above the map
            // (ADR 0041): here nothing is struck and nothing is spent in the first case, and the voxel is cut to the map's top in the second.
            if (part == 0)
            {
                var tile = battle.Map.TileIndex(target);
                // (The AI's chosen target is branch 6's; the unit on the tile is the target a player's hit has.)
                if (battle.UnitOnTile(tile) is not { } victim)
                {
                    battle.PopState();
                    return;
                }
                _victim = victim;
                var height = victim.FloatHeightOf(battle.RequiredRules) + victim.HeightOf(battle.RequiredRules) / 2 - battle.Map.TerrainLevel(tile);
                _voxel = new BattlePosition(target.X * BattleGeometry.VoxelsPerTile + 8, target.Y * BattleGeometry.VoxelsPerTile + 8,
                    Math.Min(target.Z * BattleGeometry.VoxelsPerLevel + height, battle.Map.Height * BattleGeometry.VoxelsPerLevel - 1));
            }

            // spend TU
            if (!SpendTimeUnits())
            {
                battle.PopState();
                return;
            }

            // terrain melee
            if (part > 0)
            {
                _voxel = new BattlePosition(target.X * BattleGeometry.VoxelsPerTile + 8, target.Y * BattleGeometry.VoxelsPerTile + 8,
                    target.Z * BattleGeometry.VoxelsPerLevel + 12);
                PerformMeleeAttack(part);
                return;
            }

            if (unit.Faction == BattleFaction.Hostile)
                _hitNumber = battle.RequiredRules.Battle.Equipment.Items[weapon.ItemType].Combat.AiMeleeHitCount - 1;
            PerformMeleeAttack(0);
        }

        /// <summary>Sets the unit in an aiming position, uses the ammunition and pushes the hit's explosion in front, a voxel off so the target knows the side it came from.</summary>
        private void PerformMeleeAttack(int part)
        {
            unit.Status = BattleUnitStatus.Aiming;
            battle.SpendAmmoForHit(weapon);
            // offset the damage voxel ever so slightly so that the target knows which side the attack came from; large units may cause it to offset too much, so the sides are clamped
            var from = unit.RequiredPosition;
            var damage = new BattlePosition(_voxel.X + Math.Clamp(from.X - target.X, -1, 1), _voxel.Y + Math.Clamp(from.Y - target.Y, -1, 1), _voxel.Z + (from.Z - target.Z));
            Performed = true;
            _reaction = true;
            battle.PushFront(new ExplosionState(battle, battle.RequiredRules, new BattleAttack(BattleActionType.Hit, unit, weapon, _ammo), damage, damage, null,
                lowerWeapon: true, range: 0, explosionCounter: 0, terrainMeleeTilePart: part));
        }

        public override void Think()
        {
            if (_reaction && battle.FallingUnits.Count == 0)
            {
                _reaction = false;
                // checkReactionFire(unit, action): the reactions go to the back of the list; the reference's think returns here and runs the rest next time, which this does at once.
                battle.CheckReactionFire(unit, new BattleReactionOrigin(BattleActionType.Hit, weapon, target));
            }

            // if the unit burns floor tiles, burn floor tiles
            if (battle.SpecialAbilityOf(unit) is 2 or 3) battle.Ignite(battle.Map.TileIndex(target), BurnFloorIgnitePower);

            if (_hitNumber > 0 &&
                // not performing a reaction attack
                unit.Faction == battle.Side &&
                // whose target is still alive or at least conscious
                _victim is not null && !_victim.IsOutThresholdExceeded &&
                // and we still have ammo to make the attack
                battle.AmmoForAction(weapon, 3) is not null &&
                // spend the TUs immediately
                SpendTimeUnits())
            {
                --_hitNumber;
                PerformMeleeAttack(0);
            }
            else
            {
                battle.ConvertInfected();
                battle.PopState();
            }
        }
    }

    /// <summary>
    /// The hit of a weapon in a unit's hand (<c>ActionMenuState::handleAction</c> for <c>BA_HIT</c>, then <c>MeleeAttackBState</c>): the weapon must be in the
    /// unit's hand or among its special weapons, offer the hit, be usable (research, depth, psi, hands: <c>canUseWeapon</c>) and be paid for; a unit stands in
    /// the facing direction (<see cref="ValidMeleeRange"/>) or a part of the terrain can be struck (<see cref="ValidTerrainMeleeRange"/>), else
    /// <c>STR_THERE_IS_NO_ONE_THERE</c>. There is no turn: the unit hits where it faces. Then the state runs with the explosions and deaths it starts and the falls
    /// they cause (<see cref="RunStates"/>, <see cref="TakeExplosionRefusal"/>, <see cref="ResolveFalls(out bool)"/>). A refusal before the strike leaves the battle as it was;
    /// one after it is what the command's trial copy finds.
    /// </summary>
    internal CampaignReason? MeleeAttackAction(BattleResearchGate research, int unitId, int itemId, bool altPressed)
    {
        ArgumentNullException.ThrowIfNull(research);
        if (SelectionUnit(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (unit.Position is not { } position) return BattleSelectionReasons.UnitUnknown;
        if (WeaponOf(unit, itemId) is not { } weapon) return BattleAttackReasons.WeaponUnavailable;
        if (!WeaponOffersHit(research, unit, weapon)) return BattleAttackReasons.ActionNotOffered;
        var usable = CanUseWeapon(research, weapon, unit, false, BattleActionType.Hit);
        if (!usable.Usable) return BattleAttackReasons.ForMessage(usable.Message);

        // check beforehand if we have enough time units
        var cost = BattleActionCost.For(RequiredRules, BattleActionType.Hit, unit, weapon);
        var shortfall = cost.Shortfall();
        if (shortfall == BattleActionShortfall.NoAction) return BattleAttackReasons.ActionNotOffered;
        if (shortfall != BattleActionShortfall.None) return BattleAttackReasons.ForMessage(ShortfallMessage(shortfall));

        BattlePosition target;
        var terrainPart = 0;
        if (ValidMeleeRange(position, unit.Direction, unit, null, out var dest)) target = dest!.Value;
        else if (!ValidTerrainMeleeRange(unit, weapon, altPressed, out target, out terrainPart)) return BattleAttackReasons.ForMessage(BattleAttackReasons.NoOneThere);

        return StrikeMelee(unit, weapon, target, terrainPart, cost);
    }

    /// <summary>
    /// The state of a melee attack run to its end (<c>statePushBack</c> of a <c>MeleeAttackBState</c> on an empty list, then the loop): the unit strikes the tile
    /// (the terrain part, when above 0), the explosions and deaths and falls that follow run, and a refusal a state met is returned (the command's trial copy finds it first). A strike that never started leaves the reason the reference gives (its <c>result</c>).
    /// </summary>
    internal CampaignReason? StrikeMelee(BattleUnit unit, BattleItem weapon, BattlePosition target, int terrainMeleeTilePart, BattleActionCost cost)
    {
        var state = new MeleeAttackState(this, unit, weapon, target, terrainMeleeTilePart, cost);
        PushNext(state);
        RunStates();
        if (TakeExplosionRefusal() is { } refusal) return refusal;
        if (!state.Performed) return state.Result.Length == 0 ? BattleAttackReasons.NotPerformed : BattleAttackReasons.ForMessage(state.Result);
        MarkChanged();
        return ResolveFalls(out _);
    }

    /// <summary>ActionMenuState's constructor: the hit is listed for a weapon the unit can shoot with (no psi weapon without the skill, no mana weapon without the feature) that has a melee cost.</summary>
    private bool WeaponOffersHit(BattleResearchGate research, BattleUnit unit, BattleItem weapon) =>
        WeaponShoots(research, unit, weapon) && RequiredRules.Battle.Equipment.Items[weapon.ItemType].ActionCosts.Of(RuntimeItemActionCosts.Melee).Cost.Time > 0;

    /// <summary>A psi weapon needs psi skill, a mana weapon the unlocked feature: without them the action menu lists the throw only.</summary>
    private bool WeaponShoots(BattleResearchGate research, BattleUnit unit, BattleItem weapon)
    {
        var battleRule = RequiredRules.Battle.Equipment.Items[weapon.ItemType];
        return !(battleRule.PsiRequired && unit.Stats.PsiSkill <= 0) && !(battleRule.ManaRequired && unit.OriginalFaction == BattleFaction.Player &&
            !(RequiredRules.Campaign.Combat.ManaEnabled && ManaUnlocked(research)));
    }
}
