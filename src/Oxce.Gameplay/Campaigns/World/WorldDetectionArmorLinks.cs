using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// Armor::ScriptRegister rule links: getCorpseGeoscape, both getCorpseBattlescape
/// overloads and getRequiredResearch. Items are runtime item rules and research is
/// its external ID, matching RuleMod.getRuleItem/getRuleResearch.
/// </summary>
internal static class WorldDetectionArmorLinks
{
    internal static bool Supports(string name) => name is
        "RuleArmor.getCorpseGeoscape" or "RuleArmor.getCorpseBattlescape" or "RuleArmor.getRequiredResearch";

    /// <summary>The no-argument getCorpseBattlescape reads part zero.</summary>
    internal static object? Value(string name, RuntimeArmorRule? armor, int part, RuntimeRuleCatalog rules)
    {
        if (armor is null) return null;
        return name switch
        {
            "RuleArmor.getCorpseGeoscape" => Item(armor.CorpseGeoscape, rules),
            "RuleArmor.getCorpseBattlescape" => (uint)part < (uint)armor.Script.CorpseBattle.Count
                ? Item(armor.Script.CorpseBattle[part], rules) : null,
            "RuleArmor.getRequiredResearch" => armor.Script.RequiredResearch is { } research
                ? rules.Research.GetExternalId(research) : null,
            _ => throw new InvalidOperationException("Detection armor link was not preflighted."),
        };
    }

    /// <summary>Every result any part index can produce, including null.</summary>
    internal static IEnumerable<object?> Possible(string name, RuntimeArmorRule? armor, RuntimeRuleCatalog rules)
    {
        yield return null;
        if (armor is null) yield break;
        if (name == "RuleArmor.getCorpseBattlescape")
            for (var part = 0; part < armor.Script.CorpseBattle.Count; part++) yield return Value(name, armor, part, rules);
        else yield return Value(name, armor, 0, rules);
    }

    internal static long Work(RuntimeArmorRule? armor) => 2L + (armor?.Script.CorpseBattle.Count ?? 0);

    private static RuntimeItemRule? Item(RuleHandle<ItemRuleFamily>? handle, RuntimeRuleCatalog rules) =>
        handle is { } item ? rules.Items[item].Value : null;
}
