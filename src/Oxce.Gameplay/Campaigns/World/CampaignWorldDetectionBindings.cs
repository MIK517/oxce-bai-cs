using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Api;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Read-only providers from Ufo::ScriptRegister, Craft::ScriptRegister and BindBase.</summary>
internal sealed partial class CampaignWorld
{
    private readonly DetectionTimeReference _detectionTime = new();
    private int _detectionShieldCapacity;
    private int _detectionDamageMaximum;

    private bool DetectionBindingSupported(ScriptBindingDeclaration binding) =>
        WorldDetectionDamageQueries.Supports(binding.Name) || DetectionBindingNameSupported(binding.Name) || DetectionInventoryRole(binding.Name) is not null || DetectionUnitRuleStat(binding.Name).Key is not null || DetectionCraftStatKey(binding.Name) is not null || DetectionUfoStatIndex(binding.Name) >= 0 || DetectionTagSupported(binding);

    private static bool DetectionBindingNameSupported(string name) => name is
        "Ufo.getAltitude" or "Ufo.getStatus" or "Ufo.getVisibility" or "Ufo.getDamage" or
        "Ufo.getDamageMax" or "Ufo.getDamagePercentage" or "Ufo.getDetected" or "Ufo.getHyperDetected" or "Ufo.getShield" or "Ufo.getShieldMax" or
        "Ufo.getShieldPercentage" or "Ufo.isHunterKiller" or "Ufo.isHunting" or "Ufo.isEscorting" or
        "Ufo.getHuntMode" or "Ufo.getHuntBehavior" or "Ufo.getRules" or "Ufo.getRuleUfo" or
        "RuleInventory.getId" or "RuleInventory.getType" or "RuleInventory.isRightHand" or "RuleInventory.isLeftHand" or "RuleMod.getRuleInventory" or "RuleUnit.getType" or "RuleUnit.getSpawnUnit" or "RuleUnit.getLiveAlienGeoscape" or "RuleUnit.getCivilianRecoveryItemType" or "RuleMod.getRuleUnit" or "RuleCraft.getType" or "RuleItem.getType" or "RuleArmor.getType" or "RuleSkill.getType" or
        "RuleArmor.getVisibilityAtDark" or "RuleArmor.getVisibilityAtDay" or "RuleArmor.getCamouflageAtDark" or "RuleArmor.getCamouflageAtDay" or "RuleArmor.getAntiCamouflageAtDark" or "RuleArmor.getAntiCamouflageAtDay" or "RuleArmor.getHeatVision" or "RuleArmor.getVisibilityThroughFire" or "RuleArmor.getPersonalLight" or "RuleArmor.getPersonalLightHostile" or "RuleArmor.getPersonalLightNeutral" or
        "RuleItem.getAccuracyAimed" or "RuleItem.getAccuracyAuto" or "RuleItem.getAccuracySnap" or "RuleItem.getAccuracyMelee" or
        "RuleItem.getAccuracyUse" or "RuleItem.getAccuracyMind" or "RuleItem.getAccuracyPanic" or "RuleItem.getAccuracyThrow" or
        "RuleItem.getDamageType" or "RuleItem.getMeleeDamageType" or "RuleItem.isSingleTarget" or
        "RuleDamageType.getRandomType" or "RuleDamageType.getResistType" or "RuleDamageType.getArmorIgnore" or
        "RuleDamageType.isDirect" or "RuleDamageType.isAreaOfEffect" or
        "RuleItem.isTwoHanded" or "RuleItem.isBlockingBothHands" or "RuleItem.isWaterOnly" or "RuleItem.hasCategory" or
        "RuleItem.getPower" or "RuleItem.getMeleePower" or "RuleItem.getArmorValue" or "RuleItem.getWaypoints" or
        "RuleItem.getWeight" or "RuleItem.getBattleType" or "RuleArmor.getSize" or
        "RuleUfo.getRadius" or "RuleUfo.getWeaponRange" or "RuleUfo.getWeaponPower" or "RuleUfo.getWeaponReload" or
        "Craft.getDamage" or "Craft.getDamageMax" or "Craft.getDamagePercentage" or
        "Craft.getShield" or "Craft.getShieldMax" or "Craft.getShieldPercentage" or "Craft.getRuleCraft" or
        "Craft.Stats.getRadarRange" or "Craft.Stats.getRadarChance" or
        "RuleCraft.Stats.getRadarRange" or "RuleCraft.Stats.getRadarChance" or
        "RuleCraft.getPilotsMax" or "RuleCraft.getSoldiersMax" or "RuleCraft.getVehiclesMax" or "RuleCraft.getWeaponsMax" or
        "GeoscapeGame.difficultyLevel" or "GeoscapeGame.getDaysPassed" or "GeoscapeGame.getMonthsPassed" or
        "GeoscapeGame.getCountries.init" or "GeoscapeGame.getCountries.list" or
        "Country.getRuleCountry" or "RuleCountry.getFundingCap" or "Country.getPact" or "Country.getCurrentFunding" or "Country.getCurrentActivityXcom" or "Country.getCurrentActivityAlien" or
        "GeoscapeGame.getTime" or "GeoscapeGame.isResearched" or "RuleMod.getRuleResearch" or
        "RuleMod.getSmokeReduction" or "RuleMod.getMaxViewDistance" or "RuleMod.getMaxDarknessToSeeUnits" or
        "RuleMod.getRuleItem" or "RuleMod.getRuleArmor" or "RuleMod.getRuleSoldier" or "RuleMod.getRuleSkill" or
        "RuleResearch.getNeededItem" or "RuleResearch.getCost" or "RuleResearch.getPoints" or "RuleResearch.getLookup" or "Time.getDay" or "Time.getMonth" or "Time.getYear" or
        "Time.getHour" or "Time.getMinute" or "Time.getSecond" or "Time.getSecondsPastMidnight";

