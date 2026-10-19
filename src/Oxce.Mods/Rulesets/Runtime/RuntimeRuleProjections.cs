using System.Collections.Frozen;
using System.Collections.ObjectModel;
using Oxce.Mods.Resources;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.EquipmentProduction;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed record RuntimeIdentityRule(string Id);

/// <summary>Immutable inventory metadata for read-only rule scripts.</summary>
public sealed record RuntimeInventoryRule(string Id, int Type, int Hand)
{
    public IReadOnlyDictionary<string, int> Costs { get; init; } = FrozenDictionary<string, int>.Empty;
}

/// <summary>Read-only Unit metadata used by script rule lookups.</summary>
public sealed record RuntimeUnitRule(string Type)
{
    public RuleHandle<ArmorRuleFamily>? Armor { get; init; }
    public RuleHandle<UnitRuleFamily>? SpawnUnit { get; init; }
    public RuleHandle<ItemRuleFamily>? LiveAlien { get; init; }
    public RuleHandle<ItemRuleFamily>? CivilianRecoveryItem { get; init; }
}

public sealed record RuntimeResearchRule(
    string Lookup,
    string SpawnedItem,
    int SpawnedItemCount,
    IReadOnlyList<string> SpawnedItemList,
    IReadOnlyList<string> DecreaseCounters,
    IReadOnlyList<string> IncreaseCounters,
    string SpawnedEvent,
    int Cost,
    int Points,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Unlocks,
    IReadOnlyList<string> Disables,
    IReadOnlyList<string> Reenables,
    IReadOnlyList<string> GetOneFree,
    bool SequentialGetOneFree,
    IReadOnlyList<ResearchProtectedTopics> GetOneFreeProtected,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> RequiredBaseFunctions,
    string? NeededItem,
    bool NeedItem,
    bool DestroyItem,
    bool ReturnsItem,
    bool Repeatable,
    int ListOrder,
    IReadOnlyDictionary<string, ulong> Events)
{
    /// <summary>
    /// Mod::getFinalResearch: true only for the one research that shows the Cydonia button. Of several topics
    /// marked <c>unlockFinalMission</c>, the one whose last updating mod loads latest wins.
    /// </summary>
    public bool UnlockFinalMission { get; init; }

    /// <summary>RuleResearch::getCutscene: the video shown on discovery, whose flags may end the campaign.</summary>
    public string Cutscene { get; init; } = string.Empty;
}

/// <summary>RuleVideo::getWinGame/getLoseGame: whether showing the cutscene ends the campaign.</summary>
public sealed record RuntimeCutscene(bool WinGame, bool LoseGame);

public sealed record RuntimeManufactureMaterial(
    string Id,
    int Quantity,
    RuleHandle<ItemRuleFamily>? Item,
    RuleHandle<CraftRuleFamily>? Craft);

public sealed record RuntimeManufactureRule(
    string Category,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> RequiredBaseFunctions,
    int Space,
    int Time,
    int Cost,
    int Points,
    bool Refund,
    IReadOnlyList<RuntimeManufactureMaterial> RequiredMaterials,
    IReadOnlyList<RuntimeManufactureMaterial> ProducedMaterials,
    IReadOnlyList<RandomProducedItems> RandomProducedItems,
    string SpawnedPersonType,
    string SpawnedPersonName,
    RuntimeSoldierTemplate? SpawnedSoldierTemplate,
    IReadOnlyList<int> TransferTimes,
    int ListOrder,
    IReadOnlyDictionary<string, ulong> Events);

public sealed record RuntimePurchaseRequirements(
    int MonthlyLimit, string MonthlyLimitMessage, string AlliedCountry,
    IReadOnlyList<string> BaseFunctions, IReadOnlyList<string> BuyResearch);

public sealed record RuntimeGlobeLabel(string Type, double LabelLongitude, double LabelLatitude, int LabelColor,
    int ZoomLevel);

