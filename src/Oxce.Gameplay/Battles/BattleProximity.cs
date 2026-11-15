using Oxce.Gameplay.Battles.Generation;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// Proximity grenades and death traps (Phase 7 branch 5 S8): <c>BattlescapeGame::checkForProximityGrenades</c> and <c>BattleItem::fuseProximityEvent</c>. The reference calls the check
/// from <c>UnitWalkBState::think</c> after each step and from <c>UnitFallBState::think</c> when a unit lands; the explosions it starts are pushed behind the running state with
/// <c>statePushNext</c>. The walk and the fall are not states of the list here, so the check collects the explosions and the caller pushes them (<see cref="PushBehindRunning"/>) when
/// its own updates are done, which is where the reference's list reaches them.
/// </summary>
internal sealed partial class BattleState
{
    private const int DeathTrapMeleePart = 4;

    private readonly List<BattleItem> _proximityItems = [];
    private readonly List<BattleItem> _proximityRemoval = [];

    /// <summary>
    /// BattleItem::fuseProximityEvent: a proximity trigger enables the fuse (an item with no fuse type starts it at 0); a lit fuse explodes the item when its trigger says so
    /// (<c>proximityExplode</c>, or by default a proximity grenade), with the item's <c>specialChance</c> (one draw whether or not it is 100, as <c>RNG::percent</c> does).
    /// </summary>
    internal bool FuseProximityEvent(BattleItem item)
    {
        var rule = RequiredRules.Battle.Equipment.Items[item.ItemType];
        var trigger = rule.FuseTrigger;
        if (trigger.ProximityTrigger)
        {
            if (rule.FuseType == NoFuseType)
            {
                item.FuseEnabled = true;
                item.FuseTimer = 0;
            }
            else if (item.FuseTimer >= 0)
            {
                item.FuseEnabled = true;
            }
        }

        return ProximityExplodes(item, item.FuseEnabled, item.FuseTimer) && RandomPercent(rule.Combat.SpecialChance);
    }

    /// <summary>The check of <see cref="FuseProximityEvent"/> before its draw: the fuse is lit and the item's trigger says it explodes (<c>proximityExplode</c>, or by default a proximity grenade).</summary>
    private bool ProximityExplodes(BattleItem item, bool fuseEnabled, int fuseTimer)
    {
        var trigger = RequiredRules.Battle.Equipment.Items[item.ItemType].FuseTrigger;
        return fuseEnabled && fuseTimer >= 0 &&
            (trigger.ProximityExplode || trigger.DefaultBehavior && ItemRuleOf(item.ItemType).BattleType == ProximityGrenadeBattleType);
    }

    /// <summary>
    /// <c>GetBeforeShoot(type, nullptr, item)</c>: the attack of a proximity event, with no attacker, the item as the weapon, and as the damage item (<c>UpdateAmmo</c>) the item itself for
    /// a grenade, a proximity grenade or a psi-amp, else the ammunition the item's action takes.
    /// </summary>
    private BattleAttack ProximityAttack(BattleActionType type, BattleItem item)
    {
        var battleType = ItemRuleOf(item.ItemType).BattleType;
        var damage = battleType is GrenadeBattleType or ProximityGrenadeBattleType or PsiAmpBattleTypeOfItem ? item
            : ActionIndex(type) is { } index ? AmmoForActionWithMessage(item, index, out _, out _) : null;
        return new BattleAttack(type, null, item, damage);
    }

    /// <summary>
    /// <c>statePushNext</c> of the explosions the check collected, in the order the check made them, while the walk or the fall (not a state of the list) is the running state: behind
    /// a running state each push goes right behind it, so the last state pushed runs first. Without a running state the states (the fall's deaths are among them) go in the same order
    /// and the first of them starts at once; a state whose <c>Init</c> pops itself hands over to the next one, as the list does.
    /// </summary>
    private void PushBehindRunning(List<QueuedState> pushes)
    {
        if (pushes.Count == 0) return;
        var behind = _states.Count == 0 ? 0 : 1;
        for (var index = 0; index < pushes.Count; index++) _states.Insert(behind, pushes[index]);
        pushes.Clear();
        if (behind == 0) _states[0].Init();
    }

    /// <summary>RuleItem::isTargetAllowed: the item's target matrix (bit 1 the same side, 2 a hostile unit is involved, 4 a civilian and an X-COM unit).</summary>
    private static bool TargetAllowed(int matrix, BattleFaction target, BattleFaction attacker)
    {
        if (target == attacker) return (matrix & 1) != 0;
        if (target == BattleFaction.Hostile || attacker == BattleFaction.Hostile) return (matrix & 2) != 0;
        if (target == BattleFaction.Neutral || attacker == BattleFaction.Neutral) return (matrix & 4) != 0;
        return false;
    }

