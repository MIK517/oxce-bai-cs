using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed record SetSoldierTraining(int BaseId, int SoldierId, bool Physical, bool Psi) : ICampaignCommand;
public sealed record AssignSoldierToCraft(int BaseId, int SoldierId, string CraftRuleId = "", int CraftId = 0) : ICampaignCommand;
public sealed record EquipSoldierArmor(int BaseId, int SoldierId, string ArmorRuleId) : ICampaignCommand;
public sealed record EquipCraftWeapon(int BaseId, string CraftRuleId, int CraftId, int Slot, string WeaponRuleId) : ICampaignCommand;
public sealed record ChangeCraftVehicle(int BaseId, string CraftRuleId, int CraftId, string VehicleRuleId, bool Add) : ICampaignCommand;
public sealed record CampaignPersonnelChanged(int BaseId, int SoldierId) : ICampaignEvent;
public sealed record CampaignConstructionCompleted(int BaseId, string RuleId, int X, int Y) : ICampaignEvent;
public sealed record CampaignTrainingCompleted(int BaseId, int SoldierId, bool Psi) : ICampaignEvent;

public sealed partial class CampaignState
{
    private CampaignCommandResult SetTraining(SetSoldierTraining command)
    {
        var owner = FindBase(command.BaseId);
        var index = owner.Soldiers.FindIndex(s => s.Id == command.SoldierId);
        if (index < 0) return Blocked("Soldier was not found.");
        var soldier = owner.Soldiers[index];
        if (soldier.Personal is not { } personal) return Blocked("Soldier state is unresolved.");
        var soldierRule = _content.RuntimeRules.Soldiers[soldier.Rule].Value;
        if (command.Physical && !personal.Training && SoldierReadiness.IsFullyTrained(personal, soldierRule))
            return Blocked("Soldier has completed physical training.");
        var wounded = SoldierReadiness.IsWounded(personal, _content.RuntimeRules.Campaign.ManaWoundThreshold,
            _content.RuntimeRules.Campaign.HealthWoundThreshold);
        if (command.Physical && !wounded && !personal.Training &&
            owner.Soldiers.Count(s => s.Personal?.Training == true) >= AvailableTraining(owner))
            return Blocked("STR_NOT_ENOUGH_GYM_SPACE");
        if (command.Psi && !personal.PsiTraining && owner.Soldiers.Count(s => s.Personal?.PsiTraining == true) >= AvailablePsi(owner))
            return Blocked("STR_NOT_ENOUGH_PSI_LAB_SPACE");
        if (command.Psi && !_debugMode && _content.RuntimeRules.Campaign.PsiUnlockResearch is { } research &&
            !_completedResearch.Contains(_content.RuntimeRules.Research.GetExternalId(research)))
            return Blocked("Required research is not complete.");
        personal = personal with
        {
            Training = command.Physical && !wounded,
            ReturnToTrainingWhenHealed = command.Physical && wounded,
            PsiTraining = command.Psi,
        };
        owner.Soldiers[index] = soldier with { Personal = personal };
        return new([new CampaignPersonnelChanged(owner.Id, soldier.Id)]);
    }

