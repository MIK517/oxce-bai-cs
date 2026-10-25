using System.Collections.ObjectModel;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Battles;
using Oxce.Mods.Rulesets.Content;
using static Oxce.Savegames.Oxce.OxceSaveAdapter;

namespace Oxce.Savegames.Oxce;

/// <summary>
/// The battle's units (Phase 7 branch 2 S3): <c>SavedBattleGame::load</c>'s first unit pass (the faction, original faction,
/// ID and, for a unit that is no soldier, <c>genUnitType</c> and <c>genUnitArmor</c>), <c>BattleUnit::load</c> and
/// <c>save</c>, <c>AIModule::load</c> and <c>save</c>, <c>BattleUnitStatistics</c> and <c>BattleUnitKills</c> load and
/// save, and the port-only <c>oxcePortBattle</c> (controllers and the spotting the reference does not keep).
/// </summary>
public static partial class OxceBattleSave
{
    /// <summary>The code of the warning for a unit status the reference's load reports as corrupted (it logs an error and goes on).</summary>
    public const string UnitStatusCode = "OXCE-BATTLE-SAVE-UNIT-STATUS";

    /// <summary>The port-only battle node (ADR 0038), which the reference ignores.</summary>
    public const string PortKey = "oxcePortBattle";

    // BattleUnit::MAX_SOLDIER_ID: an ID below it is a geoscape soldier's.
    private const int MaximumSoldierId = 1_000_000;

    private static ReadOnlyCollection<BattleUnitSnapshot> Units(YamlMappingNode battle, RuntimeContent content, IDiagnosticSink? diagnostics)
    {
        var port = PortUnits(battle);
        var units = new List<BattleUnitSnapshot>();
        foreach (var node in Children(battle, "units"))
        {
            // unitReader["faction"].readVal<UnitFaction>() and ["id"].readVal<int>(): required, as is a mapping to hold them.
            var map = node as YamlMappingNode ?? throw new InvalidDataException("Battle save 'units' holds an entry that is not a mapping.");
            var faction = Faction(map, "faction", null);
            var originalFaction = Faction(map, "originalFaction", faction);
            var id = map.TryGet("id", out var idNode) ? Value(idNode!, "id", YamlValueReader.ReadInt32)
                : throw new InvalidDataException("Battle save unit 'id' is missing.");
            string type, armor;
            if (id < MaximumSoldierId)
            {
                // A soldier's unit is made from the soldier: genUnitType and genUnitArmor are not read.
                type = map.TryGet("genUnitType", out var soldierType) && soldierType is YamlScalarNode { Value: var typeText } ? typeText
                    : BattleUnitSnapshot.SoldierType;
                armor = map.TryGet("genUnitArmor", out var soldierArmor) && soldierArmor is YamlScalarNode { Value: var armorText } ? armorText : "";
            }
            else
            {
                type = map.TryGet("genUnitType", out var typeNode) ? Value(typeNode!, "genUnitType", YamlValueReader.ReadString)
                    : throw new InvalidDataException($"Battle save unit {id} 'genUnitType' is missing.");
                armor = map.TryGet("genUnitArmor", out var armorNode) ? Value(armorNode!, "genUnitArmor", YamlValueReader.ReadString)
                    : throw new InvalidDataException($"Battle save unit {id} 'genUnitArmor' is missing.");
                // if (!mod->getUnit(type) || !mod->getArmor(armor)) continue;
                if (!content.RuntimeRules.Units.TryGet(type, out _) || !content.RuntimeRules.Battle.Equipment.Units.ContainsKey(type) ||
                    !content.RuntimeRules.Armors.TryGet(armor, out _))
                {
                    Skipped(diagnostics, map, $"Battle save unit {id} of unit '{type}' in armor '{armor}' is not a unit of the mods; it is left out.");
                    continue;
                }
            }
            var unit = ReadUnit(map, new BattleUnitSnapshot(id, type, armor, faction) { OriginalFaction = originalFaction }, content, diagnostics);
            unit = port.TryGetValue(id, out var extra)
                ? unit with { Controller = extra.Controller ?? BattleUnitSnapshot.DefaultController(faction), SpottedByHostile = extra.Hostile, SpottedByNeutral = extra.Neutral }
                : unit with { Controller = BattleUnitSnapshot.DefaultController(faction) };
            units.Add(unit);
        }
        // load's sanity check: a status other than standing, dead, unconscious or ignored is logged as corrupted.
        foreach (var unit in units)
            if (unit.Status is not (BattleUnitStatus.Standing or BattleUnitStatus.Dead or BattleUnitStatus.Unconscious or BattleUnitStatus.IgnoreMe))
                diagnostics?.Report(new DiagnosticEvent(UnitStatusCode, DiagnosticSeverity.Warning,
                    $"Battle save unit {unit.UnitType} (id: {unit.Id}) has an invalid 'status: {(int)unit.Status}' (the reference logs the save as corrupted).",
                    battle.Span));
        return Array.AsReadOnly(units.ToArray());
    }

