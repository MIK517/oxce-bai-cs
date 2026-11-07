using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// Selection and the battlescape queries (Phase 7 branch 3 S5): <c>SavedBattleGame::selectPlayerUnit</c>,
/// <c>selectNextPlayerUnitByDistance</c>, <c>BattleUnit::isSelectable</c>, <c>BattlescapeState::btnNextSoldierClick</c>,
/// <c>btnNextStopLClick</c>, <c>btnNextStopMClick</c>, <c>btnNextStopRClick</c> and <c>updateSoldierInfo</c>,
/// <c>BattlescapeGame::primaryAction</c>'s unit selection, and what <c>Map::drawTerrain</c> draws of a tile.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>The selection is open after the briefing, on the player side's turn (<c>BattlescapeState::allowButtons</c>; nothing is ever busy here).</summary>
    internal CampaignReason? SelectionAvailable(bool inventoryWalk = false) =>
        Phase == BattlePhase.Briefing || Side != BattleFaction.Player || Phase == BattlePhase.Inventory && !inventoryWalk
            ? BattleSelectionReasons.Unavailable : null;

    internal BattleUnit? UnitById(int id) => _units.Find(unit => unit.Id == id);

    /// <summary>BattleUnit::hasInventory: the unit's armor has an inventory.</summary>
    private static bool HasInventory(RuntimeRuleCatalog rules, BattleUnit unit) =>
        rules.Battle.Equipment.Armors.TryGetValue(unit.ArmorId, out var armor) && armor.HasInventory;

    /// <summary>BattleUnit::isSelectable.</summary>
    private static bool IsSelectable(RuntimeRuleCatalog rules, BattleUnit unit, BattleFaction side, bool checkReselect, bool checkInventory) =>
        unit.Faction == side && !unit.IsOut && (!checkReselect || !unit.DontReselect) && (!checkInventory || HasInventory(rules, unit));

    /// <summary>
    /// The slot whose selection is the saved one (ADR 0039): the lowest human slot among the units that fight for the side on turn
    /// (X-COM's in single player), or -1 when no human controls one.
    /// </summary>
    internal int WritingSlot()
    {
        var slot = -1;
        foreach (var unit in _units)
            if (unit.OriginalFaction == Side && unit.Controller.Kind == BattleControllerKind.Player && (slot < 0 || unit.Controller.PlayerSlot < slot))
                slot = unit.Controller.PlayerSlot;
        return slot;
    }

    private bool WritesSavedSelection(BattleUnit unit) =>
        unit.Controller.Kind == BattleControllerKind.Player && unit.Controller.PlayerSlot == WritingSlot();

    /// <summary>
    /// SavedBattleGame::selectPlayerUnit: from <paramref name="from"/> (the selected unit) the next unit in the list direction that
    /// <c>isSelectable</c>, wrapping around. Coming back to the start returns the unit itself, or nothing when it may not be reselected.
    /// Nothing is changed but the <c>dontReselect</c> flag of <paramref name="from"/> when <paramref name="setReselect"/>.
    /// </summary>
    private BattleUnit? SelectPlayerUnit(RuntimeRuleCatalog rules, BattleUnit? from, int direction, bool checkReselect, bool setReselect, bool checkInventory)
    {
        var selected = from;
        if (selected is not null && setReselect) selected.DontReselect = true;
        if (_units.Count == 0) return null;
        var begin = direction > 0 ? 0 : _units.Count - 1;
        var end = direction > 0 ? _units.Count - 1 : 0;
        var index = selected is null ? _units.Count : _units.IndexOf(selected);
        if (index < 0) index = _units.Count;
        do
        {
            // No unit selected: start at the beginning, which the loop condition then tests (the reference's `continue`).
            if (index == _units.Count)
            {
                index = begin;
                continue;
            }
            index = index != end ? index + direction : begin;
            // Back where it started: no other unit; the selection stays unless it may not be reselected.
            if (ReferenceEquals(_units[index], selected))
            {
                if (checkReselect && selected!.DontReselect) selected = null;
                return selected;
            }
            if (selected is null && index == begin) return null;
        }
        while (!IsSelectable(rules, _units[index], Side, checkReselect, checkInventory));
        return _units[index];
    }

    /// <summary>
    /// SavedBattleGame::selectNextPlayerUnitByDistance: the selectable unit nearest to <paramref name="from"/> (squared distance in
    /// three dimensions; ties go to the list order, which <c>std::sort</c> leaves open). With <paramref name="setReselect"/> the
    /// selection is first dropped, so none is left when no unit is a candidate.
    /// </summary>
    private BattleUnit? SelectPlayerUnitByDistance(RuntimeRuleCatalog rules, BattleUnit? from, bool checkReselect, bool setReselect, bool checkInventory)
    {
        var selected = from;
        if (selected is not null && setReselect)
        {
            selected.DontReselect = true;
            selected = null;
        }
        BattleUnit? best = null;
        var bestDistance = 0;
        foreach (var unit in _units)
        {
            if (ReferenceEquals(unit, selected) || !IsSelectable(rules, unit, Side, checkReselect, checkInventory)) continue;
            var distance = from is null ? 0 : DistanceSquared(from, unit);
            if (best is null || distance < bestDistance)
            {
                best = unit;
                bestDistance = distance;
            }
        }
        return best ?? selected;
    }

    private static int DistanceSquared(BattleUnit left, BattleUnit right)
    {
        var a = left.Position ?? left.SavedPosition;
        var b = right.Position ?? right.SavedPosition;
        return (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);
    }

    /// <summary>The unit a selection command starts from: a live unit of the side on turn.</summary>
    internal CampaignReason? SelectionUnit(int unitId, out BattleUnit? unit, bool inventoryWalk = false)
    {
        unit = null;
        if (SelectionAvailable(inventoryWalk) is { } closed) return closed;
        var found = UnitById(unitId);
        if (found is null || found.Faction != Side || found.IsOut) return BattleSelectionReasons.UnitUnknown;
        unit = found;
        return null;
    }

    /// <summary>The primary action's selection: a unit of the side on turn that is on the map becomes the selection.</summary>
    internal CampaignReason? Select(int unitId, out bool saved)
    {
        saved = false;
        if (SelectionUnit(unitId, out var unit) is { } refusal) return refusal;
        if (unit!.Position is null) return BattleSelectionReasons.UnitUnknown;
        saved = WritesSavedSelection(unit);
        if (saved) SelectedUnitId = unit.Id;
        Revision++;
        return null;
    }

    /// <summary>Next or previous soldier, the saved selection written when this unit's slot is the writing one.</summary>
    internal CampaignReason? SelectAdjacent(RuntimeRuleCatalog rules, int unitId, bool backwards, bool inventoryOnly, out BattleUnit? result, out bool saved)
    {
        result = null;
        saved = false;
        if (SelectionUnit(unitId, out var from, inventoryOnly) is { } refusal) return refusal;
        saved = WritesSavedSelection(from!);
        result = SelectPlayerUnit(rules, from, backwards ? -1 : 1, checkReselect: !inventoryOnly, setReselect: false, checkInventory: inventoryOnly);
        if (saved) SelectedUnitId = result?.Id;
        Revision++;
        return null;
    }

    /// <summary>"Next and stop": the unit becomes the undo unit and may not be reselected; the next unit by list order or distance is selected.</summary>
    internal CampaignReason? Skip(RuntimeRuleCatalog rules, int unitId, bool byDistance, out BattleUnit? result, out bool saved)
    {
        result = null;
        saved = false;
        if (SelectionUnit(unitId, out var from) is { } refusal) return refusal;
        saved = WritesSavedSelection(from!);
        if (saved) UndoUnitId = from!.Id;
        result = byDistance
            ? SelectPlayerUnitByDistance(rules, from, checkReselect: true, setReselect: true, checkInventory: false)
            : SelectPlayerUnit(rules, from, 1, checkReselect: true, setReselect: true, checkInventory: false);
        if (saved) SelectedUnitId = result?.Id;
        Revision++;
        return null;
    }

    /// <summary>
    /// <c>btnVisibleUnitClick</c>'s right click: the selectable unit of the side on turn that sees the target with the most time units left (a percent
    /// of its base time units) is selected; none that sees it is a refusal (the reference does nothing).
    /// </summary>
    internal CampaignReason? SelectSpotter(RuntimeRuleCatalog rules, int unitId, int targetId, out BattleUnit? result, out bool saved)
    {
        result = null;
        saved = false;
        if (SelectionUnit(unitId, out _) is { } refusal) return refusal;
        if (UnitById(targetId) is not { } target || !Sees(Side, target)) return BattleSelectionReasons.UnitUnknown;
        var spotter = _units
            .Where(unit => IsSelectable(rules, unit, Side, false, false) && unit.HasVisibleUnit(target))
            .OrderByDescending(static unit => unit.Stats.TimeUnits > 0 ? unit.TimeUnits * 100 / unit.Stats.TimeUnits : 0)
            .FirstOrDefault();
        if (spotter is null) return BattleSelectionReasons.NoSpotter;
        if (Select(spotter.Id, out saved) is { } failed) return failed;
        result = spotter;
        return null;
    }

    /// <summary>The undo unit of the saved selection, when it can be selected again (<c>isSelectable(side, false, false)</c>).</summary>
    internal BattleUnit? UndoCandidate(RuntimeRuleCatalog rules) =>
        UndoUnitId is { } id && UnitById(id) is { } unit && IsSelectable(rules, unit, Side, false, false) ? unit : null;

    internal CampaignReason? ReselectSkipped(RuntimeRuleCatalog rules, int unitId)
    {
        if (SelectionAvailable() is { } closed) return closed;
        if (UndoCandidate(rules) is not { } candidate || candidate.Id != unitId) return BattleSelectionReasons.NothingToUndo;
        candidate.DontReselect = false;
        SelectedUnitId = candidate.Id;
        UndoUnitId = null;
        Revision++;
        return null;
    }

    // ---- Queries -------------------------------------------------------------------------------------------------------------

    private bool IsViewersSide(BattleViewer viewer) => Enum.IsDefined(viewer.Faction) ? viewer.Faction == Side : throw new ArgumentOutOfRangeException(nameof(viewer));

    internal BattleSelectionView SelectionView(RuntimeRuleCatalog rules, BattleViewer viewer)
    {
        var own = IsViewersSide(viewer) && Phase != BattlePhase.Briefing;
        int? selected = own && SelectedUnitId is { } id && UnitById(id) is { Faction: var faction } && faction == viewer.Faction ? id : null;
        int? undo = own && UndoUnitId is { } undoId && UnitById(undoId) is { Faction: var undoFaction } && undoFaction == viewer.Faction ? undoId : null;
        var selectable = own
            ? _units.Where(unit => IsSelectable(rules, unit, Side, true, false)).Select(static unit => unit.Id).ToArray()
            : [];
        return new BattleSelectionView(selected, undo, selectable, own ? WritingSlot() : -1, Revision);
    }

    internal BattleLegalActions? LegalActions(RuntimeRuleCatalog rules, BattleViewer viewer, int unitId)
    {
        if (!Enum.IsDefined(viewer.Faction)) throw new ArgumentOutOfRangeException(nameof(viewer));
        var unit = UnitById(unitId);
        if (unit is null || unit.Faction != viewer.Faction || unit.IsOut) return null;
        var actions = new List<BattleActionKind>();
        var others = new List<int>();
        if (Phase == BattlePhase.Inventory && Side == viewer.Faction && Side == BattleFaction.Player)
        {
            // The pre-battle inventory only walks the units that have an inventory.
            actions.Add(BattleActionKind.SelectNextInventoryUnit);
            actions.Add(BattleActionKind.SelectPreviousInventoryUnit);
        }
        else if (Phase == BattlePhase.InProgress && Side == viewer.Faction && Side == BattleFaction.Player)
        {
            actions.Add(BattleActionKind.SelectNextInventoryUnit);
            actions.Add(BattleActionKind.SelectPreviousInventoryUnit);
            actions.Add(BattleActionKind.SelectNextUnit);
            actions.Add(BattleActionKind.SelectPreviousUnit);
            actions.Add(BattleActionKind.SkipUnit);
            actions.Add(BattleActionKind.SkipUnitByDistance);
            if (UndoCandidate(rules) is not null) actions.Add(BattleActionKind.ReselectSkippedUnit);
            // btnInventoryClick: a selected unit whose armor has an inventory.
            if (InventoryAvailable(rules, unitId, out _) is null) actions.Add(BattleActionKind.OpenInventory);
            // The movement buttons and clicks (S10): what each lists is what the command can start; the time units it needs are the command's refusal.
            if (unit.Position is not null)
            {
                actions.Add(BattleActionKind.Turn);
                if (AllowsKneeling(rules, unit) && !unit.Floating) actions.Add(BattleActionKind.Kneel);
                if (ArmorOf(rules, unit).AllowsMoving) actions.Add(BattleActionKind.Walk);
                if (UpDownLegal(rules, unit, up: true)) actions.Add(BattleActionKind.MoveUp);
                if (UpDownLegal(rules, unit, up: false)) actions.Add(BattleActionKind.MoveDown);
            }
            foreach (var other in _units)
                if (other.Id != unitId && other.Faction == Side && !other.IsOut && other.Position is not null) others.Add(other.Id);
        }
        return new BattleLegalActions(unitId, actions, others);
    }

    internal BattleUnitInfo? UnitInfo(RuntimeRuleCatalog rules, BattleViewer viewer, int unitId)
    {
        if (!Enum.IsDefined(viewer.Faction)) throw new ArgumentOutOfRangeException(nameof(viewer));
        var unit = UnitById(unitId);
        if (unit is null || unit.Faction != viewer.Faction || unit.IsOut) return null;
        BattleTileItemView? Hand(bool right) => HandItem(rules, unit, right) is { } held ? new BattleTileItemView(held.Id, held.ItemType) : null;
        // updateSoldierInfo: the units this unit sees (its visible-unit list, in order), then the hostile units any unit of its side sees.
        var direct = unit.VisibleUnits;
        var visible = direct.Concat(_units.Where(other => other.OriginalFaction == BattleFaction.Hostile && !other.IsOut &&
                (viewer.Faction == BattleFaction.Player ? other.IsVisibleToPlayer(rules) : other.IsSpottedBy(viewer.Faction)) && !direct.Contains(other.Id))
            .Select(static other => other.Id)).ToArray();
        // updateSoldierInfo's wounded list: units under the player's control (the unconscious too) with fatal wounds; burning ones are the
        // client's option (oxceShowBurningAsWounded).
        var indicated = _units.Where(other => other.Faction == viewer.Faction && other.Status is not (BattleUnitStatus.Dead or BattleUnitStatus.IgnoreMe) && !other.DisableIndicators).ToArray();
        var wounded = indicated.Where(static other => other.FatalWounds.Sum() > 0).Select(static other => other.Id).ToArray();
        var burning = indicated.Where(static other => other.Fire > 0).Select(static other => other.Id).ToArray();
        return new BattleUnitInfo(unit.Id, unit.SoldierId, unit.UnitType, unit.TimeUnits, unit.Stats.TimeUnits, unit.Energy, unit.Stats.Stamina,
            unit.Health, unit.Stats.Health, unit.StunLevel, unit.Morale, unit.Mana, unit.Stats.Mana, unit.Kneeled,
            Hand(right: true), Hand(right: false), visible, wounded, burning);
    }

    /// <summary>
    /// One level as the viewer sees it. Tile parts show where the viewer's side has discovered them (the reference's flags for X-COM, the
    /// port's for the other sides, which also see the tiles they have explored); items and smoke only on a tile whose floor shows; a unit
    /// only when the viewer sees it.
    /// </summary>
    internal BattleLevelView? LevelView(BattleViewer viewer, int level)
    {
        if (!Enum.IsDefined(viewer.Faction)) throw new ArgumentOutOfRangeException(nameof(viewer));
        if (level < 0 || level >= Height) return null;
        var tiles = new List<BattleTileView>();
        var itemsByTile = new Dictionary<BattlePosition, List<BattleTileItemView>>();
        foreach (var item in _items)
            if (item.Tile is { } at && at.Z == level && item.Slot == GroundSlot && item.OwnerUnitId is null)
            {
                if (!itemsByTile.TryGetValue(at, out var list)) itemsByTile[at] = list = [];
            }
        foreach (var position in itemsByTile.Keys.ToArray())
            itemsByTile[position] = [.. ItemsOnTile(position).Select(static item => new BattleTileItemView(item.Id, item.ItemType))];
        for (var y = 0; y < Length; y++)
            for (var x = 0; x < Width; x++)
            {
                var position = new BattlePosition(x, y, level);
                var tile = Map.TileIndex(position);
                var explored = viewer.Faction != BattleFaction.Player && Map.LastExplored(tile, viewer.Faction) > 0;
                var parts = new List<BattleTilePartView>();
                foreach (var part in Enum.GetValues<BattleTilePart>())
                {
                    if (!(explored || Map.IsDiscovered(tile, part, viewer.Faction))) continue;
                    var (id, set) = Map.Part(tile, part);
                    if (id >= 0 && set >= 0) parts.Add(new BattleTilePartView(part, Map.DataSets[set].Name, id));
                }
                var floorShown = explored || Map.IsDiscovered(tile, BattleTilePart.Floor, viewer.Faction);
                var items = floorShown && itemsByTile.TryGetValue(position, out var onTile) ? onTile : [];
                int? standing = Map.UnitAt(tile) is { } unitId && UnitById(unitId) is { } unit && !unit.IsOut && Sees(viewer.Faction, unit) ? unitId : null;
                var smoke = floorShown ? Map.Smoke(tile) : 0;
                var fire = floorShown ? Map.Fire(tile) : 0;
                if (parts.Count == 0 && items.Count == 0 && standing is null && smoke == 0 && fire == 0) continue;
                tiles.Add(new BattleTileView(position, parts, smoke, fire, items, standing, Map.Shade(tile)));
            }
        return new BattleLevelView(level, Width, Length, Height, tiles, Revision);
    }
}
