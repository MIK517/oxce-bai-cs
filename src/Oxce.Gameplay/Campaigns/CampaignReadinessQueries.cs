using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public interface ICampaignReadinessQuery
{
    CampaignBaseReadiness QueryReadiness(int baseId);
    IReadOnlyList<CampaignBaseSite> QueryBaseSites(bool startingBase);
    CampaignBaseSite QueryBaseSite(double longitude, double latitude, bool startingBase = false);
    CampaignBaseManagement QueryBaseManagement(int baseId);
}

public sealed record CampaignWeaponReadiness(string RuleId, int Ammo, int MaximumAmmo, bool Rearming, bool Disabled);
public sealed record CampaignCraftReadiness(string RuleId, int Id, string Name, string Status,
    int Fuel, int FuelMaximum, int Damage, int Shield, int ShieldMaximum,
    IReadOnlyList<CampaignWeaponReadiness?> Weapons, IReadOnlyList<string> Vehicles);
public sealed record CampaignDefenseReadiness(string RuleId, int X, int Y, int Ammo, int AmmoMaximum, int BuildTime, bool Disabled);
public sealed record CampaignBaseReadiness(IReadOnlyList<CampaignCraftReadiness> Crafts,
    IReadOnlyList<CampaignDefenseReadiness> Defenses, string? ServiceLimitation);
public sealed record CampaignFacilityChoice(string RuleId, int SizeX, int SizeY, int Cost, int BuildTime, bool Lift, string? UnavailableReason);
public sealed record CampaignSoldierReadiness(int Id, string Name, string Armor, bool Training, bool PsiTraining,
    bool Wounded, string CraftRuleId, int CraftId);
public sealed record CampaignBaseManagement(IReadOnlyList<CampaignFacilityChoice> Facilities,
    IReadOnlyList<CampaignSoldierReadiness> Soldiers, IReadOnlyList<string> Armors,
    IReadOnlyList<string> CraftWeapons, IReadOnlyList<string> Vehicles, CampaignMaintenance Maintenance);
public sealed record CampaignMaintenance(long Facilities, long Craft, long Personnel, long Inventory)
{
    public long Total => checked(Facilities + Craft + Personnel + Inventory);
}

public sealed partial class CampaignState : ICampaignReadinessQuery
{
    public CampaignBaseReadiness QueryReadiness(int baseId)
    {
        lock (_transactionGate)
        {
            var owner = FindBase(baseId);
            var crafts = owner.Crafts.Select(craft =>
            {
                var rule = _content.RuntimeRules.Crafts[craft.Rule].Value;
                var state = craft.Logistics;
                var fuelMaximum = rule.FuelMaximum;
                var shieldMaximum = rule.ShieldCapacity;
                var weapons = state?.Weapons.Select(weapon =>
                {
                    if (weapon is null) return null;
                    var definition = _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
                    fuelMaximum = checked(fuelMaximum + definition.BonusStats.GetValueOrDefault("fuelMax"));
                    shieldMaximum = checked(shieldMaximum + definition.BonusStats.GetValueOrDefault("shieldCapacity"));
                    return new CampaignWeaponReadiness(weapon.RuleId, weapon.Ammo, definition.AmmoMaximum, weapon.Rearming, weapon.Disabled);
                }).ToArray() ?? [];
                return new CampaignCraftReadiness(_content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id,
                    state?.Name ?? "", state?.Status ?? "Unresolved craft state", state?.Fuel ?? 0, fuelMaximum,
                    state?.Damage ?? 0, state?.Shield ?? 0, shieldMaximum, Array.AsReadOnly(weapons),
                    CampaignSnapshot.ReadOnly(state?.Vehicles.Select(v => v.RuleId) ?? []));
            });
            var defenses = owner.Facilities.Select(f => new CampaignDefenseReadiness(
                _content.RuntimeRules.Facilities.GetExternalId(f.Rule), f.X, f.Y, f.Ammo,
                _content.RuntimeRules.Facilities[f.Rule].Value.AmmoMaximum, f.BuildTime, f.Disabled));
            return new(CampaignSnapshot.ReadOnly(crafts), CampaignSnapshot.ReadOnly(defenses),
                _restrictions.FirstOrDefault(r => r.BlocksTime)?.Feature ?? PreflightServicing());
        }
    }

