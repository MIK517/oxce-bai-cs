using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private string? PreflightServicing()
    {
        foreach (var owner in _bases)
        {
            foreach (var craft in owner.Crafts)
            {
                if (craft.Logistics is not { } state) return "Craft servicing requires resolved craft state.";
                if (state.IsAutoPatrolling) return "Craft auto-patrol requires world simulation.";
                var rule = _content.RuntimeRules.Crafts[craft.Rule].Value;
                if (rule.RepairRate < 0 || rule.RefuelRate < 0 || (long)state.Fuel + rule.RefuelRate > int.MaxValue)
                    return "Craft service rate exceeds the supported range.";
                if (!CraftLogistics.TryEffectiveStats(rule, state.Weapons, _content.RuntimeRules, out var effective))
                    return "Craft service capacity exceeds the supported range.";
                foreach (var weapon in state.Weapons.OfType<CraftWeaponSnapshot>())
                {
                    var weaponRule = _content.RuntimeRules.CraftWeapons[_content.RuntimeRules.CraftWeapons.GetRequired(weapon.RuleId)].Value;
                    var clipSize = weaponRule.Clip.Length == 0 ? 0 : _content.RuntimeRules.Items[_content.RuntimeRules.Items.GetRequired(weaponRule.Clip)].Value.ClipSize;
                    if (weaponRule.RearmRate < 0 || weaponRule.AmmoMaximum < 0 || weapon.Ammo > weaponRule.AmmoMaximum ||
                        (long)weapon.Ammo + weaponRule.RearmRate > int.MaxValue || (long)weaponRule.AmmoMaximum + Math.Max(0, clipSize - 1) > int.MaxValue)
                        return "Craft weapon service values exceed the supported range.";
                }
                if (effective!.FuelMaximum < 0 || effective.ShieldMaximum < 0 ||
                    (long)state.Shield + rule.ShieldRechargeAtBase is < int.MinValue or > int.MaxValue ||
                    (long)state.Fuel + rule.RefuelRate - effective.FuelMaximum + state.ExcessFuel > int.MaxValue)
                    return "Craft service capacity exceeds the supported range.";
            }
            foreach (var facility in owner.Facilities)
                if (_content.RuntimeRules.Facilities[facility.Rule].Value.RearmRate < 0 || facility.Ammo < 0)
                    return "Facility service values cannot be negative.";
        }
        return null;
    }

    private void ServiceCraftsHourly(TimeEffects effects)
    {
        foreach (var owner in _bases)
        {
            for (var index = 0; index < owner.Crafts.Count; index++)
            {
                var craft = owner.Crafts[index];
                var state = craft.Logistics!;
                var rule = _content.RuntimeRules.Crafts[craft.Rule].Value;
                if (state.Status == "STR_REPAIRS") state = CraftServicing.Repair(state, rule);
                else if (state.Status == "STR_REARMING")
                {
                    var stores = owner.Items.ToDictionary(p => _content.RuntimeRules.Items.GetExternalId(p.Key), p => p.Value, StringComparer.Ordinal);
                    var result = CraftServicing.Rearm(state, _content.RuntimeRules, stores, _random);
                    state = result.State;
                    if (result.ClipsUsed != 0) ChangeServiceStock(owner, _content.RuntimeRules.Items.GetRequired(result.Clip), -result.ClipsUsed);
                    if (result.MissingAmmo) effects.Notify(new CraftArrivalServiceMessage(owner.Id,
                        _content.RuntimeRules.Crafts.GetExternalId(craft.Rule), craft.Id, "STR_NOT_ENOUGH_ITEM_TO_REARM_CRAFT_AT_BASE"));
                }
                var shieldMaximum = CraftLogistics.EffectiveStats(rule, state.Weapons, _content.RuntimeRules).ShieldMaximum;
                if (shieldMaximum > 0 && state.Status != "STR_OUT")
                    state = state with { Shield = Math.Clamp(checked(state.Shield + rule.ShieldRechargeAtBase), 0, shieldMaximum) };
                owner.Crafts[index] = craft with { Logistics = state };
            }
        }
        foreach (var owner in _bases)
        {
            for (var index = 0; index < owner.Facilities.Count; index++)
            {
                var facility = owner.Facilities[index];
                var rule = _content.RuntimeRules.Facilities[facility.Rule].Value;
                if (rule.AmmoMaximum <= 0 || facility.BuildTime > 0) continue;
                if (facility.Ammo >= rule.AmmoMaximum)
                {
                    owner.Facilities[index] = facility with { AmmoMissingReported = false };
                    continue;
                }
                var used = Math.Min(checked(rule.AmmoMaximum - facility.Ammo), rule.RearmRate);
                if (rule.AmmoItem is { } item)
                {
                    var available = owner.Items.GetValueOrDefault(item);
                    if (available < used)
                    {
                        if (!facility.AmmoMissingReported)
                            effects.Notify(new FacilityServiceMessage(owner.Id, facility.X, facility.Y,
                                "STR_NOT_ENOUGH_ITEM_TO_REARM_FACILITY_AT_BASE"));
                        facility = facility with { AmmoMissingReported = true };
                        used = available;
                    }
                    ChangeServiceStock(owner, item, -used);
                }
                owner.Facilities[index] = facility with { Ammo = checked(facility.Ammo + used) };
            }
        }
    }

    private static void ChangeServiceStock(BaseState owner, RuleHandle<ItemRuleFamily> item, int change)
    {
        var quantity = checked(owner.Items.GetValueOrDefault(item) + change);
        if (quantity < 0) throw new InvalidDataException("Maintenance cannot consume unavailable stock.");
        if (quantity == 0) owner.Items.Remove(item); else owner.Items[item] = quantity;
    }
}

public sealed record FacilityServiceMessage(int BaseId, int X, int Y, string Message) : ICampaignEvent;
