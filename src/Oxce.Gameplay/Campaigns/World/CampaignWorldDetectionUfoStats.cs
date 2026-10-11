using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>RuleCraftStats::addGetStatsScript fields on RuleUfo and race-adjusted Ufo receivers.</summary>
internal sealed partial class CampaignWorld
{
    private RuntimeUfoStats _detectionUfoStats = RuntimeUfoStats.Empty;
    private RuntimeUfoStats _detectionUfoBonus = RuntimeUfoStats.Empty;

    internal static int DetectionUfoStatIndex(string name) => name switch
    {
        "Ufo.Stats.getFuelMax" or "RuleUfo.getFuelMax" => 0,
        "Ufo.Stats.getDamageMax" or "RuleUfo.getDamageMax" => 1,
        "Ufo.Stats.getSpeedMax" or "RuleUfo.getSpeedMax" => 2,
        "Ufo.Stats.getAccel" or "RuleUfo.getAccel" => 3,
        "Ufo.Stats.getRadarRange" or "RuleUfo.getRadarRange" => 4,
        "Ufo.Stats.getRadarChance" or "RuleUfo.getRadarChance" => 5,
        "Ufo.Stats.getSightRange" or "RuleUfo.getSightRange" => 6,
        "Ufo.Stats.getHitBonus" or "RuleUfo.getHitBonus" => 7,
        "Ufo.Stats.getAvoidBonus" or "RuleUfo.getAvoidBonus" => 8,
        "Ufo.Stats.getAvoidBonus2" or "RuleUfo.getAvoidBonus2" => 9,
        "Ufo.Stats.getPowerBonus" or "RuleUfo.getPowerBonus" => 10,
        "Ufo.Stats.getArmor" or "RuleUfo.getArmor" => 11,
        "Ufo.Stats.getShieldCapacity" or "RuleUfo.getShieldCapacity" => 12,
        "Ufo.Stats.getShieldRecharge" or "RuleUfo.getShieldRecharge" => 13,
        "Ufo.Stats.getShieldRechargeInGeoscape" or "RuleUfo.getShieldRechargeInGeoscape" => 14,
        "Ufo.Stats.getShieldBleedThrough" or "RuleUfo.getShieldBleedThrough" => 15,
        "Ufo.Stats.getMaxUnits" or "RuleUfo.getMaxUnits" => 16,
        "Ufo.Stats.getMaxVehiclesAndLargeSoldiers" or "RuleUfo.getMaxVehiclesAndLargeSoldiers" => 17,
        "Ufo.Stats.getMaxItems" or "RuleUfo.getMaxItems" => 18,
        _ => -1,
    };

    internal static int DetectionUfoStat(RuntimeUfoStats stats, int field) => field switch
    {
        0 => stats.FuelMaximum,
        1 => stats.DamageMaximum,
        2 => stats.SpeedMaximum,
        3 => stats.Acceleration,
        4 => stats.RadarRange,
        5 => stats.RadarChance,
        6 => stats.SightRange,
        7 => stats.HitBonus,
        8 => stats.AvoidBonus,
        9 => stats.AvoidBonus2,
        10 => stats.PowerBonus,
        11 => stats.Armor,
        12 => stats.ShieldCapacity,
        13 => stats.ShieldRecharge,
        14 => stats.ShieldRechargeInGeoscape,
        15 => stats.ShieldBleedThrough,
        16 => stats.MaximumUnits,
        17 => stats.MaximumVehicles,
        18 => stats.MaximumItems,
        _ => throw new InvalidOperationException("Unknown detection UFO stat."),
    };
}