public sealed record RuntimeCountryRule(
    int FundingBase,
    int FundingCap,
    double LabelLongitude,
    double LabelLatitude,
    int LabelColor,
    int ZoomLevel,
    IReadOnlyList<GeographicArea> Areas,
    IReadOnlyList<string> ProvidedBaseFunctions,
    IReadOnlyList<string> ForbiddenBaseFunctions,
    string SignedPactEventId,
    RuleHandle<EventRuleFamily>? SignedPactEvent,
    string RejoinedXcomEventId,
    RuleHandle<EventRuleFamily>? RejoinedXcomEvent,
    RuleHandleList<RuntimeScriptFamily> Scripts);

public sealed record RuntimeRegionRule(
    int BaseCost,
    IReadOnlyList<GeographicArea> Areas,
    IReadOnlyList<MissionZone> MissionZones,
    IReadOnlyDictionary<string, ulong> MissionWeights,
    ulong RegionWeight,
    string MissionRegionId,
    RuleHandle<RegionRuleFamily>? MissionRegion,
    IReadOnlyList<string> ProvidedBaseFunctions,
    IReadOnlyList<string> ForbiddenBaseFunctions);

public sealed record RuntimeIndexedResourceReference(
    string SetId,
    int RuntimeIndex,
    ResourceKind Kind,
    ResourceHandle? Override);

public sealed record RuntimeFacilityItemCost(
    string ItemId,
    RuleHandle<ItemRuleFamily>? Item,
    int Build,
    int Refund);

public sealed record RuntimeFacilityRule(
    int SizeX,
    int SizeY,
    bool Lift,
    int BuildCost,
    int RefundValue,
    int BuildTime,
    int MonthlyCost,
    int Storage,
    int Personnel,
    int Aliens,
    int Crafts,
    int Laboratories,
    int Workshops,
    int PsiLaboratories,
    int ListOrder,
    string MapName,
    RuntimeRuleReferenceList<ResearchRuleFamily> Requirements,
    RuleHandleList<FacilityRuleFamily> BuildOverFacilities,
    RuleHandleList<FacilityRuleFamily> LeavesBehindOnSell,
    RuleHandle<FacilityRuleFamily>? DestroyedFacility,
    RuleHandle<ItemRuleFamily>? AmmoItem,
    IReadOnlyList<RuntimeFacilityItemCost> BuildCostItems,
    RuntimeIndexedResourceReference SpriteShape,
    RuntimeIndexedResourceReference SpriteFacility,
    RuntimeIndexedResourceReference FireSound,
    RuntimeIndexedResourceReference HitSound,
    RuntimeIndexedResourceReference PlaceSound)
{
    public int PrisonType { get; init; }
    public int AmmoMaximum { get; init; }
    /// <summary>RuleBaseFacility::getMissileAttraction: the facility's weight as a missile target.</summary>
    public int MissileAttraction { get; init; } = 100;
    /// <summary>How many crafts the base view places in this hangar (its distinct leading craft slots).</summary>
    public int DrawnCraftSlots { get; init; } = 1;
    /// <summary>RuleBaseFacility base-defense fields (BaseDefenseState): a non-zero defense value makes the
    /// facility fire at an attacking UFO; a grav shield adds a firing pass.</summary>
    public int DefenseValue { get; init; }
    public int HitRatio { get; init; }
    public bool GravShield { get; init; }
    public int AmmoNeeded { get; init; }
    public bool UnifiedDamageFormula { get; init; }
    public int ShieldDamageModifier { get; init; } = 100;
    public int RearmRate { get; init; }
    public bool CanBeBuiltOver { get; init; }
    public int FakeUnderwater { get; init; }
    public bool UpgradeOnly { get; init; }
    public int RemovalTime { get; init; }
    public int MaximumAllowedPerBase { get; init; }
    public int TrainingRooms { get; init; }
    public int ManaRecoveryPerDay { get; init; }
    public int HealthRecoveryPerDay { get; init; }
    public float SickBayAbsoluteBonus { get; init; }
    public float SickBayRelativeBonus { get; init; }
    public IReadOnlyList<string> RequiredBaseFunctions { get; init; } = [];
    public IReadOnlyList<string> ForbiddenBaseFunctions { get; init; } = [];
    public int HangarType { get; init; }
    public IReadOnlyList<string> ProvidedBaseFunctions { get; init; } = [];
    public int SightRange { get; init; }
    public int SightChance { get; init; }
    public int RadarRange { get; init; }
    public int RadarChance { get; init; }
    public bool Hyperwave { get; init; }
    public bool MindShield { get; init; }
    public int MindShieldPower { get; init; } = 1;
}

