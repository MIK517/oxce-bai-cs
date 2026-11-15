using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Sessions;

namespace Oxce.Gameplay.Battles;

/// <summary>The time units a side keeps for a shot (<c>btnReserveClick</c>'s four buttons: none, snap, aimed and auto).</summary>
public enum BattleReserveKind
{
    None,
    SnapShot,
    AimedShot,
    AutoShot,
}

/// <summary>
/// A reserve button of the battlescape (<c>BattlescapeState::btnReserveClick</c> calling <c>BattlescapeGame::setTUReserved</c>): the saved time unit reserve becomes
/// <see cref="Kind"/>. <see cref="UnitId"/> names a unit of the sending slot (the selected one, or, with none selected, any of its units, even one that is out:
/// the reference needs no unit, only that the buttons are allowed, <c>allowButtons</c>). As with <see cref="SelectUnit"/>, only the side-owning slot writes the saved reserve (ADR 0039).
/// </summary>
public sealed record SetTimeUnitReserve(int UnitId, BattleReserveKind Kind) : IBattleCommand, IUnitCommand;

/// <summary>The kneel reserve button (<c>btnReserveKneelClick</c>): the saved <c>kneelReserved</c> is flipped. Written by the side-owning slot like <see cref="SetTimeUnitReserve"/>.</summary>
public sealed record ToggleKneelReserve(int UnitId) : IBattleCommand, IUnitCommand;

/// <summary>
/// The hand buttons' first step (<c>btnLeftHandItemClick</c>, <c>btnRightHandItemClick</c>: <c>setActiveLeftHand</c>, <c>setActiveRightHand</c>): the unit's active hand becomes the one clicked.
/// It picks the weapon of a unit with two of equal cost (<c>getMainHandWeapon</c>) and is saved.
/// </summary>
public sealed record SetActiveHand(int UnitId, bool RightHand) : IBattleCommand, IUnitCommand;

/// <summary>A unit's active hand was set.</summary>
public sealed record ActiveHandChanged(int UnitId, bool RightHand) : ICampaignEvent;

/// <summary>A reserve was set: the reserve now in force for the side and whether the saved battle was written (the side-owning slot's command) or not.</summary>
public sealed record TimeUnitReserveChanged(int UnitId, BattleReserveKind Kind, bool KneelReserved, bool Saved) : ICampaignEvent;

internal sealed partial class BattleState
{
    private static BattleReserveKind ReserveKindOf(int reserved) => (BattleActionType)reserved switch
    {
        BattleActionType.SnapShot => BattleReserveKind.SnapShot,
        BattleActionType.AimedShot => BattleReserveKind.AimedShot,
        BattleActionType.AutoShot => BattleReserveKind.AutoShot,
        _ => BattleReserveKind.None,
    };

    internal BattleReserveKind ReserveKind => ReserveKindOf(TimeUnitsReserved);

    /// <summary>
    /// Sets the saved reserve (<c>setTUReserved</c>) or flips the kneel one (<c>setKneelReserved</c>). The reference needs no selected unit (<c>allowButtons</c> checks the side, busy and
    /// projectiles), so the unit only names the sending slot: any unit of the side on turn, even one that is out.
    /// </summary>
    internal CampaignReason? SetReserve(int unitId, BattleReserveKind? kind, out bool saved)
    {
        saved = false;
        if (SelectionAvailable() is { } closed) return closed;
        var unit = UnitById(unitId);
        if (unit is null || unit.Faction != Side) return BattleSelectionReasons.UnitUnknown;
        saved = WritesSavedSelection(unit);
        if (!saved) return null;
        if (kind is { } chosen)
            TimeUnitsReserved = (int)(chosen switch
            {
                BattleReserveKind.SnapShot => BattleActionType.SnapShot,
                BattleReserveKind.AimedShot => BattleActionType.AimedShot,
                BattleReserveKind.AutoShot => BattleActionType.AutoShot,
                _ => BattleActionType.None,
            });
        else KneelReserved = !KneelReserved;
        MarkChanged();
        return null;
    }

    /// <summary>Sets the active hand of a live unit of the side on turn (<c>setActiveRightHand</c>, <c>setActiveLeftHand</c>).</summary>
    internal CampaignReason? SetActiveHand(int unitId, bool rightHand)
    {
        if (SelectionUnit(unitId, out var unit) is { } refusal) return refusal;
        var hand = rightHand ? "STR_RIGHT_HAND" : "STR_LEFT_HAND";
        if (unit!.ActiveHand == hand) return null;
        unit.ActiveHand = hand;
        MarkChanged();
        return null;
    }
}
