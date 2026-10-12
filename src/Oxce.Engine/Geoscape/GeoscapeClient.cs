using System.Diagnostics;
using System.Globalization;
using System.Text;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;

namespace Oxce.Engine.Geoscape;

/// <summary>What a geoscape client needs; persistence and the clock are supplied by the host.</summary>
public sealed record GeoscapeClientSetup(
    CampaignUiSession Session,
    RuntimeRuleCatalog Rules,
    GeoscapeAssets Assets,
    IndexedSpriteFont Font,
    Func<string, string> Localize)
{
    public Action? Save { get; init; }
    public Func<CampaignUiSession>? Load { get; init; }
    public Func<TimeSpan>? Stopwatch { get; init; }
    public GlobeRenderOptions Options { get; init; } = new();
    /// <summary>Options::oxceShowETAMode: 0 (default) no ETA, 1 not for moving targets, 2 always.</summary>
    public int ShowEtaMode { get; init; }
    /// <summary>Options::globeAllRadarsOnBaseBuild (default on): base placement previews every radar on each base.</summary>
    public bool AllRadarsOnBaseBuild { get; init; } = true;
    /// <summary>Opens the base screens; defaults to the indexed logistics client.</summary>
    public Func<CampaignUiSession, IIndexedLoopClient>? OpenBases { get; init; }
}

/// <summary>
/// The geoscape: the globe on the left of a 640x400 frame (the reference 320x200 layout at 2x)
/// and the clock and speed controls on the right. It owns the GeoscapeState behaviour that
/// matters for play: time runs only while no popup, confirmation or base screen is open; popups
/// queue and show one at a time; speed selection, timer resets, quick save and confirmed quick
/// load. Base management opens the existing base screens on top.
/// </summary>
public sealed partial class GeoscapeClient : IIndexedLoopClient
{
    public const int Width = 640;
    public const int Height = 400;
    public const int GlobeWidth = 512;
    public const int PixelScale = 2;
    private const int SidebarLeft = GlobeWidth + 6;
    private const int SpeedTop = 64;
    private const int SpeedHeight = 18;
    private const byte TextColor = 138;
    private const byte ButtonColor = 133;
    private const byte HighlightColor = 239;
    private static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(100);
    private static readonly string[] SpeedLabels =
        ["STR_5_SECONDS", "STR_1_MINUTE", "STR_5_MINUTES", "STR_30_MINUTES", "STR_1_HOUR", "STR_1_DAY"];

    private readonly GeoscapeClientSetup _setup;
    private readonly IndexedSurface _frame = new(Width, Height);
    private readonly GlobeRenderer _renderer;
    private readonly GeoscapeTimeController _time;
    private readonly List<PopupEntry> _popups = [];
    private CampaignUiSession _session;
    private CampaignWorldOverview _world;
    private CampaignOverview _overview;
    private bool _worldReady;
    private GlobeScene _scene = GlobeScene.Empty;
    private GlobeTargets _targets = GlobeTargets.Empty;
    private IIndexedLoopClient? _bases;
    private GlobeTarget? _selected;
    private GlobeRenderOptions _options;
    private string _status = "";
    private string? _entryBlocked;
    private long _revision;
    private TimeSpan _blinkElapsed;
    private TimeSpan _rotationElapsed;
    private bool _blinkOn;
    private int _rotateLongitude;
    private int _rotateLatitude;
    private bool _dragging;
    private bool _globeDirty = true;
    private bool _buildNewBase;

    public GeoscapeClient(GeoscapeClientSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _setup = setup;
        _session = setup.Session;
        if (_session.World is null) throw new ArgumentException("The geoscape needs the campaign world query.", nameof(setup));
        _options = setup.Options;
        View = new GlobeView(GlobeWidth, Height, PixelScale);
        _renderer = new GlobeRenderer(View, setup.Assets, setup.Rules.Campaign.Globe, setup.Font);
        var stopwatch = setup.Stopwatch ?? CreateStopwatch();
        _time = new GeoscapeTimeController(stopwatch);
        _world = _session.World.QueryWorld();
        _overview = _session.Queries.QueryOverview();
        ApplyView(_session.GlobeView);
        EnterWorld();
        Refresh();
        Redraw();
    }

