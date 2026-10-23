using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

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
    public int FuelMaximum { get; init; }
    /// <summary>RuleCraftStats::craftCustomDeploy: the battle deployment of the UFO's crew.</summary>
    public string CraftCustomDeployment { get; init; } = string.Empty;
    public int HitBonus { get; init; }
    public int AvoidBonus { get; init; }
    public int AvoidBonus2 { get; init; }
    public int PowerBonus { get; init; }
    public int Armor { get; init; }
    public int ShieldBleedThrough { get; init; }
    public int MaximumUnits { get; init; }
    public int MaximumVehicles { get; init; }
    public int MaximumItems { get; init; }

    public static RuntimeUfoStats Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, "");

    /// <summary>RuleUfoStats::operator+= as used by <c>Ufo::setMissionInfo</c>.
    /// Additional script-only fields retain 32-bit width; their callers validate consumed sums.
    /// </summary>
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
        bonus.MissionCustomDeployment.Length != 0 ? bonus.MissionCustomDeployment : MissionCustomDeployment)
    {
        FuelMaximum = unchecked(FuelMaximum + bonus.FuelMaximum),
        CraftCustomDeployment = bonus.CraftCustomDeployment.Length != 0 ? bonus.CraftCustomDeployment : CraftCustomDeployment,
        HitBonus = unchecked(HitBonus + bonus.HitBonus),
        AvoidBonus = unchecked(AvoidBonus + bonus.AvoidBonus),
        AvoidBonus2 = unchecked(AvoidBonus2 + bonus.AvoidBonus2),
        PowerBonus = unchecked(PowerBonus + bonus.PowerBonus),
        Armor = unchecked(Armor + bonus.Armor),
        ShieldBleedThrough = unchecked(ShieldBleedThrough + bonus.ShieldBleedThrough),
        MaximumUnits = unchecked(MaximumUnits + bonus.MaximumUnits),
        MaximumVehicles = unchecked(MaximumVehicles + bonus.MaximumVehicles),
        MaximumItems = unchecked(MaximumItems + bonus.MaximumItems),
    };
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
    /// <summary>RuleUfo::getBattlescapeTerrainData: the UFO's map, or null.</summary>
    public RuntimeTerrain? BattlescapeTerrain { get; init; }

    public int Radius { get; init; }
    public int WeaponRange { get; init; }
    public int WeaponPower { get; init; }
    public int WeaponReload { get; init; }

    // Dogfight and shoot-down properties (RuleUfo).
    public int Score { get; init; }
    public int BreakOffTime { get; init; }
    public int SoftlockThreshold { get; init; } = 100;
    public int MissilePower { get; init; }
    public int MissileStopChance { get; init; }
    public int SplashdownSurvivalChance { get; init; } = 100;
    /// <summary>RuleUfo::getBlobSize: the declared size 0-7, or one derived from the size class.</summary>
    public int BlobSize { get; init; } = 4;

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
/// <summary>
/// The landing checks of a starting condition (RuleStartingCondition, ConfirmLandingState::checkStartingCondition).
/// A forbidden list, when present, wins over the allowed list.
/// </summary>
public sealed record RuntimeStartingCondition(IReadOnlyList<string> AllowedCraft, IReadOnlyList<string> ForbiddenCraft,
    IReadOnlyList<string> AllowedSoldierTypes, IReadOnlyList<string> ForbiddenSoldierTypes,
    IReadOnlyDictionary<string, int> RequiredItems, bool DestroyRequiredItems, bool RequireCommanderOnboard)
{
    /// <summary><c>allowedArmors</c> and <c>forbiddenArmors</c> (the forbidden list wins when both are given).</summary>
    public IReadOnlyList<string> AllowedArmors { get; init; } = [];
    public IReadOnlyList<string> ForbiddenArmors { get; init; } = [];

    /// <summary><c>defaultArmor</c>: by soldier type, the weighted armors a disallowed armor is replaced with ("noChange" keeps it).</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> DefaultArmor { get; init; } =
        new ReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>(new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal));

    /// <summary>Whether vehicles, items or item categories are allowed or forbidden.</summary>
    public bool RestrictsEquipment { get; init; }

    /// <summary><c>allowedVehicles</c>, <c>forbiddenVehicles</c>, <c>allowedItems</c>, <c>forbiddenItems</c>,
    /// <c>allowedItemCategories</c> and <c>forbiddenItemCategories</c>.</summary>
    public IReadOnlyList<string> AllowedVehicles { get; init; } = [];
    public IReadOnlyList<string> ForbiddenVehicles { get; init; } = [];
    public IReadOnlyList<string> AllowedItems { get; init; } = [];
    public IReadOnlyList<string> ForbiddenItems { get; init; } = [];
    public IReadOnlyList<string> AllowedItemCategories { get; init; } = [];
    public IReadOnlyList<string> ForbiddenItemCategories { get; init; } = [];

    public bool IsVehiclePermitted(string vehicleType) => Permitted(AllowedVehicles, ForbiddenVehicles, vehicleType);

    /// <summary>
    /// RuleStartingCondition::isItemPermitted: the item lists decide first (an explicitly allowed item is permitted, an
    /// explicitly forbidden one is not); then, for a known item, its categories (and those of the craft's ammo for a
    /// firearm when ammo shares categories) against the forbidden or else the allowed categories.
    /// </summary>
    public bool IsItemPermitted(string itemType, IEnumerable<string>? categories)
    {
        var itemResult = true;
        if (ForbiddenItems.Count != 0)
        {
            if (ForbiddenItems.Contains(itemType)) return false;
        }
        else if (AllowedItems.Count != 0)
        {
            if (AllowedItems.Contains(itemType)) return true;
            itemResult = false;
        }
        var checkForbidden = ForbiddenItemCategories.Count != 0;
        var checkAllowed = !checkForbidden && AllowedItemCategories.Count != 0;
        var categoryResult = !checkAllowed;
        if ((checkForbidden || checkAllowed) && categories is not null)
        {
            foreach (var category in categories)
            {
                if (checkForbidden && ForbiddenItemCategories.Contains(category)) return false;
                if (checkAllowed && AllowedItemCategories.Contains(category)) return true;
            }
        }
        return itemResult && categoryResult;
    }

    /// <summary>RuleStartingCondition::getCraftReplacement: the craft whose map replaces a source craft's.</summary>
    public IReadOnlyDictionary<string, string> CraftTransformations { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    public bool IsCraftPermitted(string craftType) => Permitted(AllowedCraft, ForbiddenCraft, craftType);

    public bool IsSoldierTypePermitted(string soldierType) => Permitted(AllowedSoldierTypes, ForbiddenSoldierTypes, soldierType);

    private static bool Permitted(IReadOnlyList<string> allowed, IReadOnlyList<string> forbidden, string type) =>
        forbidden.Count != 0 ? !forbidden.Contains(type) : allowed.Count == 0 || allowed.Contains(type);
}

