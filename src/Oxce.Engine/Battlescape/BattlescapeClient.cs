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
        ('f', "battle.shooting-unsupported", "Phase 7 branch 5"),
        ('q', "battle.left-hand-item-unsupported", "Phase 7 branch 5"),
        ('t', "battle.right-hand-item-unsupported", "Phase 7 branch 5"),
        ('p', "battle.psi-unsupported", "Phase 7 branch 5"),
        ('y', "battle.special-unsupported", "Phase 7 branch 5"),
        ('j', "battle.skills-unsupported", "Phase 7 branch 5"),
        ('n', "battle.launch-unsupported", "Phase 7 branch 5"),
        ('v', "battle.reserve-time-units-unsupported", "Phase 7 branch 5"),
        ('b', "battle.reserve-kneel-unsupported", "Phase 7 branch 5"),
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
        switch (key)
        {
            case KeyTab: Report(Model.Next(backwards: shift)); return true;
            case 'k': Report(shift ? Model.Skip(byDistance: true) : Model.Skip()); return true;
            case 'r': Report(Model.ReselectSkipped()); return true;
            case 'c' when (input.Modifiers & InputKeyModifiers.Control) != 0: Model.Camera.ToggleShowAllLayers(); return true;
            case 'c' or HomeKey: Report(Model.CenterOnSelection()); return true;
            case 'b' when (input.Modifiers & InputKeyModifiers.Control) != 0: Message = BriefingAgainCode; return true;
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
        var before = (Model.Path, Model.Reachable);
        if (TileAt(input) is var (x, y)) Model.Preview(x, y);
        else Model.Preview(null, null);
        return !ReferenceEquals(before.Path, Model.Path);
    }

    private bool Click(in GameInputEvent input)
    {
        Model.Refresh();
        Message = "";
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

    private int? VisibleButtonAt(in GameInputEvent input)
    {
        if (Model.SelectedUnit is not { } unit) return null;
        var (px, py) = ((int)input.X - VisibleButtonsX, (int)input.Y - VisibleButtonsY);
        if (px < 0 || py < 0) return null;
        var (column, row) = (px / (VisibleButtonWidth + 2), py / (VisibleButtonHeight + 2));
        if (column >= VisibleButtonColumns || px % (VisibleButtonWidth + 2) >= VisibleButtonWidth || py % (VisibleButtonHeight + 2) >= VisibleButtonHeight) return null;
        var index = row * VisibleButtonColumns + column;
        return index < Math.Min(VisibleMax, unit.VisibleUnitIds.Count) ? index : null;
    }

    /// <summary>The visible-unit key, or button: the camera centers on the unit; the right click form selects the unit that sees it best.</summary>
    private void VisibleUnit(int index, bool right)
    {
        Model.Refresh();
        if (Model.SelectedUnit is not { } unit || index >= Math.Min(VisibleMax, unit.VisibleUnitIds.Count)) return;
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
            foreach (var part in tile.Parts)
            {
                if (Context.Sprites?.Invoke(part.DataSet, part.ObjectIndex) is null) Context.Diagnostics.Add($"missing sprite {part.DataSet}#{part.ObjectIndex}");
                switch (part.Part)
                {
                    case BattleTilePart.Floor: Frame.FillRectangle(x, y, size, size, floorKnown ? (byte)(BattleUiContext.Color.FloorShade + shade) : (byte)0); break;
                    case BattleTilePart.WestWall: Frame.FillRectangle(x, y, Math.Max(1, size / 4), size, floorKnown ? (byte)(BattleUiContext.Color.WallShade + shade) : (byte)0); break;
                    case BattleTilePart.NorthWall: Frame.FillRectangle(x, y, size, Math.Max(1, size / 4), floorKnown ? (byte)(BattleUiContext.Color.WallShade + shade) : (byte)0); break;
                    default: Frame.FillRectangle(x + size / 4, y + size / 4, Math.Max(1, size / 2), Math.Max(1, size / 2), floorKnown ? (byte)(BattleUiContext.Color.ObjectShade + shade) : (byte)0); break;
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
                Frame.FillRectangle(position.X * size + size / 2, position.Y * size + size / 2, 1, 1, BattleUiContext.Color.ReachableTile);
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
                Frame.FillRectangle(position.X * size + size / 2, position.Y * size + size / 2, 1, 1, BattleUiContext.Color.Selected);
        if (ReplayFrame is { Position: { } now } && now.Z == Model.Camera.ViewLevel) DrawBox(now.X * size, now.Y * size, size, BattleUiContext.Color.Selected);
    }

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
            Text($"HP {unit.Health}/{unit.MaxHealth}  Mo {unit.Morale}  St {unit.Stun}", x, y);
            y += 12;
            Text($"R {unit.RightHand?.ItemType ?? "-"}", x, y);
            y += 12;
            Text($"L {unit.LeftHand?.ItemType ?? "-"}", x, y);
            y += 12;
            Text($"Seen {unit.VisibleUnitIds.Count}  Hurt {unit.WoundedUnitIds.Count}", x, y);
            y += 16;
        }
        else
        {
            Text("No unit selected", x, y);
            y += 16;
        }
        if (Model.Actions is { } actions)
            foreach (var action in actions.Actions)
            {
                Text(action.ToString(), x, y);
                y += 11;
            }
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

    /// <summary>The visible-unit buttons (<c>_btnVisibleUnit</c>): the units the selected unit's side sees, numbered as their keys.</summary>
    private void DrawVisibleButtons(int x)
    {
        if (Model.SelectedUnit is not { } unit) return;
        for (var index = 0; index < Math.Min(VisibleMax, unit.VisibleUnitIds.Count); index++)
        {
            var (bx, by) = (x + index % VisibleButtonColumns * (VisibleButtonWidth + 2), VisibleButtonsY + index / VisibleButtonColumns * (VisibleButtonHeight + 2));
            Frame.FillRectangle(bx, by, VisibleButtonWidth, VisibleButtonHeight, BattleUiContext.Color.Hostile);
            Text(index < VisibleKeys ? ((index + 1) % VisibleKeys).ToString(System.Globalization.CultureInfo.InvariantCulture) : "+", bx + 6, by + 2);
        }
    }
}
