using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Shared immutable RuleItem readers for VM execution and bounded scalar forecasts.</summary>
internal static class WorldDetectionItemQueries
{
    internal static bool Supports(string name) => name is
        "RuleItem.isSingleTarget" or "RuleItem.isTwoHanded" or "RuleItem.isBlockingBothHands" or
        "RuleItem.isWaterOnly" or "RuleItem.getPower" or "RuleItem.getMeleePower" or
        "RuleItem.getArmorValue" or "RuleItem.getWaypoints" or "RuleItem.getWeight" or
        "RuleItem.getBattleType" or "RuleItem.getAccuracyAimed" or "RuleItem.getAccuracyAuto" or
        "RuleItem.getAccuracySnap" or "RuleItem.getAccuracyMelee" or "RuleItem.getAccuracyUse" or
        "RuleItem.getAccuracyMind" or "RuleItem.getAccuracyPanic" or "RuleItem.getAccuracyThrow";

    internal static int Value(string name, RuntimeItemRule rule) => name switch
    {
        "RuleItem.isSingleTarget" => rule.Damage.FixedRadius == 0 ? 1 : 0,
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
}
