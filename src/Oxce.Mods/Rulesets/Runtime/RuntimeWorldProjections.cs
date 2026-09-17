using System.Collections.ObjectModel;

namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>
/// Craft-shaped statistics of a UFO, including the race bonus applied when a mission assigns
/// its race. Reference: <c>RuleUfoStats</c> in <c>Mod/RuleCraft.h</c> and <c>RuleUfo::getStats</c>.
/// </summary>
public sealed record RuntimeUfoStats(
    int SpeedMaximum,
    int Acceleration,
    int DamageMaximum,
    int RadarRange,
    int RadarChance,
    int SightRange,
    int ShieldCapacity,
    int ShieldRecharge,
    int ShieldRechargeInGeoscape,
    string MissionCustomDeployment)
{
    public static RuntimeUfoStats Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, "");

    /// <summary>RuleUfoStats::operator+= as used by <c>Ufo::setMissionInfo</c>.</summary>
    public RuntimeUfoStats WithBonus(RuntimeUfoStats bonus) => new(
        checked(SpeedMaximum + bonus.SpeedMaximum),
        checked(Acceleration + bonus.Acceleration),
        checked(DamageMaximum + bonus.DamageMaximum),
        checked(RadarRange + bonus.RadarRange),
        checked(RadarChance + bonus.RadarChance),
        checked(SightRange + bonus.SightRange),
        checked(ShieldCapacity + bonus.ShieldCapacity),
        checked(ShieldRecharge + bonus.ShieldRecharge),
        checked(ShieldRechargeInGeoscape + bonus.ShieldRechargeInGeoscape),
        bonus.MissionCustomDeployment.Length != 0 ? bonus.MissionCustomDeployment : MissionCustomDeployment);
}

/// <summary>
/// The world-simulation projection of a UFO rule. Tactical and dogfight-only properties stay in
/// the typed catalog until their owning phase needs them. Reference: <c>Mod/RuleUfo.cpp</c>.
/// </summary>
public sealed record RuntimeUfoRule(
    string Size,
    int DefaultVisibility,
    int MissionScore,
    int FakeWaterLandingChance,
    int HunterKillerPercentage,
    int HuntMode,
    int HuntSpeed,
    int HuntBehavior,
    bool Unmanned,
    bool InstaHyper,
    bool NoAlert,
    int Marker,
    int LandedMarker,
    int CrashedMarker,
    RuntimeUfoStats Stats,
    IReadOnlyDictionary<string, RuntimeUfoStats> RaceBonuses,
    RuleHandleList<RuntimeScriptFamily> Scripts)
{
    /// <summary>RuleUfo::getRaceBonus: the unnamed bonus is the fallback.</summary>
    public RuntimeUfoStats RaceBonus(string race) =>
        RaceBonuses.TryGetValue(race, out var bonus) ? bonus : RaceBonuses.GetValueOrDefault("", RuntimeUfoStats.Empty);

    public RuntimeUfoStats StatsForRace(string race) => Stats.WithBonus(RaceBonus(race));
}

/// <summary>One trajectory waypoint. Reference: <c>TrajectoryWaypoint</c> in <c>Mod/UfoTrajectory.h</c>.</summary>
public sealed record RuntimeTrajectoryWaypoint(int Zone, int Altitude, int SpeedPercentage);

/// <summary>Reference: <c>Mod/UfoTrajectory.cpp</c>.</summary>
public sealed record RuntimeUfoTrajectoryRule(int GroundTimer, IReadOnlyList<RuntimeTrajectoryWaypoint> Waypoints)
{
    public const string RetaliationAssaultRun = "__RETALIATION_ASSAULT_RUN";

    public int Zone(int waypoint) => Waypoints[waypoint].Zone;

    public int Altitude(int waypoint) => Waypoints[waypoint].Altitude;

    /// <summary>UfoTrajectory::applySpeedPercentage.</summary>
    public int Speed(int waypoint, int baseSpeed) =>
        checked((int)((long)baseSpeed * Waypoints[waypoint].SpeedPercentage / 100));
}

/// <summary>One wave of an alien mission. Reference: <c>MissionWave</c> in <c>Mod/RuleAlienMission.h</c>.</summary>
public sealed record RuntimeMissionWave(
    string UfoType,
    RuleHandle<UfoRuleFamily>? Ufo,
    RuleHandle<AlienDeploymentRuleFamily>? Deployment,
    ulong UfoCount,
    string TrajectoryId,
    RuleHandle<UfoTrajectoryRuleFamily>? Trajectory,
    ulong SpawnTimer,
    bool Objective,
    bool ObjectiveOnTheLandingSite,
    bool ObjectiveOnXcomBase,
    int HunterKillerPercentage,
    int HuntMode,
    int HuntBehavior,
    bool Escort,
    int InterruptPercentage);

/// <summary>Mission objectives. Reference: <c>MissionObjective</c> in <c>Mod/RuleAlienMission.h</c>.</summary>
public enum RuntimeMissionObjective
{
    Score = 0,
    Infiltration = 1,
    Base = 2,
    Site = 3,
    Retaliation = 4,
    Supply = 5,
    InstantRetaliation = 6,
}

