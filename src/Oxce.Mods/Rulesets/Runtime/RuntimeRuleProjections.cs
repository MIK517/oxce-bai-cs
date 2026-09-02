using Oxce.Mods.Resources;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Content;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed record RuntimeIdentityRule(string Id);

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
    RuntimeIndexedResourceReference PlaceSound);

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
    RuleHandleList<RuntimeScriptFamily> Scripts);

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
    RuleHandleList<RuntimeScriptFamily> Scripts);

public sealed record RuntimeArmorRule(
    int ListOrder,
    int Size,
    int SpaceOccupied,
    string StoreItemId,
    RuleHandle<ItemRuleFamily>? StoreItem);

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
    RuleHandleList<RuntimeScriptFamily> Scripts);

public sealed record RuntimeStartingFacility(
    RuleHandle<FacilityRuleFamily> Rule,
    int X,
    int Y,
    int BuildTime);

public sealed record RuntimeStartingCraft(RuleHandle<CraftRuleFamily> Rule, int Id);

public sealed record RuntimeStartingSoldier(RuleHandle<SoldierRuleFamily> Rule, int Id);

public sealed record RuntimeStartingItem(RuleHandle<ItemRuleFamily> Rule, int Quantity);

public sealed record RuntimeStartingSoldierBatch(RuleHandle<SoldierRuleFamily> Rule, int Quantity);

public sealed record RuntimeStartingBaseTemplate(
    StartingBaseVariant Variant,
    IReadOnlyList<RuntimeStartingFacility> Facilities,
    IReadOnlyList<RuntimeStartingCraft> Crafts,
    IReadOnlyList<RuntimeStartingSoldier> Soldiers,
    IReadOnlyList<RuntimeStartingItem> Items,
    int RandomSoldierCount,
    IReadOnlyList<RuntimeStartingSoldierBatch> RandomSoldiers);

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
    public RuntimeStartingBaseTemplate? GetStartingBase(StartingBaseVariant variant) =>
        StartingBases.FirstOrDefault(template => template.Variant == variant) ??
        StartingBases.FirstOrDefault(static template => template.Variant == StartingBaseVariant.Default);
}

public readonly struct RuntimeScriptFamily;

public sealed record RuntimeScriptRule(ContentScriptArtifact Artifact);
