using Oxce.Engine.Input;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;

namespace Oxce.Engine.Battlescape;

/// <summary>
/// <c>InventoryState</c>: one unit's sections, carried items and the items on its tile, from <see cref="IBattleInventoryQuery"/>. A keyboard
/// stands in for the mouse: Up and Down pick an item, Left and Right a section, Enter drops the item in the first place of that section
/// the rules accept (<c>CheckInventoryMove</c>), G is the Ctrl click, Tab and Shift-Tab the next and previous soldier and Escape closes
/// (before the first turn that is <see cref="StartFirstTurn"/>, which saves the equipment layout). The buttons of features not ported yet
/// send <see cref="UseInventoryFeature"/>, so their refusal comes from the rules.
/// </summary>
public sealed class BattleInventoryScreen : BattleScreenBase, IRevealedScreen
{
    private static readonly (uint Key, InventoryFeature Feature, string Label)[] Features =
    [
        ('l', InventoryFeature.LoadAmmo, "L load"), ('u', InventoryFeature.UnloadAmmo, "U unload"), ('t', InventoryFeature.Throw, "T throw"),
        ('p', InventoryFeature.Prime, "P prime"), ('s', InventoryFeature.SaveLayout, "S save layout"), ('o', InventoryFeature.LoadLayout, "O load layout"),
        ('c', InventoryFeature.ClearInventory, "C clear"), ('a', InventoryFeature.AutoEquip, "A autoequip"), ('e', InventoryFeature.Template, "E template"),
        ('r', InventoryFeature.RenameSoldier, "R rename"),
    ];

    // The query rebuilds the loaded ammunition list every time, so record equality would see a change on every tick.
    private static readonly IEqualityComparer<BattleInventoryItemView> ItemComparer = new ItemEquality();

    private sealed class ItemEquality : IEqualityComparer<BattleInventoryItemView>
    {
        public bool Equals(BattleInventoryItemView? x, BattleInventoryItemView? y) =>
            x is not null && y is not null && (x.ItemId, x.ItemType, x.Slot, x.SlotX, x.SlotY, x.Width, x.Height, x.FuseTimer, x.AmmoQuantity, x.Fixed) ==
            (y.ItemId, y.ItemType, y.Slot, y.SlotX, y.SlotY, y.Width, y.Height, y.FuseTimer, y.AmmoQuantity, y.Fixed) && x.LoadedAmmo.SequenceEqual(y.LoadedAmmo);

        public int GetHashCode(BattleInventoryItemView item) => item.ItemId;
    }

    private readonly bool _preBattle;
    private BattleInventoryView? _view;
    private int _item;
    private int _slot;

    public BattleInventoryScreen(BattleUiContext context, bool preBattle) : base(context)
    {
        _preBattle = preBattle;
        Reload();
        Changed();
    }

    public BattleInventoryView? Inventory => _view;

    /// <summary>The item the cursor is on (carried items first, then the ground), or null.</summary>
    public BattleInventoryItemView? CurrentItem => Items.Count == 0 ? null : Items[Math.Clamp(_item, 0, Items.Count - 1)];

    /// <summary>The section the cursor is on, or null.</summary>
    public BattleInventorySlotView? CurrentSlot => _view is { Slots.Count: > 0 } view ? view.Slots[Math.Clamp(_slot, 0, view.Slots.Count - 1)] : null;

    private List<BattleInventoryItemView> Items => _view is null ? [] : [.. _view.Carried, .. _view.Ground];

    public void Revealed()
    {
        Reload();
        Changed();
    }

    private int? UnitId()
    {
        var model = Context.Model;
        model.Refresh();
        if (model.SelectedUnitId is { } id) return id;
        return model.Overview?.Units.FirstOrDefault(unit => unit.Faction == Context.Viewer.Faction && unit.ControllerSlot == Context.Viewer.PlayerSlot)?.Id;
    }

    private void Reload()
    {
        _view = UnitId() is { } id ? Context.Inventory.QueryInventory(Context.Viewer, id) : null;
        _item = Math.Clamp(_item, 0, Math.Max(0, Items.Count - 1));
        _slot = Math.Clamp(_slot, 0, Math.Max(0, (_view?.Slots.Count ?? 1) - 1));
    }

    protected override bool Poll()
    {
        var before = _view;
        Reload();
        if (before is null || _view is null) return before is null != (_view is null);
        return before.UnitId != _view.UnitId || before.TimeUnits != _view.TimeUnits || !before.Carried.SequenceEqual(_view.Carried, ItemComparer) ||
            !before.Ground.SequenceEqual(_view.Ground, ItemComparer);
    }

