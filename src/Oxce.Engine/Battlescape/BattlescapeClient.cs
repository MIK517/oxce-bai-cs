using Oxce.Engine.Input;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;

namespace Oxce.Engine.Battlescape;

/// <summary>
/// The battlescape screen (<c>BattlescapeState</c>) on the indexed loop: the level the camera shows as a flat plan of the tiles the viewer may
/// see, the units and items on them, the selected soldier's bars and hands, and the buttons. It reads <see cref="BattleViewModel"/> and
/// sends commands; the legal actions come from the rules. The tile click walks, the right click turns, the kneel and up and down keys and the
/// visible-unit buttons act, the path under the pointer is previewed with its time units and energy, and a command's events are replayed
/// (<see cref="BattleReplay"/>). Buttons whose rules belong to a later branch are refused here by their owner's name
/// (<see cref="LaterButtons"/>) and change nothing. A terrain object without a sprite is drawn as a placeholder and listed in
/// <see cref="BattleUiContext.Diagnostics"/>. Isometric drawing and pixel scrolling are the presentation work that follows.
/// </summary>
public sealed class BattlescapeClient : BattleScreenBase, IRevealedScreen
{
    private const uint HomeKey = 0x4000004a;

    /// <summary>The map area and the side panel, in pixels.</summary>
    public const int MapWidth = 448, MapHeight = 400;

    /// <summary>Buttons of the reference's battlescape whose rules arrive later: key, code and owner. Each is refused by that code.</summary>
    public static readonly IReadOnlyList<(char Key, string Code, string Owner)> LaterButtons =
    [
        ('p', "battle.psi-unsupported", "Phase 7 branch 6"),
        ('j', "battle.skills-unsupported", "Phase 7 branch 6"),
        ('z', "battle.zero-time-units-unsupported", "Phase 7 branch 6"),
        ('e', "battle.end-turn-unsupported", "Phase 7 branch 6"),
        ('a', "battle.abort-unsupported", "Phase 7 branch 7"),
        ('s', "battle.unit-stats-unsupported", "Phase 8 (UnitInfoState)"),
        ('o', "battle.options-unsupported", "Phase 8 (options, pause and save screens)"),
        ('m', "battle.minimap-unsupported", "Phase 7 branch 8 or Phase 8"),
    ];

    /// <summary>Reference handlers on other inputs, refused by owner: Esc (<c>keyBattleDeselectUnit</c>), Ctrl-B (the info-only briefing) and the middle button over a unit (<c>AlienInventoryState</c>).</summary>
    public const string DeselectCode = "battle.deselect-unsupported (Phase 7 branch 6)";
    public const string BriefingAgainCode = "battle.briefing-info-unsupported (Phase 8)";
    public const string AlienInventoryCode = "battle.alien-inventory-unsupported (Phase 8, AlienInventoryState)";

    /// <summary>A click while a walk is replayed: the reference cancels it (<c>cancelCurrentAction</c>), but a command resolves fully in the rules (ADR 0037).</summary>
    public const string CancelWalkCode = "battle.cancel-walk-unsupported (ADR 0037: an action resolves fully)";

    /// <summary>The wounded, burning and dizzy friends of <c>updateSoldierInfo</c> are not listed beside the visible units.</summary>
    public const string IndicatorButtonsCode = "battle.wounded-indicators-unsupported (Phase 8)";

    /// <summary>The hand buttons (<c>btnLeftHandItemClick</c>, <c>btnRightHandItemClick</c>), the launch button, the reserve cycle and the kneel reserve; F1 to F4 reserve none, snap, aimed and auto.</summary>
    public const char LeftHandKey = 'q', RightHandKey = 't', SpecialKey = 'y', LaunchKey = 'n', ReserveKey = 'v', ReserveKneelKey = 'b';

    private const uint F1 = 0x4000003a;

    /// <summary>The panel's lines: the hands are buttons, the menu or the actions list follow them, the aim line and the reserve buttons come below.</summary>
    private const int RightHandY = 70, LeftHandY = 82, MenuY = 110, MenuLineHeight = 12, AimY = 272, ReserveY = 284, ReserveWidth = 20, ReserveHeight = 12, SpecialX = MapWidth + 8 + 5 * (ReserveWidth + 2);

    /// <summary>The visible-unit buttons (<c>VISIBLE_MAX</c>), drawn in rows in the panel; the first ten have the keys 1 to 9 and 0.</summary>
    public const int VisibleMax = 10, VisibleKeys = 10;

    private const int VisibleButtonsX = MapWidth + 8, VisibleButtonsY = 316, VisibleButtonWidth = 20, VisibleButtonHeight = 12, VisibleButtonColumns = 8;

