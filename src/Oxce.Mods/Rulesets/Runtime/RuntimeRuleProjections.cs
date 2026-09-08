using Oxce.Mods.Resources;
using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Content;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed record RuntimeIdentityRule(string Id);

public sealed record RuntimePurchaseRequirements(
    int MonthlyLimit, string MonthlyLimitMessage, string AlliedCountry,
    IReadOnlyList<string> BaseFunctions, IReadOnlyList<string> BuyResearch);

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
    public int HangarType { get; init; }
    public IReadOnlyList<string> ProvidedBaseFunctions { get; init; } = [];
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
    public RuntimePurchaseRequirements Purchase { get; init; } = new(0, "", "", [], []);
    public int HangarType { get; init; }
    public int WeaponSlots { get; init; }
    public int RefuelRate { get; init; }
    public bool NotifyWhenRefueled { get; init; }
    public IReadOnlyList<string> FixedWeaponSlots { get; init; } = [];
    public int Pilots { get; init; }
    public int MaximumSoldiers { get; init; } = -1;
    public int MaximumSmallSoldiers { get; init; } = -1;
    public int MaximumSmallUnits { get; init; } = -1;
    public bool OnlyOneSoldierGroupAllowed { get; init; }
    public IReadOnlyList<int> AllowedSoldierGroups { get; init; } = [];
    public IReadOnlyList<int> AllowedArmorGroups { get; init; } = [];
    public IReadOnlyDictionary<int, int> ArmorGroupLimits { get; init; } = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>());
    public IReadOnlyDictionary<string, short> PilotMinimumStats { get; init; } = new ReadOnlyDictionary<string, short>(new Dictionary<string, short>());
    public IReadOnlyList<string> RequiredPilotBonuses { get; init; } = [];
}

public sealed record RuntimeCraftWeaponRule(int AmmoMaximum, int RearmRate, string Launcher, string Clip,
    IReadOnlyDictionary<string, int> BonusStats);

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
    public RuntimePurchaseRequirements Purchase { get; init; } = new(0, "", "", [], []);
    public bool IsAlien { get; init; }
    public int VehicleFixedAmmoSlot { get; init; }
    public RuleHandle<ArmorRuleFamily>? VehicleArmor { get; init; }
    public int PrisonType { get; init; }
}

public sealed record RuntimeArmorRule(
    int ListOrder,
    int Size,
    int SpaceOccupied,
    string StoreItemId,
    RuleHandle<ItemRuleFamily>? StoreItem)
{
    public int Group { get; init; }
    public IReadOnlyDictionary<string, short> Stats { get; init; } = new ReadOnlyDictionary<string, short>(new Dictionary<string, short>());
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
}

public sealed record RuntimeSoldierBonusRule(int ListOrder, IReadOnlyDictionary<string, short> Stats);
public sealed record RuntimeCommendationRule(IReadOnlyList<string> SoldierBonusTypes);

public sealed record RuntimeStartingFacility(
    RuleHandle<FacilityRuleFamily> Rule,
    int X,
    int Y,
    int BuildTime);

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
    public IReadOnlyList<int> BuyPriceCoefficients { get; init; } = Array.AsReadOnly<int>([100, 100, 100, 100, 100]);
    public IReadOnlyList<int> SellPriceCoefficients { get; init; } = Array.AsReadOnly<int>([100, 100, 100, 100, 100]);
    public IReadOnlyList<string> HireScientistsBaseFunctions { get; init; } = [];
    public IReadOnlyList<string> HireEngineersBaseFunctions { get; init; } = [];
    public int HireByCountryOdds { get; init; }
    public int HireByRegionOdds { get; init; }
    public RuntimeStartingBaseTemplate? GetStartingBase(StartingBaseVariant variant) =>
        StartingBases.FirstOrDefault(template => template.Variant == variant) ??
        StartingBases.FirstOrDefault(static template => template.Variant == StartingBaseVariant.Default);
}

public readonly struct RuntimeScriptFamily;

public sealed record RuntimeScriptRule(ContentScriptArtifact Artifact);