    private static string? DetectionInventoryRole(string name) => name switch
    {
        "RuleMod.getRuleInventoryRightHand" => "STR_RIGHT_HAND",
        "RuleMod.getRuleInventoryLeftHand" => "STR_LEFT_HAND",
        "RuleMod.getRuleInventoryBackpack" => "STR_BACK_PACK",
        "RuleMod.getRuleInventoryBelt" => "STR_BELT",
        "RuleMod.getRuleInventoryGround" => "STR_GROUND",
        _ => null,
    };

    private ScriptBindingResult InvokeDetectionBinding(ScriptBindingDeclaration binding, Span<ScriptRuntimeValue> args)
    {
        var name = binding.Name;
        if (name.EndsWith(".getTag", StringComparison.Ordinal)) return InvokeDetectionTag(binding, args);
        var receiver = args[0].Reference;
        if (WorldDetectionDamageQueries.Supports(name))
        {
            var value = receiver is RuntimeItemDamageRule queryRule
                ? WorldDetectionDamageQueries.Result(name, queryRule, args.Length > 2 ? args[2].Scalar : 0) : 0;
            if (!WorldDetectionDamageQueries.InIntegerRange(value))
                throw new InvalidOperationException("Detection damage query was not preflighted.");
            args[1] = ScriptRuntimeValue.FromScalar((int)value);
            return ScriptBindingResult.Success;
        }
        var ufo = receiver as UfoSnapshot;
        var craft = receiver as CampaignState.CraftState;
        var rules = campaign.Content.RuntimeRules;
        switch (name)
        {
            case "RuleItem.getDamageType":
            case "RuleItem.getMeleeDamageType":
                args[1] = ScriptRuntimeValue.FromReference(receiver is RuntimeItemRule damageItem
                    ? name == "RuleItem.getDamageType" ? damageItem.Damage : damageItem.MeleeDamage : null);
                break;
            case "RuleItem.isSingleTarget":
                args[1] = ScriptRuntimeValue.FromScalar(receiver is RuntimeItemRule singleItem && singleItem.Damage.FixedRadius == 0 ? 1 : 0);
                break;
            case "RuleDamageType.getRandomType":
            case "RuleDamageType.getResistType":
            case "RuleDamageType.getArmorIgnore":
            case "RuleDamageType.isDirect":
            case "RuleDamageType.isAreaOfEffect":
                args[1] = ScriptRuntimeValue.FromScalar(receiver is RuntimeItemDamageRule damageRule ? name switch
                {
                    "RuleDamageType.getRandomType" => damageRule.RandomType,
                    "RuleDamageType.getResistType" => damageRule.ResistType,
                    "RuleDamageType.getArmorIgnore" => damageRule.ArmorIgnore,
                    "RuleDamageType.isDirect" => damageRule.FixedRadius == 0 ? 1 : 0,
                    _ => damageRule.FixedRadius != 0 ? 1 : 0,
                } : 0);
                break;
            case "RuleItem.hasCategory":
                args[1] = ScriptRuntimeValue.FromScalar(receiver is RuntimeItemRule categoryItem &&
                    categoryItem.Categories.Contains(args[2].Text ?? "") ? 1 : 0);
                break;
            case "RuleInventory.getId":
                args[1] = ScriptRuntimeValue.FromText(receiver is RuntimeInventoryRule inventoryId ? inventoryId.Id : "");
                break;
            case "RuleUnit.getType":
            case "RuleCraft.getType":
            case "RuleItem.getType":
            case "RuleArmor.getType":
            case "RuleSkill.getType":
                args[1] = ScriptRuntimeValue.FromText(receiver switch
                {
                    RuntimeUnitRule typeUnit => typeUnit.Type,
                    RuntimeCraftRule typeCraft => typeCraft.Type,
                    RuntimeItemRule typeItem => typeItem.Type,
                    RuntimeArmorRule typeArmor => typeArmor.Type,
                    RuntimeIdentityRule typeSkill => typeSkill.Id,
                    _ => "",
                });
                break;
            case "Ufo.getRules":
            case "Ufo.getRuleUfo":
                args[1] = ScriptRuntimeValue.FromReference(ufo is null ? null : rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value);
                break;
            case "Craft.getRuleCraft":
                args[1] = ScriptRuntimeValue.FromReference(craft is null ? null : rules.Crafts[craft.Rule].Value);
                break;
            case "RuleMod.getRuleResearch":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : DetectionResearchRule(args[2].Text));
                break;
            case "RuleUnit.getSpawnUnit":
                args[1] = ScriptRuntimeValue.FromReference(receiver is RuntimeUnitRule { SpawnUnit: { } spawned } ? rules.Units[spawned].Value : null);
                break;
            case "RuleUnit.getLiveAlienGeoscape":
                args[1] = ScriptRuntimeValue.FromReference(receiver is RuntimeUnitRule { LiveAlien: { } live } ? rules.Items[live].Value : null);
                break;
            case "RuleUnit.getCivilianRecoveryItemType":
                args[1] = ScriptRuntimeValue.FromReference(receiver is RuntimeUnitRule { CivilianRecoveryItem: { } recovery } ? rules.Items[recovery].Value : null);
                break;
            case "RuleMod.getSmokeReduction":
                if (receiver is not RuntimeRuleCatalog) throw new InvalidOperationException("Smoke receiver was not preflighted.");
                args[1] = ScriptRuntimeValue.FromScalar(unchecked(args[1].Scalar * rules.Campaign.MaximumViewDistance) / 60);
                break;
            case "RuleMod.getRuleInventoryRightHand":
            case "RuleMod.getRuleInventoryLeftHand":
            case "RuleMod.getRuleInventoryBackpack":
            case "RuleMod.getRuleInventoryBelt":
            case "RuleMod.getRuleInventoryGround":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null :
                    rules.Inventories[rules.Inventories.GetRequired(DetectionInventoryRole(name)!)].Value);
                break;
            case "RuleMod.getRuleInventory":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : DetectionRule(rules.Inventories, args[2].Text));
                break;
            case "RuleMod.getRuleUnit":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : DetectionRule(rules.Units, args[2].Text));
                break;
            case "RuleMod.getRuleItem":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null || args[2].Text == "STR_NONE"
                    ? null : DetectionRule(rules.Items, args[2].Text));
                break;
            case "RuleMod.getRuleArmor":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : DetectionRule(rules.Armors, args[2].Text));
                break;
            case "RuleMod.getRuleSoldier":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : DetectionRule(rules.Soldiers, args[2].Text));
                break;
            case "RuleMod.getRuleSkill":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : DetectionRule(rules.Skills, args[2].Text));
                break;
            case "RuleResearch.getNeededItem":
                var researchRule = receiver is string neededTopic ? rules.Research[rules.Research.GetRequired(neededTopic)].Value : null;
                args[1] = ScriptRuntimeValue.FromReference(researchRule is { NeedItem: true } && researchRule.NeededItem != "STR_NONE"
                    ? DetectionRule(rules.Items, researchRule.NeededItem) : null);
                break;
            case "RuleResearch.getLookup":
                args[1] = ScriptRuntimeValue.FromReference(receiver is string researchId
                    ? DetectionResearchRule(rules.Research[rules.Research.GetRequired(researchId)].Value.Lookup) : null);
                break;
            case "GeoscapeGame.isResearched":
                args[1] = ScriptRuntimeValue.FromScalar(receiver is not null && args[2].Reference is string research &&
                    campaign.HasCompletedResearch(research) ? 1 : 0);
                break;
            case "RuleResearch.getCost":
            case "RuleResearch.getPoints":
                args[1] = ScriptRuntimeValue.FromScalar(receiver is string topic
                    ? name == "RuleResearch.getCost" ? rules.Research[rules.Research.GetRequired(topic)].Value.Cost
                        : rules.Research[rules.Research.GetRequired(topic)].Value.Points : 0);
                break;
            case "Country.getRuleCountry":
                args[1] = ScriptRuntimeValue.FromReference(receiver is CampaignState.CountryState owner ? rules.Countries[owner.Rule].Value : null);
                break;
            case "GeoscapeGame.getCountries.init":
                args[1] = ScriptRuntimeValue.FromScalar(0);
                args[2] = ScriptRuntimeValue.FromScalar(receiver is null ? 0 : campaign.CountryStates.Count);
                break;
            case "GeoscapeGame.getCountries.list":
                if (receiver is null)
                {
                    args[1] = ScriptRuntimeValue.FromScalar(0);
                    args[2] = ScriptRuntimeValue.FromScalar(0);
                    args[3] = ScriptRuntimeValue.FromReference(null);
                }
                else
                {
                    var index = args[1].Scalar;
                    args[3] = ScriptRuntimeValue.FromReference((uint)index < (uint)campaign.CountryStates.Count ? campaign.CountryStates[index] : null);
                    args[1] = ScriptRuntimeValue.FromScalar(unchecked(index + 1));
                }
                break;
            case "GeoscapeGame.getTime":
                _detectionTime.Time = campaign.Time;
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : _detectionTime);
                break;
            default:
                args[1] = ScriptRuntimeValue.FromScalar(receiver is null ? 0 :
                    ufo is not null ? DetectionUfoValue(name, ufo) :
                    craft is not null ? DetectionCraftValue(name, craft) :
                    receiver is CampaignState.CountryState country ? name switch
                    {
                        "Country.getPact" => country.Pact ? 1 : 0,
                        "Country.getCurrentFunding" => country.Funding[^1],
                        "Country.getCurrentActivityXcom" => country.ActivityXcom[^1],
                        "Country.getCurrentActivityAlien" => country.ActivityAlien[^1],
                        _ => throw new InvalidOperationException("Detection country provider was not preflighted."),
                    } :
                    receiver is RuntimeCraftRule rule ? DetectionCraftRuleValue(name, rule) :
                    receiver is RuntimeUfoRule ufoRule ? name switch
                    {
                        "RuleUfo.getRadius" => ufoRule.Radius,
                        "RuleUfo.getWeaponRange" => ufoRule.WeaponRange,
                        "RuleUfo.getWeaponPower" => ufoRule.WeaponPower,
                        "RuleUfo.getWeaponReload" => ufoRule.WeaponReload,
                        _ => DetectionUfoStat(ufoRule.Stats, DetectionUfoStatIndex(name)),
                    } :
                    receiver is RuntimeInventoryRule inventoryRule ? name switch
                    {
                        "RuleInventory.getType" => inventoryRule.Type,
                        "RuleInventory.isRightHand" => inventoryRule.Hand == 2 ? 1 : 0,
                        "RuleInventory.isLeftHand" => inventoryRule.Hand == 1 ? 1 : 0,
                        _ => throw new InvalidOperationException("Detection inventory provider was not preflighted."),
                    } :
                    receiver is RuntimeCountryRule countryRule ? countryRule.FundingCap :
                    receiver is RuntimeItemRule itemRule ? DetectionItemValue(name, itemRule) :
                    receiver is RuntimeArmorRule armorRule ? DetectionArmorValue(name, armorRule) :
                    receiver is RuntimeSoldierRule soldierRule ? DetectionSoldierStat(name, soldierRule) :
                    receiver is DetectionTimeReference time ? DetectionTimeValue(name, time.Time) :
                    name switch
                    {
                        "RuleMod.getMaxViewDistance" => rules.Campaign.MaximumViewDistance,
                        "RuleMod.getMaxDarknessToSeeUnits" => rules.Campaign.MaximumDarknessToSeeUnits,
                        "GeoscapeGame.difficultyLevel" => (int)campaign.Difficulty,
                        "GeoscapeGame.getDaysPassed" => campaign.DaysPassed,
                        "GeoscapeGame.getMonthsPassed" => campaign.MonthsPassed,
                        _ => throw new InvalidOperationException("Detection provider receiver was not preflighted."),
                    });
                break;
        }
        return ScriptBindingResult.Success;
    }

    private static int DetectionItemValue(string name, RuntimeItemRule rule) => name switch
    {
        "RuleItem.isTwoHanded" => rule.TwoHanded ? 1 : 0,
        "RuleItem.isBlockingBothHands" => rule.BlockingBothHands ? 1 : 0,
        "RuleItem.isWaterOnly" => rule.WaterOnly ? 1 : 0,
        "RuleItem.getPower" => rule.Power,
        "RuleItem.getMeleePower" => rule.MeleePower,
        "RuleItem.getArmorValue" => rule.ArmorValue,
        "RuleItem.getWaypoints" => rule.Waypoints,
        "RuleItem.getWeight" => rule.Weight,
        "RuleItem.getBattleType" => rule.BattleType,
        "RuleItem.getAccuracyAimed" => rule.Accuracy.Aimed,
        "RuleItem.getAccuracyAuto" => rule.Accuracy.Auto,
        "RuleItem.getAccuracySnap" => rule.Accuracy.Snap,
        "RuleItem.getAccuracyMelee" => rule.Accuracy.Melee,
        "RuleItem.getAccuracyUse" => rule.Accuracy.Use,
        "RuleItem.getAccuracyMind" => rule.Accuracy.Mind,
        "RuleItem.getAccuracyPanic" => rule.Accuracy.Panic,
        "RuleItem.getAccuracyThrow" => rule.Accuracy.Throw,
        _ => throw new InvalidOperationException("Detection item provider was not preflighted."),
    };

    private string? DetectionResearchRule(string? id)
    {
        var rules = campaign.Content.RuntimeRules.Research;
        return id is not null && id.Length != 0 && id != "\0" && rules.TryGet(id, out var handle)
            ? rules.GetExternalId(handle) : null;
    }

    private static TProjection? DetectionRule<TFamily, TProjection>(
        RuntimeRuleFamily<TFamily, TProjection> rules, string? id) where TProjection : class =>
        id is not null && id.Length != 0 && id != "\0" && rules.TryGet(id, out var handle) ? rules[handle].Value : null;

    private int DetectionUfoValue(string name, UfoSnapshot ufo)
    {
        var field = DetectionUfoStatIndex(name);
        if (field >= 0) return unchecked(DetectionUfoStat(_detectionUfoStats, field) + DetectionUfoStat(_detectionUfoBonus, field));
        switch (name)
        {
            case "Ufo.getAltitude": return WorldAltitudes.Index(ufo.Altitude);
            case "Ufo.getStatus": return (int)ufo.Status;
            case "Ufo.getDamage": return ufo.Damage;
            case "Ufo.getDamageMax": return _detectionDamageMaximum;
            case "Ufo.getDamagePercentage": return (int)Math.Floor((double)ufo.Damage / _detectionDamageMaximum * 100);
            case "Ufo.getDetected": return ufo.Detected ? 1 : 0;
            case "Ufo.getHyperDetected": return ufo.HyperDetected ? 1 : 0;
            case "Ufo.getShield": return ufo.Shield;
            case "Ufo.isHunterKiller": return ufo.HunterKiller ? 1 : 0;
            case "Ufo.isHunting": return ufo.Hunting ? 1 : 0;
            case "Ufo.isEscorting": return ufo.Escorting ? 1 : 0;
            case "Ufo.getHuntMode": return ufo.HuntMode;
            case "Ufo.getHuntBehavior": return ufo.HuntBehavior;
        }
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        if (name == "Ufo.getVisibility") return WorldAltitudes.Visibility(rule.DefaultVisibility, ufo.Altitude);
        var capacity = _detectionShieldCapacity;
        return name switch
        {
            "Ufo.getShieldMax" => capacity,
            "Ufo.getShieldPercentage" => capacity == 0 ? 0 : unchecked(ufo.Shield * 100) / capacity,
            _ => throw new InvalidOperationException("Detection UFO provider was not preflighted."),
        };
    }

    private int DetectionCraftValue(string name, CampaignState.CraftState craft)
    {
        if (name == "Craft.getDamage") return craft.Logistics?.Damage ?? 0;
        if (name == "Craft.getShield") return craft.Logistics?.Shield ?? 0;
        var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
        if (name is "Craft.getDamageMax" or "Craft.getDamagePercentage")
        {
            var maximum = checked((int)DetectionCraftStat(rule, craft.Logistics, "damageMax"));
            return name == "Craft.getDamageMax" ? maximum :
                (int)Math.Floor((double)(craft.Logistics?.Damage ?? 0) / maximum * 100);
        }
        if (name is "Craft.getShieldMax" or "Craft.getShieldPercentage")
        {
            var maximum = checked((int)DetectionCraftStat(rule, craft.Logistics, "shieldCapacity"));
            return name == "Craft.getShieldMax" ? maximum : maximum == 0 ? 0 :
                unchecked((craft.Logistics?.Shield ?? 0) * 100) / maximum;
        }
        var key = DetectionCraftStatKey(name)
            ?? throw new InvalidOperationException("Detection craft provider was not preflighted.");
        return checked((int)DetectionCraftStat(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
            craft.Logistics, key));
    }

    private long DetectionCraftStat(RuntimeCraftRule rule, CraftLogisticsState? state, string key)
    {
        long value = rule.DefaultStats.GetValueOrDefault(key);
        if (state is null) return value;
        var rules = campaign.Content.RuntimeRules;
        for (var index = 0; index < state.Weapons.Count; index++)
            if (state.Weapons[index] is { } weapon)
                value += rules.CraftWeapons[rules.CraftWeapons.GetRequired(weapon.RuleId)].Value.BonusStats.GetValueOrDefault(key);
        return value;
    }

    private static int DetectionCraftRuleValue(string name, RuntimeCraftRule rule) =>
        DetectionCraftStatKey(name) is { } key ? rule.DefaultStats.GetValueOrDefault(key) : name switch
        {
            "RuleCraft.getPilotsMax" => rule.Pilots,
            "RuleCraft.getSoldiersMax" => rule.SoldierCapacity,
            "RuleCraft.getVehiclesMax" => rule.VehicleCapacity,
            "RuleCraft.getWeaponsMax" => rule.WeaponSlots,
            _ => throw new InvalidOperationException("Detection craft rule provider was not preflighted."),
        };

    private static string? DetectionCraftStatKey(string name) => name switch
    {
        "RuleCraft.Stats.getFuelMax" or "Craft.Stats.getFuelMax" => "fuelMax",
        "RuleCraft.Stats.getDamageMax" or "Craft.Stats.getDamageMax" => "damageMax",
        "RuleCraft.Stats.getSpeedMax" or "Craft.Stats.getSpeedMax" => "speedMax",
        "RuleCraft.Stats.getAccel" or "Craft.Stats.getAccel" => "accel",
        "RuleCraft.Stats.getRadarRange" or "Craft.Stats.getRadarRange" => "radarRange",
        "RuleCraft.Stats.getRadarChance" or "Craft.Stats.getRadarChance" => "radarChance",
        "RuleCraft.Stats.getSightRange" or "Craft.Stats.getSightRange" => "sightRange",
        "RuleCraft.Stats.getHitBonus" or "Craft.Stats.getHitBonus" => "hitBonus",
        "RuleCraft.Stats.getAvoidBonus" or "Craft.Stats.getAvoidBonus" => "avoidBonus",
        "RuleCraft.Stats.getAvoidBonus2" or "Craft.Stats.getAvoidBonus2" => "avoidBonus2",
        "RuleCraft.Stats.getPowerBonus" or "Craft.Stats.getPowerBonus" => "powerBonus",
        "RuleCraft.Stats.getArmor" or "Craft.Stats.getArmor" => "armor",
        "RuleCraft.Stats.getShieldCapacity" or "Craft.Stats.getShieldCapacity" => "shieldCapacity",
        "RuleCraft.Stats.getShieldRecharge" or "Craft.Stats.getShieldRecharge" => "shieldRecharge",
        "RuleCraft.Stats.getShieldRechargeInGeoscape" or "Craft.Stats.getShieldRechargeInGeoscape" => "shieldRechargeInGeoscape",
        "RuleCraft.Stats.getShieldBleedThrough" or "Craft.Stats.getShieldBleedThrough" => "shieldBleedThrough",
        "RuleCraft.Stats.getMaxUnits" or "Craft.Stats.getMaxUnits" => "soldiers",
        "RuleCraft.Stats.getMaxVehiclesAndLargeSoldiers" or "Craft.Stats.getMaxVehiclesAndLargeSoldiers" => "vehicles",
        "RuleCraft.Stats.getMaxItems" or "Craft.Stats.getMaxItems" => "maxItems",
        _ => null,
    };

    private static int DetectionTimeValue(string name, CampaignTime time) => name switch
    {
        "Time.getDay" => time.Day,
        "Time.getMonth" => time.Month,
        "Time.getYear" => time.Year,
        "Time.getHour" => time.Hour,
        "Time.getMinute" => time.Minute,
        "Time.getSecond" => time.Second,
        "Time.getSecondsPastMidnight" => time.Hour * 3600 + time.Minute * 60 + time.Second,
        _ => throw new InvalidOperationException("Detection time provider was not preflighted."),
    };

    private sealed class DetectionTimeReference
    {
        public CampaignTime Time { get; set; }
    }
}