    private IReadOnlyList<BattleReplayFrame> _replay = [];
    private int _replayAt;

    public BattlescapeClient(BattleUiContext context) : base(context)
    {
        Context.Model.Refresh();
        Opened();
        Changed();
    }

    /// <summary>
    /// <c>BattlescapeState::init</c> (the first time: a unit is selected when none is playable) and <c>NextTurnState::close</c>
    /// (<c>btnCenterClick</c>): X-COM's turn opens with a selected unit and the camera on it.
    /// </summary>
    private void Opened()
    {
        if (Model.Overview is not { Side: BattleFaction.Player }) return;
        if (Model.SelectedUnitId is null && Model.Selection?.SelectableUnitIds is [var first, ..]) Model.Select(first);
        Model.CenterOnSelection();
    }

    public BattleViewModel Model => Context.Model;

    /// <summary>Pixels per tile, so the whole level fits the map area.</summary>
    public int TileSize => Model.Overview is { } overview ? Math.Clamp(Math.Min(MapWidth / overview.Width, MapHeight / overview.Length), 2, 12) : 8;

    public void Revealed()
    {
        Model.Refresh();
        Changed();
    }

    protected override bool Poll() => Model.Refresh();

    /// <summary>The frames of the last command's events and how many have been played; the replay is over when all have.</summary>
    public IReadOnlyList<BattleReplayFrame> Replay => _replay;

    public int ReplayPlayed => _replayAt;

    public bool Replaying => _replayAt < _replay.Count;

    /// <summary>The frame played last, or null when none has played yet. The trail of the steps played stays on the map until the next input.</summary>
    public BattleReplayFrame? ReplayFrame => _replayAt > 0 ? _replay[_replayAt - 1] : null;

    public override void Tick(TimeSpan fixedInterval)
    {
        // The battle already holds the result of the command: the replay only shows how it came about, one event a tick.
        if (!Replaying)
        {
            base.Tick(fixedInterval);
            return;
        }
        Model.Refresh();
        Message = _replay[_replayAt].Text;
        _replayAt++;
        Changed();
    }

    private void StartReplay()
    {
        _replay = BattleReplay.From(Model.LastEvents);
        _replayAt = 0;
    }

    private void EndReplay()
    {
        _replay = [];
        _replayAt = 0;
    }

    /// <summary>Plays out what has not been replayed yet at once: the last frame's line stays as the message.</summary>
    private void FinishReplay()
    {
        if (_replay.Count != 0) Message = _replay[^1].Text;
        EndReplay();
    }

    protected override bool Handle(in GameInputEvent input)
    {
        // The pointer moving only previews; any other input ends a replay in progress (the battle already holds its result).
        if (input.Kind == GameInputEventKind.PointerMoved) return Hover(input);
        // Releases, text and key repeats do not end it (the release of the click that started the walk follows it at once).
        var pressed = input.Kind == GameInputEventKind.PointerPressed || input.Kind == GameInputEventKind.KeyPressed && !input.IsRepeat;
        if (!pressed) return false;
        var replayed = _replay.Count != 0;
        if (Replaying)
        {
            // The game is busy while a walk is shown: the press ends the replay and is dropped (a right click would cancel the walk in the reference).
            FinishReplay();
            if (input.Kind == GameInputEventKind.PointerPressed && input.PointerButton == 3) Message = CancelWalkCode;
            return true;
        }
        EndReplay();
        if (input.Kind == GameInputEventKind.PointerPressed) return Click(input) || replayed;
        Model.Refresh();
        Message = "";
        var key = input.KeyCode;
        var shift = Shift(input);
        if (Model.Menu is { } menu) return MenuKey(menu, key, (input.Modifiers & InputKeyModifiers.Alt) != 0);
        switch (key)
        {
            case LeftHandKey or RightHandKey: HandButton(key == RightHandKey, 1); return true;
            case SpecialKey: SpecialButton(false); return true;
            case LaunchKey: Attacked(Model.Launch()); return true;
            case ReserveKey: Report(Model.Reserve(NextReserve(Model.Selection?.TimeUnitReserve ?? BattleReserveKind.None))); return true;
            case 'b' when (input.Modifiers & InputKeyModifiers.Control) != 0: Message = BriefingAgainCode; return true;
            case ReserveKneelKey: Report(Model.ToggleKneelReserve()); return true;
            case >= F1 and <= F1 + 3: Report(Model.Reserve((BattleReserveKind)(key - F1))); return true;
            case KeyEscape when Model.CancelLast(): return true;
            case KeyTab: Report(Model.Next(backwards: shift)); return true;
            case 'k': Report(shift ? Model.Skip(byDistance: true) : Model.Skip()); return true;
            case 'r': Report(Model.ReselectSkipped()); return true;
            case 'c' when (input.Modifiers & InputKeyModifiers.Control) != 0: Model.Camera.ToggleShowAllLayers(); return true;
            case 'c' or HomeKey: Report(Model.CenterOnSelection()); return true;
            case KeyEscape: Message = DeselectCode; return true;
            case >= '0' and <= '9': VisibleUnit((int)(key - '1' + VisibleKeys) % VisibleKeys, right: false); return true;
            case KeyPageUp or ']': Report(Model.LevelUp()); return true;
            case KeyPageDown or '[': Report(Model.LevelDown()); return true;
            case 'i': return OpenInventory();
            case KneelKey: Moved(Model.Kneel()); return true;
            case UnitUpKey: Moved(Model.MoveUpDown(up: true)); return true;
            case UnitDownKey: Moved(Model.MoveUpDown(up: false)); return true;
            case ReachableKey: Model.ShowReachable = !Model.ShowReachable; Model.Refresh(); return true;
            case KeyLeft: Scroll(-1, 0); return true;
            case KeyRight: Scroll(1, 0); return true;
            case KeyUp: Scroll(0, -1); return true;
            case KeyDown: Scroll(0, 1); return true;
        }
        foreach (var (later, code, owner) in LaterButtons)
            if (key == later)
            {
                Message = $"{code} ({owner})";
                return true;
            }
        return replayed;
    }

