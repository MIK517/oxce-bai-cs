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
}

/// <summary>
/// The legal actions for a viewer's unit now (only those the rules would accept, none faked for later branches), and the other units
/// of its side the viewer may select.
/// </summary>
public sealed record BattleLegalActions(int UnitId, IReadOnlyList<BattleActionKind> Actions, IReadOnlyList<int> SelectableUnitIds);

/// <summary>
/// What the selection looks like to a viewer: the saved selection and undo unit (only when they are the viewer's side's), the units
/// of the side that <c>SavedBattleGame::selectPlayerUnit</c> would pick (live, not flagged <c>dontReselect</c>), in list order, and the
/// slot whose selection is the saved one.
/// </summary>
public sealed record BattleSelectionView(int? SelectedUnitId, int? UndoUnitId, IReadOnlyList<int> SelectableUnitIds, int WritingSlot, long Revision);

/// <summary>A discovered part of a tile: the terrain object (data set name and index in it) and the part it occupies.</summary>
public readonly record struct BattleTilePartView(BattleTilePart Part, string DataSet, int ObjectIndex);

/// <summary>An item lying on a visible tile floor.</summary>
public readonly record struct BattleTileItemView(int ItemId, string ItemType);

/// <summary>
/// A tile as a viewer sees it: only its discovered parts, the smoke and fire when the floor is discovered, the items on it and the
/// unit standing on it when the viewer sees that unit. A tile with nothing to show is not listed.
/// </summary>
public sealed record BattleTileView(
    BattlePosition Position,
    IReadOnlyList<BattleTilePartView> Parts,
    int Smoke,
    int Fire,
    IReadOnlyList<BattleTileItemView> Items,
    int? UnitId);

/// <summary>One level of the map for a viewer: the map size and the tiles it may see, in index order.</summary>
public sealed record BattleLevelView(int Level, int Width, int Length, int LevelCount, IReadOnlyList<BattleTileView> Tiles, long Revision);

/// <summary>
/// <c>BattlescapeState::updateSoldierInfo</c>: a viewer's own unit with its bars, hands, the hostile units it sees and its wounded (and burning) units. The cap on indicators (<c>VISIBLE_MAX</c>) is the screen's.
/// <see cref="VisibleUnitIds"/> lists the units the unit itself sees, then the live hostile units the viewer's side has spotted.
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
    IReadOnlyList<int> BurningUnitIds);

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
}

/// <summary>The reasons selection refuses.</summary>
internal static class BattleSelectionReasons
{
    internal static readonly CampaignReason Unavailable = new("battle.selection-unavailable",
        "Units are selected after the briefing, on X-COM's turn only.");

    internal static readonly CampaignReason UnitUnknown = new("battle.selection-unit-unknown",
        "The unit is no live unit of the side on turn.");

    internal static readonly CampaignReason NothingToUndo = new("battle.selection-nothing-to-undo",
        "The unit is not the last one skipped, or it cannot be selected again.");
}
