using System.Collections.ObjectModel;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Campaigns;

public enum CampaignDifficulty
{
    Beginner,
    Experienced,
    Veteran,
    Genius,
    Superhuman,
}

public sealed record CampaignOptions(bool StorageLimitsEnforced = false, bool CanSellLiveAliens = false,
    bool AutoCombatDefaultSoldier = true, bool AnytimePsiTraining = false, bool AllowPsiStrengthImprovement = false,
    int MaximumBases = 8, bool AllowBuildingQueue = false, bool UfoLandingAlert = false,
    bool AggressiveRetaliation = true, bool CraftLaunchAlways = false, bool SuppressLandingWithoutEquipment = false,
    bool SuppressRedundantHunterAlert = true, bool RetainCorpses = false, bool DogfightAI = true,
    bool RememberDisabledCraftWeapons = false, bool GlobeSeasons = false, int WoundedDefendBaseIf = 100,
    bool PsiStrengthEval = false, bool GeoscapeEventsInstantDelivery = true, bool Autosave = true,
    bool DisableAutoEquip = false, bool AlternateCraftEquipmentManagement = false, int PreprimeGrenades = 0,
    bool IncludePrimeStateInSavedLayout = false)
{
    /// <summary>Every option with its external key, in record order (the order saves write them).</summary>
    public static IReadOnlyList<CampaignOptionField> Fields { get; } =
    [
        CampaignOptionField.Flag("storageLimitsEnforced", o => o.StorageLimitsEnforced, (o, v) => o with { StorageLimitsEnforced = v }),
        CampaignOptionField.Flag("canSellLiveAliens", o => o.CanSellLiveAliens, (o, v) => o with { CanSellLiveAliens = v }),
        CampaignOptionField.Flag("autoCombatDefaultSoldier", o => o.AutoCombatDefaultSoldier, (o, v) => o with { AutoCombatDefaultSoldier = v }),
        CampaignOptionField.Flag("anytimePsiTraining", o => o.AnytimePsiTraining, (o, v) => o with { AnytimePsiTraining = v }),
        CampaignOptionField.Flag("allowPsiStrengthImprovement", o => o.AllowPsiStrengthImprovement, (o, v) => o with { AllowPsiStrengthImprovement = v }),
        // A port setting, not a reference user option: mods cannot fix it and reference saves do not record it.
        CampaignOptionField.Number("maximumBases", false, o => o.MaximumBases, (o, v) => o with { MaximumBases = v }),
        CampaignOptionField.Flag("allowBuildingQueue", o => o.AllowBuildingQueue, (o, v) => o with { AllowBuildingQueue = v }),
        CampaignOptionField.Flag("oxceUfoLandingAlert", o => o.UfoLandingAlert, (o, v) => o with { UfoLandingAlert = v }),
        CampaignOptionField.Flag("aggressiveRetaliation", o => o.AggressiveRetaliation, (o, v) => o with { AggressiveRetaliation = v }),
        CampaignOptionField.Flag("craftLaunchAlways", o => o.CraftLaunchAlways, (o, v) => o with { CraftLaunchAlways = v }),
        CampaignOptionField.Flag("oxceGeoSuppressLandingWithoutEquipment", o => o.SuppressLandingWithoutEquipment,
            (o, v) => o with { SuppressLandingWithoutEquipment = v }),
        CampaignOptionField.Flag("oxceGeoSuppressRedundantHKAlert", o => o.SuppressRedundantHunterAlert,
            (o, v) => o with { SuppressRedundantHunterAlert = v }),
        CampaignOptionField.Flag("retainCorpses", o => o.RetainCorpses, (o, v) => o with { RetainCorpses = v }),
        CampaignOptionField.Flag("dogfightAI", o => o.DogfightAI, (o, v) => o with { DogfightAI = v }),
        CampaignOptionField.Flag("oxceRememberDisabledCraftWeapons", o => o.RememberDisabledCraftWeapons,
            (o, v) => o with { RememberDisabledCraftWeapons = v }),
        CampaignOptionField.Flag("globeSeasons", o => o.GlobeSeasons, (o, v) => o with { GlobeSeasons = v }),
        CampaignOptionField.Number("oxceWoundedDefendBaseIf", true, o => o.WoundedDefendBaseIf, (o, v) => o with { WoundedDefendBaseIf = v }),
        CampaignOptionField.Flag("psiStrengthEval", o => o.PsiStrengthEval, (o, v) => o with { PsiStrengthEval = v }),
        CampaignOptionField.Flag("oxceGeoscapeEventsInstantDelivery", o => o.GeoscapeEventsInstantDelivery,
            (o, v) => o with { GeoscapeEventsInstantDelivery = v }),
        CampaignOptionField.Flag("autosave", o => o.Autosave, (o, v) => o with { Autosave = v }),
        // Battle equipment (BattlescapeGenerator::deployXCOM, autoEquip, placeItemByLayout).
        CampaignOptionField.Flag("disableAutoEquip", o => o.DisableAutoEquip, (o, v) => o with { DisableAutoEquip = v }),
        CampaignOptionField.Flag("oxceAlternateCraftEquipmentManagement", o => o.AlternateCraftEquipmentManagement,
            (o, v) => o with { AlternateCraftEquipmentManagement = v }),
        CampaignOptionField.Number("preprimeGrenades", true, o => o.PreprimeGrenades, (o, v) => o with { PreprimeGrenades = v }),
        CampaignOptionField.Flag("includePrimeStateInSavedLayout", o => o.IncludePrimeStateInSavedLayout,
            (o, v) => o with { IncludePrimeStateInSavedLayout = v }),
    ];

    /// <summary>
    /// Mod::loadAll's fixedUserOptions pass: a loaded mod forces these user options for every
    /// campaign. OptionInfo::load uses std::boolalpha: skip leading classic-locale whitespace,
    /// read the case-sensitive true/false prefix, and leave any trailing text unread.
    /// </summary>
    public CampaignOptions WithFixedUserOptions(IReadOnlyDictionary<string, string> fixedOptions)
    {
        ArgumentNullException.ThrowIfNull(fixedOptions);
        if (fixedOptions.Count == 0) return this;
        var options = this;
        foreach (var field in Fields)
        {
            if (!field.IsUserOption || !fixedOptions.TryGetValue(field.Key, out var value)) continue;
            options = field.With(options, field.IsInteger ? ReadFixedInteger(value) : ReadFixedBoolean(value) ? 1 : 0);
        }
        return options;
    }

    // OptionInfo::load's `ss >> std::dec >> i`: leading whitespace, an optional sign and decimal digits; a failed
    // extraction stores 0 and an out-of-range value the nearest limit.
    private static int ReadFixedInteger(string value)
    {
        var text = value.AsSpan();
        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]) && text[index] <= ' ') index++;
        var negative = index < text.Length && text[index] == '-';
        if (index < text.Length && text[index] is '-' or '+') index++;
        long magnitude = 0;
        var digits = 0;
        for (; index < text.Length && char.IsAsciiDigit(text[index]); index++, digits++)
            magnitude = Math.Min(magnitude * 10 + (text[index] - '0'), (long)int.MaxValue + 1);
        if (digits == 0) return 0;
        var signed = negative ? -magnitude : magnitude;
        return (int)Math.Clamp(signed, int.MinValue, int.MaxValue);
    }

    private static bool ReadFixedBoolean(string value)
    {
        var text = value.AsSpan();
        var start = 0;
        while (start < text.Length && text[start] is ' ' or '\t' or '\n' or '\r' or '\f' or '\v') start++;
        // Failed non-empty boolalpha extraction yields false. For empty/whitespace-only
        // input, where the reference leaves an uninitialized bool, choose false deliberately.
        return text[start..].StartsWith("true", StringComparison.Ordinal);
    }
}