    /// <summary>A UnitFaction value; the port holds player, hostile and neutral only (the reference indexes arrays by it).</summary>
    private static BattleFaction Faction(YamlMappingNode map, string key, BattleFaction? fallback)
    {
        if (!map.TryGet(key, out var node))
            return fallback ?? throw new InvalidDataException($"Battle save unit '{key}' is missing.");
        var value = Value(node!, key, YamlValueReader.ReadInt32);
        return value is >= 0 and <= 2 ? (BattleFaction)value
            : throw new InvalidDataException($"Battle save unit '{key}' {value} is no faction (0 player, 1 hostile, 2 neutral).");
    }

    private static int? OptionalInt(YamlMappingNode map, string key) => map.TryGet(key, out var node) ? Value(node!, key, YamlValueReader.ReadInt32) : null;

    private static bool? OptionalBool(YamlMappingNode map, string key) =>
        map.TryGet(key, out var node) ? Value(node!, key, YamlValueReader.ReadBoolean) : null;

    // tryReadAs<int> into a Uint8: the integer cut to its low byte (a pointer written as hex included).
    private static int Byte(YamlMappingNode map, string key, int fallback) => map.TryGet(key, out var node)
        ? Value(node!, key, YamlValueReader.ReadInt32) & 0xFF : fallback;

