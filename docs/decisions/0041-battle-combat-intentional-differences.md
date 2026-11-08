# 0041: Battle combat intentional differences

Status: accepted for Phase 7 branch 5 (2026-11-09); later slices add entries.

## Problem

Battle combat (the [Phase 7 plan](../phase-7-plan.md#branch-5--battle-combat)) reaches reference paths whose behaviour is
undefined or depends on animation time. The port must be deterministic there and record what it does
([AGENTS.md](../../AGENTS.md)). Evidence is in [battle combat status](../battle-combat-status.md). Each entry names its slice.

## Decision

**A thrower of no strength (S2).** `ProjectileFlyBState::getMaxThrowDistance` computes `50 * weight / strength` in integers. A unit whose
strength is 0 divides by zero there, which ends the program. The port counts a strength of at least 1 for that quotient. The curvature of
`TileEngine::validateThrow` divides in floating point (`strength / weight`), where a weight of 0 gives an infinity and the curve 0.48 (so does a strength and a weight of 0, where `std::max(0.48, NaN)` is 0.48); the port
does the same, guarding `Math.Max` against the NaN.

**A target that lies off the map (S2).** `validThrowRange` dereferences the target tile of a throw (`getTerrainLevel`) after the item's throw-range check;
a target outside the map would crash. The commands refuse a target outside the map before it gets there, and the port's `validateThrow` treats
a missing tile as a throw that cannot land (for a shot as well, which the reference does not look at there).