    private static BattleReserveKind NextReserve(BattleReserveKind now) => now == BattleReserveKind.AutoShot ? BattleReserveKind.None : now + 1;

    /// <summary>A key while the action menu is open: a number picks its line, + and - set the fuse of the priming line, Esc closes it; every other key is ignored (the menu is modal).</summary>
    private bool MenuKey(BattleAimMenuView menu, uint key, bool alt)
    {
        if (key is >= '1' and <= '9')
        {
            ChooseLine(menu, (int)(key - '1'), alt);
            return true;
        }
        switch (key)
        {
            case '+' or '=': Model.PrimeTurns = Math.Min(23, Model.PrimeTurns + 1); break;
            case '-': Model.PrimeTurns = Math.Max(0, Model.PrimeTurns - 1); break;
            case KeyEscape: Model.CancelAim(); break;
        }
        return true;
    }

    private void ChooseLine(BattleAimMenuView menu, int line, bool alt)
    {
        if (line >= menu.Items.Count) return;
        var kind = menu.Items[line].Kind;
        var done = Model.Choose(kind, alt);
        // A priming line and a hit act at once: their events are replayed or their refusal shown. A launch line the menu's checks refuse shows the refusal.
        if (kind is BattleAimKind.Prime or BattleAimKind.Unprime or BattleAimKind.Hit or BattleAimKind.Launch) Attacked(done);
    }

    /// <summary>A hand button: the active hand is set and the menu opens, or the click is refused by name (<paramref name="button"/> 1 left, 2 middle, 3 right).</summary>
    private void HandButton(bool rightHand, int button, bool ctrl = false)
    {
        if (!Model.OpenMenu(rightHand, button, ctrl) || Model.Message.Length != 0) Message = Model.Message;
    }

    /// <summary>The special-weapon button (key <see cref="SpecialKey"/>): the menu of the unit's special icon weapon.</summary>
    private void SpecialButton(bool middle)
    {
        if (!Model.OpenSpecialMenu(middle) || Model.Message.Length != 0) Message = Model.Message;
    }

    /// <summary>After an attack, priming or launch: its events are replayed, or its refusal shown.</summary>
    private void Attacked(bool done) => Moved(done);

    /// <summary>Kneel, unit up, unit down and the reachable-tile overlay's keys.</summary>
    public const char KneelKey = 'x', UnitUpKey = 'u', UnitDownKey = 'd', ReachableKey = 'g';

    /// <summary>After a movement command: its events are replayed, or its refusal shown.</summary>
    private void Moved(bool done)
    {
        if (done) StartReplay();
        else Message = Model.Message;
    }

    /// <summary>The map tile under a pointer position, or null outside the map.</summary>
    private (int X, int Y)? TileAt(in GameInputEvent input)
    {
        if (input.X < 0 || input.X >= MapWidth || input.Y < 0 || input.Y >= MapHeight || Model.Overview is not { } overview) return null;
        var tile = TileSize;
        var (x, y) = ((int)input.X / tile, (int)input.Y / tile);
        return x < overview.Width && y < overview.Length ? (x, y) : null;
    }