    /// <summary>The globe's centre and zoom, for SavedGame's globeLon/globeLat/globeZoom.</summary>
    public GlobeViewState CurrentView => new(View.CenterLongitude, View.CenterLatitude, View.Zoom);

    /// <summary>
    /// Globe's constructor: centre and zoom from the save. Globe::setZoom takes a size_t, so a
    /// negative saved zoom wraps to the closest view and a larger one is clamped to it.
    /// </summary>
    private void ApplyView(GlobeViewState? view)
    {
        if (view is null) return;
        if (double.IsFinite(view.Longitude) && double.IsFinite(view.Latitude)) View.Center(view.Longitude, view.Latitude);
        View.SetZoom(view.Zoom < 0 ? GlobeView.ZoomLevels - 1 : view.Zoom);
        _globeDirty = true;
    }

    public IndexedSurface Frame => _bases?.Frame ?? _frame;
    public IndexedPalette Palette => _bases?.Palette ?? _setup.Assets.Palette;
    public long PresentationRevision => _bases is null ? _revision : checked(_revision + _bases.PresentationRevision);
    public bool ExitRequested { get; private set; }

    public GlobeView View { get; }
    public GeoscapeTimeController Time => _time;
    public CampaignWorldOverview World => _world;
    public GlobeTarget? Selected => _selected;
    public IReadOnlyList<GeoscapePopup> Popups => _popups.Select(static entry => entry.Popup).ToArray();
    public bool BasesOpen => _bases is not null;
    public string Status => _status;
    public GlobeRenderOptions Options => _options;

    /// <summary>Time may run: no popup, destination choice or screen above the globe and the world has been entered.</summary>
    public bool TimeCanRun => _popups.Count == 0 && _bases is null && _selecting is null && _placing is null &&
        _naming is null && _worldReady;

