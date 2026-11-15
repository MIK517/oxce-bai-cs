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

    /// <summary>The action menu of the weapon in the hand the player opened (<c>ActionMenuState</c>), or null while none is open.</summary>
    public BattleAimMenuView? Menu { get; private set; }

    /// <summary>The weapon and the action being aimed (<c>BattleAction::targeting</c>): null while the player is not aiming.</summary>
    public (int ItemId, BattleAimKind Kind)? Aiming { get; private set; }

    /// <summary>What aiming at the tile under the pointer shows (the cursor's accuracy and the checks of <c>ProjectileFlyBState</c>), or null.</summary>
    public BattleAimView? Aim { get; private set; }

    /// <summary>The waypoints of a launch, in order (<c>BattleAction::waypoints</c>).</summary>
    public IReadOnlyList<BattlePosition> Waypoints => _waypoints;

    /// <summary>The turns a primed item's fuse is set to (the prime grenade menu's buttons, 0 to 23), used by the menu's priming line.</summary>
    public int PrimeTurns { get; set; }

    private readonly List<BattlePosition> _waypoints = [];
    private (int X, int Y, bool Force)? _aimTile;
    private int _maxWaypoints, _sprayMax;
    private bool _spraying;

    /// <summary>The hand and special buttons' middle click (Ufopaedia), refused by owner, and the special button of a unit with no special weapon (the reference's hack opens the skills menu, branch 6).</summary>
    public const string SkillsCode = "battle.skills-unsupported (Phase 7 branch 6)";
    public const string UfopaediaCode = "battle.ufopaedia-unsupported (Phase 8)";

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
            CancelAim();
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
        // An aim belongs to the unit and weapon it was opened for: another selection, or a weapon that left the hand, ends it.
        if ((Aiming?.ItemId ?? Menu?.ItemId) is { } weapon && (SelectedUnit is null || SelectedUnit.UnitId != (Menu?.UnitId ?? SelectedUnit.UnitId) ||
                SelectedUnit.RightHand?.ItemId != weapon && SelectedUnit.LeftHand?.ItemId != weapon && SelectedUnit.SpecialWeaponIds?.Contains(weapon) != true)) CancelAim();
        Aim = Aiming is { Kind: not (BattleAimKind.Launch or BattleAimKind.Hit) } aiming && _aimTile is var (ax, ay, force) && _selected is { } shooter
            ? _view.QueryAim(Viewer, shooter, aiming.ItemId, aiming.Kind, new BattlePosition(ax, ay, Camera.ViewLevel), force)
            : null;
        return true;
    }

    /// <summary>
    /// The hand buttons (<c>btnLeftHandItemClick</c>, <c>btnRightHandItemClick</c>): an aim in progress is cancelled instead (the touch concession; a launch loses its last waypoint); else
    /// the clicked hand becomes the unit's active hand, and the left click opens the action menu of the item in it (an empty hand opens the menu of the special weapon that uses an empty hand,
    /// and nothing when there is none). The right click toggles the hand for reactions (<paramref name="ctrl"/> disables its reactions instead) and the middle one opens the item's Ufopaedia page, which
    /// is refused by name. <paramref name="button"/> is 1, 2 or 3. True when the menu opened or the reaction hand was toggled.
    /// </summary>
    public bool OpenMenu(bool rightHand, int button = 1, bool ctrl = false)
    {
        Refresh();
        Message = "";
        if (Aiming is not null)
        {
            CancelLast();
            return false;
        }
        CancelAim();
        if (SelectedUnit is not { } unit) return false;
        var result = _commands.Execute(new SetActiveHand(unit.UnitId, rightHand));
        Message = result.Events.OfType<CampaignActionBlocked>().FirstOrDefault()?.Code ?? "";
        if (Message.Length != 0) return false;
        _gate.Reset();
        Refresh();
        if (button == 3)
        {
            var toggled = _commands.Execute(new ToggleHandForReactions(unit.UnitId, rightHand, ctrl));
            Message = toggled.Events.OfType<CampaignActionBlocked>().FirstOrDefault()?.Code ?? "";
            _gate.Reset();
            Refresh();
            return Message.Length == 0;
        }
        return OpenItemMenu(unit, (rightHand ? unit.RightHand : unit.LeftHand) ?? unit.EmptyHandItem, button == 2);
    }

    /// <summary>
    /// The special-weapon button (<c>btnSpecialClick</c>): an aim in progress is cancelled instead (a launch loses its last waypoint, as with the hand buttons); an open menu is closed first, as the hand buttons do; else the menu of the unit's special icon weapon opens (the middle click is its Ufopaedia page).
    /// A unit with none does nothing, but for the skills menu the reference's button falls back on, which is branch 6's and refused by name for a soldier whose rules list a skill.
    /// </summary>
    public bool OpenSpecialMenu(bool middle = false)
    {
        Refresh();
        Message = "";
        if (Aiming is not null)
        {
            CancelLast();
            return false;
        }
        CancelAim();
        if (SelectedUnit is not { } unit) return false;
        if (unit.SpecialIconItem is null)
        {
            if (unit.HasSkills) Message = SkillsCode;
            return false;
        }
        return OpenItemMenu(unit, unit.SpecialIconItem, middle);
    }

    /// <summary><c>handleItemClick</c>: nothing for no item, the Ufopaedia refusal for a middle click, else the item's action menu.</summary>
    private bool OpenItemMenu(BattleUnitInfo unit, BattleTileItemView? shown, bool middle)
    {
        if (shown is not { } item) return false;
        if (middle)
        {
            Message = UfopaediaCode;
            return false;
        }
        Menu = _view.QueryAimMenu(Viewer, unit.UnitId, item.ItemId);
        // The medikit, scanner, mind probe and psi amp lines are branch 6's: the menu opens with what it can list, and says what it cannot.
        if (Menu is { Unsupported.Length: > 0 }) Message = Menu.Unsupported;
        return Menu is not null;
    }

    /// <summary>
    /// A line of the open menu (<c>ActionMenuState::btnActionMenuItemClick</c>): a priming line and a hit act at once, the other lines start aiming. The launch line checks the time units
    /// and the ammunition first and refuses at once, as the menu does. <paramref name="alt"/> is the Alt key of the click or key that chose the line (a hit reads it).
    /// </summary>
    public bool Choose(BattleAimKind kind, bool alt = false)
    {
        if (Menu is not { } menu || menu.Items.All(item => item.Kind != kind)) return false;
        Menu = null;
        switch (kind)
        {
            case BattleAimKind.Prime: return SendAction(new PrimeItem(menu.UnitId, menu.ItemId, PrimeTurns));
            case BattleAimKind.Unprime: return SendAction(new UnprimeItem(menu.UnitId, menu.ItemId));
            case BattleAimKind.Hit: return SendAction(new Attack(menu.UnitId, menu.ItemId, BattleAimKind.Hit, alt));
            case BattleAimKind.Launch when menu.LaunchRefusal.Length != 0:
                Message = menu.LaunchRefusal;
                return false;
        }
        Aiming = (menu.ItemId, kind);
        Message = "";
        _waypoints.Clear();
        (_maxWaypoints, _sprayMax, _spraying) = (menu.Waypoints, menu.SprayWaypoints, false);
        return true;
    }

    /// <summary>Closes the menu and ends the aim and its waypoints (<c>cancelCurrentAction</c>).</summary>
    public void CancelAim()
    {
        (Menu, Aiming, Aim, _aimTile) = (null, null, null, null);
        _waypoints.Clear();
        _spraying = false;
    }

    /// <summary>The pointer over a tile while aiming: the rules answer what a shot there would show (Ctrl is force fire).</summary>
    public void AimAt(int? x, int? y, bool force)
    {
        var tile = x is { } px && y is { } py ? (px, py, force) : ((int, int, bool)?)null;
        if (_aimTile == tile) return;
        _aimTile = tile;
        _previewDirty = true;
        Refresh();
    }

    /// <summary>
    /// The click of an aim (<c>primaryAction</c> while targeting): a shot or a throw goes at the tile; a launch adds a waypoint (a click beyond the weapon's limit is ignored), and the launch
    /// button then fires it; Ctrl and Shift with the first click of an auto shot of a weapon with spray waypoints start the spray, whose clicks collect the waypoints and fire early with
    /// Ctrl and Shift or at the last one. A shot leaves the aim as it was, so the player can shoot again; a refusal leaves it too, and a throw or a launch that was done ends it.
    /// </summary>
    public bool Target(int x, int y, bool force, bool alt = false, bool shift = false)
    {
        Refresh();
        if (Aiming is not { } aiming || _selected is not { } unit) return false;
        var target = new BattlePosition(x, y, Camera.ViewLevel);
        Message = "";
        LastEvents = [];
        if (aiming.Kind == BattleAimKind.Launch)
        {
            if (_maxWaypoints == -1 || _waypoints.Count < _maxWaypoints) _waypoints.Add(target);
            return true;
        }
        if (_spraying)
        {
            // At the limit, or with Ctrl and Shift, or at the last free waypoint, the spray fires (with one more waypoint when it is early).
            if (_waypoints.Count >= _sprayMax || force && shift || _waypoints.Count == _sprayMax - 1)
            {
                if (_waypoints.Count < _sprayMax) _waypoints.Add(target);
                var spray = _waypoints.ToArray();
                _spraying = false;
                _waypoints.Clear();
                return SendAction(new Attack(unit, aiming.ItemId, aiming.Kind, alt, spray[0], true, SprayWaypoints: spray));
            }
            _waypoints.Add(target);
            return true;
        }
        if (aiming.Kind == BattleAimKind.AutoShot && _sprayMax > 0 && force && shift && _waypoints.Count == 0)
        {
            _spraying = true;
            _waypoints.Add(target);
            return true;
        }
        return SendAction(new Attack(unit, aiming.ItemId, aiming.Kind, alt, target, force));
    }

    /// <summary>The launch button (<c>btnLaunchClick</c>): the missile flies through the waypoints set so far. Nothing happens without a waypoint.</summary>
    public bool Launch()
    {
        Refresh();
        if (Aiming is not { Kind: BattleAimKind.Launch } aiming || _waypoints.Count == 0 || _selected is not { } unit) return false;
        return SendAction(new Attack(unit, aiming.ItemId, BattleAimKind.Launch, Waypoints: [.. _waypoints]));
    }

    /// <summary>
    /// The right click or Esc while aiming (<c>cancelCurrentAction</c>): the last waypoint of a launch or a spray goes first (the spray ends with its last one), else the aim ends. False
    /// when nothing was aimed.
    /// </summary>
    public bool CancelLast()
    {
        if (Menu is null && Aiming is null) return false;
        if (_waypoints.Count != 0 && (_spraying || Aiming is { Kind: BattleAimKind.Launch }))
        {
            _waypoints.RemoveAt(_waypoints.Count - 1);
            if (_waypoints.Count == 0) _spraying = false;
        }
        else CancelAim();
        return true;
    }

    /// <summary>
    /// Sends the command of an action. As in <c>BattlescapeGame::popState</c> the aim stays after a shot (the player may shoot again in the same mode) and after a refusal (a launch keeps
    /// its waypoints); a throw or a launch that was done ends it. A priming or a hit has no aim (the menu closed).
    /// </summary>
    private bool SendAction(ICampaignCommand command)
    {
        Message = "";
        LastEvents = [];
        var result = _commands.Execute(command);
        Message = result.Events.OfType<CampaignActionBlocked>().FirstOrDefault()?.Code ?? "";
        if (Message.Length != 0) return false;
        LastEvents = result.Events;
        if (Aiming is { Kind: BattleAimKind.Throw or BattleAimKind.Launch }) CancelAim();
        _gate.Reset();
        Refresh();
        return true;
    }

    /// <summary>The reserve buttons (<c>btnReserveClick</c>): the time units kept for a snap, aimed or auto shot, or none.</summary>
    public bool Reserve(BattleReserveKind kind) => SendReserve(unit => new SetTimeUnitReserve(unit, kind));

    /// <summary>The kneel reserve button (<c>btnReserveKneelClick</c>).</summary>
    public bool ToggleKneelReserve() => SendReserve(static unit => new ToggleKneelReserve(unit));

    /// <summary>The reference needs no selected unit for a reserve button, only that the buttons are allowed; the command names a unit of this client's slot, the selected one or else any.</summary>
    private bool SendReserve(Func<int, ICampaignCommand> command)
    {
        Refresh();
        CancelAim();
        Message = "";
        var unit = _selected ?? Overview?.Units.FirstOrDefault(own => own.Faction == Viewer.Faction && own.ControllerSlot == Viewer.PlayerSlot)?.Id ?? -1;
        var result = _commands.Execute(command(unit));
        Message = result.Events.OfType<CampaignActionBlocked>().FirstOrDefault()?.Code ?? "";
        if (Message.Length != 0) return false;
        _gate.Reset();
        Refresh();
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

    /// <summary>A visible-unit button's left click (<c>btnVisibleUnitClick</c>): the camera centers on that unit, or on the unit that carries it.</summary>
    public bool CenterOnUnit(int unitId)
    {
        Message = "";
        if (Overview?.Units.FirstOrDefault(unit => unit.Id == unitId)?.Whereabouts is not { } position) return false;
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
