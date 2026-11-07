using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Sessions;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// Turning a unit towards a tile (<c>BattlescapeGame::secondaryAction</c>, the right click: <c>UnitTurnBState</c>): the unit turns one eighth
/// at a time, paying its armor's turn cost for each, until it faces the tile, runs out of time units or reserved time units, or sees a unit
/// it had not seen this turn. A unit that already faces the tile tries the door in front of it (<c>unitOpensDoor</c>): a swinging door or a
/// UFO door in the tiles its direction looks at opens for the door's time units.
/// </summary>
public sealed record TurnUnit(int UnitId, BattlePosition Target) : IBattleCommand, IUnitCommand;

/// <summary>
/// The kneel button (<c>BattlescapeState::btnKneelClick</c>, <c>BattlescapeGame::kneel</c>): the unit kneels, or stands up when it kneels,
/// for the time units the change costs.
/// </summary>
public sealed record KneelUnit(int UnitId) : IBattleCommand, IUnitCommand;

/// <summary>How a unit moves along a path (<c>BattleActionMove</c> without the guided missile): the player's client sends <see cref="Normal"/>.</summary>
public enum BattleMoveType
{
    Normal = 0,
    Run = 1,
    Strafe = 2,
    Sneak = 3,
}

/// <summary>
/// Walking, or flying, to a tile (<c>BattlescapeGame::primaryAction</c>'s "start walking" and <c>UnitWalkBState</c>): the path is calculated for the
/// move type, and the unit follows it to its end or until something stops it (a unit newly spotted, running out of time units or energy, the reserved
/// time units, a unit in the way). <see cref="IgnoreSpottedEnemies"/> is Shift: the walk does not stop on a newly spotted unit (a run with
/// <c>extendedRunningCost</c> ignores them as well).
/// </summary>
public sealed record WalkUnit(int UnitId, BattlePosition Target, BattleMoveType Move = BattleMoveType.Normal, bool IgnoreSpottedEnemies = false)
    : IBattleCommand, IUnitCommand;

/// <summary>The unit up and down buttons (<c>BattlescapeGame::moveUpDown</c>): a flying unit, or one on a ladder or a lift, moves one level.</summary>
public sealed record MoveUnitUpDown(int UnitId, bool Up) : IBattleCommand, IUnitCommand;

/// <summary>Why a walk ended.</summary>
public enum BattleWalkStop
{
    /// <summary>The unit followed its path to the end.</summary>
    Arrived,

    /// <summary>A step or a turn showed the unit one it had not seen this turn, so the walk stopped there.</summary>
    SpottedUnits,

    /// <summary>The unit could not pay the next step's time units.</summary>
    NotEnoughTimeUnits,

    /// <summary>The unit could not pay the next step's energy.</summary>
    NotEnoughEnergy,

    /// <summary>The next step would have used the time units reserved for the unit's weapon or kneeling.</summary>
    ReservedTimeUnits,

    /// <summary>The next step is impossible (the cost the pathfinding gives is invalid).</summary>
    PathBlocked,

    /// <summary>Another unit stands where the next step goes.</summary>
    UnitInTheWay,

    /// <summary>The unit is out of action.</summary>
    Incapacitated,
}

/// <summary>One step of a walk: a tile in a direction (0-7, 8 up, 9 down), what it cost and where the unit stands afterwards (a step down stairs or off an edge differs from the neighbour tile).</summary>
public sealed record UnitStepped(int UnitId, BattlePosition From, BattlePosition To, int Direction, int TimeUnitsSpent, int EnergySpent) : ICampaignEvent;

/// <summary>A turn of one eighth during a walk: the direction now and the time units it cost (turning is free except before the first step).</summary>
public sealed record UnitWalkTurned(int UnitId, int Direction, int TimeUnitsSpent) : ICampaignEvent;

/// <summary>A door opened on the way: what happened, how many door parts opened, where light and sight were updated and the time units it cost.</summary>
public sealed record UnitWalkOpenedDoor(int UnitId, BattleDoorResult Door, int DoorsOpened, BattlePosition? DoorCentre, int TimeUnitsSpent) : ICampaignEvent;

/// <summary>A unit moved by gravity during a walk: a unit that fell, or one that moved out of the way of a falling unit.</summary>
public sealed record UnitFell(int UnitId, BattlePosition From, BattlePosition To) : ICampaignEvent;

/// <summary>The units a walker saw for the first time this turn (the walk stopped there unless it ignores them).</summary>
public sealed record UnitsSpotted(int UnitId, IReadOnlyList<int> Spotted) : ICampaignEvent;

/// <summary>A walk ended: why, how many steps it took and what it cost in all (the kneel, the doors, the turns before the first step and the steps).</summary>
public sealed record UnitWalkEnded(int UnitId, BattleWalkStop Stop, int Steps, int TimeUnitsSpent, int EnergySpent, BattlePosition Position) : ICampaignEvent;