public sealed record RuntimeCraftRule(
    int ListOrder,
    int CostBuy,
    int CostRent,
    int CostSell,
    int TransferTime,
    int FuelMaximum,
    int DamageMaximum,
    int SpeedMaximum,
    int SoldierCapacity,
    int VehicleCapacity,
    int EffectiveMaximumUnits,
    int EffectiveMaximumVehiclesAndLargeSoldiers,
    bool AllowLanding,
    RuntimeRuleReferenceList<ResearchRuleFamily> Requirements,
    RuleHandle<ItemRuleFamily>? RefuelItem,
    RuleHandleList<CraftWeaponRuleFamily> FixedWeapons,
    RuleHandleList<RuntimeScriptFamily> Scripts)
{
    /// <summary>RuleCraft::getGroups: map script commands with craft groups run only for a craft in one of them.</summary>
    public IReadOnlyList<int> Groups { get; init; } = [];

    /// <summary>RuleCraft::isMapVisible: whether the craft's map starts discovered.</summary>
    public bool MapVisible { get; init; } = true;

    /// <summary>RuleCraft::getBattlescapeTerrainData: the craft's map, or null.</summary>
    public RuntimeTerrain? BattlescapeTerrain { get; init; }

    public string Type { get; init; } = "";
    public RuntimePurchaseRequirements Purchase { get; init; } = new(0, "", "", [], []);
    /// <summary>RuleCraft::_stats, before installed weapon bonuses and capacity limits.</summary>
    public IReadOnlyDictionary<string, int> DefaultStats { get; init; } =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
    public int HangarType { get; init; }
    /// <summary>Runtime <c>GlobeMarkers</c> frame, or -1 for Craft::getMarker's default.</summary>
    public int Marker { get; init; } = -1;
    public int WeaponSlots { get; init; }
    /// <summary>RuleCraft::getWeaponSlotString: the craft screen label of each weapon slot ("" hides it).</summary>
    public IReadOnlyList<string> WeaponStrings { get; init; } = ["STR_WEAPON_ONE", "STR_WEAPON_TWO", "", ""];
    public int RefuelRate { get; init; }
    public int RepairRate { get; init; }
    public int ShieldCapacity { get; init; }
    public int ShieldRechargeInGeoscape { get; init; }
    public int ShieldRechargeAtBase { get; init; }
    /// <summary>RuleCraft::_missilePower; a positive value makes the craft a missile (RuleCraft::isMissile).</summary>
    public int MissilePower { get; init; }
    /// <summary>RuleCraft::getScore: the X-COM activity lost when the craft is destroyed.</summary>
    public int Score { get; init; }
    /// <summary>RuleCraft::_maxAltitude; above -1 the craft is water-only (RuleCraft::isWaterOnly) and cannot
    /// engage a UFO above this altitude.</summary>
    public int MaximumAltitude { get; init; } = -1;
    public bool IsMissile => MissilePower > 0;
    public bool IsWaterOnly => MaximumAltitude > -1;
    public int SightRange { get; init; }
    public int RadarRange { get; init; }
    public int RadarChance { get; init; }
    public int MaximumItems { get; init; }
    public double MaximumStorageSpace { get; init; }
    public bool NotifyWhenRefueled { get; init; }
    public bool Undetectable { get; init; }
    public bool PatrolWithoutFuel { get; init; }
    /// <summary>RuleCraft::getSpacecraft: the craft can fly the final mission.</summary>
    public bool Spacecraft { get; init; }
    public bool AutoPatrol { get; init; }
    public IReadOnlyList<string> FixedWeaponSlots { get; init; } = [];
    public int Pilots { get; init; }
    public int MaximumSoldiers { get; init; } = -1;
    public int MaximumVehicles { get; init; } = -1;
    public int MaximumSmallSoldiers { get; init; } = -1;
    public int MaximumLargeSoldiers { get; init; } = -1;
    public int MaximumSmallVehicles { get; init; } = -1;
    public int MaximumLargeVehicles { get; init; } = -1;
    public int MaximumSmallUnits { get; init; } = -1;
    public int MaximumLargeUnits { get; init; } = -1;
    public bool OnlyOneSoldierGroupAllowed { get; init; }
    public IReadOnlyList<int> AllowedSoldierGroups { get; init; } = [];
    public IReadOnlyList<int> AllowedArmorGroups { get; init; } = [];
    public IReadOnlyDictionary<int, int> ArmorGroupLimits { get; init; } = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>());
    public IReadOnlyDictionary<string, short> PilotMinimumStats { get; init; } = new ReadOnlyDictionary<string, short>(new Dictionary<string, short>());
    public IReadOnlyList<string> RequiredPilotBonuses { get; init; } = [];
}

