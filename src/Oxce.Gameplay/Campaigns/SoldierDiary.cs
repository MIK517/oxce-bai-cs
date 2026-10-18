using System.Collections.ObjectModel;

namespace Oxce.Gameplay.Campaigns;

/// <summary>
/// BattleUnitKills: one entry of a soldier's kill list. The enum fields keep the reference's numbers (UnitStatus,
/// UnitFaction, UnitSide, UnitBodyPart). A missing field takes the default constructor's value.
/// </summary>
public sealed record SoldierKill(string Rank, string Race, string Weapon, string WeaponAmmo, int Status, int Faction,
    int Mission, int Turn, int Side, int BodyPart)
{
    public const int StatusTurning = 3;
    public const int StatusDead = 6;
    public const int StatusUnconscious = 7;
    public const int StatusPanicking = 8;
    public const int StatusIgnoreMe = 10;
    public const int FactionHostile = 1;

    /// <summary>The victim's unit type, or its name when it has no type (a soldier); and its battle unit ID.</summary>
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Id { get; init; }

    /// <summary>BattleUnitKills::hostileTurn.</summary>
    public bool HostileTurn => (Turn - 1) % 3 == 0;

    public string StatusName => Status switch
    {
        StatusDead => "STATUS_DEAD",
        StatusUnconscious => "STATUS_UNCONSCIOUS",
        StatusPanicking => "STATUS_PANICKING",
        StatusTurning => "STATUS_TURNING",
        _ => "status error",
    };

    public string FactionName => Faction switch
    {
        0 => "FACTION_PLAYER",
        FactionHostile => "FACTION_HOSTILE",
        2 => "FACTION_NEUTRAL",
        _ => "faction error",
    };

    public string SideName => Side switch
    {
        0 => "SIDE_FRONT",
        1 => "SIDE_LEFT",
        2 => "SIDE_RIGHT",
        3 => "SIDE_REAR",
        4 => "SIDE_UNDER",
        _ => "side error",
    };

    public string BodyPartName => BodyPart switch
    {
        0 => "BODYPART_HEAD",
        1 => "BODYPART_TORSO",
        2 => "BODYPART_RIGHTARM",
        3 => "BODYPART_LEFTARM",
        4 => "BODYPART_RIGHTLEG",
        5 => "BODYPART_LEFTLEG",
        _ => "body part error",
    };
}

/// <summary>
/// SoldierDiary's counters, kill list and mission IDs, which commendations read. The commendations and the
/// interception totals stay on <see cref="SoldierPersonalState"/>. Counters are keyed by their save names; a counter
/// that is absent is zero, and the two flags (<c>bestSoldier</c>, <c>globeTrotter</c>) are 0 or 1.
/// </summary>
public sealed record SoldierDiaryState
{
    /// <summary>The integer counters SoldierDiary::save writes when they are not zero, in its order.</summary>
    public static IReadOnlyList<string> CounterKeys { get; } = Array.AsReadOnly(new[]
    {
        "daysWoundedTotal", "totalShotByFriendlyCounter", "totalShotFriendlyCounter", "loneSurvivorTotal", "monthsService",
        "unconciousTotal", "shotAtCounterTotal", "hitCounterTotal", "ironManTotal", "longDistanceHitCounterTotal",
        "lowAccuracyHitCounterTotal", "shotsFiredCounterTotal", "shotsLandedCounterTotal", "shotAtCounter10in1Mission",
        "hitCounter5in1Mission", "timesWoundedTotal", "killedInAction", "allAliensKilledTotal", "allAliensStunnedTotal",
        "woundsHealedTotal", "allUFOs", "allMissionTypes", "statGainTotal", "revivedUnitTotal", "revivedSoldierTotal",
        "revivedHostileTotal", "revivedNeutralTotal", "wholeMedikitTotal", "braveryGainTotal", "bestOfRank",
        "missingInAction", "bestSoldier", "martyrKillsTotal", "postMortemKills", "globeTrotter", "slaveKillsTotal",
    });

    /// <summary>The counters the reference stores as <c>bool</c>.</summary>
    public static bool IsFlag(string key) => key is "bestSoldier" or "globeTrotter";

    public static SoldierDiaryState Empty { get; } = new();

    public IReadOnlyDictionary<string, int> Counters { get; init; } = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
    public IReadOnlyList<SoldierKill> Kills { get; init; } = [];
    public IReadOnlyList<int> MissionIds { get; init; } = [];

    public int Counter(string key) => Counters.GetValueOrDefault(key);

    public SoldierDiaryState WithCounter(string key, int value)
    {
        var counters = new Dictionary<string, int>(Counters, StringComparer.Ordinal);
        if (value == 0) counters.Remove(key);
        else counters[key] = value;
        return this with { Counters = new ReadOnlyDictionary<string, int>(counters) };
    }
}

/// <summary>
/// MissionStatistics: one finished battle in the save's <c>missionStatistics</c>, as commendations read it. Phase 7
/// adds entries; until then they come from the imported save and are written back unchanged.
/// </summary>
public sealed record MissionStatisticSnapshot(int Id, string MarkerName, string Region, string Country, string Type, string Ufo,
    bool Success, int Score, int Daylight, bool ValiantCrux, int LootValue)
{
    public bool IsAlienBase => Type.Contains("STR_ALIEN_BASE", StringComparison.Ordinal) ||
        Type.Contains("STR_ALIEN_COLONY", StringComparison.Ordinal);

    public bool IsBaseDefense => Type == "STR_BASE_DEFENSE";

    public bool IsUfoMission => Ufo != "NO_UFO";
}
