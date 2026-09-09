using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

internal static class StartingCrewAssignment
{
    public static void Assign(IReadOnlyList<CampaignState.CraftState> crafts,
        List<CampaignState.SoldierState> soldiers, RuntimeRuleCatalog rules)
    {
        for (var index = 0; index < soldiers.Count; index++)
        {
            var soldier = soldiers[index];
            var personal = soldier.Personal!;
            var armor = Armor(soldier);
            if (armor.Size > 1) continue;
            var soldierRule = rules.Soldiers[soldier.Rule].Value;
            CampaignState.CraftState? found = null;
            foreach (var craft in crafts)
            {
                var rule = rules.Crafts[craft.Rule].Value;
                var type = rules.Crafts.GetExternalId(craft.Rule);
                var crew = soldiers.Where(s => s.Personal!.CraftType == type && s.Personal.CraftId == craft.Id).ToArray();
                var state = craft.Logistics!;
                var vehicles = state.Vehicles.Select(v => VehicleDimensions(v, rules)).ToArray();
                var used = checked(crew.Sum(s => Armor(s).SpaceOccupied) + vehicles.Sum(v => v.Space));
                var capacity = CraftLogistics.EffectiveStats(rule, state.Weapons, rules).SoldierCapacity;
                var small = crew.Count(s => Armor(s).Size == 1);
                if (capacity - used < armor.SpaceOccupied ||
                    Reached(rule.MaximumSoldiers, crew.Length) || Reached(rule.MaximumSmallSoldiers, small) ||
                    Reached(rule.MaximumSmallUnits, small + vehicles.Count(v => v.Size == 1)) ||
                    rule.AllowedSoldierGroups.Count != 0 && !rule.AllowedSoldierGroups.Contains(soldierRule.Group) ||
                    rule.OnlyOneSoldierGroupAllowed && crew.Length != 0 && rules.Soldiers[crew[0].Rule].Value.Group != soldierRule.Group ||
                    rule.AllowedArmorGroups.Count != 0 && !rule.AllowedArmorGroups.Contains(armor.Group) ||
                    rule.ArmorGroupLimits.Any(limit => crew.Count(s => Armor(s).Group == limit.Key) >= limit.Value)) continue;
                if (found is null && rule.AllowLanding) found = craft;
                if (!rule.AllowLanding && used < rule.Pilots && SoldierPiloting.MeetsRequirements(personal, soldierRule, rule, rules))
                {
                    found = craft;
                    break;
                }
            }
            soldiers[index] = soldier with
            {
                Personal = personal with
                {
                    CraftType = found is null ? "" : rules.Crafts.GetExternalId(found.Rule),
                    CraftId = found?.Id ?? 0,
                }
            };
        }

        RuntimeArmorRule Armor(CampaignState.SoldierState soldier) => rules.Armors[rules.Armors.GetRequired(soldier.Personal!.Armor)].Value;
        static bool Reached(int maximum, int count) => maximum > -1 && count >= maximum;
    }

    private static (int Size, int Space) VehicleDimensions(CraftVehicleSnapshot vehicle, RuntimeRuleCatalog rules)
    {
        var item = rules.Items[rules.Items.GetRequired(vehicle.RuleId)].Value;
        if (item.VehicleArmor is not { } handle) throw new InvalidDataException($"Vehicle '{vehicle.RuleId}' has no vehicle armor.");
        var armor = rules.Armors[handle].Value;
        var size = vehicle.Size ?? checked(armor.Size * armor.Size);
        var space = vehicle.SpaceOccupied ?? armor.SpaceOccupied;
        if (size < 1 || space < 0) throw new InvalidDataException("Vehicle dimensions must be nonnegative and size must be positive.");
        return (size, space);
    }
}

public static class SoldierPiloting
{
    public static bool MeetsRequirements(SoldierPersonalState personal, RuntimeSoldierRule soldier,
        RuntimeCraftRule craft, RuntimeRuleCatalog rules)
    {
        if (!soldier.AllowPiloting) return false;
        var bonuses = new List<RuleHandle<SoldierBonusRuleFamily>>();
        foreach (var id in personal.TransformationBonuses.Keys.Order(StringComparer.Ordinal)) AddBonus(id);
        foreach (var commendation in personal.Commendations)
        {
            if (commendation.DecorationLevel < 0) throw new InvalidDataException("Commendation level cannot be negative.");
            if (!rules.Commendations.TryGet(commendation.RuleId, out var handle)) continue;
            var types = rules.Commendations[handle].Value.SoldierBonusTypes;
            if (types.Count != 0) AddBonus(types[Math.Min(commendation.DecorationLevel, types.Count - 1)]);
        }
        foreach (var required in craft.RequiredPilotBonuses)
            if (!rules.SoldierBonuses.TryGet(required, out var handle) || !bonuses.Contains(handle)) return false;
        var armor = rules.Armors[rules.Armors.GetRequired(personal.Armor)].Value;
        foreach (var requirement in craft.PilotMinimumStats)
        {
            short value = personal.CurrentStats.GetValueOrDefault(requirement.Key);
            foreach (var bonus in bonuses) value = unchecked((short)(value + rules.SoldierBonuses[bonus].Value.Stats.GetValueOrDefault(requirement.Key)));
            value = unchecked((short)(value + armor.Stats.GetValueOrDefault(requirement.Key)));
            if (Math.Max(value, requirement.Key == "health" ? 1 : 0) < requirement.Value) return false;
        }
        return true;

        void AddBonus(string id)
        {
            if (!rules.SoldierBonuses.TryGet(id, out var handle)) return;
            // Match the reference lower_bound insertion, including equal-list-order behavior.
            var order = rules.SoldierBonuses[handle].Value.ListOrder;
            var index = bonuses.FindIndex(b => rules.SoldierBonuses[b].Value.ListOrder >= order);
            if (index < 0) bonuses.Add(handle);
            else if (bonuses[index] != handle) bonuses.Insert(index, handle);
        }
    }
}