    /// <summary>The pointer over a tile previews the path to it. The reference previews only on a click and only with <c>battleNewPreviewPath</c> on (default off); the hover is the port's addition.</summary>
    private bool Hover(in GameInputEvent input)
    {
        if (Model.Aiming is not null)
        {
            // While aiming the pointer shows the cursor's accuracy for the tile (Ctrl is force fire) instead of a path.
            var beforeAim = Model.Aim;
            if (TileAt(input) is var (ax, ay)) Model.AimAt(ax, ay, (input.Modifiers & InputKeyModifiers.Control) != 0);
            else Model.AimAt(null, null, false);
            return !ReferenceEquals(beforeAim, Model.Aim);
        }
        var before = Model.Path;
        if (TileAt(input) is var (x, y)) Model.Preview(x, y);
        else Model.Preview(null, null);
        return !ReferenceEquals(before, Model.Path);
    }

    private bool Click(in GameInputEvent input)
    {
        Model.Refresh();
        Message = "";
        if (Model.Menu is { } menu)
        {
            // The menu is a popup: a click on a line picks it, any other click closes it.
            if (input.PointerButton == 1 && MenuLineAt(input) is { } line) ChooseLine(menu, line, (input.Modifiers & InputKeyModifiers.Alt) != 0);
            else Model.CancelAim();
            return true;
        }
        if (input.PointerButton == 1 && ReserveButtonAt(input) is { } reserve)
        {
            Report(reserve == 4 ? Model.ToggleKneelReserve() : Model.Reserve((BattleReserveKind)reserve));
            return true;
        }
        if (input.PointerButton is 1 or 2 or 3 && HandAt(input) is { } rightHand)
        {
            HandButton(rightHand, (int)input.PointerButton, (input.Modifiers & InputKeyModifiers.Control) != 0);
            return true;
        }
        if (input.PointerButton is 1 or 2 && SpecialButtonAt(input))
        {
            SpecialButton(input.PointerButton == 2);
            return true;
        }
        if (Model.Aiming is { } aiming && TileAt(input) is var (tx, ty))
        {
            // primaryAction while targeting: the shot or throw goes at the tile (Ctrl: force fire), a launch gets a waypoint; the right click cancels.
            if (input.PointerButton == 3) Model.CancelLast();
            else if (input.PointerButton == 1)
            {
                var done = Model.Target(tx, ty, (input.Modifiers & InputKeyModifiers.Control) != 0, (input.Modifiers & InputKeyModifiers.Alt) != 0, Shift(input));
                // A launch only collects a waypoint (the launch button fires), as does the start or a click of a spray; a shot's events are replayed, a refusal shown.
                if (done && Model.LastEvents.Count == 0) Message = "";
                else Attacked(done);
            }
            return true;
        }
        if (VisibleButtonAt(input) is { } button)
        {
            // A visible-unit button: left centers on the unit, right selects the unit that sees it best.
            if (input.PointerButton is 1 or 3) VisibleUnit(button, right: input.PointerButton == 3);
            return true;
        }
        if (TileAt(input) is not var (x, y)) return false;
        var unit = Model.Level?.Tiles.FirstOrDefault(shown => shown.Position.X == x && shown.Position.Y == y)?.UnitId;
        var faction = unit is { } shownUnit ? Model.Overview!.Units.FirstOrDefault(other => other.Id == shownUnit)?.Faction : null;
        switch (input.PointerButton)
        {
            case 3:
                // secondaryAction: turn towards the tile, or open the door in front.
                Moved(Model.Turn(x, y));
                return true;
            case 2:
                // The middle button over a visible unit opens the alien inventory (Phase 8); anywhere else it does nothing.
                if (unit is not null) Message = AlienInventoryCode;
                return true;
        }
        // primaryAction: a click on a visible unit that is not the selected one selects it when it is the side's (a hostile one does nothing);
        // anything else is a walk to the tile.
        if (unit is { } id && id != Model.SelectedUnitId)
        {
            if (faction == Context.Viewer.Faction) Report(Model.Select(id));
            return true;
        }
        Moved(Model.Walk(x, y, Shift(input)));
        return true;
    }

    private int? MenuLineAt(in GameInputEvent input)
    {
        if (Model.Menu is not { } menu || input.X < MapWidth + 8 || input.Y < MenuY) return null;
        var line = ((int)input.Y - MenuY) / MenuLineHeight;
        return line < menu.Items.Count ? line : null;
    }

    /// <summary>The hand line under the pointer: true for the right hand, false for the left, null for neither.</summary>
    private bool? HandAt(in GameInputEvent input)
    {
        if (Model.SelectedUnit is null || input.X < MapWidth + 8) return null;
        if (input.Y >= RightHandY && input.Y < RightHandY + 12) return true;
        return input.Y >= LeftHandY && input.Y < LeftHandY + 12 ? false : null;
    }

