namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>
/// Scheduling inputs from RuleEvent. Payload effects remain owned by the campaign-event capability.
/// Reference: RuleEvent::load, SavedGame::spawnEvent and GeoscapeState::time30Minutes.
/// </summary>
public sealed record RuntimeEventRule(int Timer, int TimerRandom, string InterruptResearch);
