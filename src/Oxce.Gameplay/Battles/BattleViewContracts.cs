using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Sessions;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// Clicking a unit of the side on turn (<c>BattlescapeGame::primaryAction</c>'s "select unit"): the unit becomes the selection.
/// With several human players only the side-owning slot writes the saved <c>selectedUnit</c> (ADR 0039); every
/// other player's selection is the client's own and the event says it was not saved.
/// </summary>
public sealed record SelectUnit(int UnitId) : IBattleCommand, IUnitCommand;

/// <summary>
/// The next or previous soldier button (<c>BattlescapeState::btnNextSoldierClick</c>, <c>btnPrevSoldierClick</c>), walking the unit
/// list from <see cref="UnitId"/>, the unit the client has selected. <see cref="InventoryOnly"/> is the inventory screen's walk
/// (<c>InventoryState::btnNextClick</c>: reselect not checked, a unit with an inventory required).
/// </summary>
public sealed record SelectNextUnit(int UnitId, bool Backwards = false, bool InventoryOnly = false) : IBattleCommand, IUnitCommand;

/// <summary>
/// "Next and stop" (<c>btnNextStopLClick</c> by list order, <c>btnNextStopMClick</c> by distance): the unit is remembered as the
/// undo unit, may not be reselected this turn, and the next unit is selected.
/// </summary>
public sealed record SkipUnit(int UnitId, bool ByDistance = false) : IBattleCommand, IUnitCommand;

/// <summary><c>btnNextStopRClick</c>: the unit that was skipped last (the undo unit, <see cref="UnitId"/>) is selected again.</summary>
public sealed record ReselectSkippedUnit(int UnitId) : IBattleCommand, IUnitCommand;

/// <summary>
/// The right click of a visible-unit button (<c>BattlescapeState::btnVisibleUnitClick</c>): of the units of the side that see
/// <see cref="TargetUnitId"/> and can be selected, the one with the most time units left (as a percent of its own, list order for a tie) becomes
/// the selection. <see cref="UnitId"/> is the unit the client has selected.
/// </summary>
public sealed record SelectSpotter(int UnitId, int TargetUnitId) : IBattleCommand, IUnitCommand;

/// <summary>
/// A unit was selected. <see cref="Saved"/> says whether the battle's saved selection was written (the side-owning slot's) or the
/// selection stays the client's.
/// </summary>
public sealed record UnitSelected(int UnitId, bool Saved) : ICampaignEvent;

/// <summary>
/// A unit was skipped. <see cref="SelectedUnitId"/> is the unit selected next, or -1 when none could be (the reference then
/// selects nothing).
/// </summary>
public sealed record UnitSkipped(int UnitId, int SelectedUnitId, bool Saved) : ICampaignEvent;

/// <summary>What a battle client can ask for now that the rules list as legal for a unit; later branches add their own.</summary>
public enum BattleActionKind
{
    SelectNextUnit,
    SelectPreviousUnit,
    SkipUnit,
    SkipUnitByDistance,
    ReselectSkippedUnit,
    SelectNextInventoryUnit,
    SelectPreviousInventoryUnit,
    OpenInventory,

    /// <summary>The right click: turn towards a tile, or open the door in front.</summary>
    Turn,

    /// <summary>The kneel button: the armor allows kneeling and the unit does not float.</summary>
    Kneel,

    /// <summary>A click on a tile: the armor allows moving.</summary>
    Walk,

    /// <summary>The unit up button: a level above can be moved to from here (<c>validateUpDown</c>).</summary>
    MoveUp,

    /// <summary>The unit down button.</summary>
    MoveDown,
}

/// <summary>
/// The legal actions for a viewer's unit now (only those the rules would accept, none faked for later branches), and the other units
/// of its side the viewer may select.
/// </summary>
public sealed record BattleLegalActions(int UnitId, IReadOnlyList<BattleActionKind> Actions, IReadOnlyList<int> SelectableUnitIds);

/// <summary>
/// What the selection looks like to a viewer: the saved selection and undo unit (only when they are the viewer's side's), the units
/// of the side that <c>SavedBattleGame::selectPlayerUnit</c> would pick (live, not flagged <c>dontReselect</c>), in list order, and the
/// slot whose selection is the saved one, and the side's saved time unit reserve and kneel reserve (<c>TUReserved</c>, <c>kneelReserved</c>).
/// </summary>
public sealed record BattleSelectionView(int? SelectedUnitId, int? UndoUnitId, IReadOnlyList<int> SelectableUnitIds, int WritingSlot, long Revision,
    BattleReserveKind TimeUnitReserve = BattleReserveKind.None, bool KneelReserved = false);