    protected override bool Handle(in GameInputEvent input)
    {
        if (input.Kind != GameInputEventKind.KeyPressed || input.IsRepeat) return false;
        var key = input.KeyCode;
        Message = "";
        switch (key)
        {
            case KeyEscape:
                Close();
                return true;
            case KeyTab:
                if (!Context.Model.NextInventoryUnit(backwards: Shift(input))) Message = Context.Model.Message;
                Reload();
                _item = 0;
                return true;
            case KeyDown: _item = Math.Min(_item + 1, Math.Max(0, Items.Count - 1)); return true;
            case KeyUp: _item = Math.Max(_item - 1, 0); return true;
            case KeyRight: _slot = Math.Min(_slot + 1, Math.Max(0, (_view?.Slots.Count ?? 1) - 1)); return true;
            case KeyLeft: _slot = Math.Max(_slot - 1, 0); return true;
            case KeyEnter:
                Drop();
                return true;
            case 'g':
                if (CurrentItem is { } auto && _view is { } view)
                    Run(new AutoPlaceInventoryItem(view.UnitId, auto.ItemId, Smart: true));
                return true;
        }
        foreach (var (featureKey, feature, _) in Features)
            if (key == featureKey && _view is { } open)
            {
                Run(new UseInventoryFeature(open.UnitId, CurrentItem?.ItemId ?? -1, feature));
                return true;
            }
        return false;
    }

    private void Drop()
    {
        if (_view is not { } view || CurrentItem is not { } item || CurrentSlot is not { } slot) return;
        IReadOnlyList<BattleInventoryCell> cells = slot.Cells.Count == 0 ? [new BattleInventoryCell(0, 0)] : slot.Cells;
        foreach (var cell in cells)
            if (Context.Inventory.CheckInventoryMove(Context.Viewer, view.UnitId, item.ItemId, slot.Id, cell.X, cell.Y) is null)
            {
                Run(new MoveInventoryItem(view.UnitId, item.ItemId, slot.Id, cell.X, cell.Y));
                return;
            }
        // No place accepts it: the rules' reason for the first cell is the one shown.
        Message = Context.Inventory.CheckInventoryMove(Context.Viewer, view.UnitId, item.ItemId, slot.Id, cells[0].X, cells[0].Y) ?? "";
    }

    private void Run(ICampaignCommand command)
    {
        Message = Code(Context.Commands.Execute(command));
        Reload();
    }

    private void Close()
    {
        if (!_preBattle)
        {
            ExitRequested = true;
            return;
        }
        Message = Code(Context.Commands.Execute(new StartFirstTurn()));
        if (Message.Length != 0) return;
        Context.Model.Refresh();
        Context.Screens.Replace(new BattlescapeClient(Context));
    }

    protected override void Draw()
    {
        if (_view is not { } view)
        {
            Text("No inventory", 16, 16, BattleUiContext.Color.Orange);
            Text(Message, 16, 388, BattleUiContext.Color.Orange);
            return;
        }
        Text($"Unit {view.UnitId}   TU {view.TimeUnits}{(view.ChargesTimeUnits ? "" : " (free)")}", 16, 8, BattleUiContext.Color.Green);
        var y = 28;
        for (var index = 0; index < view.Slots.Count; index++)
        {
            var slot = view.Slots[index];
            var held = view.Carried.Where(item => item.Slot == slot.Id).Select(item => Context.Localize(item.ItemType));
            Text($"{(index == _slot ? ">" : " ")} {Context.Localize(slot.Id)}: {string.Join(", ", held)}", 16, y,
                index == _slot ? BattleUiContext.Color.Green : BattleUiContext.Color.White);
            y += 12;
        }
        y += 8;
        var items = Items;
        Text("Items", 16, y, BattleUiContext.Color.Green);
        y += 14;
        for (var index = 0; index < items.Count && y < 340; index++, y += 12)
        {
            var item = items[index];
            if (index == Math.Clamp(_item, 0, items.Count - 1)) Frame.FillRectangle(12, y - 1, 400, 12, BattleUiContext.Color.Panel);
            var place = view.Ground.Any(ground => ground.ItemId == item.ItemId) ? "ground" : item.Slot;
            Text($"{Context.Localize(item.ItemType)}  {place} {item.SlotX},{item.SlotY}{(item.Fixed ? " fixed" : "")}", 16, y);
        }
        Text("Enter: drop  G: auto  Tab: next soldier  Esc: close", 16, 360);
        Text(string.Join("  ", Features.Select(static feature => feature.Label)), 16, 372);
        if (Message.Length != 0) Text(Message, 16, 388, BattleUiContext.Color.Orange);
    }
}
