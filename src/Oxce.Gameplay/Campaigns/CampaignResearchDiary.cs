namespace Oxce.Gameplay.Campaigns;

/// <summary>Native ResearchDiary.h source codes; saves also retain unrecognized byte values.</summary>
public enum ResearchDiscoverySource : byte
{
    Base = 0,
    FreeFrom = 1,
    FreeAfter = 2,
    Mission = 3,
    Event = 4,
}

/// <summary>One ordered discovery occurrence, including repeatable research.</summary>
public sealed record ResearchDiaryEntrySnapshot(string RuleId, uint Year, uint Month, uint Day,
    byte SourceType, string SourceName)
{
    public string PreservationKey { get; init; } = string.Empty;
}

public sealed partial class CampaignState
{
    public const int MaximumResearchDiaryEntries = 100_000;
    private const string NewResearchDiaryKeyPrefix = "research-diary:new:";
    private List<ResearchDiaryEntrySnapshot> _researchDiary = [];
    private ulong _researchDiarySequence;

    private void RestoreResearchDiary(CampaignSnapshot snapshot)
    {
        if (snapshot.ResearchDiary.Count > MaximumResearchDiaryEntries)
            throw new InvalidDataException("Research diary exceeds the supported population limit.");
        _researchDiarySequence = snapshot.ResearchDiarySequence;
        _researchDiary = [];
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in snapshot.ResearchDiary)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.RuleId) ||
                !_content.RuntimeRules.Research.TryGet(entry.RuleId, out _) || entry.SourceName is null || entry.PreservationKey is null)
                throw new InvalidDataException("Research diary entry has invalid research or source data.");
            var key = entry.PreservationKey;
            if (key.Length == 0)
            {
                if (_researchDiarySequence == ulong.MaxValue)
                    throw new InvalidDataException("Research diary identity sequence is exhausted.");
                key = NewResearchDiaryKeyPrefix + (++_researchDiarySequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (key.StartsWith(NewResearchDiaryKeyPrefix, StringComparison.Ordinal) &&
                (!ulong.TryParse(key.AsSpan(NewResearchDiaryKeyPrefix.Length), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var sequence) || sequence == 0 || sequence > snapshot.ResearchDiarySequence))
                throw new InvalidDataException("Research diary identity exceeds its saved sequence.");
            if (!keys.Add(key)) throw new InvalidDataException("Duplicate research diary preservation keys.");
            _researchDiary.Add(entry with { PreservationKey = key });
        }
    }
}