    /// <summary>The special-weapon button, drawn after the reserve buttons: only a unit with a special icon weapon of a usable type (or a soldier with skills, whose button falls back on the skills menu) shows it (<c>BattleUnitInfo.SpecialButtonShown</c>).</summary>
    private bool SpecialButtonAt(in GameInputEvent input) =>
        Model.SelectedUnit is { SpecialButtonShown: true } &&
        input.X >= SpecialX && input.X < SpecialX + ReserveWidth && input.Y >= ReserveY && input.Y < ReserveY + ReserveHeight;

    /// <summary>The reserve button under the pointer: 0 to 3 for none, snap, aimed and auto, 4 for kneeling.</summary>
    private static int? ReserveButtonAt(in GameInputEvent input)
    {
        var (px, py) = ((int)input.X - (MapWidth + 8), (int)input.Y - ReserveY);
        if (px < 0 || py < 0 || py >= ReserveHeight) return null;
        var index = px / (ReserveWidth + 2);
        return index < 5 && px % (ReserveWidth + 2) < ReserveWidth ? index : null;
    }

    /// <summary>How many visible-unit buttons the unit's panel shows (at most <see cref="VisibleMax"/>).</summary>
    private static int VisibleButtonCount(BattleUnitInfo unit) => Math.Min(VisibleMax, unit.VisibleUnitIds.Count);

    private int? VisibleButtonAt(in GameInputEvent input)
    {
        if (Model.SelectedUnit is not { } unit) return null;
        var (px, py) = ((int)input.X - VisibleButtonsX, (int)input.Y - VisibleButtonsY);
        if (px < 0 || py < 0) return null;
        var (column, row) = (px / (VisibleButtonWidth + 2), py / (VisibleButtonHeight + 2));
        if (column >= VisibleButtonColumns || px % (VisibleButtonWidth + 2) >= VisibleButtonWidth || py % (VisibleButtonHeight + 2) >= VisibleButtonHeight) return null;
        var index = row * VisibleButtonColumns + column;
        return index < VisibleButtonCount(unit) ? index : null;
    }

    /// <summary>The visible-unit key, or button: the camera centers on the unit; the right click form selects the unit that sees it best.</summary>
    private void VisibleUnit(int index, bool right)
    {
        Model.Refresh();
        if (Model.SelectedUnit is not { } unit || index >= VisibleButtonCount(unit)) return;
        var target = unit.VisibleUnitIds[index];
        if (right) Report(Model.SelectSpotter(target));
        else Report(Model.CenterOnUnit(target));
    }

    private bool OpenInventory()
    {
        // Asked of the rules now: the camera's cached actions may be older than the battle.
        var actions = Model.SelectedUnitId is { } unit ? Context.View.QueryLegalActions(Context.Viewer, unit) : null;
        if (actions is null || !actions.Actions.Contains(BattleActionKind.OpenInventory))
        {
            Message = "battle.inventory-unavailable";
            return true;
        }
        Context.Screens.Push(new BattleInventoryScreen(Context, preBattle: false));
        return true;
    }

    private void Scroll(int dx, int dy)
    {
        var center = Model.Camera.Center;
        Model.Camera.CenterOn(center with { X = center.X + dx, Y = center.Y + dy, Z = Model.Camera.ViewLevel });
    }

    private void Report(bool done)
    {
        // Model.Message is cleared by the buttons that are silent no-ops, so it only holds the rules' code of a refused command.
        if (!done) Message = Model.Message;
    }

    protected override void Draw()
    {
        DrawMap();
        DrawPanel();
    }

