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

    internal static readonly CampaignReason ReactionFireUnsupported = new("battle.reaction-fire-unsupported",
        "An opposing unit near the unit could react with a weapon, and reaction fire arrives with the attack commands (Phase 7 branch 5); the unit near may be one the player has not seen.");
}
