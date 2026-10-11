using System.Globalization;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;

namespace Oxce.Engine.Geoscape;

/// <summary>
/// Base placement on the globe: BuildNewBaseState (radar preview under the pointer),
/// ConfirmNewBaseState (cost and area), BaseNameState and the access lift choice. The first base
/// is placed before the world starts, as a new game does; further bases come from the N key or
/// the base screens' build-new-base command.
/// </summary>
public sealed partial class GeoscapeClient
{
    private const int MaximumBaseName = 40;

    private sealed record PlacementState(bool First, int BaseIndex);

    private sealed record NamingState(string Text, Action<string> Done);

    private static readonly TimeSpan HoverInterval = TimeSpan.FromMilliseconds(50);

    private PlacementState? _placing;
    private NamingState? _naming;
    private GlobeBaseHover? _hover;
    private GlobeBaseHover? _pendingHover;
    private TimeSpan _hoverElapsed;
    private string? _keyText;
    private string? _suppressText;

    /// <summary>A base site is being chosen on the globe (BuildNewBaseState); true for the starting base.</summary>
    public bool? PlacingBase => _placing?.First;

    /// <summary>The base name being typed (BaseNameState), if any.</summary>
    public string? BaseName => _naming?.Text;

    /// <summary>Starts choosing a site: the starting base when none is placed, otherwise a new base.</summary>
    public void BeginBasePlacement()
    {
        if (_session.Readiness is null)
        {
            _status = "Base placement is unavailable for this session.";
            return;
        }
        // Only the starting base is ever unplaced (GeoscapeState::init checks the first base).
        var first = _overview.Bases.Count != 0 && !_overview.Bases[0].IsPlaced;
        if (!first && _session.Readiness.QueryNewBaseRestriction() is { } restriction)
        {
            // BasescapeState hides "Build new base" at the base limit or before the unlock research.
            Push(Message(_setup.Localize(restriction)));
            return;
        }
        _placing = new PlacementState(first, 0);
        _rotateLongitude = _rotateLatitude = 0;
        _dragging = false;
        _time.Interrupt();
        _globeDirty = true;
    }

    private void CancelBasePlacement()
    {
        // BuildNewBaseState hides Cancel for the starting base.
        if (_placing is not { First: false }) return;
        _placing = null;
        _hover = _pendingHover = null;
        RebuildScene();
    }

    /// <summary>BuildNewBaseState::globeHover: the preview moves on the next 50 ms hover tick.</summary>
    private void HoverBaseSite(int x, int y)
    {
        if (!View.TryUnproject(x, y, out var longitude, out var latitude)) return;
        _pendingHover = new GlobeBaseHover(longitude, latitude, _setup.AllRadarsOnBaseBuild);
    }

    /// <summary>BuildNewBaseState::hoverRedraw: redraws only when the hovered point changed.</summary>
    private bool TickHover(TimeSpan interval)
    {
        if (_placing is null || _pendingHover is null) return false;
        _hoverElapsed += interval;
        if (_hoverElapsed < HoverInterval) return false;
        _hoverElapsed = TimeSpan.Zero;
        var pending = _pendingHover;
        _pendingHover = null;
        if (_hover == pending) return false;
        _hover = pending;
        RebuildScene();
        return true;
    }

    /// <summary>BuildNewBaseState::globeClick.</summary>
    private void ChooseBaseSite(double longitude, double latitude)
    {
        var placement = _placing!;
        longitude = (longitude % Math.Tau + Math.Tau) % Math.Tau;
        var site = _session.Readiness!.QueryBaseSite(longitude, latitude, placement.First);
        if (site.UnavailableReason is { } reason)
        {
            Push(Message(_setup.Localize(reason)));
            return;
        }
        if (placement.First)
        {
            BeginNaming(name => PlaceStartingBase(placement, name, longitude, latitude));
            return;
        }
        var tr = _setup.Localize;
        // ConfirmNewBaseState: the region's base cost and name; OK refuses without the funds.
        Push(new GeoscapePopup(GeoscapeNotifications.Format(tr("STR_COST_"), Funding(site.Cost)),
        [
            GeoscapeNotifications.Format(tr("STR_AREA_"), site.RegionId.Length == 0 ? "" : tr(site.RegionId)),
        ],
        [
            new(tr("STR_OK"), OkKey: true),
            new(tr("STR_CANCEL_UC"), CancelKey: true),
        ]), choice =>
        {
            if (choice != 0) return;
            if (_overview.Funds < site.Cost)
            {
                Push(Message(tr("STR_NOT_ENOUGH_MONEY")));
                return;
            }
            var lifts = _session.Readiness!.QueryAccessLifts(site.FakeUnderwater)
                .Where(lift => lift.UnavailableReason is null).ToArray();
            if (lifts.Length == 0)
            {
                Push(Message("No access lift is available for this base type."));
                return;
            }
            BeginNaming(name => ChooseLift(name, site, lifts));
        });
    }

    private void PlaceStartingBase(PlacementState placement, string name, double longitude, double latitude)
    {
        var result = _session.Commands.Execute(new PlaceStartingBase(placement.BaseIndex, name, longitude, latitude));
        if (result.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blocked)
        {
            Push(Message(_setup.Localize(blocked.Reason)));
            return;
        }
        _placing = null;
        _hover = _pendingHover = null;
        // GeoscapeState::init enters the world once the starting base exists.
        EnterWorld();
        Refresh();
    }

