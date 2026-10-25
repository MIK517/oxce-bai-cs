using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles.Generation;

/// <summary>
/// The end of BattlescapeGenerator::deployXCOM: the craft's cargo on the craft inventory tile goes to the soldiers by
/// their equipment layouts (placeItemByLayout, reloadFixedWeaponsByLayout), ground weapons load (loadWeapons), extra
/// ammunition follows the layouts, and soldiers without a layout take the rest (autoEquip).
/// </summary>
internal sealed class XcomCargo
{
    // BattleType values.
    private const int Firearm = 1;
    private const int Ammo = 2;
    private const int Melee = 3;
    private const int Grenade = 4;
    private const int ProximityGrenade = 5;
    private const int Scanner = 7;
    private const int MindProbe = 8;
    private const int PsiAmp = 9;
    private const int Flare = 10;

    /// <summary>BattleFuseType BFT_NONE.</summary>
    private const int NoFuse = -3;

    /// <summary>ItemDamageType DT_SMOKE.</summary>
    private const int Smoke = 9;

    private readonly BattleState _battle;
    private readonly UnitEquipment _equipment;
    private readonly RuntimeRuleCatalog _rules;
    private readonly CampaignOptions _options;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<SoldierLayoutItem>> _layouts;
    private readonly int _worldShade;

    private XcomCargo(BattleState battle, UnitEquipment equipment, RuntimeRuleCatalog rules, CampaignOptions options,
        IReadOnlyDictionary<int, IReadOnlyList<SoldierLayoutItem>> layouts, int worldShade)
    {
        _battle = battle;
        _equipment = equipment;
        _rules = rules;
        _options = options;
        _layouts = layouts;
        _worldShade = worldShade;
    }

    public static void Distribute(BattleState battle, UnitEquipment equipment, RuntimeRuleCatalog rules, CampaignOptions options,
        IReadOnlyDictionary<int, IReadOnlyList<SoldierLayoutItem>> layouts, int worldShade) =>
        new XcomCargo(battle, equipment, rules, options, layouts, worldShade).Run();

    private void Run()
    {
        // Units were placed, so the craft inventory tile exists.
        var tile = _battle.CraftInventoryTile ?? throw new MapGenerationException("The X-COM units have no craft inventory tile.");
        var items = TileItems(tile);
        foreach (var item in items)
        {
            item.XcomProperty = true;
            // Don't let the soldiers take extra ammo yet.
            if (Type(item) == Ammo && !_options.AlternateCraftEquipmentManagement) continue;
            PlaceItemByLayout(item, items);
        }
        ReloadFixedWeaponsByLayout(tile);
        // Ammo loads in a predictable and moddable order (std::sort leaves equal load orders unspecified; the port keeps
        // the tile's order for them).
        LoadWeapons([.. TileItems(tile).OrderBy(item => _equipment.BattleItemRule(item.ItemType).LoadOrder)]);
        items = TileItems(tile);
        foreach (var item in items)
            if (Type(item) == Ammo) PlaceItemByLayout(item, items);
        AutoEquip(TileItems(tile));
    }

    /// <summary>Tile::getInventory: the items on the tile, in the order they arrived (creation order here).</summary>
    private List<BattleItem> TileItems(BattlePosition tile) => [.. _battle.Items.Where(item => item.Tile == tile)];

    private static bool OnGround(BattleItem item) => item.Slot == BattleState.GroundSlot;

    private int Type(BattleItem item) => _equipment.ItemRule(item.ItemType).BattleType;

    private IReadOnlyList<SoldierLayoutItem> Layout(BattleUnit unit) =>
        unit.SoldierId is { } id && _layouts.TryGetValue(id, out var layout) ? layout : [];

