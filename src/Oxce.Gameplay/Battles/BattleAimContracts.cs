using Oxce.Gameplay.Sessions;

namespace Oxce.Gameplay.Battles;

/// <summary>The attacks a weapon offers that take aim (<c>ActionMenuState</c>'s throw, shots, launch and hit).</summary>
public enum BattleAimKind
{
    Throw,
    AutoShot,
    SnapShot,
    AimedShot,
    Launch,
    Hit,
}

/// <summary>One line of a weapon's action menu (<c>ActionMenuState::addItem</c>): the action, its name key, the accuracy it shows and the time units it costs.</summary>
public sealed record BattleAimMenuItem(BattleAimKind Kind, string Name, int Accuracy, int TimeUnits);

/// <summary>
/// The aiming actions a weapon in a viewer's unit's hand offers (<c>ActionMenuState</c>'s constructor): a throw for an item that is not fixed, the
/// shots of a firearm (a launch instead of the aimed shot for a launcher), a hit. The priming, medikit, scanner, psi amp and mind probe lines
/// belong to the owners of those actions. <see cref="Reason"/> is empty, or why the unit cannot use the weapon at all (<c>canUseWeapon</c>'s message).
/// </summary>
public sealed record BattleAimMenuView(int UnitId, int ItemId, IReadOnlyList<BattleAimMenuItem> Items, bool Usable, string Reason);

/// <summary>
/// What aiming a weapon at a tile shows (the cursor of <c>Map::drawTerrain</c> and the checks of <c>ProjectileFlyBState</c>): whether the unit may use
/// the weapon, the time units (and whether it can pay them), whether the tile is in range, whether a line of fire reaches it, the accuracy shown
/// (after the range and sight adjustments, 0 out of range) and the chance to hit it becomes with the mod's hit chance tables.
/// </summary>
public sealed record BattleAimView(int UnitId, int ItemId, BattleAimKind Kind, BattlePosition Target, bool Usable, string Reason, int TimeUnits, bool CanPay,
    string ShortfallReason, bool InRange, bool HasLineOfFire, int Accuracy, int HitChance);

/// <summary>
/// An attack with a weapon in the hand of a unit of the side on turn (<c>ActionMenuState</c>'s choice, then <c>BattlescapeGame::primaryAction</c> for a shot and
/// <c>handleNonTargetAction</c> for the hit). The hit (<see cref="BattleAimKind.Hit"/>) has no turn: the unit strikes in the direction it faces at the unit it can reach
/// there, or at the part of the terrain the mod's <c>extendedTerrainMelee</c> lets it hit, for the weapon's melee time units; <see cref="AltPressed"/> is the Alt key of the
/// terrain strike (on the upper part of stairs the target is one level up unless it is held). An aimed, snap or auto shot (Phase 7 branch 5 S7) turns the unit to
/// <see cref="Target"/> (each eighth costs time units), then fires the weapon's shots one after the other at that tile; <see cref="ForceFire"/> is Ctrl (the shot aims at the
/// middle of the tile, whatever stands there). <see cref="SprayWaypoints"/> are the tiles of the spray auto shot (a weapon with <c>sprayWaypoints</c>, Ctrl and Shift): the
/// first is the target (<see cref="Target"/> is ignored), and the weapon's shots are spaced along them. The throw and the launch are S8's.
/// </summary>
public sealed record Attack(int UnitId, int ItemId, BattleAimKind Kind, bool AltPressed = false, BattlePosition Target = default, bool ForceFire = false,
    IReadOnlyList<BattlePosition>? SprayWaypoints = null)
    : IBattleCommand, IUnitCommand;