public sealed record RuntimeWeightedTimeline(ulong FirstMonth, IReadOnlyDictionary<string, ulong> Weights);

/// <summary>The strategic projection of an alien race: retaliation aggression and the retaliation
/// mission timeline in rule order (AlienRace::retaliationMissionWeights).</summary>
public sealed record RuntimeAlienRaceRule(string Id, int RetaliationAggression, IReadOnlyList<RuntimeWeightedTimeline> RetaliationMissionWeights)
{
    /// <summary>AlienRace::getBaseCustomDeploy and getBaseCustomMission: alien-base battle deployments.</summary>
    public string BaseCustomDeploy { get; init; } = string.Empty;
    public string BaseCustomMission { get; init; } = string.Empty;

    /// <summary>AlienRace <c>members</c> by rank, and <c>membersRandom</c> (one set per rank; it wins when present).</summary>
    public IReadOnlyList<string> Members { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> RandomMembers { get; init; } = [];
}

/// <summary>
/// The runtime projection of an alien deployment: site and alien-base bookkeeping, and in
/// <see cref="Battle"/> the battlescape properties the battle generator reads so far.
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
    public string StartingCondition { get; init; } = string.Empty;
    /// <summary>AlienDeployment::getRace: the fixed race of a final-destination battle.</summary>
    public string Race { get; init; } = string.Empty;

    /// <summary>AlienDeployment::getAlienBaseDiscoveredMessage: AlienBaseState's title, or "" for the default.</summary>
    public string AlienBaseDiscoveredMessage { get; init; } = string.Empty;

