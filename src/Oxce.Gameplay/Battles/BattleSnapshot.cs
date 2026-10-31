using System.Collections.ObjectModel;
using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The persistence boundary of <see cref="BattleState"/> (Phase 7 branch 2): what <c>SavedBattleGame::load</c> reads and
/// <c>save</c> writes, as values. String rule IDs and plain values only; the live battle holds runtime handles. Defaults
/// are the <c>SavedBattleGame</c> constructor's, which a key the load does not find keeps.
/// </summary>
/// <remarks>
/// S1 holds the battle root, S2 the map, S3 the units and S4 the items.
/// </remarks>
public sealed record BattleSnapshot(int Width, int Length, int Height, string MissionType)
{
    /// <summary>The tag owner of the battle root's script values (ScriptValues&lt;SavedBattleGame&gt;).</summary>
    public const string ScriptOwner = "BattleGame";

    public static IReadOnlyDictionary<string, int> EmptyCounts { get; } =
        new ReadOnlyDictionary<string, int>(new SortedDictionary<string, int>(StringComparer.Ordinal));

    /// <summary><c>strTarget</c> and <c>strCraftOrBase</c>: the briefing's target and craft or base lines.</summary>
    public string TargetName { get; init; } = "";
    public string CraftOrBaseName { get; init; } = "";

    /// <summary><c>startingConditionType</c> and <c>enviroEffectsType</c>: a known rule's ID, "" for none.</summary>
    public string StartingConditionId { get; init; } = "";
    public string EnviroEffectsId { get; init; } = "";

    public bool NameDisplay { get; init; }

    /// <summary><c>ecEnabledFriendly</c>, <c>ecEnabledHostile</c> and <c>ecEnabledNeutral</c>.</summary>
    public bool EnvironmentalConditionFriendly { get; init; }
    public bool EnvironmentalConditionHostile { get; init; }
    public bool EnvironmentalConditionNeutral { get; init; }

    public string AlienCustomDeploy { get; init; } = "";
    public string AlienCustomMission { get; init; } = "";
    public int AlienItemLevel { get; init; }

    /// <summary><c>lastUsedMapScript</c>.</summary>
    public string MapScriptId { get; init; } = "";

    public string ReinforcementsDeployment { get; init; } = "";
    public string ReinforcementsRace { get; init; } = "";
    public int ReinforcementsItemLevel { get; init; }

    /// <summary><c>reinforcementsMemory</c>: how often each reinforcement wave has come (std::map, byte order).</summary>
    public IReadOnlyDictionary<string, int> ReinforcementsMemory { get; init; } = EmptyCounts;

    /// <summary><c>globalshade</c>.</summary>
    public int GlobalShade { get; init; }
    public int Turn { get; init; }
    public int BughuntMinTurn { get; init; } = 20;
    public bool BughuntMode { get; init; }
    public int Depth { get; init; }
    public int AnimFrame { get; init; }

    /// <summary><c>selectedUnit</c> and <c>undoUnit</c>: unit IDs, -1 for none.</summary>
    public int SelectedUnitId { get; init; } = -1;
    public int UndoUnitId { get; init; } = -1;

    public int VipEscapeType { get; init; }
    public int VipSurvivalPercentage { get; init; }
    public int VipsSaved { get; init; }
    public int VipsLost { get; init; }
    public int VipsWaitingOutside { get; init; }
    public int VipsSavedScore { get; init; }
    public int VipsLostScore { get; init; }
    public int VipsWaitingOutsideScore { get; init; }

    /// <summary><c>objectiveType</c>, <c>objectivesDestroyed</c> and <c>objectivesNeeded</c>.</summary>
    public int ObjectiveType { get; init; } = -1;
    public int ObjectivesDestroyed { get; init; }
    public int ObjectivesNeeded { get; init; }

    /// <summary><c>tuReserved</c> (a BattleActionType) and <c>kneelReserved</c>.</summary>
    public int TimeUnitsReserved { get; init; }
    public bool KneelReserved { get; init; }

    /// <summary><c>ambience</c> (a sound, -1 for none) and the random ambience.</summary>
    public int Ambience { get; init; } = -1;
    public double AmbientVolume { get; init; } = 0.5;
    public IReadOnlyList<int> AmbienceRandom { get; init; } = [];
    public int MinimumAmbienceRandomDelay { get; init; } = 20;
    public int MaximumAmbienceRandomDelay { get; init; } = 60;
    public int CurrentAmbienceDelay { get; init; }

    public string Music { get; init; } = "";

    /// <summary><c>baseItems</c> (ItemContainer): item quantities, known items only.</summary>
    public IReadOnlyDictionary<string, int> BaseItems { get; init; } = EmptyCounts;

    public int TurnLimit { get; init; }
    public int ChronoTrigger { get; init; }
    public int CheatTurn { get; init; } = 20;

    public bool TogglePersonalLight { get; init; } = true;
    public bool ToggleNightVision { get; init; }
    public int ToggleBrightness { get; init; }

    /// <summary>The root's <c>tags</c> (ScriptValues of <c>BattleGame</c>), non-zero values in tag order.</summary>
    public IReadOnlyList<ScriptValueEntry> ScriptValues { get; init; } = [];

    /// <summary>
    /// <c>reinforcementsBlocks</c>, <c>flattenedMapTerrainNames</c> and <c>flattenedMapBlockNames</c>: per 10x10 block,
    /// x outer and y inner, as saved (the load does not check their shape against the map).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> ReinforcementsBlocks { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> FlattenedMapTerrainNames { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> FlattenedMapBlockNames { get; init; } = [];

