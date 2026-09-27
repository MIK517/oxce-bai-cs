namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>A trigger retains missing IDs: negative tests of absent rules can be meaningful.</summary>
public sealed record RuntimeScriptTrigger<TFamily>(RuntimeRuleReference<TFamily> Rule, bool Expected);

/// <summary>Ordinal WeightedOptions entry, with a generation-scoped reference when available.</summary>
public sealed record RuntimeScriptWeight<TFamily>(RuntimeRuleReference<TFamily> Rule, ulong Weight);

/// <summary>Timeline entries retain load order, including appended and duplicate months.</summary>
public sealed record RuntimeScriptTimeline<TFamily>(ulong FirstMonth, IReadOnlyList<RuntimeScriptWeight<TFamily>> Choices);

public sealed record RuntimeScriptCriteria(
    int FirstMonth, int LastMonth, int ExecutionOdds, int MinimumDifficulty, int MaximumDifficulty,
    int MinimumScore, int MaximumScore, long MinimumFunds, long MaximumFunds,
    string MissionVariable, string MissionMarker, int CounterMinimum, int CounterMaximum,
    IReadOnlyList<RuntimeScriptTrigger<ResearchRuleFamily>> Research,
    IReadOnlyList<RuntimeScriptTrigger<ItemRuleFamily>> Items,
    IReadOnlyList<RuntimeScriptTrigger<FacilityRuleFamily>> Facilities,
    IReadOnlyList<RuntimeScriptTrigger<SoldierRuleFamily>> SoldierTypes,
    IReadOnlyList<RuntimeScriptTrigger<RegionRuleFamily>> BaseRegions,
    IReadOnlyList<RuntimeScriptTrigger<CountryRuleFamily>> BaseCountries,
    IReadOnlyList<RuntimeScriptTrigger<CountryRuleFamily>> PactCountries);

/// <summary>RuleMissionScript, shared by ordinary and tagged adhoc scheduling.</summary>
public sealed record RuntimeMissionScriptRule(
    RuntimeScriptCriteria Criteria, string VariableName, int Label, int TargetBaseOdds,
    int MaximumRuns, int AvoidRepeats, int StartDelay, int RandomDelay, bool UseTable, bool SiteType,
    IReadOnlyList<int> Conditionals, IReadOnlyList<string> AdhocTags,
    IReadOnlyList<RuntimeScriptTimeline<AlienMissionRuleFamily>> Missions,
    IReadOnlyList<RuntimeScriptTimeline<AlienRaceRuleFamily>> Races,
    IReadOnlyList<RuntimeScriptTimeline<RegionRuleFamily>> Regions);

public sealed record RuntimeArcScriptRule(
    RuntimeScriptCriteria Criteria, int MaximumArcs,
    RuntimeRuleReferenceList<ResearchRuleFamily> Sequential,
    IReadOnlyList<RuntimeScriptWeight<ResearchRuleFamily>> Random);

public sealed record RuntimeEventScriptRule(
    RuntimeScriptCriteria Criteria, bool AffectsGameProgression,
    RuntimeRuleReferenceList<EventRuleFamily> Sequential,
    IReadOnlyList<RuntimeScriptWeight<EventRuleFamily>> Random,
    IReadOnlyList<RuntimeScriptTimeline<EventRuleFamily>> Events);