    /// <summary>BattlescapeGenerator::placeItemByLayout: the first soldier whose free layout slot wants the item takes it.</summary>
    private bool PlaceItemByLayout(BattleItem item, List<BattleItem> items)
    {
        if (!OnGround(item)) return false;
        var rule = _equipment.BattleItemRule(item.ItemType);
        foreach (var unit in _battle.Units)
        {
            foreach (var entry in Layout(unit))
            {
                // Fixed items are handled elsewhere.
                if (entry.Fixed || entry.ItemType != item.ItemType) continue;
                // Every cell the item covers must be free, not just its top left.
                var overlaps = false;
                for (var x = 0; x < rule.InventoryWidth && !overlaps; x++)
                    for (var y = 0; y < rule.InventoryHeight && !overlaps; y++)
                        overlaps = _equipment.ItemAt(unit, entry.Slot, x + entry.SlotX, y + entry.SlotY) is not null;
                if (overlaps) continue;
                var toLoad = LoadLayoutAmmo(item, entry, items, allowRemovalWhileIterating: true);
                // Only a weapon loaded with its layout ammo (if any) goes onto the soldier.
                if (toLoad == 0 || _equipment.HaveAnyAmmo(item))
                {
                    UnitEquipment.MoveToOwner(item, unit);
                    item.Slot = entry.Slot;
                    item.SlotX = entry.SlotX;
                    item.SlotY = entry.SlotY;
                    if (_options.IncludePrimeStateInSavedLayout && rule.FuseType != NoFuse) item.SetFuseTimer(entry.FuseTimer, rule);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Loads a layout entry's ammunition from the ground items into the weapon; returns how many slots stay unloaded.
    /// An ammo item matching a layout slot is tried once, whether or not it loads.
    /// </summary>
    private int LoadLayoutAmmo(BattleItem weapon, SoldierLayoutItem entry, List<BattleItem> items, bool allowRemovalWhileIterating)
    {
        var toLoad = entry.AmmoItems.Count(static ammo => ammo.Length != 0);
        if (toLoad == 0) return 0;
        foreach (var ammo in items)
        {
            if (!OnGround(ammo)) continue;
            var loaded = false;
            for (var slot = 0; slot < entry.AmmoItems.Count; slot++)
            {
                if (ammo.ItemType != entry.AmmoItems[slot]) continue;
                if (_equipment.SetAmmoPreMission(weapon, ammo))
                {
                    toLoad--;
                    loaded = true;
                }
                break;
            }
            if (toLoad == 0) break;
            // reloadFixedWeaponsByLayout walks the live tile inventory, which loading erases from; the reference then
            // reads past the shrunken vector.
            if (loaded && !allowRemovalWhileIterating)
                throw new MapGenerationException($"Fixed weapon {weapon.ItemType} loads several layout ammo items from the craft inventory tile.");
        }
        return toLoad;
    }

    /// <summary>BattlescapeGenerator::reloadFixedWeaponsByLayout: soldiers' fixed weapons take their layout ammo.</summary>
    private void ReloadFixedWeaponsByLayout(BattlePosition tile)
    {
        foreach (var unit in _battle.Units)
        {
            foreach (var entry in Layout(unit))
            {
                if (!entry.Fixed) continue;
                var weapon = _battle.InventoryOf(unit).FirstOrDefault(item => item.Slot == entry.Slot && item.SlotX == entry.SlotX &&
                    item.SlotY == entry.SlotY && item.ItemType == entry.ItemType);
                if (weapon is null) continue;
                LoadLayoutAmmo(weapon, entry, TileItems(tile), allowRemovalWhileIterating: false);
            }
        }
    }

    /// <summary>BattlescapeGenerator::loadWeapons: each unfixed weapon still lacking ammo loads from the ground items.</summary>
    private void LoadWeapons(List<BattleItem> items)
    {
        foreach (var weapon in items)
        {
            if (!weapon.IsWeaponWithAmmo || UnitEquipment.HaveAllAmmo(weapon) || _equipment.BattleItemRule(weapon.ItemType).Fixed) continue;
            foreach (var ammo in items)
                if (OnGround(ammo) && _equipment.SetAmmoPreMission(weapon, ammo) && UnitEquipment.HaveAllAmmo(weapon)) break;
        }
    }

    /// <summary>
    /// BattlescapeGenerator::autoEquip: soldiers without a layout, strongest first, take the heaviest-first ground items one
    /// best-scoring item per pass until nobody takes anything (std::sort's order for equal strengths and weights is
    /// unspecified; the port keeps the battle's and the tile's order). Then pre-primed grenades, by the user option.
    /// </summary>
    private void AutoEquip(List<BattleItem> craftItems)
    {
        var units = _battle.Units.OrderByDescending(static unit => unit.Stats.Strength).ToArray();
        var items = craftItems.OrderByDescending(_equipment.TotalWeight).ToArray();
        var allowAutoLoadout = !_options.DisableAutoEquip;
        bool someoneGotSomething;
        do
        {
            someoneGotSomething = false;
            foreach (var unit in units)
            {
                if (!Eligible(unit)) continue;
                BattleItem? best = null;
                var highest = -1.0;
                foreach (var candidate in items)
                {
                    var type = Type(candidate);
                    if (type == Flare && _worldShade <= _rules.Campaign.MaximumDarknessToSeeUnits) continue;
                    var rule = _equipment.BattleItemRule(candidate.ItemType);
                    if (rule.InventoryHeight == 0 || rule.InventoryWidth == 0 || !OnGround(candidate)) continue;
                    var count = 0;
                    var baseScore = 1.0;
                    var name = _equipment.ItemRule(candidate.ItemType).Name;
                    foreach (var carried in _battle.InventoryOf(unit))
                    {
                        // Ammo for an item the unit has doubles the score; more of a kind lowers it.
                        if (_equipment.SlotForAmmo(carried, candidate.ItemType) != -1) baseScore = 2.0;
                        else if (type == Type(carried))
                        {
                            if (type is Firearm or Melee or MindProbe or PsiAmp or Scanner) count += 10;
                            else if (_equipment.ItemRule(carried.ItemType).Name == name) count++;
                        }
                    }
                    var score = baseScore / (1.0 + count);
                    if (_equipment.AddItem(candidate, unit, allowSecondClip: true, allowAutoLoadout, allowUnloadedWeapons: true,
                            allowInfinite: true, testMode: true) && score > highest)
                    {
                        highest = score;
                        best = candidate;
                    }
                }
                if (best is not null && _equipment.AddItem(best, unit, allowSecondClip: true, allowAutoLoadout, allowUnloadedWeapons: true,
                        allowInfinite: true, testMode: false))
                    someoneGotSomething = true;
            }
        } while (someoneGotSomething);

        if (_options.PreprimeGrenades <= 0) return;
        foreach (var unit in units)
        {
            if (!Eligible(unit)) continue;
            foreach (var item in _battle.InventoryOf(unit))
            {
                var type = Type(item);
                if (type is not (Grenade or ProximityGrenade)) continue;
                if (_options.PreprimeGrenades < 2 && _equipment.ItemRule(item.ItemType).Damage.ResistType != Smoke) continue;
                if (_options.PreprimeGrenades < 3 && type == ProximityGrenade) continue;
                item.SetFuseTimer(0, _equipment.BattleItemRule(item.ItemType));
            }
        }
    }

    private bool Eligible(BattleUnit unit) => _equipment.HasInventory(unit) && unit.SoldierId is not null && Layout(unit).Count == 0;
}
