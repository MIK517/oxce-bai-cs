using System.Globalization;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;

namespace Oxce.Engine.Geoscape;

/// <summary>
/// The interception windows (DogfightState) over the globe and the dogfight timer of
/// <c>GeoscapeState::think</c> (D3): while any window is open the dogfight timer steps every open
/// interception every <c>dogfightSpeed</c>; game time runs only when every window is minimized and no
/// popup is up. Windows are drawn as text panels: the reference's craft and UFO sprites, range bar and
/// weapon icons are not drawn yet.
/// </summary>
public sealed partial class GeoscapeClient
{
    /// <summary>Options::dogfightSpeed default.</summary>
    public static readonly TimeSpan DogfightClock = TimeSpan.FromMilliseconds(30);
    private const int DogfightWidth = 248;
    private const int DogfightHeight = 190;
    private const int DogfightButtonHeight = 14;
    private const int MinimizedWidth = 72;
    private TimeSpan _dogfightElapsed;
    private IReadOnlyList<CampaignInterception> _interceptions = [];

    /// <summary>The open interceptions, minimized or not, in window-number order.</summary>
    public IReadOnlyList<CampaignInterception> Interceptions => _interceptions;

    private bool InterceptionWindowsOpen => _interceptions.Any(static window => !window.Minimized);

    // SelectDestinationState, BuildNewBaseState, base naming and the base screens cover the geoscape: its
    // think, and with it the dogfight timer and windows, wait beneath them.
    private bool GeoscapeCovered => PopupShown || _bases is not null || _naming is not null || _selecting is not null ||
        _placing is not null;

    private void RefreshInterceptions() =>
        _interceptions = [.. (_session.Interceptions?.QueryInterceptions() ?? []).OrderBy(static window => window.Number)];

    /// <summary>
    /// GeoscapeState::think's dogfight branch: one <see cref="AdvanceInterceptions"/> per dogfight clock
    /// while interceptions exist and nothing covers the geoscape. A slow host does not bank more than one
    /// extra step.
    /// </summary>
    private bool TickInterceptions(TimeSpan elapsed)
    {
        if (_interceptions.Count == 0 || GeoscapeCovered)
        {
            _dogfightElapsed = TimeSpan.Zero;
            return false;
        }
        _dogfightElapsed = TimeSpan.FromTicks(Math.Min((_dogfightElapsed + elapsed).Ticks, DogfightClock.Ticks * 2));
        var stepped = false;
        while (_dogfightElapsed >= DogfightClock && _interceptions.Count != 0 && !PopupShown)
        {
            _dogfightElapsed -= DogfightClock;
            ExecuteInterception(new AdvanceInterceptions());
            stepped = true;
        }
        return stepped;
    }

    private void ExecuteInterception(ICampaignCommand command)
    {
        CampaignCommandResult result;
        try
        {
            result = _session.Commands.Execute(command);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or InvalidDataException or OverflowException)
        {
            _status = error.Message;
            return;
        }
        Refresh();
        if (result.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blocked)
        {
            // A refused step stops the dogfight timer behind its message, as a popup does.
            Push(new GeoscapePopup(RefusalText(blocked, command, _world), [],
                [new(_setup.Localize("STR_OK"), OkKey: true, CancelKey: true)]));
            return;
        }
        Notify(result.Events);
    }

    /// <summary>DogfightState::calculateWindowPosition, laid out for this screen: four slots in a grid.</summary>
    private static (int X, int Y) WindowOrigin(int number)
    {
        var slot = Math.Clamp(number, 1, 4) - 1;
        return (8 + slot % 2 * (DogfightWidth + 8), 8 + slot / 2 * (DogfightHeight + 8));
    }

    private readonly record struct WindowButton(int X, int Y, int Width, string Label, ICampaignCommand? Command, bool Active);

    private IEnumerable<WindowButton> WindowButtons(CampaignInterception window)
    {
        var (x, y) = WindowOrigin(window.Number);
        var right = x + DogfightWidth - 96;
        var top = y + 28;
        (InterceptionMode Mode, string Key)[] modes =
        [
            (InterceptionMode.Standoff, "STR_STANDOFF"), (InterceptionMode.Cautious, "STR_CAUTIOUS_ATTACK"),
            (InterceptionMode.Standard, "STR_STANDARD_ATTACK"), (InterceptionMode.Aggressive, "STR_AGGRESSIVE_ATTACK"),
            (InterceptionMode.Disengage, "STR_DISENGAGE"),
        ];
        for (var index = 0; index < modes.Length; index++)
        {
            var (mode, key) = modes[index];
            var offered = window.AvailableModes.Contains(mode);
            yield return new(right, top + index * (DogfightButtonHeight + 2), 88, _setup.Localize(key),
                offered ? new SetInterceptionMode(window.Number, mode) : null, window.Mode == mode);
        }
        // The minimize button; a defenceless craft's button arms self-destruct instead.
        var minimize = window.CraftDefenseless ? window.SelfDestructPressed ? "Self-destruct armed" : "Self-destruct" : "Minimise";
        yield return new(right, top + modes.Length * (DogfightButtonHeight + 2) + 6, 88, minimize,
            new MinimizeInterception(window.Number), window.SelfDestructPressed);
        for (var index = 0; index < window.Weapons.Count; index++)
        {
            var weapon = window.Weapons[index];
            yield return new(x + 8, y + DogfightHeight - 16 - (window.Weapons.Count - index) * (DogfightButtonHeight + 2), right - x - 16,
                string.Create(CultureInfo.InvariantCulture, $"{_setup.Localize(weapon.RuleId)} {weapon.Ammo}{(weapon.Enabled ? "" : " (off)")}"),
                new ToggleInterceptionWeapon(window.Number, weapon.Slot), weapon.Enabled);
        }
    }