    public CampaignBaseManagement QueryBaseManagement(int baseId)
    {
        lock (_transactionGate)
        {
            var owner = FindBase(baseId);
            var facilities = _content.RuntimeRules.Facilities.Rules.Select(entry =>
            {
                var rule = entry.Value;
                string? reason = null;
                if (!FacilityAllowed(rule, owner.FakeUnderwater)) reason = "Incompatible base type";
                else if (!_debugMode && rule.Requirements.Any(r => !_completedResearch.Contains(r.Id))) reason = "Research required";
                else if (!HasFunctions(owner, rule.RequiredBaseFunctions)) reason = "Base function required";
                return new CampaignFacilityChoice(entry.Id, rule.SizeX, rule.SizeY, rule.BuildCost, rule.BuildTime, rule.Lift, reason);
            });
            var soldiers = owner.Soldiers.Where(s => s.Personal is not null).Select(s => new CampaignSoldierReadiness(s.Id,
                s.Personal!.Name, s.Personal.Armor, s.Personal.Training, s.Personal.PsiTraining,
                SoldierReadiness.IsWounded(s.Personal, _content.RuntimeRules.Campaign.ManaWoundThreshold,
                    _content.RuntimeRules.Campaign.HealthWoundThreshold), s.Personal.CraftType, s.Personal.CraftId));
            return new(CampaignSnapshot.ReadOnly(facilities), CampaignSnapshot.ReadOnly(soldiers),
                CampaignSnapshot.ReadOnly(_content.RuntimeRules.Armors.Rules.Select(r => r.Id)),
                CampaignSnapshot.ReadOnly(_content.RuntimeRules.CraftWeapons.Rules.Select(r => r.Id)),
                CampaignSnapshot.ReadOnly(_content.RuntimeRules.Items.Rules.Where(r => r.Value.VehicleArmor is not null).Select(r => r.Id)),
                CalculateMaintenance(owner));
        }
    }

    private CampaignMaintenance CalculateMaintenance(BaseState owner)
    {
        long facilities = owner.Facilities.Where(f => f.BuildTime == 0).Sum(f => (long)FacilityRule(f).MonthlyCost);
        long craft = owner.Crafts.Sum(c => (long)_content.RuntimeRules.Crafts[c.Rule].Value.CostRent) +
            owner.Transfers.Where(t => !t.Delivered && t.Craft is not null).Sum(t =>
                (long)_content.RuntimeRules.Crafts[_content.RuntimeRules.Crafts.GetRequired(t.Craft!.RuleId)].Value.CostRent);
        long personnel = checked((owner.Scientists + owner.Transfers.Where(t => !t.Delivered && t.Kind == CampaignTransferKind.Scientist).Sum(t => t.Quantity)) *
            (long)_content.RuntimeRules.Campaign.CostScientist +
            (owner.Engineers + owner.Transfers.Where(t => !t.Delivered && t.Kind == CampaignTransferKind.Engineer).Sum(t => t.Quantity)) *
            (long)_content.RuntimeRules.Campaign.CostEngineer);
        foreach (var soldier in owner.Soldiers.Select(s => (s.Rule, s.Personal?.Rank ?? 0)).Concat(owner.Transfers
            .Where(t => !t.Delivered && t.Soldier is not null).Select(t => (_content.RuntimeRules.Soldiers.GetRequired(t.Soldier!.RuleId), t.Soldier.Personal?.Rank ?? 0))))
        {
            var salaries = _content.RuntimeRules.Soldiers[soldier.Item1].Value.Salaries;
            personnel = checked(personnel + salaries[Math.Clamp(soldier.Item2, 0, salaries.Count - 1)]);
        }
        long inventory = owner.Items.Sum(pair => (long)pair.Value * ItemMonthlyCost(pair.Key));
        inventory = checked(inventory + owner.Transfers.Where(t => !t.Delivered && t.Kind == CampaignTransferKind.Item).Sum(t =>
            (long)t.Quantity * ItemMonthlyCost(_content.RuntimeRules.Items.GetRequired(t.RuleId))));
        foreach (var soldier in owner.Soldiers.Select(s => s.Personal).Concat(owner.Transfers.Where(t => !t.Delivered && t.Soldier is not null).Select(t => t.Soldier!.Personal)).OfType<SoldierPersonalState>())
        {
            var storeItem = _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(soldier.Armor)].Value.StoreItem;
            if (storeItem is { } item) inventory = checked(inventory + ItemMonthlyCost(item));
        }
        foreach (var state in owner.Crafts.Select(c => c.Logistics).OfType<CraftLogisticsState>())
        {
            inventory = checked(inventory + state.Items.Sum(pair => (long)pair.Value *
                ItemMonthlyCost(_content.RuntimeRules.Items.GetRequired(pair.Key))));
            inventory = checked(inventory + state.Vehicles.Sum(v => ItemMonthlyCost(_content.RuntimeRules.Items.GetRequired(v.RuleId))));
        }
        return new(facilities, craft, personnel, inventory);

        long ItemMonthlyCost(RuleHandle<ItemRuleFamily> item)
        {
            var rule = _content.RuntimeRules.Items[item].Value;
            return checked((long)rule.MonthlySalary + rule.MonthlyMaintenance);
        }
    }
}