    /// <summary>BattleUnit::load (useIndex: the first of repeated keys), over the load's identity.</summary>
    private static BattleUnitSnapshot ReadUnit(YamlMappingNode map, BattleUnitSnapshot unit, RuntimeContent content, IDiagnosticSink? diagnostics)
    {
        // spawnUnit: getUnit(name, false), an unknown or empty one being none; respawn and its faction only with one.
        var spawnName = String(map, "spawnUnit", "");
        var spawn = spawnName.Length != 0 && content.RuntimeRules.Units.TryGet(spawnName, out _) ? spawnName : "";
        return unit with
        {
            Status = (BattleUnitStatus)Int(map, "status", 0),
            WantsToSurrender = Bool(map, "wantsToSurrender", false),
            IsSurrendering = Bool(map, "isSurrendering", false),
            Position = map.TryGet("position", out var position) ? Position(position!, "position") : default,
            Direction = Int(map, "direction", 0),
            DirectionTurret = Int(map, "directionTurret", 0),
            TimeUnits = OptionalInt(map, "tu"),
            Health = OptionalInt(map, "health"),
            Mana = OptionalInt(map, "mana"),
            StunLevel = Int(map, "stunlevel", 0),
            Energy = OptionalInt(map, "energy"),
            Morale = OptionalInt(map, "morale"),
            Kneeled = Bool(map, "kneeled", false),
            Floating = Bool(map, "floating", false),
            // reader["armor"][i].tryReadVal: the children there are (at most SIDE_MAX), the constructor's for the rest.
            Armor = Array.AsReadOnly(Children(map, "armor").Take(9).Select(static side => Value(side, "armor", YamlValueReader.ReadInt32)).ToArray()),
            FatalWounds = Array.AsReadOnly(Enumerable.Range(0, 6).Select(part => Children(map, "fatalWounds") is var wounds && part < wounds.Count
                ? Value(wounds[part], "fatalWounds", YamlValueReader.ReadInt32) : 0).ToArray()),
            Fire = Int(map, "fire", 0),
            Experience = new BattleUnitStats(0, 0, 0, Short(map, "expBravery"), Short(map, "expReactions"), Short(map, "expFiring"),
                Short(map, "expThrowing"), 0, Short(map, "expPsiStrength"), Short(map, "expPsiSkill"), Short(map, "expMelee"), Short(map, "expMana")),
            CurrentStats = map.TryGet("currStats", out var stats) ? Stats(stats!) : new Dictionary<string, short>(StringComparer.Ordinal),
            TurretType = Int(map, "turretType", -1),
            Visible = Bool(map, "visible", false),
            TurnsSinceSpotted = Array.AsReadOnly([Byte(map, "turnsSinceSpottedByXcom", 255), Byte(map, "turnsSinceSpotted", 255),
                Byte(map, "turnsSinceSpottedByCivilian", 255)]),
            TurnsLeftSpottedForSnipers = Array.AsReadOnly([Byte(map, "turnsLeftSpottedForSnipersByXcom", 0),
                Byte(map, "turnsLeftSpottedForSnipers", 0), Byte(map, "turnsLeftSpottedForSnipersByCivilian", 0)]),
            TurnsSinceStunned = Byte(map, "turnsSinceStunned", 255),
            TurnsSinceSeenBy = Array.AsReadOnly([Int(map, "turnsSinceSeenByPlayer", 255), Int(map, "turnsSinceSeenByHostile", 255),
                Int(map, "turnsSinceSeenByNeutral", 255)]),
            TileLastSpottedBy = Array.AsReadOnly([Int(map, "tileLastSpottedByPlayer", -1), Int(map, "tileLastSpottedByHostile", -1),
                Int(map, "tileLastSpottedByNeutral", -1)]),
            TileLastSpottedForBlindShotBy = Array.AsReadOnly([Int(map, "tileLastSpottedForBlindShotByPlayer", -1),
                Int(map, "tileLastSpottedForBlindShotByHostile", -1), Int(map, "tileLastSpottedForBlindShotByNeutral", -1)]),
            RankInt = OptionalInt(map, "rankInt"),
            RankIntUnified = OptionalInt(map, "rankIntUnified"),
            MoraleRestored = Int(map, "moraleRestored", 0),
            NotificationShown = Int(map, "notificationShown", 0),
            KilledBy = OptionalInt(map, "killedBy"),
            Kills = Int(map, "kills", 0),
            DontReselect = Bool(map, "dontReselect", false),
            AiMedikitUsed = Bool(map, "aiMedikitUsed", false),
            IsBrutal = Bool(map, "isBrutal", false),
            IsNotBrutal = Bool(map, "isNotBrutal", false),
            IsCheatOnMovement = Bool(map, "isCheatOnMovement", false),
            SpawnUnit = spawn,
            Respawn = spawn.Length != 0 && Bool(map, "respawn", false),
            SpawnUnitFaction = spawn.Length != 0 ? Int(map, "spawnUnitFaction", 1) : 1,
            MotionPoints = Int(map, "motionPoints", 0),
            CustomMarker = Int(map, "customMarker", 0),
            AlreadyRespawned = Bool(map, "alreadyRespawned", false),
            ActiveHand = String(map, "activeHand", "STR_RIGHT_HAND"),
            PreferredHandForReactions = String(map, "preferredHandForReactions", ""),
            ReactionsDisabledForLeftHand = Bool(map, "reactionsDisabledForLeftHand", false),
            ReactionsDisabledForRightHand = Bool(map, "reactionsDisabledForRightHand", false),
            Statistics = map.TryGet("tempUnitStatistics", out var statistics) ? ReadStatistics(statistics!) : BattleUnitStatisticsSnapshot.Empty,
            MurdererId = Int(map, "murdererId", 0),
            FatalShotSide = Int(map, "fatalShotSide", 0) & 0xFF,
            FatalShotBodyPart = Int(map, "fatalShotBodyPart", 0),
            MurdererWeapon = String(map, "murdererWeapon", ""),
            MurdererWeaponAmmo = String(map, "murdererWeaponAmmo", ""),
            Recolor = map.TryGet("recolor", out var recolor) ? Array.AsReadOnly(Children(recolor).Select(ReadRecolor).ToArray()) : null,
            MindControllerId = Int(map, "mindControllerID", 0),
            SummonedPlayerUnit = Bool(map, "summonedPlayerUnit", false),
            ResummonedFakeCivilian = Bool(map, "resummonedFakeCivilian", false),
            PickUpWeaponsMoreActively = OptionalBool(map, "pickUpWeaponsMoreActively"),
            DisableIndicators = Bool(map, "disableIndicators", false),
            MovementType = OptionalInt(map, "movementType"),
            MoveCosts = map.TryGet("moveCost", out var moveCost) ? ReadMoveCosts(moveCost!) : BattleMoveCosts.None,
            Vip = OptionalBool(map, "vip"),
            BannedInNextStage = Bool(map, "bannedInNextStage", false),
            MeleeAttackedBy = Array.AsReadOnly(Children(map, "meleeAttackedBy").Select(static id => Value(id, "meleeAttackedBy", YamlValueReader.ReadInt32)).ToArray()),
            AllowAutoCombat = OptionalBool(map, "allowAutoCombat"),
            Aggression = OptionalInt(map, "aggression"),
            HasPanickedLastTurn = Bool(map, "hasPanickedLastTurn", false),
            Ai = map.TryGet("AI", out var ai) ? ReadAi(ai!) : null,
            PreviousOwnerId = Int(map, "previousOwner", -1),
            ScriptValues = Tags(map, content, diagnostics, BattleUnitSnapshot.ScriptOwner, $"Battle save unit {unit.Id} tag"),
        };
    }

    private static short Short(YamlMappingNode map, string key) => map.TryGet(key, out var node) ? Value(node!, key, YamlValueReader.ReadInt16) : (short)0;

    /// <summary>read(UnitStats): each statistic the mapping has (none for a value that is no mapping).</summary>
    private static ReadOnlyDictionary<string, short> Stats(YamlNode node)
    {
        var result = new Dictionary<string, short>(StringComparer.Ordinal);
        if (node is YamlMappingNode map)
            foreach (var key in BattleUnitStats.Keys)
                if (map.TryGet(key, out var value)) result[key] = Value(value!, "currStats", YamlValueReader.ReadInt16);
        return new(result);
    }

    /// <summary>A recolor pair: recolor[i][0] and [1] as Uint8, each required.</summary>
    private static BattleRecolor ReadRecolor(YamlNode node)
    {
        var pair = Children(node);
        if (pair.Count < 2) throw new InvalidDataException("Battle save 'recolor' entries hold two colors.");
        return new BattleRecolor(Value(pair[0], "recolor", YamlValueReader.ReadUInt8), Value(pair[1], "recolor", YamlValueReader.ReadUInt8));
    }