    private CampaignCommandResult AssignSoldier(AssignSoldierToCraft command)
    {
        var owner = FindBase(command.BaseId);
        var index = owner.Soldiers.FindIndex(s => s.Id == command.SoldierId);
        if (index < 0 || owner.Soldiers[index].Personal is not { } personal) return Blocked("Soldier was not found or is unresolved.");
        if (command.CraftRuleId.Length == 0)
        {
            owner.Soldiers[index] = owner.Soldiers[index] with { Personal = personal with { CraftType = "", CraftId = 0 } };
            return new([new CampaignPersonnelChanged(owner.Id, command.SoldierId)]);
        }
        var craft = owner.Crafts.SingleOrDefault(c => c.Id == command.CraftId && _content.RuntimeRules.Crafts.GetExternalId(c.Rule) == command.CraftRuleId);
        if (craft is null || craft.Logistics is null) return Blocked("Craft was not found or is unresolved.");
        var rule = _content.RuntimeRules.Crafts[craft.Rule].Value;
        var armor = _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(personal.Armor)].Value;
        var crew = Crew(owner, command.CraftRuleId, command.CraftId).Where(s => s.Id != command.SoldierId).ToArray();
        var occupied = crew.Sum(s => _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(s.Personal!.Armor)].Value.SpaceOccupied);
        var vehicleSpace = craft.Logistics.Vehicles.Sum(v => v.SpaceOccupied ?? _content.RuntimeRules.Armors[
            _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(v.RuleId)].Value.VehicleArmor!.Value].Value.SpaceOccupied);
        var capacity = Math.Min(Math.Max(0, checked(rule.SoldierCapacity + craft.Logistics.Weapons.OfType<CraftWeaponSnapshot>().Sum(w =>
            _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(w.RuleId)].Value.BonusStats.GetValueOrDefault("soldiers")))), rule.EffectiveMaximumUnits);
        var soldierRule = _content.RuntimeRules.Soldiers[owner.Soldiers[index].Rule].Value;
        var small = crew.Count(s => _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(s.Personal!.Armor)].Value.Size == 1);
        var large = crew.Length - small;
        var smallVehicles = craft.Logistics.Vehicles.Count(v => (v.Size ?? _content.RuntimeRules.Armors[
            _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(v.RuleId)].Value.VehicleArmor!.Value].Value.Size) == 1);
        var largeVehicles = craft.Logistics.Vehicles.Count - smallVehicles;
        if (occupied + vehicleSpace + armor.SpaceOccupied > capacity ||
            rule.MaximumSoldiers >= 0 && crew.Length + 1 > rule.MaximumSoldiers ||
            rule.MaximumSmallSoldiers >= 0 && small + (armor.Size == 1 ? 1 : 0) > rule.MaximumSmallSoldiers ||
            rule.MaximumLargeSoldiers >= 0 && large + (armor.Size == 1 ? 0 : 1) > rule.MaximumLargeSoldiers ||
            rule.MaximumSmallUnits >= 0 && small + smallVehicles + (armor.Size == 1 ? 1 : 0) > rule.MaximumSmallUnits ||
            rule.MaximumLargeUnits >= 0 && large + largeVehicles + (armor.Size == 1 ? 0 : 1) > rule.MaximumLargeUnits ||
            armor.Size != 1 && large + largeVehicles >= CraftVehicleCapacity(rule, craft.Logistics.Weapons) ||
            rule.AllowedSoldierGroups.Count != 0 && !rule.AllowedSoldierGroups.Contains(soldierRule.Group) ||
            rule.OnlyOneSoldierGroupAllowed && crew.Length != 0 && _content.RuntimeRules.Soldiers[crew[0].Rule].Value.Group != soldierRule.Group ||
            rule.AllowedArmorGroups.Count != 0 && !rule.AllowedArmorGroups.Contains(armor.Group) ||
            rule.ArmorGroupLimits.TryGetValue(armor.Group, out var limit) && crew.Count(s => _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(s.Personal!.Armor)].Value.Group == armor.Group) >= limit ||
            !rule.AllowLanding && (crew.Length >= rule.Pilots || !SoldierPiloting.MeetsRequirements(personal, soldierRule, rule, _content.RuntimeRules)))
            return Blocked("Soldier does not fit this craft.");
        owner.Soldiers[index] = owner.Soldiers[index] with { Personal = personal with { CraftType = command.CraftRuleId, CraftId = command.CraftId } };
        return new([new CampaignPersonnelChanged(owner.Id, command.SoldierId)]);
    }

    private CampaignCommandResult EquipArmor(EquipSoldierArmor command)
    {
        var owner = FindBase(command.BaseId);
        var index = owner.Soldiers.FindIndex(s => s.Id == command.SoldierId);
        if (index < 0 || owner.Soldiers[index].Personal is not { } personal) return Blocked("Soldier was not found or is unresolved.");
        var armorHandle = _content.RuntimeRules.Armors.GetRequired(command.ArmorRuleId);
        var next = _content.RuntimeRules.Armors[armorHandle].Value;
        var previous = _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(personal.Armor)].Value;
        if (next.StoreItem is { } nextItem && owner.Items.GetValueOrDefault(nextItem) <= 0) return Blocked("Required armor is not in stores.");
        if (personal.CraftType.Length != 0)
        {
            var craft = owner.Crafts.SingleOrDefault(c => c.Id == personal.CraftId &&
                _content.RuntimeRules.Crafts.GetExternalId(c.Rule) == personal.CraftType);
            if (craft?.Logistics is null || !CanChangeAssignedArmor(owner, craft, command.SoldierId, previous, next))
                return Blocked("STR_NOT_ENOUGH_CRAFT_SPACE");
        }
        var stock = new Dictionary<RuleHandle<ItemRuleFamily>, int>(owner.Items);
        if (next.StoreItem is { } consume) ChangeStock(stock, consume, -1);
        if (previous.StoreItem is { } restore) ChangeStock(stock, restore, 1);
        owner.Items.Clear();
        foreach (var pair in stock) owner.Items.Add(pair.Key, pair.Value);
        owner.Soldiers[index] = owner.Soldiers[index] with { Personal = personal with { Armor = command.ArmorRuleId } };
        return new([new CampaignPersonnelChanged(owner.Id, command.SoldierId)]);
    }

    private bool CanChangeAssignedArmor(BaseState owner, CraftState craft, int soldierId,
        RuntimeArmorRule previous, RuntimeArmorRule next)
    {
        if (previous.Size == next.Size) return true;
        var rule = _content.RuntimeRules.Crafts[craft.Rule].Value;
        var crew = Crew(owner, _content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id).ToArray();
        var otherCrew = crew.Where(s => s.Id != soldierId).ToArray();
        var occupied = crew.Sum(s => _content.RuntimeRules.Armors[
            _content.RuntimeRules.Armors.GetRequired(s.Personal!.Armor)].Value.SpaceOccupied);
        var vehicleSpace = craft.Logistics!.Vehicles.Sum(v => v.SpaceOccupied ?? _content.RuntimeRules.Armors[
            _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(v.RuleId)].Value.VehicleArmor!.Value].Value.SpaceOccupied);
        var capacity = Math.Min(Math.Max(0, checked(rule.SoldierCapacity + craft.Logistics.Weapons.OfType<CraftWeaponSnapshot>().Sum(w =>
            _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(w.RuleId)].Value.BonusStats.GetValueOrDefault("soldiers")))),
            rule.EffectiveMaximumUnits);
        if (previous.Size < next.Size && occupied + vehicleSpace + next.Size - previous.Size > capacity) return false;
        var smallSoldiers = otherCrew.Count(s => _content.RuntimeRules.Armors[
            _content.RuntimeRules.Armors.GetRequired(s.Personal!.Armor)].Value.Size == 1);
        var largeSoldiers = otherCrew.Length - smallSoldiers;
        var smallVehicles = craft.Logistics.Vehicles.Count(v => (v.Size ?? _content.RuntimeRules.Armors[
            _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(v.RuleId)].Value.VehicleArmor!.Value].Value.Size) == 1);
        var largeVehicles = craft.Logistics.Vehicles.Count - smallVehicles;
        return (rule.MaximumSmallSoldiers < 0 || smallSoldiers + (next.Size == 1 ? 1 : 0) <= rule.MaximumSmallSoldiers) &&
            (rule.MaximumLargeSoldiers < 0 || largeSoldiers + (next.Size == 1 ? 0 : 1) <= rule.MaximumLargeSoldiers) &&
            (rule.MaximumSmallUnits < 0 || smallSoldiers + smallVehicles + (next.Size == 1 ? 1 : 0) <= rule.MaximumSmallUnits) &&
            (rule.MaximumLargeUnits < 0 || largeSoldiers + largeVehicles + (next.Size == 1 ? 0 : 1) <= rule.MaximumLargeUnits) &&
            (next.Size == 1 || largeSoldiers + largeVehicles < rule.EffectiveMaximumVehiclesAndLargeSoldiers);
    }

    private CampaignCommandResult EquipWeapon(EquipCraftWeapon command)
    {
        var owner = FindBase(command.BaseId);
        var index = owner.Crafts.FindIndex(c => c.Id == command.CraftId && _content.RuntimeRules.Crafts.GetExternalId(c.Rule) == command.CraftRuleId);
        if (index < 0 || owner.Crafts[index].Logistics is not { } logistics) return Blocked("Craft was not found or is unresolved.");
        if ((uint)command.Slot >= (uint)logistics.Weapons.Count) return Blocked("Craft weapon slot is outside its range.");
        var craftRule = _content.RuntimeRules.Crafts[owner.Crafts[index].Rule].Value;
        if (command.Slot < craftRule.FixedWeaponSlots.Count && craftRule.FixedWeaponSlots[command.Slot].Length != 0)
            return Blocked("This craft weapon slot is fixed.");
        var weapons = logistics.Weapons.ToArray();
        var stock = new Dictionary<RuleHandle<ItemRuleFamily>, int>(owner.Items);
        if (weapons[command.Slot] is { } old)
        {
            var oldRule = _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(old.RuleId)].Value;
            if (oldRule.Launcher.Length != 0) ChangeStock(stock, _content.RuntimeRules.Items.GetRequired(oldRule.Launcher), 1);
            var clips = CraftLogistics.WeaponClipCount(old, _content.RuntimeRules);
            if (clips != 0 && oldRule.Clip.Length != 0) ChangeStock(stock, _content.RuntimeRules.Items.GetRequired(oldRule.Clip), clips);
        }
        if (command.WeaponRuleId.Length == 0) weapons[command.Slot] = null;
        else
        {
            var weaponRule = _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(command.WeaponRuleId)].Value;
            var launcher = _content.RuntimeRules.Items.GetRequired(weaponRule.Launcher);
            if (stock.GetValueOrDefault(launcher) <= 0) return Blocked("Required craft weapon is not in stores.");
            ChangeStock(stock, launcher, -1);
            weapons[command.Slot] = new(command.WeaponRuleId, 0, true);
        }
        var soldierCapacity = Math.Min(Math.Max(0, checked(craftRule.SoldierCapacity + weapons.OfType<CraftWeaponSnapshot>().Sum(w =>
            _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(w.RuleId)].Value.BonusStats.GetValueOrDefault("soldiers")))), craftRule.EffectiveMaximumUnits);
        var vehicleCapacity = CraftVehicleCapacity(craftRule, weapons);
        var crewSpace = Crew(owner, command.CraftRuleId, command.CraftId).Sum(s =>
            _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(s.Personal!.Armor)].Value.SpaceOccupied);
        var vehicleSpace = logistics.Vehicles.Sum(v => v.SpaceOccupied ?? _content.RuntimeRules.Armors[
            _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(v.RuleId)].Value.VehicleArmor!.Value].Value.SpaceOccupied);
        var largeSoldiers = Crew(owner, command.CraftRuleId, command.CraftId).Count(s =>
            _content.RuntimeRules.Armors[_content.RuntimeRules.Armors.GetRequired(s.Personal!.Armor)].Value.Size != 1);
        if (crewSpace + vehicleSpace > soldierCapacity || logistics.Vehicles.Count + largeSoldiers > vehicleCapacity)
            return Blocked("Removing this weapon would exceed craft capacity.");
        owner.Items.Clear(); foreach (var pair in stock) owner.Items.Add(pair.Key, pair.Value);
        var fuelMaximum = checked(craftRule.FuelMaximum + weapons.OfType<CraftWeaponSnapshot>().Sum(w =>
            _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(w.RuleId)].Value.BonusStats.GetValueOrDefault("fuelMax")));
        var shieldMaximum = checked(craftRule.ShieldCapacity + weapons.OfType<CraftWeaponSnapshot>().Sum(w =>
            _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(w.RuleId)].Value.BonusStats.GetValueOrDefault("shieldCapacity")));
        owner.Crafts[index] = owner.Crafts[index] with
        {
            Logistics = logistics with
            {
                Weapons = Array.AsReadOnly(weapons),
                Status = "STR_REARMING",
                Fuel = Math.Clamp(logistics.Fuel, 0, Math.Max(0, fuelMaximum)),
                Shield = Math.Clamp(logistics.Shield, 0, Math.Max(0, shieldMaximum))
            }
        };
        return new([new CampaignPersonnelChanged(owner.Id, 0)]);
    }

    private CampaignCommandResult ChangeVehicle(ChangeCraftVehicle command)
    {
        var owner = FindBase(command.BaseId);
        var index = owner.Crafts.FindIndex(c => c.Id == command.CraftId && _content.RuntimeRules.Crafts.GetExternalId(c.Rule) == command.CraftRuleId);
        if (index < 0 || owner.Crafts[index].Logistics is not { } logistics) return Blocked("Craft was not found or is unresolved.");
        var itemHandle = _content.RuntimeRules.Items.GetRequired(command.VehicleRuleId);
        var item = _content.RuntimeRules.Items[itemHandle].Value;
        if (item.VehicleArmor is not { } armorHandle) return Blocked("Item is not a craft vehicle.");
        var armor = _content.RuntimeRules.Armors[armorHandle].Value;
        var vehicles = logistics.Vehicles.ToList();
        var stock = new Dictionary<RuleHandle<ItemRuleFamily>, int>(owner.Items);
        if (command.Add)
        {
            var craft = _content.RuntimeRules.Crafts[owner.Crafts[index].Rule].Value;
            var capacity = CraftVehicleCapacity(craft, logistics.Weapons);
            var occupied = vehicles.Sum(v => v.SpaceOccupied ?? _content.RuntimeRules.Armors[
                _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(v.RuleId)].Value.VehicleArmor!.Value].Value.SpaceOccupied);
            var crew = Crew(owner, command.CraftRuleId, command.CraftId).ToArray();
            var crewSpace = crew.Sum(s => _content.RuntimeRules.Armors[
                _content.RuntimeRules.Armors.GetRequired(s.Personal!.Armor)].Value.SpaceOccupied);
            var smallSoldiers = crew.Count(s => _content.RuntimeRules.Armors[
                _content.RuntimeRules.Armors.GetRequired(s.Personal!.Armor)].Value.Size == 1);
            var smallVehicles = vehicles.Count(v => (v.Size ?? _content.RuntimeRules.Armors[
                _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(v.RuleId)].Value.VehicleArmor!.Value].Value.Size) == 1);
            var largeVehicles = vehicles.Count - smallVehicles;
            var unitCapacity = Math.Min(Math.Max(0, checked(craft.SoldierCapacity + logistics.Weapons.OfType<CraftWeaponSnapshot>().Sum(w =>
                _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(w.RuleId)].Value.BonusStats.GetValueOrDefault("soldiers")))),
                craft.EffectiveMaximumUnits);
            if (vehicles.Count + crew.Length - smallSoldiers >= capacity || crewSpace + occupied + armor.SpaceOccupied > unitCapacity ||
                craft.MaximumVehicles >= 0 && vehicles.Count >= craft.MaximumVehicles ||
                craft.MaximumSmallVehicles >= 0 && armor.Size == 1 && smallVehicles >= craft.MaximumSmallVehicles ||
                craft.MaximumLargeVehicles >= 0 && armor.Size != 1 && largeVehicles >= craft.MaximumLargeVehicles ||
                craft.MaximumSmallUnits >= 0 && armor.Size == 1 && smallSoldiers + smallVehicles >= craft.MaximumSmallUnits ||
                craft.MaximumLargeUnits >= 0 && armor.Size != 1 && crew.Length - smallSoldiers + largeVehicles >= craft.MaximumLargeUnits)
                return Blocked("Craft vehicle capacity is full.");
            if (stock.GetValueOrDefault(itemHandle) <= 0) return Blocked("Required vehicle is not in stores.");
            var candidate = new CraftVehicleSnapshot(command.VehicleRuleId, item.ClipSize)
            {
                Size = checked(armor.Size * armor.Size),
                SpaceOccupied = armor.SpaceOccupied,
                PreservationKey = $"{Identity.Id}:vehicle:{command.CraftRuleId}:{command.CraftId}:{vehicles.Count}"
            };
            var ammunition = CraftLogistics.VehicleAmmunition(candidate, _content.RuntimeRules);
            RuleHandle<ItemRuleFamily>? ammoHandle = ammunition.Id.Length == 0 ? null : _content.RuntimeRules.Items.GetRequired(ammunition.Id);
            if (ammoHandle is { } required && stock.GetValueOrDefault(required) < ammunition.Count) return Blocked("Required vehicle ammunition is not in stores.");
            ChangeStock(stock, itemHandle, -1);
            if (ammoHandle is { } consume && ammunition.Count != 0) ChangeStock(stock, consume, -ammunition.Count);
            vehicles.Add(candidate);
        }
        else
        {
            var vehicleIndex = vehicles.FindLastIndex(v => v.RuleId == command.VehicleRuleId);
            if (vehicleIndex < 0) return Blocked("Vehicle is not loaded on this craft.");
            var vehicle = vehicles[vehicleIndex];
            ChangeStock(stock, itemHandle, 1);
            var ammunition = CraftLogistics.VehicleAmmunition(vehicle, _content.RuntimeRules);
            if (ammunition.Id.Length != 0 && ammunition.Count != 0)
                ChangeStock(stock, _content.RuntimeRules.Items.GetRequired(ammunition.Id), ammunition.Count);
            vehicles.RemoveAt(vehicleIndex);
        }
        owner.Items.Clear(); foreach (var pair in stock) owner.Items.Add(pair.Key, pair.Value);
        owner.Crafts[index] = owner.Crafts[index] with { Logistics = logistics with { Vehicles = vehicles.AsReadOnly() } };
        return new([new CampaignPersonnelChanged(owner.Id, 0)]);
    }

    private int AvailableTraining(BaseState owner) => owner.Facilities.Where(f => f.BuildTime == 0).Sum(f => FacilityRule(f).TrainingRooms);
    private int AvailablePsi(BaseState owner) => owner.Facilities.Where(f => f.BuildTime == 0).Sum(f => FacilityRule(f).PsiLaboratories);
    private int CraftVehicleCapacity(RuntimeCraftRule craft, IEnumerable<CraftWeaponSnapshot?> weapons) =>
        Math.Min(Math.Max(0, checked(craft.VehicleCapacity + weapons.OfType<CraftWeaponSnapshot>().Sum(weapon =>
            _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(weapon.RuleId)].Value.BonusStats.GetValueOrDefault("vehicles")))),
            craft.EffectiveMaximumVehiclesAndLargeSoldiers);

    private void AdvanceReadinessDaily(TimeEffects effects)
    {
        foreach (var owner in _bases)
        {
            for (var index = 0; index < owner.Facilities.Count; index++)
            {
                var facility = owner.Facilities[index];
                if (facility.BuildTime <= 0) continue;
                facility = facility with
                {
                    BuildTime = facility.BuildTime - 1,
                    HadPreviousFacility = facility.BuildTime - 1 == 0 ? false : facility.HadPreviousFacility
                };
                owner.Facilities[index] = facility;
                if (facility.BuildTime == 0) effects.Notify(new CampaignConstructionCompleted(owner.Id,
                    _content.RuntimeRules.Facilities.GetExternalId(facility.Rule), facility.X, facility.Y));
            }
            if (owner.Soldiers.Count == 0) continue;
            var complete = owner.Facilities.Where(f => f.BuildTime == 0).Select(FacilityRule).ToArray();
            var positiveMana = complete.MaxBy(r => r.ManaRecoveryPerDay)?.ManaRecoveryPerDay ?? 0;
            var negativeMana = complete.MinBy(r => r.ManaRecoveryPerDay)?.ManaRecoveryPerDay ?? 0;
            var recovery = new SoldierDailyRecovery(positiveMana > 0 ? positiveMana : Math.Min(0, negativeMana),
                Math.Max(0, complete.MaxBy(r => r.HealthRecoveryPerDay)?.HealthRecoveryPerDay ?? 0),
                complete.Sum(r => r.SickBayAbsoluteBonus), complete.Sum(r => r.SickBayRelativeBonus));
            for (var index = 0; index < owner.Soldiers.Count; index++)
            {
                var soldier = owner.Soldiers[index];
                if (soldier.Personal is not { } personal) continue;
                var rule = _content.RuntimeRules.Soldiers[soldier.Rule].Value;
                personal = SoldierReadiness.Recover(personal, recovery);
                if (personal.Training)
                {
                    personal = SoldierReadiness.TrainPhysical(personal, rule, _content.RuntimeRules.Campaign.CustomTrainingFactor,
                        _random, _content.RuntimeRules.Campaign.ManaWoundThreshold, _content.RuntimeRules.Campaign.HealthWoundThreshold);
                    if (SoldierReadiness.IsFullyTrained(personal, rule))
                    { personal = personal with { Training = false }; effects.Notify(new CampaignTrainingCompleted(owner.Id, soldier.Id, false)); }
                }
                else if (personal.ReturnToTrainingWhenHealed && !SoldierReadiness.IsWounded(personal,
                    _content.RuntimeRules.Campaign.ManaWoundThreshold, _content.RuntimeRules.Campaign.HealthWoundThreshold))
                {
                    if (!SoldierReadiness.IsFullyTrained(personal, rule) && owner.Soldiers.Count(s => s.Personal?.Training == true) < AvailableTraining(owner))
                        personal = personal with { Training = true };
                    personal = personal with { ReturnToTrainingWhenHealed = false };
                }
                if (Options.AnytimePsiTraining && AvailablePsi(owner) > 0)
                {
                    personal = SoldierReadiness.TrainPsiDaily(personal, rule, Options.AllowPsiStrengthImprovement, _random);
                    if (personal.PsiTraining && SoldierReadiness.IsFullyPsiTrained(personal, rule, Options.AllowPsiStrengthImprovement))
                    { personal = personal with { PsiTraining = false }; effects.Notify(new CampaignTrainingCompleted(owner.Id, soldier.Id, true)); }
                }
                owner.Soldiers[index] = soldier with { Personal = personal };
            }
        }
    }
}