/// <summary>
/// One <see cref="CampaignOptions"/> field: its external key, whether it is a number or a flag, and whether it is a
/// reference user option (fixed by mods' fixedUserOptions and recorded in a reference save's options snapshot).
/// </summary>
public sealed class CampaignOptionField
{
    private readonly Func<CampaignOptions, int> _get;
    private readonly Func<CampaignOptions, int, CampaignOptions> _with;

    private CampaignOptionField(string key, bool isInteger, bool isUserOption, Func<CampaignOptions, int> get,
        Func<CampaignOptions, int, CampaignOptions> with)
    {
        Key = key;
        IsInteger = isInteger;
        IsUserOption = isUserOption;
        _get = get;
        _with = with;
    }

    public string Key { get; }
    public bool IsInteger { get; }
    public bool IsUserOption { get; }

    /// <summary>The value; a flag reads as 1 or 0.</summary>
    public int Get(CampaignOptions options) => _get(options);

    /// <summary>The options with this field set; a flag is set by any non-zero value.</summary>
    public CampaignOptions With(CampaignOptions options, int value) => _with(options, value);

    internal static CampaignOptionField Flag(string key, Func<CampaignOptions, bool> get,
        Func<CampaignOptions, bool, CampaignOptions> with) =>
        new(key, false, true, options => get(options) ? 1 : 0, (options, value) => with(options, value != 0));