    private void DrawMap()
    {
        if (Model.Overview is not { } overview || Model.Level is not { } level)
        {
            Text("No battle", 16, 16, BattleUiContext.Color.Orange);
            return;
        }
        var size = TileSize;
        Frame.FillRectangle(0, 0, overview.Width * size, overview.Length * size, BattleUiContext.Color.Frame);
        var units = overview.Units.ToDictionary(static unit => unit.Id);
        foreach (var tile in level.Tiles)
        {
            var (x, y) = (tile.Position.X * size, tile.Position.Y * size);
            // The tile's light (Tile::getShade, 0 full light to 15 dark) darkens what the viewer has discovered of it.
            // Map::drawTerrain: a part whose tile floor is not discovered is drawn in shade 16 (black).
            var floorKnown = tile.Parts.Any(static part => part.Part == BattleTilePart.Floor);
            var shade = Math.Clamp(tile.Shade, 0, 15);
            byte Shaded(byte first) => floorKnown ? (byte)(first + shade) : (byte)0;
            foreach (var part in tile.Parts)
            {
                if (Context.Sprites?.Invoke(part.DataSet, part.ObjectIndex) is null) Context.Diagnostics.Add($"missing sprite {part.DataSet}#{part.ObjectIndex}");
                switch (part.Part)
                {
                    case BattleTilePart.Floor: Frame.FillRectangle(x, y, size, size, Shaded(BattleUiContext.Color.FloorShade)); break;
                    case BattleTilePart.WestWall: Frame.FillRectangle(x, y, Math.Max(1, size / 4), size, Shaded(BattleUiContext.Color.WallShade)); break;
                    case BattleTilePart.NorthWall: Frame.FillRectangle(x, y, size, Math.Max(1, size / 4), Shaded(BattleUiContext.Color.WallShade)); break;
                    default: Frame.FillRectangle(x + size / 4, y + size / 4, Math.Max(1, size / 2), Math.Max(1, size / 2), Shaded(BattleUiContext.Color.ObjectShade)); break;
                }
            }
            if (tile.Smoke > 0) Frame.FillRectangle(x, y, size, 1, BattleUiContext.Color.Smoke);
            if (tile.Fire > 0) Frame.FillRectangle(x, y + size - 1, size, 1, BattleUiContext.Color.Fire);
            if (tile.Items.Count > 0) Frame.FillRectangle(x + size - 2, y + size - 2, 2, 2, BattleUiContext.Color.Item);
            if (tile.UnitId is not { } unit) continue;
            var color = units.TryGetValue(unit, out var view)
                ? view.Faction switch { BattleFaction.Player => BattleUiContext.Color.Xcom, BattleFaction.Hostile => BattleUiContext.Color.Hostile, _ => BattleUiContext.Color.Neutral }
                : BattleUiContext.Color.Neutral;
            Frame.FillRectangle(x + 1, y + 1, Math.Max(1, size - 2), Math.Max(1, size - 2), color);
            if (unit == Model.SelectedUnitId) DrawBox(x, y, size, BattleUiContext.Color.Selected);
        }
        DrawReachable(size);
        DrawPath(size);
        DrawReplay(size);
        DrawAimMarks(size);
        var center = Model.Camera.Center;
        if (center.X >= 0 && center.X < overview.Width && center.Y >= 0 && center.Y < overview.Length)
            DrawBox(center.X * size, center.Y * size, size, BattleUiContext.Color.Camera);
    }

    /// <summary>The tiles of the camera's level the selected unit can reach, as a dot each.</summary>
    private void DrawReachable(int size)
    {
        if (Model.Reachable is not { } reachable) return;
        foreach (var position in reachable.Tiles)
            if (position.Z == Model.Camera.ViewLevel)
                Dot(position, size, BattleUiContext.Color.ReachableTile);
    }

    /// <summary>
    /// The previewed path of the camera's level: a marker on each step tile in the colour of its affordability, (<c>refreshPath</c> colours; the amounts left are in the view, the panel shows the walk cost).
    /// </summary>
    private void DrawPath(int size)
    {
        if (Model.Path is not { Steps.Count: > 0 } path) return;
        foreach (var step in path.Steps)
        {
            if (step.Position.Z != Model.Camera.ViewLevel) continue;
            var color = step.Marker switch
            {
                BattlePathMarker.Affordable => BattleUiContext.Color.PathAffordable,
                BattlePathMarker.ReservedTimeUnits => BattleUiContext.Color.PathReserved,
                _ => BattleUiContext.Color.PathUnaffordable,
            };
            Frame.FillRectangle(step.Position.X * size + size / 4, step.Position.Y * size + size / 4, Math.Max(1, size / 2), Math.Max(1, size / 2), color);
        }
    }

    /// <summary>The replayed steps so far: a dot on each tile stepped on and a box on the tile of the frame played last.</summary>
    private void DrawReplay(int size)
    {
        for (var index = 0; index < _replayAt; index++)
            if (_replay[index].Position is { } position && position.Z == Model.Camera.ViewLevel)
                Dot(position, size, BattleUiContext.Color.Selected);
        if (ReplayFrame is { Position: { } now } && now.Z == Model.Camera.ViewLevel) DrawBox(now.X * size, now.Y * size, size, BattleUiContext.Color.Selected);
        // A shot's end and a blast's centre: where the frame played last landed.
        if (ReplayFrame is { Marker: { } landed } && landed.Z == Model.Camera.ViewLevel) DrawBox(landed.X * size, landed.Y * size, size, BattleUiContext.Color.Fire);
    }

