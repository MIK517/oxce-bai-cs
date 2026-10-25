# 0039: Battle save intentional differences

Status: accepted for Phase 7 branch 2 (2026-10-25); later slices add entries.

## Problem

Battle saves (`battleGame`, [ADR 0038](0038-battle-aggregate.md), D6) reach reference paths whose behaviour is undefined,
an implementation accident or a value the port cannot keep. The port must be deterministic there and record what it
does ([AGENTS.md](../../AGENTS.md)). The branch decisions are in the
[Phase 7 plan](../phase-7-plan.md#branch-decisions); evidence is in [battle save status](../battle-save-status.md).
Each entry names its slice.

## Decision

**The ambience delay's seedless stream (S1).** `BattlescapeGenerator::generateMap` draws the first random ambience
delay with `RNG::seedless`, a second stream seeded from the clock that the game's stream never sees. The port draws it
from a `SplitMix64RandomSource` seeded with the campaign stream's state (mixed with a constant) at that point, without
drawing from the campaign stream, so the game's draws match the reference's order and tests stay reproducible. The
value is presentation only (when the next ambient sound plays).

**A reversed or overflowing ambience delay range (S1).** `resetCurrentAmbienceDelay` draws
`RNG::seedless(min * 10, max * 10)`, and `RandomState::generate` takes the raw value modulo `max - min + 1`; a terrain
whose `ambienceRandomDelay` has the larger value first makes that range zero (a division by zero) or negative (wrapped
to a huge unsigned modulus), and a delay whose tenths pass the `int` range overflows (undefined). The port uses the
minimum's tenths, clamped to the `int` range, then the reference's floor of 10.

**The sound index's mod size limit (S1).** `Mod::loadOffsetNode` throws a rule error for a terrain sound index below
-1 or past the declaring mod's reserved size. The port applies the offset rule (an index at or above BATTLE.CAT's
shared sounds is offset into the mod's range) but does not repeat the size check at link time, as it does not for the
other offset sounds and sprites; such an index stays a number no sound set holds.

**The warnings for what the reference drops (S1).** An unknown `startingConditionType` or `enviroEffectsType` is none
(the reference says nothing), and an unknown `baseItems` item or root tag is left out (the reference logs an error).
The port reports each to the load's diagnostics (`OXCE-BATTLE-SAVE-SKIPPED`), an addition. Unlike the strategic tags,
an unknown battle tag does not fail the load: the plan's rule for battles is to skip where the reference skips. Values
the reference cannot read (a present value of the wrong type, for `tryRead` and `readVal(default)` alike, and a
missing `selectedUnit`) fail the load naming the key, as the reference throws.

**The battle's deployment (S1).** The reference saves no deployment; its readers derive one and disagree.
`DebriefingState` takes the alien custom mission's deployment, else the mission type's, else the in-battle UFO's own;
`BattlescapeState` (the next stage) takes the mission type's, else the UFO's with the custom mission standing in for
it; `AbortMissionState` takes the mission type's only. They differ for an alien base whose race has a
`baseCustomMission` naming a deployment (`ConfirmLandingState`), and for a custom mission that names none. The restored
`BattleState.DeploymentId` follows `DebriefingState` (the outcome, and the generator's choice); the branches that port
the next stage and the abort read the mission type as their own reader does.

## Consequences

- A generated battle's root is written as the reference's generator leaves it; only the ambience delay's value differs,
  as any seedless draw does between two runs of the reference.
- Later slices (S2 to S5) add their entries here: the old text tiles' `discovered`, the duplicate unit keys, the
  armor sides and `fuseEnabed`.
