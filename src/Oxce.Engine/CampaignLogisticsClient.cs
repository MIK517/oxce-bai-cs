using System.Globalization;
using Oxce.Core.Graphics;
using Oxce.Engine.Input;
using Oxce.Gameplay.Campaigns;
using Oxce.Rendering;

namespace Oxce.Engine;

public sealed record CampaignUiSession(ICampaignQuery Queries, ICampaignCommandTarget Commands);

/// <summary>Keyboard-operated indexed logistics UI; persistence is supplied by App.</summary>
public sealed class CampaignLogisticsClient : IIndexedLoopClient
{
    private CampaignUiSession _session;
    private readonly IndexedSpriteFont _font;
    private readonly Func<string, string> _text;
    private readonly Action? _save;
    private readonly Func<CampaignUiSession>? _load;
    private CampaignLogisticsScreen? _screen;
    private int _baseIndex;
    private int _destinationIndex;
    private int _row;
    private string _message = "";
    private bool _chooseDestination;
    private bool _loadConfirmation;

    public CampaignLogisticsClient(CampaignUiSession session, IndexedSpriteFont? font = null,
        Func<string, string>? localize = null, Action? save = null, Func<CampaignUiSession>? load = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _font = font ?? IndexedInterfaceFont.Create();
        _text = localize ?? (value => value);
        _save = save;
        _load = load;
        Frame = new IndexedSurface(640, 400);
        var colors = new Rgba32[256];
        colors[1] = new(10, 18, 32);
        colors[2] = new(28, 52, 76);
        for (var shade = 0; shade < 16; shade++)
        {
            var brightness = (16 - shade) / 16.0;
            colors[16 + shade] = new((byte)(225 * brightness), (byte)(237 * brightness), (byte)(245 * brightness));
            colors[32 + shade] = new((byte)(125 * brightness), (byte)(215 * brightness), (byte)(165 * brightness));
            colors[48 + shade] = new((byte)(255 * brightness), (byte)(170 * brightness), (byte)(120 * brightness));
        }
        Palette = new(colors);
        Redraw();
    }

    public IndexedSurface Frame { get; }
    public IndexedPalette Palette { get; }
    public long PresentationRevision { get; private set; }
    public bool ExitRequested { get; private set; }
    public CampaignLogisticsScreen? Screen => _screen;

    public void HandleInput(in GameInputEvent input)
    {
        if (input.Kind != GameInputEventKind.KeyPressed || input.IsRepeat) return;
        var key = input.KeyCode;
        var overview = _session.Queries.QueryOverview();
        try
        {
            if (_loadConfirmation)
            {
                if (key == 'y')
                {
                    _session = _load!();
                    _baseIndex = 0;
                    _screen = null;
                    _chooseDestination = false;
                    _message = "Campaign loaded.";
                    _loadConfirmation = false;
                }
                else if (key == 27) { _loadConfirmation = false; _message = "Load cancelled."; }
            }
            else if (key == 27)
            {
                if (_chooseDestination) _chooseDestination = false;
                else if (_screen?.Quote is not null) _screen.Cancel();
                else if (_screen is not null) _screen = null;
                else ExitRequested = true;
            }
            else if (key == 0x4000003e && _screen?.Quote is null) // F5
            {
                if (_save is null) _message = "Save is unavailable for this session.";
                else { _save(); _message = "Campaign saved."; }
            }
            else if (key == 0x40000042 && _screen?.Quote is null) // F9
            {
                if (_load is null) _message = "Load is unavailable for this session.";
                else { _loadConfirmation = true; _message = "Load saved campaign and discard unsaved changes? Y: Load  Esc: Cancel"; }
            }
            else if (overview.Bases.Count == 0) _message = "No base is available.";
            else if (key == 9 && _screen?.Quote is null)
            {
                _baseIndex = (_baseIndex + 1) % overview.Bases.Count;
                _screen = null;
            }
            else if (_chooseDestination)
            {
                if (key is 0x40000051 or 0x40000052)
                    _destinationIndex = (_destinationIndex + (key == 0x40000051 ? 1 : overview.Bases.Count - 1)) % overview.Bases.Count;
                if (key == 13)
                {
                    _screen!.OpenOrder(LogisticsOperation.Transfer, overview.Bases[_destinationIndex].Id);
                    _chooseDestination = false;
                    _row = 0;
                }
            }
            else if (_screen?.Quote is { } quote)
            {
                if (key is 0x40000051 or 0x40000052)
                    _row = Math.Clamp(_row + (key == 0x40000051 ? 1 : -1), 0, Math.Max(0, quote.Rows.Count - 1));
                if (quote.Rows.Count > 0 && key is (uint)'+' or (uint)'=' or (uint)'-')
                {
                    var step = (input.Modifiers & InputKeyModifiers.Shift) != 0 ? 10 : 1;
                    _screen.SetQuantity(_row, (int)Math.Clamp((long)_screen.Quantity(_row) + (key == '-' ? -step : step), 0, int.MaxValue));
                }
                if (key == 13) _screen.RequestConfirmation();
                if (key == 'y' && _screen.ConfirmationPending) _screen.Confirm();
            }
            else if (key is (uint)'i' or (uint)'b' or (uint)'s' or (uint)'t')
            {
                _screen = new(_session.Queries, _session.Commands, overview.Bases[_baseIndex].Id);
                _row = 0;
                _message = "";
                if (key == 'b') _screen.OpenOrder(LogisticsOperation.Purchase);
                if (key == 's') _screen.OpenOrder(LogisticsOperation.Sell);
                if (key == 't')
                {
                    _chooseDestination = true;
                    _destinationIndex = (_baseIndex + 1) % overview.Bases.Count;
                }
            }
            else if (key == 'p' && !overview.Bases[_baseIndex].IsPlaced)
                Feedback(_session.Commands.Execute(new PlaceStartingBase(_baseIndex, "First Base", 0, 0)));
            else if (key == ' ')
            {
                _screen = null;
                Feedback(_session.Commands.Execute(new AdvanceCampaignTime((input.Modifiers & InputKeyModifiers.Shift) != 0 ? 720 : 12)));
            }
            else if (_screen is not null && key is 0x40000051 or 0x40000052)
                _row = Math.Clamp(_row + (key == 0x40000051 ? 1 : -1), 0,
                    Math.Max(0, _screen.Stores.Items.Count + _screen.Stores.Transfers.Count - 1));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or OverflowException or ArgumentException)
        {
            _message = exception.Message;
        }
        Redraw();
    }

