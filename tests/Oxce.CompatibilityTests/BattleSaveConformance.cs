using System.Globalization;
using Oxce.Formats.Yaml;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// The conformance reader (Phase 7 plan, branch 2 decisions): applies the reading rules of the reference's
/// <c>SavedBattleGame::load</c> (<c>src/Savegame/SavedBattleGame.cpp</c> at 4df3a5e) to a written <c>battleGame</c>, so a
/// battle the port writes is one the reference loads. It stands in for a reference engine binary and checks the
/// reference's rules, not the port's reader. S1 covers the battle root, S2 the map and S3 the units; S4 adds the items, and
/// S5 the campaign's turn rule.
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
        CheckMap(battle);
        CheckUnits(battle);
    }

    // BattleUnit::load's integer keys (tryRead and tryReadAs<int>, src/Savegame/BattleUnit.cpp lines 635-752).
    private static readonly string[] UnitIntegerKeys =
    [
        "id", "status", "direction", "directionTurret", "tu", "health", "mana", "stunlevel", "energy", "morale", "fire", "turretType",
        "turnsSinceSpotted", "turnsLeftSpottedForSnipers", "turnsSinceSpottedByXcom", "turnsLeftSpottedForSnipersByXcom",
        "turnsSinceSpottedByCivilian", "turnsLeftSpottedForSnipersByCivilian", "turnsSinceStunned", "turnsSinceSeenByHostile",
        "turnsSinceSeenByNeutral", "turnsSinceSeenByPlayer", "tileLastSpottedByHostile", "tileLastSpottedByNeutral", "tileLastSpottedByPlayer",
        "tileLastSpottedForBlindShotByHostile", "tileLastSpottedForBlindShotByNeutral", "tileLastSpottedForBlindShotByPlayer", "rankInt",
        "rankIntUnified", "moraleRestored", "notificationShown", "killedBy", "kills", "spawnUnitFaction", "motionPoints", "customMarker",
        "murdererId", "fatalShotSide", "fatalShotBodyPart", "mindControllerID", "movementType", "aggression", "previousOwner",
    ];

    // Its Sint16 experience keys (lines 661-668), boolean keys and string keys (tryRead into bool and std::string).
    private static readonly string[] UnitShortKeys =
        ["expBravery", "expReactions", "expFiring", "expThrowing", "expPsiSkill", "expPsiStrength", "expMana", "expMelee"];

    private static readonly string[] UnitBooleanKeys =
    [
        "wantsToSurrender", "isSurrendering", "kneeled", "floating", "visible", "dontReselect", "aiMedikitUsed", "respawn", "alreadyRespawned",
        "reactionsDisabledForLeftHand", "reactionsDisabledForRightHand", "summonedPlayerUnit", "resummonedFakeCivilian",
        "pickUpWeaponsMoreActively", "disableIndicators", "vip", "bannedInNextStage", "allowAutoCombat", "hasPanickedLastTurn",
    ];

    private static readonly string[] UnitStringKeys =
        ["genUnitType", "genUnitArmor", "spawnUnit", "activeHand", "preferredHandForReactions", "murdererWeapon", "murdererWeaponAmmo"];

    private static readonly string[] AiIntegerKeys = ["fromNode", "toNode", "AIMode", "targetFaction"];

    /// <summary>
    /// The units (SavedBattleGame::load lines 266-304 and 354-362, BattleUnit::load, AIModule::load, BattleUnitStatistics::load).
    /// A written unit has each key once, so useIndex's first-of-repeated rule reads what was written.
    /// </summary>
    private static void CheckUnits(YamlMappingNode battle)
    {
        if (!battle.TryGet("units", out var units)) return;
        // writer.write("units", _units, ...) writes no empty sequence.
        Assert.True(units is YamlSequenceNode { Items.Count: > 0 }, "units must be a non-empty sequence.");
        foreach (var node in ((YamlSequenceNode)units!).Items)
        {
            Assert.True(node is YamlMappingNode, "Each unit must be a mapping.");
            var unit = (YamlMappingNode)node;
            var keys = unit.Entries.Select(static entry => entry.ScalarKey).ToArray();
            Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
            // faction and id: readVal without a default (lines 268 and 270); genUnitType and genUnitArmor for a unit that is
            // no soldier (lines 276-277). The port writes the factions the reference's arrays hold.
            Assert.True(unit.TryGet("faction", out var faction) && IsFaction(faction!), "A unit's faction must be 0, 1 or 2.");
            Assert.True(unit.TryGet("id", out var id) && IsInteger(id!), "A unit's id must be an integer.");
            if (int.Parse(((YamlScalarNode)id!).Value, CultureInfo.InvariantCulture) >= 1_000_000)
                Assert.True(unit.TryGet("genUnitType", out _) && unit.TryGet("genUnitArmor", out _), "A generated unit needs genUnitType and genUnitArmor.");
            if (unit.TryGet("originalFaction", out var original)) Assert.True(IsFaction(original!), "A unit's originalFaction must be 0, 1 or 2.");
            foreach (var key in UnitIntegerKeys)
                if (unit.TryGet(key, out var value)) Assert.True(IsInteger(value!), $"Unit {key} must be an integer.");
            foreach (var key in UnitShortKeys)
                if (unit.TryGet(key, out var value)) Assert.True(IsShort(value!), $"Unit {key} must be a Sint16.");
            foreach (var key in UnitBooleanKeys)
                if (unit.TryGet(key, out var value)) Assert.True(IsBoolean(value!), $"Unit {key} must be a boolean.");
            foreach (var key in UnitStringKeys)
                if (unit.TryGet(key, out var value)) Assert.True(value is YamlScalarNode, $"Unit {key} must be a string.");
            // read(Position): three required Sint16 (Position.cpp).
            if (unit.TryGet("position", out var position))
                Assert.True(position is YamlSequenceNode { Items.Count: 3 } coordinates && coordinates.Items.All(IsShort),
                    "A unit's position must be three Sint16.");
            // reader["armor"][i] and ["fatalWounds"][i].tryReadVal: SIDE_MAX and BODYPART_MAX integers.
            if (unit.TryGet("armor", out var armor))
                Assert.True(armor is YamlSequenceNode { Items.Count: 9 } sides && sides.Items.All(IsInteger), "A unit's armor must be nine integers.");
            if (unit.TryGet("fatalWounds", out var wounds))
                Assert.True(wounds is YamlSequenceNode { Items.Count: 6 } parts && parts.Items.All(IsInteger), "A unit's fatalWounds must be six integers.");
            // read(UnitStats): Sint16 per key.
            if (unit.TryGet("currStats", out var stats))
                Assert.True(stats is YamlMappingNode statMap && statMap.Entries.All(static entry => IsShort(entry.Value)), "currStats must map to Sint16.");
            // recolor[i][0] and [1].readVal<Uint8>() (lines 726-731).
            if (unit.TryGet("recolor", out var recolor))
                Assert.True(recolor is YamlSequenceNode pairs && pairs.Items.All(static pair => pair is YamlSequenceNode { Items.Count: 2 } colors &&
                    colors.Items.All(IsByte)), "recolor must hold pairs of Uint8.");
            // ArmorMoveCost::load: reader[0] and [1].readVal<int>().
            if (unit.TryGet("moveCost", out var moveCost))
                Assert.True(moveCost is YamlMappingNode costs && costs.Entries.All(static entry => entry.ScalarKey is "basePercent" or "baseFlyPercent" or
                    "baseClimbPercent" or "baseNormalPercent" && entry.Value is YamlSequenceNode { Items.Count: 2 } pair && pair.Items.All(IsInteger)),
                    "moveCost must map its kinds to integer pairs.");
            if (unit.TryGet("meleeAttackedBy", out var attackers))
                Assert.True(attackers is YamlSequenceNode list && list.Items.All(IsInteger), "meleeAttackedBy must be integers.");
            if (unit.TryGet("tags", out var tags)) Assert.True(IsIntegerMap(tags!), "A unit's tags must map tag names to integers.");
            if (unit.TryGet("AI", out var ai)) CheckAi(ai!);
            // BattleUnitStatistics::load (or the null an empty one is written as).
            if (unit.TryGet("tempUnitStatistics", out var statistics) && statistics is not YamlNullNode)
            {
                Assert.True(statistics is YamlMappingNode, "tempUnitStatistics must be a mapping or null.");
                foreach (var entry in ((YamlMappingNode)statistics!).Entries)
                {
                    if (entry.ScalarKey == "kills")
                        Assert.True(entry.Value is YamlSequenceNode kills && kills.Items.All(static kill => kill is YamlMappingNode), "Kills must be mappings.");
                    else
                        Assert.True(IsInteger(entry.Value) || IsBoolean(entry.Value), $"Statistic {entry.ScalarKey} must be a value.");
                }
            }
        }
    }

    /// <summary>AIModule::load: readVal(-1) of the nodes and readVal of the mode, tryRead of the rest.</summary>
    private static void CheckAi(YamlNode ai)
    {
        Assert.True(ai is YamlMappingNode, "AI must be a mapping.");
        var module = (YamlMappingNode)ai;
        foreach (var key in AiIntegerKeys)
            if (module.TryGet(key, out var value)) Assert.True(IsInteger(value!), $"AI {key} must be an integer.");
        if (module.TryGet("wasHitBy", out var hits)) Assert.True(hits is YamlSequenceNode hitList && hitList.Items.All(IsInteger), "AI wasHitBy must be integers.");
        if (module.TryGet("weaponPickedUp", out var picked)) Assert.True(IsBoolean(picked!), "AI weaponPickedUp must be a boolean.");
    }

    private static bool IsFaction(YamlNode node) => node is YamlScalarNode { Value: "0" or "1" or "2" };

    private static bool IsBoolean(YamlNode node) => node is YamlScalarNode { Value: "true" or "false" };

    private static bool IsByte(YamlNode node) =>
        node is YamlScalarNode scalar && byte.TryParse(scalar.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    // The field sizes the binary tile key gives (load lines 203-212): readVal<char>, one character each.
    private static readonly string[] TileSizeKeys =
    [
        "tileIndexSize", "tileFireSize", "tileSmokeSize", "tileIDSize", "tileSetIDSize", "tileBoolFieldsSize", "lastExploredByPlayer",
        "lastExploredByHostile", "lastExploredByNeutral",
    ];

    /// <summary>The map keys (load lines 168-170 and 180-244, Node::load, Tile::loadBinary, SerializationHelper.cpp).</summary>
    private static void CheckMap(YamlMappingNode battle)
    {
        // tryRead into std::vector<std::vector<int>> and <std::vector<std::string>> (lines 168-170).
        if (battle.TryGet("reinforcementsBlocks", out var blocks)) Assert.True(IsGrid(blocks!, IsInteger), "reinforcementsBlocks must be a grid of integers.");
        foreach (var key in new[] { "flattenedMapTerrainNames", "flattenedMapBlockNames" })
            if (battle.TryGet(key, out var names)) Assert.True(IsGrid(names!, static node => node is YamlScalarNode), $"{key} must be a grid of strings.");
        // mdsReader.readVal<std::string>() for each data set (line 182).
        if (battle.TryGet("mapdatasets", out var sets))
            Assert.True(sets is YamlSequenceNode list && list.Items.All(static node => node is YamlScalarNode), "mapdatasets must be a sequence of names.");
        // The port writes binary tiles; their presence is what makes the reference read them (line 187).
        Assert.True(battle.TryGet("tileTotalBytesPer", out var totalBytesNode), "The binary tiles' tileTotalBytesPer must be written.");
        Assert.False(battle.TryGet("tiles", out _), "Old text tiles must not be written next to the binary ones.");
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in TileSizeKeys)
        {
            // from_chars(csubstr, char*): exactly one character; unserializeInt reads sizes 1, 2 and 4. (The reference
            // also accepts 0 for the lastExploredBy fields, which loadBinary then skips; the port always writes 2.)
            Assert.True(battle.TryGet(key, out var node) && node is YamlScalarNode { Value.Length: 1 }, $"{key} must be one character.");
            var size = (int)((YamlScalarNode)node!).Value[0];
            Assert.True(size is 1 or 2 or 4, $"{key} must be 1, 2 or 4 bytes.");
            sizes[key] = size;
        }
        Assert.True(IsUnsigned(totalBytesNode!), "tileTotalBytesPer must be an unsigned integer.");
        var totalBytes = ulong.Parse(((YamlScalarNode)totalBytesNode!).Value, CultureInfo.InvariantCulture);
        // The fields loadBinary reads after the index fit the record (line 226 advances by totalBytes).
        Assert.True(totalBytes >= (ulong)(sizes["tileIndexSize"] + sizes["tileIDSize"] * 4 + sizes["tileSetIDSize"] * 4 + sizes["tileFireSize"] +
            sizes["tileSmokeSize"] + sizes["tileBoolFieldsSize"] + sizes["lastExploredByPlayer"] + sizes["lastExploredByHostile"] +
            sizes["lastExploredByNeutral"]), "tileTotalBytesPer must hold every field.");
        // readVal<size_t>() (line 200) and readValBase64 (line 216): the buffer holds totalTiles records exactly.
        Assert.True(battle.TryGet("totalTiles", out var totalNode) && IsUnsigned(totalNode!), "totalTiles must be an unsigned integer.");
        var totalTiles = ulong.Parse(((YamlScalarNode)totalNode!).Value, CultureInfo.InvariantCulture);
        Assert.True(battle.TryGet("binTiles", out var data) && data is YamlScalarNode, "binTiles must be a scalar.");
        var buffer = Convert.FromBase64String(((YamlScalarNode)data!).Value);
        Assert.Equal(totalTiles * totalBytes, (ulong)buffer.Length);
        // assert(index >= 0 && index < _mapsize_x * _mapsize_z * _mapsize_y) (line 224).
        var tileCount = Int(battle, "width") * Int(battle, "length") * Int(battle, "height");
        Assert.True(sizes["tileIndexSize"] == 4, "The port writes 4-byte tile indices.");
        for (ulong start = 0; start < (ulong)buffer.Length; start += totalBytes)
        {
            var index = BitConverter.ToUInt32(buffer, (int)start);
            Assert.True(index < (ulong)tileCount, $"Tile index {index} is outside the map.");
        }
        // Node::load: tryRead of each field into int, Position (three Sint16), bool and std::vector<int>.
        if (battle.TryGet("nodes", out var nodes))
        {
            Assert.True(nodes is YamlSequenceNode, "nodes must be a sequence.");
            foreach (var node in ((YamlSequenceNode)nodes!).Items)
            {
                Assert.True(node is YamlMappingNode, "Each node must be a mapping.");
                var map = (YamlMappingNode)node;
                foreach (var key in new[] { "id", "type", "rank", "flags", "reserved", "priority" })
                    if (map.TryGet(key, out var value)) Assert.True(IsInteger(value!), $"Node {key} must be an integer.");
                foreach (var key in new[] { "allocated", "dummy" })
                    if (map.TryGet(key, out var value)) Assert.True(value is YamlScalarNode { Value: "true" or "false" }, $"Node {key} must be a boolean.");
                if (map.TryGet("position", out var position))
                    Assert.True(position is YamlSequenceNode { Items.Count: 3 } coordinates && coordinates.Items.All(IsShort), "Node position must be three Sint16.");
                if (map.TryGet("links", out var links))
                    Assert.True(links is YamlSequenceNode linkList && linkList.Items.All(IsInteger), "Node links must be integers.");
            }
        }
        // tryRead into std::vector<std::vector<std::pair<int, int>>>, a base defense's only (line 232; read(std::pair)
        // needs a sequence of exactly two).
        if (battle.TryGet("moduleMap", out var modules))
            Assert.True(IsGrid(modules!, static pair => pair is YamlSequenceNode { Items.Count: 2 } items && items.Items.All(IsInteger)),
                "moduleMap must be a grid of integer pairs.");
    }

    private static int Int(YamlMappingNode map, string key) =>
        map.TryGet(key, out var node) && node is YamlScalarNode scalar ? int.Parse(scalar.Value, CultureInfo.InvariantCulture) : 0;

    private static bool IsGrid(YamlNode node, Func<YamlNode, bool> isItem) =>
        node is YamlSequenceNode rows && rows.Items.All(row => row is YamlSequenceNode items && items.Items.All(isItem));

    private static bool IsUnsigned(YamlNode node) =>
        node is YamlScalarNode scalar && ulong.TryParse(scalar.Value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool IsShort(YamlNode node) =>
        node is YamlScalarNode scalar && short.TryParse(scalar.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static bool IsInteger(YamlNode node) =>
        node is YamlScalarNode scalar && int.TryParse(scalar.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static bool IsIntegerMap(YamlNode node) =>
        node is YamlMappingNode map && map.Entries.All(static entry => entry.ScalarKey is not null && IsInteger(entry.Value));
}