    internal static CampaignOptionField Number(string key, bool isUserOption, Func<CampaignOptions, int> get,
        Func<CampaignOptions, int, CampaignOptions> with) => new(key, true, isUserOption, get, with);
}

public readonly record struct CampaignId
{
    public CampaignId(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("Campaign ID cannot be empty.", nameof(value));
        Value = value;
    }

    public Guid Value { get; }

    public override string ToString() => Value.ToString("D");
}

public sealed record CampaignIdentity(
    CampaignId Id,
    string Name,
    string MasterId,
    IReadOnlyList<string> ActiveMods,
    DateTimeOffset CreatedAtUtc,
    bool Ironman);

public sealed record CountrySnapshot(
    string RuleId,
    IReadOnlyList<int> Funding,
    IReadOnlyList<int> ActivityXcom,
    IReadOnlyList<int> ActivityAlien,
    bool Pact,
    bool NewPact,
    bool CancelPact,
    IReadOnlyList<ScriptValueEntry> ScriptValues);

public sealed record RegionSnapshot(
    string RuleId,
    IReadOnlyList<int> ActivityXcom,
    IReadOnlyList<int> ActivityAlien);

public sealed record FacilitySnapshot(
    string RuleId,
    int X,
    int Y,
    int BuildTime,
    int Ammo,
    bool AmmoMissingReported,
    bool Disabled,
    bool HadPreviousFacility)
{
    public string PreservationKey { get; init; } = string.Empty;
}

public sealed record CraftSnapshot(string RuleId, int Id)
{
    public IReadOnlyList<ScriptValueEntry> ScriptValues { get; init; } = [];
    public string PreservationKey { get; init; } = string.Empty;
    public CraftLogisticsState? Logistics { get; init; }
}

public sealed record SoldierSnapshot(string RuleId, int Id)
{
    public string PreservationKey { get; init; } = string.Empty;
    public SoldierPersonalState? Personal { get; init; }
}

/// <summary>
/// A soldier in the memorial (SavedGame deadSoldiers): the soldier as it was at death and the death time.
/// The cause of a battle death stays in the save node until Phase 7 owns it.
/// </summary>
public sealed record DeadSoldierSnapshot(SoldierSnapshot Soldier, CampaignTime? DiedAt);

public enum CampaignTransferKind { Item, Scientist, Engineer, Soldier, Craft }

public sealed record TransferSnapshot(
    int Id, int Hours, CampaignTransferKind Kind, string RuleId, int Quantity,
    SoldierSnapshot? Soldier = null, CraftSnapshot? Craft = null, bool Delivered = false)
{
    public string PreservationKey { get; init; } = string.Empty;
}

public sealed record CampaignRestriction(string Feature, int? BaseId, bool BlocksLogistics, bool BlocksTime);

