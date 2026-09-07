using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private CraftState FindCraft(BaseState state, LogisticsRow row) => state.Crafts.Single(c =>
        c.Id == row.EntityId && _content.RuntimeRules.Crafts.GetExternalId(c.Rule) == row.RuleId);

    private static IEnumerable<SoldierState> Crew(BaseState state, string type, int id) =>
        state.Soldiers.Where(s => s.Personal is { } p && p.CraftType == type && p.CraftId == id);

    private void AddCraftRows(BaseState origin, BaseState destination, List<LogisticsRow> rows,
        LogisticsOperation operation, double distance, int hours)
    {
        foreach (var craft in origin.Crafts)
        {
            var type = _content.RuntimeRules.Crafts.GetExternalId(craft.Rule);
            var rule = _content.RuntimeRules.Crafts[craft.Rule].Value;
            var state = craft.Logistics;
            string? reason = state is null ? "Craft logistics state is required for sale or transfer." : null;
            if (origin.Soldiers.Any(s => s.Personal is null)) reason ??= "Soldier assignments must be resolved before moving or selling craft.";
            if (state is { Status: "STR_OUT" }) reason ??= "Airborne craft require world simulation before sale or transfer.";
            if (operation == LogisticsOperation.Transfer)
            {
                if (state is { IsAutoPatrolling: true }) reason ??= "Craft auto-patrol requires world simulation.";
                if (UsedHangars(destination, rule.HangarType) >= AvailableHangars(destination, rule.HangarType)) reason ??= "No compatible destination hangar.";
                if (Crew(origin, type, craft.Id).Count() > AvailableQuarters(destination) - UsedQuarters(destination)) reason ??= "No living space for the crew.";
                if (Options.StorageLimitsEnforced && state is not null && StrategicLogisticsMath.StoresOverfull(AvailableStores(destination), UsedStores(destination), CraftStorage(state)))
                    reason ??= "No storage space for craft cargo.";
            }
            rows.Add(new(rows.Count, CampaignTransferKind.Craft, type, craft.Id, type, 1,
                operation == LogisticsOperation.Transfer ? StrategicLogisticsMath.TransferUnitCost(distance, CampaignTransferKind.Craft) : rule.CostSell,
                reason is null ? 1 : 0, hours, reason));
        }
    }

    private Dictionary<string, int> UnloadedCraftItems(CraftLogisticsState state)
    {
        var rules = _content.RuntimeRules;
        var items = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in CraftLogistics.UnloadedWeaponItems(state, rules)) Add(pair.Key, pair.Value);
        foreach (var pair in state.Items) Add(pair.Key, pair.Value);
        foreach (var vehicle in state.Vehicles)
        {
            Add(vehicle.RuleId, 1);
            var rule = rules.Items[rules.Items.GetRequired(vehicle.RuleId)].Value;
            if (rule.VehicleFixedAmmoSlot < 0) continue;
            if (rule.VehicleFixedAmmoSlot >= rule.CompatibleAmmo.Count) throw new InvalidDataException("Vehicle ammunition slot is outside the reference range.");
            var ammunition = rule.CompatibleAmmo[rule.VehicleFixedAmmoSlot];
            if (ammunition.Count == 0) continue;
            var ammo = rules.Items[ammunition[0]].Value;
            Add(rules.Items.GetExternalId(ammunition[0]), rule.ClipSize > 0 && ammo.ClipSize > 0 ? rule.ClipSize / ammo.ClipSize : ammo.ClipSize);
        }
        return items;

        void Add(string id, int count)
        {
            if (count <= 0 || id.Length == 0) return;
            items[id] = checked(items.GetValueOrDefault(id) + count);
        }
    }

    private double CraftStorage(CraftLogisticsState state)
    {
        var rules = _content.RuntimeRules;
        var size = state.Items.Sum(p => rules.Items[rules.Items.GetRequired(p.Key)].Value.Size * p.Value);
        foreach (var vehicle in state.Vehicles)
        {
            var rule = rules.Items[rules.Items.GetRequired(vehicle.RuleId)].Value;
            size += rule.Size;
            if (rule.VehicleFixedAmmoSlot < 0) continue;
            if (rule.VehicleFixedAmmoSlot >= rule.CompatibleAmmo.Count) throw new InvalidDataException("Vehicle ammunition slot is outside the reference range.");
            var ammunition = rule.CompatibleAmmo[rule.VehicleFixedAmmoSlot];
            if (ammunition.Count == 0) continue;
            var ammo = rules.Items[ammunition[0]].Value;
            var clips = rule.ClipSize > 0 && ammo.ClipSize > 0 ? rule.ClipSize / ammo.ClipSize : ammo.ClipSize;
            size += ammo.Size * clips;
        }
        foreach (var weapon in state.Weapons.OfType<CraftWeaponSnapshot>())
        {
            var rule = rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
            size += rules.Items[rules.Items.GetRequired(rule.Launcher)].Value.Size;
            if (rule.Clip.Length == 0) continue;
            var clip = rules.Items[rules.Items.GetRequired(rule.Clip)].Value;
            var divisor = clip.ClipSize > 0 ? clip.ClipSize : rule.RearmRate;
            if (divisor <= 0) throw new InvalidDataException("Craft weapon clip divisor must be positive.");
            size += clip.Size * Math.Floor((double)weapon.Ammo / divisor);
        }
        return size;
    }

    private void RefuelArrivingCrafts(TimeEffects effects)
    {
        if (effects.ArrivingCrafts is null) return;
        // The hourly arrival popup does not cancel the current tick's half-hour handler.
        foreach (var arriving in effects.ArrivingCrafts)
        {
            var owner = FindBase(arriving.BaseId);
            var index = owner.Crafts.FindIndex(c => c.Id == arriving.Id && _content.RuntimeRules.Crafts.GetExternalId(c.Rule) == arriving.Type);
            var craft = owner.Crafts[index];
            var state = craft.Logistics!;
            if (state.Status != "STR_REFUELLING") continue;
            var rule = _content.RuntimeRules.Crafts[craft.Rule].Value;
            var result = CraftLogistics.Refuel(state, rule, _content.RuntimeRules,
                rule.RefuelItem is { } fuelItem ? owner.Items.GetValueOrDefault(fuelItem) : 0);
            state = result.State;
            if (rule.RefuelItem is { } item && result.FuelItemChange != 0)
            {
                var quantity = checked(owner.Items.GetValueOrDefault(item) + result.FuelItemChange);
                if (quantity == 0) owner.Items.Remove(item); else owner.Items[item] = quantity;
            }
            owner.Crafts[index] = craft with { Logistics = state };
            if (result.MissingFuel) effects.Notify(new CraftArrivalServiceMessage(owner.Id, arriving.Type, arriving.Id, "STR_NOT_ENOUGH_ITEM_TO_REFUEL_CRAFT_AT_BASE"));
            else if (state.Status == "STR_READY" && rule.NotifyWhenRefueled)
                effects.Notify(new CraftArrivalServiceMessage(owner.Id, arriving.Type, arriving.Id, "STR_CRAFT_IS_READY"));
        }
    }

    private void AddCraftPurchaseRows(BaseState state, List<LogisticsRow> rows)
    {
        foreach (var entry in _content.RuntimeRules.Crafts.Rules)
        {
            var rule = entry.Value;
            if (rule.CostBuy == 0) continue;
            var reason = PurchaseRestriction(state, rule.Purchase);
            if (!_debugMode && rule.Requirements.Any(r => !_completedResearch.Contains(r.Id))) reason ??= "Required research is not complete.";
            if (rule.WeaponSlots is < 0 or > 4) reason ??= "Craft weapon slot count is outside the reference range.";
            var maximum = Math.Min(MaximumLogisticsLines, Math.Max(0, AvailableHangars(state, rule.HangarType) - UsedHangars(state, rule.HangarType)));
            if (rule.CostBuy > 0) maximum = (int)Math.Min(maximum, Math.Max(0, _funds[^1] / rule.CostBuy));
            if (rule.Purchase.MonthlyLimit > 0) maximum = Math.Min(maximum, Math.Max(0, rule.Purchase.MonthlyLimit - _monthlyPurchaseLog.GetValueOrDefault(entry.Id)));
            if (maximum == 0) reason ??= "No funds, compatible hangar or monthly purchase allowance.";
            rows.Add(new(rows.Count, CampaignTransferKind.Craft, entry.Id, 0, entry.Id,
                state.Crafts.Count(c => c.Rule == _content.RuntimeRules.Crafts.GetRequired(entry.Id)), rule.CostBuy,
                reason is null ? maximum : 0, rule.TransferTime, reason));
        }
    }

    private int NextCraftId(string type)
    {
        var highest = _bases.SelectMany(b => b.Crafts.Where(c => _content.RuntimeRules.Crafts.GetExternalId(c.Rule) == type).Select(c => c.Id)
            .Concat(b.Transfers.Where(t => t.Craft?.RuleId == type).Select(t => t.Craft!.Id))).DefaultIfEmpty(0).Max();
        return Math.Max(_nextIds.GetValueOrDefault(type, 1), highest == int.MaxValue ? int.MaxValue : highest + 1);
    }

    private int AvailableHangars(BaseState state, int type) => state.Facilities.Where(f => f.BuildTime == 0)
        .Select(f => _content.RuntimeRules.Facilities[f.Rule].Value).Where(f => f.HangarType == type).Sum(f => f.Crafts);

    private int UsedHangars(BaseState state, int type) => state.Crafts.Count(c => _content.RuntimeRules.Crafts[c.Rule].Value.HangarType == type) +
        state.Transfers.Where(t => !t.Delivered && t.Craft is not null &&
            _content.RuntimeRules.Crafts[_content.RuntimeRules.Crafts.GetRequired(t.Craft.RuleId)].Value.HangarType == type).Sum(t => t.Quantity);
}