    public void Tick(TimeSpan fixedInterval) => ArgumentOutOfRangeException.ThrowIfLessThan(fixedInterval, TimeSpan.Zero);

    private void Feedback(CampaignCommandResult result)
    {
        _message = string.Join(" / ", result.Events.Select(e => e switch
        {
            CampaignActionBlocked blocked => blocked.Reason,
            SuppliesArrived arrived => $"Supplies arrived at base {arrived.BaseId}.",
            CraftArrivalServiceMessage service => service.Message,
            StartingBasePlaced => "Base placed.",
            _ => "",
        }).Where(s => s.Length > 0));
    }

    private void Redraw()
    {
        Frame.Clear(1);
        var overview = _session.Queries.QueryOverview();
        var selectedBase = overview.Bases.Count == 0 ? null : overview.Bases[_baseIndex];
        Text($"{overview.Time.Year}-{overview.Time.Month:D2}-{overview.Time.Day:D2}  {overview.Time.Hour:D2}:{overview.Time.Minute:D2}    {overview.Funds}", 12, 10, 2);
        Text(selectedBase?.Name is { Length: > 0 } name ? name : "Starting base", 12, 28);
        Text("I: Stores  B: Buy/hire  S: Sell/dismiss  T: Transfer  Tab: Base", 12, 48);
        Text("Space: Minute  Shift+Space: Hour  F5: Save  F9: Load  Esc: Back/quit", 12, 64);
        if (_screen is null && selectedBase is { IsPlaced: false }) Text("P: Place starting base at 0,0", 12, 86, 3);
        if (_chooseDestination)
        {
            Text("Choose destination: Up/Down, Enter", 12, 106);
            Text(overview.Bases[_destinationIndex].Name, 12, 128, 2);
        }
        else if (_screen?.Quote is { } quote)
        {
            Text($"{quote.Operation}  Funds {quote.Funds}  Stores {quote.UsedStores:F1}/{quote.AvailableStores}  Quarters {quote.UsedQuarters}/{quote.AvailableQuarters}", 12, 88);
            Text("Item / person / craft                       Owned     Cost    Hours   Order/max", 12, 108);
            var start = _row / 10 * 10;
            for (var i = start; i < Math.Min(start + 10, quote.Rows.Count); i++)
            {
                var row = quote.Rows[i];
                if (i == _row) Frame.FillRectangle(8, 125 + (i - start) * 16, 624, 15, 2);
                Text(_text(row.Label), 12, 128 + (i - start) * 16, row.UnavailableReason is null ? 1 : 3, 265);
                Text($"{row.Owned,5} {row.UnitCost,9} {row.Hours,6} {_screen.Quantity(i),6}/{row.MaximumQuantity}", 285, 128 + (i - start) * 16);
            }
            string total;
            try { total = _screen.Total.ToString(CultureInfo.InvariantCulture); }
            catch (OverflowException) { total = "Order exceeds transaction range"; }
            Text($"Total: {total}   Up/Down: Row  +/-: Quantity  Shift: 10", 12, 295, 2);
            Text(_screen.ConfirmationPending ? "Y: Confirm this order   Esc: Keep editing" : "Enter: Review confirmation", 12, 314, 2);
            var reason = quote.Rows.Count > 0 ? quote.Rows[_row].UnavailableReason : "No rows available.";
            Text(_screen.Message.Length == 0 ? reason ?? "" : _screen.Message, 12, 335, 3);
        }
        else if (_screen is not null)
        {
            var stores = _screen.Stores;
            Text(stores.CapacityLimitation ?? $"Stores {stores.UsedStores:F1}/{stores.AvailableStores}  Quarters {stores.UsedQuarters}/{stores.AvailableQuarters}", 12, 88);
            Text("Item                                       Stored   Incoming   Size / Hours", 12, 108);
            var lines = stores.Items.Select(i => $"{_text(i.Label),-40} {i.Stored,8} {i.Incoming,8} {i.Size,8:F2}")
                .Concat(stores.Transfers.Select(t => $"{_text(t.Label),-40} {t.Quantity,8}   {_text("in transit")} {t.Hours,5}h")).ToArray();
            var start = _row / 12 * 12;
            for (var i = start; i < Math.Min(start + 12, lines.Length); i++) Text(lines[i], 12, 128 + (i - start) * 16);
            Text(_screen.Message, 12, 335, 2);
        }
        Text(_message, 12, 360, 3);
        PresentationRevision = checked(PresentationRevision + 1);
    }

    private void Text(string text, int x, int y, int group = 1, int maximumWidth = 615)
    {
        var translated = _text(text);
        var builder = new System.Text.StringBuilder();
        var width = 0;
        foreach (var rune in translated.EnumerateRunes())
        {
            width += _font.GetCharacterSize(rune).Width;
            if (width > maximumWidth) break;
            builder.Append(rune);
        }
        _font.DrawText(Frame, builder.ToString(), x, y, group);
    }
}