    /// <summary><c>mapdatasets</c>: the names of the map data sets the tiles' set IDs index, repeats included.</summary>
    public IReadOnlyList<string> MapDataSets { get; init; } = [];

    /// <summary>The saved tiles (the binary <c>binTiles</c> or the old text <c>tiles</c>) in index order; the others are empty.</summary>
    public IReadOnlyList<BattleTileSnapshot> Tiles { get; init; } = [];

    /// <summary><c>nodes</c>, in ID order.</summary>
    public IReadOnlyList<BattleNodeSnapshot> Nodes { get; init; } = [];

    /// <summary>
    /// <c>moduleMap</c> (a base defense's only): per 10x10 block, x outer and y inner, the base module tiles in all and
    /// those left, each -1 for a block without any.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<BattleModuleSnapshot>> ModuleMap { get; init; } = [];

    /// <summary><c>units</c>, in the battle's order (S3); a unit whose rule or armor is gone is left out on read.</summary>
    public IReadOnlyList<BattleUnitSnapshot> Units { get; init; } = [];

    /// <summary>
    /// <c>items</c> and <c>itemsSpecial</c> (the battle's items; save puts an item owned in no inventory section, a special
    /// built-in item, in the second), <c>recoverConditional</c> and <c>recoverGuaranteed</c> (S4), each in the save's order;
    /// an item whose rule is gone is left out on read.
    /// </summary>
    public IReadOnlyList<BattleItemSnapshot> Items { get; init; } = [];
    public IReadOnlyList<BattleItemSnapshot> ItemsSpecial { get; init; } = [];
    public IReadOnlyList<BattleItemSnapshot> RecoverConditional { get; init; } = [];
    public IReadOnlyList<BattleItemSnapshot> RecoverGuaranteed { get; init; } = [];

    /// <summary>The mission type whose battles keep a module map (SavedBattleGame::load and save).</summary>
    public const string BaseDefenseMission = "STR_BASE_DEFENSE";

    /// <summary>
    /// SavedBattleGame::load's fallback for a base defense saved without <c>moduleMap</c>: calculateModuleMap runs before
    /// loadMapResources has pointed any tile part at its object, so it finds no base module and every whole block keeps
    /// (-1, -1). Empty for a map size the battle cannot hold, which the restore refuses.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<BattleModuleSnapshot>> UncountedModuleMap(int width, int length) =>
        !BattleMap.IsSupportedSize(width, length, 1) ? []
            : Array.AsReadOnly(Enumerable.Range(0, width / 10).Select(_ => (IReadOnlyList<BattleModuleSnapshot>)Array.AsReadOnly(
                Enumerable.Repeat(new BattleModuleSnapshot(-1, -1), length / 10).ToArray())).ToArray());
}

/// <summary>
/// One saved tile (Tile::loadBinary and saveBinary, or the old Tile::load): each part's map data ID and data set ID (-1
/// for none), smoke, fire, the floor's and walls' discovered flags, the walls' door frames (set by an open UFO door) and
/// the turn each faction last explored it.
/// </summary>
public readonly record struct BattleTileSnapshot(int Index)
{
    public int FloorId { get; init; } = -1;
    public int FloorSet { get; init; } = -1;
    public int WestWallId { get; init; } = -1;
    public int WestWallSet { get; init; } = -1;
    public int NorthWallId { get; init; } = -1;
    public int NorthWallSet { get; init; } = -1;
    public int ObjectId { get; init; } = -1;
    public int ObjectSet { get; init; } = -1;
    public int Smoke { get; init; }
    public int Fire { get; init; }
    public bool FloorDiscovered { get; init; }
    public bool WestWallDiscovered { get; init; }
    public bool NorthWallDiscovered { get; init; }

    /// <summary>
    /// The west and north walls' door frame (<c>currentFrame</c> 7): what makes a UFO door open. A wall that is no UFO door
    /// keeps the frame unused, and the save writes only open UFO doors.
    /// </summary>
    public bool WestDoorOpen { get; init; }
    public bool NorthDoorOpen { get; init; }

    public int LastExploredByHostile { get; init; }
    public int LastExploredByNeutral { get; init; }
    public int LastExploredByPlayer { get; init; }

    /// <summary>A part's (map data ID, data set ID), parts in the reference's order (floor, west wall, north wall, object).</summary>
    public (int Id, int Set) Part(int part) => part switch
    {
        0 => (FloorId, FloorSet),
        1 => (WestWallId, WestWallSet),
        2 => (NorthWallId, NorthWallSet),
        3 => (ObjectId, ObjectSet),
        _ => throw new ArgumentOutOfRangeException(nameof(part)),
    };

    public BattleTileSnapshot WithPart(int part, int id, int set) => part switch
    {
        0 => this with { FloorId = id, FloorSet = set },
        1 => this with { WestWallId = id, WestWallSet = set },
        2 => this with { NorthWallId = id, NorthWallSet = set },
        3 => this with { ObjectId = id, ObjectSet = set },
        _ => throw new ArgumentOutOfRangeException(nameof(part)),
    };
}

/// <summary>
/// One saved route node (Node::load and save): <c>segment</c> is neither read nor written. A dummy is a node the
/// generator culled; it keeps the Node() constructor's values.
/// </summary>
public sealed record BattleNodeSnapshot(int Id, BattlePosition Position, int Type, int Rank, int Flags, int Reserved, int Priority,
    bool Allocated, IReadOnlyList<int> Links, bool Dummy);

/// <summary>One block of a base defense's module map: the base module tiles in all (the pair's first) and left (its second).</summary>
public readonly record struct BattleModuleSnapshot(int Total, int Remaining);
