namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>
/// Scheduling inputs from RuleEvent and the payload that GeoscapeEventState::eventLogic applies.
/// Reference: RuleEvent::load, SavedGame::spawnEvent and GeoscapeState::time30Minutes.
/// </summary>
public sealed record RuntimeEventRule(
    int Timer, int TimerRandom, string InterruptResearch, IReadOnlyList<string> AdhocMissionScriptTags)
{
    public RuntimeEventPayload Payload { get; init; } = RuntimeEventPayload.Empty;
}

/// <summary>
/// What a popped event gives or takes, in the reference's order. Maps keep std::map's key order (UTF-8 bytes);
/// lists keep rule order. Names stay names: the payload looks them up when it runs.
/// </summary>
public sealed record RuntimeEventPayload(
    string Description,
    string Cutscene,
    IReadOnlyList<string> Regions,
    bool CitySpecific,
    int Points,
    int Funds,
    string SpawnedCraftType,
    int SpawnedPersons,
    string SpawnedPersonType,
    string SpawnedPersonName,
    RuntimeSoldierTemplate? SpawnedSoldierTemplate,
    IReadOnlyList<KeyValuePair<string, int>> EveryMultiItems,
    IReadOnlyList<string> EveryItems,
    IReadOnlyList<string> RandomItems,
    IReadOnlyList<IReadOnlyList<KeyValuePair<string, int>>> RandomMultiItems,
    IReadOnlyList<KeyValuePair<string, ulong>> WeightedItems,
    IReadOnlyList<string> Research,
    bool Invert,
    IReadOnlyList<KeyValuePair<string, int>> EveryMultiSoldiers,
    IReadOnlyList<IReadOnlyList<KeyValuePair<string, int>>> RandomMultiSoldiers)
{
    public static RuntimeEventPayload Empty { get; } = new("", "", [], false, 0, 0, "", 0, "", "", null,
        [], [], [], [], [], [], false, [], []);
}