/// <summary>A discovered part of a tile: the terrain object (data set name and index in it) and the part it occupies.</summary>
public readonly record struct BattleTilePartView(BattleTilePart Part, string DataSet, int ObjectIndex);

/// <summary>An item lying on a visible tile floor.</summary>
public readonly record struct BattleTileItemView(int ItemId, string ItemType);

/// <summary>
/// A tile as a viewer sees it: only its discovered parts (with the tile's shade, 0 full light to 15 dark: <c>Tile::getShade</c>), the smoke and fire when the floor is discovered, the items on it and the
/// unit standing on it when the viewer sees that unit. A tile with nothing to show is not listed.
/// </summary>
public sealed record BattleTileView(
    BattlePosition Position,
    IReadOnlyList<BattleTilePartView> Parts,
    int Smoke,
    int Fire,
    IReadOnlyList<BattleTileItemView> Items,
    int? UnitId,
    int Shade = 0);

/// <summary>One level of the map for a viewer: the map size and the tiles it may see, in index order.</summary>
public sealed record BattleLevelView(int Level, int Width, int Length, int LevelCount, IReadOnlyList<BattleTileView> Tiles, long Revision);

/// <summary>
/// <c>BattlescapeState::updateSoldierInfo</c>: a viewer's own unit with its bars, hands, the hostile units it sees and its wounded (and burning) units. The cap on indicators (<c>VISIBLE_MAX</c>) is the screen's.
/// <see cref="FatalWounds"/> is the unit's wounds over every body part (the panel's wound indicator). <see cref="VisibleUnitIds"/> lists the units the unit itself sees, then the live hostile units the viewer's side has spotted.
/// <see cref="SpecialIconItem"/> is <c>getSpecialIconWeapon</c> (the special-weapon button's item), <see cref="EmptyHandItem"/> the special weapon an empty hand's button opens
/// (<c>btnRightHandItemClick</c>'s loop over the battle types, the weapon must use an empty hand), <see cref="SpecialWeaponIds"/> every special weapon of the unit, and the last three the
/// hands' reaction flags (<c>isRightHandPreferredForReactions</c>, <c>isLeftHandDisabledForReactions</c>). <see cref="SpecialIconItem"/> is only set for a type <c>updateUiButton</c> shows (not none, ammo, grenade, proximity grenade, flare or corpse),
/// <see cref="ShownEmptyHandItem"/> is the empty hand's item <c>drawHandsItems</c> draws (it also needs <c>specialUseEmptyHandShow</c>), and <see cref="HasSkills"/> says the soldier's rules list a skill (branch 6's menu).
/// </summary>
public sealed record BattleUnitInfo(
    int UnitId,
    int? SoldierId,
    string UnitType,
    int TimeUnits,
    int MaxTimeUnits,
    int Energy,
    int MaxEnergy,
    int Health,
    int MaxHealth,
    int Stun,
    int Morale,
    int Mana,
    int MaxMana,
    bool Kneeled,
    BattleTileItemView? RightHand,
    BattleTileItemView? LeftHand,
    IReadOnlyList<int> VisibleUnitIds,
    IReadOnlyList<int> WoundedUnitIds,
    IReadOnlyList<int> BurningUnitIds,
    int FatalWounds = 0,
    BattleTileItemView? SpecialIconItem = null,
    BattleTileItemView? EmptyHandItem = null,
    IReadOnlyList<int>? SpecialWeaponIds = null,
    string PreferredHandForReactions = "",
    bool LeftHandDisabledForReactions = false,
    bool RightHandDisabledForReactions = false,
    BattleTileItemView? ShownEmptyHandItem = null,
    bool HasSkills = false)
{
    /// <summary><c>updateUiButton</c>: the special-weapon button shows for an icon weapon of a usable type, or for a unit with the skills menu (the reference's separate skills button, which shares the key).</summary>
    public bool SpecialButtonShown => SpecialIconItem is not null || HasSkills;
}

/// <summary>How a previewed step stands against the unit's time units, energy and reserve (<c>Pathfinding::refreshPath</c>'s marker colours).</summary>
public enum BattlePathMarker
{
    /// <summary>Green: the unit can pay it and keep its reserve.</summary>
    Affordable,

    /// <summary>Yellow: the unit can pay it but would use the time units reserved for its weapon or for kneeling.</summary>
    ReservedTimeUnits,

