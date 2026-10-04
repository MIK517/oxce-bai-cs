using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Immutable rule readers shared by detection execution and bounded forecasts.</summary>
internal static class WorldDetectionRuleQueries
{
    internal static bool Supports(string name) => CampaignWorld.DetectionUnitRuleStat(name).Key is not null ||
        name.StartsWith("RuleCraft.", StringComparison.Ordinal) && CampaignWorld.DetectionCraftStatKey(name) is not null ||
        name.StartsWith("RuleUfo.", StringComparison.Ordinal) && CampaignWorld.DetectionUfoStatIndex(name) >= 0 || name is
        "RuleArmor.getSize" or "RuleArmor.getVisibilityAtDark" or "RuleArmor.getVisibilityAtDay" or
        "RuleArmor.getCamouflageAtDark" or "RuleArmor.getCamouflageAtDay" or "RuleArmor.getAntiCamouflageAtDark" or
        "RuleArmor.getAntiCamouflageAtDay" or "RuleArmor.getHeatVision" or "RuleArmor.getVisibilityThroughFire" or
        "RuleArmor.getPersonalLight" or "RuleArmor.getPersonalLightHostile" or "RuleArmor.getPersonalLightNeutral" or
        "RuleCraft.getPilotsMax" or "RuleCraft.getSoldiersMax" or "RuleCraft.getVehiclesMax" or "RuleCraft.getWeaponsMax" or
        "RuleUfo.getRadius" or "RuleUfo.getWeaponRange" or "RuleUfo.getWeaponPower" or "RuleUfo.getWeaponReload" or
        "RuleInventory.getType" or "RuleInventory.isRightHand" or "RuleInventory.isLeftHand" or
        "RuleCountry.getFundingCap" or "RuleResearch.getCost" or "RuleResearch.getPoints" or "RuleMod.getMaxViewDistance" or "RuleMod.getMaxDarknessToSeeUnits";

    internal static int Value(string name, object? receiver, RuntimeRuleCatalog rules) => receiver switch
    {
        null => 0,
        RuntimeCountryRule country => country.FundingCap,
        RuntimeArmorRule armor => CampaignWorld.DetectionArmorValue(name, armor),
        RuntimeSoldierRule soldier => CampaignWorld.DetectionSoldierStat(name, soldier),
        RuntimeCraftRule craft => CampaignWorld.DetectionCraftRuleValue(name, craft),
        RuntimeUfoRule ufo => name switch
        {
            "RuleUfo.getRadius" => ufo.Radius,
            "RuleUfo.getWeaponRange" => ufo.WeaponRange,
            "RuleUfo.getWeaponPower" => ufo.WeaponPower,
            "RuleUfo.getWeaponReload" => ufo.WeaponReload,
            _ => CampaignWorld.DetectionUfoStat(ufo.Stats, CampaignWorld.DetectionUfoStatIndex(name)),
        },
        RuntimeInventoryRule inventory => name switch
        {
            "RuleInventory.getType" => inventory.Type,
            "RuleInventory.isRightHand" => inventory.Hand == 2 ? 1 : 0,
            _ => inventory.Hand == 1 ? 1 : 0,
        },
        string id => name == "RuleResearch.getCost" ? rules.Research[rules.Research.GetRequired(id)].Value.Cost
            : rules.Research[rules.Research.GetRequired(id)].Value.Points,
        RuntimeRuleCatalog => name == "RuleMod.getMaxViewDistance" ? rules.Campaign.MaximumViewDistance
            : rules.Campaign.MaximumDarknessToSeeUnits,
        _ => throw new InvalidOperationException("Detection immutable rule receiver was not preflighted."),
    };

    internal static long CandidateCount(string name, RuntimeRuleCatalog rules) => Family(name) switch
    {
        1 => rules.Armors.Count,
        2 => rules.Soldiers.Count,
        3 => rules.Crafts.Count,
        4 => rules.Ufos.Count,
        5 => rules.Inventories.Count,
        6 => rules.Research.Count,
        7 => rules.Countries.Count,
        _ => 1,
    };

    internal static IEnumerable<object?> Candidates(string name, RuntimeRuleCatalog rules) => Family(name) switch
    {
        1 => rules.Armors.Rules.Select(static rule => (object?)rule.Value),
        2 => rules.Soldiers.Rules.Select(static rule => (object?)rule.Value),
        3 => rules.Crafts.Rules.Select(static rule => (object?)rule.Value),
        4 => rules.Ufos.Rules.Select(static rule => (object?)rule.Value),
        5 => rules.Inventories.Rules.Select(static rule => (object?)rule.Value),
        6 => rules.Research.Rules.Select(static rule => (object?)rule.Id),
        7 => rules.Countries.Rules.Select(static rule => (object?)rule.Value),
        _ => new object?[] { rules },
    };

    private static int Family(string name) => name.StartsWith("RuleArmor.", StringComparison.Ordinal) ? 1 :
        name.StartsWith("RuleSoldier.", StringComparison.Ordinal) ? 2 :
        name.StartsWith("RuleCraft.", StringComparison.Ordinal) ? 3 :
        name.StartsWith("RuleUfo.", StringComparison.Ordinal) ? 4 :
        name.StartsWith("RuleInventory.", StringComparison.Ordinal) ? 5 :
        name.StartsWith("RuleResearch.", StringComparison.Ordinal) ? 6 :
        name.StartsWith("RuleCountry.", StringComparison.Ordinal) ? 7 : 0;
}
