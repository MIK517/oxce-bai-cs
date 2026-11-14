using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>The reasons the shooting command refuses (Phase 7 branch 5 S7).</summary>
internal static class BattleShotReasons
{
    internal static readonly CampaignReason TargetOutside = new("battle.attack-target-outside", "The target tile is not on the map.");

    internal static readonly CampaignReason ShotNotOffered = new("battle.attack-action-not-offered",
        "The weapon does not offer this shot to this unit (it is no firearm, it has no cost for the shot, a launcher offers no aimed shot, or the unit lacks the psi skill or the mana feature the weapon needs).");

    internal static readonly CampaignReason WaypointsInvalid = new("battle.attack-waypoints-invalid",
        "A launch needs at least one waypoint, and no more than the weapon (or its ammunition) allows.");

    internal static readonly CampaignReason SprayNotOffered = new("battle.attack-spray-not-offered",
        "A spray needs an auto shot with a weapon that has spray waypoints, and no more waypoints than the weapon allows.");
}

/// <summary>
/// Shooting (Phase 7 branch 5 S7): <c>BattlescapeGame::primaryAction</c>'s targeting part for the aimed, snap and auto shots, <c>UnitTurnBState</c> in its targeting form,
/// and <c>ProjectileFlyBState</c> for the shots (the checks of <c>init</c>, close quarters combat, the target voxel, <c>createNewProjectile</c> for a straight or an arcing
/// shot, <c>think</c>'s impact, the shotgun's pellets, the statistics and the end of the action). The projectile's flight is collapsed: a shot flies and lands within one
/// <c>think</c>, and the explosion of its impact is the next state of the list. The throw is S8's first part (<see cref="ThrowAction"/> and the throw branches here), the launch the rest of S8, the reaction shot S9's, the AI's shot branch 6's.
/// User options at their defaults: realistic accuracy and off-centre shooting are off.
/// </summary>
internal sealed partial class BattleState
{
    private static readonly (int X, int Y)[] SurroundingTiles = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    /// <summary>The direction offsets a failed close quarters check turns the shot by (down, up, left 90, left 45, right 45, right 90).</summary>
    private static readonly int[] CloseQuartersFailedResults = [0, 0, 6, 7, 1, 2];

    /// <summary>
    /// ActionMenuState's constructor for a shot: a firearm the unit can shoot with (<see cref="WeaponShoots"/>) with a cost for the shot; a launcher (a weapon with waypoints)
    /// offers the launch in place of the aimed shot, and no shot whose ammunition slot it shares with the launch.
    /// </summary>
    private bool WeaponOffersShot(BattleResearchGate research, BattleUnit unit, BattleItem weapon, BattleAimKind kind) =>
        ItemRuleOf(weapon.ItemType).BattleType == RuntimeBattleItem.FirearmBattleType && WeaponShoots(research, unit, weapon) && FirearmOffers(weapon, kind);

    /// <summary>The menu's rule for a firearm (shared by <see cref="AimMenu"/> and the shooting command): the launcher offers the launch and no aimed shot, and no shot of the slot it launches from.</summary>
    private bool FirearmOffers(BattleItem weapon, BattleAimKind kind)
    {
        var battleRule = RequiredRules.Battle.Equipment.Items[weapon.ItemType];
        var costs = battleRule.ActionCosts;
        var launcher = CurrentWaypoints(weapon) != 0;
        var slotLauncher = battleRule.ActionAmmoSlots[0];
        return kind switch
        {
            BattleAimKind.AutoShot => (!launcher || slotLauncher != battleRule.ActionAmmoSlots[1]) && costs.Of(RuntimeItemActionCosts.Auto).Cost.Time > 0,
            BattleAimKind.SnapShot => (!launcher || slotLauncher != battleRule.ActionAmmoSlots[2]) && costs.Of(RuntimeItemActionCosts.Snap).Cost.Time > 0,
            BattleAimKind.Launch => launcher,
            BattleAimKind.AimedShot => !launcher && costs.Of(RuntimeItemActionCosts.Aimed).Cost.Time > 0,
            _ => false,
        };
    }