    /// <summary>ArmorMoveCost::load for each kind the node has: a pair of required integers.</summary>
    private static BattleMoveCosts ReadMoveCosts(YamlNode node)
    {
        (int, int)? Kind(string key)
        {
            if (node is not YamlMappingNode map || !map.TryGet(key, out var value)) return null;
            var pair = Children(value);
            if (pair.Count < 2) throw new InvalidDataException($"Battle save 'moveCost' '{key}' holds a time and an energy percentage.");
            return (Value(pair[0], "moveCost", YamlValueReader.ReadInt32), Value(pair[1], "moveCost", YamlValueReader.ReadInt32));
        }
        return new(Kind("basePercent"), Kind("baseFlyPercent"), Kind("baseClimbPercent"), Kind("baseNormalPercent"));
    }

    /// <summary>AIModule::load: the nodes and mode by readVal(default), the rest by tryRead.</summary>
    private static BattleAiSnapshot ReadAi(YamlNode node)
    {
        var map = node as YamlMappingNode ?? new YamlMappingNode(node.Span, []);
        return new BattleAiSnapshot(Int(map, "fromNode", -1), Int(map, "toNode", -1), Int(map, "AIMode", 0),
            Array.AsReadOnly(Children(map, "wasHitBy").Select(static id => Value(id, "wasHitBy", YamlValueReader.ReadInt32)).ToArray()),
            Bool(map, "weaponPickedUp", false))
        {
            TargetFaction = OptionalInt(map, "targetFaction"),
        };
    }

    /// <summary>BattleUnitStatistics::load (useIndex): nothing for a value that is no mapping (the <c>~</c> of an empty one).</summary>
    private static BattleUnitStatisticsSnapshot ReadStatistics(YamlNode node)
    {
        if (node is not YamlMappingNode map) return BattleUnitStatisticsSnapshot.Empty;
        return new BattleUnitStatisticsSnapshot
        {
            WasUnconscious = Bool(map, "wasUnconcious", false),
            Kills = Array.AsReadOnly(Children(map, "kills").Select(ReadKill).ToArray()),
            ShotAtCounter = Int(map, "shotAtCounter", 0),
            HitCounter = Int(map, "hitCounter", 0),
            ShotByFriendlyCounter = Int(map, "shotByFriendlyCounter", 0),
            ShotFriendlyCounter = Int(map, "shotFriendlyCounter", 0),
            LoneSurvivor = Bool(map, "loneSurvivor", false),
            IronMan = Bool(map, "ironMan", false),
            LongDistanceHitCounter = Int(map, "longDistanceHitCounter", 0),
            LowAccuracyHitCounter = Int(map, "lowAccuracyHitCounter", 0),
            ShotsFiredCounter = Int(map, "shotsFiredCounter", 0),
            ShotsLandedCounter = Int(map, "shotsLandedCounter", 0),
            NikeCross = Bool(map, "nikeCross", false),
            MercyCross = Bool(map, "mercyCross", false),
            WoundsHealed = Int(map, "woundsHealed", 0),
            AppliedStimulant = Int(map, "appliedStimulant", 0),
            AppliedPainKill = Int(map, "appliedPainKill", 0),
            RevivedSoldier = Int(map, "revivedSoldier", 0),
            RevivedHostile = Int(map, "revivedHostile", 0),
            RevivedNeutral = Int(map, "revivedNeutral", 0),
            Martyr = Int(map, "martyr", 0),
            SlaveKills = Int(map, "slaveKills", 0),
        };
    }

    /// <summary>BattleUnitKills::load: <c>name</c> only without a <c>type</c>; the side is a Uint8 enumeration.</summary>
    private static BattleUnitKillSnapshot ReadKill(YamlNode node)
    {
        if (node is not YamlMappingNode map) return new BattleUnitKillSnapshot();
        var type = String(map, "type", "");
        return new BattleUnitKillSnapshot
        {
            Type = type,
            Name = type.Length == 0 ? String(map, "name", "") : "",
            Rank = String(map, "rank", ""),
            Race = String(map, "race", ""),
            Weapon = String(map, "weapon", ""),
            WeaponAmmo = String(map, "weaponAmmo", ""),
            Status = Int(map, "status", (int)BattleUnitStatus.IgnoreMe),
            Faction = Int(map, "faction", (int)BattleFaction.Hostile),
            Mission = Int(map, "mission", 0),
            Turn = Int(map, "turn", 0),
            Side = Int(map, "side", 0) & 0xFF,
            BodyPart = Int(map, "bodypart", 0),
            Id = Int(map, "id", 0),
        };
    }

