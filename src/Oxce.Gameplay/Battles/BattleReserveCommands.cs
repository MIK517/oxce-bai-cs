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

/// <summary>
/// The hand buttons' right click (<c>toggleRightHandForReactions</c>, <c>toggleLeftHandForReactions</c>): without <see cref="Ctrl"/> the hand becomes the preferred one for reactions, or stops being it
/// and the hand's reactions are enabled; with it the preferred flag is dropped if it was this hand's and the hand's reactions are disabled or enabled again. Saved with the unit.
/// </summary>
public sealed record ToggleHandForReactions(int UnitId, bool RightHand, bool Ctrl) : IBattleCommand, IUnitCommand;

/// <summary>A hand's reaction flags were toggled: the unit's preferred hand now, and whether each hand's reactions are disabled.</summary>
public sealed record HandForReactionsChanged(int UnitId, string PreferredHand, bool LeftDisabled, bool RightDisabled) : ICampaignEvent;

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

    /// <summary>BattleUnit::toggleRightHandForReactions and toggleLeftHandForReactions for a live unit of the side on turn.</summary>
    internal CampaignReason? ToggleHandForReactions(int unitId, bool rightHand, bool ctrl, out BattleUnit? changed)
    {
        changed = null;
        if (SelectionUnit(unitId, out var unit) is { } refusal) return refusal;
        var hand = rightHand ? RightHand : LeftHand;
        var disabled = rightHand ? unit!.ReactionsDisabledForRightHand : unit!.ReactionsDisabledForLeftHand;
        if (unit.PreferredHandForReactions == hand) unit.PreferredHandForReactions = "";
        else if (!ctrl) unit.PreferredHandForReactions = hand;
        disabled = ctrl && !disabled;
        if (rightHand) unit.ReactionsDisabledForRightHand = disabled;
        else unit.ReactionsDisabledForLeftHand = disabled;
        MarkChanged();
        changed = unit;
        return null;
    }

    /// <summary>
    /// BattleUnit::getSpecialIconWeapon: the first special weapon whose rule has a special icon sprite, and <c>updateUiButton</c>'s filter on it: its battle type is not none, ammo, grenade,
    /// proximity grenade, flare or corpse (the first icon weapon only: a later one of a usable type does not take its place).
    /// </summary>
    internal BattleItem? SpecialIconWeapon(BattleUnit unit)
    {
        var icon = SpecialWeapons(unit).FirstOrDefault(special => RequiredRules.Battle.Equipment.Items[special.ItemType].Combat.SpecialIconSprite != -1);
        return icon is not null && ItemRuleOf(icon.ItemType).BattleType is not (0 or 2 or 4 or 5 or 10 or 11) ? icon : null;
    }

    /// <summary>
    /// <c>skillMenuCheck</c>'s first part: the soldier's rules list a skill. The skill's cost, bonus and psi tests are branch 6's, so a soldier whose skills fail them still shows the button
    /// (the reference hides it): an over-refusal, the button only answers with <c>battle.skills-unsupported</c>.
    /// </summary>
    internal bool HasSkills(BattleUnit unit) =>
        unit.SoldierId is not null && RequiredRules.Soldiers.TryGet(unit.UnitType, out var handle) && RequiredRules.Soldiers[handle].Value.Skills.Count > 0;

    /// <summary>
    /// The special weapon an empty hand's button opens: the first of the melee, psi amp, firearm, medikit, scanner and mind probe special weapons that uses an empty hand
    /// (<c>btnRightHandItemClick</c>'s loop; the reference's <c>BT_NONE</c> candidates never match). With <paramref name="shown"/> it is <c>drawHandsItems</c>' loop, which also needs <c>specialUseEmptyHandShow</c>.
    /// </summary>
    internal BattleItem? EmptyHandSpecialWeapon(BattleUnit unit, bool shown = false)
    {
        foreach (var type in new[] { BattleTypeMelee, BattleTypePsiAmp, BattleTypeFirearm, BattleTypeMedikit, BattleTypeScanner, BattleTypeMindProbe })
            if (SpecialWeapon(unit, type) is { } special && RequiredRules.Battle.Equipment.Items[special.ItemType].Combat is var combat && combat.SpecialUseEmptyHand &&
                (!shown || combat.SpecialUseEmptyHandShow)) return special;
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