    private void DrawInterceptions()
    {
        if (_selecting is not null || _placing is not null) return;
        var minimized = 0;
        foreach (var window in _interceptions)
        {
            if (window.Minimized)
            {
                // The minimized icon on the globe's edge, with the craft's distance to its UFO.
                var iconY = Height - 24 - minimized++ * 20;
                _frame.FillRectangle(4, iconY, MinimizedWidth, 16, ButtonColor);
                Text(window.Distance.ToString(CultureInfo.InvariantCulture), 8, iconY + 3, 0);
                continue;
            }
            var (x, y) = WindowOrigin(window.Number);
            _frame.FillRectangle(x, y, DogfightWidth, DogfightHeight, 0);
            _frame.FillRectangle(x, y, DogfightWidth, 2, HighlightColor);
            _frame.FillRectangle(x, y + DogfightHeight - 2, DogfightWidth, 2, HighlightColor);
            _frame.FillRectangle(x, y, 2, DogfightHeight, HighlightColor);
            _frame.FillRectangle(x + DogfightWidth - 2, y, 2, DogfightHeight, HighlightColor);
            var craft = _world.Crafts.FirstOrDefault(entry => entry.BaseId == window.BaseId && entry.TypeId == window.CraftTypeId &&
                entry.Id == window.CraftId);
            var ufo = _world.Targets.FirstOrDefault(target => target.Kind == WorldTargetKind.Ufo && target.Reference.UniqueId == window.UfoUniqueId);
            Text(craft is null ? GeoscapeScreens.DefaultCraftName(window.CraftTypeId, window.CraftId, _setup.Localize)
                : GeoscapeScreens.CraftName(craft, _setup.Localize), x + 8, y + 6, HighlightColor);
            Text(ufo is null ? "UFO" : GeoscapeScreens.TargetLabel(ufo, _setup.Localize), x + 8, y + 16, HighlightColor);
            Text(string.Create(CultureInfo.InvariantCulture, $"{window.Distance} / {window.TargetDistance}"), x + 8, y + 32);
            Wrap(_setup.Localize(window.Status), x + 8, y + 46, DogfightWidth - 112);
            foreach (var button in WindowButtons(window))
            {
                var color = button.Command is null ? (byte)0 : button.Active ? HighlightColor : ButtonColor;
                _frame.FillRectangle(button.X, button.Y, button.Width, DogfightButtonHeight, color);
                Text(button.Label, button.X + 3, button.Y + 3, button.Command is null ? TextColor : (byte)0);
            }
        }
    }

    /// <summary>A press on an interception window or a minimized icon; true when it was one of them.</summary>
    private bool HandleInterceptionPress(in GameInputEvent input)
    {
        if (_interceptions.Count == 0 || input.PointerButton != 1 || GeoscapeCovered) return false;
        int x = (int)input.X, y = (int)input.Y;
        var minimized = 0;
        foreach (var window in _interceptions)
        {
            if (!window.Minimized) continue;
            var iconY = Height - 24 - minimized++ * 20;
            if (x >= 4 && x < 4 + MinimizedWidth && y >= iconY && y < iconY + 16)
            {
                ExecuteInterception(new RestoreInterception(window.Number));
                Redraw();
                return true;
            }
        }
        foreach (var window in _interceptions.Reverse())
        {
            if (window.Minimized) continue;
            var (wx, wy) = WindowOrigin(window.Number);
            if (x < wx || x >= wx + DogfightWidth || y < wy || y >= wy + DogfightHeight) continue;
            foreach (var button in WindowButtons(window))
                if (button.Command is { } command && x >= button.X && x < button.X + button.Width &&
                    y >= button.Y && y < button.Y + DogfightButtonHeight)
                {
                    ExecuteInterception(command);
                    break;
                }
            // The window covers the globe beneath it.
            Redraw();
            return true;
        }
        return false;
    }
}