public sealed record RuntimeCraftWeaponRule(int AmmoMaximum, int RearmRate, string Launcher, string Clip,
    IReadOnlyDictionary<string, int> BonusStats)
{
    public bool StatisticalBulletSaving { get; init; }
    public double BonusStorageSpace { get; init; }

    // Dogfight properties (RuleCraftWeapon; read by DogfightState and CraftWeaponProjectile).
    public int Damage { get; init; }
    public int ShieldDamageModifier { get; init; } = 100;
    public int Range { get; init; }
    public int Accuracy { get; init; }
    public int ReloadCautious { get; init; }
    public int ReloadStandard { get; init; }
    public int ReloadAggressive { get; init; }
    /// <summary>CraftWeaponProjectileType: 0 stingray missile, 1 avalanche missile, 2 cannon round
    /// (default), 3 fusion ball, 4 laser beam, 5 plasma beam.</summary>
    public int ProjectileType { get; init; } = 2;
    public int ProjectileSpeed { get; init; }
    public int WeaponType { get; init; }
    public int TractorBeamPower { get; init; }
    public bool UnifiedDamageFormula { get; init; }
    public bool UnderwaterOnly { get; init; }
}

/// <summary>Raw rule action accuracy before tactical bonuses and random rolls.</summary>
public sealed record RuntimeItemAccuracy(int Aimed, int Auto, int Snap, int Melee, int Use, int Mind, int Panic, int Throw)
{
    public static RuntimeItemAccuracy Default { get; } = new(0, 0, 0, 0, 0, 0, 20, 100);
}

public sealed record RuntimeItemRule(
    string Name,
    int ListOrder,
    int CostBuy,
    int CostSell,
    int TransferTime,
    int Weight,
    double Size,
    int BattleType,
    int ClipSize,
    RuleHandleList<ResearchRuleFamily> Requirements,
    IReadOnlyList<RuleHandleList<ItemRuleFamily>> CompatibleAmmo,
    RuleHandleList<RuntimeScriptFamily> Scripts)
{
    public string Type { get; init; } = "";
    public RuntimeItemAccuracy Accuracy { get; init; } = RuntimeItemAccuracy.Default;
    public RuntimeItemDamageRule Damage { get; init; } = new(0, 8, 0, 0, 1, new(0, 0.1f, 0, 1, 0, 0, 0, 0.25f, 0.5f, 0, 1));
    public RuntimeItemDamageRule MeleeDamage { get; init; } = new(0, 8, 0, 0, 1, new(0, 0.1f, 0, 1, 0, 0, 0, 0.25f, 0.5f, 0, 1));
    public bool TwoHanded { get; init; }
    public bool BlockingBothHands { get; init; }
    public bool WaterOnly { get; init; }
    public IReadOnlySet<string> Categories { get; init; } = FrozenSet<string>.Empty;
    public int Power { get; init; }
    public int MeleePower { get; init; }
    public int ArmorValue { get; init; } = 20;
    public int Waypoints { get; init; }
    public RuntimePurchaseRequirements Purchase { get; init; } = new(0, "", "", [], []);
    public bool IsAlien { get; init; }
    public bool Recoverable { get; init; } = true;
    public bool CorpseRecoverable { get; init; } = true;
    public int VehicleFixedAmmoSlot { get; init; }
    public RuleHandle<ArmorRuleFamily>? VehicleArmor { get; init; }
    public int PrisonType { get; init; }
    public int MonthlyMaintenance { get; init; }
    public int MonthlySalary { get; init; }
}