public sealed record ResearchProjectSnapshot(string RuleId, int Assigned, int Spent, int Cost);

public sealed record ProductionSnapshot(
    string RuleId, int Assigned, int Spent, int Amount, bool Infinite, bool Sell, bool IsFallback,
    IReadOnlyDictionary<string, int> RandomProductionInfo);

public sealed record BaseSnapshot(
    int Id,
    string Name,
    double Longitude,
    double Latitude,
    IReadOnlyList<FacilitySnapshot> Facilities,
    IReadOnlyList<CraftSnapshot> Crafts,
    IReadOnlyList<SoldierSnapshot> Soldiers,
    IReadOnlyDictionary<string, int> Items,
    int Scientists,
    int Engineers)
{
    public bool FakeUnderwater { get; init; }
    public bool RetaliationTarget { get; init; }
    public int? RetaliationMissionId { get; init; }
    public IReadOnlyList<TransferSnapshot> Transfers { get; init; } = [];
    public IReadOnlyList<ResearchProjectSnapshot> Research { get; init; } = [];
    public IReadOnlyList<ProductionSnapshot> Productions { get; init; } = [];
}

public sealed record CampaignSnapshot(
    CampaignIdentity Identity,
    CampaignDifficulty Difficulty,
    CampaignTime Time,
    int Ending,
    int MonthsPassed,
    int DaysPassed,
    ulong RandomState,
    IReadOnlyList<long> Funds,
    IReadOnlyList<long> Maintenance,
    IReadOnlyList<long> Incomes,
    IReadOnlyList<long> Expenditures,
    IReadOnlyList<int> ResearchScores,
    IReadOnlyDictionary<string, int> NextIds,
    IReadOnlyList<CountrySnapshot> Countries,
    IReadOnlyList<RegionSnapshot> Regions,
    IReadOnlyList<BaseSnapshot> Bases,
    IReadOnlyList<ScriptValueEntry> ScriptValues)
{
    public IReadOnlyList<CampaignRestriction> Restrictions { get; init; } = [];
    /// <summary>The memorial, in death order.</summary>
    public IReadOnlyList<DeadSoldierSnapshot> DeadSoldiers { get; init; } = [];
    public IReadOnlyList<string> CompletedResearch { get; init; } = [];
    public IReadOnlyList<ResearchDiaryEntrySnapshot> ResearchDiary { get; init; } = [];
    public ulong ResearchDiarySequence { get; init; }
    public IReadOnlyDictionary<string, int> ResearchRuleStatus { get; init; } = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
    public IReadOnlyDictionary<string, int> ManufactureRuleStatus { get; init; } = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
    public IReadOnlyDictionary<string, int> MonthlyPurchaseLog { get; init; } = new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());
    /// <summary>SavedGame::_customRuleCraftDeployments: craft type to its [x, y, z, dir] deployment points.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<int>>> CustomRuleCraftDeployments { get; init; } = EmptyCraftDeployments;
    public bool DebugMode { get; init; }
    /// <summary>SavedGame::_warned: the monthly report has warned about funds at or below defeatFunds.</summary>
    public bool Warned { get; init; }
    /// <summary>SavedGame::_missionStatistics, in save order.</summary>
    public IReadOnlyList<MissionStatisticSnapshot> MissionStatistics { get; init; } = [];
    public CampaignOptions Options { get; init; } = new();
    public WorldSnapshot World { get; init; } = WorldSnapshot.Empty;

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<int>>> EmptyCraftDeployments =
        new ReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<int>>>(new Dictionary<string, IReadOnlyList<IReadOnlyList<int>>>());

    internal static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());

    internal static IReadOnlyDictionary<string, int> ReadOnlyIds(IEnumerable<KeyValuePair<string, int>> values) =>
        new ReadOnlyDictionary<string, int>(values.ToDictionary(static pair => pair.Key, static pair => pair.Value,
            StringComparer.Ordinal));
}

public interface ICampaignClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemCampaignClock : ICampaignClock
{
    public static SystemCampaignClock Instance { get; } = new();

    private SystemCampaignClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
