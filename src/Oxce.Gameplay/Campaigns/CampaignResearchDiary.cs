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

    private bool ResearchDiaryCapacityFits(long additions) => additions >= 0 &&
        additions <= MaximumResearchDiaryEntries - _researchDiary.Count &&
        (ulong)additions <= ulong.MaxValue - _researchDiarySequence;

    private void AppendResearchDiary(string id, ResearchDiscoverySource source, string sourceName)
    {
        if (!ResearchDiaryCapacityFits(1))
            throw new InvalidOperationException("Research diary capacity was not reserved before the transition.");
        var key = NewResearchDiaryKeyPrefix + (++_researchDiarySequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _researchDiary.Add(new(id, (uint)Time.Year, (uint)Time.Month, (uint)Time.Day, (byte)source, sourceName)
        { PreservationKey = key });
    }

    private void RecordResearchDiary(string id, ResearchDiscoverySource source, string sourceName, bool considerDebug)
    {
        if (id.Length != 0 && _content.RuntimeRules.Research.TryGet(id, out _) &&
            !(considerDebug && _debugMode) && !_completedResearch.Contains(id) &&
            _researchRuleStatus.GetValueOrDefault(id) != 2)
            AppendResearchDiary(id, source, sourceName);
    }

    private string? PreflightDailyResearchDiary(ref long work)
    {
        if (!_bases.Any(owner => owner.Research.Any(project => (long)project.Spent + project.Assigned >= project.Cost))) return null;
        long additions;
        if (ResearchPreflight.TryForecastDailyStocks(ref work, out var forecast, includeDiscoveries: true))
            additions = forecast.DiaryEntries;
        else
        {
            // Random rewards can change later eligibility. Bound each initial/lookup
            // chain separately, including repeated discoveries after disables/re-enables.
            long automatic = 0;
            foreach (var entry in _content.RuntimeRules.Research.Rules)
            {
                if (++work > 1_000_000) return "Research diary forecasts exceed the supported work limit.";
                if (entry.Value.Cost == 0) automatic++;
            }
            additions = 0;
            foreach (var owner in _bases)
                foreach (var project in owner.Research)
                {
                    if (++work > 1_000_000) return "Research diary forecasts exceed the supported work limit.";
                    if ((long)project.Spent + project.Assigned < project.Cost) continue;
                    var rule = _content.RuntimeRules.Research[project.Rule].Value;
                    var calls = 1 + (rule.Lookup.Length == 0 ? 0 : 1);
                    var bonusCalls = 0;
                    foreach (var id in rule.GetOneFree.Concat(rule.GetOneFreeProtected.SelectMany(group => group.Topics)))
                    {
                        if (++work > 1_000_000) return "Research diary forecasts exceed the supported work limit.";
                        if (_content.RuntimeRules.Research.TryGet(id, out var handle))
                            bonusCalls = Math.Max(bonusCalls, 1 + (_content.RuntimeRules.Research[handle].Value.Lookup.Length == 0 ? 0 : 1));
                    }
                    additions += (calls + bonusCalls) * (automatic + (_debugMode ? 0 : 1));
                }
        }
        if (work > 1_000_000) return "Research diary forecasts exceed the supported work limit.";
        return ResearchDiaryCapacityFits(additions) ? null : "Research diary exceeds its supported population or identity range.";
    }

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