public sealed record RuntimeArmorRule(
    int ListOrder,
    int Size,
    int SpaceOccupied,
    string StoreItemId,
    RuleHandle<ItemRuleFamily>? StoreItem)
{
    public string Type { get; init; } = "";
    public RuntimeArmorVision Vision { get; init; } = RuntimeArmorVision.Default;
    public RuleHandle<ItemRuleFamily>? CorpseGeoscape { get; init; }
    public int Group { get; init; }
    public IReadOnlyDictionary<string, short> Stats { get; init; } = new ReadOnlyDictionary<string, short>(new Dictionary<string, short>());
    public RuntimeArmorScriptFields Script { get; init; } = RuntimeArmorScriptFields.Default;
}

/// <summary>
/// Remaining read-only Armor::ScriptRegister providers: side armor in UnitSide order
/// (front, left = side + leftArmorDiff, right, rear, under), presentation fields,
/// base move costs, linked battle corpses and required research.
/// </summary>
public sealed record RuntimeArmorScriptFields(
    IReadOnlyList<int> Sides,
    int DrawingRoutine,
    bool DrawBubbles,
    int DeathFrames,
    bool ConstantAnimation,
    (int Time, int Energy) MoveCostBase,
    (int Time, int Energy) MoveCostBaseNormal,
    (int Time, int Energy) MoveCostBaseFly,
    IReadOnlyList<RuleHandle<ItemRuleFamily>?> CorpseBattle,
    RuleHandle<ResearchRuleFamily>? RequiredResearch)
{
    public static RuntimeArmorScriptFields Default { get; } =
        new([0, 0, 0, 0, 0], 0, false, 3, false, (100, 100), (100, 100), (100, 100), [], null);
}

/// <summary>Immutable Armor vision and light metadata, before tactical calculations.</summary>
public sealed record RuntimeArmorVision(
    int VisibilityAtDark,
    int VisibilityAtDay,
    int CamouflageAtDark,
    int CamouflageAtDay,
    int AntiCamouflageAtDark,
    int AntiCamouflageAtDay,
    int HeatVision,
    int VisibilityThroughFire,
    int PersonalLight,
    int PersonalLightHostile,
    int PersonalLightNeutral)
{
    public static RuntimeArmorVision Default { get; } = new(0, 0, 0, 0, 0, 0, 0, 100, 15, 0, 0);
}

public sealed record RuntimeSoldierRule(
    int ListOrder,
    int Group,
    int CostBuy,
    int CostSalary,
    int TransferTime,
    bool AllowPromotion,
    bool AllowPiloting,
    RuleHandle<ArmorRuleFamily> Armor,
    RuntimeRuleReferenceList<ResearchRuleFamily> Requirements,
    RuleHandleList<SkillRuleFamily> Skills,
    RuleHandleList<RuntimeScriptFamily> Scripts)
{
    public RuntimePurchaseRequirements Purchase { get; init; } = new(0, "", "", [], []);
    public IReadOnlyDictionary<string, short> MinimumStats { get; init; } = new ReadOnlyDictionary<string, short>(new Dictionary<string, short>());
    public IReadOnlyDictionary<string, short> MaximumStats { get; init; } = new ReadOnlyDictionary<string, short>(new Dictionary<string, short>());
    public int FemaleFrequency { get; init; } = 50;
    public IReadOnlyList<RuntimeSoldierNamePool> NamePools { get; init; } = [];
    public RuntimeSoldierTemplate? SpawnedTemplate { get; init; }
    public IReadOnlyList<int> Salaries { get; init; } = [];
    public int RankCount { get; init; }
    public IReadOnlyDictionary<string, short> StatCaps { get; init; } = new ReadOnlyDictionary<string, short>(new Dictionary<string, short>());
    public IReadOnlyDictionary<string, short> TrainingStatCaps { get; init; } = new ReadOnlyDictionary<string, short>(new Dictionary<string, short>());
    /// <summary>RuleSoldier::getDogfightExperience: percent chances per interception win.</summary>
    public IReadOnlyDictionary<string, short> DogfightExperience { get; init; } = new ReadOnlyDictionary<string, short>(new Dictionary<string, short>());
}

