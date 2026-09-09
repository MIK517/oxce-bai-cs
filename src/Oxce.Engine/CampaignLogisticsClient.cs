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
    private bool _readiness;
    private bool _management;
    private bool _personnel;
    private int _gridX;
    private int _gridY;
    private int _choice;

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
                    _readiness = false;
                    _chooseDestination = false;
                    _message = "Campaign loaded.";
                    _loadConfirmation = false;
                }
                else if (key == 27) { _loadConfirmation = false; _message = "Load cancelled."; }
            }
            else if (key == 27)
            {
                if (_management || _personnel) { _management = false; _personnel = false; }
                else if (_readiness) _readiness = false;
                else if (_chooseDestination) _chooseDestination = false;
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
                _management = false;
                _personnel = false;
                _chooseDestination = false;
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
            else if (key == 'r')
            {
                _readiness = true;
                _management = false;
                _personnel = false;
                _screen = null;
                _row = 0;
            }
            else if (key == 'a' && !_management && _session.Queries is ICampaignReadinessQuery)
            {
                _management = true; _personnel = false; _readiness = false; _screen = null; _choice = 0;
            }
            else if (key == 'u' && _session.Queries is ICampaignReadinessQuery)
            {
                _personnel = true; _management = false; _readiness = false; _screen = null; _row = 0;
            }
            else if (_management && _session.Queries is ICampaignReadinessQuery management)
            {
                var state = management.QueryBaseManagement(overview.Bases[_baseIndex].Id);
                if (key is 0x40000051 or 0x40000052 && state.Facilities.Count > 0)
                    _choice = (_choice + (key == 0x40000051 ? 1 : state.Facilities.Count - 1)) % state.Facilities.Count;
                else if (key == 'w') _gridY = Math.Max(0, _gridY - 1);
                else if (key == 's') _gridY = Math.Min(5, _gridY + 1);
                else if (key == 'a') _gridX = Math.Max(0, _gridX - 1);
                else if (key == 'd') _gridX = Math.Min(5, _gridX + 1);
                else if (key == 13 && state.Facilities.Count > 0)
                    Feedback(_session.Commands.Execute(new BuildCampaignFacility(overview.Bases[_baseIndex].Id,
                        state.Facilities[_choice].RuleId, _gridX, _gridY)));
                else if (key == 'x') Feedback(_session.Commands.Execute(new DismantleCampaignFacility(
                    overview.Bases[_baseIndex].Id, _gridX, _gridY)));
            }
            else if (_personnel && _session.Queries is ICampaignReadinessQuery personnel)
            {
                var state = personnel.QueryBaseManagement(overview.Bases[_baseIndex].Id);
                if (key is 0x40000051 or 0x40000052)
                    _row = Math.Clamp(_row + (key == 0x40000051 ? 1 : -1), 0, Math.Max(0, state.Soldiers.Count - 1));
                else if (state.Soldiers.Count > 0)
                {
                    var soldier = state.Soldiers[_row];
                    if (key == 'g') Feedback(_session.Commands.Execute(new SetSoldierTraining(overview.Bases[_baseIndex].Id,
                        soldier.Id, !soldier.Training, soldier.PsiTraining)));
                    else if (key == 'q') Feedback(_session.Commands.Execute(new SetSoldierTraining(overview.Bases[_baseIndex].Id,
                        soldier.Id, soldier.Training, !soldier.PsiTraining)));
                    else if (key == 'c')
                    {
                        var crafts = personnel.QueryReadiness(overview.Bases[_baseIndex].Id).Crafts;
                        var craft = crafts.Count == 0 ? null : crafts[0];
                        Feedback(_session.Commands.Execute(soldier.CraftRuleId.Length == 0 && craft is not null
                            ? new AssignSoldierToCraft(overview.Bases[_baseIndex].Id, soldier.Id, craft.RuleId, craft.Id)
                            : new AssignSoldierToCraft(overview.Bases[_baseIndex].Id, soldier.Id)));
                    }
                    else if (key == 'o' && state.Armors.Count > 0)
                    {
                        var armor = state.Armors[(state.Armors.ToList().IndexOf(soldier.Armor) + 1) % state.Armors.Count];
                        Feedback(_session.Commands.Execute(new EquipSoldierArmor(overview.Bases[_baseIndex].Id, soldier.Id, armor)));
                    }
                }
            }
            else if (key is (uint)'i' or (uint)'b' or (uint)'s' or (uint)'t')
            {
                _readiness = false;
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
            else if (key == 'p' && !overview.Bases[_baseIndex].IsPlaced && _session.Queries is ICampaignReadinessQuery placement)
            {
                var sites = placement.QueryBaseSites(true);
                if (sites.Count == 0) _message = "No legal starting-base site is available.";
                else Feedback(_session.Commands.Execute(new PlaceStartingBase(_baseIndex, "First Base", sites[0].Longitude, sites[0].Latitude)));
            }
            else if (key == 'n' && _session.Queries is ICampaignReadinessQuery newBase)
            {
                var sites = newBase.QueryBaseSites(false);
                var site = sites.Count == 0 ? null : sites[0];
                var lift = site is null ? null : newBase.QueryAccessLifts(site.FakeUnderwater)
                    .FirstOrDefault(f => f.UnavailableReason is null);
                if (sites.Count == 0 || lift is null) _message = "No legal site or access lift is available.";
                else Feedback(_session.Commands.Execute(new CreateCampaignBase($"Base {overview.Bases.Count + 1}",
                    site!.Longitude, site.Latitude, lift.RuleId, 2, 2)));
            }
            else if (_readiness && key == 'e' && _session.Queries is ICampaignReadinessQuery equipment)
            {
                var readiness = equipment.QueryReadiness(overview.Bases[_baseIndex].Id);
                var choices = equipment.QueryBaseManagement(overview.Bases[_baseIndex].Id).CraftWeapons;
                if (readiness.Crafts.Count == 0 || readiness.Crafts[0].Weapons.Count == 0) _message = "No editable craft weapon slot is available.";
                else
                {
                    var slot = readiness.Crafts[0].Weapons.Count - 1;
                    var current = readiness.Crafts[0].Weapons[slot]?.RuleId ?? "";
                    var position = choices.ToList().IndexOf(current) + 1;
                    var next = (position + 1) % (choices.Count + 1);
                    Feedback(_session.Commands.Execute(new EquipCraftWeapon(overview.Bases[_baseIndex].Id,
                        readiness.Crafts[0].RuleId, readiness.Crafts[0].Id, slot, next == 0 ? "" : choices[next - 1])));
                }
            }
            else if (_readiness && key == 'v' && _session.Queries is ICampaignReadinessQuery vehicleEquipment)
            {
                var readiness = vehicleEquipment.QueryReadiness(overview.Bases[_baseIndex].Id);
                var choices = vehicleEquipment.QueryBaseManagement(overview.Bases[_baseIndex].Id).Vehicles;
                if (readiness.Crafts.Count == 0 || choices.Count == 0) _message = "No craft or vehicle type is available.";
                else
                {
                    var craft = readiness.Crafts[0];
                    Feedback(_session.Commands.Execute(new ChangeCraftVehicle(overview.Bases[_baseIndex].Id,
                        craft.RuleId, craft.Id, choices[0], !craft.Vehicles.Contains(choices[0], StringComparer.Ordinal))));
                }
            }
            else if (key == ' ')
            {
                _screen = null;
                Feedback(_session.Commands.Execute(new AdvanceCampaignTime((input.Modifiers & InputKeyModifiers.Shift) != 0 ? 720 : 12)));
            }
            else if (_readiness && key is 0x40000051 or 0x40000052)
                _row = Math.Max(0, _row + (key == 0x40000051 ? 1 : -1));
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
            FacilityServiceMessage service => service.Message,
            StartingBasePlaced => "Base placed.",
            CampaignBaseCreated created => $"Base {created.Name} created.",
            CampaignFacilityChanged facility => $"Facility {facility.RuleId} changed.",
            CampaignConstructionCompleted facility => $"Facility {facility.RuleId} completed.",
            CampaignTrainingCompleted training => $"Soldier {training.SoldierId} completed training.",
            CampaignPersonnelChanged personnel => $"Soldier {personnel.SoldierId} updated.",
            CampaignSoldierTransformed transformed => $"Soldier {transformed.SoldierId} transformed.",
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
        Text("I: Stores B: Buy T: Transfer R: Readiness A: Base U: Personnel N: New", 12, 48);
        Text("Space: Minute  Shift+Space: Hour  F5: Save  F9: Load  Esc: Back/quit", 12, 64);
        if (_screen is null && selectedBase is { IsPlaced: false }) Text("P: Place starting base at 0,0", 12, 86, 3);
        if (_management && selectedBase is not null && _session.Queries is ICampaignReadinessQuery management)
        {
            var state = management.QueryBaseManagement(selectedBase.Id);
            Text($"Base layout cursor ({_gridX},{_gridY})  WASD: Move  Enter: Build  X: Dismantle", 12, 88);
            Text($"Monthly maintenance {state.Maintenance.Total} (fac {state.Maintenance.Facilities}, craft {state.Maintenance.Craft}, staff {state.Maintenance.Personnel}, items {state.Maintenance.Inventory})", 12, 148);
            if (state.Facilities.Count > 0)
            {
                _choice = Math.Min(_choice, state.Facilities.Count - 1);
                var choice = state.Facilities[_choice];
                Text($"Up/Down: {_text(choice.RuleId)} {choice.SizeX}x{choice.SizeY} cost {choice.Cost} time {choice.BuildTime}d", 12, 108,
                    choice.UnavailableReason is null ? 1 : 3);
                Text(choice.UnavailableReason ?? "", 12, 128, 3);
            }
            foreach (var facility in selectedBase.Facilities)
                Text($"{facility.X},{facility.Y} {facility.SizeX}x{facility.SizeY} {facility.BuildTime}d", 30 + facility.X * 95, 180 + facility.Y * 28);
        }
        else if (_personnel && selectedBase is not null && _session.Queries is ICampaignReadinessQuery personnel)
        {
            var state = personnel.QueryBaseManagement(selectedBase.Id);
            _row = Math.Min(_row, Math.Max(0, state.Soldiers.Count - 1));
            Text("Up/Down: Soldier  G: Gym  Q: Psi  C: Assign craft  O: Armor", 12, 88);
            for (var i = 0; i < state.Soldiers.Count && i < 14; i++)
            {
                var soldier = state.Soldiers[i];
                if (i == _row) Frame.FillRectangle(8, 108 + i * 17, 624, 16, 2);
                Text($"{soldier.Name}  {soldier.Armor}  {(soldier.Wounded ? "wounded" : "fit")}  {(soldier.Training ? "gym" : "")} {(soldier.PsiTraining ? "psi" : "")} {soldier.CraftRuleId}#{soldier.CraftId}",
                    12, 111 + i * 17);
            }
        }
        else if (_readiness && selectedBase is not null)
        {
            if (_session.Queries is ICampaignReadinessQuery query)
            {
                var readiness = query.QueryReadiness(selectedBase.Id);
                var lines = readiness.Crafts.SelectMany(c => new[]
                {
                    $"{(c.Name.Length == 0 ? _text(c.RuleId) : c.Name)} #{c.Id}: {_text(c.Status)}",
                    $"  Fuel {c.Fuel}/{c.FuelMaximum}  Damage {c.Damage}  Shield {c.Shield}/{c.ShieldMaximum}",
                }.Concat(c.Weapons.Select((w, i) => w is null ? $"  Slot {i + 1}: empty" :
                    $"  Slot {i + 1}: {_text(w.RuleId)} {w.Ammo}/{w.MaximumAmmo} {(w.Disabled ? "disabled" : w.Rearming ? "rearming" : "")}")))
                    .Concat(readiness.Defenses.Where(f => f.AmmoMaximum > 0).Select(f =>
                        $"{_text(f.RuleId)} ({f.X},{f.Y}): {f.Ammo}/{f.AmmoMaximum}  Build {f.BuildTime}d")).ToArray();
                _row = Math.Min(_row, Math.Max(0, lines.Length - 1));
                Text("Hourly service. E: cycle slot 1 weapon  V: toggle first vehicle  Up/Down: scroll", 12, 88);
                for (var i = _row; i < Math.Min(_row + 13, lines.Length); i++) Text(lines[i], 12, 110 + (i - _row) * 16);
                Text(readiness.ServiceLimitation ?? "Space advances time; shortages pause after the current tick.", 12, 335, 3);
            }
            else Text("Readiness queries are unavailable for this session.", 12, 110, 3);
        }
        else if (_chooseDestination)
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
