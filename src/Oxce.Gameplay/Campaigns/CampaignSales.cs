using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private int SaleItemCount(BaseState state, string id)
    {
        var total = state.Items.GetValueOrDefault(_content.RuntimeRules.Items.GetRequired(id));
        foreach (var craft in state.Crafts) total = checked(total + CountCraft(craft.Logistics!));
        foreach (var transfer in state.Transfers.Where(t => !t.Delivered))
            total = checked(total + (transfer.Kind == CampaignTransferKind.Item && transfer.RuleId == id ? transfer.Quantity :
                transfer.Craft?.Logistics is { } craft ? CountCraft(craft) : 0));
        return total;

        int CountCraft(CraftLogisticsState craft)
        {
            var count = craft.Items.GetValueOrDefault(id);
            foreach (var weapon in craft.Weapons.OfType<CraftWeaponSnapshot>())
            {
                var rule = _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
                count = checked(count + (rule.Launcher == id ? 1 : rule.Clip == id ? WeaponClips(weapon) : 0));
            }
            foreach (var vehicle in craft.Vehicles)
            {
                var ammo = VehicleClips(vehicle);
                count = checked(count + (vehicle.RuleId == id ? 1 : ammo.Id == id ? ammo.Count : 0));
            }
            return count;
        }
    }

    private CampaignCommandResult CompleteSale(BaseState origin, LogisticsQuote quote, LogisticsSelection[] selections,
        long subtotal, long funds, long accounting)
    {
        var planned = new BaseState(origin.Id, origin.Name, origin.Longitude, origin.Latitude, origin.Facilities,
            origin.Crafts, origin.Soldiers, new(origin.Items), origin.Scientists, origin.Engineers);
        planned.Transfers.AddRange(origin.Transfers);
        try
        {
            foreach (var selection in selections)
            {
                var row = quote.Rows[selection.RowId];
                if (row.Kind == CampaignTransferKind.Item)
                {
                    RemoveSaleItems(planned, row.RuleId, selection.Quantity);
                    continue;
                }
                if (row.Kind == CampaignTransferKind.Soldier)
                {
                    var personal = planned.Soldiers.Single(s => s.Id == row.EntityId).Personal!;
                    var armor = _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(personal.Armor)].Value;
                    if (armor.StoreItem is { } item) planned.Items[item] = checked(planned.Items.GetValueOrDefault(item) + 1);
                }
                if (row.Kind == CampaignTransferKind.Craft)
                {
                    foreach (var pair in UnloadedCraftItems(FindCraft(planned, row).Logistics!)) Store(planned, pair.Key, pair.Value);
                    for (var i = 0; i < planned.Soldiers.Count; i++)
                        if (planned.Soldiers[i].Personal is { } p && p.CraftType == row.RuleId && p.CraftId == row.EntityId)
                            planned.Soldiers[i] = planned.Soldiers[i] with { Personal = p with { CraftType = "", CraftId = 0 } };
                }
                RemoveStock(planned, row, selection.Quantity);
            }
        }
        catch (OverflowException) { return Blocked("Sale refunds exceed the item quantity range."); }
        catch (SaleCapabilityException error) { return Blocked(error.Message); }
        if (Options.StorageLimitsEnforced && StrategicLogisticsMath.StoresOverfull(AvailableStores(planned), UsedStores(planned)))
            return Blocked("Sell or transfer enough items to bring stores within capacity.");
        origin.Items.Clear();
        foreach (var pair in planned.Items) origin.Items.Add(pair.Key, pair.Value);
        origin.Crafts.Clear(); origin.Crafts.AddRange(planned.Crafts);
        origin.Soldiers.Clear(); origin.Soldiers.AddRange(planned.Soldiers);
        origin.Transfers.Clear(); origin.Transfers.AddRange(planned.Transfers);
        origin.Scientists = planned.Scientists; origin.Engineers = planned.Engineers;
        _funds[^1] = funds;
        if (subtotal > 0) _incomes[^1] = accounting; else _expenditures[^1] = accounting;
        _logisticsQuote = null;
        return new([new LogisticsOrderCompleted(origin.Id, LogisticsOperation.Sell, subtotal)]);
    }

    private void RemoveSaleItems(BaseState state, string id, int count)
    {
        var handle = _content.RuntimeRules.Items.GetRequired(id);
        var stored = state.Items.GetValueOrDefault(handle);
        var take = Math.Min(stored, count);
        count -= take;
        if (stored == take) state.Items.Remove(handle); else state.Items[handle] = stored - take;
        for (var i = 0; i < state.Crafts.Count && count > 0; i++)
            state.Crafts[i] = state.Crafts[i] with { Logistics = RemoveFromCraft(state.Crafts[i].Logistics!) };
        for (var i = 0; i < state.Transfers.Count && count > 0;)
        {
            var transfer = state.Transfers[i];
            if (transfer.Delivered) { i++; continue; }
            if (transfer.Kind == CampaignTransferKind.Item && transfer.RuleId == id)
            {
                var removed = Math.Min(count, transfer.Quantity);
                count -= removed;
                if (removed == transfer.Quantity) { state.Transfers.RemoveAt(i); continue; }
                state.Transfers[i] = transfer with { Quantity = transfer.Quantity - removed };
            }
            else if (transfer.Craft is { } craft)
                state.Transfers[i] = transfer with { Craft = craft with { Logistics = RemoveFromCraft(craft.Logistics!) } };
            i++;
        }
        if (count != 0) throw new InvalidDataException("Sale quantity exceeds available inventory.");

        CraftLogisticsState RemoveFromCraft(CraftLogisticsState craft)
        {
            var items = new Dictionary<string, int>(craft.Items, StringComparer.Ordinal);
            var stored = items.GetValueOrDefault(id);
            var removed = Math.Min(stored, count);
            count -= removed;
            if (stored == removed) items.Remove(id); else items[id] = stored - removed;
            var weapons = craft.Weapons.ToArray();
            for (var slot = 0; slot < weapons.Length && count > 0; slot++)
            {
                if (weapons[slot] is not { } weapon) continue;
                var rule = _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
                var launcher = Take(rule.Launcher, 1);
                var clips = Take(rule.Clip, WeaponClips(weapon));
                if (!launcher.Changed && !clips.Changed) continue;
                // SellState deletes these weapons without refreshing the craft's cached stats.
                // Readiness owns that mutable cache; do not silently substitute recalculated stats.
                if (rule.BonusStats.Any(p => p.Value != 0))
                    throw new SaleCapabilityException("Selling mounted bonus equipment requires craft readiness state.");
                Store(state, rule.Launcher, launcher.Remaining);
                Store(state, rule.Clip, clips.Remaining);
                weapons[slot] = null;
            }
            var vehicles = new List<CraftVehicleSnapshot>();
            foreach (var vehicle in craft.Vehicles)
            {
                var ammunition = VehicleClips(vehicle);
                var launcher = Take(vehicle.RuleId, 1);
                var clips = Take(ammunition.Id, ammunition.Count);
                if (!launcher.Changed && !clips.Changed) { vehicles.Add(vehicle); continue; }
                Store(state, vehicle.RuleId, launcher.Remaining);
                Store(state, ammunition.Id, clips.Remaining);
            }
            return craft with { Items = new ReadOnlyDictionary<string, int>(items), Weapons = Array.AsReadOnly(weapons), Vehicles = vehicles.AsReadOnly() };
        }

        (bool Changed, int Remaining) Take(string type, int quantity)
        {
            if (type != id) return (false, quantity);
            var removed = Math.Min(count, quantity);
            count -= removed;
            return (removed != 0, quantity - removed);
        }
    }

    private void Store(BaseState state, string id, int count)
    {
        if (id.Length == 0 || count <= 0) return;
        var handle = _content.RuntimeRules.Items.GetRequired(id);
        state.Items[handle] = checked(state.Items.GetValueOrDefault(handle) + count);
    }

    private int WeaponClips(CraftWeaponSnapshot weapon)
    {
        var rule = _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
        if (rule.Clip.Length == 0) return 0;
        var clip = _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(rule.Clip)].Value;
        var divisor = clip.ClipSize > 0 ? clip.ClipSize : rule.RearmRate;
        if (divisor <= 0) throw new InvalidDataException("Craft weapon clip divisor must be positive.");
        return checked((int)Math.Floor((double)weapon.Ammo / divisor));
    }

    private (string Id, int Count) VehicleClips(CraftVehicleSnapshot vehicle)
    {
        var rules = _content.RuntimeRules;
        var rule = rules.Items[rules.Items.GetRequired(vehicle.RuleId)].Value;
        if (rule.VehicleFixedAmmoSlot < 0) return ("", 0);
        if (rule.VehicleFixedAmmoSlot >= rule.CompatibleAmmo.Count) throw new InvalidDataException("Vehicle ammunition slot is outside the reference range.");
        var ammunition = rule.CompatibleAmmo[rule.VehicleFixedAmmoSlot];
        if (ammunition.Count == 0) return ("", 0);
        var ammo = rules.Items[ammunition[0]].Value;
        return (rules.Items.GetExternalId(ammunition[0]), rule.ClipSize > 0 && ammo.ClipSize > 0 ? rule.ClipSize / ammo.ClipSize : ammo.ClipSize);
    }

    private sealed class SaleCapabilityException(string message) : Exception(message);
}
