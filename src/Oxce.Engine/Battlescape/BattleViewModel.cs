using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;

namespace Oxce.Engine.Battlescape;

/// <summary>
/// A battlescape client's view of the battle (Phase 7 branch 3 S5): what the battle's queries answer for its viewer, its own
/// selection and its camera. It decides nothing: every change of the battle is a command, every fact comes from a query, and the
/// selection, the legal actions and the visible tiles are the rules' (ADR 0037, ADR 0038).
/// </summary>
public sealed class BattleViewModel
{
    private readonly ICampaignCommandTarget _commands;
    private readonly IBattleQuery _battle;
    private readonly IBattleViewQuery _view;
    private readonly PresentationRevisionGate _gate = new();
    private int? _selected;
    private int _levelRead = -1;
    private (int X, int Y)? _previewTile;
    private bool _previewDirty;
    private bool _showReachable;

    public BattleViewModel(ICampaignCommandTarget commands, IBattleQuery battle, IBattleViewQuery view, BattleViewer viewer)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _battle = battle ?? throw new ArgumentNullException(nameof(battle));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        Viewer = viewer;
    }

    public BattleViewer Viewer { get; }

    public BattleCamera Camera { get; } = new();

    /// <summary>The battle as the viewer sees it, or null while there is none.</summary>
    public BattleOverview? Overview { get; private set; }

    public BattleSelectionView? Selection { get; private set; }

    /// <summary>The level at the camera's view level, with the tiles the viewer may see.</summary>
    public BattleLevelView? Level { get; private set; }

    /// <summary>The viewer's selected unit (this client's own: another player's selection never moves it), or null.</summary>
    public int? SelectedUnitId => _selected;

    public BattleUnitInfo? SelectedUnit { get; private set; }

    public BattleLegalActions? Actions { get; private set; }

    /// <summary>
    /// The path to the previewed tile (<see cref="Preview"/>) at the camera's level, as the rules answer for this viewer; null with no preview, no
    /// selection or a tile the viewer has not discovered. A path that does not exist is a view with no steps and its reason.
    /// </summary>
    public BattlePathView? Path { get; private set; }

    /// <summary>The discovered tiles the selected unit can reach, while <see cref="ShowReachable"/> is on.</summary>
    public BattleReachableView? Reachable { get; private set; }

    /// <summary>Whether the reachable tiles are asked for and shown.</summary>
    public bool ShowReachable
    {
        get => _showReachable;
        set
        {
            _showReachable = value;
            _previewDirty = true;
        }
    }

    /// <summary>The events of the last command that ran (empty after a refusal): what the screen replays.</summary>
    public IReadOnlyList<ICampaignEvent> LastEvents { get; private set; } = [];

    /// <summary>The refusal code of the last command, or "".</summary>
    public string Message { get; private set; } = "";

    /// <summary>
    /// Reads the queries again when the battle changed (its revision), or the camera's level did. Returns true when anything
    /// was read, so a screen knows to draw.
    /// </summary>
    public bool Refresh()
    {
        var overview = _battle.QueryBattle(Viewer);
        if (overview is null)
        {
            var had = Overview is not null;
            Overview = null;
            (Selection, Level, SelectedUnit, Actions, _selected, Path, Reachable) = (null, null, null, null, null, null, null);
            _gate.Reset();
            return had;
        }
        var changed = _gate.TryAccept(overview.Revision);
        if (!changed && Camera.ViewLevel == _levelRead && !_previewDirty) return false;
        Overview = overview;
        Camera.SetMap(overview.Width, overview.Length, overview.Height);
        Selection = _view.QuerySelection(Viewer);
        // The client keeps its own selection while the unit is still the viewer's; otherwise it follows the saved one.
        if (_selected is not { } own || _view.QueryUnitInfo(Viewer, own) is null) _selected = Selection?.SelectedUnitId;
        SelectedUnit = _selected is { } id ? _view.QueryUnitInfo(Viewer, id) : null;
        Actions = _selected is { } unit ? _view.QueryLegalActions(Viewer, unit) : null;
        _levelRead = Camera.ViewLevel;
        Level = _view.QueryLevel(Viewer, Camera.ViewLevel);
        // The preview and the reachable tiles are the rules' answers for the selected unit now; a new revision or level asks again.
        Path = _selected is { } walker && _previewTile is var (x, y) ? _view.QueryPath(Viewer, walker, new BattlePosition(x, y, Camera.ViewLevel)) : null;
        Reachable = _showReachable && _selected is { } reacher ? _view.QueryReachable(Viewer, reacher) : null;
        _previewDirty = false;
        return true;
    }

    /// <summary>
    /// Takes the rules' selection as this client's own (<c>BattlescapeState</c> shows <c>getSelectedUnit</c>), for when the rules
    /// moved it themselves: <c>startFirstTurn</c> selects the unit standing on the inventory tile. A negative ID is no selection.
    /// </summary>
    public void FollowSelection(int unitId)
    {
        _selected = unitId >= 0 ? unitId : null;
        _gate.Reset();
    }

    /// <summary>Clicking a unit: <c>BattlescapeGame::primaryAction</c>'s selection; the camera stays.</summary>
    public bool Select(int unitId) => Send(new SelectUnit(unitId), centerOnSelection: false);

    /// <summary>The next soldier button (right click: the previous one).</summary>
    public bool Next(bool backwards = false, bool keepCamera = false) => SendFromSelection(unit => new SelectNextUnit(unit, backwards), keepCamera, backwards);

    /// <summary>The inventory screen's previous and next soldier: units with an inventory, the camera stays.</summary>
    public bool NextInventoryUnit(bool backwards = false) => SendFromSelection(unit => new SelectNextUnit(unit, backwards, InventoryOnly: true), keepCamera: true, backwards);

    /// <summary>"Next and stop", by list order or by distance.</summary>
    public bool Skip(bool byDistance = false, bool keepCamera = false) => SendFromSelection(unit => new SkipUnit(unit, byDistance), keepCamera);

    /// <summary>The right click of "next and stop": the unit skipped last is selected again.</summary>
    public bool ReselectSkipped(bool keepCamera = false)
    {
        Refresh();
        Message = "";
        return Selection?.UndoUnitId is { } undo && Send(new ReselectSkippedUnit(undo), !keepCamera);
    }

    private bool SendFromSelection(Func<int, ICampaignCommand> command, bool keepCamera, bool backwards = false)
    {
        Refresh();
        if (_selected is { } unit) return Send(command(unit), !keepCamera);
        // Nothing selected (the reference then starts at the front of the list): select the first unit the rules offer.
        // The reference starts at the front of the list (at the end, going back); a command on no unit is refused with the rules' or the
        // session's reason. Skipping from nothing does not clear the undo unit as the reference does (ADR 0039).
        var units = Selection?.SelectableUnitIds ?? [];
        return Send(new SelectUnit(units.Count == 0 ? -1 : backwards ? units[^1] : units[0]), !keepCamera);
    }

    /// <summary>Previews the path to a tile of the camera's level (the pointer over it), or none (null).</summary>
    public void Preview(int? x, int? y)
    {
        var tile = x is { } px && y is { } py ? (px, py) : ((int, int)?)null;
        if (_previewTile == tile) return;
        _previewTile = tile;
        _previewDirty = true;
        Refresh();
    }

    /// <summary>
    /// A click on a tile (<c>BattlescapeGame::primaryAction</c>'s "start walking"): the selected unit walks to the tile at the camera's level;
    /// <paramref name="ignoreSpottedEnemies"/> is the Shift key. The camera follows the unit.
    /// </summary>
    public bool Walk(int x, int y, bool ignoreSpottedEnemies) =>
        SendMovement(unit => new WalkUnit(unit, new BattlePosition(x, y, Camera.ViewLevel), BattleMoveType.Normal, ignoreSpottedEnemies), follow: true);

    /// <summary>The right click on a tile (<c>secondaryAction</c>): the selected unit turns towards it, or tries the door it faces.</summary>
    public bool Turn(int x, int y) => SendMovement(unit => new TurnUnit(unit, new BattlePosition(x, y, Camera.ViewLevel)), follow: false);

    /// <summary>The kneel button (<c>btnKneelClick</c>).</summary>
    public bool Kneel() => SendMovement(static unit => new KneelUnit(unit), follow: false);

    /// <summary>The unit up and down buttons (<c>btnUnitUpClick</c>, <c>btnUnitDownClick</c>); the camera follows the unit to its level.</summary>
    public bool MoveUpDown(bool up) => SendMovement(unit => new MoveUnitUpDown(unit, up), follow: true);

    private bool SendMovement(Func<int, ICampaignCommand> command, bool follow)
    {
        Refresh();
        Message = "";
        LastEvents = [];
        // With nothing selected the command names no unit and the rules refuse it (as the selection buttons do).
        var result = _commands.Execute(command(_selected ?? -1));
        Message = result.Events.OfType<CampaignActionBlocked>().FirstOrDefault()?.Code ?? "";
        if (Message.Length != 0) return false;
        LastEvents = result.Events;
        _gate.Reset();
        Refresh();
        if (follow) CenterOnSelection();
        return true;
    }

    /// <summary>A visible-unit button's left click (<c>btnVisibleUnitClick</c>): the camera centers on that unit.</summary>
    public bool CenterOnUnit(int unitId)
    {
        Message = "";
        if (Overview?.Units.FirstOrDefault(unit => unit.Id == unitId)?.Position is not { } position)
        {
            // The reference centers on the unit that carries it (Phase 7 branch 5, with the carried units).
            Message = "battle.visible-unit-carried-unsupported (Phase 7 branch 5)";
            return false;
        }
        Camera.CenterOn(position);
        Refresh();
        return true;
    }

    /// <summary>A visible-unit button's right click: the rules select the unit of the side that sees it with the most time units left, and the camera centers on it.</summary>
    public bool SelectSpotter(int targetUnitId)
    {
        Refresh();
        return Send(new SelectSpotter(_selected ?? -1, targetUnitId), centerOnSelection: true);
    }

    private bool Send(ICampaignCommand command, bool centerOnSelection)
    {
        var result = _commands.Execute(command);
        Message = result.Events.OfType<CampaignActionBlocked>().FirstOrDefault()?.Code ?? "";
        if (Message.Length != 0) return false;
        foreach (var raised in result.Events)
            switch (raised)
            {
                case UnitSelected selected:
                    _selected = selected.UnitId >= 0 ? selected.UnitId : null;
                    break;
                case UnitSkipped skipped:
                    _selected = skipped.SelectedUnitId >= 0 ? skipped.SelectedUnitId : null;
                    break;
            }
        _gate.Reset();
        Refresh();
        if (centerOnSelection) CenterOnSelection();
        return true;
    }

    /// <summary>Camera centered on the selected unit (<c>btnCenterClick</c>); false without a selection or off the map.</summary>
    public bool CenterOnSelection()
    {
        Message = "";
        if (Overview is not { Side: BattleFaction.Player } || _selected is not { } id || Overview.Units.FirstOrDefault(unit => unit.Id == id)?.Position is not { } position) return false;
        Camera.CenterOn(position);
        Refresh();
        return true;
    }

    /// <summary>The map level buttons (<c>btnMapUpClick</c>, <c>btnMapDownClick</c>): only on X-COM's turn.</summary>
    public bool LevelUp() => ChangeLevel(static camera => camera.Up());

    public bool LevelDown() => ChangeLevel(static camera => camera.Down());

    private bool ChangeLevel(Func<BattleCamera, bool> change)
    {
        // A level button the camera or the turn does not allow is a silent no-op, as in the reference.
        Message = "";
        if (Overview is not { Side: BattleFaction.Player } || !change(Camera)) return false;
        Refresh();
        return true;
    }
}