    /// <summary>Red: the unit cannot pay time units or energy this far.</summary>
    Unaffordable,
}

/// <summary>
/// One tile of a previewed path: where the unit stands after the step, the direction it took (0-7, 8 up, 9 down), and the time units and
/// energy the unit would have left (not below 0) as the reference shows them over the tile.
/// </summary>
public readonly record struct BattlePathStepView(BattlePosition Position, int Direction, int TimeUnitsLeft, int EnergyLeft, BattlePathMarker Marker);

/// <summary>
/// A path as a player would see it previewed (<c>Pathfinding::calculate</c> then <c>refreshPath</c>): the steps, the time units and energy the whole walk
/// would cost (the kneel's standing up and the turn before the first step included in the time units), or the refusal code when there is no walk
/// (<see cref="Steps"/> empty). The query reads the battle and changes nothing.
/// </summary>
public sealed record BattlePathView(int UnitId, BattlePosition Target, BattleMoveType Move, IReadOnlyList<BattlePathStepView> Steps, int TimeUnitsCost,
    int EnergyCost, string NoPathReason);

/// <summary>The tiles a unit can reach with its time units and energy that the viewer has discovered, cheapest first (the first is its own).</summary>
public sealed record BattleReachableView(int UnitId, IReadOnlyList<BattlePosition> Tiles);

/// <summary>The queries of the battlescape screen (ADR 0038: every one takes the viewer and answers from what its side knows).</summary>
public interface IBattleViewQuery
{
    BattleSelectionView? QuerySelection(BattleViewer viewer);

    /// <summary>One level of the map, or null when there is no battle or the level is outside the map.</summary>
    BattleLevelView? QueryLevel(BattleViewer viewer, int level);

    /// <summary>The unit's soldier info, or null for a unit that is not the viewer's side's, is out, or when there is no battle.</summary>
    BattleUnitInfo? QueryUnitInfo(BattleViewer viewer, int unitId);

    /// <summary>The actions the rules accept for the viewer's unit now, or null when the unit is not the viewer's side's live unit.</summary>
    BattleLegalActions? QueryLegalActions(BattleViewer viewer, int unitId);

    /// <summary>
    /// The path the unit would walk to a tile (<c>Pathfinding::calculate</c> and <c>refreshPath</c>), or null when the unit is not the viewer's
    /// side's live unit on turn, the tile is outside the map, or the viewer has not discovered the tile (the preview must not tell what the
    /// viewer cannot see). A path that does not exist is a view with no steps and the reason.
    /// </summary>
    BattlePathView? QueryPath(BattleViewer viewer, int unitId, BattlePosition target, BattleMoveType move = BattleMoveType.Normal);

    /// <summary>The discovered tiles the unit can reach (<c>Pathfinding::findReachable</c>), or null for a unit that is not the viewer's side's live unit on turn.</summary>
    BattleReachableView? QueryReachable(BattleViewer viewer, int unitId);

    /// <summary>
    /// The aiming actions the weapon in a hand of the viewer's unit offers (<c>ActionMenuState</c>), with the accuracy and time units of each, or null
    /// when the unit is not the viewer's side's live unit on turn or does not hold the item.
    /// </summary>
    BattleAimMenuView? QueryAimMenu(BattleViewer viewer, int unitId, int itemId);

    /// <summary>
    /// What aiming a shot or a throw at a tile shows (the cursor and the checks of <c>ProjectileFlyBState</c>), or null when the unit is not the viewer's
    /// side's live unit on turn, does not hold the item, the action is a launch or a hit, or the viewer has not discovered the tile.
    /// <paramref name="forceFire"/> is Ctrl: the line of fire is not checked.
    /// </summary>
    BattleAimView? QueryAim(BattleViewer viewer, int unitId, int itemId, BattleAimKind kind, BattlePosition target, bool forceFire = false);
}

/// <summary>The reasons selection refuses.</summary>
internal static class BattleSelectionReasons
{
    internal static readonly CampaignReason Unavailable = new("battle.selection-unavailable",
        "Units are selected after the briefing, on X-COM's turn only.");

    internal static readonly CampaignReason UnitUnknown = new("battle.selection-unit-unknown",
        "The unit is no live unit of the side on turn.");

    internal static readonly CampaignReason NoSpotter = new("battle.selection-no-spotter",
        "No selectable unit of the side sees that unit.");

    internal static readonly CampaignReason NothingToUndo = new("battle.selection-nothing-to-undo",
        "The unit is not the last one skipped, or it cannot be selected again.");
}
