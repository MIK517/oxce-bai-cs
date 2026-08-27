using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.CampaignStart;

public sealed record GeographicArea(double LongitudeMinimum, double LongitudeMaximum, double LatitudeMinimum, double LatitudeMaximum);

public sealed record CountryRule(
    string SignedPactEvent,
    string RejoinedXcomEvent,
    int FundingBase,
    int FundingCap,
    double LabelLongitude,
    double LabelLatitude,
    int LabelColor,
    int ZoomLevel,
    IReadOnlyList<GeographicArea> Areas,
    IReadOnlyList<string> ProvidedBaseFunctions,
    IReadOnlyList<string> ForbiddenBaseFunctions);

public sealed record MissionArea(
    double LongitudeMinimum,
    double LongitudeMaximum,
    double LatitudeMinimum,
    double LatitudeMaximum,
    int Texture,
    string Name)
{
    public bool IsPoint => LongitudeMinimum.Equals(LongitudeMaximum) && LatitudeMinimum.Equals(LatitudeMaximum);
}

public sealed record MissionZone(IReadOnlyList<MissionArea> Areas);

public sealed record RegionRule(
    int BaseCost,
    IReadOnlyList<GeographicArea> Areas,
    IReadOnlyList<MissionZone> MissionZones,
    IReadOnlyDictionary<string, ulong> MissionWeights,
    ulong RegionWeight,
    string MissionRegion,
    IReadOnlyList<string> ProvidedBaseFunctions,
    IReadOnlyList<string> ForbiddenBaseFunctions);

public sealed record FacilityItemCost(int Build, int Refund);
public sealed record FacilityPosition(int X, int Y, int Z);

public sealed record BaseFacilityRule(
    string UfopediaType,
    IReadOnlyList<string> Requires,
    IReadOnlyList<string> RequiredBaseFunctions,
    IReadOnlyList<string> ProvidedBaseFunctions,
    IReadOnlyList<string> ForbiddenBaseFunctions,
    RuleIndexReference SpriteShape,
    RuleIndexReference SpriteFacility,
    bool ConnectorsDisabled,
    int FakeUnderwater,
    int MissileAttraction,
    bool Lift,
    bool HyperWave,
    bool MindShield,
    bool GravShield,
    int MindPower,
    int SizeX,
    int SizeY,
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
    bool SpriteEnabled,
    int SightRange,
    int SightChance,
    int RadarRange,
    int RadarChance,
    int Defense,
    int HitRatio,
    RuleIndexReference FireSound,
    RuleIndexReference HitSound,
    RuleIndexReference PlaceSound,
    int AmmoMaximum,
    int RearmRate,
    int AmmoNeeded,
    bool UnifiedDamageFormula,
    int ShieldDamageModifier,
    string AmmoItem,
    string MapName,
    int ListOrder,
    int TrainingRooms,
    int MaximumAllowedPerBase,
    int ManaRecoveryPerDay,
    int HealthRecoveryPerDay,
    float SickBayAbsoluteBonus,
    float SickBayRelativeBonus,
    int PrisonType,
    int HangarType,
    int RightClickActionType,
    IReadOnlyDictionary<string, FacilityItemCost> BuildCostItems,
    IReadOnlyList<string> LeavesBehindOnSell,
    int RemovalTime,
    bool CanBeBuiltOver,
    bool UpgradeOnly,
    IReadOnlyList<string> BuildOverFacilities,
    IReadOnlyList<FacilityPosition> StorageTiles,
    IReadOnlyList<FacilityPosition> CraftSlots,
    string DestroyedFacility);

internal sealed class CountryBuilder(string id)
{
    public string Id { get; } = id;
    public string SignedPactEvent { get; set; } = string.Empty;
    public string RejoinedXcomEvent { get; set; } = string.Empty;
    public int FundingBase { get; set; }
    public int FundingCap { get; set; }
    public double LabelLongitude { get; set; }
    public double LabelLatitude { get; set; }
    public int LabelColor { get; set; }
    public int ZoomLevel { get; set; }
    public List<GeographicArea> Areas { get; } = [];
    public List<string> ProvidedBaseFunctions { get; } = [];
    public List<string> ForbiddenBaseFunctions { get; } = [];
}