    /// <summary>PlaceLiftState: the access lift, chosen when the base type offers several.</summary>
    private void ChooseLift(string name, CampaignBaseSite site, CampaignFacilityChoice[] lifts)
    {
        if (lifts.Length == 1)
        {
            CreateBase(name, site, lifts[0]);
            return;
        }
        var tr = _setup.Localize;
        // PlaceLiftState lists the access lifts when the base type allows several.
        Push(new GeoscapePopup(tr("STR_ACCESS_LIFT"), [],
            [.. lifts.Select(lift => new GeoscapePopupAction(tr(lift.RuleId)))]), choice => CreateBase(name, site, lifts[choice]));
    }

    private void CreateBase(string name, CampaignBaseSite site, CampaignFacilityChoice lift)
    {
        // The lift goes where a facility of its size fits in the 6x6 grid's centre.
        var x = Math.Max(0, (6 - lift.SizeX) / 2);
        var y = Math.Max(0, (6 - lift.SizeY) / 2);
        var result = _session.Commands.Execute(new CreateCampaignBase(name, site.Longitude, site.Latitude, lift.RuleId, x, y));
        if (result.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blocked)
        {
            Push(Message(_setup.Localize(blocked.Reason)));
            return;
        }
        _placing = null;
        _hover = _pendingHover = null;
        Refresh();
        // PlaceLiftState continues to BasescapeState for the new base.
        if (result.Events.OfType<CampaignBaseCreated>().FirstOrDefault() is { } created) OpenBases(created.BaseId);
    }

    private void BeginNaming(Action<string> done)
    {
        _naming = new NamingState(string.Empty, done);
        // A digit that chose the button that opened the name box also arrives as text: drop it.
        _suppressText = _keyText;
        _rotateLongitude = _rotateLatitude = 0;
        _dragging = false;
        _time.Interrupt();
    }

    /// <summary>BaseNameState: typed text, Backspace, and Enter or Esc to accept a non-empty name.</summary>
    private void HandleNamingInput(in GameInputEvent input)
    {
        var naming = _naming!;
        switch (input.Kind)
        {
            case GameInputEventKind.TextInput when input.Text is { Length: > 0 } text && _suppressText == text:
                _suppressText = null;
                break;
            case GameInputEventKind.TextInput when input.Text is { Length: > 0 } text:
                _suppressText = null;
                var appended = naming.Text + new string(text.Where(static character => !char.IsControl(character)).ToArray());
                _naming = naming with { Text = appended.Length > MaximumBaseName ? appended[..MaximumBaseName] : appended };
                break;
            case GameInputEventKind.KeyPressed when input.KeyCode == 8 && naming.Text.Length > 0:
                _naming = naming with { Text = naming.Text[..^1] };
                break;
            case GameInputEventKind.KeyPressed when input.KeyCode is 13 or 0x40000058 or 27:
                AcceptName();
                break;
            case GameInputEventKind.PointerPressed when input.PointerButton == 1 &&
                PopupButtonAt(NamingPopup(), (int)input.X, (int)input.Y) == 0:
                AcceptName();
                break;
        }
    }

    private void AcceptName()
    {
        // BaseNameState accepts any non-empty name; a blank one would be refused by the command.
        var naming = _naming!;
        if (string.IsNullOrWhiteSpace(naming.Text)) return;
        _naming = null;
        _suppressText = null;
        naming.Done(naming.Text);
    }

    private GeoscapePopup NamingPopup() => new(_setup.Localize("STR_BASE_NAME"), [_naming!.Text + "_"],
        string.IsNullOrWhiteSpace(_naming.Text) ? [] : [new(_setup.Localize("STR_OK"), OkKey: true, CancelKey: true)]);

    /// <summary>Unicode::formatFunding: thousands separated, the one-character currency in front.</summary>
    private static string Funding(long value) =>
        (value < 0 ? "-$" : "$") + Math.Abs(value).ToString("N0", CultureInfo.InvariantCulture);

    private GeoscapePopup Message(string text) =>
        new(text, [], [new(_setup.Localize("STR_OK"), OkKey: true, CancelKey: true)]);

    private void DrawPlacementSidebar(int y)
    {
        var placing = _placing!;
        if (!placing.First)
        {
            _frame.FillRectangle(GlobeWidth + 2, CancelTop, Width - GlobeWidth - 4, SpeedHeight - 2, ButtonColor);
            Text(_setup.Localize("STR_CANCEL_UC"), SidebarLeft, CancelTop + 4, 0);
        }
        Wrap(_setup.Localize("STR_SELECT_SITE_FOR_NEW_BASE"), SidebarLeft, y, Width - SidebarLeft - 2, HighlightColor);
        var message = _status;
        if (message.Length != 0) Wrap(message, SidebarLeft, Height - 60, Width - SidebarLeft - 2);
    }

    private void RebuildScene()
    {
        _scene = GlobeScene.Build(_world, _setup.Rules, _setup.Localize, _placing is null ? null : _hover);
        _globeDirty = true;
    }
}
