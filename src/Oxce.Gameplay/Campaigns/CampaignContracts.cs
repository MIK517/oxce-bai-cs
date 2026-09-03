using System.Collections.ObjectModel;
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
    bool HadPreviousFacility);

public sealed record CraftSnapshot(string RuleId, int Id);

public sealed record SoldierSnapshot(string RuleId, int Id);

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
    int Engineers);

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
