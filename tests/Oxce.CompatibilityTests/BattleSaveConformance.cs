using System.Globalization;
using Oxce.Formats.Yaml;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// The conformance reader (Phase 7 plan, branch 2 decisions): applies the reading rules of the reference's
/// <c>SavedBattleGame::load</c> (<c>src/Savegame/SavedBattleGame.cpp</c> at 4df3a5e) to a written <c>battleGame</c>, so a
/// battle the port writes is one the reference loads. It stands in for a reference engine binary and checks the
/// reference's rules, not the port's reader. S1 covers the battle root; S2 to S4 add the map, units and items, and S5
/// the campaign's turn rule.
/// </summary>
internal static class BattleSaveConformance
{
    // tryRead into int (YamlNodeReader::tryRead throws a type error on anything else): load lines 162-176 and 399-425.
    private static readonly string[] IntegerKeys =
    [
        "alienItemLevel", "reinforcementsItemLevel", "globalshade", "turn", "bughuntMinTurn", "depth", "animFrame",
        "vipEscapeType", "vipSurvivalPercentage", "vipsSaved", "vipsLost", "vipsWaitingOutside", "vipsSavedScore",
        "vipsLostScore", "vipsWaitingOutsideScore", "objectiveType", "objectivesDestroyed", "objectivesNeeded",
        "tuReserved", "ambience", "minAmbienceRandomDelay", "maxAmbienceRandomDelay", "currentAmbienceDelay",
        "turnLimit", "chronoTrigger", "cheatTurn", "toggleBrightness",
    ];

    // readVal(default) into int (width, length, height: load lines 138-140; undoUnit: line 178). The reference takes the
    // default only for a missing key; an unreadable value throws (YamlNodeReader::tryReadVal).
    private static readonly string[] DefaultedIntegerKeys = ["width", "length", "height", "undoUnit"];

    // tryRead into bool: load lines 156-159, 174 (bughuntMode), 411 (kneelReserved), 423-424.
    private static readonly string[] BooleanKeys =
    [
        "nameDisplay", "ecEnabledFriendly", "ecEnabledHostile", "ecEnabledNeutral", "bughuntMode", "kneelReserved",
        "togglePersonalLight", "toggleNightVision",
    ];

    // tryRead into std::string (a type error unless a value): load lines 143-145, 160-165, 418; readVal<std::string> of
    // startingConditionType and enviroEffectsType (lines 146-155).
    private static readonly string[] StringKeys =
    [
        "missionType", "strTarget", "strCraftOrBase", "alienCustomDeploy", "alienCustomMission", "lastUsedMapScript",
        "reinforcementsDeployment", "reinforcementsRace", "music", "startingConditionType", "enviroEffectsType",
    ];

    /// <summary>Fails the test when <paramref name="battle"/> breaks a reading rule of the reference.</summary>
    public static void Check(YamlMappingNode battle)
    {
        ArgumentNullException.ThrowIfNull(battle);
        // SavedBattleGame::load: useIndex reads the first of repeated keys; the root a battle writes has none.
        var keys = battle.Entries.Select(static entry => entry.ScalarKey).ToArray();
        Assert.All(keys, static key => Assert.NotNull(key));
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        // int selectedUnitId = reader["selectedUnit"].readVal<int>(): throws without a readable value (line 177).
        Assert.True(battle.TryGet("selectedUnit", out var selected) && IsInteger(selected!), "selectedUnit must be an integer.");
        foreach (var key in IntegerKeys.Concat(DefaultedIntegerKeys))
            if (battle.TryGet(key, out var node)) Assert.True(IsInteger(node!), $"{key} must be an integer.");
        foreach (var key in BooleanKeys)
            if (battle.TryGet(key, out var node)) Assert.True(node is YamlScalarNode { Value: "true" or "false" }, $"{key} must be a boolean.");
        foreach (var key in StringKeys)
            if (battle.TryGet(key, out var node)) Assert.True(node is YamlScalarNode, $"{key} must be a string.");
        // tryRead into double (line 413).
        if (battle.TryGet("ambientVolume", out var volume))
            Assert.True(volume is YamlScalarNode scalar && double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
                "ambientVolume must be a number.");
        // tryRead into std::vector<int> (line 414) and std::map<std::string, int> (line 167).
        if (battle.TryGet("ambienceRandom", out var random))
            Assert.True(random is YamlSequenceNode sequence && sequence.Items.All(IsInteger), "ambienceRandom must be a sequence of integers.");
        if (battle.TryGet("reinforcementsMemory", out var memory)) Assert.True(IsIntegerMap(memory!), "reinforcementsMemory must map to integers.");
        // ItemContainer::load (line 419): a mapping of item types to readVal<int>; anything else is ignored.
        if (battle.TryGet("baseItems", out var items)) Assert.True(IsIntegerMap(items!), "baseItems must map item types to integers.");
        // ScriptValuesBase::loadBase (line 426): a mapping of tag names to values.
        if (battle.TryGet("tags", out var tags)) Assert.True(IsIntegerMap(tags!), "tags must map tag names to integers.");
    }

    private static bool IsInteger(YamlNode node) =>
        node is YamlScalarNode scalar && int.TryParse(scalar.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static bool IsIntegerMap(YamlNode node) =>
        node is YamlMappingNode map && map.Entries.All(static entry => entry.ScalarKey is not null && IsInteger(entry.Value));
}
