using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private ReadOnlyDictionary<string, int> SaleInventory(BaseState state)
    {
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in state.Items)
            Add(_content.RuntimeRules.Items.GetExternalId(pair.Key), pair.Value);
        foreach (var craft in state.Crafts)
            if (craft.Logistics is { } logistics) AddCraft(logistics);
        foreach (var transfer in state.Transfers.Where(t => !t.Delivered))
            if (transfer.Kind == CampaignTransferKind.Item) Add(transfer.RuleId, transfer.Quantity);
            else if (transfer.Craft?.Logistics is { } logistics) AddCraft(logistics);
        return new ReadOnlyDictionary<string, int>(items);

        void AddCraft(CraftLogisticsState logistics)
        {
            foreach (var pair in CraftLogistics.UnloadedItems(logistics, _content.RuntimeRules)) Add(pair.Key, pair.Value);
        }

        void Add(string id, int count)
        {
            if (count > 0) items[id] = checked(items.GetValueOrDefault(id) + count);
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
                    foreach (var pair in CraftLogistics.UnloadedItems(FindCraft(planned, row).Logistics!, _content.RuntimeRules)) Store(planned, pair.Key, pair.Value);
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
            state.Crafts[i] = state.Crafts[i] with
            {
                Logistics = RemoveFromCraft(state.Crafts[i].Logistics!,
                _content.RuntimeRules.Crafts[state.Crafts[i].Rule].Value,
                _content.RuntimeRules.Crafts.GetExternalId(state.Crafts[i].Rule), state.Crafts[i].Id)
            };
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
                state.Transfers[i] = transfer with
                {
                    Craft = craft with
                    {
                        Logistics = RemoveFromCraft(craft.Logistics!,
                    _content.RuntimeRules.Crafts[_content.RuntimeRules.Crafts.GetRequired(craft.RuleId)].Value, craft.RuleId, craft.Id)
                    }
                };
            i++;
        }
        if (count != 0) throw new InvalidDataException("Sale quantity exceeds available inventory.");

        CraftLogisticsState RemoveFromCraft(CraftLogisticsState craft, RuntimeCraftRule craftRule, string craftType, int craftId)
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
                var clips = Take(rule.Clip, CraftLogistics.WeaponClipCount(weapon, _content.RuntimeRules));
                if (!launcher.Changed && !clips.Changed) continue;
                Store(state, rule.Launcher, launcher.Remaining);
                Store(state, rule.Clip, clips.Remaining);
                weapons[slot] = null;
            }
            var vehicles = new List<CraftVehicleSnapshot>();
            foreach (var vehicle in craft.Vehicles)
            {
                var ammunition = CraftLogistics.VehicleAmmunition(vehicle, _content.RuntimeRules);
                var launcher = Take(vehicle.RuleId, 1);
                var clips = Take(ammunition.Id, ammunition.Count);
                if (!launcher.Changed && !clips.Changed) { vehicles.Add(vehicle); continue; }
                Store(state, vehicle.RuleId, launcher.Remaining);
                Store(state, ammunition.Id, clips.Remaining);
            }
            var effective = CraftLogistics.EffectiveStats(craftRule, weapons, _content.RuntimeRules);
            var assigned = state.Soldiers.Where(s => s.Personal?.CraftType == craftType && s.Personal.CraftId == craftId)
                .Sum(s => _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(s.Personal!.Armor)].Value.SpaceOccupied);
            var vehicleSpace = vehicles.Sum(v => v.SpaceOccupied ?? _content.RuntimeRules.Armors[
                _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(v.RuleId)].Value.VehicleArmor!.Value].Value.SpaceOccupied);
            var largeSoldiers = state.Soldiers.Count(s => s.Personal?.CraftType == craftType && s.Personal.CraftId == craftId &&
                _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(s.Personal.Armor)].Value.Size != 1);
            if (assigned + vehicleSpace > effective.SoldierCapacity || vehicles.Count + largeSoldiers > effective.VehicleCapacity)
                throw new SaleCapabilityException("Selling mounted equipment would exceed craft capacity.");
            return craft with
            {
                Items = new ReadOnlyDictionary<string, int>(items),
                Weapons = Array.AsReadOnly(weapons),
                Vehicles = vehicles.AsReadOnly(),
                Fuel = Math.Clamp(craft.Fuel, 0, Math.Max(0, effective.FuelMaximum)),
                Shield = Math.Clamp(craft.Shield, 0, Math.Max(0, effective.ShieldMaximum))
            };
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

    private sealed class SaleCapabilityException(string message) : Exception(message);
}