    /// <summary>
    /// <c>oxcePortBattle</c>'s units by ID: the controller (<c>controller: ai</c> or <c>player</c> with <c>playerSlot</c>)
    /// and the factions other than the player's that have spotted the unit (<c>spottedBy</c>). The reference ignores it.
    /// </summary>
    private static Dictionary<int, (BattleController? Controller, bool Hostile, bool Neutral)> PortUnits(YamlMappingNode battle)
    {
        var result = new Dictionary<int, (BattleController?, bool, bool)>();
        if (!battle.TryGet(PortKey, out var port)) return result;
        var map = port as YamlMappingNode ?? throw new InvalidDataException($"Battle save '{PortKey}' must be a mapping.");
        foreach (var node in Children(map, "units"))
        {
            var unit = node as YamlMappingNode ?? throw new InvalidDataException($"Battle save '{PortKey}' units must be mappings.");
            var id = unit.TryGet("id", out var idNode) ? Value(idNode!, PortKey, YamlValueReader.ReadInt32)
                : throw new InvalidDataException($"Battle save '{PortKey}' unit 'id' is missing.");
            BattleController? controller = String(unit, "controller", "") switch
            {
                "" => null,
                "ai" => BattleController.Ai,
                "player" => Int(unit, "playerSlot", 0) is >= 0 and var slot ? BattleController.Human(slot)
                    : throw new InvalidDataException($"Battle save '{PortKey}' unit {id} has a negative 'playerSlot'."),
                var other => throw new InvalidDataException($"Battle save '{PortKey}' unit {id} has unknown controller '{other}'."),
            };
            var spotted = Children(unit, "spottedBy").Select(static faction => Value(faction, PortKey, YamlValueReader.ReadInt32)).ToArray();
            if (spotted.Any(static faction => faction is not (1 or 2)))
                throw new InvalidDataException($"Battle save '{PortKey}' unit {id} 'spottedBy' names a faction other than hostile (1) or neutral (2).");
            if (!result.TryAdd(id, (controller, spotted.Contains(1), spotted.Contains(2))))
                throw new InvalidDataException($"Battle save '{PortKey}' names unit {id} twice.");
        }
        return result;
    }

    /// <summary>
    /// <c>oxcePortBattle</c> for the units whose controller or spotting a reference save would not give them; no node when
    /// every unit has the defaults.
    /// </summary>
    private static YamlMappingNode? WritePort(IReadOnlyList<BattleUnitSnapshot> units, YamlMappingNode? source)
    {
        var entries = new List<YamlNode>();
        foreach (var unit in units)
        {
            var controller = unit.Controller != BattleUnitSnapshot.DefaultController(unit.Faction);
            if (!controller && !unit.SpottedByHostile && !unit.SpottedByNeutral) continue;
            var spotted = new List<YamlNode>();
            if (unit.SpottedByHostile) spotted.Add(Integer((int)BattleFaction.Hostile));
            if (unit.SpottedByNeutral) spotted.Add(Integer((int)BattleFaction.Neutral));
            entries.Add(Overlay(null,
            [
                Pair("id", Integer(unit.Id)),
                Pair("controller", controller ? Scalar(unit.Controller.Kind == BattleControllerKind.Ai ? "ai" : "player") : null),
                Pair("playerSlot", controller && unit.Controller.Kind == BattleControllerKind.Player ? Integer(unit.Controller.PlayerSlot) : null),
                Pair("spottedBy", spotted.Count != 0 ? Sequence(spotted) : null),
            ]));
        }
        var existing = source is not null && source.TryGet(PortKey, out var node) ? node as YamlMappingNode : null;
        var written = Overlay(existing, [Pair("units", entries.Count != 0 ? Sequence(entries) : null)]);
        return written.Entries.Count == 0 ? null : written;
    }

    /// <summary>The units, each over its source node (the first source unit of its ID) so keys no reader reads survive.</summary>
    private static YamlSequenceNode? WriteUnits(IReadOnlyList<BattleUnitSnapshot> units, YamlMappingNode? source)
    {
        if (units.Count == 0) return null;
        var sources = new Dictionary<int, YamlMappingNode>();
        if (source is not null)
            foreach (var node in Children(source, "units"))
                if (node is YamlMappingNode map && map.TryGet("id", out var id) && YamlValueReader.TryReadInt32(id!, out var value))
                    sources.TryAdd(value, map);
        return Sequence(units.Select(unit => (YamlNode)WriteUnit(unit, sources.GetValueOrDefault(unit.Id))));
    }