    /// <summary>The battlescape properties of this deployment.</summary>
    public RuntimeDeploymentBattle Battle { get; init; } = RuntimeDeploymentBattle.Default;

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

/// <summary>
/// AlienDeployment battlescape properties read by <c>BattlescapeGenerator::run</c>: the map size
/// (<c>getDimensions</c>), <c>turnLimit</c>, <c>chronoTrigger</c> (0 force lose, 1 force abort, 2 force win,
/// 3 force win surrender), <c>cheatTurn</c> and the shade overrides (-1 for none).
/// </summary>
public sealed record RuntimeDeploymentBattle(
    int Width, int Length, int Height, int TurnLimit, int ChronoTrigger, int CheatTurn, int Shade, int MinShade, int MaxShade)
{
    public static RuntimeDeploymentBattle Default { get; } = new(0, 0, 0, 0, 0, 20, -1, -1, -1);

    /// <summary>AlienDeployment::getTerrains: the battle picks one of these, if any, over the globe texture's.</summary>
    public IReadOnlyList<string> Terrains { get; init; } = [];

    /// <summary>AlienDeployment's <c>script</c> ("" for none) and <c>mapScripts</c> (one is picked when present).</summary>
    public string MapScript { get; init; } = "";

    public IReadOnlyList<string> MapScripts { get; init; } = [];

    /// <summary>AlienDeployment <c>depth</c> [min, max]; a positive maximum overrides the terrain's.</summary>
    public int MinimumDepth { get; init; }

    public int MaximumDepth { get; init; }

    /// <summary>AlienDeployment <c>enviroEffects</c> ("" for the terrain's).</summary>
    public string EnviroEffects { get; init; } = "";

    /// <summary>AlienDeployment <c>customUfo</c>: the UFO an addUFO command uses when the battle has none.</summary>
    public string CustomUfo { get; init; } = "";

    /// <summary>AlienDeployment <c>forcePercentageOutsideUfo</c>: outside spawns apply without a UFO too.</summary>
    public bool ForcePercentageOutsideUfo { get; init; }

    /// <summary>AlienDeployment <c>civilians</c>, <c>civiliansByType</c> and <c>civilianSpawnNodeRank</c>.</summary>
    public int Civilians { get; init; }

    public IReadOnlyDictionary<string, int> CiviliansByType { get; init; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, int>(new Dictionary<string, int>());

    public int CivilianSpawnNodeRank { get; init; }

    /// <summary>AlienDeployment <c>markCiviliansAsVIP</c>.</summary>
    public bool MarkCiviliansAsVip { get; init; }

    /// <summary>AlienDeployment <c>objectiveType</c> (a special tile type, -1 for none) and <c>objectivesRequired</c> (0 for all).</summary>
    public int ObjectiveType { get; init; } = -1;
    public int ObjectivesRequired { get; init; }

    /// <summary>AlienDeployment <c>bughuntMinTurn</c> (0 for the mod's), <c>escapeType</c> and <c>vipSurvivalPercentage</c>.</summary>
    public int BughuntMinTurn { get; init; }
    public int EscapeType { get; init; }
    public int VipSurvivalPercentage { get; init; }

    /// <summary>AlienDeployment <c>noWeaponPile</c>: the craft's leftover items go home instead of lying on the map.</summary>
    public bool NoWeaponPile { get; init; }

    /// <summary>AlienDeployment <c>data</c>: the alien groups to deploy.</summary>
    public IReadOnlyList<RuntimeDeploymentData> Data { get; init; } = [];

    /// <summary>Why the <c>data</c> node is unusable (the reference throws reading it), or null.</summary>
    public string? DataInvalid { get; init; }
}

/// <summary>
/// DeploymentData: an alien rank (or a custom unit type), quantities by difficulty (low, medium, high), random extras
/// (<c>dQty</c>, <c>extraQty</c>), the chance to deploy outside the UFO, and the item sets by alien item level.
/// </summary>
public sealed record RuntimeDeploymentData(
    int AlienRank,
    string CustomUnitType,
    int LowQuantity,
    int MediumQuantity,
    int HighQuantity,
    int DifficultyQuantity,
    int ExtraQuantity,
    int PercentageOutsideUfo,
    IReadOnlyList<IReadOnlyList<string>> ItemSets,
    IReadOnlyList<IReadOnlyList<string>> ExtraRandomItems)
{
    internal static IReadOnlyList<RuntimeDeploymentData> ReadAll(IReadOnlyList<Oxce.Formats.Yaml.YamlNode> nodes, out string? invalid)
    {
        invalid = null;
        var result = new List<RuntimeDeploymentData>();
        try
        {
            foreach (var node in nodes)
            {
                if (node is not Oxce.Formats.Yaml.YamlMappingNode map)
                    throw new Oxce.Formats.Yaml.YamlFormatException("Deployment data entries must be mappings.", node.Span);
                result.Add(new RuntimeDeploymentData(map.ReadInt32("alienRank", 0), map.ReadString("customUnitType", ""),
                    map.ReadInt32("lowQty", 0), map.ReadInt32("medQty", 0), map.ReadInt32("highQty", 0), map.ReadInt32("dQty", 0),
                    map.ReadInt32("extraQty", 0), map.ReadInt32("percentageOutsideUfo", 0), Sets(map, "itemSets"), Sets(map, "extraRandomItems")));
            }
        }
        catch (Exception exception) when (exception is Oxce.Formats.Yaml.YamlFormatException or FormatException or InvalidDataException or OverflowException)
        {
            invalid = exception.Message;
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static ReadOnlyCollection<IReadOnlyList<string>> Sets(YamlMappingNode map, string key) =>
        !map.TryGet(key, out var node) ? Array.AsReadOnly(Array.Empty<IReadOnlyList<string>>())
            : Array.AsReadOnly(Oxce.Formats.Yaml.YamlValueReader.ReadSequence(node!, static set => (IReadOnlyList<string>)
                Array.AsReadOnly(Oxce.Formats.Yaml.YamlValueReader.ReadSequence(set, Oxce.Formats.Yaml.YamlValueReader.ReadString))));
}

/// <summary>Reference: <c>AlienDeployment::getAlienRaceEvolution</c> tuples.</summary>
public sealed record RuntimeAlienRaceEvolution(int Month, string FromRace, string ToRace);

internal static class RuntimeWorldReadOnly
{
    public static IReadOnlyDictionary<TKey, TValue> Dictionary<TKey, TValue>(IDictionary<TKey, TValue> source)
        where TKey : notnull => new ReadOnlyDictionary<TKey, TValue>(new Dictionary<TKey, TValue>(source));
}
