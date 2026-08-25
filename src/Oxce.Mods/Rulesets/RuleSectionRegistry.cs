using System.Collections.ObjectModel;

namespace Oxce.Mods.Rulesets;

public enum SpecialRuleSectionBehavior
{
    ReplaceByIdentity,
    MergeByIdentity,
    Append,
    AppendOrDeleteIdentity,
}

public sealed record SpecialRuleSectionDefinition
{
    public SpecialRuleSectionDefinition(
        string name,
        SpecialRuleSectionBehavior behavior,
        params string[] identityKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(identityKeys);
        if (identityKeys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Identity keys cannot be empty.", nameof(identityKeys));
        }

        Name = name;
        Behavior = behavior;
        IdentityKeys = Array.AsReadOnly(identityKeys.ToArray());
    }

    public string Name { get; }

    public SpecialRuleSectionBehavior Behavior { get; }

    public IReadOnlyList<string> IdentityKeys { get; }
}

public static class RuleSectionRegistry
{
    private static readonly ReadOnlyCollection<RuleSectionDefinition> Named =
        Array.AsReadOnly<RuleSectionDefinition>(
    [
        new("countries", "type"),
        new("extraGlobeLabels", "type"),
        new("regions", "type"),
        new("facilities", "type"),
        new("crafts", "type"),
        new("craftWeapons", "type"),
        new("itemCategories", "type"),
        new("items", "type"),
        new("weaponSets", "type"),
        new("ufos", "type"),
        new("invs", "id"),
        new("terrains", "name"),
        new("armors", "type"),
        new("skills", "type"),
        new("soldiers", "type"),
        new("units", "type"),
        new("alienRaces", "id"),
        new("enviroEffects", "type"),
        new("startingConditions", "type"),
        new("alienDeployments", "type"),
        new("research", "name"),
        new("manufacture", "name"),
        new("manufactureShortcut", "name"),
        new("soldierBonuses", "name"),
        new("soldierTransformation", "name"),
        new("commendations", "type"),
        new("ufoTrajectories", "id"),
        new("alienMissions", "type"),
        new("arcScripts", "type"),
        new("eventScripts", "type"),
        new("events", "name"),
        new("missionScripts", "type"),
        new("adhocScripts", "type"),
        new("soundDefs", "type"),
        new("customPalettes", "type"),
        new("interfaces", "type"),
        new("cutscenes", "type"),
        new("musics", "type"),
    ]);

    private static readonly ReadOnlyCollection<SpecialRuleSectionDefinition> Special =
        Array.AsReadOnly<SpecialRuleSectionDefinition>(
    [
        new("mapScripts", SpecialRuleSectionBehavior.ReplaceByIdentity, "type", "delete"),
        new("ufopaedia", SpecialRuleSectionBehavior.MergeByIdentity, "id", "delete"),
        new("unitResponseSounds", SpecialRuleSectionBehavior.MergeByIdentity, "name"),
        new("MCDPatches", SpecialRuleSectionBehavior.MergeByIdentity, "type"),
        new("extraSprites", SpecialRuleSectionBehavior.AppendOrDeleteIdentity, "type", "typeSingle", "delete"),
        new("extraSounds", SpecialRuleSectionBehavior.Append, "type"),
        new("extraStrings", SpecialRuleSectionBehavior.MergeByIdentity, "type"),
        new("statStrings", SpecialRuleSectionBehavior.Append),
    ]);

    private static readonly ReadOnlyDictionary<string, RuleSectionDefinition> NamedByName =
        new(Named.ToDictionary(section => section.Name, StringComparer.Ordinal));

    private static readonly ReadOnlyDictionary<string, SpecialRuleSectionDefinition> SpecialByName =
        new(Special.ToDictionary(section => section.Name, StringComparer.Ordinal));

    public static IReadOnlyList<RuleSectionDefinition> NamedRuleSections => Named;

    public static IReadOnlyList<SpecialRuleSectionDefinition> SpecialRuleSections => Special;

    public static bool TryGetNamed(string name, out RuleSectionDefinition? section)
    {
        ArgumentNullException.ThrowIfNull(name);
        return NamedByName.TryGetValue(name, out section);
    }

    public static bool TryGetSpecial(string name, out SpecialRuleSectionDefinition? section)
    {
        ArgumentNullException.ThrowIfNull(name);
        return SpecialByName.TryGetValue(name, out section);
    }
}