    public void Tick(TimeSpan fixedInterval)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fixedInterval, TimeSpan.Zero);
        if (_bases is not null)
        {
            _bases.Tick(fixedInterval);
            if (_bases.ExitRequested) CloseBases();
            return;
        }
        var redraw = TickHover(fixedInterval);
        _blinkElapsed += fixedInterval;
        while (_blinkElapsed >= BlinkInterval)
        {
            _blinkElapsed -= BlinkInterval;
            _blinkOn = !_blinkOn;
            _globeDirty = redraw = true;
        }
        if ((_rotateLongitude != 0 || _rotateLatitude != 0) && !PopupShown)
        {
            _rotationElapsed += fixedInterval;
            while (_rotationElapsed >= GlobeView.RotationInterval)
            {
                _rotationElapsed -= GlobeView.RotationInterval;
                View.Rotate(_rotateLongitude, _rotateLatitude);
                _globeDirty = redraw = true;
            }
        }
        if (TimeCanRun)
        {
            GeoscapeTimeStep step;
            try
            {
                step = _time.Tick(fixedInterval, _session.Commands);
            }
            catch (Exception error) when (error is InvalidOperationException or InvalidDataException or OverflowException)
            {
                // A failure after the transaction gate may leave the tick partly applied.
                _time.Halt("Time stopped after an internal error; load a save to continue. " + error.Message);
                Refresh();
                Redraw();
                return;
            }
            // GeoscapeState::timeAdvance redraws once its step ends or a popup interrupts it.
            if (step.Events.Count > 0 || (step.Ticks > 0 && _time.Owed == 0))
            {
                Refresh();
                Notify(step.Events);
                redraw = true;
            }
        }
        if (redraw) Redraw();
    }

    public void HandleInput(in GameInputEvent input)
    {
        if (input.Kind == GameInputEventKind.PointerReleased && input.PointerButton == 2) _dragging = false;
        if (input.Kind == GameInputEventKind.FocusLost) _shift = false;
        _keyText = input.Kind == GameInputEventKind.KeyPressed && input.KeyCode is >= 32 and < 127
            ? ((char)input.KeyCode).ToString()
            : null;
        if (input.Kind is GameInputEventKind.KeyPressed or GameInputEventKind.KeyReleased)
            _shift = (input.Modifiers & InputKeyModifiers.Shift) != 0 ||
                (input.Kind == GameInputEventKind.KeyPressed && input.KeyCode is ShiftLeft or ShiftRight);
        if (_bases is not null)
        {
            _bases.HandleInput(input);
            if (_bases.ExitRequested) CloseBases();
            return;
        }
        if (_naming is not null)
        {
            HandleNamingInput(input);
            Redraw();
            return;
        }
        if (PopupShown)
        {
            HandlePopupInput(input);
            Redraw();
            return;
        }
        switch (input.Kind)
        {
            case GameInputEventKind.KeyPressed when !input.IsRepeat:
                HandleKey(input.KeyCode, pressed: true);
                break;
            case GameInputEventKind.KeyReleased:
                HandleKey(input.KeyCode, pressed: false);
                break;
            case GameInputEventKind.PointerWheel when input.X < GlobeWidth:
                if (input.DeltaY > 0) View.ZoomIn();
                else if (input.DeltaY < 0) View.ZoomOut();
                _globeDirty = true;
                break;
            case GameInputEventKind.PointerPressed:
                HandlePress(input);
                break;
            case GameInputEventKind.PointerReleased when input.PointerButton == 2:
                _dragging = false;
                break;
            case GameInputEventKind.PointerMoved when _placing is not null && !_dragging && input.X < GlobeWidth:
                // BuildNewBaseState::globeHover: the radar preview follows the pointer.
                HoverBaseSite((int)input.X, (int)input.Y);
                break;
            case GameInputEventKind.PointerMoved when _dragging:
                // Globe::mouseOver drag scrolling, measured in reference pixels.
                var factor = 1.0 / (View.Zoom + 1) / 2 / (GlobeView.DefaultScrollSpeed / 10.0) / PixelScale;
                View.Center(View.CenterLongitude - input.DeltaX * GlobeView.RotateLongitude * factor,
                    View.CenterLatitude - input.DeltaY * GlobeView.RotateLatitude * factor);
                _globeDirty = true;
                break;
            default:
                return;
        }
        Redraw();
    }

    private void HandleKey(uint key, bool pressed)
    {
        switch (key)
        {
            case 0x40000050: _rotateLongitude = pressed ? -1 : 0; return; // left
            case 0x4000004F: _rotateLongitude = pressed ? 1 : 0; return; // right
            case 0x40000052: _rotateLatitude = pressed ? -1 : 0; return; // up
            case 0x40000051: _rotateLatitude = pressed ? 1 : 0; return; // down
        }
        if (!pressed) return;
        if (_selecting is not null || _placing is not null)
        {
            if (_placing is not null && key == 27) CancelBasePlacement();
            else HandleSelectionKey(key);
            return;
        }
        switch (key)
        {
            case >= '1' and <= '6':
                _time.Select((CampaignTimeSpeed)(key - '1'));
                break;
            case '+' or '=' or 0x40000057:
                View.ZoomIn();
                _globeDirty = true;
                break;
            case '-' or 0x40000056:
                View.ZoomOut();
                _globeDirty = true;
                break;
            case 9: // Tab: Globe::toggleDetail
                _options = _options with { Detail = !_options.Detail };
                _globeDirty = true;
                break;
            case 'r':
                _options = _options with { RadarLines = !_options.RadarLines };
                _globeDirty = true;
                break;
            case 'b':
                OpenBases();
                break;
            case 'i':
                OpenIntercept(new InterceptIntent(), NewGroup());
                break;
            case 'n':
                // BasescapeState's "Build new base" (BuildNewBaseState).
                BeginBasePlacement();
                break;
            case '.' or ',':
                SelectNext(key == '.' ? 1 : -1);
                break;
            case 13 when _selected is not null:
                View.Center(_selected.Longitude, _selected.Latitude);
                OpenTarget(_selected);
                _globeDirty = true;
                break;
            case 0x4000003E: // F5: GeoscapeState quick save
                if (_setup.Save is null) _status = "Save is unavailable for this session.";
                else Guard(() =>
                {
                    // A step spread over frames keeps running after the save; the save holds the ticks done so far.
                    _setup.Save();
                    _status = "Campaign saved.";
                });
                break;
            case 0x40000042: // F9: quick load after confirmation
                if (_setup.Load is null) _status = "Load is unavailable for this session.";
                else Enqueue(new GeoscapePopup("Load saved campaign?", ["Unsaved progress will be lost."],
                    [new(_setup.Localize("STR_OK"), OkKey: true), new(_setup.Localize("STR_CANCEL_UC"), CancelKey: true)]),
                    choice =>
                    {
                        if (choice == 0) Guard(LoadCampaign);
                    });
                break;
            case 27:
                ExitRequested = true;
                break;
        }
    }

    private void HandlePress(in GameInputEvent input)
    {
        int x = (int)input.X, y = (int)input.Y;
        if (x >= GlobeWidth)
        {
            if (_placing is not null)
            {
                if (input.PointerButton == 1 && y >= CancelTop && y < CancelTop + SpeedHeight) CancelBasePlacement();
                return;
            }
            if (_selecting is not null)
            {
                // SelectDestinationState's Cancel button.
                if (input.PointerButton == 1 && y >= CancelTop && y < CancelTop + SpeedHeight) CancelSelection();
                return;
            }
            var row = (y - SpeedTop) / SpeedHeight;
            if (input.PointerButton == 1 && y >= SpeedTop && row < SpeedLabels.Length) _time.Select((CampaignTimeSpeed)row);
            else if (input.PointerButton == 1 && y >= InterceptTop && y < InterceptTop + SpeedHeight)
                OpenIntercept(new InterceptIntent(), NewGroup());
            return;
        }
        if (input.PointerButton == 2)
        {
            _dragging = true;
            return;
        }
        if (!View.TryUnproject(x, y, out var longitude, out var latitude)) return;
        if (input.PointerButton == 3)
        {
            // Globe::mouseClick: the right button centres the globe.
            View.Center(longitude, latitude);
            _globeDirty = true;
            return;
        }
        if (input.PointerButton != 1) return;
        if (_placing is not null)
        {
            ChooseBaseSite(longitude, latitude);
            return;
        }
        if (_selecting is not null)
        {
            SelectDestinationAt(x, y, longitude, latitude);
            return;
        }
        var near = _targets.Near(View, x, y);
        if (near.Count == 0) return;
        _selected = near[0];
        if (near.Count == 1) OpenTarget(near[0]);
        else ChooseTarget(near, target =>
        {
            _selected = target;
            OpenTarget(target);
        });
    }

    /// <summary>MultipleTargetsState: one button per target, then cancel.</summary>
    private void ChooseTarget(IReadOnlyList<GlobeTarget> near, Action<GlobeTarget> chosen, int group = 0)
    {
        // The popup has room for this many buttons; the reference scrolls a longer list.
        if (near.Count > MaximumTargetChoices) near = near.Take(MaximumTargetChoices).ToArray();
        var actions = near.Select(target => new GeoscapePopupAction(target.Label)).Append(
            new GeoscapePopupAction(_setup.Localize("STR_CANCEL_UC"), CancelKey: true)).ToArray();
        Push(new GeoscapePopup(_setup.Localize("STR_TARGETS"), [], actions), choice =>
        {
            if (choice < near.Count) chosen(near[choice]);
        }, group);
    }

    private void HandlePopupInput(in GameInputEvent input)
    {
        var popup = _popups[0].Popup;
        var count = popup.Actions.Count;
        int choice;
        if (input.Kind == GameInputEventKind.KeyPressed && !input.IsRepeat)
        {
            choice = input.KeyCode switch
            {
                13 => IndexOf(popup, static action => action.OkKey),
                27 => IndexOf(popup, static action => action.CancelKey),
                >= '1' and <= '9' when input.KeyCode - '1' < count => (int)(input.KeyCode - '1'),
                _ => -1,
            };
        }
        else if (input.Kind == GameInputEventKind.PointerPressed && input.PointerButton == 1)
            choice = PopupButtonAt(popup, (int)input.X, (int)input.Y);
        else return;
        if (choice < 0) return;
        Choose(choice, _shift || (input.Modifiers & InputKeyModifiers.Shift) != 0);
    }

    private static int IndexOf(GeoscapePopup popup, Func<GeoscapePopupAction, bool> binding)
    {
        for (var index = 0; index < popup.Actions.Count; index++)
            if (binding(popup.Actions[index])) return index;
        return -1;
    }

    /// <summary>
    /// Applies a popup button: speed reset, centring, the popup's own callback, then the button's
    /// command or the screen it opens. A disabled button keeps the popup open and reports why.
    /// </summary>
    public void Choose(int choice, bool shift = false)
    {
        if (_popups.Count == 0) throw new InvalidOperationException("No popup is open.");
        var entry = _popups[0];
        ArgumentOutOfRangeException.ThrowIfNegative(choice);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(choice, entry.Popup.Actions.Count);
        var action = entry.Popup.Actions[choice];
        if (action.Disabled is { } disabled && !(shift && action.Intent is InterceptRowIntent))
        {
            _status = disabled;
            Redraw();
            return;
        }
        _popups.RemoveAt(0);
        if (action.ResetSpeed) _time.Reset();
        if (action.Center is { } center)
        {
            View.Center(center.Longitude, center.Latitude);
            _globeDirty = true;
        }
        entry.Chosen?.Invoke(choice);
        if (action.Command is { } command) Execute(command, entry.Group);
        if (action.Intent is { } intent) Open(intent, entry, shift);
        Redraw();
    }

    private void Notify(IReadOnlyList<ICampaignEvent> events)
    {
        foreach (var notification in events)
        {
            if (GeoscapeNotifications.Describe(notification, _world, _setup.Localize, _setup.Rules, _setup.ShowEtaMode) is { } popup)
                Enqueue(popup);
        }
    }

    private void Enqueue(GeoscapePopup popup, Action<int>? chosen = null)
    {
        // GeoscapeState::popup pauses time and stops the globe rotating.
        _popups.Add(new(popup, chosen, 0));
        _rotateLongitude = _rotateLatitude = 0;
        _dragging = false;
        _time.Interrupt();
        if (popup.ResetsSpeedWhenShown) _time.Reset();
    }

    private void SelectNext(int direction)
    {
        var visible = _targets.Visible(View);
        if (visible.Count == 0)
        {
            _selected = null;
            return;
        }
        var index = -1;
        if (_selected is not null)
            for (var position = 0; position < visible.Count; position++)
                if (GlobeTargets.Same(visible[position].Reference, _selected.Reference)) index = position;
        index = index < 0 ? (direction > 0 ? 0 : visible.Count - 1) : (index + direction + visible.Count) % visible.Count;
        _selected = visible[index];
    }

    private void OpenBases(int? baseId = null)
    {
        _rotateLongitude = _rotateLatitude = 0;
        _dragging = false;
        // Time belongs to the geoscape: the base screens may not advance it behind its popups.
        _bases = _setup.OpenBases?.Invoke(_session) ?? new CampaignLogisticsClient(_session, _setup.Font, _setup.Localize,
            _setup.Save, _setup.Load is null ? null : () =>
            {
                var session = _setup.Load();
                Replace(session);
                return session;
            }, advanceTime: false, baseId: baseId, buildNewBase: () => _buildNewBase = true);
        _revision++;
    }

    private void CloseBases()
    {
        if (_bases is null) return;
        _revision = checked(_revision + _bases.PresentationRevision + 1);
        _bases = null;
        // Placing the first base in the base screens enters the world, as GeoscapeState::init does.
        EnterWorld();
        Refresh();
        if (_buildNewBase)
        {
            _buildNewBase = false;
            BeginBasePlacement();
        }
        Redraw();
    }

    private void LoadCampaign()
    {
        Replace(_setup.Load!());
        _status = "Campaign loaded.";
    }

    private void Replace(CampaignUiSession session)
    {
        if (session.World is null) throw new InvalidOperationException("The loaded session has no world query.");
        _session = session;
        // Nothing owed by the old campaign's clock may run on the loaded one.
        _time.Interrupt();
        _popups.Clear();
        _selected = null;
        _selecting = null;
        _confirm = null;
        _intercept = null;
        _placing = null;
        _naming = null;
        _hover = _pendingHover = null;
        _rotateLongitude = _rotateLatitude = 0;
        _dragging = false;
        _buildNewBase = false;
        _suppressText = null;
        _time.Select(CampaignTimeSpeed.FiveSeconds);
        // A loaded save always has a view: SavedGame's defaults are 0, 0, 0.
        ApplyView(session.GlobeView ?? new GlobeViewState(0, 0, 0));
        EnterWorld();
        Refresh();
    }

    private void EnterWorld()
    {
        var result = _session.Commands.Execute(new EnterCampaignWorld());
        _entryBlocked = result.Events.OfType<CampaignActionBlocked>().FirstOrDefault()?.Reason;
        if (_entryBlocked is not null) _status = "World entry is blocked: " + _entryBlocked;
        _world = _session.World!.QueryWorld();
        _overview = _session.Queries.QueryOverview();
        _worldReady = _overview.Bases.Count != 0 && _overview.Bases[0].IsPlaced && _entryBlocked is null;
        // A new game starts by choosing the starting base's site (BuildNewBaseState for the first base).
        if (_placing is null && _overview.Bases.Count != 0 && !_overview.Bases[0].IsPlaced && _session.Readiness is not null)
            BeginBasePlacement();
        Notify(result.Events.Where(static e => e is not CampaignActionBlocked).ToArray());
    }

    /// <summary>Re-reads the world and repairs a selection whose target is gone.</summary>
    private void Refresh()
    {
        _world = _session.World!.QueryWorld();
        _overview = _session.Queries.QueryOverview();
        _scene = GlobeScene.Build(_world, _setup.Rules, _setup.Localize, _placing is null ? null : _hover);
        _targets = GlobeTargets.Build(_world, _setup.Localize);
        if (_selected is not null) _selected = _targets.Find(_selected.Reference);
        RepairScreens();
        _globeDirty = true;
    }

    private void Redraw()
    {
        if (_globeDirty)
        {
            // BuildNewBaseState switches radar lines on while a site is chosen.
            _renderer.Render(_scene, _overview.Time, _placing is null ? _options : _options with { RadarLines = true },
                _blinkOn, CraftRange());
            _globeDirty = false;
        }
        _frame.Clear(0);
        _frame.Blit(_renderer.Surface, 0, 0, transparent: false);
        DrawSidebar();
        if (_naming is not null) DrawPopup(NamingPopup());
        else if (PopupShown) DrawPopup(_popups[0].Popup);
        _revision++;
    }

    private void DrawSidebar()
    {
        var overview = _overview;
        var time = overview.Time;
        Text(string.Create(CultureInfo.InvariantCulture, $"{time.Hour:00}:{time.Minute:00}:{time.Second:00}"), SidebarLeft, 8);
        Text(string.Create(CultureInfo.InvariantCulture, $"{time.Day:00}.{time.Month:00}.{time.Year}"), SidebarLeft, 20);
        Text(overview.Funds.ToString("N0", CultureInfo.InvariantCulture), SidebarLeft, 32);
        if (_selecting is not null)
        {
            DrawSelectionSidebar(SpeedTop + 2 * SpeedHeight);
            return;
        }
        if (_placing is not null)
        {
            DrawPlacementSidebar(SpeedTop + 2 * SpeedHeight);
            return;
        }
        for (var index = 0; index < SpeedLabels.Length; index++)
        {
            var top = SpeedTop + index * SpeedHeight;
            var current = (int)_time.Speed == index;
            _frame.FillRectangle(GlobeWidth + 2, top, Width - GlobeWidth - 4, SpeedHeight - 2, current ? HighlightColor : ButtonColor);
            Text(_setup.Localize(SpeedLabels[index]), SidebarLeft, top + 4, current ? (byte)0 : TextColor);
        }
        var y = SpeedTop + SpeedLabels.Length * SpeedHeight + 8;
        if (!_worldReady) Text(_entryBlocked is null ? "Place a base (B)" : "World entry blocked", SidebarLeft, y);
        else if (!_time.Running) Text("Time halted", SidebarLeft, y);
        y += 14;
        Text("B " + _setup.Localize("STR_BASES"), SidebarLeft, y);
        // GeoscapeState's Intercept button (also the I key).
        _frame.FillRectangle(GlobeWidth + 2, InterceptTop, Width - GlobeWidth - 4, SpeedHeight - 2, ButtonColor);
        Text(_setup.Localize("STR_INTERCEPT"), SidebarLeft, InterceptTop + 4, 0);
        y = InterceptTop + SpeedHeight;
        if (_selected is { } selected)
        {
            y += 24;
            Text(selected.Label, SidebarLeft, y);
            if (selected.Target is { Status.Length: > 0 } target) Text(target.Status, SidebarLeft, y + 12);
            if (selected.Craft is { } craft) Text(_setup.Localize(craft.Status), SidebarLeft, y + 12);
        }
        var message = _time.HaltReason ?? _status;
        if (message.Length != 0) Wrap(message, SidebarLeft, Height - 60, Width - SidebarLeft - 2);
    }

    private const int PopupLeft = 36;
    private const int MaximumTargetChoices = 16;
    private const int PopupWidth = 440;
    private const int ButtonHeight = 14;

    private void DrawPopup(GeoscapePopup popup)
    {
        var (top, height) = PopupBounds(popup);
        _frame.FillRectangle(PopupLeft, top, PopupWidth, height, 0);
        _frame.FillRectangle(PopupLeft, top, PopupWidth, 2, HighlightColor);
        _frame.FillRectangle(PopupLeft, top + height - 2, PopupWidth, 2, HighlightColor);
        _frame.FillRectangle(PopupLeft, top, 2, height, HighlightColor);
        _frame.FillRectangle(PopupLeft + PopupWidth - 2, top, 2, height, HighlightColor);
        var y = Wrap(popup.Title, PopupLeft + 10, top + 10, PopupWidth - 20, HighlightColor) + 6;
        foreach (var line in popup.Lines) y = Wrap(line, PopupLeft + 10, y, PopupWidth - 20) + 2;
        for (var index = 0; index < popup.Actions.Count; index++)
        {
            var (bx, by, bw, bh) = ButtonRectangle(popup, index);
            var action = popup.Actions[index];
            _frame.FillRectangle(bx, by, bw, bh, action.Disabled is null ? ButtonColor : (byte)0);
            Text(action.Label, bx + 4, by + 3, action.Disabled is null ? (byte)0 : TextColor);
        }
    }

    /// <summary>The popup sized to its title, lines and buttons, centred on the globe.</summary>
    private (int Top, int Height) PopupBounds(GeoscapePopup popup)
    {
        var text = WrappedHeight(popup.Title, PopupWidth - 20) + 6;
        foreach (var line in popup.Lines) text += WrappedHeight(line, PopupWidth - 20) + 2;
        var height = Math.Min(Height - 8, 20 + text + 8 + popup.Actions.Count * (ButtonHeight + 2) + 8);
        height = Math.Max(height, 120);
        return ((Height - height) / 2, height);
    }

    private (int X, int Y, int Width, int Height) ButtonRectangle(GeoscapePopup popup, int index)
    {
        var (top, height) = PopupBounds(popup);
        var bottom = top + height - 10;
        return (PopupLeft + 10, bottom - (popup.Actions.Count - index) * (ButtonHeight + 2), PopupWidth - 20, ButtonHeight);
    }

    private int PopupButtonAt(GeoscapePopup popup, int x, int y)
    {
        for (var index = 0; index < popup.Actions.Count; index++)
        {
            var (bx, by, bw, bh) = ButtonRectangle(popup, index);
            if (x >= bx && x < bx + bw && y >= by && y < by + bh) return index;
        }
        return -1;
    }

    /// <summary>The height <see cref="Wrap"/> would use for a text.</summary>
    private int WrappedHeight(string text, int width)
    {
        var font = _setup.Font;
        var lines = 0;
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length != 0 && font.Measure(candidate).Width > width)
            {
                lines++;
                line = word;
            }
            else line = candidate;
        }
        if (line.Length != 0) lines++;
        return lines * (font.Height + 2);
    }

    private void Text(string text, int x, int y, byte color = TextColor) =>
        _setup.Font.DrawTextShifted(_frame, text, x, y, color);

    /// <summary>Draws word-wrapped text and returns the y below it.</summary>
    private int Wrap(string text, int x, int y, int width, byte color = TextColor)
    {
        var font = _setup.Font;
        var line = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length != 0 && font.Measure(candidate).Width > width)
            {
                Text(line.ToString(), x, y, color);
                y += font.Height + 2;
                line.Clear().Append(word);
            }
            else line.Clear().Append(candidate);
        }
        if (line.Length != 0)
        {
            Text(line.ToString(), x, y, color);
            y += font.Height + 2;
        }
        return y;
    }

    /// <summary>Save and load report failures in the status line instead of ending the session.</summary>
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
                                          InvalidOperationException or ArgumentException or OverflowException)
        {
            _status = error.Message;
        }
    }

    private static Func<TimeSpan> CreateStopwatch()
    {
        var stopwatch = Stopwatch.StartNew();
        return () => stopwatch.Elapsed;
    }
}
