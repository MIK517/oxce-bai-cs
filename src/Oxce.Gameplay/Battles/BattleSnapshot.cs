using System.Collections.ObjectModel;
using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The persistence boundary of <see cref="BattleState"/> (Phase 7 branch 2): what <c>SavedBattleGame::load</c> reads and
/// <c>save</c> writes, as values. String rule IDs and plain values only; the live battle holds runtime handles. Defaults
/// are the <c>SavedBattleGame</c> constructor's, which a key the load does not find keeps.
/// </summary>
/// <remarks>
/// S1 holds the battle root. The map (S2), the units (S3) and the items (S4) follow; until then their keys stay in the
/// source document, which the save module overlays.
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
}
