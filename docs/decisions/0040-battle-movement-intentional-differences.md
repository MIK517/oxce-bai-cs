# 0040: Battle movement intentional differences

Status: accepted for Phase 7 branch 4 (2026-11-01); later slices add entries.

## Problem

Battle movement (the [Phase 7 plan](../phase-7-plan.md#branch-4--battle-movement)) reaches reference paths whose
behaviour is undefined or an implementation accident. The port must be deterministic there and record what it does
([AGENTS.md](../../AGENTS.md)). Evidence is in [battle movement status](../battle-movement-status.md). Each entry names its
slice.

## Decision

**The order of the reachable tiles (S2).** `Pathfinding::findReachablePathFindingNodes` sorts its nodes with
`MinNodeCosts`, which says a node comes first only when it is cheaper in both time units and energy. Two nodes that are
cheaper in one and dearer in the other are unordered both ways, which is not a strict weak ordering: `std::sort` is
undefined for it and its result depends on the standard library. The port orders by time units, then energy, and keeps
the order in which the search visited equal costs. The set of tiles is the reference's. The port's start is always first; the reference's need not be, because `MinNodeCosts` treats a cost of zero time and energy as equal to any node with zero energy (a grav lift step, a fall), which `std::sort` may put before it.

The open set of the A* search is not such a case: `PathfindingOpenSet` is a `std::priority_queue`, and the port's heap
follows the standard library's sift order (the same in libstdc++ and the MSVC library), so equal costs leave in the
reference's order and the chosen path is the reference's.

**The line of fire templates (S4).** Sight reads `Mod::getVoxelData` (`GEODATA/LOFTEMPS.DAT`) with `vector::at`, which throws, and
so ends the program, for a template past the file's end or a missing file. The port needs the file to generate or load a battle and
refuses (`battle.generation-failed`, or a corrupted save) when it is missing, unreadable, or a map object or an armor in the battle
names a template it lacks; a unit that arrives later (a spawn) with such an armor stops the first turn
(`battle.line-of-fire-data-invalid`). The reference's `voxelCheck` skips a tile that `Tile::isVoid` calls empty; the port tests the
parts and units only, because a tile with only smoke or items falls through every check to empty as well.

**The event sector's arithmetic (S4).** `setupEventVisibilitySector` computes its tangent points with `asinf`, `atan2f`, `sinf`,
`cosf` and `roundf`; the port uses the single-precision functions of .NET (`MathF`). A tangent point on a rounding boundary can fall
a tile either way between C libraries, as it can between two builds of the reference, so a partial update around an event
(S6 onwards) may include or leave out one edge column. A full recheck is not affected, and neither is a unit's own sight.

**Doors (S6).** `Tile::openDoor` reads a swinging door's alternative with `MapDataSet::getObject(getAltMCD())`, which throws a fatal exception when the
index lies past the set's end (load-time validation only logs it). The port treats such a door as not openable (result -1) and the search goes on to the
next check position, which the reference never reaches. The reference opens a UFO door over animation frames (`Tile::animate`, frame 7 open) and a walk waits
for it (result 3, "still opening"); the port sets the door open at once. Light and sight already count the door open from frame 1 in the reference
(`isUfoDoorOpen` is `currentFrame != 0`), so they are the same; only a walk's waiting differs, and result 3 never arises. A door opened by a right click whose
time units cannot be paid after it opened (result 4 after the open, result 5) stays open, as in the reference.

**Falling (S7).** `UnitFallBState` runs over many animation cycles: each unit's step takes 8 phases (16 for a diagonal escape, more for big
units), the unit's tile changes at the middle phase, and a landed unit's light, sight and `checkReactionFire` run on the cycle after its
last step, interleaved with units still in mid-step. The port runs rounds: every unit that stands still either takes its whole next step or
has landed, all steps of a round complete together, and landings run at the start of the next round. In the reference a unit that starts falling is
advanced in the same cycle (`++unit`), so its tile change can come a cycle before an escaping unit leaves its tile; the final state is the same, because `setTile`
clears a tile only while it still names that unit. The sequence of units' tile changes is the reference's whenever the units fall independently; the order of one unit's landing light and
sight update against another unit's mid-step differs. The reference keeps `tilesToFallInto` for the whole fall, never clearing it between
cycles, and the port does too; the tiles it reserves for a big unit's escape (`escapeTiles`) are taken from the *last* section's tile
(a tile offset by one from the unit's real footprint), as the reference does. The falling list is runtime state a save does not keep, so
`StartFirstTurn` queues the units that have no floor again (the reference's `BattlescapeGame` constructor queues them through `checkForCasualties`, and the spawns queue theirs at the briefing). A fall that would knock out a unit
below (no escape) is refused before it starts (`battle.fall-knockout-unsupported`, branch 6's casualty work), as are a landing that a reaction
fire, a proximity explosion or a floor-burning unit (S9) could follow.