/// <summary>Why a turn ended.</summary>
public enum BattleTurnStop
{
    /// <summary>The unit faces the tile.</summary>
    Arrived,

    /// <summary>The unit already faced the tile (or stands on it): nothing happened.</summary>
    NoTurn,

    /// <summary>A step of the turn showed the unit one it had not seen this turn, so the turn stopped there.</summary>
    SpottedUnits,

    /// <summary>The unit could not pay the next eighth.</summary>
    NotEnoughTimeUnits,

    /// <summary>Paying the next eighth would have used the time units reserved for the unit's weapon or kneeling.</summary>
    ReservedTimeUnits,
}

/// <summary>
/// A unit turned (possibly not all the way): its direction now, the eighths it turned and the time units it paid. A unit that did not turn
/// may have tried a door: <see cref="Door"/> says what happened, <see cref="DoorsOpened"/> how many door parts opened and
/// <see cref="DoorCentre"/> where light and sight were updated; the time units it paid are those of the door.
/// </summary>
public sealed record UnitTurned(int UnitId, int Direction, int Eighths, int TimeUnitsSpent, BattleTurnStop Stop,
    BattleDoorResult Door = BattleDoorResult.None, int DoorsOpened = 0, BattlePosition? DoorCentre = null) : ICampaignEvent;

/// <summary>A unit kneeled or stood up (<see cref="Kneeling"/> is whether it kneels now), paying <see cref="TimeUnitsSpent"/>.</summary>
public sealed record UnitKneeled(int UnitId, bool Kneeling, int TimeUnitsSpent) : ICampaignEvent;

/// <summary>The reasons turning and kneeling refuse.</summary>
internal static class BattleMovementReasons
{
    internal static readonly CampaignReason TurnTargetOutside = new("battle.turn-target-outside-map",
        "The tile to turn towards lies outside the map.");

    internal static readonly CampaignReason TurnTimeUnitsReserved = new("battle.turn-time-units-reserved",
        "Turning would use the time units reserved for the unit's weapon or for kneeling.");

    internal static readonly CampaignReason TurnNotEnoughTimeUnits = new("battle.turn-not-enough-time-units",
        "The unit has not enough time units to turn.");

    internal static readonly CampaignReason KneelNotAllowed = new("battle.kneel-not-allowed",
        "The unit's armor does not allow kneeling.");

    internal static readonly CampaignReason KneelFloating = new("battle.kneel-floating",
        "A floating unit cannot kneel.");

    internal static readonly CampaignReason KneelTimeUnitsReserved = new("battle.kneel-time-units-reserved",
        "Changing stance would use the time units reserved for the unit's weapon or for kneeling.");

    internal static readonly CampaignReason KneelNotEnoughTimeUnits = new("battle.kneel-not-enough-time-units",
        "The unit has not enough time units to change its stance.");

    internal static readonly CampaignReason WalkTargetOutside = new("battle.walk-target-outside-map",
        "The tile to walk to lies outside the map.");

    internal static readonly CampaignReason WalkNotAllowed = new("battle.walk-not-allowed",
        "The unit's armor does not allow moving.");

    internal static readonly CampaignReason WalkMoveNotAllowed = new("battle.walk-move-not-allowed",
        "The unit's armor does not allow this way of moving (running, strafing or sneaking).");

    internal static readonly CampaignReason WalkNoPath = new("battle.walk-no-path",
        "There is no path to the tile.");

    internal static readonly CampaignReason WalkPathBlocked = new("battle.walk-blocked",
        "The first step of the path cannot be taken.");

    internal static readonly CampaignReason WalkNotEnoughTimeUnits = new("battle.walk-not-enough-time-units",
        "The unit has not enough time units for the first step.");

    internal static readonly CampaignReason WalkNotEnoughEnergy = new("battle.walk-not-enough-energy",
        "The unit has not enough energy for the first step.");

    internal static readonly CampaignReason WalkTimeUnitsReserved = new("battle.walk-time-units-reserved",
        "The first step would use the time units reserved for the unit's weapon or for kneeling.");

    internal static readonly CampaignReason WalkUnitInTheWay = new("battle.walk-unit-in-the-way",
        "Another unit stands where the first step goes.");

    internal static readonly CampaignReason UpDownImpossible = new("battle.up-down-impossible",
        "The unit cannot move up or down from where it is.");

    internal static readonly CampaignReason ReactionFireUnsupported = new("battle.reaction-fire-unsupported",
        "An opposing unit near the unit could react with a weapon, and reaction fire arrives with the attack commands (Phase 7 branch 5); the unit near may be one the player has not seen.");
}