    /// <summary>
    /// A shot with a weapon in the hand of a unit of the side on turn: <c>ActionMenuState::handleAction</c>'s checks (the weapon is held, offers the shot and is usable:
    /// research, depth, psi, hands), then <c>primaryAction</c>, which pushes the targeting turn and the projectile state with no further check. The unit turns to the
    /// target, each eighth paying its time units (the reserve is not asked), then <c>ProjectileFlyBState</c> checks the time units, the ammunition, the range and the line of
    /// fire, pays, and fires the weapon's shots. A check that fails after the unit turned stops the action with <see cref="UnitAttackStopped"/>; one that fails with nothing
    /// changed (the unit already faced the target) is a refusal. The states, the explosions and deaths they start and the falls then run
    /// (<see cref="RunStates"/>, <see cref="TakeExplosionRefusal"/>, <see cref="ResolveFalls(out bool)"/>). A reaction this slice cannot run (S9) is refused too, on the
    /// command's trial copy.
    /// </summary>
    internal CampaignReason? ShootAction(BattleResearchGate research, int unitId, int itemId, BattleAimKind kind, BattlePosition target, bool forceFire,
        IReadOnlyList<BattlePosition>? sprayWaypoints = null)
    {
        ArgumentNullException.ThrowIfNull(research);
        if (SelectionUnit(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (unit.Position is null) return BattleSelectionReasons.UnitUnknown;
        if (WeaponOf(unit, itemId) is not { } weapon) return BattleAttackReasons.WeaponUnavailable;
        if (kind is not (BattleAimKind.AutoShot or BattleAimKind.SnapShot or BattleAimKind.AimedShot) || !WeaponOffersShot(research, unit, weapon, kind))
            return BattleShotReasons.ShotNotOffered;
        var type = AimType(kind);
        var usable = CanUseWeapon(research, weapon, unit, false, type);
        if (!usable.Usable) return BattleAttackReasons.ForMessage(usable.Message);
        // primaryAction's spray targeting: the waypoints of an auto shot with a weapon that has spray waypoints (the first is the action's target).
        var spray = sprayWaypoints is { Count: > 0 };
        if (spray)
        {
            if (kind != BattleAimKind.AutoShot || sprayWaypoints!.Count > RequiredRules.Battle.Equipment.Items[weapon.ItemType].Combat.SprayWaypoints)
                return BattleShotReasons.SprayNotOffered;
            if (sprayWaypoints.Any(waypoint => !Contains(waypoint))) return BattleShotReasons.TargetOutside;
            target = sprayWaypoints[0];
        }
        if (!Contains(target)) return BattleShotReasons.TargetOutside;

        var action = new BattleAimAction(unit, type, weapon, target) { ForceFire = forceFire };
        if (spray)
        {
            action.SprayTargeting = true;
            action.Waypoints.AddRange(SprayVoxels(sprayWaypoints!, ActionConfig(weapon, ActionIndex(BattleActionType.AutoShot)!.Value).Shots));
        }
        return RunProjectileAction(unit, weapon, kind, action);
    }

    /// <summary>
    /// A throw of an item in the hand of a unit of the side on turn: <c>ActionMenuState::handleAction</c> (a throw needs neither research nor <c>canUseWeapon</c>) and
    /// <c>primaryAction</c>, which pushes the targeting turn and the projectile state with no further check, as for a shot. <c>ProjectileFlyBState</c> then checks the time
    /// units and the throwing range (<c>validThrowRange</c>), finds the arc and lets the item leave the unit; the item lands, or its fuse blows it up where it lands.
    /// </summary>
    internal CampaignReason? ThrowAction(int unitId, int itemId, BattlePosition target, bool forceFire)
    {
        if (SelectionUnit(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (unit.Position is null) return BattleSelectionReasons.UnitUnknown;
        if (WeaponOf(unit, itemId) is not { } weapon) return BattleAttackReasons.WeaponUnavailable;
        var battleRule = RequiredRules.Battle.Equipment.Items[weapon.ItemType];
        // The menu's throw line: an item that is not fixed with a time cost for the throw.
        if (battleRule.Fixed || battleRule.ActionCosts.Of(RuntimeItemActionCosts.Throw).Cost.Time <= 0) return BattleShotReasons.ShotNotOffered;
        if (!Contains(target)) return BattleShotReasons.TargetOutside;
        return RunProjectileAction(unit, weapon, BattleAimKind.Throw, new BattleAimAction(unit, BattleActionType.Throw, weapon, target) { ForceFire = forceFire });
    }

    /// <summary>
    /// A launch with a weapon in the hand of a unit of the side on turn (<c>ActionMenuState::handleAction</c>, <c>primaryAction</c>'s waypoint placing and
    /// <c>BattlescapeGame::launchAction</c>): the menu offers the launch, the unit may use the weapon (research, depth, hands), and the waypoints are tiles on the map, no more than
    /// <c>getCurrentWaypoints</c> allows (-1 is any number); the time units and the ammunition are checked before anything else (the unit does not turn for a launch it cannot
    /// pay). The unit turns to the first waypoint; the missile flies from the unit to it, and from each waypoint to the next
    /// (<c>ProjectileFlyBState</c>'s waypoint cascade) until the last one, where it explodes. The time units are paid once, by the first leg; the ammunition is spent at the end.
    /// </summary>
    internal CampaignReason? LaunchAction(BattleResearchGate research, int unitId, int itemId, IReadOnlyList<BattlePosition>? waypoints)
    {
        ArgumentNullException.ThrowIfNull(research);
        if (SelectionUnit(unitId, out var found) is { } closed) return closed;
        var unit = found!;
        if (unit.Position is null) return BattleSelectionReasons.UnitUnknown;
        if (WeaponOf(unit, itemId) is not { } weapon) return BattleAttackReasons.WeaponUnavailable;
        if (!WeaponOffersShot(research, unit, weapon, BattleAimKind.Launch)) return BattleShotReasons.ShotNotOffered;
        var usable = CanUseWeapon(research, weapon, unit, false, BattleActionType.Launch);
        if (!usable.Usable) return BattleAttackReasons.ForMessage(usable.Message);
        // The menu checks the launch beforehand (before the unit turns): the time units, then the ammunition.
        var launchCost = BattleActionCost.For(RequiredRules, BattleActionType.Launch, unit, weapon);
        if (launchCost.Shortfall() is var shortfall && shortfall != BattleActionShortfall.None)
            return shortfall == BattleActionShortfall.NoAction ? BattleAttackReasons.ActionNotOffered : BattleAttackReasons.ForMessage(ShortfallMessage(shortfall));
        if (AmmoForActionWithMessage(weapon, ActionIndex(BattleActionType.Launch)!.Value, out var ammoMessage, out _) is null)
            return BattleAttackReasons.ForMessage(ammoMessage);
        var allowed = CurrentWaypoints(weapon);
        if (waypoints is not { Count: > 0 } || allowed != -1 && waypoints.Count > allowed) return BattleShotReasons.WaypointsInvalid;
        if (waypoints.Any(waypoint => !Contains(waypoint))) return BattleShotReasons.TargetOutside;

        var action = new BattleAimAction(unit, BattleActionType.Launch, weapon, waypoints[0]);
        action.Waypoints.AddRange(waypoints);
        return RunProjectileAction(unit, weapon, BattleAimKind.Launch, action);
    }

    /// <summary>
    /// The states <c>primaryAction</c> pushes for a shot or a throw (the turn to the front, the projectile state to the back), their run, and the outcome of the command:
    /// the events, a refusal when nothing changed, or <see cref="UnitAttackStopped"/> when the action stopped after something did.
    /// </summary>
    private CampaignReason? RunProjectileAction(BattleUnit unit, BattleItem weapon, BattleAimKind kind, BattleAimAction action)
    {
        var target = action.Target;
        var cost = BattleActionCost.For(RequiredRules, action.Type, unit, weapon);
        var turn = new TargetTurnState(this, unit, target);
        var flight = new ProjectileFlyState(this, action, cost);
        // The projectile state goes to the back of the list, the targeting turn to its front (first of all turn towards the target).
        _states.Add(flight);
        PushFront(turn);
        RunStates();
        if (TakeExplosionRefusal() is { } refusal) return refusal;

        if (!flight.Fired)
        {
            var reason = flight.Result.Length != 0 ? flight.Result : turn.Result;
            if (!turn.Changed && !flight.Touched)
                return reason.Length == 0 ? BattleAttackReasons.NotPerformed : BattleAttackReasons.ForMessage(reason);
            Events.Add(new UnitAttackStopped(unit.Id, weapon.Id, kind, reason));
            MarkChanged();
            return ResolveFalls(out _);
        }
        // popState shows the action's result whatever the outcome: a failed close quarters check, or the line of fire a later shot of the burst did not find.
        if (flight.Result.Length != 0) Events.Add(new UnitAttackStopped(unit.Id, weapon.Id, kind, flight.Result));
        MarkChanged();
        return ResolveFalls(out _);
    }

    /// <summary>
    /// The spray's targets (<c>primaryAction</c>): the shots spaced evenly along the waypoints, in voxels at the middle of each tile, the last element of the list used first
    /// (the first waypoint) and each later shot taking the one before it.
    /// </summary>
    private static List<BattlePosition> SprayVoxels(IReadOnlyList<BattlePosition> tiles, int shots)
    {
        var centres = tiles.Select(static tile => new BattlePosition(tile.X * 16 + BattleSight.VoxelTileCenter.X, tile.Y * 16 + BattleSight.VoxelTileCenter.Y,
            tile.Z * 24 + BattleSight.VoxelTileCenter.Z)).ToList();
        var count = centres.Count;
        var voxels = new List<BattlePosition>();
        for (var i = shots - 1; i > 0; --i)
        {
            // Evenly space shots along the waypoints according to the number of waypoints and the number of shots.
            var index = Math.Max(0, Math.Min(count - 1, i * (count - 1) / (shots - 1)));
            var previous = centres[index];
            var next = centres[Math.Min(count - 1, index + 1)];
            var part = i * (count - 1) % (shots - 1);
            voxels.Add(new BattlePosition(previous.X + (next.X - previous.X) * part / (shots - 1), previous.Y + (next.Y - previous.Y) * part / (shots - 1),
                previous.Z + (next.Z - previous.Z) * part / (shots - 1)));
        }
        voxels.Add(centres[0]);
        return voxels;
    }

    /// <summary>
    /// <c>UnitTurnBState</c> for a targeting turn (the action is targeting, so a turret unit turns only its turret): <c>init</c> looks at the target, and <c>think</c> pays and
    /// takes each eighth with the field of view updated; the reserve is not asked, a unit that cannot pay stops with <c>STR_NOT_ENOUGH_TIME_UNITS</c> (the shot then follows).
    /// </summary>
    private sealed class TargetTurnState(BattleState battle, BattleUnit unit, BattlePosition target) : QueuedState
    {
        private bool _turret;
        private int _eighths;
        private int _spent;

        /// <summary>Whether the unit paid or turned anything.</summary>
        public bool Changed { get; private set; }

        /// <summary>BattleAction::result: the message of a turn that could not be paid.</summary>
        public string Result { get; private set; } = "";

        public override void Init()
        {
            if (unit.IsOut)
            {
                battle.PopState();
                return;
            }
            // If the unit has a turret and we are turning during targeting, then only the turret turns.
            _turret = unit.TurretType != -1;
            if (unit.RequiredPosition != target) unit.LookAt(target, _turret);
            if (unit.Status != BattleUnitStatus.Turning) battle.PopState();
        }

        public override void Think()
        {
            var sight = battle.Sight();
            while (true)
            {
                var timeUnits = _turret ? 1 : unit.TurnCost(battle.RequiredRules);
                if (!unit.SpendTimeUnits(timeUnits))
                {
                    Result = "STR_NOT_ENOUGH_TIME_UNITS";
                    unit.AbortTurn();
                    Done(BattleTurnStop.NotEnoughTimeUnits);
                    return;
                }
                Changed = true;
                ++_eighths;
                _spent += timeUnits;
                unit.Turn(_turret);
                sight.CalculateFov(unit);
                if (unit.Status == BattleUnitStatus.Standing)
                {
                    Done(BattleTurnStop.Arrived);
                    return;
                }
            }
        }

        /// <summary>The turn is over: what the unit turned and paid is reported when it did anything.</summary>
        private void Done(BattleTurnStop stop)
        {
            if (_eighths != 0) battle.Events.Add(new UnitTurned(unit.Id, _turret ? unit.DirectionTurret : unit.Direction, _eighths, _spent, stop));
            battle.PopState();
        }
    }

    /// <summary>
    /// <c>ProjectileFlyBState</c> for the aimed, snap and auto shots. <c>init</c> checks the time units, the ammunition and the range, runs close quarters combat, finds the target
    /// voxel (<see cref="ChooseTargetVoxel"/>) and creates the first projectile; <c>think</c> lets the projectile land (an <see cref="ExplosionState"/> goes in front, then the
    /// hit's statistics, the shotgun's pellets), creates the next shot of the burst, and at the end checks for reactions and turns the unit back.
    /// </summary>
    private sealed class ProjectileFlyState(BattleState battle, BattleAimAction action, BattleActionCost cost, int range = 0, BattlePosition? originTile = null) : QueuedState
    {
        private readonly BattleUnit _unit = action.Actor;

        /// <summary>The tile the projectile starts from: the unit's, or for a later leg of a launch the waypoint it left.</summary>
        private readonly BattlePosition _origin = originTile ?? action.Actor.RequiredPosition;
        private BattleItem? _ammo;
        private BattlePosition? _targetVoxel;
        private BattlePosition? _originVoxel;
        private BattleProjectile? _projectile;
        private BattleVoxelType _impact = BattleVoxelType.Empty;
        private bool _initialized;

        /// <summary>Whether a shot left the weapon.</summary>
        public bool Fired { get; private set; }

        /// <summary><c>targetFloor</c>: a launch leg whose waypoint is the tile it starts from aims at the floor.</summary>
        public bool TargetFloor { get; init; }

        /// <summary>Whether the state changed the battle before it stopped (close quarters combat's result).</summary>
        public bool Touched { get; private set; }

        /// <summary>BattleAction::result: why the shot did not start.</summary>
        public string Result => action.Result;

        private int ConfigIndex => BattleState.ActionIndex(action.Type) ?? throw new InvalidOperationException("The action is not a shot.");

        public override void Init()
        {
            if (_initialized) return;
            _initialized = true;

            var weapon = action.Weapon;
            // (A reaction shot, a unit that is not on turn, is S9's.)
            if (_unit.Faction != battle.Side) throw new InvalidOperationException("A shot of a unit that is not on turn is a reaction (Phase 7 branch 5 S9).");
            if (!battle.Contains(action.Target)) // invalid target position
            {
                battle.PopState();
                return;
            }

            // test TU only on the first launch waypoint or a normal shot
            if (range == 0 && cost.Shortfall() is var shortfall && shortfall != BattleActionShortfall.None)
            {
                action.Result = ShortfallMessage(shortfall);
                battle.PopState();
                return;
            }

            var throwing = action.Type == BattleActionType.Throw;
            if (!throwing)
            {
                _ammo = battle.AmmoForActionWithMessage(weapon, ConfigIndex, out var message, out _);
                if (_ammo is null)
                {
                    action.Result = message;
                    battle.PopState();
                    return;
                }
            }

            if (_unit.IsOut || _unit.IsOutThresholdExceeded)
            {
                // something went wrong - we can't shoot when dead or unconscious, or if we're about to fall over.
                battle.PopState();
                return;
            }

            if (throwing)
            {
                var targetTile = battle.Map.TileIndex(action.Target);
                if (!battle.Sight().ValidThrowRange(action, battle.Sight().OriginVoxel(action), targetTile, battle.Depth))
                {
                    action.Result = "STR_OUT_OF_RANGE";
                    battle.PopState();
                    return;
                }
                // An item thrown at a tile whose floor is 24 high (a full block) lands on the level above.
                if (battle.Map.TerrainLevel(targetTile) == -24 && action.Target.Z + 1 < battle.Map.Height)
                    action.Target = action.Target with { Z = action.Target.Z + 1 };
            }
            else if (battle.IsOutOfRange(weapon, BattleSight.DistanceSquared(_unit, action.Target)))
            {
                action.Result = "STR_OUT_OF_RANGE";
                battle.PopState();
                return;
            }

            CloseQuartersCombat();

            var isPlayer = battle.Side == BattleFaction.Player;
            // (A throw's arc is found from the action's target: it has no target voxel.)
            if (!throwing && (action.Type == BattleActionType.Launch || action.ForceFire && isPlayer || !action.PanicHandled))
            {
                // target nothing, targets the middle of the tile (a launch: a little above it, or the floor); the origin voxel is the unit's, or the middle of the waypoint's tile
                // (the reference sets the origin voxel of a later leg from the last flight's end, and init then overwrites it with this one).
                var choice = battle.ChooseTargetVoxel(action, _origin, TargetFloor);
                _targetVoxel = choice.Voxel;
                _originVoxel = choice.OriginVoxel;
            }
            else if (!battle.ArcingShot(weapon, action.Type))
            {
                // aim at the centre of the unit, the object, the walls or the floor (in that priority); if there is no line of fire to the centre, try elsewhere
                _targetVoxel = battle.ChooseTargetVoxel(action, _origin).Voxel;
            }

            if (CreateNewProjectile())
            {
                if (range == 0) _unit.SpendCost(cost);
                _unit.UpdateEnemyKnowledge(battle.Map.TileIndex(_unit.RequiredPosition));
            }
        }

        /// <summary>
        /// The close quarters check (the mod's <c>enableCloseQuartersCombat</c>): an enemy next to the unit that can strike it and sees it (or, with the sneak up chance, may not) makes
        /// the unit roll a melee check; a failed one spends the enemy's time units and energy and turns the shot to a random neighbouring tile, or the floor or ceiling.
        /// </summary>
        private void CloseQuartersCombat()
        {
            var combat = battle.RequiredRules.Campaign.Combat;
            var ignoresThreat = battle.ArmorOf(_unit).IgnoresMeleeThreat ?? false;
            if (combat.EnableCloseQuartersCombat == 0 || action.Type is BattleActionType.Throw or BattleActionType.Launch || _unit.TurretType != -1 || ignoresThreat) return;
            var targets = new List<BattleUnit>();
            for (var direction = 0; direction < 8; direction++)
            {
                var checkPosition = new BattlePosition(_origin.X + SurroundingTiles[direction].X, _origin.Y + SurroundingTiles[direction].Y, _origin.Z);
                if (!battle.Contains(checkPosition)) continue;
                var candidate = battle.UnitOnTile(battle.Map.TileIndex(checkPosition));
                var checkDirection = BattleGeometry.DirectionTo(checkPosition, _unit.RequiredPosition);
                if (candidate is null || _unit.Faction == candidate.Faction || !(battle.ArmorOf(candidate).CreatesMeleeThreat ?? true) ||
                    candidate.TimeUnits < combat.CloseQuartersTuCostGlobal || candidate.Energy < combat.CloseQuartersEnergyCostGlobal ||
                    !battle.ValidMeleeRange(candidate.RequiredPosition, checkDirection, candidate, _unit, out _) ||
                    // civilians don't inhibit the player, and the player doesn't inhibit civilians
                    _unit.Faction == BattleFaction.Player && candidate.Faction == BattleFaction.Neutral ||
                    _unit.Faction == BattleFaction.Neutral && candidate.Faction == BattleFaction.Player) continue;
                if (RandomChance.Percent(battle.Random, combat.CloseQuartersSneakUpGlobal))
                {
                    if (_unit.Faction == BattleFaction.Hostile)
                    {
                        // the X-COM or civilian victim does not see the attacker and cannot defend itself
                        if (!candidate.HasVisibleUnit(_unit)) continue;
                    }
                    else if (_unit.TurnsSinceSpotted[(int)BattleFaction.Hostile] > 1)
                    {
                        // the aliens (as a collective) are not aware of the attacker and cannot defend themselves
                        continue;
                    }
                }
                targets.Add(candidate);
            }

            foreach (var defender in targets)
            {
                var attack = new BattleAttack(BattleActionType.CloseQuarters, _unit, action.Weapon, action.Weapon);
                if (battle.MeleeAttack(attack, defender, 0)) continue;

                // failed the check, roll again to see the result
                if (battle.Side == BattleFaction.Player) action.Result = "STR_FAILED_CQB_CHECK";
                var roll = battle.Random.NextInclusive(0, 5);
                var newTarget = _unit.RequiredPosition;
                if (roll == 1) newTarget = newTarget with { Z = newTarget.Z + 1 };
                else if (roll > 1)
                {
                    var facing = (_unit.Direction + CloseQuartersFailedResults[roll]) % 8;
                    newTarget = new BattlePosition(newTarget.X + SurroundingTiles[facing].X, newTarget.Y + SurroundingTiles[facing].Y, newTarget.Z);
                }
                // make sure the new target is in bounds, else fire at our feet
                if (!battle.Contains(newTarget)) newTarget = _unit.RequiredPosition;

                // turn to look at the new target
                action.Target = newTarget;
                _unit.LookAt(newTarget, _unit.TurretType != -1);
                while (_unit.Status == BattleUnitStatus.Turning) _unit.Turn(_unit.TurretType != -1);

                // we're done, spend time units and energy; and don't check the remaining candidates anymore
                defender.SpendTimeUnits(combat.CloseQuartersTuCostGlobal);
                defender.SpendEnergy(combat.CloseQuartersEnergyCostGlobal);
                battle.Events.Add(new UnitCloseQuartersFailed(_unit.Id, defender.Id, newTarget, _unit.TurretType != -1 ? _unit.DirectionTurret : _unit.Direction,
                    combat.CloseQuartersTuCostGlobal, combat.CloseQuartersEnergyCostGlobal));
                Touched = true;
                break;
            }
        }

        /// <summary>ProjectileFlyBState::createNewProjectile for a shot: the trajectory (or the arc of an arcing shot) with the shooter's accuracy; it fails with no line of fire.</summary>
        private bool CreateNewProjectile()
        {
            ++action.AutoShotCounter;
            var weapon = action.Weapon;
            if (action.SprayTargeting)
            {
                // Special handling for "spray" auto attack, get target positions from the action's waypoints, starting from the back; just spraying, so the middle of the tile.
                _targetVoxel = action.Waypoints[^1];
                var targetPosition = BattleGeometry.TileOfVoxel(_targetVoxel.Value);
                // The waypoint is possibly out of range of the gun, so move the voxel to the max range of the gun if it is.
                var distanceSquared = BattleSight.DistanceSquared(_unit, targetPosition);
                if (battle.IsOutOfRange(weapon, distanceSquared))
                {
                    var actorPosition = _unit.RequiredPosition;
                    var maxRange = battle.RequiredRules.Battle.Equipment.Items[weapon.ItemType].Combat.MaxRange;
                    var distance = (int)Math.Ceiling(MathF.Sqrt(distanceSquared));
                    var tile = new BattlePosition(actorPosition.X + (targetPosition.X - actorPosition.X) * maxRange / distance,
                        actorPosition.Y + (targetPosition.Y - actorPosition.Y) * maxRange / distance, actorPosition.Z + (targetPosition.Z - actorPosition.Z) * maxRange / distance);
                    _targetVoxel = new BattlePosition(tile.X * 16 + BattleSight.VoxelTileCenter.X, tile.Y * 16 + BattleSight.VoxelTileCenter.Y, tile.Z * 24 + BattleSight.VoxelTileCenter.Z);
                    targetPosition = tile;
                }

                // Turn at the end (to a potentially modified target position).
                _unit.LookAt(targetPosition, _unit.TurretType != -1);
                while (_unit.Status == BattleUnitStatus.Turning) _unit.Turn(_unit.TurretType != -1);
                action.Waypoints.RemoveAt(action.Waypoints.Count - 1);
            }

            var projectile = new BattleProjectile(battle, battle.RequiredRules, action, _origin, _targetVoxel ?? default);
            // berserking units are half as accurate
            var accuracy = battle.FiringAccuracy(_unit, weapon, action.Type) / (action.PanicHandled ? 100.0 : 200.0);
            if (action.Type == BattleActionType.Throw) return Throw(projectile, accuracy);

            BattleVoxelType impact;
            bool lineOfFire;
            if (battle.ArcingShot(weapon, action.Type))
            {
                impact = projectile.CalculateThrow(accuracy);
                lineOfFire = impact != BattleVoxelType.Empty && impact != BattleVoxelType.OutOfBounds;
            }
            else
            {
                // (No target voxel is no line of fire: the reference's invalid voxel ends in nothing, whatever the deviation.)
                impact = _targetVoxel is null ? BattleVoxelType.Empty
                    : _originVoxel is { } originVoxel ? projectile.CalculateTrajectory(accuracy, originVoxel, false) : projectile.CalculateTrajectory(accuracy);
                // (A launch needs no hit: its waypoints are in the air.)
                lineOfFire = _targetVoxel is not null && (impact != BattleVoxelType.Empty || action.Type == BattleActionType.Launch);
            }

            if (!lineOfFire)
            {
                // no line of fire
                if (action.PanicHandled) action.Result = battle.ArcingShot(weapon, action.Type) ? "STR_NO_TRAJECTORY" : "STR_NO_LINE_OF_FIRE";
                _unit.AbortTurn();
                battle.PopState();
                return false;
            }

            _projectile = projectile;
            _impact = impact;
            // set the soldier in an aiming position, and we have a lift-off
            _unit.Status = BattleUnitStatus.Aiming;
            // (A launch spends its ammunition at the last impact, and is no shot for the statistics.)
            if (action.Type != BattleActionType.Launch)
            {
                battle.SpendAmmoForAction(weapon, ConfigIndex);
                _unit.Statistics = _unit.Statistics with { ShotsFiredCounter = _unit.Statistics.ShotsFiredCounter + 1 };
            }
            Fired = true;
            battle.Events.Add(new UnitFired(_unit.Id, weapon.Id, _ammo!.Id, KindOf(action.Type), [.. projectile.Trajectory], (int)impact));
            return true;
        }

        /// <summary>
        /// The throw part of <c>createNewProjectile</c>: the arc with the throwing accuracy; an item that finds a place to land leaves the unit's hands (a hostile unit's grenade is
        /// primed with its default fuse, a flare's light moves out of the unit, vanilla gives throwing experience for any throw); one that does not stops the action with
        /// <c>STR_UNABLE_TO_THROW_HERE</c> and no cost.
        /// </summary>
        private bool Throw(BattleProjectile projectile, double accuracy)
        {
            var weapon = action.Weapon;
            var impact = projectile.CalculateThrow(accuracy);
            if (impact == BattleVoxelType.OutOfBounds)
            {
                // unable to throw here
                action.Result = "STR_UNABLE_TO_THROW_HERE";
                cost.ClearTimeUnits();
                battle.PopState();
                return false;
            }

            var equipment = battle.RequiredRules.Battle.Equipment.Items[weapon.ItemType];
            if (_unit.Faction != BattleFaction.Player && battle.ItemRuleOf(weapon.ItemType).BattleType is GrenadeBattleType or ProximityGrenadeBattleType)
                weapon.SetFuseTimer(BattleState.FuseTimerDefault(equipment), equipment);
            BattleState.MoveToOwner(weapon, null);
            if (battle.GlowRange(weapon) is { } glow)
            {
                battle.CalculateLighting(BattleLightLayer.Units, _unit.RequiredPosition);
                battle.Sight().CalculateFov(_unit.RequiredPosition, glow, updateTiles: false);
            }
            // vanilla compatibility (throwing anything anywhere gives throwing experience)
            if (!battle.RequiredRules.Campaign.Combat.ExtendedExperienceAwardSystem) BattleState.AddExperience(_unit, TrainingKind.Throwing);

            _projectile = projectile;
            _impact = impact;
            Fired = true;
            battle.Events.Add(new UnitFired(_unit.Id, weapon.Id, weapon.Id, BattleAimKind.Throw, [.. projectile.Trajectory], (int)impact));
            return true;
        }

        /// <summary>The impact of a throw: the item lands where it ends (its fuse may make it explode at once) and the light and the sight around it are updated.</summary>
        private void ThrowImpact(BattleProjectile projectile)
        {
            var weapon = action.Weapon;
            var trajectory = projectile.Trajectory;
            _projectile = null;
            var landing = BattleGeometry.TileOfVoxel(BattleSight.PositionFromEnd(trajectory, BattleSight.ItemDropVoxelOffset));
            // The reference steps back from a tile just past the map's edge (ProjectileFlyBState.cpp:688-695, with its `>`); kept for parity, CalculateThrow reports OutOfBounds first.
            if (landing.Y > battle.Map.Length) landing = landing with { Y = landing.Y - 1 };
            if (landing.X > battle.Map.Width) landing = landing with { X = landing.X - 1 };

            var rule = battle.ItemRuleOf(weapon.ItemType);
            if (battle.FuseThrowEvent(weapon))
            {
                if (rule.BattleType is GrenadeBattleType or ProximityGrenadeBattleType)
                {
                    // it's a hot grenade to explode immediately
                    battle.PushFront(new ExplosionState(battle, battle.RequiredRules, new BattleAttack(BattleActionType.Throw, _unit, weapon, weapon),
                        BattleSight.PositionFromEnd(trajectory, BattleSight.ItemDropVoxelOffset), BattleSight.PositionFromEnd(trajectory, BattleSight.ItemDropVoxelOffset - 2),
                        null, false, 0, 0));
                }
                else
                {
                    battle.RemoveItem(weapon);
                }
            }
            else
            {
                // (The danger zone a thrown grenade sets for the AI's units is branch 6's.)
                battle.ItemDrop(landing, weapon, updateLight: true);
            }
        }

        private static BattleAimKind KindOf(BattleActionType type) => type switch
        {
            BattleActionType.Throw => BattleAimKind.Throw,
            BattleActionType.AutoShot => BattleAimKind.AutoShot,
            BattleActionType.SnapShot => BattleAimKind.SnapShot,
            BattleActionType.AimedShot => BattleAimKind.AimedShot,
            BattleActionType.Launch => BattleAimKind.Launch,
            _ => throw new InvalidOperationException("The action is not a shot."),
        };

        /// <summary>BattleItem::haveNextShotsForAction: the shots fired so far are fewer than the action's.</summary>
        private bool HaveNextShots() => action.Type != BattleActionType.Throw && action.AutoShotCounter < battle.ActionConfig(action.Weapon, ConfigIndex).Shots;

        /// <summary>The lambda <c>noMoreShotsToShoot</c> of think.</summary>
        private bool NoMoreShotsToShoot() => !HaveNextShots() || battle.AmmoForActionWithMessage(action.Weapon, ConfigIndex, out _, out _) is null;

        public override void Think()
        {
            // The reference's think runs once per animation frame: a projectile that was just created flies on the next one, and the state thinks again after an
            // impact that pushed nothing; here those frames follow each other until the state hands over to another one or pops itself.
            while (true)
            {
                if (_projectile is null)
                {
                    var hasFloor = !_unit.HaveNoFloorBelow;
                    var unitCanFly = _unit.MovementType == MovementTypeFly;
                    if (HaveNextShots() && !_unit.IsOut && battle.AmmoQuantityOf(_ammo!) != 0 && (hasFloor || unitCanFly))
                    {
                        // the next shot of the burst
                        if (!CreateNewProjectile()) return;
                    }
                    else
                    {
                        Finish();
                        return;
                    }
                }
                Impact(_projectile!);
                if (!battle.IsFront(this)) return;
            }
        }

        /// <summary>The end of the action: the reaction check (S9's reactions are refused while one could happen), the unit stops aiming, and an infected unit turns.</summary>
        private void Finish()
        {
            if (battle.FallingUnits.Count == 0 && action.PanicHandled)
            {
                // checkReactionFire(unit, action): a reaction is S9's, so a shot a reactor could answer is refused (the command's trial copy finds it first).
                if (battle.HasPossibleReactor(_unit))
                {
                    battle.ExplosionRefusal ??= BattleMovementReasons.ReactionFireUnsupported;
                    battle.PopState();
                    return;
                }
                // getSpottingUnits runs whether or not a reactor could answer: the units that see the shooter spot it.
                battle.SpotMover(battle.Sight(), _unit);
            }
            if (!_unit.IsOut) _unit.AbortTurn();
            battle.ConvertInfected();
            battle.PopState();
        }

        private static float DistanceOf(List<BattlePosition> trajectory)
        {
            var distance = 0f;
            for (var index = 1; index < trajectory.Count; index++) distance += BattleSight.TrajectoryStepSize(trajectory, index);
            return distance;
        }

        /// <summary>The projectile ended its flight: the explosion of the impact goes in front of this state, with the statistics and the shotgun's extra pellets.</summary>
        private void Impact(BattleProjectile projectile)
        {
            if (action.Type == BattleActionType.Throw)
            {
                ThrowImpact(projectile);
                return;
            }
            var rules = battle.RequiredRules;
            var weapon = action.Weapon;
            var ammo = _ammo!;
            var trajectory = projectile.Trajectory;
            var attack = new BattleAttack(action.Type, _unit, weapon, ammo);
            var distance = DistanceOf(trajectory);
            _projectile = null;

            if (action.Type == BattleActionType.Launch && action.Waypoints.Count > 1 && _impact == BattleVoxelType.Empty)
            {
                // launch the next projectile in the waypoint cascade: from the waypoint it reached to the next one, with the range flown so far (the leg pays nothing and spends nothing)
                var origin = action.Waypoints[0];
                action.Waypoints.RemoveAt(0);
                action.Target = action.Waypoints[0];
                battle.PushNext(new ProjectileFlyState(battle, action, cost, (int)(range + distance), origin) { TargetFloor = origin == action.Target });
                return;
            }

            if (battle.UnitOnTile(battle.Map.TileIndex(action.Target)) is { } aimedAt && aimedAt != _unit)
                aimedAt.Statistics = aimedAt.Statistics with { ShotAtCounter = aimedAt.Statistics.ShotAtCounter + 1 };
            if (action.Type == BattleActionType.Launch) battle.SpendAmmoForAction(weapon, ConfigIndex);

            if (_impact != BattleVoxelType.OutOfBounds)
            {
                var ammoCombat = rules.Battle.Equipment.Items[ammo.ItemType].Combat;
                var ammoRule = battle.ItemRuleOf(ammo.ItemType);
                var shotgun = ammoCombat.ShotgunPellets != 0 && ammoRule.Damage.IsDirect;
                var offset = 0;
                // explosions impact not inside the voxel but two steps back (projectiles generally move 2 voxels at a time)
                if (battle.ExplosionRadius(ammo, attack) != 0 && _impact != BattleVoxelType.Unit) offset = -2;

                battle.PushFront(new ExplosionState(battle, rules, attack, BattleSight.PositionFromEnd(trajectory, offset),
                    BattleSight.PositionFromEnd(trajectory, offset - 2), null, NoMoreShotsToShoot(), shotgun ? 0 : (int)(range + distance), 0));

                if (_impact == BattleVoxelType.Unit) ProjectileHitUnit(BattleSight.PositionFromEnd(trajectory, offset));

                // remember the unit's original experience, used for nerfing below
                _unit.RememberExperience();

                // special shotgun behaviour: trace extra projectile paths, and add bullet hits at their termination points
                if (shotgun) FirePellets(ammo, attack, projectile, offset);

                // nerf the unit's experience (gained via extra shotgun bullets)
                _unit.NerfExperience();
            }
            else if (NoMoreShotsToShoot())
            {
                _unit.Status = BattleUnitStatus.Standing;
            }
        }

        private void FirePellets(BattleItem ammo, in BattleAttack attack, BattleProjectile first, int offset)
        {
            var weapon = action.Weapon;
            var rules = battle.RequiredRules;
            var ammoCombat = rules.Battle.Equipment.Items[ammo.ItemType].Combat;
            var weaponCombat = rules.Battle.Equipment.Items[weapon.ItemType].Combat;
            var behavior = ammoCombat.ShotgunBehavior;
            var spread = ammoCombat.ShotgunSpread;
            var choke = weaponCombat.ShotgunChoke;
            var firstPelletImpact = BattleSight.PositionFromEnd(first.Trajectory, -2);
            var originalTarget = _targetVoxel ?? default;
            var target = originalTarget;
            var sight = battle.Sight();
            for (var i = 1; i < ammoCombat.ShotgunPellets; i++)
            {
                if (behavior == 1)
                {
                    // use the impact location to determine the spread (instead of the originally targeted voxel), as long as it is not the origin
                    target = firstPelletImpact != sight.OriginVoxel(action, _origin) ? firstPelletImpact : originalTarget;
                }

                var pellet = new BattleProjectile(battle, rules, action, _origin, target);
                // let it trace to the point where it hits
                var secondaryImpact = behavior == 1
                    // pellet spread based on the spread and choke values
                    ? pellet.CalculateTrajectory(Math.Max(0.0, (1.0 - spread / 100.0) * choke / 100.0))
                    // pellet spread based on the spread and the firing accuracy with the diminishing formula (the vanilla one when the spread is 100)
                    : pellet.CalculateTrajectory(Math.Max(0.0, battle.FiringAccuracy(_unit, weapon, action.Type) / 100.0 - i * 5.0 * spread / 100.0));
                if (secondaryImpact == BattleVoxelType.Empty || secondaryImpact == BattleVoxelType.OutOfBounds) continue;

                // as above: the shot is at the end of its path, and a hit goes in
                var at = BattleSight.PositionFromEnd(pellet.Trajectory, offset);
                if (secondaryImpact == BattleVoxelType.Unit) ProjectileHitUnit(at);
                var pelletDistance = DistanceOf(pellet.Trajectory);
                var power = weaponCombat.IgnoreAmmoPower
                    ? (int)(battle.PowerBonus(weapon, attack) - weaponCombat.PowerRangeReductionAt(pelletDistance))
                    : (int)(battle.PowerBonus(ammo, attack) - ammoCombat.PowerRangeReductionAt(pelletDistance));
                battle.Hit(attack, at, power, battle.ItemRuleOf(ammo.ItemType).Damage, true);
            }
        }

        /// <summary>
        /// ProjectileFlyBState::projectileHitUnit: the unit the shot hit counts it, a shot at a friend counts for both, and a hit on the aimed unit counts as landed, long-range
        /// or low accuracy by the accuracy the shot had at that distance. The victim's tile becomes known to the shooter's enemies.
        /// </summary>
        private void ProjectileHitUnit(BattlePosition position)
        {
            var victim = battle.OverlappingUnit(battle.Map.TileIndex(BattleGeometry.TileOfVoxel(position)));
            var targetVictim = battle.UnitOnTile(battle.Map.TileIndex(action.Target)); // who we were aiming at (not necessarily who we hit)
            if (victim is null || victim.IsOut) return;
            victim.Statistics = victim.Statistics with { HitCounter = victim.Statistics.HitCounter + 1 };
            if (_unit.OriginalFaction == BattleFaction.Player && victim.OriginalFaction == BattleFaction.Player)
            {
                victim.Statistics = victim.Statistics with { ShotByFriendlyCounter = victim.Statistics.ShotByFriendlyCounter + 1 };
                _unit.Statistics = _unit.Statistics with { ShotFriendlyCounter = _unit.Statistics.ShotFriendlyCounter + 1 };
            }
            if (victim == targetVictim) // hit our target
            {
                // (distance3dToUnitSq: the squared distance of the two positions, a big unit's far corner not counted.)
                var from = _unit.RequiredPosition;
                var to = victim.RequiredPosition;
                var distanceSquared = (from.X - to.X) * (from.X - to.X) + (from.Y - to.Y) * (from.Y - to.Y) + (from.Z - to.Z) * (from.Z - to.Z);
                var distance = (int)Math.Ceiling(MathF.Sqrt(distanceSquared));
                var accuracy = battle.FiringAccuracy(_unit, action.Weapon, action.Type);
                var dropoff = battle.CalculateLimits(action.Weapon, action.Type, out var upperLimit, out var lowerLimit);
                if (distance > upperLimit) accuracy -= (distance - upperLimit) * dropoff;
                else if (distance < lowerLimit) accuracy -= (lowerLimit - distance) * dropoff;
                if (accuracy < 0) accuracy = 0;
                var statistics = _unit.Statistics with { ShotsLandedCounter = _unit.Statistics.ShotsLandedCounter + 1 };
                if (distance > 30) statistics = statistics with { LongDistanceHitCounter = statistics.LongDistanceHitCounter + 1 };
                if (accuracy < distance) statistics = statistics with { LowAccuracyHitCounter = statistics.LowAccuracyHitCounter + 1 };
                _unit.Statistics = statistics;
            }
            // the victim's tile becomes known to the units that see it shot at
            victim.UpdateEnemyKnowledge(battle.Map.TileIndex(victim.RequiredPosition));
            // (The brutal units that learn of it through checkForReactivation are the AI's, branch 6.)
        }
    }
}