    /// <summary>The tile aimed at (green when a shot could be taken, orange when it could not) and the launch's waypoints.</summary>
    private void DrawAimMarks(int size)
    {
        if (Model.Aim is { } aim && aim.Target.Z == Model.Camera.ViewLevel)
            DrawBox(aim.Target.X * size, aim.Target.Y * size, size, aim.Usable && aim.CanPay && aim.InRange && aim.HasLineOfFire ? BattleUiContext.Color.PathAffordable : BattleUiContext.Color.PathUnaffordable);
        foreach (var waypoint in Model.Waypoints)
            if (waypoint.Z == Model.Camera.ViewLevel) DrawBox(waypoint.X * size, waypoint.Y * size, size, BattleUiContext.Color.PathAffordable);
    }

    /// <summary>A one-pixel marker in the middle of a tile.</summary>
    private void Dot(BattlePosition position, int size, byte color) =>
        Frame.FillRectangle(position.X * size + size / 2, position.Y * size + size / 2, 1, 1, color);

    private void DrawBox(int x, int y, int size, byte color)
    {
        Frame.DrawLine(x, y, x + size - 1, y, color);
        Frame.DrawLine(x, y + size - 1, x + size - 1, y + size - 1, color);
        Frame.DrawLine(x, y, x, y + size - 1, color);
        Frame.DrawLine(x + size - 1, y, x + size - 1, y + size - 1, color);
    }

    private void DrawPanel()
    {
        const int x = MapWidth + 8;
        Frame.FillRectangle(MapWidth, 0, 640 - MapWidth, 400, BattleUiContext.Color.Panel);
        if (Model.Overview is not { } overview) return;
        var y = 6;
        Text($"Turn {overview.Turn}  {overview.Side}", x, y, BattleUiContext.Color.Green);
        y += 12;
        Text($"Level {Model.Camera.ViewLevel + 1}/{overview.Height}{(Model.Camera.ShowAllLayers ? " all" : "")}", x, y);
        y += 16;
        if (Model.SelectedUnit is { } unit)
        {
            Text($"Unit {unit.UnitId} {unit.UnitType}", x, y, BattleUiContext.Color.Green);
            y += 12;
            Text($"TU {unit.TimeUnits}/{unit.MaxTimeUnits}  En {unit.Energy}/{unit.MaxEnergy}", x, y);
            y += 12;
            Text($"HP {unit.Health}/{unit.MaxHealth} W {unit.FatalWounds} Mo {unit.Morale} St {unit.Stun}", x, y,
                unit.FatalWounds > 0 ? BattleUiContext.Color.Orange : BattleUiContext.Color.White);
            y += 12;
            var (rightLine, leftLine) = HandLines(unit);
            Text(rightLine, x, y);
            y += 12;
            Text(leftLine, x, y);
            y += 12;
            Text($"Seen {unit.VisibleUnitIds.Count}  Hurt {unit.WoundedUnitIds.Count}", x, y);
            y += 16;
        }
        else
        {
            Text("No unit selected", x, y);
            y += 16;
        }
        if (Model.Menu is { } menu) DrawMenu(menu, x);
        else if (Model.Actions is { } actions)
            foreach (var action in actions.Actions)
            {
                Text(action.ToString(), x, y);
                y += 11;
            }
        DrawAim(x);
        DrawReserve(x);
        if (Model.Path is { } path)
        {
            Text(path.Steps.Count == 0 ? path.NoPathReason : $"Path {path.Steps.Count} steps  {path.TimeUnitsCost} TU {path.EnergyCost} En", x, 300,
                path.Steps.Count == 0 ? BattleUiContext.Color.Orange : BattleUiContext.Color.Green);
        }
        DrawVisibleButtons(x);
        if (Model.SelectedUnit is { } indicated && (indicated.WoundedUnitIds.Count > 0 || indicated.BurningUnitIds.Count > 0)) Text(IndicatorButtonsCode, x, 336, BattleUiContext.Color.Orange);
        Text("Later: " + string.Join(" ", LaterButtons.Select(static later => later.Key)), x, 348, BattleUiContext.Color.Orange);
        Text("Tab next  K skip  R undo  C center", x, 360);
        Text("X kneel  U/D up/down  G reach  PgUp/PgDn  I inv", x, 372);
        if (Message.Length != 0) Text(Message, x, 388, BattleUiContext.Color.Orange);
    }

    /// <summary>The action menu (<c>ActionMenuState</c>): a line per action with its accuracy and time units, numbered as its key; the reason when the weapon cannot be used.</summary>
    private void DrawMenu(BattleAimMenuView menu, int x)
    {
        for (var index = 0; index < menu.Items.Count; index++)
        {
            var item = menu.Items[index];
            var detail = item.Kind is BattleAimKind.Prime or BattleAimKind.Unprime ? $" {item.TimeUnits} TU, fuse {Model.PrimeTurns} (+/-)" : $" {item.Accuracy}% {item.TimeUnits} TU";
            Text($"{index + 1} {item.Name}{detail}", x, MenuY + index * MenuLineHeight, menu.Usable ? BattleUiContext.Color.Green : BattleUiContext.Color.Orange);
        }
        if (!menu.Usable) Text(menu.Reason, x, MenuY + menu.Items.Count * MenuLineHeight, BattleUiContext.Color.Orange);
    }