/// <summary>Where a mission's UFOs operate from. Reference: <c>AlienMissionOperationType</c>.</summary>
public enum RuntimeMissionOperationType
{
    Space = 0,
    RegionExistingBase = 1,
    RegionNewBase = 2,
    RegionNewBaseIfNecessary = 3,
    EarthExistingBase = 4,
    EarthNewBase = 5,
    EarthNewBaseIfNecessary = 6,
}

/// <summary>Reference: <c>Mod/RuleAlienMission.cpp</c>.</summary>
public sealed record RuntimeAlienMissionRule(
    RuntimeMissionObjective Objective,
    int Points,
    int SpawnZone,
    int RetaliationOdds,
    int TargetBaseOdds,
    RuntimeMissionOperationType OperationType,
    int OperationSpawnZone,
    string OperationBaseTypeId,
    RuleHandle<AlienDeploymentRuleFamily>? OperationBaseType,
    string SpawnUfoId,
    RuleHandle<UfoRuleFamily>? SpawnUfo,
    string SiteTypeId,
    RuleHandle<AlienDeploymentRuleFamily>? SiteType,
    string InterruptResearchId,
    RuleHandle<ResearchRuleFamily>? InterruptResearch,
    bool SkipScoutingPhase,
    bool EndlessInfiltration,
    bool MultiUfoRetaliation,
    bool MultiUfoRetaliationExtra,
    bool IgnoreBaseDefenses,
    bool InstaHyper,
    bool DespawnEvenIfTargeted,
    bool RespawnUfoAfterSiteDespawn,
    bool ShowAlienBase,
    IReadOnlyList<RuntimeMissionWave> Waves,
    IReadOnlyDictionary<ulong, int> MissionWeights,
    IReadOnlyList<RuntimeWeightedTimeline> RaceWeights,
    IReadOnlyList<RuntimeWeightedTimeline> RegionWeights)
{
    public bool HasRaceWeights => RaceWeights.Count != 0;
    public bool HasRegionWeights => RegionWeights.Count != 0;
}

/// <summary>A month-keyed weighted table. Reference: <c>WeightedOptions</c> timelines in the mod rules.</summary>
public sealed record RuntimeWeightedTimeline(ulong FirstMonth, IReadOnlyDictionary<string, ulong> Weights);

/// <summary>
/// The strategic projection of an alien deployment: site and alien-base bookkeeping only.
/// Battlescape generation properties remain in the typed catalog for Phase 7.
/// Reference: <c>Mod/AlienDeployment.cpp</c>.
/// </summary>
public sealed record RuntimeAlienDeploymentRule(
    string MarkerName,
    int MarkerIcon,
    int DurationMinimum,
    int DurationMaximum,
    int Points,
    int DespawnPenalty,
    bool IsAlienBase,
    bool FinalDestination,
    int FakeUnderwaterSpawnChance,
    int BaseDetectionRange,
    int BaseDetectionChance,
    int HuntMissionMaxFrequency,
    int GenMissionFrequency,
    int GenMissionLimit,
    bool GenMissionRaceFromAlienBase,
    bool HuntMissionRaceFromAlienBase,
    bool ResetAlienBaseAgeAfterUpgrade,
    bool ResetAlienBaseAge,
    string UpgradeRace,
    string BaseSelfDestructCodeId,
    RuleHandle<ResearchRuleFamily>? BaseSelfDestructCode,
    string UnlockedResearchOnDespawnId,
    RuleHandle<ResearchRuleFamily>? UnlockedResearchOnDespawn,
    string CounterDespawn,
    string CounterFailure,
    string CounterAll,
    string DecreaseCounterDespawn,
    string DecreaseCounterFailure,
    string DecreaseCounterAll,
    IReadOnlyDictionary<string, ulong> GenMission,
    IReadOnlyList<RuntimeWeightedTimeline> HuntMissions,
    IReadOnlyList<RuntimeWeightedTimeline> AlienBaseUpgrades,
    IReadOnlyList<RuntimeAlienRaceEvolution> AlienRaceEvolution,
    IReadOnlyDictionary<string, ulong> DespawnEvents)
{
    /// <summary>
    /// AlienDeployment::generateHuntMission / generateAlienBaseUpgrade: the timeline is stored in
    /// descending month order and the first entry at or below the given month wins.
    /// </summary>
    public static IReadOnlyDictionary<string, ulong>? Timeline(
        IReadOnlyList<RuntimeWeightedTimeline> timeline, ulong month)
    {
        for (var index = timeline.Count - 1; index >= 0; index--)
            if (month >= timeline[index].FirstMonth) return timeline[index].Weights;
        return timeline.Count == 0 ? null : timeline[0].Weights;
    }
}

/// <summary>Reference: <c>AlienDeployment::getAlienRaceEvolution</c> tuples.</summary>
public sealed record RuntimeAlienRaceEvolution(int Month, string FromRace, string ToRace);

internal static class RuntimeWorldReadOnly
{
    public static IReadOnlyDictionary<TKey, TValue> Dictionary<TKey, TValue>(IDictionary<TKey, TValue> source)
        where TKey : notnull => new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(source));
}