public sealed record RuntimeSoldierBonusRule(int ListOrder, IReadOnlyDictionary<string, short> Stats);
public sealed record RuntimeSoldierTransformationRule(
    IReadOnlyDictionary<string, string> Strings,
    IReadOnlyDictionary<string, int> Integers,
    IReadOnlyDictionary<string, bool> Booleans,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> RequiredBaseFunctions,
    IReadOnlyList<string> AllowedSoldierTypes,
    IReadOnlyList<string> RequiredPreviousTransformations,
    IReadOnlyList<string> ForbiddenPreviousTransformations,
    IReadOnlyList<string> RemovedTransformations,
    IReadOnlyDictionary<string, int> RequiredItems,
    IReadOnlyDictionary<string, int> RequiredCommendations,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, short>> StatSets,
    IReadOnlyDictionary<string, ulong> Events);
/// <summary>
/// RuleCommendations as SoldierDiary::manageCommendations reads it: <see cref="Criteria"/> in std::map (UTF-8 byte)
/// order, the kill criteria's OR groups of AND criteria, the mission filters, and the research and soldier types.
/// </summary>
public sealed record RuntimeCommendationRule(IReadOnlyList<string> SoldierBonusTypes)
{
    public IReadOnlyList<KeyValuePair<string, IReadOnlyList<int>>> Criteria { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<PersonnelTactical.CommendationKillCriterion>> KillCriteria { get; init; } = [];
    public IReadOnlyList<string> MissionMarkers { get; init; } = [];
    public IReadOnlyList<string> MissionTypes { get; init; } = [];
    public IReadOnlyList<string> Requires { get; init; } = [];
    public IReadOnlyList<string> Units { get; init; } = [];
}

public sealed record RuntimeStartingFacility(
    RuleHandle<FacilityRuleFamily> Rule,
    int X,
    int Y,
    int BuildTime)
{
    public int Ammo { get; init; }
    public bool AmmoMissingReported { get; init; }
    public bool Disabled { get; init; }
    public bool HadPreviousFacility { get; init; }
}

public sealed record RuntimeStartingCraft(RuleHandle<CraftRuleFamily> Rule, int Id)
{
    public RuntimeCraftTemplate? Template { get; init; }
}

public sealed record RuntimeStartingSoldier(RuleHandle<SoldierRuleFamily> Rule, int Id)
{
    public RuntimeSoldierTemplate? Template { get; init; }
    public string CraftType { get; init; } = string.Empty;
    public int CraftId { get; init; }
}

public sealed record RuntimeStartingItem(RuleHandle<ItemRuleFamily> Rule, int Quantity);

public sealed record RuntimeStartingSoldierBatch(RuleHandle<SoldierRuleFamily> Rule, int Quantity);

public sealed record RuntimeStartingBaseTemplate(
    StartingBaseVariant Variant,
    IReadOnlyList<RuntimeStartingFacility> Facilities,
    IReadOnlyList<RuntimeStartingCraft> Crafts,
    IReadOnlyList<RuntimeStartingSoldier> Soldiers,
    IReadOnlyList<RuntimeStartingItem> Items,
    int RandomSoldierCount,
    IReadOnlyList<RuntimeStartingSoldierBatch> RandomSoldiers,
    int Scientists,
    int Engineers)
{
    public bool AssignRandomSoldiers { get; init; }
}

public sealed record RuntimeCampaignSettings(
    CampaignStartTime StartingTime,
    int StartingDifficulty,
    int InitialFunding,
    int CostHireEngineer,
    int CostHireScientist,
    int CostEngineer,
    int CostScientist,
    int PersonnelTransferTime,
    int GlobalTransferCostMultiplier,
    int GlobalTransferCostDivisor,
    RuleHandle<ResearchRuleFamily>? PsiUnlockResearch,
    RuleHandle<ResearchRuleFamily>? FakeUnderwaterBaseUnlockResearch,
    RuleHandle<ResearchRuleFamily>? NewBaseUnlockResearch,
    RuleHandle<ResearchRuleFamily>? HireScientistsUnlockResearch,
    RuleHandle<ResearchRuleFamily>? HireEngineersUnlockResearch,
    RuleHandle<FacilityRuleFamily>? DestroyedFacility,
    IReadOnlyList<RuntimeStartingBaseTemplate> StartingBases)
{
    public RuntimeGlobe Globe { get; init; } = RuntimeGlobe.Empty;
    public int BuildTimeReductionScaling { get; init; } = 100;
    public int CustomTrainingFactor { get; init; } = 100;
    public int ManaWoundThreshold { get; init; } = 200;
    public int HealthWoundThreshold { get; init; } = 100;
    public int EscortRange { get; init; } = 20;
    /// <summary>Mod::_drawEnemyRadarCircles: 0 none, 1 hyper-detected hunter-killers, above 1 all.</summary>
    public int DrawEnemyRadarCircles { get; init; } = 1;
    /// <summary><c>extraGlobeLabels</c> in load order; Globe::drawDetail shows each from its zoom level.</summary>
    public IReadOnlyList<RuntimeGlobeLabel> GlobeLabels { get; init; } = [];
    public int MaximumViewDistance { get; init; } = 20;
    public int MaximumDarknessToSeeUnits { get; init; } = 9;
    public bool HunterKillerFastRetarget { get; init; } = true;
    public bool AllowAlienBasesOnWrongTextures { get; init; } = true;
    public bool AllowCountriesToCancelAlienPact { get; init; }
    public bool BuildInfiltrationBaseCloseToTheCountry { get; init; }
    public bool InfiltrateRandomCountryInTheRegion { get; init; }
    public IReadOnlyList<int> BuyPriceCoefficients { get; init; } = Array.AsReadOnly<int>([100, 100, 100, 100, 100]);
    public IReadOnlyList<int> SellPriceCoefficients { get; init; } = Array.AsReadOnly<int>([100, 100, 100, 100, 100]);
    public IReadOnlyList<string> HireScientistsBaseFunctions { get; init; } = [];
    public IReadOnlyList<string> HireEngineersBaseFunctions { get; init; } = [];
    public int HireByCountryOdds { get; init; }
    public int HireByRegionOdds { get; init; }
    public double PerformanceBonusFactor { get; init; }
    /// <summary>Global rules read by interception, landing and base defense.</summary>
    public CampaignInterceptionSettings Interception { get; init; } = CampaignInterceptionSettings.Default;
    /// <summary>Global rules read by the month rollover and the monthly report.</summary>
    public CampaignMonthlySettings Monthly { get; init; } = CampaignMonthlySettings.Default;
    /// <summary>Mod::getDefeatScore: the monthly rating threshold before the difficulty term.</summary>
    public int DefeatScore { get; init; }
    /// <summary>Mod::getDefeatFunds: funds at or below which the monthly report warns, then ends the campaign.</summary>
    public int DefeatFunds { get; init; }
    /// <summary>Every <c>cutscenes</c> rule by ID (Mod::getVideo), for the endings its flags trigger.</summary>
    public IReadOnlyDictionary<string, RuntimeCutscene> Cutscenes { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, RuntimeCutscene>(new Dictionary<string, RuntimeCutscene>());
    /// <summary>Mod::getFixedUserOptions: options that the loaded mods force for every campaign.</summary>
    public IReadOnlyDictionary<string, string> FixedUserOptions { get; init; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    public RuntimeStartingBaseTemplate? GetStartingBase(StartingBaseVariant variant) =>
        StartingBases.FirstOrDefault(template => template.Variant == variant) ??
        StartingBases.FirstOrDefault(static template => template.Variant == StartingBaseVariant.Default);
}

public readonly struct RuntimeScriptFamily;

public sealed record RuntimeScriptRule(ContentScriptArtifact Artifact);
