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

**The unit keys written twice (S3).** `BattleUnit::save` writes `turnsSinceSpotted` and `turnsLeftSpottedForSnipers`
first as the `Uint8` arrays themselves (their address, in hex: `0x5803c54`) and then as the hostile value, and
`turnsSinceStunned` twice. The load reads the first of repeated keys (`useIndex`) with `tryReadAs<int>` into a `Uint8`,
so the hostile values become the address's low byte. The port reads the same way and writes each key once, at its
first place, with the value read (the address's low byte for a reference save, the hostile value for the port's own),
which the reference reads back unchanged. A rewrite of a reference save is compared with its source in that form.

**The diagonal armor sides and a soldier's aggression (S3).** `_currentArmor` has `SIDE_MAX` (9) sides; the
constructors set the first five and leave the four diagonal ones uninitialized (the corpus holds garbage there), and
`adjustStats` multiplies all nine for a hostile unit. Nothing reads the diagonal sides. The port keeps them as saved
and gives a unit it creates 0 (`adjustStats` applies to the five sides only). The soldier constructor also leaves
`_aggression` uninitialized (saved as garbage); the port's soldier has 0. `isBrutal`, `isNotBrutal` and
`isCheatOnMovement` are uninitialized without their keys; the port reads them as false, and, as in the reference, they
are never written. A kill in `tempUnitStatistics` is read by `BattleUnitKills(reader)`, which leaves `status`, `faction`,
`mission`, `turn`, `side`, `bodypart` and `id` uninitialized without their keys; the port gives them the
`BattleUnitKills()` constructor's values.

**The constructors' seedless draws on load (S3).** A unit made from a unit rule draws its recolor (and a civilian its
unified rank) from `RNG::seedless`, which the load overrides from the save's `recolor` and `rankIntUnified`. The port's
generation draws them from the battle's seedless stream (after the ambience delay); a restored battle's stream is
seeded from a constant, so a save without those keys restores the same values every time.

**Units the restored battle cannot hold (S3).** The reference dereferences a null soldier for a soldier ID the campaign
lacks, and `setTile` lets a second unit overwrite a tile's unit and puts a large unit partly outside the map. The port
fails the load as a corrupted save for a missing soldier or one in unknown armor, two units on one tile, a repeated
unit ID, a footprint outside the map, and a faction or original faction outside 0-2 (the reference indexes its
per-faction arrays with it). A standing unit outside the map stands on no tile, as `getTile` returns null. The restored
battle skips the generator's placement check: units stand where the save puts them.

**Flags a reload loses (S3).** `save` writes `vip` and `pickUpWeaponsMoreActively` only when set, `dontReselect` only for
a player's unit, and the load-only flags above never, so a reload gives a false `vip` or pick-up the unit rule's value
and loses the others. The port writes as the reference does, so its reloads lose the same values.

**Units and their parts in block style (S3).** `armor`, `fatalWounds`, `position`, `recolor`'s pairs, `moveCost`, the AI
and the kills are flow style in the reference; the port's writer writes block style, which reads the same. A key a
loaded unit lacks (`visible` after the load sets it) is added at the end of that unit's node.

**Items the restored battle cannot hold (S4).** The load keeps the last of repeated item IDs in its index (both items
stay in their lists, and the second pass links the later one twice), dereferences `getTile`'s null for a ground item
outside the map, and overflows the item counter after an ID of `INT_MAX`. The port fails the load as a corrupted save
for a repeated ID (within a list or across `items`, `itemsSpecial` and the recovery lists), a ground position outside
the map and an ID of `INT_MAX`, and the restored battle's `Validate` fails one ammo item loaded into two weapons and a
weapon of the battle loaded with an item of a recovery list, and an owned item that also lies on a tile (an owner, a
ground section and a position: the reference puts it both in the owner's inventory and on the tile). A restored item may be held by nothing (the items of a unit
the load left out, a ground item saved without a position, unloaded ammo nowhere), as in the reference.

**The items' skip warnings and inventory order (S4).** The reference logs an unknown item type and drops it, sends an
item in an unknown inventory section to the ground silently, and logs a fifth special built-in item of one unit
(`addLoadedSpecialWeapon`), which stays owned in no slot. The port drops and moves the same items, with an
`OXCE-BATTLE-SAVE-SKIPPED` warning for the first two; the restore reports nothing for the fifth special item. A loaded
item joins its owner's inventory in the load's list order in the reference (`items`, `recoverConditional`,
`recoverGuaranteed`, `itemsSpecial`); the port's inventory is the battle's items the unit owns in a section, in ID
order, which is the same for every save the reference writes (its lists are in ID order) but differs for a hand-made
unsorted list, and an owned item of a recovery list keeps its owner without joining the unit's inventory.

**Items in block style (S4).** `position` and `ammoItemSlots` are flow style in the reference; the port writes block
style, which reads the same.

## Consequences

- A generated battle's root is written as the reference's generator leaves it; only the ambience delay's value differs,
  as any seedless draw does between two runs of the reference.
- A generated battle's map is written as the reference writes it, except that nodes are written in block style (the
  reference's flow style reads the same).
- A generated battle's units are written as the reference's constructors leave them (with the starting condition's
  `bannedInNextStage`), except for the diagonal armor sides, a soldier's aggression and the seedless draws' values.
- A generated battle's items are written as the reference's constructor and generator leave them (`setFuseTimer` for
  the map's and layouts' fuses, `moveToOwner`'s previous owner), including `fuseEnabed` (sic) as the reference spells it.
- The campaign's battle (S5) adds no difference in behaviour: `battleGame`, the header's battle lines, the
  `inBattlescape` flags (with `setSpeed(0)`) and the soldiers' battle armor follow `SavedGame`, the generator's setters
  and `deployXCOM`. Only the key order differs on a loaded rewrite whose source lacks them: the header's `mission`,
  `target`, `craftOrBase` and `turn` and the body's `battleGame` are appended after the source's keys, where the
  reference writes them after `time` and after `options`. Readers look keys up by name, so the order is not read.
