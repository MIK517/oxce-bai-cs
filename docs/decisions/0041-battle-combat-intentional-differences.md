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

**The generator behind a hit's rolls (S3).** `BattleUnit::damage` takes one draw of the game generator to make a sub-sequence on every hit on a living unit, and draws the leg, the burn time
and the infection and self-destruct percents from it; the side rolls use the game generator. The port draws everything from the battle's generator in the same order, without the extra draw;
exact streams are out of scope.

**Recovery bonuses in the murderer rule (S3).** `hasNegativeHealthRegen` reads the armor's and the soldier bonuses' `recovery: health`, which the port does not read yet (branch 6). It counts as false, so a
mod with a negative health recovery records a different murderer for a harmless hit on a burning or wounded victim. `Options::alienBleeding` is fixed off.

**A unit it cannot turn into (S3).** An infecting hit looks the unit it would spawn up with `Mod::getUnit`, whose result `damage` dereferences, and refuses (logs) one with a bigger armor than the
victim's. The port converts nothing for both a missing unit and a bigger armor.

**A corpse the armor does not list (S4).** `UnitDieBState::convertUnitToCorpse` reads `getCorpseBattlescape()[i]` for each tile part of the body, which is
undefined when the armor lists fewer corpses than its size squared (and a null item is dereferenced when one entry names no item). The port leaves no corpse for a part
the armor lacks (the rule loader requires a corpse per tile part, so this guard is defensive).

**A spawn unit the mod lacks or that does not fit (S4).** `convertUnit` creates the unit from `unit->getSpawnUnit()` without a check, and sets the new unit's tiles
whatever stands there. The port treats a unit whose spawn unit names no unit of the mod as having none (it dies and leaves a corpse), and spawns nothing when the new unit's
footprint would cover a tile off the map or a tile another unit stands on (the old unit is dead and its things lie on the ground).

**The memorial's cause and the dead soldier's armor (S4).** `SavedGame::killSoldier(false, ...)` keeps the soldier's geoscape armor during the battle, and the death's cause is
the murderer's statistics with the victim's side and body part. `DeadSoldierSnapshot.Cause` carries it and the save adapter writes `death.cause` for a death in battle; a loaded death keeps its node.
The `KIA` flag of the victim's statistics is not saved by the reference and not kept here.

**A unit that is out stands on no tile (S4).** The reference keeps a dead or unconscious unit's `_pos` but clears its tile; the port keeps the position in `SavedPosition`
(the save writes it) and `Position` is the tile link, null once the unit is out, so a unit that is out and lies on the map has no tile. Its body items (and the unit it carries) name it.

**A stun before the game starts (S4).** The reference converts a unit before the game only through the state constructor's instant path (explosions, units already unconscious); a stunned standing unit's state waits until the battlescape runs, after the pre-battle inventory and the first turn's preparation. The port runs the state at the briefing's confirmation, so a unit stunned out at that point is unconscious before the inventory opens.

**A module destroyed in a block the saved module map lacks (S5).** `TileEngine::hit` and `detonate` decrement `getModuleMap()[x / 10][y / 10].second` of a base defense without a check, which indexes past
the vector when a restored save's map lacks the block (undefined behaviour). The port destroys the part and skips the count (`CountModuleLost`). ADR 0039 asked branches 5 to 7 to refuse such a block; for the destruction this
replaces that (AGENTS.md allows it for undefined behaviour), while the AI (branch 6) and the debriefing (branch 7) keep ADR 0039's rule.

**Explosions before the battlescape exists (S5).** `explodePowerSources` and `explodeOtherJunk` run in the generator, where a unit that blows itself up would `statePushNext` on a battlescape game that does not exist yet (a null dereference). The port
runs the pushed explosion with the battle's own list of states: into the empty list its `init` goes off at once (inside the ray loop that hit the bomber), and its `think` and the deaths wait for the end of the routine. The units
taken out are then ordinary deaths of that check, before the briefing's hidden one. A self-destruct item that is a melee weapon, which the port cannot run yet, fails the generation by name (`battle.self-destruct-item-unsupported`).

**The order of detonation (S5).** `explode` keeps the tiles it reached in a `std::map` keyed by tile pointer, so the detonation order is the allocation order of the tiles. The port detonates in tile index order. The animation's draws
(`RNG::generate` for each explosion sprite and the camera) are presentation and draw nothing.

**Tile links and the danger zone (S4).** A dropped primed grenade's danger zone (`setDangerZone`) is AI state that branch 6 ports; `ItemDrop` omits it.