    /// <summary>
    /// BattleUnit::save, in its order. The keys it writes twice (<c>turnsSinceSpotted</c>, <c>turnsLeftSpottedForSnipers</c>,
    /// <c>turnsSinceStunned</c>) are written once, at their first place, with the value the reference reads back (ADR 0039).
    /// The load-only <c>isBrutal</c>, <c>isNotBrutal</c> and <c>isCheatOnMovement</c> are not written.
    /// </summary>
    private static YamlMappingNode WriteUnit(BattleUnitSnapshot unit, YamlMappingNode? source)
    {
        static YamlNode? Flag(bool value) => value ? Boolean(true) : null;
        static YamlNode? NonZero(int value) => value != 0 ? Integer(value) : null;
        static YamlNode? Text(string value) => value.Length != 0 ? Scalar(value) : null;
        static YamlSequenceNode Flow(IEnumerable<int> values) => Sequence(values.Select(static value => (YamlNode)Integer(value)));
        var armor = Enumerable.Range(0, 9).Select(side => side < unit.Armor.Count ? unit.Armor[side] : 0);
        var stats = unit.CurrentStats;
        var costs = unit.MoveCosts;
        var armorCosts = unit.ArmorMoveCosts;
        (string Key, (int Time, int Energy)? Value, (int Time, int Energy)? Armor)[] kinds =
        [
            ("basePercent", costs.Base, armorCosts.Base), ("baseFlyPercent", costs.Fly, armorCosts.Fly),
            ("baseClimbPercent", costs.Climb, armorCosts.Climb), ("baseNormalPercent", costs.Normal, armorCosts.Normal),
        ];
        var changed = kinds.Where(static kind => kind.Value is { } value && value != kind.Armor).ToArray();
        var movementChanged = unit.MovementType is { } movement && movement != unit.OriginalMovementType;
        var spawn = unit.SpawnUnit.Length != 0;
        return Overlay(source,
        [
            Pair("id", Integer(unit.Id)),
            Pair("genUnitType", Scalar(unit.UnitType)),
            Pair("genUnitArmor", Scalar(unit.ArmorId)),
            Pair("faction", Integer((int)unit.Faction)),
            Pair("status", Integer((int)unit.Status)),
            Pair("wantsToSurrender", Flag(unit.WantsToSurrender)),
            Pair("isSurrendering", Flag(unit.IsSurrendering)),
            Pair("position", Flow([unit.Position.X, unit.Position.Y, unit.Position.Z])),
            Pair("direction", Integer(unit.Direction)),
            Pair("directionTurret", Integer(unit.DirectionTurret)),
            Pair("tu", unit.TimeUnits is { } tu ? Integer(tu) : null),
            Pair("health", unit.Health is { } health ? Integer(health) : null),
            Pair("mana", unit.Mana is { } mana ? Integer(mana) : null),
            Pair("stunlevel", Integer(unit.StunLevel)),
            Pair("energy", unit.Energy is { } energy ? Integer(energy) : null),
            Pair("morale", unit.Morale is { } morale ? Integer(morale) : null),
            Pair("kneeled", Flag(unit.Kneeled)),
            Pair("floating", Flag(unit.Floating)),
            Pair("armor", Flow(armor)),
            Pair("fatalWounds", Flow(Enumerable.Range(0, 6).Select(part => part < unit.FatalWounds.Count ? unit.FatalWounds[part] : 0))),
            Pair("fire", Integer(unit.Fire)),
            Pair("expBravery", Integer(unit.Experience.Bravery)),
            Pair("expReactions", Integer(unit.Experience.Reactions)),
            Pair("expFiring", Integer(unit.Experience.Firing)),
            Pair("expThrowing", Integer(unit.Experience.Throwing)),
            Pair("expPsiSkill", Integer(unit.Experience.PsiSkill)),
            Pair("expPsiStrength", Integer(unit.Experience.PsiStrength)),
            Pair("expMana", Integer(unit.Experience.Mana)),
            Pair("expMelee", Integer(unit.Experience.Melee)),
            Pair("currStats", Overlay(null, [.. BattleUnitStats.Keys.Select(key => Pair(key, stats.TryGetValue(key, out var value) ? Integer(value) : null))])),
            Pair("turretType", unit.TurretType > -1 ? Integer(unit.TurretType) : null),
            Pair("visible", Flag(unit.Visible)),
            Pair("turnsSinceSpotted", Integer(unit.TurnsSinceSpotted[(int)BattleFaction.Hostile])),
            Pair("turnsLeftSpottedForSnipers", Integer(unit.TurnsLeftSpottedForSnipers[(int)BattleFaction.Hostile])),
            Pair("turnsSinceSeenByHostile", Integer(unit.TurnsSinceSeenBy[(int)BattleFaction.Hostile])),
            Pair("turnsSinceSeenByNeutral", Integer(unit.TurnsSinceSeenBy[(int)BattleFaction.Neutral])),
            Pair("turnsSinceSeenByPlayer", Integer(unit.TurnsSinceSeenBy[(int)BattleFaction.Player])),
            Pair("turnsSinceStunned", Integer(unit.TurnsSinceStunned)),
            Pair("tileLastSpottedByHostile", Integer(unit.TileLastSpottedBy[(int)BattleFaction.Hostile])),
            Pair("tileLastSpottedByNeutral", Integer(unit.TileLastSpottedBy[(int)BattleFaction.Neutral])),
            Pair("tileLastSpottedByPlayer", Integer(unit.TileLastSpottedBy[(int)BattleFaction.Player])),
            Pair("tileLastSpottedForBlindShotByHostile", Integer(unit.TileLastSpottedForBlindShotBy[(int)BattleFaction.Hostile])),
            Pair("tileLastSpottedForBlindShotByNeutral", Integer(unit.TileLastSpottedForBlindShotBy[(int)BattleFaction.Neutral])),
            Pair("tileLastSpottedForBlindShotByPlayer", Integer(unit.TileLastSpottedForBlindShotBy[(int)BattleFaction.Player])),
            Pair("turnsSinceSpottedByXcom", unit.TurnsSinceSpotted[(int)BattleFaction.Player] != 255 ? Integer(unit.TurnsSinceSpotted[(int)BattleFaction.Player]) : null),
            Pair("turnsLeftSpottedForSnipersByXcom", NonZero(unit.TurnsLeftSpottedForSnipers[(int)BattleFaction.Player])),
            Pair("turnsSinceSpottedByCivilian", unit.TurnsSinceSpotted[(int)BattleFaction.Neutral] != 255 ? Integer(unit.TurnsSinceSpotted[(int)BattleFaction.Neutral]) : null),
            Pair("turnsLeftSpottedForSnipersByCivilian", NonZero(unit.TurnsLeftSpottedForSnipers[(int)BattleFaction.Neutral])),
            Pair("rankInt", unit.RankInt is { } rank ? Integer(rank) : null),
            Pair("rankIntUnified", unit.RankIntUnified is { } unified ? Integer(unified) : null),
            Pair("moraleRestored", Integer(unit.MoraleRestored)),
            Pair("notificationShown", unit.NotificationShown > 0 ? Integer(unit.NotificationShown) : null),
            Pair("AI", unit.Ai is { } ai ? WriteAi(ai, unit, source is not null && source.TryGet("AI", out var sourceAi) ? sourceAi as YamlMappingNode : null) : null),
            Pair("killedBy", unit.KilledBy is { } killedBy ? Integer(killedBy) : null),
            Pair("originalFaction", unit.OriginalFaction != unit.Faction ? Integer((int)unit.OriginalFaction) : null),
            Pair("kills", NonZero(unit.Kills)),
            Pair("dontReselect", Flag(unit.Faction == BattleFaction.Player && unit.DontReselect)),
            Pair("aiMedikitUsed", Flag(unit.AiMedikitUsed)),
            Pair("previousOwner", unit.PreviousOwnerId != -1 ? Integer(unit.PreviousOwnerId) : null),
            Pair("spawnUnit", spawn ? Scalar(unit.SpawnUnit) : null),
            Pair("respawn", spawn ? Boolean(unit.Respawn) : null),
            Pair("spawnUnitFaction", spawn ? Integer(unit.SpawnUnitFaction) : null),
            Pair("motionPoints", Integer(unit.MotionPoints)),
            Pair("customMarker", unit.CustomMarker > 0 ? Integer(unit.CustomMarker) : null),
            Pair("alreadyRespawned", Flag(unit.AlreadyRespawned)),
            Pair("activeHand", Scalar(unit.ActiveHand)),
            Pair("preferredHandForReactions", Text(unit.PreferredHandForReactions)),
            Pair("reactionsDisabledForLeftHand", Flag(unit.ReactionsDisabledForLeftHand)),
            Pair("reactionsDisabledForRightHand", Flag(unit.ReactionsDisabledForRightHand)),
            Pair("tempUnitStatistics", WriteStatistics(unit.Statistics)),
            Pair("murdererId", NonZero(unit.MurdererId)),
            Pair("fatalShotSide", NonZero(unit.FatalShotSide)),
            Pair("fatalShotBodyPart", NonZero(unit.FatalShotBodyPart)),
            Pair("murdererWeapon", Text(unit.MurdererWeapon)),
            Pair("murdererWeaponAmmo", Text(unit.MurdererWeaponAmmo)),
            // writer.write("recolor", _recolor, ...): no key without pairs.
            Pair("recolor", unit.Recolor is { Count: > 0 } recolor ? Sequence(recolor.Select(pair => (YamlNode)Flow([pair.Group, pair.Color]))) : null),
            Pair("mindControllerID", NonZero(unit.MindControllerId)),
            Pair("summonedPlayerUnit", Flag(unit.SummonedPlayerUnit)),
            Pair("resummonedFakeCivilian", Flag(unit.ResummonedFakeCivilian)),
            Pair("pickUpWeaponsMoreActively", Flag(unit.PickUpWeaponsMoreActively == true)),
            Pair("disableIndicators", Flag(unit.DisableIndicators)),
            Pair("movementType", movementChanged ? Integer(unit.MovementType!.Value) : null),
            Pair("moveCost", changed.Length != 0 ? Overlay(null, [.. changed.Select(static kind => Pair(kind.Key,
                Flow([kind.Value!.Value.Time, kind.Value.Value.Energy])))]) : null),
            Pair("vip", Flag(unit.Vip == true)),
            Pair("bannedInNextStage", Flag(unit.BannedInNextStage)),
            Pair("meleeAttackedBy", unit.MeleeAttackedBy.Count != 0 ? Sequence(unit.MeleeAttackedBy.Select(static id => (YamlNode)Integer(id))) : null),
            Pair("allowAutoCombat", unit.AllowAutoCombat is { } autoCombat ? Boolean(autoCombat) : null),
            Pair("aggression", unit.Aggression is { } aggression ? Integer(aggression) : null),
            Pair("hasPanickedLastTurn", Boolean(unit.HasPanickedLastTurn)),
            Pair("isBrutal", null),
            Pair("isNotBrutal", null),
            Pair("isCheatOnMovement", null),
            Pair("tags", ScriptValues(Tags(unit.ScriptValues))),
        ]);
    }

