using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>Where a shot aims (<c>ProjectileFlyBState::init</c>'s targeting): the target voxel, or none (no line of fire), and the origin voxel it was found from.</summary>
internal readonly record struct BattleShotTarget(BattlePosition? Voxel, BattlePosition OriginVoxel, bool SelfShot, bool Arcing)
{
    /// <summary>Whether a line of fire was found (an arcing shot decides in <see cref="BattleProjectile.CalculateThrow"/>).</summary>
    public bool HasLineOfFire => Arcing || Voxel is not null;
}

/// <summary>
/// The aiming queries (Phase 7 branch 5 S2): the target voxel of a shot (<c>ProjectileFlyBState::init</c>), and the viewer-scoped menu and cursor
/// of a weapon (<c>ActionMenuState</c>, the cursor of <c>Map::drawTerrain</c>). Both read the battle and change nothing, and both answer for a unit
/// of the viewer's side on its turn, aiming at a tile the viewer has discovered.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>
    /// ProjectileFlyBState::init's choice of the target voxel for a shot that is not forced: the centre of the unit on the tile (if the player's side
    /// sees it), the first of the voxels <c>canTargetUnit</c> finds, or for a tile without a unit the voxel <c>adjustTargetVoxelFromTileType</c> picks
    /// (its object, wall or floor, in that order). A forced shot (Ctrl), a launch and a panicked unit's shot aim at the middle of the tile; an arcing
    /// shot is aimed by <see cref="BattleProjectile.CalculateThrow"/>. <paramref name="origin"/> is the tile the shot starts from (the unit's, or a
    /// waypoint's), <paramref name="targetFloor"/> a launch whose last two waypoints are the same tile. The obstacles that stopped the lines are
    /// collected for a player's side (the no line of fire indicator).
    /// </summary>
    internal BattleShotTarget ChooseTargetVoxel(BattleAimAction action, BattlePosition origin, bool targetFloor = false,
        List<BattleObstacle>? obstacles = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        var sight = Sight();
        var actor = action.Actor;
        var isPlayer = Side == BattleFaction.Player;
        var tileCorner = new BattlePosition(action.Target.X * 16, action.Target.Y * 16, action.Target.Z * 24);
        if (action.Type == BattleActionType.Launch || action.ForceFire && isPlayer || !action.PanicHandled)
        {
            // Target nothing: the middle of the tile.
            var voxel = new BattlePosition(tileCorner.X + BattleSight.VoxelTileCenter.X, tileCorner.Y + BattleSight.VoxelTileCenter.Y, tileCorner.Z + BattleSight.VoxelTileCenter.Z);
            if (action.Type == BattleActionType.Launch)
            {
                // Launched missiles with two waypoints placed on the same tile target the floor; otherwise they go slightly higher than the middle.
                voxel = voxel with { Z = voxel.Z + (targetFloor ? -10 : 4) };
            }
            return new BattleShotTarget(voxel, sight.OriginVoxel(action, origin), false, false);
        }
        if (ArcingShot(action.Weapon, action.Type)) return new BattleShotTarget(null, sight.OriginVoxel(action, origin), false, true);

        // Aim at the centre of the unit, the object, the walls or the floor (in that priority); if there is no line of fire to the centre, try elsewhere.
        var targetTile = Map.TileIndex(action.Target);
        var originVoxel = sight.OriginVoxel(action, origin);
        var onTile = UnitOnTile(targetTile);
        if (onTile is not null && (actor.Faction != BattleFaction.Player || onTile.IsVisibleToPlayer(RequiredRules)))
        {
            if (origin == action.Target || onTile == actor)
            {
                // Don't shoot at yourself but shoot at the floor.
                return new BattleShotTarget(new BattlePosition(tileCorner.X + 8, tileCorner.Y + 8, tileCorner.Z), originVoxel, true, false);
            }
            // Classic accuracy: the first voxel canTargetUnit finds (off-centre shooting is off).
            var found = sight.CanTargetUnit(originVoxel, targetTile, out var scan, actor, null, isPlayer ? obstacles : null);
            action.RelativeOrigin = BattleActionOrigin.Centre; // the normal origin
            return new BattleShotTarget(found ? scan : null, originVoxel, false, false);
        }
        return new BattleShotTarget(sight.AdjustTargetVoxelFromTileType(originVoxel, targetTile, actor, isPlayer ? obstacles : null), originVoxel, false, false);
    }

    // ---- the viewer's menu and cursor ----

    private static string AimKindName(BattleAimKind kind) => kind switch
    {
        BattleAimKind.Throw => "STR_THROW",
        BattleAimKind.Launch => "STR_LAUNCH_MISSILE",
        _ => "",
    };

    private static BattleActionType AimType(BattleAimKind kind) => kind switch
    {
        BattleAimKind.Throw => BattleActionType.Throw,
        BattleAimKind.AutoShot => BattleActionType.AutoShot,
        BattleAimKind.SnapShot => BattleActionType.SnapShot,
        BattleAimKind.AimedShot => BattleActionType.AimedShot,
        BattleAimKind.Launch => BattleActionType.Launch,
        BattleAimKind.Hit => BattleActionType.Hit,
        BattleAimKind.Prime => BattleActionType.Prime,
        BattleAimKind.Unprime => BattleActionType.Unprime,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The weapon the viewer's unit holds in a hand or as a special weapon, or null.</summary>
    private BattleItem? WeaponOf(BattleUnit unit, int itemId)
    {
        var item = _items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null || item.OwnerUnitId != unit.Id) return null;
        return item.Slot is RightHand or LeftHand || unit.SpecialWeaponIds.Contains(item.Id) ? item : null;
    }

    /// <summary>
    /// ActionMenuState's constructor for the aiming actions: a throw (an item that is not fixed with a throw cost), then, unless a psi or mana weapon
    /// the unit cannot use, the shots of a firearm in the order auto, snap, then launch or aimed (a launcher's shot shares an ammunition slot with the
    /// ones it replaces), and a hit. Each line shows the accuracy and the time units it costs.
    /// </summary>
    internal BattleAimMenuView? AimMenu(BattleResearchGate research, BattleViewer viewer, int unitId, int itemId)
    {
        ArgumentNullException.ThrowIfNull(research);
        if (ViewersUnitOnTurn(viewer, unitId) is not { } unit || WeaponOf(unit, itemId) is not { } weapon) return null;
        var rule = ItemRuleOf(weapon.ItemType);
        var battleRule = RequiredRules.Battle.Equipment.Items[weapon.ItemType];
        var costs = battleRule.ActionCosts;
        var items = new List<BattleAimMenuItem>();
        void Add(BattleAimKind kind, string name)
        {
            var type = AimType(kind);
            items.Add(new BattleAimMenuItem(kind, name, FiringAccuracy(unit, weapon, type), unit.ActionCost(costs, type).Time));
        }

        var usable = CanUseWeapon(research, weapon, unit, false, BattleActionType.None);
        if (!battleRule.Fixed && costs.Of(RuntimeItemActionCosts.Throw).Cost.Time > 0) Add(BattleAimKind.Throw, AimKindName(BattleAimKind.Throw));
        // A psi weapon needs psi skill, a mana weapon the unlocked feature: without them the menu lists the throw only.
        if (WeaponShoots(research, unit, weapon))
        {
            // The priming lines come before the shots; they show no accuracy.
            if (WeaponOffersPrime(research, unit, weapon, true))
                items.Add(new BattleAimMenuItem(BattleAimKind.Prime, battleRule.PrimeActionName, 0, unit.ActionCost(costs, BattleActionType.Prime).Time));
            if (WeaponOffersPrime(research, unit, weapon, false))
                items.Add(new BattleAimMenuItem(BattleAimKind.Unprime, battleRule.UnprimeActionName, 0, unit.ActionCost(costs, BattleActionType.Unprime).Time));
            if (rule.BattleType == RuntimeBattleItem.FirearmBattleType)
            {
                if (FirearmOffers(weapon, BattleAimKind.AutoShot)) Add(BattleAimKind.AutoShot, battleRule.Combat.Auto.Name);
                if (FirearmOffers(weapon, BattleAimKind.SnapShot)) Add(BattleAimKind.SnapShot, battleRule.Combat.Snap.Name);
                if (FirearmOffers(weapon, BattleAimKind.Launch)) Add(BattleAimKind.Launch, AimKindName(BattleAimKind.Launch));
                else if (FirearmOffers(weapon, BattleAimKind.AimedShot)) Add(BattleAimKind.AimedShot, battleRule.Combat.Aimed.Name);
            }
            if (costs.Of(RuntimeItemActionCosts.Melee).Cost.Time > 0)
            {
                var name = battleRule.Combat.Melee.Name;
                // A stun rod, or a melee weapon.
                if (name.Length == 0) name = rule.BattleType == RuntimeBattleItem.MeleeBattleType && rule.Damage.ResistType == StunResistType ? "STR_STUN" : "STR_HIT_MELEE";
                Add(BattleAimKind.Hit, name);
            }
        }
        return new BattleAimMenuView(unit.Id, weapon.Id, items, usable.Usable, usable.Usable ? "" : usable.Message);
    }

    /// <summary>ItemDamageType DT_STUN.</summary>
    private const int StunResistType = 6;

    /// <summary>SavedGame::isManaUnlocked: no research is named, or it is done (always in the debug mode).</summary>
    private bool ManaUnlocked(BattleResearchGate research)
    {
        var name = RequiredRules.Campaign.Combat.ManaUnlockResearch;
        return name.Length == 0 || name == "STR_NULL" || research.DebugMode || research.IsResearched(name);
    }

    /// <summary>The first thing <see cref="BattleActionCost.Shortfall"/> found missing, as the message <c>haveTU</c> gives ("" for an action that costs nothing).</summary>
    internal static string ShortfallMessage(BattleActionShortfall shortfall) => shortfall switch
    {
        BattleActionShortfall.TimeUnits => "STR_NOT_ENOUGH_TIME_UNITS",
        BattleActionShortfall.Energy => "STR_NOT_ENOUGH_ENERGY",
        BattleActionShortfall.Morale => "STR_NOT_ENOUGH_MORALE",
        BattleActionShortfall.Health => "STR_NOT_ENOUGH_HEALTH",
        BattleActionShortfall.Mana => "STR_NOT_ENOUGH_MANA",
        BattleActionShortfall.Stun => "STR_NOT_ENOUGH_STUN",
        _ => "",
    };

    /// <summary>
    /// What aiming the weapon at a tile shows, for a shot or a throw (a launch's waypoints and a hit's range belong to their actions): the accuracy as
    /// the cursor of <c>Map::drawTerrain</c> works it out. It is the weapon's accuracy for the action, less its dropoff past the aimed range or inside the
    /// minimum range (a throw: past 200 tiles never), times the no line of sight penalty when the unit does not see the tile, and 0 out of range; the
    /// hit chance tables then turn it into a chance to hit at the distance in tiles. Whether a line of fire reaches the tile is the one the shot would
    /// find, without drawing.
    /// </summary>
    internal BattleAimView? AimView(BattleResearchGate research, BattleViewer viewer, int unitId, int itemId, BattleAimKind kind,
        BattlePosition target, bool forceFire)
    {
        ArgumentNullException.ThrowIfNull(research);
        if (kind is BattleAimKind.Launch or BattleAimKind.Hit or BattleAimKind.Prime or BattleAimKind.Unprime) return null;
        if (ViewersUnitOnTurn(viewer, unitId) is not { } unit || WeaponOf(unit, itemId) is not { } weapon || !Contains(target)) return null;
        var targetTile = Map.TileIndex(target);
        if (!ViewerKnowsTile(viewer, targetTile)) return null;
        var type = AimType(kind);
        var action = new BattleAimAction(unit, type, weapon, target) { ForceFire = forceFire };
        var cost = BattleActionCost.For(RequiredRules, type, unit, weapon);
        var shortfall = cost.Shortfall();
        var canPay = shortfall == BattleActionShortfall.None;
        // An item can be thrown whatever canUseWeapon says (ActionMenuState skips it for a throw).
        var usable = type == BattleActionType.Throw ? BattleWeaponUse.Allowed : CanUseWeapon(research, weapon, unit, false, type);
        var sight = Sight();

        var distanceSquared = BattleSight.DistanceSquared(unit, target);
        var distance = (int)Math.Ceiling(MathF.Sqrt(distanceSquared));
        var accuracy = FiringAccuracy(unit, weapon, type);
        var dropoff = CalculateLimits(weapon, type, out var upperLimit, out var lowerLimit);
        if (distance > upperLimit) accuracy -= (distance - upperLimit) * dropoff;
        else if (distance < lowerLimit) accuracy -= (lowerLimit - distance) * dropoff;
        // The penalty for the tile out of the unit's sight (the cursor does not ask for a tile outside the unit's view, the shot does not either).
        var unitOnTile = UnitOnTile(targetTile);
        var noLosPenalty = RequiredRules.Battle.Equipment.Items[weapon.ItemType].Combat.NoLosAccuracyPenalty;
        if (noLosPenalty != -1)
        {
            var hasLos = unitOnTile is not null && unitOnTile.IsVisibleToPlayer(RequiredRules) ? sight.Visible(unit, targetTile) : sight.IsTileInLos(action, targetTile, true);
            if (!hasLos) accuracy = accuracy * noLosPenalty / 100;
        }
        var outOfRange = type == BattleActionType.Throw ? IsOutOfThrowRange(weapon, distanceSquared, Depth) : IsOutOfRange(weapon, distanceSquared);
        if (accuracy <= 0 || outOfRange) accuracy = 0;
        var size = unitOnTile is not null && unitOnTile.IsVisibleToPlayer(RequiredRules) ? unitOnTile.ArmorSize : 1;
        var hitChance = HitChance(distance, accuracy, RequiredRules.Campaign.Combat.HitChances(size));

        bool lineOfFire;
        if (type == BattleActionType.Throw || ArcingShot(weapon, type))
        {
            // ProjectileFlyBState::init lifts a throw to the level above a tile with a terrain level of -24.
            var arcAction = action;
            if (type == BattleActionType.Throw && Map.TerrainLevel(targetTile) == -24 && target.Z + 1 < Map.Height)
                arcAction = new BattleAimAction(unit, type, weapon, target with { Z = target.Z + 1 }) { ForceFire = forceFire };
            var projectile = new BattleProjectile(this, RequiredRules, arcAction, unit.RequiredPosition, default);
            var arc = projectile.FindArc(null, out _, out _, out _, out var forced);
            lineOfFire = forced || arc != BattleVoxelType.OutOfBounds;
        }
        else
        {
            var choice = ChooseTargetVoxel(action, unit.RequiredPosition);
            lineOfFire = choice.HasLineOfFire;
            if (choice.Voxel is { } voxel)
            {
                // The shot's first check, on the first shot of a burst: the straight line must end on the target tile.
                action.AutoShotCounter = 1;
                lineOfFire = !new BattleProjectile(this, RequiredRules, action, unit.RequiredPosition, voxel).LineOfFireBlocked(choice.OriginVoxel);
            }
        }

        return new BattleAimView(unit.Id, weapon.Id, kind, target, usable.Usable, usable.Usable ? "" : usable.Message, cost.Time, canPay, ShortfallMessage(shortfall),
            !outOfRange, lineOfFire, accuracy, hitChance);
    }
}