    /// <summary>
    /// BattlescapeGame::checkForProximityGrenades: a unit on a death trap floor tile triggers the item <c>STR_DEATH_TRAP_n</c> (a proximity grenade explodes at once, a melee item hits the
    /// tile with no attacker; the item is made on the tile when none lies there), else every item on the tiles around the unit (one tile in each direction, for a big unit all the tiles it
    /// occupies) gets its proximity event: a grenade that explodes starts an explosion, any other item that is triggered is removed. The explosions are added to
    /// <paramref name="explosions"/>. Returns 2 when an explosion starts, 1 when the light of an item changed (the unit's tile then updates the items' light layer), else 0.
    /// </summary>
    private int CheckForProximityGrenades(BattleUnit unit, List<QueuedState> explosions)
    {
        var position = unit.Position!.Value;
        // death trap?
        if (DeathTrapTriggered(unit, out var trap, out var name))
        {
            var trapRule = ItemRuleOf(name);
            var equipment = RequiredRules.Battle.Equipment.Items[name];
            var trapItem = ItemsOnTile(trap).FirstOrDefault(item => item.ItemType == name) ??
                new UnitEquipment(this, RequiredRules, Random).CreateItemForTile(name, trap);
            if (trapRule.BattleType == ProximityGrenadeBattleType)
            {
                trapItem.SetFuseTimer(0, equipment);
                var voxel = new BattlePosition(trap.X * 16 + 8, trap.Y * 16 + 8, trap.Z * 24 + Map.TerrainLevel(Map.TileIndex(trap)));
                explosions.Add(new ExplosionState(this, RequiredRules, ProximityAttack(BattleActionType.TriggerProximityGrenade, trapItem), voxel, voxel, null, false, 0, 0));
            }
            else
            {
                // EXPERIMENTAL in the reference: the terrain melee part is 4 (a unit), and nobody attacks.
                var voxel = new BattlePosition(trap.X * 16 + 8, trap.Y * 16 + 8, trap.Z * 24 + 12);
                explosions.Add(new ExplosionState(this, RequiredRules, ProximityAttack(BattleActionType.Hit, trapItem), voxel, voxel, null, false, 0, 0, DeathTrapMeleePart));
            }
            return 2;
        }

        var exploded = false;
        var glow = false;
        var size = unit.ArmorSize + 1;
        for (var tx = -1; tx < size; tx++)
        {
            for (var ty = -1; ty < size; ty++)
            {
                var at = new BattlePosition(position.X + tx, position.Y + ty, position.Z);
                if (!Contains(at)) continue;
                // The items of the tile in arrival order, in a buffer the check reuses (a removal waits until the tile's items are done).
                _proximityItems.Clear();
                foreach (var item in _items)
                    if (item.Tile == at && item.Slot == GroundSlot) _proximityItems.Add(item);
                if (_proximityItems.Count == 0) continue;
                _proximityItems.Sort(static (first, second) => first.ArrivalOrder.CompareTo(second.ArrivalOrder));
                _proximityRemoval.Clear();
                foreach (var item in _proximityItems)
                {
                    var itemRule = ItemRuleOf(item.ItemType);
                    var glowing = GlowRange(item) is not null;
                    if (FuseProximityEvent(item))
                    {
                        if (itemRule.BattleType is GrenadeBattleType or ProximityGrenadeBattleType)
                        {
                            var voxel = new BattlePosition(at.X * 16 + 8, at.Y * 16 + 8, at.Z * 24 + Map.TerrainLevel(Map.TileIndex(at)));
                            explosions.Add(new ExplosionState(this, RequiredRules, ProximityAttack(BattleActionType.TriggerProximityGrenade, item), voxel, voxel, null, false, 0, 0));
                            exploded = true;
                        }
                        else
                        {
                            _proximityRemoval.Add(item);
                            if (glowing) glow = true;
                        }
                    }
                    else if (glowing != (GlowRange(item) is not null))
                    {
                        glow = true;
                    }
                }
                foreach (var item in _proximityRemoval) RemoveItem(item);
            }
        }
        return exploded ? 2 : glow ? 1 : 0;
    }

    /// <summary>
    /// The death trap of a landed or stepping unit (<c>checkForProximityGrenades</c>'s first part): the first tile the unit covers with a floor special type of 200 or more, whose item
    /// <c>STR_DEATH_TRAP_n</c> exists, may be triggered by the unit's original faction (taken as an X-COM attacker, for backward compatibility) and is a proximity grenade or a melee item.
    /// </summary>
    private bool DeathTrapTriggered(BattleUnit unit, out BattlePosition tile, out string name)
    {
        tile = default;
        name = "";
        foreach (var covered in BattleMap.Footprint(unit.Position!.Value, unit.ArmorSize))
        {
            if (!Contains(covered)) continue;
            var special = Map.FloorSpecialType(Map.TileIndex(covered));
            if (special < DeathTraps) continue;
            tile = covered;
            name = $"STR_DEATH_TRAP_{special}";
            if (!RequiredRules.Items.TryGet(name, out _)) return false;
            return TargetAllowed(RequiredRules.Battle.Equipment.Items[name].Combat.TargetMatrix, unit.OriginalFaction, BattleFaction.Player) &&
                ItemRuleOf(name).BattleType is ProximityGrenadeBattleType or MeleeBattleTypeOfItem;
        }
        return false;
    }
}