    /// <summary>The aim line: what the cursor shows over the tile (accuracy, chance to hit, time units, and why a shot cannot be taken), or the launch's waypoints.</summary>
    private void DrawAim(int x)
    {
        if (Model.Aiming is not { } aiming) return;
        if (aiming.Kind == BattleAimKind.Launch)
        {
            Text($"Launch: {Model.Waypoints.Count} waypoints, N fires", x, AimY, BattleUiContext.Color.Green);
            return;
        }
        if (Model.Aim is not { } aim)
        {
            Text($"{aiming.Kind}: pick a tile", x, AimY, BattleUiContext.Color.Green);
            return;
        }
        var reason = aim.Reason.Length != 0 ? aim.Reason : aim.ShortfallReason.Length != 0 ? aim.ShortfallReason : !aim.InRange ? "STR_OUT_OF_RANGE" : !aim.HasLineOfFire ? "STR_NO_LINE_OF_FIRE" : "";
        Text(reason.Length == 0 ? $"Acc {aim.Accuracy}% Hit {aim.HitChance}% {aim.TimeUnits} TU" : $"{reason} ({aim.TimeUnits} TU)", x, AimY,
            reason.Length == 0 ? BattleUiContext.Color.Green : BattleUiContext.Color.Orange);
    }

    /// <summary>
    /// The panel's two hand lines (<c>drawHandsItems</c>): each names its item and carries the reaction marker; an empty hand shows the special item that is shown in an empty hand, in the left
    /// hand when it is empty and else in the right one.
    /// </summary>
    public static (string Right, string Left) HandLines(BattleUnitInfo unit) =>
        ($"R{HandFlag(unit, right: true)} {unit.RightHand?.ItemType ?? (unit.LeftHand is not null ? unit.ShownEmptyHandItem?.ItemType : null) ?? "-"}",
         $"L{HandFlag(unit, right: false)} {unit.LeftHand?.ItemType ?? unit.ShownEmptyHandItem?.ItemType ?? "-"}");

    /// <summary>A hand's marker for reactions (<c>drawItem</c>'s preferred and disabled flags): * preferred, ! disabled.</summary>
    private static string HandFlag(BattleUnitInfo unit, bool right) =>
        (right ? unit.RightHandDisabledForReactions : unit.LeftHandDisabledForReactions) ? "!" :
        unit.PreferredHandForReactions == (right ? "STR_RIGHT_HAND" : "STR_LEFT_HAND") ? "*" : "";

    /// <summary>The reserve buttons (<c>_btnReserveNone</c> and the others): none, snap, aimed, auto and kneeling, the one in force marked.</summary>
    private void DrawReserve(int x)
    {
        if (Model.SelectedUnit is null || Model.Selection is not { } selection) return;
        string[] labels = ["-", "S", "A", "F", "K"];
        for (var index = 0; index < labels.Length; index++)
        {
            var on = index == 4 ? selection.KneelReserved : (int)selection.TimeUnitReserve == index;
            var bx = x + index * (ReserveWidth + 2);
            Frame.FillRectangle(bx, ReserveY, ReserveWidth, ReserveHeight, on ? BattleUiContext.Color.Xcom : BattleUiContext.Color.Frame);
            Text(labels[index], bx + 7, ReserveY + 2);
        }
        if (Model.SelectedUnit is { SpecialButtonShown: true })
        {
            Frame.FillRectangle(SpecialX, ReserveY, ReserveWidth, ReserveHeight, BattleUiContext.Color.Frame);
            Text("Y", SpecialX + 7, ReserveY + 2);
        }
    }

    /// <summary>The visible-unit buttons (<c>_btnVisibleUnit</c>): the units the selected unit's side sees, numbered as their keys.</summary>
    private void DrawVisibleButtons(int x)
    {
        if (Model.SelectedUnit is not { } unit) return;
        for (var index = 0; index < VisibleButtonCount(unit); index++)
        {
            var (bx, by) = (x + index % VisibleButtonColumns * (VisibleButtonWidth + 2), VisibleButtonsY + index / VisibleButtonColumns * (VisibleButtonHeight + 2));
            Frame.FillRectangle(bx, by, VisibleButtonWidth, VisibleButtonHeight, BattleUiContext.Color.Hostile);
            Text(index < VisibleKeys ? ((index + 1) % VisibleKeys).ToString(System.Globalization.CultureInfo.InvariantCulture) : "+", bx + 6, by + 2);
        }
    }
}
