using Oxce.Engine.Input;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;

namespace Oxce.Engine.Battlescape;

/// <summary>
/// The battlescape screen (<c>BattlescapeState</c>) on the indexed loop: the level the camera shows as a flat plan of the tiles the viewer may
/// see, the units and items on them, the selected soldier's bars and hands, and the buttons. It reads <see cref="BattleViewModel"/> and
/// sends commands; the legal actions come from the rules. Buttons whose rules belong to a later branch are refused here by their owner's
/// name (<see cref="LaterButtons"/>) and change nothing. A terrain object without a sprite is drawn as a placeholder and listed in
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
        ('x', "battle.kneel-unsupported", "Phase 7 branch 4"),
        ('w', "battle.walking-unsupported", "Phase 7 branch 4"),
        ('u', "battle.unit-up-unsupported", "Phase 7 branch 4"),
        ('d', "battle.unit-down-unsupported", "Phase 7 branch 4"),
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

    /// <summary>Reference handlers on other inputs, refused by owner: Esc (<c>keyBattleDeselectUnit</c>), Ctrl-B (the info-only briefing), the digits (<c>btnVisibleUnitClick</c>) and the other mouse buttons.</summary>
    public const string DeselectCode = "battle.deselect-unsupported (Phase 7 branch 6)";
    public const string BriefingAgainCode = "battle.briefing-info-unsupported (Phase 8)";
    public const string VisibleUnitCode = "battle.visible-unit-center-unsupported (Phase 7 branch 4)";
    public const string SecondaryActionCode = "battle.secondary-action-unsupported (Phase 7 branch 4)";
    public const string MiddleButtonCode = "battle.middle-button-unsupported (Phase 7 branch 4)";

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

    protected override bool Handle(in GameInputEvent input)
    {
        if (input.Kind == GameInputEventKind.PointerPressed) return Click(input);
        if (input.Kind != GameInputEventKind.KeyPressed || input.IsRepeat) return false;
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
            case >= '0' and <= '9': Message = VisibleUnitCode; return true;
            case KeyPageUp or ']': Report(Model.LevelUp()); return true;
            case KeyPageDown or '[': Report(Model.LevelDown()); return true;
            case 'i': return OpenInventory();
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
        return false;
    }

    private bool Click(in GameInputEvent input)
    {
        Model.Refresh();
        Message = "";
        if (input.X < 0 || input.X >= MapWidth || input.Y < 0 || input.Y >= MapHeight || Model.Overview is null) return false;
        var tile = TileSize;
        var (x, y) = ((int)input.X / tile, (int)input.Y / tile);
        // Button 1 is the left one: a click on a unit of the side on turn selects it (primaryAction); a click on a tile is a walk.
        var unit = Model.Level?.Tiles.FirstOrDefault(shown => shown.Position.X == x && shown.Position.Y == y)?.UnitId;
        if (input.PointerButton == 1 && unit is { } id)
        {
            Report(Model.Select(id));
            return true;
        }
        if (input.PointerButton == 3) { Message = SecondaryActionCode; return true; }
        if (input.PointerButton == 2) { Message = MiddleButtonCode; return true; }
        var walking = LaterButtons.First(static later => later.Key == 'w');
        Message = $"{walking.Code} ({walking.Owner})";
        return true;
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
            foreach (var part in tile.Parts)
            {
                if (Context.Sprites?.Invoke(part.DataSet, part.ObjectIndex) is null) Context.Diagnostics.Add($"missing sprite {part.DataSet}#{part.ObjectIndex}");
                switch (part.Part)
                {
                    case BattleTilePart.Floor: Frame.FillRectangle(x, y, size, size, BattleUiContext.Color.Floor); break;
                    case BattleTilePart.WestWall: Frame.FillRectangle(x, y, Math.Max(1, size / 4), size, BattleUiContext.Color.WestWall); break;
                    case BattleTilePart.NorthWall: Frame.FillRectangle(x, y, size, Math.Max(1, size / 4), BattleUiContext.Color.NorthWall); break;
                    default: Frame.FillRectangle(x + size / 4, y + size / 4, Math.Max(1, size / 2), Math.Max(1, size / 2), BattleUiContext.Color.ObjectPart); break;
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
        var center = Model.Camera.Center;
        if (center.X >= 0 && center.X < overview.Width && center.Y >= 0 && center.Y < overview.Length)
            DrawBox(center.X * size, center.Y * size, size, BattleUiContext.Color.Camera);
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
        Text("Later: " + string.Join(" ", LaterButtons.Select(static later => later.Key)), x, 348, BattleUiContext.Color.Orange);
        Text("Tab next  K skip  R undo  C center", x, 360);
        Text("PgUp/PgDn level  I inventory", x, 372);
        if (Message.Length != 0) Text(Message, x, 388, BattleUiContext.Color.Orange);
    }
}
