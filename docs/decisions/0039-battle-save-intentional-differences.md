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

**The old text tiles' `discovered` (S2).** `Tile::load` maps the three values of a text tile's `discovered` with
`i == 2 ? 0 : i - 1`: the first goes to `_objectsCache[-1]` (undefined), the second and the third both to the floor.
The port still requires three readable booleans, as `readVal<bool>` does, ignores the first, and gives the floor the
third (the last write). The walls keep what they had; no refusal.

**The binary tile key's other sizes (S2).** `unserializeInt` reads sizes 1 (Uint8), 2 (Sint16) and 4 (Uint32 cast to
int); for any other size the release build logs a warning, returns 0 and skips that many bytes, and the debug build
reaches `CrossPlatform::unreachable`. The port follows the release build and reports one `OXCE-BATTLE-SAVE-SKIPPED`
warning per key. A key missing from the save is 0 (the key is zeroed before it is read), except `tileBoolFieldsSize`,
which defaults to 1; a `lastExploredBy` size of 0 leaves the field unread, as `loadBinary` checks. The writer's
`serializeInt` truncates a value wider than its field (an `assert` in debug builds); the port truncates too.

**Tile reads the reference makes outside its memory (S2).** The load reads `totalTiles` records of
`tileTotalBytesPer` bytes from the decoded `binTiles` without checking its length, indexes `_tiles` with an index it
only `assert`s, wraps its pointer when a record is shorter than its index, and dereferences the null `getTile` of a
text tile outside the map; a missing `binTiles` is read with `readValBase64` on an invalid node, and a
`mapdatasets` name without its MCD file makes `MapDataSet::loadData` dereference the null stream of
`FileMap::getIStream` (whether or not a tile uses the set). Each of these fails the load naming the key (the port's
save error), as does a `totalTiles` whose byte count overflows. A buffer longer than `totalTiles` records is accepted (the reference stops at
`totalTiles`), and fields that run past a record into the next one read those bytes, as the reference does.

**The module map of a base defense saved without one (S2).** The load's fallback, `calculateModuleMap`, runs before
`loadMapResources` has pointed any tile part at its object, so `getMapData(O_OBJECT)` is null everywhere and every
whole block keeps (-1, -1): no module counts, and no facility can be lost. The port does the same; it is the
reference's behaviour, not an undefined one. A present `moduleMap` that is no grid (a scalar) is an empty map, as
`tryRead` into a vector reads no children, and the fallback does not run.

**Block grids keep their saved shape (S2).** `reinforcementsBlocks`, `flattenedMapTerrainNames` and
`flattenedMapBlockNames` are read as saved, without a check against the map's blocks; a later reader indexing past a
row is the reference's undefined indexing, which the branches that port those readers (the reinforcements) must refuse.
A base defense's `moduleMap` is kept in its saved shape too (`tryRead`, no check); its readers index it per block of a
tile or facility (`TileEngine` when a module is destroyed, `AIModule` choosing targets, `DebriefingState` counting lost
facilities), so branches 5 (destruction), 6 (the AI) and 7 (the debriefing) must refuse a block the saved grid lacks.

**A restored map is validated (S2).** The reference loads any node list. The port runs `BattleState.Validate` on a
restored battle: a node outside the map, a node whose ID is not its index (dummies aside), or a link to no live node
is a corrupted save. Saves the reference writes from its generator keep these, as the corpus does.

**Fire and smoke animation offsets (S2).** `Tile::load` and `loadBinary` draw an animation offset from
`RNG::seedless` for a tile with fire or smoke. It is presentation only and not part of the port's battle state.

## Consequences

- A generated battle's root is written as the reference's generator leaves it; only the ambience delay's value differs,
  as any seedless draw does between two runs of the reference.
- A generated battle's map is written as the reference writes it, except that nodes are written in block style (the
  reference's flow style reads the same).
- Later slices (S3 to S5) add their entries here: the duplicate unit keys, the armor sides and `fuseEnabed`.