    /// <summary>
    /// AIModule::save: <c>weaponPickedUp</c> only when set, <c>targetFaction</c> only for a hostile unit
    /// turned neutral that targets the hostile faction.
    /// </summary>
    private static YamlMappingNode WriteAi(BattleAiSnapshot ai, BattleUnitSnapshot unit, YamlMappingNode? source)
    {
        var target = unit.OriginalFaction == BattleFaction.Hostile && unit.Faction == BattleFaction.Neutral &&
            ai.TargetFaction == (int)BattleFaction.Hostile;
        return Overlay(source,
        [
            Pair("fromNode", Integer(ai.FromNode)),
            Pair("toNode", Integer(ai.ToNode)),
            Pair("AIMode", Integer(ai.Mode)),
            Pair("wasHitBy", Sequence(ai.WasHitBy.Select(static id => (YamlNode)Integer(id)))),
            Pair("weaponPickedUp", ai.WeaponPickedUp ? Boolean(true) : null),
            Pair("targetFaction", target ? Integer((int)BattleFaction.Hostile) : null),
        ]);
    }

    /// <summary>BattleUnitStatistics::save: the non-zero counters and set flags, and <c>~</c> when there is none.</summary>
    private static YamlNode WriteStatistics(BattleUnitStatisticsSnapshot statistics)
    {
        static YamlNode? Count(int value) => value != 0 ? Integer(value) : null;
        static YamlNode? Flag(bool value) => value ? Boolean(true) : null;
        var written = Overlay(null,
        [
            Pair("wasUnconcious", Flag(statistics.WasUnconscious)),
            Pair("kills", statistics.Kills.Count != 0 ? Sequence(statistics.Kills.Select(static kill => (YamlNode)WriteKill(kill))) : null),
            Pair("shotAtCounter", Count(statistics.ShotAtCounter)),
            Pair("hitCounter", Count(statistics.HitCounter)),
            Pair("shotByFriendlyCounter", Count(statistics.ShotByFriendlyCounter)),
            Pair("shotFriendlyCounter", Count(statistics.ShotFriendlyCounter)),
            Pair("loneSurvivor", Flag(statistics.LoneSurvivor)),
            Pair("ironMan", Flag(statistics.IronMan)),
            Pair("longDistanceHitCounter", Count(statistics.LongDistanceHitCounter)),
            Pair("lowAccuracyHitCounter", Count(statistics.LowAccuracyHitCounter)),
            Pair("shotsFiredCounter", Count(statistics.ShotsFiredCounter)),
            Pair("shotsLandedCounter", Count(statistics.ShotsLandedCounter)),
            Pair("nikeCross", Flag(statistics.NikeCross)),
            Pair("mercyCross", Flag(statistics.MercyCross)),
            Pair("woundsHealed", Count(statistics.WoundsHealed)),
            Pair("appliedStimulant", Count(statistics.AppliedStimulant)),
            Pair("appliedPainKill", Count(statistics.AppliedPainKill)),
            Pair("revivedSoldier", Count(statistics.RevivedSoldier)),
            Pair("revivedHostile", Count(statistics.RevivedHostile)),
            Pair("revivedNeutral", Count(statistics.RevivedNeutral)),
            Pair("martyr", Count(statistics.Martyr)),
            Pair("slaveKills", Count(statistics.SlaveKills)),
        ]);
        return written.Entries.Count == 0 ? new YamlNullNode(GeneratedSpan, "~") : written;
    }

    /// <summary>BattleUnitKills::save: <c>name</c> and <c>type</c> only when set.</summary>
    private static YamlMappingNode WriteKill(BattleUnitKillSnapshot kill)
    {
        return Overlay(null,
        [
            Pair("name", kill.Name.Length != 0 ? Scalar(kill.Name) : null),
            Pair("type", kill.Type.Length != 0 ? Scalar(kill.Type) : null),
            Pair("rank", Scalar(kill.Rank)),
            Pair("race", Scalar(kill.Race)),
            Pair("weapon", Scalar(kill.Weapon)),
            Pair("weaponAmmo", Scalar(kill.WeaponAmmo)),
            Pair("status", Integer(kill.Status)),
            Pair("faction", Integer(kill.Faction)),
            Pair("mission", Integer(kill.Mission)),
            Pair("turn", Integer(kill.Turn)),
            Pair("side", Integer(kill.Side)),
            Pair("bodypart", Integer(kill.BodyPart)),
            Pair("id", Integer(kill.Id)),
        ]);
    }
}