internal sealed class RegionBuilder(string id)
{
    public string Id { get; } = id;
    public int BaseCost { get; set; }
    public List<GeographicArea> Areas { get; } = [];
    public List<MissionZone> MissionZones { get; set; } = [];
    public SortedDictionary<string, ulong> MissionWeights { get; } = new(StringComparer.Ordinal);
    public ulong RegionWeight { get; set; }
    public string MissionRegion { get; set; } = string.Empty;
    public List<string> ProvidedBaseFunctions { get; } = [];
    public List<string> ForbiddenBaseFunctions { get; } = [];
}

internal sealed class BaseFacilityBuilder(string id, int listOrder)
{
    public string Id { get; } = id;
    public string UfopediaType { get; set; } = string.Empty;
    public List<string> Requires { get; } = [];
    public List<string> RequiredBaseFunctions { get; } = [];
    public List<string> ProvidedBaseFunctions { get; } = [];
    public List<string> ForbiddenBaseFunctions { get; } = [];
    public RuleIndexReference SpriteShape { get; set; } = new(-1, string.Empty);
    public RuleIndexReference SpriteFacility { get; set; } = new(-1, string.Empty);
    public bool ConnectorsDisabled { get; set; }
    public int FakeUnderwater { get; set; } = -1;
    public int MissileAttraction { get; set; } = 100;
    public bool Lift { get; set; }
    public bool HyperWave { get; set; }
    public bool MindShield { get; set; }
    public bool GravShield { get; set; }
    public int MindPower { get; set; } = 1;
    public int SizeX { get; set; } = 1;
    public int SizeY { get; set; } = 1;
    public int BuildCost { get; set; }
    public int RefundValue { get; set; }
    public int BuildTime { get; set; }
    public int MonthlyCost { get; set; }
    public int Storage { get; set; }
    public int Personnel { get; set; }
    public int Aliens { get; set; }
    public int Crafts { get; set; }
    public int Laboratories { get; set; }
    public int Workshops { get; set; }
    public int PsiLaboratories { get; set; }
    public bool SpriteEnabled { get; set; }
    public int SightRange { get; set; }
    public int SightChance { get; set; }
    public int RadarRange { get; set; }
    public int RadarChance { get; set; }
    public int Defense { get; set; }
    public int HitRatio { get; set; }
    public RuleIndexReference FireSound { get; set; } = new(0, string.Empty);
    public RuleIndexReference HitSound { get; set; } = new(0, string.Empty);
    public RuleIndexReference PlaceSound { get; set; } = new(-1, string.Empty);
    public int AmmoMaximum { get; set; }
    public int RearmRate { get; set; } = 1;
    public int AmmoNeeded { get; set; } = 1;
    public bool UnifiedDamageFormula { get; set; }
    public int ShieldDamageModifier { get; set; } = 100;
    public string AmmoItem { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public int ListOrder { get; set; } = listOrder;
    public int TrainingRooms { get; set; }
    public int MaximumAllowedPerBase { get; set; }
    public int ManaRecoveryPerDay { get; set; }
    public int HealthRecoveryPerDay { get; set; }
    public float SickBayAbsoluteBonus { get; set; }
    public float SickBayRelativeBonus { get; set; }
    public int PrisonType { get; set; }
    public int HangarType { get; set; } = -1;
    public int RightClickActionType { get; set; }
    public SortedDictionary<string, FacilityItemCost> BuildCostItems { get; } = new(StringComparer.Ordinal);
    public List<string> LeavesBehindOnSell { get; set; } = [];
    public int RemovalTime { get; set; }
    public bool CanBeBuiltOver { get; set; }
    public bool UpgradeOnly { get; set; }
    public List<string> BuildOverFacilities { get; } = [];
    public List<FacilityPosition> StorageTiles { get; set; } = [];
    public List<FacilityPosition> CraftSlots { get; set; } = [];
    public string DestroyedFacility { get; set; } = string.Empty;
}
