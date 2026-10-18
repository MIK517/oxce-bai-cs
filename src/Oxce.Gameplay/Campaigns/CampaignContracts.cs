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
    bool PsiStrengthEval = false, bool GeoscapeEventsInstantDelivery = true, bool Autosave = true)
{
    /// <summary>
    /// Mod::loadAll's fixedUserOptions pass: a loaded mod forces these user options for every
    /// campaign. OptionInfo::load uses std::boolalpha: skip leading classic-locale whitespace,
    /// read the case-sensitive true/false prefix, and leave any trailing text unread.
    /// </summary>
    public CampaignOptions WithFixedUserOptions(IReadOnlyDictionary<string, string> fixedOptions)
    {
        ArgumentNullException.ThrowIfNull(fixedOptions);
        if (fixedOptions.Count == 0) return this;
        return this with
        {
            StorageLimitsEnforced = Fixed("storageLimitsEnforced", StorageLimitsEnforced),
            CanSellLiveAliens = Fixed("canSellLiveAliens", CanSellLiveAliens),
            AutoCombatDefaultSoldier = Fixed("autoCombatDefaultSoldier", AutoCombatDefaultSoldier),
            AnytimePsiTraining = Fixed("anytimePsiTraining", AnytimePsiTraining),
            AllowPsiStrengthImprovement = Fixed("allowPsiStrengthImprovement", AllowPsiStrengthImprovement),
            AllowBuildingQueue = Fixed("allowBuildingQueue", AllowBuildingQueue),
            UfoLandingAlert = Fixed("oxceUfoLandingAlert", UfoLandingAlert),
            AggressiveRetaliation = Fixed("aggressiveRetaliation", AggressiveRetaliation),
            CraftLaunchAlways = Fixed("craftLaunchAlways", CraftLaunchAlways),
            SuppressLandingWithoutEquipment = Fixed("oxceGeoSuppressLandingWithoutEquipment", SuppressLandingWithoutEquipment),
            SuppressRedundantHunterAlert = Fixed("oxceGeoSuppressRedundantHKAlert", SuppressRedundantHunterAlert),
            RetainCorpses = Fixed("retainCorpses", RetainCorpses),
            DogfightAI = Fixed("dogfightAI", DogfightAI),
            RememberDisabledCraftWeapons = Fixed("oxceRememberDisabledCraftWeapons", RememberDisabledCraftWeapons),
            GlobeSeasons = Fixed("globeSeasons", GlobeSeasons),
            PsiStrengthEval = Fixed("psiStrengthEval", PsiStrengthEval),
            GeoscapeEventsInstantDelivery = Fixed("oxceGeoscapeEventsInstantDelivery", GeoscapeEventsInstantDelivery),
            Autosave = Fixed("autosave", Autosave),
            WoundedDefendBaseIf = fixedOptions.TryGetValue("oxceWoundedDefendBaseIf", out var wounded)
                ? ReadFixedInteger(wounded) : WoundedDefendBaseIf,
        };

        bool Fixed(string id, bool current) =>
            fixedOptions.TryGetValue(id, out var value) ? ReadFixedBoolean(value) : current;
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
    public bool DebugMode { get; init; }
    /// <summary>SavedGame::_warned: the monthly report has warned about funds at or below defeatFunds.</summary>
    public bool Warned { get; init; }
    /// <summary>SavedGame::_missionStatistics, in save order.</summary>
    public IReadOnlyList<MissionStatisticSnapshot> MissionStatistics { get; init; } = [];
    public CampaignOptions Options { get; init; } = new();
    public WorldSnapshot World { get; init; } = WorldSnapshot.Empty;

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
